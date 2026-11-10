using System.Collections.Generic;
using System.IO;
using System.Linq;
using FirstSymbol.Vectra.Rendering;
using FirstSymbol.Vectra.Svg;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace FirstSymbol.Vectra.Editor.Tests
{
    /// <summary>
    /// Filters on elements that paint more than one thing are computed from the picture of the whole subtree (a layer); a single simple shape
    /// keeps its native effects. Shadow blend modes composite against the backdrop. Pixel tests use the rasterizer.
    /// </summary>
    public class SvgFilterLayerTests
    {
        readonly List<Object> m_Owned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in m_Owned)
                if (o != null) Object.DestroyImmediate(o);
            m_Owned.Clear();
        }

        static string Doc(string body, string bg = "#fff") =>
            "<svg xmlns='http://www.w3.org/2000/svg' width='160' height='160' viewBox='0 0 160 160'><rect width='160' height='160' fill='" + bg + "'/>" + body + "</svg>";

        // Figma's drop shadow chain: offset (4, 8), blur 6, the given colour (black 45 % by default), closing blend with the given mode.
        static string ShadowFilter(string mode = "normal", string rgba = "0 0 0 0.45")
        {
            var c = rgba.Split(' ');
            return "<defs><filter id='f' x='0' y='0' width='160' height='160' filterUnits='userSpaceOnUse' color-interpolation-filters='sRGB'>" +
            "<feFlood flood-opacity='0' result='BackgroundImageFix'/>" +
            "<feColorMatrix in='SourceAlpha' type='matrix' values='0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 127 0' result='hardAlpha'/>" +
            "<feOffset dx='4' dy='8'/><feGaussianBlur stdDeviation='6'/>" +
            "<feColorMatrix type='matrix' values='0 0 0 0 " + c[0] + " 0 0 0 0 " + c[1] + " 0 0 0 0 " + c[2] + " 0 0 0 " + c[3] + " 0'/>" +
            "<feBlend mode='" + mode + "' in2='BackgroundImageFix' result='effect1'/>" +
            "<feBlend mode='normal' in='SourceGraphic' in2='effect1' result='shape'/></filter></defs>";
        }

        const string CircleAndSquare = "<circle cx='55' cy='75' r='30' fill='#E63946'/><rect x='75' y='45' width='60' height='60' fill='#2A9D8F'/>";

        VectraSvgDocument Parse(string svg)
        {
            var doc = SvgParser.Parse(svg);
            m_Owned.Add(doc);
            return doc;
        }

        // ---- what becomes a layer ----

        [Test]
        public void ShadowOnAGroup_IsOneFilterOfALayer_AndNoShapeHasAShadowOfItsOwn()
        {
            var doc = Parse(Doc(ShadowFilter() + "<g filter='url(#f)'>" + CircleAndSquare + "</g>"));
            Assert.AreEqual(1, doc.layers.Count);
            var l = doc.layers[0];
            Assert.AreEqual(2, l.to - l.from);
            Assert.IsNotNull(l.filter);
            Assert.AreEqual(new[] { SvgFilterKind.DropShadow, SvgFilterKind.Source }, l.filter.Select(s => s.kind).ToArray());
            Assert.AreEqual(8f, l.filter[0].dy, 1e-4f);
            Assert.AreEqual(6f * 5f / 6f, l.filter[0].sigma, 1e-4f);
            foreach (var e in doc.elements) Assert.AreEqual(0, e.effects.Count, "the shadow comes from the picture of the group, not from a shape");
            Assert.IsFalse(l.NeedsBackdrop, "a normal shadow does not read the backdrop");
        }

        [Test]
        public void ShadowOnASingleSimpleShape_StaysANativeEffect()
        {
            var doc = Parse(Doc(ShadowFilter() + "<g filter='url(#f)'><rect x='40' y='40' width='60' height='60' fill='#36c'/></g>"));
            Assert.AreEqual(0, doc.layers.Count);
            var fx = doc.elements[1].effects;
            Assert.AreEqual(1, fx.Count);
            Assert.IsInstanceOf<DropShadowEffect>(fx[0]);
        }

        [Test]
        public void ShadowOnAShapeWithFillAndStroke_IsALayer_BecauseTheShadowIsOfBoth()
        {
            var doc = Parse(Doc(ShadowFilter() + "<rect x='40' y='40' width='60' height='60' fill='#36c' stroke='#000' stroke-width='10' filter='url(#f)'/>"));
            Assert.AreEqual(1, doc.layers.Count);
            Assert.AreEqual(0, doc.elements[1].effects.Count);
        }

        [Test]
        public void ShadowOnAUseOfAGroup_IsALayer()
        {
            var doc = Parse(Doc(ShadowFilter() + "<defs><g id='g'>" + CircleAndSquare + "</g></defs><use href='#g' filter='url(#f)'/>"));
            Assert.AreEqual(1, doc.layers.Count);
            Assert.AreEqual(SvgFilterKind.DropShadow, doc.layers[0].filter[0].kind);
        }

        [Test]
        public void LayerBlurOnAGroup_IsBlurOfThePictureOfTheGroup()
        {
            var svg = Doc("<defs><filter id='b' x='0' y='0' width='160' height='160' filterUnits='userSpaceOnUse'><feFlood flood-opacity='0' result='BackgroundImageFix'/>" +
                          "<feBlend mode='normal' in='SourceGraphic' in2='BackgroundImageFix' result='shape'/><feGaussianBlur stdDeviation='3' result='fx'/></filter></defs>" +
                          "<g filter='url(#b)'>" + CircleAndSquare + "</g>");
            var doc = Parse(svg);
            Assert.AreEqual(1, doc.layers.Count);
            Assert.AreEqual(new[] { SvgFilterKind.Source, SvgFilterKind.Blur }, doc.layers[0].filter.Select(s => s.kind).ToArray());
            Assert.AreEqual(3f * 5f / 6f, doc.layers[0].filter[1].sigma, 1e-4f);
        }

        [Test]
        public void ClassicDropShadowOnAGroup_IsALayerToo()
        {
            var svg = Doc("<defs><filter id='d'><feDropShadow dx='3' dy='5' stdDeviation='2' flood-color='#000' flood-opacity='0.5'/></filter></defs><g filter='url(#d)'>" + CircleAndSquare + "</g>");
            var doc = Parse(svg);
            Assert.AreEqual(1, doc.layers.Count);
            var st = doc.layers[0].filter;
            Assert.AreEqual(SvgFilterKind.DropShadow, st[0].kind);
            Assert.AreEqual(2f, st[0].sigma, 1e-4f, "other generators write the blur as it is: no Figma factor");
        }

        [Test]
        public void ShadowBlendMode_IsKept_AndTheShapeIsALayer_NotReportedAsNormal()
        {
            foreach (var mode in new[] { "multiply", "screen", "overlay", "color-dodge", "plus-lighter" })
            {
                var doc = Parse(Doc(ShadowFilter(mode) + "<g filter='url(#f)'><rect x='40' y='40' width='60' height='60' fill='#fff'/></g>"));
                Assert.AreEqual(1, doc.layers.Count, mode);
                var step = doc.layers[0].filter[0];
                Assert.AreEqual(SvgLayers.ParseBlend(mode), step.blend, mode);
                Assert.IsTrue(doc.layers[0].NeedsBackdrop, mode + ": the shadow blends with what is below");
                Assert.IsFalse(doc.warnings.Any(w => w.Contains("blend mode")), string.Join(" | ", doc.warnings));
            }
        }

        [Test]
        public void InnerShadowWithScreen_GoesThroughTheCompositor_WithoutNeedingTheBackdrop()
        {
            var doc = Parse(Doc(
                "<defs><filter id='i' x='0' y='0' width='160' height='160' filterUnits='userSpaceOnUse'><feFlood flood-opacity='0' result='BackgroundImageFix'/>" +
                "<feBlend mode='normal' in='SourceGraphic' in2='BackgroundImageFix' result='shape'/>" +
                "<feColorMatrix in='SourceAlpha' type='matrix' values='0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 127 0' result='hardAlpha'/>" +
                "<feOffset dy='6'/><feGaussianBlur stdDeviation='6'/><feComposite in2='hardAlpha' operator='arithmetic' k2='-1' k3='1'/>" +
                "<feColorMatrix type='matrix' values='0 0 0 0 0.95 0 0 0 0 0.6 0 0 0 0 0.4 0 0 0 0.9 0'/>" +
                "<feBlend mode='screen' in2='shape' result='e1'/></filter></defs>" +
                "<rect x='30' y='30' width='100' height='100' rx='20' fill='#457B9D' filter='url(#i)'/>", "#1D3557"));
            Assert.AreEqual(1, doc.layers.Count);
            var steps = doc.layers[0].filter;
            Assert.AreEqual(new[] { SvgFilterKind.Source, SvgFilterKind.InnerShadow }, steps.Select(s => s.kind).ToArray());
            Assert.AreEqual(SvgBlendMode.Screen, steps[1].blend);
            Assert.IsFalse(doc.layers[0].NeedsBackdrop);
        }

        [Test]
        public void AShadowInsideAMask_StaysNativeOnEachShape_WithTheOldReport()
        {
            var doc = Parse(Doc(ShadowFilter("multiply") +
                "<mask id='m'><g filter='url(#f)'>" + CircleAndSquare.Replace("#E63946", "#fff").Replace("#2A9D8F", "#fff") + "</g></mask><rect width='160' height='160' fill='#36c' mask='url(#m)'/>"));
            Assert.IsTrue(doc.warnings.Any(w => w.Contains("multiply")), string.Join(" | ", doc.warnings));
        }

        [Test]
        public void ExpandIntoObjects_GivesEveryShapeOfAFilteredGroupItsShadow()
        {
            var doc = Parse(Doc(ShadowFilter() + "<g filter='url(#f)'>" + CircleAndSquare + "</g>"));
            var canvasGo = new GameObject("Expand canvas", typeof(Canvas));
            m_Owned.Add(canvasGo);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var go = new GameObject("Svg", typeof(RectTransform));
            go.transform.SetParent(canvasGo.transform, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(160f, 160f);
            var img = go.AddComponent<VectraSvgImage>();
            img.Document = doc;
            var root = VectraSvg.Expand(img);
            m_Owned.Add(root.gameObject);
            var shadows = root.GetComponentsInChildren<VectraShape>().Select(s => s.Effects.OfType<DropShadowEffect>().Count()).ToArray();
            Assert.AreEqual(1, shadows.Count(n => n == 0), "the background has none");
            Assert.AreEqual(2, shadows.Count(n => n == 1), "the circle and the square have one each, as before layers");
        }

        [Test]
        public void ShadowOnAGlyphLikePathOrASharpStar_IsALayer_ARectangleOrCircleStaysNative()
        {
            string Svg(string shape) => Doc(ShadowFilter() + "<g filter='url(#f)'>" + shape + "</g>");
            string glyphs = "<path d='M10 10H20V30H10Z M30 10H40V30H30Z' fill='#000'/>";
            string star = "<path d='M80 30L91.2 64.5H127.5L98.2 85.9L109.4 120.5L80 99.1L50.6 120.5L61.8 85.9L32.4 64.5H68.8L80 30Z' fill='#000'/>";
            Assert.AreEqual(1, Parse(Svg(glyphs)).layers.Count, "two contours: text outlines and icons");
            Assert.AreEqual(1, Parse(Svg(star)).layers.Count, "tips sharper than 60 degrees");
            Assert.AreEqual(0, Parse(Svg("<rect x='10' y='10' width='50' height='50' fill='#000'/>")).layers.Count);
            Assert.AreEqual(0, Parse(Svg("<rect x='10' y='10' width='50' height='50' rx='8' fill='#000'/>")).layers.Count);
            Assert.AreEqual(0, Parse(Svg("<circle cx='50' cy='50' r='30' fill='#000'/>")).layers.Count);
        }

        static string PngBase64(params Color32[] pixels2x2)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            t.SetPixels32(pixels2x2);
            t.Apply();
            string b = System.Convert.ToBase64String(t.EncodeToPNG());
            Object.DestroyImmediate(t);
            return b;
        }

        [Test]
        public void Pixels_AMagnifiedPictureFill_HasCrispTexels_AndKeepsTheTexelEdgesAntiAliased()
        {
            // Figma's way of writing an image fill: a pattern with the image scaled to the bounding box. 2x2 texels at 40 px each.
            string png = PngBase64(new Color32(255, 0, 0, 255), new Color32(0, 0, 255, 255), new Color32(0, 255, 0, 255), new Color32(255, 255, 0, 255));
            var doc = Parse("<svg xmlns='http://www.w3.org/2000/svg' xmlns:xlink='http://www.w3.org/1999/xlink' viewBox='0 0 80 80'><defs>" +
                            "<pattern id='p' patternContentUnits='objectBoundingBox' width='1' height='1'><use xlink:href='#i' transform='scale(0.5)'/></pattern>" +
                            "<image id='i' width='2' height='2' preserveAspectRatio='none' xlink:href='data:image/png;base64," + png + "'/></defs>" +
                            "<rect width='80' height='80' fill='url(#p)'/></svg>");
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("needs a graphics device");
            var t = VectraRasterizer.Render(doc, 80, 80);
            m_Owned.Add(t);
            // Bilinear would mix 25 % of the neighbour 10 px from the boundary; crisp texels are pure there.
            var left = t.GetPixel(30, 60); // the top row of the picture is the last two pixels set: green | yellow
            Assert.Less(left.r, 0.03f, left.ToString());
            Assert.Greater(left.g, 0.97f, left.ToString());
            var right = t.GetPixel(50, 60);
            Assert.Greater(right.r, 0.97f, right.ToString());
            Assert.Greater(right.g, 0.97f, right.ToString());
            // The boundary is a sharp edge, anti-aliased over one pixel: four pixels apart the colours are already pure.
            Assert.Less(t.GetPixel(38, 60).r, 0.1f);
            Assert.Greater(t.GetPixel(42, 60).r, 0.9f);
        }

        [Test]
        public void ADocumentWithoutFilters_HasNoFilterWork()
        {
            var doc = Parse(Doc("<g opacity='0.5'>" + CircleAndSquare + "</g>"));
            Assert.AreEqual(1, doc.layers.Count);
            Assert.IsNull(doc.layers[0].filter);
            Assert.IsFalse(doc.layers[0].FilterNeedsBackdrop);
        }

        // ---- pixels ----

        static Color Px(Texture2D t, float x, float y) => t.GetPixel(Mathf.RoundToInt(x * t.width / 160f), t.height - 1 - Mathf.RoundToInt(y * t.height / 160f));

        Texture2D Render(VectraSvgDocument doc)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("needs a graphics device");
            var tex = VectraRasterizer.Render(doc, 320, 320);
            m_Owned.Add(tex);
            return tex;
        }

        [Test]
        public void Pixels_GroupShadow_FallsOnTheBackground_NotOnTheCircleBesideTheSquare()
        {
            var t = Render(Parse(Doc(ShadowFilter() + "<g filter='url(#f)'>" + CircleAndSquare + "</g>")));
            var c = Px(t, 68, 75);   // the circle, 7 units left of the square: the square's own shadow (blur 5) would reach it
            Assert.AreEqual(0xE6 / 255f, c.r, 0.02f);
            Assert.AreEqual(0x39 / 255f, c.g, 0.02f);
            Assert.AreEqual(0x46 / 255f, c.b, 0.02f);
            var circleEdge = Px(t, 73, 75); // 2 units left of the square: its own shadow would darken this circle pixel
            Assert.AreEqual(0xE6 / 255f, circleEdge.r, 0.02f);
            var below = Px(t, 110, 108); // below the square: one soft shadow
            Assert.Less(below.g, 0.8f);
            Assert.Greater(below.g, 0.35f);
            var beside = Px(t, 95, 108); // elsewhere under the square: the same shadow
            Assert.AreEqual(below.g, beside.g, 0.06f);
            var underCircle = Px(t, 55, 112); // under the circle (its lowest point is 105): the silhouette's shadow reaches here too
            Assert.Less(underCircle.g, 0.85f);
            Assert.Greater(Px(t, 150, 150).g, 0.99f);
        }

        [Test]
        public void Pixels_MultiplyShadow_MultipliesWithTheBackdrop()
        {
            const string body = "<g filter='url(#f)'><rect x='40' y='40' width='80' height='80' rx='12' fill='#fff'/></g>";
            string yellow = "#E9C46A";
            var multiply = Render(Parse(Doc(ShadowFilter("multiply", "0.9 0.2 0.3 0.8") + body, yellow)));
            var normal = Render(Parse(Doc(ShadowFilter("normal", "0.9 0.2 0.3 0.8") + body, yellow)));
            var m = Px(multiply, 80, 126);
            var n = Px(normal, 80, 126);
            // Shadow colour (0.9, 0.2, 0.3) at about half over yellow (0.91, 0.77, 0.42): multiplied, the blue is 0.42 * 0.3 there, not 0.3.
            Assert.Less(m.b, n.b - 0.04f, "multiplied: " + m + " normal: " + n);
            Assert.AreEqual(1f, Px(multiply, 80, 80).b, 0.02f, "the white shape itself is untouched");
        }

        [Test]
        public void Pixels_SpreadAndChoke_AreAppliedToThePictureOfTheGroup()
        {
            var filter =
                "<defs><filter id='s' x='0' y='0' width='160' height='160' filterUnits='userSpaceOnUse'><feFlood flood-opacity='0' result='BackgroundImageFix'/>" +
                "<feColorMatrix in='SourceAlpha' type='matrix' values='0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 127 0'/>" +
                "<feMorphology radius='10' operator='dilate'/><feOffset/><feGaussianBlur stdDeviation='0'/>" +
                "<feColorMatrix type='matrix' values='0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 1 0'/>" +
                "<feBlend mode='normal' in2='BackgroundImageFix' result='e1'/><feBlend mode='normal' in='SourceGraphic' in2='e1' result='shape'/></filter></defs>";
            var t = Render(Parse(Doc(filter + "<g filter='url(#s)'><rect x='40' y='40' width='30' height='30' fill='#fff'/><rect x='85' y='40' width='30' height='30' fill='#fff'/></g>")));
            Assert.Less(Px(t, 34, 55).r, 0.1f, "6 units left of the shape: inside the 10 unit spread");
            Assert.Less(Px(t, 77, 55).r, 0.1f, "between the two squares: both spreads cover it");
            Assert.Greater(Px(t, 28, 55).r, 0.9f, "12 units away: outside");
        }

        [Test]
        public void Pixels_EdgesOnPixelBoundaries_AreCrisp()
        {
            var doc = Parse("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 40 40'><rect x='10' y='10' width='20' height='20' fill='#000'/></svg>");
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("needs a graphics device");
            var t = VectraRasterizer.Render(doc, 40, 40);
            m_Owned.Add(t);
            Assert.Less(t.GetPixel(9, 20).a, 0.02f, "the pixel outside the edge is clean");
            Assert.Greater(t.GetPixel(10, 20).a, 0.98f, "the pixel inside the edge is full");
            Assert.Greater(t.GetPixel(29, 20).a, 0.98f);
            Assert.Less(t.GetPixel(30, 20).a, 0.02f);
        }

        [Test]
        public void Pixels_AHalfPixelEdge_IsHalfCovered()
        {
            var doc = Parse("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 40 40'><rect x='10.5' y='10' width='19' height='20' fill='#000'/></svg>");
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("needs a graphics device");
            var t = VectraRasterizer.Render(doc, 40, 40);
            m_Owned.Add(t);
            Assert.AreEqual(0.5f, t.GetPixel(10, 20).a, 0.06f);
            Assert.AreEqual(0.5f, t.GetPixel(29, 20).a, 0.06f);
        }

        [Test]
        public void Pixels_BackgroundBlur_DoesNotPullInTheColoursOutsideTheShape()
        {
            // A glass over a red / blue boundary: just inside its left edge the blur may only mix what is inside the glass (blue and red
            // that is inside), never the stripe of another colour outside it.
            var svg = Doc("<rect x='60' width='20' height='160' fill='#f00'/><rect x='80' width='80' height='160' fill='#00f'/>" +
                          "<rect data-figma-bg-blur-radius='24' x='60' y='40' width='80' height='80' fill='#fff' fill-opacity='0.01'/>", "#fff");
            var t = Render(Parse(svg));
            // 3 units inside the left edge (x = 63) the backdrop is red; the blue lies 17 units away and carries little weight, and the
            // white outside the glass none (a plain blur of the whole backdrop would give it about 40 %).
            var c = Px(t, 63, 80);
            Assert.Greater(c.r, 0.8f, c.ToString());
            Assert.Less(c.g, 0.35f, c.ToString());
        }

        // ---- the strict comparison with Figma's own renders ----

        static string Corpus()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(VectraShape).Assembly);
            if (info != null) return Path.Combine(info.resolvedPath, "Tests", "FigmaCorpus~");
            var found = Directory.GetDirectories(Application.dataPath, "FigmaCorpus~", SearchOption.AllDirectories);
            if (found.Length == 0) Assert.Ignore("FigmaCorpus~ not found");
            return found[0];
        }

        // Share of pixels whose colour differs by more than 8 from every pixel within one pixel of it in Figma's render (over white).
        static double OffBy(Texture2D ours, Texture2D figma, int limit)
        {
            int w = figma.width, h = figma.height;
            Assert.AreEqual((w, h), (ours.width, ours.height));
            var a = ours.GetPixels32();
            var b = figma.GetPixels32();
            int wrong = 0;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = a[y * w + x];
                int inv = 255 - c.a;
                int r = (c.r * c.a + 255 * inv) / 255, g = (c.g * c.a + 255 * inv) / 255, bl = (c.b * c.a + 255 * inv) / 255;
                int best = int.MaxValue;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var f = b[Mathf.Clamp(y + dy, 0, h - 1) * w + Mathf.Clamp(x + dx, 0, w - 1)];
                    int d = Mathf.Max(Mathf.Abs(r - f.r), Mathf.Max(Mathf.Abs(g - f.g), Mathf.Abs(bl - f.b)));
                    if (d < best) best = d;
                }

                if (best > limit) wrong++;
            }

            return wrong / (double)(w * h);
        }

        // The cases that were off by more than 8 in 0.7.3 (shadows on groups, shadow blend modes, edges on pixel boundaries, background blur).
        [TestCase("05_layer_blur")]
        [TestCase("06_background_blur")]
        [TestCase("11_image_fill")]
        [TestCase("10_diamond_gradient")]
        [TestCase("15_blend_screen")]
        [TestCase("16_fill_blend_mode")]
        [TestCase("18_mask_alpha_gradient")]
        [TestCase("19_mask_vector")]
        [TestCase("29_per_side_strokes")]
        [TestCase("41_diamond_stroke_rect")]
        [TestCase("42_shadow_blend_multiply")]
        [TestCase("45_inner_shadow_blend_screen")]
        [TestCase("46_shadow_on_group")]
        [TestCase("47_text_shadow_live")]
        public void RasterizerIsWithin8OfFigma(string name)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("needs a graphics device");
            string dir = Corpus();
            var doc = SvgParser.Parse(File.ReadAllText(Path.Combine(dir, "svg", name + ".svg")));
            var reference = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Texture2D ours = null;
            try
            {
                Assert.IsTrue(reference.LoadImage(File.ReadAllBytes(Path.Combine(dir, "figma", name + ".png"))));
                ours = VectraRasterizer.Render(doc, reference.width, reference.height);
                double bad = OffBy(ours, reference, 8);
                Assert.Less(bad, 0.01, $"{name}: {bad * 100:0.00} % of the pixels are off by more than 8");
            }
            finally
            {
                Object.DestroyImmediate(doc);
                Object.DestroyImmediate(reference);
                if (ours != null) Object.DestroyImmediate(ours);
            }
        }
    }
}
