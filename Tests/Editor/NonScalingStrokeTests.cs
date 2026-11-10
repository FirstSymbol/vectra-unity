using FirstSymbol.Vectra.Svg;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Vectra.Editor.Tests
{
    /// <summary>vector-effect="non-scaling-stroke": what the parser makes of it.</summary>
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
        public void Document_ReportsPixelStrokes()
        {
            Assert.IsTrue(Parse(Svg(VLine(Ns))).HasPixelStrokes);
            Assert.IsFalse(Parse(Svg(VLine(""))).HasPixelStrokes);
        }
    }
}
