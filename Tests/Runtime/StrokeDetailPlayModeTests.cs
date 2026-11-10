using System;
using System.Collections;
using System.Collections.Generic;
using FirstSymbol.Vectra.Geometry;
using FirstSymbol.Vectra.Rendering;
using FirstSymbol.Vectra.Svg;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FirstSymbol.Vectra.Tests
{
    /// <summary>Wide strokes are uniform, and the curves of meshes follow the densest view (a Scene view) like the pictures do.</summary>
    public class StrokeDetailPlayModeTests
    {
        const int Size = 160;
        readonly List<Object> m_Owned = new List<Object>();
        RenderTexture m_Target;
        Camera m_Camera;

        const string Ring = "<svg xmlns='http://www.w3.org/2000/svg' width='160' height='160' viewBox='0 0 160 160'><rect width='160' height='160' fill='#fff'/>" +
                            "<circle cx='80' cy='80' r='42' fill='none' stroke='#000' stroke-width='16'/></svg>";

        [SetUp]
        public void SetUp()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("needs a graphics device");
            foreach (var other in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
                if (other.CompareTag("MainCamera")) other.tag = "Untagged";
            m_Target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32);
            m_Owned.Add(m_Target);
            var go = new GameObject("Stroke test camera", typeof(Camera)) { tag = "MainCamera" };
            m_Owned.Add(go);
            m_Camera = go.GetComponent<Camera>();
            m_Camera.orthographic = true;
            m_Camera.orthographicSize = 5f;
            m_Camera.transform.position = new Vector3(0f, 0f, -10f);
            m_Camera.clearFlags = CameraClearFlags.SolidColor;
            m_Camera.backgroundColor = Color.white;
            m_Camera.targetTexture = m_Target;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in m_Owned)
                if (o != null) Object.Destroy(o);
            m_Owned.Clear();
        }

        static IEnumerator Settle()
        {
            // The density is looked at ten times a second.
            yield return new WaitForSecondsRealtime(0.35f);
            for (int i = 0; i < 4; i++) yield return null;
        }

        static int LightestOnRing(Color32[] px, int w, int h)
        {
            int worst = 0;
            for (int i = 0; i < 1440; i++)
            {
                float a = i * Mathf.PI * 2f / 1440f;
                for (float r = 38f; r <= 46f; r += 0.5f)
                {
                    int x = Mathf.RoundToInt(80f + Mathf.Cos(a) * r), y = Mathf.RoundToInt(80f + Mathf.Sin(a) * r);
                    worst = Math.Max(worst, px[(h - 1 - y) * w + x].r);
                }
            }

            return worst;
        }

        [Test]
        public void WideStroke_IsUniform_InTheRasterizer()
        {
            var doc = SvgParser.Parse(Ring);
            m_Owned.Add(doc);
            var tex = VectraRasterizer.Render(doc, Size, Size);
            int worst = LightestOnRing(tex.GetPixels32(), tex.width, tex.height);
            Object.DestroyImmediate(tex);
            Assert.LessOrEqual(worst, 2, "no lighter pixels across the stroke");
        }

        [UnityTest]
        public IEnumerator WideStroke_IsUniform_OnTheCanvas()
        {
            WorldCanvasImage();
            yield return Settle();
            Canvas.ForceUpdateCanvases();
            m_Camera.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = m_Target;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            RenderTexture.active = prev;
            int worst = LightestOnRing(tex.GetPixels32(), Size, Size);
            Object.DestroyImmediate(tex);
            Assert.LessOrEqual(worst, 2, "no lighter pixels across the stroke");
        }

        RectTransform WorldCanvas(string name)
        {
            var canvasGo = new GameObject(name, typeof(Canvas));
            m_Owned.Add(canvasGo);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = m_Camera;
            var crt = (RectTransform)canvasGo.transform;
            crt.sizeDelta = new Vector2(160f, 160f);
            crt.localScale = Vector3.one * 0.0625f;
            return crt;
        }

        VectraSvgImage WorldCanvasImage()
        {
            var go = new GameObject("svg", typeof(RectTransform));
            go.transform.SetParent(WorldCanvas("Stroke world canvas"), false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var image = go.AddComponent<VectraSvgImage>();
            image.raycastTarget = false;
            var doc = SvgParser.Parse(Ring);
            m_Owned.Add(doc);
            image.Document = doc;
            return image;
        }

        sealed class ExtraView : IDisposable
        {
            readonly Func<int> m_Count;
            readonly Func<int, Camera> m_Get;

            public ExtraView(Camera cam)
            {
                m_Count = VectraScreenDensity.ExtraCameraCount;
                m_Get = VectraScreenDensity.ExtraCamera;
                VectraScreenDensity.ExtraCameraCount = () => 1;
                VectraScreenDensity.ExtraCamera = _ => cam;
            }

            public void Dispose()
            {
                VectraScreenDensity.ExtraCameraCount = m_Count;
                VectraScreenDensity.ExtraCamera = m_Get;
            }
        }

        Camera ZoomedCamera(float factor)
        {
            var go = new GameObject("Scene view stand-in", typeof(Camera));
            m_Owned.Add(go);
            var extra = go.GetComponent<Camera>();
            extra.enabled = false;
            extra.orthographic = true;
            extra.orthographicSize = m_Camera.orthographicSize / factor;
            extra.transform.SetPositionAndRotation(m_Camera.transform.position, m_Camera.transform.rotation);
            extra.targetTexture = m_Target;
            return extra;
        }

        [UnityTest]
        public IEnumerator SvgMesh_CurvesFollowTheExtraCamera_AndNothingIsRebuiltWhenIdle()
        {
            var image = WorldCanvasImage();
            // Linear light makes a Linear project draw the document as a mesh; in a Gamma project it is a mesh anyway.
            image.ColorBlending = SvgColorBlendingChoice.Linear;
            yield return Settle();
            int before = image.Stats.vertices;
            Assert.Greater(before, 0);
            using (new ExtraView(ZoomedCamera(4f)))
            {
                yield return Settle();
                int zoomed = image.Stats.vertices;
                Assert.Greater(zoomed, before * 1.3f, "the outline has more segments for a view four times closer");
                var mesh = image.canvasRenderer.GetMesh();
                for (int i = 0; i < 20; i++) yield return null;
                Assert.AreEqual(zoomed, image.Stats.vertices, "idle frames change nothing");
                Assert.AreSame(mesh, image.canvasRenderer.GetMesh());
            }

            yield return Settle();
            Assert.AreEqual(before, image.Stats.vertices, "and back when that view is gone");
        }

        [UnityTest]
        public IEnumerator NativeShape_CurvesFollowTheExtraCamera()
        {
            var go = new GameObject("shape", typeof(RectTransform));
            go.transform.SetParent(WorldCanvas("Native world canvas"), false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(120f, 120f);
            var shape = go.AddComponent<VectraShape>();
            shape.Geometry = new RectangleShape { cornerRadii = new Vector4(40f, 40f, 40f, 40f) };
            shape.Strokes.Add(new StrokeLayer(Color.black, 16f) { join = LineJoin.Round, trimEnd = 0.96f });
            shape.SetAllDirty();
            yield return Settle();
            int before = shape.Stats.vertices;
            Assert.Greater(before, 0);
            using (new ExtraView(ZoomedCamera(4f)))
            {
                yield return Settle();
                Assert.Greater(shape.Stats.vertices, before * 1.3f, "rounded corners have more segments for a view four times closer");
            }

            yield return Settle();
            Assert.AreEqual(before, shape.Stats.vertices, "and back when that view is gone");
        }
    }
}
