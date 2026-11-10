using System.Collections;
using System.Collections.Generic;
using FirstSymbol.Vectra.Svg;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FirstSymbol.Vectra.UIToolkit.Tests
{
    /// <summary>Layers (group opacity, masks, blend modes) in a UI Toolkit panel: the composited texture sits where the shapes would, right way up.</summary>
    public class VectraSvgLayerTests
    {
        UitkHarness m_H;
        readonly List<Object> m_Objects = new List<Object>();

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

        VectraSvgDocument Parse(string body)
        {
            var d = SvgParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 160 160'><rect width='160' height='160' fill='#fff'/>" + body + "</svg>");
            m_Objects.Add(d);
            return d;
        }

        const string TwoCircles = "<circle cx='60' cy='75' r='35' fill='#E63946'/><circle cx='100' cy='75' r='35' fill='#264653'/>";

        [UnityTest]
        public IEnumerator GroupOpacity_IsCompositedAsOne_AndKeepsItsPlaceAndOrientation()
        {
            // The element sits at (40, 40) and is 160 x 160 points: document (x, y) is panel (40 + x, 40 + y).
            m_H.Place(new VectraSvgElement { Document = Parse("<g opacity='0.5'>" + TwoCircles + "</g>") }, 40, 40, 160, 160);
            yield return m_H.Frames();
            Assert.IsTrue(UitkHarness.Near(m_H.Px(40 + 80, 40 + 75), 146, 162, 169, 8), "overlap " + UitkHarness.Hex(m_H.Px(120, 115)));
            Assert.IsTrue(UitkHarness.Near(m_H.Px(40 + 40, 40 + 75), 243, 156, 163, 8), "left circle " + UitkHarness.Hex(m_H.Px(80, 115)));
            Assert.IsTrue(UitkHarness.Near(m_H.Px(40 + 5, 40 + 5), 255, 255, 255, 3), "the page rectangle at the top-left corner " + UitkHarness.Hex(m_H.Px(45, 45)));
            Assert.IsTrue(UitkHarness.Near(m_H.Px(10, 10), 0, 0, 0, 3), "outside the element");
        }

        [UnityTest]
        public IEnumerator Layer_IsNotFlippedOrMirrored()
        {
            var doc = Parse("<g opacity='0.5'><rect x='0' y='0' width='100' height='50' fill='#f00'/><rect x='20' y='0' width='60' height='50' fill='#00f'/></g>");
            m_H.Place(new VectraSvgElement { Document = doc }, 40, 40, 160, 160);
            yield return m_H.Frames();
            Assert.IsTrue(UitkHarness.Near(m_H.Px(40 + 50, 40 + 25), 128, 128, 255, 8), "blue over red at the top " + UitkHarness.Hex(m_H.Px(90, 65)));
            Assert.IsTrue(UitkHarness.Near(m_H.Px(40 + 10, 40 + 25), 255, 128, 128, 8), "red only " + UitkHarness.Hex(m_H.Px(50, 65)));
            Assert.IsTrue(UitkHarness.Near(m_H.Px(40 + 130, 40 + 25), 255, 255, 255, 3), "top right is empty " + UitkHarness.Hex(m_H.Px(170, 65)));
            Assert.IsTrue(UitkHarness.Near(m_H.Px(40 + 50, 40 + 135), 255, 255, 255, 3), "bottom is empty " + UitkHarness.Hex(m_H.Px(90, 175)));
        }

        [UnityTest]
        public IEnumerator Multiply_BlendsWithTheDocumentBelow()
        {
            var doc = Parse("<rect x='20' y='20' width='120' height='120' fill='#C86432'/><rect x='40' y='40' width='80' height='80' fill='#3366CC' style='mix-blend-mode:multiply'/>");
            m_H.Place(new VectraSvgElement { Document = doc }, 40, 40, 160, 160);
            yield return m_H.Frames();
            Assert.IsTrue(UitkHarness.Near(m_H.Px(40 + 80, 40 + 80), 40, 40, 40, 8), "blended " + UitkHarness.Hex(m_H.Px(120, 120)));
            Assert.IsTrue(UitkHarness.Near(m_H.Px(40 + 30, 40 + 30), 200, 100, 50, 4), "backdrop " + UitkHarness.Hex(m_H.Px(70, 70)));
        }

        [UnityTest]
        public IEnumerator Mask_OfAGradient_FadesTheContent()
        {
            var doc = Parse("<defs><linearGradient id='g' x1='0' y1='0' x2='160' y2='0' gradientUnits='userSpaceOnUse'><stop stop-color='#fff'/><stop offset='1' stop-color='#000'/></linearGradient></defs>" +
                            "<mask id='m'><rect width='160' height='160' fill='url(#g)'/></mask><rect width='160' height='160' fill='#00f' mask='url(#m)'/>");
            m_H.Place(new VectraSvgElement { Document = doc }, 40, 40, 160, 160);
            yield return m_H.Frames();
            Assert.IsTrue(UitkHarness.Near(m_H.Px(40 + 80, 40 + 80), 128, 128, 255, 12), "middle " + UitkHarness.Hex(m_H.Px(120, 120)));
            Assert.IsTrue(UitkHarness.Near(m_H.Px(40 + 5, 40 + 80), 8, 8, 255, 16), "left " + UitkHarness.Hex(m_H.Px(45, 120)));
        }
    }
}
