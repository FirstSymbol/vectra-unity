using System.Collections.Generic;
using System.IO;
using System.Linq;
using FirstSymbol.Vectra.Rendering;
using FirstSymbol.Vectra.Svg;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Vectra.Editor.Tests
{
    // SVG filters and gradients as Figma exports them (Tests/FigmaCorpus~ holds the exported files).
    public class FigmaFilterTests
    {
        static readonly List<Object> Cleanup = new List<Object>();

        // Figma blurs with 5/6 of the standard deviation its export writes (see SvgParser.FigmaBlurScale).
        const float Scale = 5f / 6f;

        [TearDown]
        public void TearDown()
        {
            foreach (var o in Cleanup)
                if (o != null) Object.DestroyImmediate(o);
            Cleanup.Clear();
        }

        static string CorpusFolder()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(VectraShape).Assembly);
            if (info != null) return Path.Combine(info.resolvedPath, "Tests", "FigmaCorpus~");
            var found = Directory.GetDirectories(Application.dataPath, "FigmaCorpus~", SearchOption.AllDirectories);
            if (found.Length == 0) Assert.Ignore("FigmaCorpus~ not found");
            return found[0];
        }

        static VectraSvgDocument Load(string name)
        {
            var doc = SvgParser.Parse(File.ReadAllText(Path.Combine(CorpusFolder(), name + ".svg")));
            Cleanup.Add(doc);
            return doc;
        }

        static VectraSvgDocument ParseText(string svg)
        {
            var doc = SvgParser.Parse(svg);
            Cleanup.Add(doc);
            return doc;
        }

        // The element after the white background rect.
        static SvgElementData Shape(VectraSvgDocument doc) => doc.elements[1];

        static void NoUnsupportedWarning(VectraSvgDocument doc)
        {
            Assert.IsFalse(doc.warnings.Any(w => w.Contains("not supported")), string.Join(" | ", doc.warnings));
        }

        [Test]
        public void DropShadow_IsReadFromFigmasChain()
        {
            var doc = Load("01_drop_shadow");
            var fx = Shape(doc).effects;
            Assert.AreEqual(1, fx.Count);
            var d = (DropShadowEffect)fx[0];
            Assert.AreEqual(new Vector2(4f, 6f), d.offset);
            Assert.AreEqual(10f * Scale, d.blur, 1e-4f); // stdDeviation 5 = Figma radius 10, drawn at 5/6 of it like Figma
            Assert.AreEqual(0f, d.spread);
            Assert.AreEqual(0.4f, d.color.a, 1e-4f);
            Assert.AreEqual(0f, d.color.r);
            NoUnsupportedWarning(doc);
        }

        [Test]
        public void TwoShadows_KeepTheirOrderAndSpread()
        {
            var doc = Load("02_drop_shadow_spread_double");
            var fx = Shape(doc).effects;
            Assert.AreEqual(2, fx.Count);
            var a = (DropShadowEffect)fx[0];
            var b = (DropShadowEffect)fx[1];
            Assert.AreEqual(new Vector2(0f, 2f), a.offset);
            Assert.AreEqual(4f * Scale, a.blur, 1e-4f);
            Assert.AreEqual(0.25f, a.color.a, 1e-4f);
            Assert.AreEqual(new Vector2(8f, 12f), b.offset);
            Assert.AreEqual(20f * Scale, b.blur, 1e-4f);
            Assert.AreEqual(6f, b.spread, 1e-4f);
            Assert.AreEqual(0.901961f, b.color.r, 1e-4f);
            Assert.AreEqual(0.5f, b.color.a, 1e-4f);
            NoUnsupportedWarning(doc);
        }

        [Test]
        public void InnerShadow_IsRecognisedByTheArithmeticComposite()
        {
            var doc = Load("03_inner_shadow");
            var fx = Shape(doc).effects;
            Assert.AreEqual(1, fx.Count);
            var s = (InnerShadowEffect)fx[0];
            Assert.AreEqual(new Vector2(4f, 4f), s.offset);
            Assert.AreEqual(8f * Scale, s.blur, 1e-4f);
            Assert.AreEqual(0f, s.choke);
            Assert.AreEqual(0.5f, s.color.a, 1e-4f);
            NoUnsupportedWarning(doc);
        }

        [Test]
        public void DropAndTwoInnerShadows_StackInOrder_ErodeIsChoke()
        {
            var doc = Load("04_drop_and_inner");
            var fx = Shape(doc).effects;
            Assert.AreEqual(3, fx.Count);
            Assert.IsInstanceOf<DropShadowEffect>(fx[0]);
            var i1 = (InnerShadowEffect)fx[1];
            var i2 = (InnerShadowEffect)fx[2];
            Assert.AreEqual(new Vector2(-4f, -6f), i1.offset);
            Assert.AreEqual(1f, i1.color.r);
            Assert.AreEqual(0.8f, i1.color.a, 1e-4f);
            Assert.AreEqual(2f, i2.choke, 1e-4f);
            Assert.AreEqual(10f * Scale, i2.blur, 1e-4f);
            NoUnsupportedWarning(doc);
        }

        [Test]
        public void LayerBlur_IsRead()
        {
            // A star has sharp tips, which the native distance-field blur rounds off: it is blurred as a picture.
            var doc = Load("05_layer_blur");
            Assert.AreEqual(1, doc.layers.Count);
            var blur = doc.layers[0].filter.Single(s => s.kind == SvgFilterKind.Blur);
            Assert.AreEqual(3f * Scale, blur.sigma, 1e-4f);
            NoUnsupportedWarning(doc);
            var rect = ParseText("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100'><defs><filter id='b'><feGaussianBlur stdDeviation='3'/></filter></defs>" +
                                 "<rect width='50' height='50' fill='red' filter='url(#b)'/></svg>");
            Assert.AreEqual(6f, ((LayerBlurEffect)rect.elements[0].effects[0]).blur, 1e-4f, "a plain filter of another generator is not scaled");
        }

        [Test]
        public void Noise_BecomesMonotoneGrain()
        {
            var doc = Load("32_noise_texture");
            var fx = Shape(doc).effects;
            Assert.AreEqual(1, fx.Count);
            var g = (GrainEffect)fx[0];
            Assert.IsTrue(g.monotone);
            Assert.AreEqual(0.4f, g.color.a, 1e-4f);
            Assert.AreEqual(1f, g.size, 1e-4f); // baseFrequency 0.5
            Assert.That(g.density, Is.InRange(0.08f, 0.16f));
            NoUnsupportedWarning(doc);
        }

        [Test]
        public void AngularGradient_ComesFromTheJson_AndTheFallbackGroupIsSkipped()
        {
            var doc = Load("09_angular_gradient");
            Assert.AreEqual(2, doc.elements.Count); // the background and the circle
            var p = doc.elements[1].fill;
            Assert.IsTrue(doc.elements[1].hasFill);
            Assert.AreEqual(PaintType.AngularGradient, p.type);
            Assert.AreEqual(4, p.gradient.stops.Count);
            Assert.AreEqual(0.33f, p.gradient.stops[1].position, 1e-3f);
            Assert.IsFalse(doc.warnings.Any(w => w.Contains("foreignObject")), string.Join(" | ", doc.warnings));
        }

        [Test]
        public void DiamondGradient_ComesFromTheJson()
        {
            var doc = Load("10_diamond_gradient");
            Assert.AreEqual(2, doc.elements.Count);
            Assert.AreEqual(PaintType.DiamondGradient, doc.elements[1].fill.type);
            Assert.AreEqual(2, doc.elements[1].fill.gradient.stops.Count);
        }

        [Test]
        public void FigmaGradientStroke_IsReadLikeTheFill()
        {
            var svg = "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100' fill='none'>" +
                      "<circle cx='50' cy='50' r='30' stroke-width='6' data-figma-gradient-stroke=\"{&quot;type&quot;:&quot;GRADIENT_ANGULAR&quot;," +
                      "&quot;stops&quot;:[{&quot;color&quot;:{&quot;r&quot;:1,&quot;g&quot;:0,&quot;b&quot;:0,&quot;a&quot;:1},&quot;position&quot;:0}," +
                      "{&quot;color&quot;:{&quot;r&quot;:0,&quot;g&quot;:0,&quot;b&quot;:1,&quot;a&quot;:1},&quot;position&quot;:1}]," +
                      "&quot;transform&quot;:{&quot;m00&quot;:60,&quot;m01&quot;:0,&quot;m02&quot;:20,&quot;m10&quot;:0,&quot;m11&quot;:60,&quot;m12&quot;:20},&quot;opacity&quot;:1}\"/></svg>";
            var doc = ParseText(svg);
            Assert.AreEqual(1, doc.elements.Count);
            Assert.IsTrue(doc.elements[0].hasStroke);
            Assert.AreEqual(PaintType.AngularGradient, doc.elements[0].stroke.paint.type);
        }

        [Test]
        public void UnrecognisedPrimitive_KeepsTheElement_AndIsReported()
        {
            var svg = "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100'><defs><filter id='f'>" +
                      "<feGaussianBlur stdDeviation='2' result='b'/><feComposite in='SourceGraphic' in2='b' operator='xor'/>" +
                      "<feMorphology operator='dilate' radius='2' in='SourceGraphic'/></filter></defs>" +
                      "<rect width='50' height='50' fill='red' filter='url(#f)'/></svg>";
            var doc = ParseText(svg);
            Assert.AreEqual(1, doc.elements.Count);
            Assert.IsTrue(doc.elements[0].hasFill);
            Assert.IsTrue(doc.warnings.Any(w => w.Contains("feComposite") && w.Contains("xor")), string.Join(" | ", doc.warnings));
            Assert.IsTrue(doc.warnings.Any(w => w.Contains("feMorphology")), string.Join(" | ", doc.warnings));
        }

        [Test]
        public void ShadowOffset_PointsDownTheDocument_OnTheCanvasToo()
        {
            var doc = Load("01_drop_shadow");
            var canvasGo = new GameObject("Figma canvas", typeof(Canvas));
            Cleanup.Add(canvasGo);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var go = new GameObject("Svg", typeof(RectTransform));
            go.transform.SetParent(canvasGo.transform, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(160f, 160f);
            var img = go.AddComponent<VectraSvgImage>();
            img.Document = doc;
            var root = VectraSvg.Expand(img);
            Cleanup.Add(root.gameObject);
            var shadow = root.GetComponentsInChildren<VectraShape>().SelectMany(s => s.Effects).OfType<DropShadowEffect>().First();
            Assert.AreEqual(4f, shadow.offset.x, 1e-3f);
            Assert.AreEqual(-6f, shadow.offset.y, 1e-3f); // y-up local space: the shadow falls below the shape
        }

        [Test]
        public void RealFigmaStrokeGradients_ReplaceThePaintAndItsZeroOpacity()
        {
            foreach (var pair in new[] { ("40_angular_stroke", PaintType.AngularGradient), ("41_diamond_stroke_rect", PaintType.DiamondGradient) })
            {
                var doc = Load(pair.Item1);
                var e = doc.elements.Last();
                Assert.IsTrue(e.hasStroke, pair.Item1);
                Assert.AreEqual(pair.Item2, e.stroke.paint.type);
                Assert.AreEqual(1f, e.stroke.paint.opacity, 1e-4f);
                Assert.IsFalse(doc.warnings.Any(w => w.Contains("foreignObject")), string.Join(" | ", doc.warnings));
            }
        }

        [Test]
        public void Pixels_ShadowOfAStrokeOnlyElementFollowsTheStroke()
        {
            var t = Render("44_shadow_on_stroke_only");
            Assert.Greater(Px(t, 80, 80).r, 0.98f);      // the disc interior stays clean
            Assert.Less(Px(t, 80, 126).r, 0.9f);          // below the ring: its shadow
            var s = Render("40_angular_stroke");
            Assert.Less(Px(s, 124, 80).g, 0.4f);          // ring painted (red at 3 o'clock)
            Assert.Greater(Px(s, 80, 80).r, 0.98f);       // hole
        }

        // ---- pixels ----

        static Color Px(Texture2D t, float x, float y) => t.GetPixel(Mathf.RoundToInt(x * t.width / 160f), t.height - 1 - Mathf.RoundToInt(y * t.height / 160f));

        static Texture2D Render(string name)
        {
            var tex = VectraRasterizer.Render(Load(name), 320, 320);
            Cleanup.Add(tex);
            return tex;
        }

        [Test]
        public void Pixels_DropShadowFallsBelowAndRight_InnerShadowSitsOnTheTopLeftEdge()
        {
            var t = Render("01_drop_shadow");
            Assert.Greater(Px(t, 150, 150).r, 0.99f);   // far corner stays white
            Assert.Less(Px(t, 80, 126).r, 0.85f); // below the box: shadow
            Assert.Greater(Px(t, 80, 34).r, 0.97f);      // above the box: none (offset down, blur 10 reaches 4 px up)
            var s = Render("03_inner_shadow");
            Assert.Less(Px(s, 34, 80).r, Px(s, 126, 80).r - 0.1f); // inner shadow offset (4,4): darker on the left than the right
        }

        [Test]
        public void Pixels_AngularGradientStartsRight_AndRunsClockwise()
        {
            var t = Render("09_angular_gradient");
            var right = Px(t, 120, 80);
            Assert.Greater(right.r, 0.85f);
            Assert.Less(right.g, 0.4f);
            var below = Px(t, 80, 120);  // a quarter turn clockwise: between red and yellow
            Assert.Greater(below.g, 0.5f);
            Assert.Greater(below.r, 0.8f);
            var above = Px(t, 80, 40);   // three quarters: between teal and red
            Assert.Less(above.r, 0.5f);
        }

        [Test]
        public void Pixels_NoiseSpecklesTheShape_AndLeavesTheOutsideAlone()
        {
            var t = Render("32_noise_texture");
            Assert.Greater(Px(t, 10, 10).r, 0.99f);
            int dark = 0, total = 0;
            for (int y = 40; y < 120; y++)
                for (int x = 40; x < 120; x++)
                {
                    total++;
                    if (Px(t, x, y).r < 0x45 / 255f - 0.04f) dark++;
                }

            float share = dark / (float)total;
            Assert.That(share, Is.InRange(0.06f, 0.2f));
        }
    }
}
