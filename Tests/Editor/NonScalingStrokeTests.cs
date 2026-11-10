using System.Collections.Generic;
using FirstSymbol.Vectra.Rendering;
using FirstSymbol.Vectra.Svg;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Vectra.Editor.Tests
{
    /// <summary>vector-effect="non-scaling-stroke": parsing, export and the width of the baked stroke at several scales.</summary>
    public class NonScalingStrokeTests
    {
        const string Ns = "vector-effect=\"non-scaling-stroke\"";

        static string Svg(string body) =>
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 100 100\">" + body + "</svg>";

        static string VLine(string attrs) =>
            "<line x1=\"50\" y1=\"0\" x2=\"50\" y2=\"100\" stroke=\"#fff\" stroke-width=\"4\" " + attrs + "/>";

        static string HDash(string attrs) =>
            "<line x1=\"0\" y1=\"50\" x2=\"100\" y2=\"50\" stroke=\"#fff\" stroke-width=\"4\" stroke-dasharray=\"10 10\" " + attrs + "/>";

        static VectraSvgDocument Parse(string svg) => SvgParser.Parse(svg);

        static StrokeLayer StrokeOf(string svg)
        {
            var doc = Parse(svg);
            Assert.AreEqual(1, doc.elements.Count, string.Join("; ", doc.warnings));
            return doc.elements[0].stroke;
        }

        [Test]
        public void Attribute_Style_AndClass_SwitchToPixelMode()
        {
            foreach (var body in new[]
            {
                VLine(Ns),
                VLine("style=\"vector-effect:non-scaling-stroke\""),
                "<style>.n{vector-effect:non-scaling-stroke}</style>" + VLine("class=\"n\"")
            })
            {
                var s = StrokeOf(Svg(body));
                Assert.AreEqual(StrokeWidthMode.Pixels, s.widthMode, body);
                Assert.AreEqual(4f, s.width, 1e-4f, body);
            }

            Assert.AreEqual(StrokeWidthMode.Local, StrokeOf(Svg(VLine(""))).widthMode);
            Assert.AreEqual(StrokeWidthMode.Local, StrokeOf(Svg(VLine("vector-effect=\"none\""))).widthMode);
        }

        [Test]
        public void WidthAndDashes_IgnoreEveryTransform()
        {
            // Browsers: the width is the authored number in screen units, whatever the viewBox, scale() or element transform.
            var normal = StrokeOf(Svg("<g transform=\"scale(3)\">" + HDash("") + "</g>"));
            var fixedStroke = StrokeOf(Svg("<g transform=\"scale(3)\">" + HDash(Ns + " stroke-dashoffset=\"5\"") + "</g>"));
            Assert.AreEqual(12f, normal.width, 1e-3f);
            Assert.AreEqual(30f, normal.dashPattern[0], 1e-3f);
            Assert.AreEqual(4f, fixedStroke.width, 1e-4f);
            Assert.IsTrue(fixedStroke.dashInPixels);
            Assert.AreEqual(10f, fixedStroke.dashPattern[0], 1e-4f);
            Assert.AreEqual(5f, fixedStroke.dashOffset, 1e-4f);
        }

        [Test]
        public void GroupDoesNotInherit_ButUseTargetAndItsOwnProperty_Work()
        {
            // vector-effect is not an inherited property (SVG 2): Chrome draws a stroke in a group with it as a normal stroke.
            var inGroup = StrokeOf(Svg("<g transform=\"scale(2)\" " + Ns + ">" + VLine("") + "</g>"));
            Assert.AreEqual(StrokeWidthMode.Local, inGroup.widthMode);
            Assert.AreEqual(8f, inGroup.width, 1e-3f);

            var def = "<defs>" + VLine("id=\"l\" " + Ns).Replace("<line ", "<line ") + "</defs>";
            var viaUse = StrokeOf(Svg(def + "<use href=\"#l\" transform=\"scale(2)\"/>"));
            Assert.AreEqual(StrokeWidthMode.Pixels, viaUse.widthMode);
            Assert.AreEqual(4f, viaUse.width, 1e-4f);

            var plainDef = "<defs>" + VLine("id=\"l\"") + "</defs>";
            var useCarries = StrokeOf(Svg(plainDef + "<use href=\"#l\" " + Ns + "/>"));
            Assert.AreEqual(StrokeWidthMode.Local, useCarries.widthMode, "the use element's own value is not inherited");
        }

        [Test]
        public void OtherValues_AreIgnoredAndReported()
        {
            foreach (var value in new[] { "non-scaling-size", "non-rotation", "fixed-position" })
            {
                var doc = Parse(Svg(VLine("vector-effect=\"" + value + "\"")));
                Assert.AreEqual(StrokeWidthMode.Local, doc.elements[0].stroke.widthMode, value);
                Assert.IsTrue(doc.warnings.Exists(w => w.Contains(value)), value + ": " + string.Join("; ", doc.warnings));
            }
        }

        [Test]
        public void Export_WritesTheEffect_AndRoundTrips()
        {
            var content = new VectraShapeContent();
            content.geometry = new RectangleShape { cornerRadii = Vector4.zero };
            content.fills.Clear();
            content.strokes.Clear();
            content.strokes.Add(new StrokeLayer(Color.white, 3f) { widthMode = StrokeWidthMode.Pixels });
            content.strokes.Add(new StrokeLayer(Color.red, 5f));
            string svg = SvgWriter.Write(content, new Rect(-50, -50, 100, 100));
            StringAssert.Contains(Ns, svg);
            Assert.AreEqual(1, System.Text.RegularExpressions.Regex.Matches(svg, "vector-effect").Count, "only the pixel stroke is marked");
            var doc = Parse(svg);
            Assert.AreEqual(2, doc.elements.Count);
            Assert.AreEqual(StrokeWidthMode.Pixels, doc.elements[0].stroke.widthMode);
            Assert.AreEqual(3f, doc.elements[0].stroke.width, 1e-3f);
            Assert.AreEqual(StrokeWidthMode.Local, doc.elements[1].stroke.widthMode);
        }

        [Test]
        public void Document_ReportsPixelStrokes()
        {
            Assert.IsTrue(Parse(Svg(VLine(Ns))).HasPixelStrokes);
            Assert.IsFalse(Parse(Svg(VLine(""))).HasPixelStrokes);
        }

        // --- Baked pixels: one device pixel is one baked pixel -----------------------------------------------------

        static Texture2D Bake(string svg, int size)
        {
            var doc = Parse(svg);
            try { return VectraRasterizer.Render(doc, size, size); }
            finally { Object.DestroyImmediate(doc); }
        }

        // Coverage across the middle row of a vertical line: its width in pixels.
        static float RowCoverage(Texture2D t)
        {
            var row = t.GetPixels(0, t.height / 2, t.width, 1);
            float sum = 0f;
            foreach (var c in row) sum += c.a;
            return sum;
        }

        static int RisingEdges(Texture2D t)
        {
            var row = t.GetPixels(0, t.height / 2, t.width, 1);
            int n = 0;
            bool on = false;
            foreach (var c in row)
            {
                bool now = c.a > 0.5f;
                if (now && !on) n++;
                on = now;
            }

            return n;
        }

        [TestCase(50)]
        [TestCase(100)]
        [TestCase(200)]
        [TestCase(400)]
        public void BakedWidth_StaysFourPixels_WhileNormalStrokesScale(int size)
        {
            var fixedTex = Bake(Svg(VLine(Ns)), size);
            var normalTex = Bake(Svg(VLine("")), size);
            try
            {
                Assert.AreEqual(4f, RowCoverage(fixedTex), 0.35f, "non-scaling");
                Assert.AreEqual(4f * size / 100f, RowCoverage(normalTex), 0.35f, "normal");
            }
            finally
            {
                Object.DestroyImmediate(fixedTex);
                Object.DestroyImmediate(normalTex);
            }
        }

        [Test]
        public void BakedWidth_IgnoresAnInnerScaleToo()
        {
            var tex = Bake(Svg("<g transform=\"scale(3)\"><line x1=\"16.67\" y1=\"0\" x2=\"16.67\" y2=\"34\" stroke=\"#fff\" stroke-width=\"4\" " + Ns + "/></g>"), 200);
            try { Assert.AreEqual(4f, RowCoverage(tex), 0.35f); }
            finally { Object.DestroyImmediate(tex); }
        }

        [Test]
        public void BakedDashes_AreInPixelsToo()
        {
            var a = Bake(Svg(HDash(Ns)), 200);
            var b = Bake(Svg(HDash(Ns)), 400);
            var c = Bake(Svg(HDash("")), 200);
            var d = Bake(Svg(HDash("")), 400);
            try
            {
                Assert.AreEqual(10, RisingEdges(a), "200 px / 20 px period");
                Assert.AreEqual(20, RisingEdges(b), "400 px / 20 px period");
                Assert.AreEqual(5, RisingEdges(c), "normal dashes scale: 40 px period");
                Assert.AreEqual(5, RisingEdges(d), "normal dashes scale: 80 px period");
            }
            finally
            {
                foreach (var t in new[] { a, b, c, d }) Object.DestroyImmediate(t);
            }
        }
    }
}
