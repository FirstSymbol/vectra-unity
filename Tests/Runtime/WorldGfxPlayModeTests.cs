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

        [TearDown]
        public void TearDown()
        {
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
                done(Sample(recorders));
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
        public IEnumerator FiveHundredShapes_UrpOrHdrp_BatchBetterThanTheBuiltInShader()
        {
            var kind = VectraMaterials.CurrentWorldKind;
            if (kind == VectraWorldShaderKind.BuiltIn) Assert.Ignore("The Built-in pipeline has no SRP Batcher.");
            long[] now = null, legacy = null;
            yield return Measure(null, v => now = v);
            foreach (var o in m_Owned.ToArray())
                if (o is GameObject g && g.GetComponent<VectraRenderer>() != null) Object.Destroy(g);
            yield return null;
            yield return Measure(VectraWorldShaderKind.BuiltIn, v => legacy = v);
            Debug.Log($"Vectra world batching, 500 shapes ({kind}): [batches, draw calls, set-pass calls] new shader {string.Join("/", now)}, Built-in shader {string.Join("/", legacy)}");
            if (now.All(v => v == 0)) Assert.Ignore("Render statistics are not available in this player.");
            // The SRP Batcher counts a batch per run of draws with one shader variant; the CG shader cannot join one.
            Assert.LessOrEqual(now[2], legacy[2], "set-pass calls");
        }

        [UnityTest]
        public IEnumerator HairlineStroke_KeepsItsWidthWhenTheCameraZoomsOut()
        {
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
