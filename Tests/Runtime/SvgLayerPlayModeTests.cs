using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FirstSymbol.Vectra.Rendering;
using FirstSymbol.Vectra.Svg;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace FirstSymbol.Vectra.Tests
{
    /// <summary>
    /// Group opacity, masks, blend modes and background blur in pixels, in the rasterizer (sprites, PNG) and in the Canvas and world
    /// components. Every picture starts with a white or coloured rectangle so the results are opaque and the numbers can be computed by hand.
    /// </summary>
    public class SvgLayerPlayModeTests
    {
        const int Size = 160;
        readonly List<Object> m_Owned = new List<Object>();
        RenderTexture m_Target;
        Camera m_Camera;
        RenderPipelineAsset m_PreviousPipeline;
        bool m_PipelineChanged;
        GameObject m_LastCanvas;

        static string Svg(string body) =>
            "<svg xmlns='http://www.w3.org/2000/svg' width='160' height='160' viewBox='0 0 160 160'><rect width='160' height='160' fill='#fff'/>" + body + "</svg>";

        [SetUp]
        public void SetUp()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("needs a graphics device");
            foreach (var other in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
                if (other.CompareTag("MainCamera")) other.tag = "Untagged";
            m_Target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32);
            m_Owned.Add(m_Target);
            var go = new GameObject("Layer test camera", typeof(Camera)) { tag = "MainCamera" };
            m_Owned.Add(go);
            m_Camera = go.GetComponent<Camera>();
            m_Camera.orthographic = true;
            m_Camera.orthographicSize = 5f; // 16 px per world unit: a document 10 units tall fills the target
            m_Camera.transform.position = new Vector3(0f, 0f, -10f);
            m_Camera.clearFlags = CameraClearFlags.SolidColor;
            m_Camera.backgroundColor = Color.white;
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
            var urp = Type.GetType("UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset, Unity.RenderPipelines.Universal.Runtime");
            if (urp == null) yield break;
            var create = urp.GetMethods().First(m => m.Name == "Create" && m.GetParameters().Length == 1 && m.IsStatic);
            var asset = (RenderPipelineAsset)create.Invoke(null, new object[] { null });
            m_Owned.Add(asset);
            m_PreviousPipeline = GraphicsSettings.defaultRenderPipeline;
            m_PipelineChanged = true;
            GraphicsSettings.defaultRenderPipeline = asset;
            yield return null;
        }

        VectraSvgDocument Parse(string svg)
        {
            var doc = SvgParser.Parse(svg);
            m_Owned.Add(doc);
            return doc;
        }

        /// <summary>A picture read back; <c>[x, y]</c> counts from the top-left like the SVG.</summary>
        sealed class Shot
        {
            readonly Color32[] m_Pixels;
            readonly int m_W, m_H;

            public Shot(Texture2D t)
            {
                m_Pixels = t.GetPixels32();
                m_W = t.width;
                m_H = t.height;
                Object.DestroyImmediate(t);
            }

            public Color32 this[int x, int y] => m_Pixels[(m_H - 1 - y) * m_W + x];
        }

        static void AssertNear(Shot s, int x, int y, int r, int g, int b, int tol, string what)
        {
            var c = s[x, y];
            Assert.IsTrue(Math.Abs(c.r - r) <= tol && Math.Abs(c.g - g) <= tol && Math.Abs(c.b - b) <= tol,
                $"{what} at ({x},{y}): expected ({r},{g},{b}) +-{tol}, got ({c.r},{c.g},{c.b})");
        }

        Shot Raster(string svg) => new Shot(VectraRasterizer.Render(Parse(svg), Size, Size));

        Shot Capture()
        {
            Canvas.ForceUpdateCanvases();
            m_Camera.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = m_Target;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            return new Shot(tex);
        }

        VectraSvgImage CanvasImage(string svg)
        {
            if (m_LastCanvas != null) Object.DestroyImmediate(m_LastCanvas);
            var canvasGo = new GameObject("Layer test canvas", typeof(Canvas));
            m_LastCanvas = canvasGo;
            m_Owned.Add(canvasGo);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = m_Camera;
            canvas.planeDistance = 1f;
            var go = new GameObject("svg", typeof(RectTransform));
            go.transform.SetParent(canvasGo.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var image = go.AddComponent<VectraSvgImage>();
            image.raycastTarget = false;
            image.Document = Parse(svg);
            return image;
        }

        static IEnumerator Settle()
        {
            for (int i = 0; i < 4; i++) yield return null;
        }

        // Runs the same check on the rasterizer and on the Canvas component.
        IEnumerator OnBothPaths(string svg, Action<string, Shot> check)
        {
            check("rasterizer", Raster(svg));
            CanvasImage(svg);
            yield return Settle();
            check("canvas", Capture());
        }

        // ---- opacity ----

        const string TwoCircles =
            "<circle cx='60' cy='75' r='35' fill='#E63946'/><circle cx='100' cy='75' r='35' fill='#264653'/>";

        [UnityTest]
        public IEnumerator GroupOpacity_CompositesTheGroupAsOne()
        {
            yield return OnBothPaths(Svg("<g opacity='0.5'>" + TwoCircles + "</g>"), (path, s) =>
            {
                AssertNear(s, 40, 75, 243, 156, 163, 8, path + " left circle");
                // Where the circles overlap the top one hides the other, then the whole group is half transparent.
                AssertNear(s, 80, 75, 146, 162, 169, 8, path + " overlap");
                AssertNear(s, 120, 75, 146, 162, 169, 8, path + " right circle");
                AssertNear(s, 10, 10, 255, 255, 255, 2, path + " outside");
            });
        }

        // Shapes only in the top third: a layer that came out upside down or mirrored would show them at the bottom or on the other side.
        const string TopLeft = "<g opacity='0.5'><rect x='0' y='0' width='100' height='50' fill='#f00'/><rect x='20' y='0' width='60' height='50' fill='#00f'/></g>";

        static void AssertTopLeft(Shot s, string path)
        {
            AssertNear(s, 50, 25, 128, 128, 255, 6, path + " top, blue over red");
            AssertNear(s, 10, 25, 255, 128, 128, 6, path + " top left, red only");
            AssertNear(s, 130, 25, 255, 255, 255, 2, path + " top right");
            AssertNear(s, 50, 135, 255, 255, 255, 2, path + " bottom");
        }

        [UnityTest]
        public IEnumerator Layers_KeepTheirOrientation_OnEveryPath()
        {
            yield return OnBothPaths(Svg(TopLeft), (path, s) => AssertTopLeft(s, path));
            Object.DestroyImmediate(m_LastCanvas); // it would draw under the world picture
            World(Svg(TopLeft));
            yield return Settle();
            AssertTopLeft(Capture(), "world");
        }

        [UnityTest]
        public IEnumerator ShapeOpacity_WithFillAndStroke_CompositesFillAndStrokeAsOne()
        {
            yield return OnBothPaths(Svg("<circle opacity='0.5' cx='80' cy='80' r='42' fill='#E63946' stroke-width='16' stroke='#264653'/>"), (path, s) =>
            {
                AssertNear(s, 80, 80, 243, 156, 163, 8, path + " fill only");
                AssertNear(s, 118, 80, 146, 162, 169, 8, path + " fill under the stroke");
                AssertNear(s, 34, 80, 146, 162, 169, 8, path + " stroke only");
            });
        }

        [UnityTest]
        public IEnumerator NestedGroups_MultiplyTheirOpacity()
        {
            yield return OnBothPaths(Svg("<g opacity='0.5'><g opacity='0.5'>" + TwoCircles + "</g></g>"), (path, s) =>
            {
                // 0.25 of the dark circle over white.
                AssertNear(s, 120, 75, 200, 209, 212, 8, path);
            });
        }

        // ---- masks ----

        [UnityTest]
        public IEnumerator LuminanceMask_ShowsWhereTheMaskIsBright()
        {
            const string mask = "<mask id='m' maskUnits='userSpaceOnUse' x='0' y='0' width='160' height='160'><rect width='80' height='160' fill='#fff'/><rect x='80' width='80' height='160' fill='#000'/></mask>";
            yield return OnBothPaths(Svg(mask + "<rect width='160' height='160' fill='#f00' mask='url(#m)'/>"), (path, s) =>
            {
                AssertNear(s, 40, 80, 255, 0, 0, 4, path + " white part of the mask");
                AssertNear(s, 120, 80, 255, 255, 255, 4, path + " black part of the mask");
            });
        }

        [UnityTest]
        public IEnumerator LuminanceMask_OfGray_ShowsHalf()
        {
            const string mask = "<mask id='m'><rect width='160' height='160' fill='#808080'/></mask>";
            yield return OnBothPaths(Svg(mask + "<rect width='160' height='160' fill='#f00' mask='url(#m)'/>"), (path, s) =>
                AssertNear(s, 80, 80, 255, 127, 127, 6, path));
        }

        [UnityTest]
        public IEnumerator LuminanceMask_WithAGradient_FadesTheContent()
        {
            const string mask =
                "<defs><linearGradient id='g' x1='0' y1='0' x2='160' y2='0' gradientUnits='userSpaceOnUse'><stop stop-color='#fff'/><stop offset='1' stop-color='#000'/></linearGradient></defs>" +
                "<mask id='m'><rect width='160' height='160' fill='url(#g)'/></mask>";
            yield return OnBothPaths(Svg(mask + "<rect width='160' height='160' fill='#00f' mask='url(#m)'/>"), (path, s) =>
            {
                AssertNear(s, 4, 80, 8, 8, 255, 14, path + " left");
                AssertNear(s, 80, 80, 128, 128, 255, 10, path + " middle");
                AssertNear(s, 156, 80, 250, 250, 255, 14, path + " right");
            });
        }

        [UnityTest]
        public IEnumerator AlphaMask_UsesTheAlphaNotTheColour()
        {
            const string content = "<rect width='160' height='160' fill='#000' fill-opacity='0.5'/></mask>";
            const string use = "<rect width='160' height='160' fill='#f00' mask='url(#m)'/>";
            yield return OnBothPaths(Svg("<mask id='m' style='mask-type:alpha'>" + content + use), (path, s) =>
                AssertNear(s, 80, 80, 255, 127, 127, 6, path + " alpha mask: half"));
            yield return OnBothPaths(Svg("<mask id='m' mask-type='luminance'>" + content + use), (path, s) =>
                AssertNear(s, 80, 80, 255, 255, 255, 4, path + " luminance mask of black: nothing"));
        }

        [UnityTest]
        public IEnumerator Mask_RegionLimitsTheMask()
        {
            const string mask = "<mask id='m' maskUnits='userSpaceOnUse' x='0' y='0' width='80' height='160'><rect width='160' height='160' fill='#808080'/></mask>";
            yield return OnBothPaths(Svg(mask + "<rect width='160' height='160' fill='#f00' mask='url(#m)'/>"), (path, s) =>
            {
                AssertNear(s, 40, 80, 255, 127, 127, 6, path + " inside the region");
                AssertNear(s, 120, 80, 255, 255, 255, 4, path + " outside the region");
            });
        }

        [UnityTest]
        public IEnumerator WhiteShapeMask_StaysCrispAndExact()
        {
            const string mask = "<mask id='m' fill='#fff'><circle cx='80' cy='80' r='40'/></mask>";
            yield return OnBothPaths(Svg(mask + "<rect width='160' height='160' fill='#f00' mask='url(#m)'/>"), (path, s) =>
            {
                AssertNear(s, 80, 80, 255, 0, 0, 2, path + " inside");
                AssertNear(s, 10, 10, 255, 255, 255, 2, path + " outside");
            });
            Assert.AreEqual(0, Parse(Svg(mask + "<rect width='160' height='160' fill='#f00' mask='url(#m)'/>")).layers.Count, "no texture for a plain mask");
        }

        // ---- blend modes: backdrop (200,100,50), source (51,102,204) ----

        const string Backdrop = "<rect x='20' y='20' width='120' height='120' fill='#C86432'/>";

        string Blend(string mode) => Svg(Backdrop + "<rect x='40' y='40' width='80' height='80' fill='#3366CC' style='mix-blend-mode:" + mode + "'/>");

        void AssertBlend(Shot s, string path, int r, int g, int b)
        {
            AssertNear(s, 80, 80, r, g, b, 7, path + " blended");
            AssertNear(s, 30, 30, 200, 100, 50, 3, path + " backdrop untouched");
            AssertNear(s, 5, 5, 255, 255, 255, 2, path + " page untouched");
        }

        [UnityTest]
        public IEnumerator Multiply() { yield return OnBothPaths(Blend("multiply"), (p, s) => AssertBlend(s, p, 40, 40, 40)); }

        [UnityTest]
        public IEnumerator Screen() { yield return OnBothPaths(Blend("screen"), (p, s) => AssertBlend(s, p, 211, 162, 214)); }

        [UnityTest]
        public IEnumerator Overlay() { yield return OnBothPaths(Blend("overlay"), (p, s) => AssertBlend(s, p, 167, 80, 80)); }

        [UnityTest]
        public IEnumerator Darken() { yield return OnBothPaths(Blend("darken"), (p, s) => AssertBlend(s, p, 51, 100, 50)); }

        [UnityTest]
        public IEnumerator Difference() { yield return OnBothPaths(Blend("difference"), (p, s) => AssertBlend(s, p, 149, 2, 154)); }

        [UnityTest]
        public IEnumerator ColorBlend_KeepsTheBackdropLuminance() { yield return OnBothPaths(Blend("color"), (p, s) => AssertBlend(s, p, 78, 129, 231)); }

        [Test]
        public void Multiply_OnATransparentBackdrop_IsNormal()
        {
            const string svg = "<svg xmlns='http://www.w3.org/2000/svg' width='160' height='160' viewBox='0 0 160 160'><rect x='40' y='40' width='80' height='80' fill='#3366CC' style='mix-blend-mode:multiply'/></svg>";
            var tex = VectraRasterizer.Render(Parse(svg), Size, Size);
            var c = tex.GetPixel(80, 80);
            Object.DestroyImmediate(tex);
            Assert.AreEqual(51 / 255f, c.r, 0.02f);
            Assert.AreEqual(1f, c.a, 0.01f);
        }

        [UnityTest]
        public IEnumerator GroupBlend_BlendsTheGroupAsOne()
        {
            // The two circles overlap; the group is multiplied as a whole, so the overlap shows the top circle times the page, not a double multiply.
            yield return OnBothPaths(Svg(Backdrop + "<g style='mix-blend-mode:multiply'><rect x='40' y='40' width='80' height='80' fill='#3366CC'/><rect x='60' y='60' width='80' height='80' fill='#3366CC'/></g>"), (path, s) =>
                AssertNear(s, 80, 80, 40, 40, 40, 7, path + " overlap of the group"));
        }

        // ---- background blur ----

        [UnityTest]
        public IEnumerator BackgroundBlur_BlursTheContentBelow_OnlyInsideTheShape()
        {
            const string svg =
                "<rect width='80' height='160' fill='#000'/>" +
                "<rect data-figma-bg-blur-radius='40' x='40' y='40' width='80' height='80' fill='#fff' fill-opacity='0.01'/>";
            yield return OnBothPaths(Svg(svg), (path, s) =>
            {
                // Sigma 20: the edge of black and white at x = 80 is smoothed inside the shape and stays sharp outside it.
                AssertNear(s, 70, 80, 79, 79, 79, 30, path + " inside, left of the edge");
                AssertNear(s, 90, 80, 176, 176, 176, 30, path + " inside, right of the edge");
                AssertNear(s, 78, 20, 0, 0, 0, 6, path + " outside, left of the edge");
                AssertNear(s, 82, 20, 255, 255, 255, 6, path + " outside, right of the edge");
                AssertNear(s, 30, 80, 0, 0, 0, 6, path + " outside the shape, far left");
            });
        }

        // ---- the component ----

        [UnityTest]
        public IEnumerator Canvas_RedrawsTheTextureWhenTheSizeChanges_NotWhenOnlyTheColourDoes()
        {
            var image = CanvasImage(Svg("<g opacity='0.5'>" + TwoCircles + "</g>"));
            yield return Settle();
            var composite = image.GetComponentInChildren<VectraSvgComposite>();
            Assert.IsNotNull(composite, "the group is a composited part");
            Assert.AreEqual(Size, composite.Texture.width);
            int before = SvgCompositor.StepsRendered;
            image.color = new Color(1f, 0.5f, 0.5f, 1f);
            yield return Settle();
            Assert.AreEqual(before, SvgCompositor.StepsRendered, "a colour change tints the quad, it does not redraw the layer");
            var rect = (RectTransform)image.transform;
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            yield return Settle();
            Assert.Greater(SvgCompositor.StepsRendered, before, "a new size draws again");
            Assert.AreEqual(Size / 2, composite.Texture.width, 1);
        }

        [UnityTest]
        public IEnumerator Canvas_TextureIsFreed_WithTheComponent()
        {
            var image = CanvasImage(Svg("<g opacity='0.5'>" + TwoCircles + "</g>"));
            yield return Settle();
            var tex = image.GetComponentInChildren<VectraSvgComposite>().Texture;
            Assert.IsNotNull(tex);
            Object.Destroy(image.gameObject);
            yield return null;
            Assert.IsTrue(tex == null, "the render texture is destroyed with its component");
        }

        // ---- world space ----

        VectraSvgRenderer World(string svg)
        {
            var go = new GameObject("world svg");
            m_Owned.Add(go);
            var r = go.AddComponent<VectraSvgRenderer>();
            r.Height = 10f;
            r.Document = Parse(svg);
            return r;
        }

        [UnityTest]
        public IEnumerator World_GroupOpacity_BuiltInPipeline()
        {
            World(Svg("<g opacity='0.5'>" + TwoCircles + "</g>"));
            yield return Settle();
            var s = Capture();
            AssertNear(s, 80, 75, 146, 162, 169, 8, "overlap");
            AssertNear(s, 40, 75, 243, 156, 163, 8, "left circle");
            AssertNear(s, 10, 10, 255, 255, 255, 2, "outside");
        }

        [UnityTest]
        public IEnumerator World_GroupOpacity_UniversalPipeline()
        {
            yield return UseInstalledPipeline();
            World(Svg("<g opacity='0.5'>" + TwoCircles + "</g>"));
            yield return Settle();
            var s = Capture();
            AssertNear(s, 80, 75, 146, 162, 169, 8, "overlap");
            AssertNear(s, 10, 10, 255, 255, 255, 2, "outside");
        }

        [UnityTest]
        public IEnumerator World_Multiply_BlendsWithTheDocumentBelow()
        {
            World(Blend("multiply"));
            yield return Settle();
            var s = Capture();
            AssertBlend(s, "world", 40, 40, 40);
        }
    }
}
