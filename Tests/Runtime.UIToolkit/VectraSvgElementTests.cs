using System.Collections;
using FirstSymbol.Vectra.Svg;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace FirstSymbol.Vectra.UIToolkit.Tests
{
    public class VectraSvgElementTests
    {
        UitkHarness m_H;
        readonly System.Collections.Generic.List<Object> m_Objects = new System.Collections.Generic.List<Object>();

        [SetUp]
        public void SetUp() => m_H = new UitkHarness(256, 256);

        [TearDown]
        public void TearDown()
        {
            m_H.Dispose();
            foreach (var o in m_Objects)
                if (o != null) Object.DestroyImmediate(o);
            m_Objects.Clear();
        }

        VectraSvgDocument Parse(string svg)
        {
            var d = SvgParser.Parse(svg);
            m_Objects.Add(d);
            return d;
        }

        const string TopHalf =
            "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100'><rect x='0' y='0' width='100' height='50' fill='#f00'/></svg>";

        [UnityTest]
        public IEnumerator ShapesKeepTheirPlaceAndOrientation()
        {
            var e = m_H.Place(new VectraSvgElement { Document = Parse(TopHalf), ColorBlending = Rendering.SvgColorBlendingChoice.Linear }, 40, 40, 100, 100);
            yield return m_H.Frames();
            Assert.IsTrue(UitkHarness.Near(m_H.Px(90, 60), 255, 0, 0), "top half is red " + UitkHarness.Hex(m_H.Px(90, 60)));
            Assert.IsTrue(UitkHarness.Near(m_H.Px(90, 120), 0, 0, 0), "bottom half is empty " + UitkHarness.Hex(m_H.Px(90, 120)));
            Assert.AreEqual(0, e.GeneratedCount, "one run of shapes needs no child");
        }

        [UnityTest]
        public IEnumerator PreserveAspectLetterboxesALargerBox()
        {
            // A 100 x 50 document in a 200 x 200 box: 200 x 100 centred vertically.
            var doc = Parse("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 50'><rect width='100' height='50' fill='#0f0'/></svg>");
            m_H.Place(new VectraSvgElement { Document = doc }, 20, 20, 200, 200);
            yield return m_H.Frames();
            Assert.IsTrue(UitkHarness.Near(m_H.Px(120, 120), 0, 255, 0), "centre");
            Assert.IsTrue(UitkHarness.Near(m_H.Px(120, 40), 0, 0, 0), "above the letterbox " + UitkHarness.Hex(m_H.Px(120, 40)));
            Assert.IsTrue(UitkHarness.Near(m_H.Px(120, 200), 0, 0, 0), "below the letterbox");
        }

        [UnityTest]
        public IEnumerator OverridesChangeColoursAndHide()
        {
            var doc = Parse("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100'><rect id='a' width='50' height='100' fill='#f00'/><rect id='b' x='50' width='50' height='100' fill='#00f'/></svg>");
            var e = m_H.Place(new VectraSvgElement { Document = doc }, 40, 40, 100, 100);
            e.Overrides.Add(new SvgOverride { selector = "#a", overrideFill = true, fill = Color.green });
            e.Overrides.Add(new SvgOverride { selector = "#b", hide = true });
            e.SetDirty();
            yield return m_H.Frames();
            Assert.IsTrue(UitkHarness.Near(m_H.Px(65, 90), 0, 255, 0), "recoloured " + UitkHarness.Hex(m_H.Px(65, 90)));
            Assert.IsTrue(UitkHarness.Near(m_H.Px(115, 90), 0, 0, 0), "hidden");
        }

        const string Sandwich =
            "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100'>" +
            "<rect width='100' height='100' fill='#003'/>" +
            "<text x='5' y='60' font-size='50' fill='#fff'>HHH</text>" +
            "<rect x='40' y='0' width='60' height='100' fill='#f00'/></svg>";

        [UnityTest]
        public IEnumerator ShapesAfterTextPaintOverIt()
        {
            // These tests look at the meshes and children; the default in a Linear project draws pictures.
            var e = m_H.Place(new VectraSvgElement { Document = Parse(Sandwich), ColorBlending = Rendering.SvgColorBlendingChoice.Linear }, 10, 10, 200, 200);
            yield return m_H.Frames(6);
            Assert.AreEqual(2, e.GeneratedCount, "a label and a layer for the shapes after it");
            // The red block starts at x = 10 + 40 * 2 = 90; no white may show inside it.
            int whiteInside = 0, whiteOutside = 0;
            for (int y = 10; y < 210; y++)
            {
                for (int x = 95; x < 210; x++)
                {
                    var c = m_H.Px(x, y);
                    if (c.r > 200 && c.g > 200 && c.b > 200) whiteInside++;
                }

                for (int x = 12; x < 86; x++)
                {
                    var c = m_H.Px(x, y);
                    if (c.r > 200 && c.g > 200 && c.b > 200) whiteOutside++;
                }
            }

            Assert.AreEqual(0, whiteInside, "the red block covers the text");
            Assert.Greater(whiteOutside, 200, "the text is drawn left of the block");
            Assert.IsTrue(UitkHarness.Near(m_H.Px(150, 100), 255, 0, 0), "red block " + UitkHarness.Hex(m_H.Px(150, 100)));
        }

        [UnityTest]
        public IEnumerator TextBaselineSitsOnItsY()
        {
            var doc = Parse("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100'><text x='10' y='70' font-size='60' fill='#fff'>H</text></svg>");
            m_H.Place(new VectraSvgElement { Document = doc }, 0, 0, 200, 200);
            yield return m_H.Frames(6);
            // Document y 70 is panel y 140. Find the lowest white row of the H.
            int lowest = -1, highest = 9999;
            for (int y = 0; y < 200; y++)
            for (int x = 0; x < 200; x++)
            {
                var c = m_H.Px(x, y);
                if (c.r > 160 && c.g > 160 && c.b > 160)
                {
                    lowest = Mathf.Max(lowest, y);
                    highest = Mathf.Min(highest, y);
                }
            }

            Debug.Log($"SVGTEXT ink rows {highest}..{lowest} (baseline 140, cap height of 120 px font would reach about 140 - 0.7 * 120 = 56). {m_H.Dump("svg_text")}");
            Assert.Greater(lowest, 0, "ink found");
            Assert.AreEqual(140, lowest, 5, "the bottom of the H is on the baseline");
        }

        [UnityTest]
        public IEnumerator MiddleAnchorCentresTheText()
        {
            var doc = Parse("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100'><text x='50' y='70' font-size='30' text-anchor='middle' fill='#fff'>WWWW</text></svg>");
            m_H.Place(new VectraSvgElement { Document = doc }, 0, 0, 200, 200);
            yield return m_H.Frames(6);
            int left = 9999, right = -1;
            for (int y = 0; y < 200; y++)
            for (int x = 0; x < 200; x++)
            {
                var c = m_H.Px(x, y);
                if (c.r > 160) { left = Mathf.Min(left, x); right = Mathf.Max(right, x); }
            }

            Assert.Greater(right, left, "ink found");
            Assert.AreEqual(100f, (left + right) * 0.5f, 6f, $"ink spans {left}..{right}, centre should be 100");
        }

        [UnityTest]
        public IEnumerator ElementReleasesItsLayersWhenRemoved()
        {
            var e = m_H.Place(new VectraSvgElement { Document = Parse(Sandwich) }, 10, 10, 200, 200);
            yield return m_H.Frames(4);
            Assert.AreEqual(1, VectraUitk.LiveSvgs);
            e.RemoveFromHierarchy();
            Assert.AreEqual(0, VectraUitk.LiveSvgs);
            Assert.AreEqual(0, e.GeneratedCount);
        }
    }
}
