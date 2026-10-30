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
    /// (<c>_VectraBackdropBlur</c>, used by frosted glass). Computed once per camera, before transparent
    /// objects (and therefore Screen Space - Camera canvases) are drawn, and shared by every shape.
    /// To blur other UI, draw that UI with a base camera and the glass with an overlay camera of its stack:
    /// the overlay camera's capture then contains the finished background. Add it to the Universal Renderer asset.
    /// </summary>
    public class VectraBackdropFeature : ScriptableRendererFeature
    {
        [Serializable]
        public class Settings
        {
            [Tooltip("When the camera image is captured. Shapes drawn before this point are part of the backdrop; " +
                     "shapes using the backdrop must be drawn after it.")]
            public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
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
            m_Pass?.Dispose();
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

        protected override void Dispose(bool disposing)
        {
            m_Pass?.Dispose();
            CoreUtils.Destroy(m_Material);
        }

        class BackdropPass : ScriptableRenderPass
        {
            Material m_Material;
            Settings m_Settings;
            // Persistent targets: the globals stay valid for every pass that runs after the capture.
            RTHandle m_Sharp, m_BlurA, m_BlurB;

            class PassData { }

            public void Setup(Material material, Settings settings)
            {
                m_Material = material;
                m_Settings = settings;
                requiresIntermediateTexture = true;
            }

            public void Dispose()
            {
                m_Sharp?.Release();
                m_BlurA?.Release();
                m_BlurB?.Release();
                m_Sharp = m_BlurA = m_BlurB = null;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer) return;
                var cameraData = frameData.Get<UniversalCameraData>();
                var desc = cameraData.cameraTargetDescriptor;
                desc.msaaSamples = 1;
                desc.depthBufferBits = 0;
                desc.depthStencilFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.None;
                RenderingUtils.ReAllocateHandleIfNeeded(ref m_Sharp, desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_VectraBackdropSharp");
                var small = desc;
                small.width = Mathf.Max(1, desc.width >> m_Settings.downsample);
                small.height = Mathf.Max(1, desc.height >> m_Settings.downsample);
                RenderingUtils.ReAllocateHandleIfNeeded(ref m_BlurA, small, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_VectraBackdropBlurA");
                RenderingUtils.ReAllocateHandleIfNeeded(ref m_BlurB, small, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_VectraBackdropBlurB");

                var source = resources.activeColorTexture;
                var sharp = renderGraph.ImportTexture(m_Sharp);
                var a = renderGraph.ImportTexture(m_BlurA);
                var b = renderGraph.ImportTexture(m_BlurB);
                renderGraph.AddBlitPass(source, sharp, Vector2.one, Vector2.zero, passName: "Vectra Backdrop Copy");
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