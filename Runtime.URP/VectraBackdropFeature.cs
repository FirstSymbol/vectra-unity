using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace FirstSymbol.Vectra.URP
{
    /// <summary>
    /// URP renderer feature that publishes the camera image for Vectra backdrop effects: a sharp copy
    /// (<c>_VectraBackdropSharp</c>, used by advanced blend modes) and a blurred pyramid result
    /// (<c>_VectraBackdropBlur</c>, used by frosted glass). Computed once per camera and shared by every shape.
    /// Add it to the Universal Renderer asset.
    /// </summary>
    public class VectraBackdropFeature : ScriptableRendererFeature
    {
        [Serializable]
        public class Settings
        {
            public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            [Range(1, 4)] public int downsample = 2;
            [Range(1, 6)] public int blurIterations = 4;
            [Tooltip("Skip the sharp copy when no shape uses advanced blend modes.")]
            public bool sharpCopy = true;
        }

        public Settings settings = new Settings();

        Material m_Material;
        BackdropPass m_Pass;

        public static readonly int BlurId = Shader.PropertyToID("_VectraBackdropBlur");
        public static readonly int SharpId = Shader.PropertyToID("_VectraBackdropSharp");

        public override void Create()
        {
            m_Pass = new BackdropPass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (m_Material == null)
            {
                var shader = Resources.Load<Shader>("VectraURP/VectraBackdropBlur") ?? Shader.Find("Hidden/Vectra/BackdropBlur");
                if (shader == null) return;
                m_Material = CoreUtils.CreateEngineMaterial(shader);
            }

            var cam = renderingData.cameraData.camera;
            if (cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection) return;
            m_Pass.renderPassEvent = settings.renderPassEvent;
            m_Pass.Setup(m_Material, settings);
            renderer.EnqueuePass(m_Pass);
        }

        protected override void Dispose(bool disposing) => CoreUtils.Destroy(m_Material);

        class BackdropPass : ScriptableRenderPass
        {
            Material m_Material;
            Settings m_Settings;

            class PassData { }

            public void Setup(Material material, Settings settings)
            {
                m_Material = material;
                m_Settings = settings;
                requiresIntermediateTexture = true;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer) return;
                var source = resources.activeColorTexture;
                var desc = renderGraph.GetTextureDesc(source);
                desc.msaaSamples = MSAASamples.None;
                desc.depthBufferBits = DepthBits.None;
                desc.clearBuffer = false;

                desc.name = "_VectraBackdropSharp";
                var sharp = renderGraph.CreateTexture(desc);
                renderGraph.AddBlitPass(source, sharp, Vector2.one, Vector2.zero, passName: "Vectra Backdrop Copy");

                var small = desc;
                small.width = Mathf.Max(1, desc.width >> m_Settings.downsample);
                small.height = Mathf.Max(1, desc.height >> m_Settings.downsample);
                small.name = "_VectraBackdropBlurA";
                var a = renderGraph.CreateTexture(small);
                small.name = "_VectraBackdropBlurB";
                var b = renderGraph.CreateTexture(small);

                renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(sharp, a, m_Material, 0), "Vectra Backdrop Downsample");
                // Kawase blur: each pass samples further away, ping-ponging between two textures.
                for (int i = 0; i < m_Settings.blurIterations; i++)
                {
                    int pass = 1 + Mathf.Min(i, 3);
                    renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(a, b, m_Material, pass), "Vectra Backdrop Blur");
                    (a, b) = (b, a);
                }

                using var builder = renderGraph.AddUnsafePass<PassData>("Vectra Backdrop Publish", out _);
                builder.UseTexture(a);
                builder.SetGlobalTextureAfterPass(a, BlurId);
                if (m_Settings.sharpCopy)
                {
                    builder.UseTexture(sharp);
                    builder.SetGlobalTextureAfterPass(sharp, SharpId);
                }

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc((PassData _, UnsafeGraphContext __) => { });
            }
        }
    }
}
