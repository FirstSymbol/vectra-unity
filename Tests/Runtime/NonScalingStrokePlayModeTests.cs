using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FirstSymbol.Vectra.Svg;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FirstSymbol.Vectra.Tests
{
    /// <summary>
    /// vector-effect="non-scaling-stroke" in a real render: the stroke keeps its screen width while the image rect, the Canvas
    /// scale, the object scale and the camera change, and ordinary strokes scale. The numbers agree with headless Chrome
    /// (see CHANGELOG 0.6.1): a 4 px stroke is 4 px at every size.
    /// </summary>
    public class NonScalingStrokePlayModeTests
    {
        const int Size = 512;
        const string Ns = "vector-effect=\"non-scaling-stroke\"";
        readonly List<Object> m_Owned = new List<Object>();
        RenderTexture m_Target;
        Camera m_Camera;
        RenderPipelineAsset m_PreviousPipeline;
        bool m_PipelineChanged;

        static string Svg(string body) => "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 100 100\">" + body + "</svg>";

        static string VLine(string attrs) =>
            "<line x1=\"50\" y1=\"0\" x2=\"50\" y2=\"100\" stroke=\"#fff\" stroke-width=\"4\" " + attrs + "/>";

        static string HDash(string attrs) =>
            "<line x1=\"0\" y1=\"50\" x2=\"100\" y2=\"50\" stroke=\"#fff\" stroke-width=\"4\" stroke-dasharray=\"10 10\" " + attrs + "/>";

        [SetUp]
        public void SetUp()
        {
            foreach (var other in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
                if (other.CompareTag("MainCamera")) other.tag = "Untagged";
            m_Target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32);
            m_Owned.Add(m_Target);
            var go = new GameObject("Non-scaling test camera", typeof(Camera)) { tag = "MainCamera" };
            m_Owned.Add(go);
            m_Camera = go.GetComponent<Camera>();
            m_Camera.orthographic = true;
            m_Camera.orthographicSize = 5f; // 51.2 px per world unit
            m_Camera.transform.position = new Vector3(0f, 0f, -10f);
            m_Camera.clearFlags = CameraClearFlags.SolidColor;
            m_Camera.backgroundColor = Color.black;
            m_Camera.targetTexture = m_Target;
        }

        [TearDown]
        public void TearDown()
        {
            if (m_PipelineChanged) GraphicsSettings.defaultRenderPipeline = m_PreviousPipeline;
            m_PipelineChanged = false;
            foreach (var o in m_Owned)
                if (o != null) Object.Destroy(o);
            m_Owned.Clear();
        }

        // The test projects keep the Built-in pipeline by default even when URP is installed: use the installed one, put the old one back.
        IEnumerator UseInstalledPipeline()
        {
            if (GraphicsSettings.currentRenderPipeline != null) yield break;
            var urp = System.Type.GetType("UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset, Unity.RenderPipelines.Universal.Runtime");
            if (urp == null) yield break;
            var create = urp.GetMethods().First(m => m.Name == "Create" && m.GetParameters().Length == 1 && m.IsStatic);
            var asset = (RenderPipelineAsset)create.Invoke(null, new object[] { null });
            m_Owned.Add(asset);
            m_PreviousPipeline = GraphicsSettings.defaultRenderPipeline;
            m_PipelineChanged = true;
            GraphicsSettings.defaultRenderPipeline = asset;
            yield return null;
        }

        Texture2D Capture()
        {
            Canvas.ForceUpdateCanvases();
            m_Camera.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = m_Target;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            return tex;
        }

        // Pixels crossed by a bright vertical line on the middle row.
        static float Width(Texture2D t)
        {
            float sum = 0f;
            for (int x = 0; x < Size; x++) sum += t.GetPixel(x, Size / 2).r;
            return sum;
        }

        static int Dashes(Texture2D t)
        {
            int n = 0;
            bool on = false;
            for (int x = 0; x < Size; x++)
            {
                bool now = t.GetPixel(x, Size / 2).r > 0.5f;
                if (now && !on) n++;
                on = now;
            }

            return n;
        }

        // A world SVG whose rect is `pixels` screen pixels tall.
        VectraSvgRenderer World(string svg, float pixels)
        {
            var go = new GameObject("world svg");
            m_Owned.Add(go);
            var doc = SvgParser.Parse(svg);
            m_Owned.Add(doc);
            var r = go.AddComponent<VectraSvgRenderer>();
            r.Height = pixels / (Size / (2f * m_Camera.orthographicSize));
            r.Document = doc;
            return r;
        }

        static IEnumerator Settle()
        {
            for (int i = 0; i < 4; i++) yield return null;
        }

        void Measure(string name, Texture2D tex, ref float into)
        {
            into = Width(tex);
            Debug.Log("NSMEASURE " + name + " " + into.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
            var dir = System.Environment.GetEnvironmentVariable("VECTRA_SHOTS");
            if (!string.IsNullOrEmpty(dir))
            {
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, "unity_" + name + ".png"), tex.EncodeToPNG());
            }

            Object.Destroy(tex);
        }

        [UnityTest]
        public IEnumerator World_StrokeStaysFourPixels_AtEveryImageSize_WhileANormalStrokeScales()
        {
            yield return UseInstalledPipeline();
            foreach (int size in new[] { 50, 100, 200, 400 })
            {
                var fixedSvg = World(Svg(VLine(Ns)), size);
                yield return Settle();
                float w = 0f;
                Measure("world_ns_" + size, Capture(), ref w);
                Object.Destroy(fixedSvg.gameObject);
                var normalSvg = World(Svg(VLine("")), size);
                yield return Settle();
                float n = 0f;
                Measure("world_normal_" + size, Capture(), ref n);
                Object.Destroy(normalSvg.gameObject);
                yield return null;
                Assert.AreEqual(4f, w, 0.7f, "non-scaling at " + size + " px");
                Assert.AreEqual(4f * size / 100f, n, 0.7f, "normal at " + size + " px");
            }
        }

        [UnityTest]
        public IEnumerator World_StrokeKeepsItsWidthWhenTheCameraZoomsAndTheObjectScales()
        {
            yield return UseInstalledPipeline();
            var r = World(Svg(VLine(Ns)), 200);
            var normal = World(Svg(VLine("")), 200);
            normal.gameObject.SetActive(false);
            yield return Settle();
            float near = 0f;
            Measure("world_zoom_near", Capture(), ref near);

            m_Camera.orthographicSize = 20f; // four times further: the image is 50 px
            yield return Settle();
            float far = 0f;
            Measure("world_zoom_far", Capture(), ref far);

            m_Camera.orthographicSize = 5f;
            r.transform.localScale = new Vector3(2f, 2f, 1f); // the object is 400 px
            yield return Settle();
            float scaled = 0f;
            Measure("world_object_scale", Capture(), ref scaled);
            Assert.AreEqual(4f, near, 0.7f, "near");
            Assert.AreEqual(4f, far, 0.7f, "four times further away");
            Assert.AreEqual(4f, scaled, 0.7f, "object scaled by 2");

            r.gameObject.SetActive(false);
            normal.gameObject.SetActive(true);
            m_Camera.orthographicSize = 20f;
            yield return Settle();
            float normalFar = 0f;
            Measure("world_zoom_far_normal", Capture(), ref normalFar);
            Assert.AreEqual(2f, normalFar, 0.6f, "a normal stroke shrinks with the zoom (the image is 50 px: 4 x 50 / 100)");
        }

        [UnityTest]
        public IEnumerator World_DashesAreScreenLengths()
        {
            yield return UseInstalledPipeline();
            foreach (int size in new[] { 200, 400 })
            {
                var ns = World(Svg(HDash(Ns)), size);
                yield return Settle();
                var texNs = Capture();
                int dashesNs = Dashes(texNs);
                Object.Destroy(texNs);
                Object.Destroy(ns.gameObject);
                var normal = World(Svg(HDash("")), size);
                yield return Settle();
                var texNormal = Capture();
                int dashesNormal = Dashes(texNormal);
                Object.Destroy(texNormal);
                Object.Destroy(normal.gameObject);
                yield return null;
                Assert.AreEqual(size / 20, dashesNs, "20 px period at " + size);
                Assert.AreEqual(5, dashesNormal, "normal dashes scale with the image at " + size);
            }
        }

        [UnityTest]
        public IEnumerator World_GroupDoesNotInherit_UseOfAMarkedTargetDoes()
        {
            yield return UseInstalledPipeline();
            string group = Svg("<g " + Ns + ">" + VLine("") + "</g>");
            string use = Svg("<defs>" + VLine("id=\"l\" " + Ns) + "</defs><use href=\"#l\"/>");
            var a = World(group, 400);
            yield return Settle();
            float inGroup = 0f;
            Measure("world_group", Capture(), ref inGroup);
            Object.Destroy(a.gameObject);
            var b = World(use, 400);
            yield return Settle();
            float viaUse = 0f;
            Measure("world_use", Capture(), ref viaUse);
            Assert.AreEqual(16f, inGroup, 1f, "vector-effect is not inherited: a normal stroke");
            Assert.AreEqual(4f, viaUse, 0.7f, "a <use> of a marked line is non-scaling");
        }

        [UnityTest]
        public IEnumerator Canvas_StrokeIsFourPointsAtEveryScaleFactorAndObjectScale()
        {
            var canvasGo = new GameObject("Non-scaling canvas", typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
            m_Owned.Add(canvasGo);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = m_Camera;
            canvas.planeDistance = 5f;
            var scaler = canvasGo.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;

            VectraSvgImage Image(string svg)
            {
                // The image goes under the Canvas first: it asks the Canvas for the vertex channels when it is enabled.
                var go = new GameObject("svg", typeof(RectTransform));
                go.transform.SetParent(canvasGo.transform, false);
                ((RectTransform)go.transform).sizeDelta = new Vector2(200f, 200f);
                go.AddComponent<CanvasRenderer>();
                var image = go.AddComponent<VectraSvgImage>();
                var doc = SvgParser.Parse(svg);
                m_Owned.Add(doc);
                image.Document = doc;
                return image;
            }

            var ns = Image(Svg(VLine(Ns)));
            var normal = Image(Svg(VLine("")));
            normal.gameObject.SetActive(false);
            foreach (float factor in new[] { 1f, 2f })
            {
                scaler.scaleFactor = factor;
                yield return Settle();
                float w = 0f;
                Measure("canvas_ns_x" + factor, Capture(), ref w);
                Assert.AreEqual(4f * factor, w, 0.7f, "one SVG px is one reference point: 4 points at scale factor " + factor);
            }

            scaler.scaleFactor = 1f;
            ns.transform.localScale = new Vector3(2f, 2f, 1f); // 400 px on screen
            yield return Settle();
            float objectScaled = 0f;
            Measure("canvas_ns_object2", Capture(), ref objectScaled);
            Assert.AreEqual(4f, objectScaled, 0.7f, "object scaled by 2");

            ns.gameObject.SetActive(false);
            normal.gameObject.SetActive(true);
            foreach (float factor in new[] { 1f, 2f })
            {
                scaler.scaleFactor = factor;
                yield return Settle();
                float w = 0f;
                Measure("canvas_normal_x" + factor, Capture(), ref w);
                Assert.AreEqual(8f * factor, w, 0.9f, "a normal stroke of a 200-unit image: 8 units at scale factor " + factor);
            }
        }
    }
}
