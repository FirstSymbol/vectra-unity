using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FirstSymbol.Vectra.Rendering;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FirstSymbol.Vectra.Tests
{
    /// <summary>World shapes: the SRP Batcher path and hairline strokes in a real render.</summary>
    public class WorldGfxPlayModeTests
    {
        const int Count = 500;
        readonly List<Object> m_Owned = new List<Object>();
        RenderTexture m_Target;
        Camera m_Camera;

        [SetUp]
        public void SetUp()
        {
            foreach (var other in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
                if (other.CompareTag("MainCamera")) other.tag = "Untagged";
            m_Target = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32);
            m_Owned.Add(m_Target);
            var go = new GameObject("Vectra World Test Camera", typeof(Camera)) { tag = "MainCamera" };
            m_Owned.Add(go);
            m_Camera = go.GetComponent<Camera>();
            m_Camera.orthographic = true;
            m_Camera.orthographicSize = 5f;
            m_Camera.transform.position = new Vector3(0f, 0f, -10f);
            m_Camera.clearFlags = CameraClearFlags.SolidColor;
            m_Camera.backgroundColor = Color.black;
            m_Camera.targetTexture = m_Target;
        }

        RenderPipelineAsset m_PreviousPipeline;
        bool m_PipelineChanged;

        /// <summary>
        /// The test projects keep the Built-in pipeline by default even when URP or HDRP is installed: use the installed one for this test
        /// (URP first), and put the old setting back afterwards.
        /// </summary>
        IEnumerator UseInstalledPipeline()
        {
            if (GraphicsSettings.currentRenderPipeline != null) yield break;
            RenderPipelineAsset asset = null;
            var urp = System.Type.GetType("UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset, Unity.RenderPipelines.Universal.Runtime");
            var hdrp = System.Type.GetType("UnityEngine.Rendering.HighDefinition.HDRenderPipelineAsset, Unity.RenderPipelines.HighDefinition.Runtime");
            if (urp != null)
            {
                var create = urp.GetMethods().First(m => m.Name == "Create" && m.GetParameters().Length == 1 && m.IsStatic);
                asset = (RenderPipelineAsset)create.Invoke(null, new object[] { null });
            }
            else if (hdrp != null) asset = (RenderPipelineAsset)ScriptableObject.CreateInstance(hdrp);

            if (asset == null) yield break;
            m_Owned.Add(asset);
            m_PreviousPipeline = GraphicsSettings.defaultRenderPipeline;
            m_PipelineChanged = true;
            GraphicsSettings.defaultRenderPipeline = asset;
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            if (m_PipelineChanged) GraphicsSettings.defaultRenderPipeline = m_PreviousPipeline;
            m_PipelineChanged = false;
            VectraMaterials.ForcedWorldKind = null;
            foreach (var o in m_Owned)
                if (o != null) Object.Destroy(o);
            m_Owned.Clear();
        }

        void Spawn()
        {
            for (int i = 0; i < Count; i++)
            {
                var go = new GameObject("Vectra World " + i, typeof(MeshFilter), typeof(MeshRenderer), typeof(VectraRenderer));
                m_Owned.Add(go);
                go.transform.position = new Vector3(-4.6f + (i % 25) * 0.38f, -3.8f + (i / 25) * 0.4f, 0f);
                var r = go.GetComponent<VectraRenderer>();
                r.Size = new Vector2(0.3f, 0.3f);
                if (i % 3 == 0) r.SetGeometry<StarShape>();
                else if (i % 3 == 1) r.SetGeometry<EllipseShape>();
                r.Fills.Clear();
                r.Fills.Add(new FillLayer(VectraPaint.Solid(Color.HSVToRGB((i % 25) / 25f, 0.8f, 1f))));
                r.Strokes.Add(new StrokeLayer(Color.white, 1f) { widthMode = StrokeWidthMode.Pixels });
                r.SetShapeDirty();
            }
        }

        static long[] Sample(ProfilerRecorder[] recorders)
        {
            var v = new long[recorders.Length];
            for (int r = 0; r < recorders.Length; r++) v[r] = recorders[r].Valid ? recorders[r].LastValue : 0;
            return v;
        }

        IEnumerator Measure(VectraWorldShaderKind? kind, System.Action<long[]> done)
        {
            VectraMaterials.ForcedWorldKind = kind;
            var names = new[] { "Batches Count", "Draw Calls Count", "SetPass Calls Count" };
            var recorders = new ProfilerRecorder[names.Length];
            for (int r = 0; r < names.Length; r++) recorders[r] = ProfilerRecorder.StartNew(ProfilerCategory.Render, names[r]);
            try
            {
                Spawn();
                for (int f = 0; f < 4; f++) yield return null;
                m_Camera.Render();
                yield return null;
                var v = Sample(recorders);
#if UNITY_EDITOR
                // The editor's own statistics count SRP Batcher batches; the profiler counters above do not in every version.
                v = v.Concat(new long[] { UnityEditor.UnityStats.drawCalls, UnityEditor.UnityStats.setPassCalls }).ToArray();
#endif
                done(v);
            }
            finally
            {
                foreach (var r in recorders) r.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator FiveHundredShapes_ShareOneMaterialAndNoPropertyBlocks()
        {
            Spawn();
            yield return null;
            var materials = new HashSet<Material>();
            foreach (var r in Object.FindObjectsByType<VectraRenderer>())
            {
                var mr = r.GetComponent<MeshRenderer>();
                materials.Add(mr.sharedMaterial);
                Assert.IsFalse(mr.HasPropertyBlock(), "no MaterialPropertyBlock on a world shape");
            }

            Assert.AreEqual(1, materials.Count, "all 500 shapes use one material");
        }

        [UnityTest]
        public IEnumerator FiveHundredShapes_UrpOrHdrp_CostNoMoreThanTheBuiltInShader()
        {
            yield return UseInstalledPipeline();
            var kind = VectraMaterials.CurrentWorldKind;
            if (kind == VectraWorldShaderKind.BuiltIn) Assert.Ignore("The Built-in pipeline has no SRP Batcher.");
            long[] now = null, legacy = null;
            yield return Measure(null, v => now = v);
            foreach (var o in m_Owned.ToArray())
                if (o is GameObject g && g.GetComponent<VectraRenderer>() != null) Object.Destroy(g);
            yield return null;
            yield return Measure(VectraWorldShaderKind.BuiltIn, v => legacy = v);
            Debug.Log($"Vectra world batching, 500 shapes ({kind}): [profiler batches, draw calls, set-pass calls; editor stats draw calls, set-pass calls] new shader {string.Join("/", now)}, Built-in shader {string.Join("/", legacy)}");
            if (now.All(v => v == 0)) Assert.Ignore("Render statistics are not available in this player.");
            // The SRP Batcher counts a batch per run of draws with one shader variant; the CG shader cannot join one.
            Assert.LessOrEqual(now[2], legacy[2], "set-pass calls");
        }

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator ShaderGraphSample_DrawsARoundedCard()
        {
            yield return UseInstalledPipeline();
            if (VectraMaterials.CurrentWorldKind == VectraWorldShaderKind.BuiltIn) Assert.Ignore("Shader Graph needs URP or HDRP in this project.");
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(VectraRenderer).Assembly);
            if (info == null) Assert.Ignore("Vectra is a folder under Assets in this project: the sample is read from the package folder.");
            const string dir = "Assets/VectraSgTest";
            UnityEditor.AssetDatabase.DeleteAsset(dir);
            UnityEditor.AssetDatabase.CreateFolder("Assets", "VectraSgTest");
            string target = dir + "/Vectra Card.shadergraph";
            System.IO.File.Copy(System.IO.Path.Combine(info.resolvedPath, "Samples~", "ShaderGraph", "Vectra Card.shadergraph"), target);
            UnityEditor.AssetDatabase.ImportAsset(target, UnityEditor.ImportAssetOptions.ForceSynchronousImport);
            try
            {
                var shader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>(target);
                Assert.NotNull(shader, "the sample graph imports as a shader");
                var errors = new List<string>();
                foreach (var m in UnityEditor.ShaderUtil.GetShaderMessages(shader))
                    if (m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) errors.Add(m.message + " (" + m.file + ":" + m.line + ")");
                Assert.IsEmpty(errors, "shader errors: " + string.Join("; ", errors));

                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                m_Owned.Add(quad);
                Object.Destroy(quad.GetComponent<Collider>());
                quad.transform.localScale = new Vector3(8f, 8f, 1f);
                var material = new Material(shader);
                m_Owned.Add(material);
                quad.GetComponent<MeshRenderer>().sharedMaterial = material;
                yield return null;
                m_Camera.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = m_Target;
                var tex = new Texture2D(512, 512, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
                RenderTexture.active = prev;
                var centre = tex.GetPixel(256, 256);
                var corner = tex.GetPixel(256 - 200, 256 + 200);
                Object.Destroy(tex);
                Assert.Greater(centre.maxColorComponent, 0.2f, "the card is painted in the middle");
                Assert.Less(corner.maxColorComponent, 0.05f, "outside the rounded corner stays clear");
            }
            finally
            {
                UnityEditor.AssetDatabase.DeleteAsset(dir);
            }
        }
#endif

        [UnityTest]
        public IEnumerator HairlineStroke_KeepsItsWidthWhenTheCameraZoomsOut()
        {
            yield return UseInstalledPipeline();
            var go = new GameObject("Vectra Hairline", typeof(MeshFilter), typeof(MeshRenderer), typeof(VectraRenderer));
            m_Owned.Add(go);
            var r = go.GetComponent<VectraRenderer>();
            r.SetGeometry<RectangleShape>();
            ((RectangleShape)r.Content.geometry).cornerRadii = Vector4.zero;
            r.Size = new Vector2(6f, 6f);
            r.Fills.Clear();
            r.Strokes.Clear();
            r.Strokes.Add(new StrokeLayer(Color.white, 2f) { widthMode = StrokeWidthMode.Pixels });
            yield return null;

            float first = StrokeThickness();
            m_Camera.orthographicSize = 20f; // four times further out: the frame is 4x smaller on screen
            yield return null;
            yield return null;
            float second = StrokeThickness();
            Assert.Greater(first, 1.2f, "pixels across the stroke, near");
            Assert.AreEqual(first, second, 0.5f, "a hairline is as thick far away as near");

            float StrokeThickness()
            {
                m_Camera.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = m_Target;
                var tex = new Texture2D(512, 512, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
                RenderTexture.active = prev;
                // Walk the middle column from the top: the first bright run is the top side of the frame.
                float sum = 0f;
                bool started = false;
                for (int y = 511; y >= 256; y--)
                {
                    float v = tex.GetPixel(256, y).r;
                    if (v > 0.02f) { started = true; sum += v; }
                    else if (started) break;
                }

                Object.Destroy(tex);
                return sum;
            }
        }
    }
}
