using System.Collections;
using FirstSymbol.Vectra.Svg;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace FirstSymbol.Vectra.UIToolkit.Tests
{
    /// <summary>Pieces of one SVG text with their own x and y, and letter spacing, in a UI Toolkit panel.</summary>
    public class VectraSvgTextLayoutTests
    {
        UitkHarness m_H;
        VectraSvgDocument m_Doc;

        [SetUp]
        public void SetUp() => m_H = new UitkHarness(256, 256);

        [TearDown]
        public void TearDown()
        {
            m_H.Dispose();
            if (m_Doc != null) Object.DestroyImmediate(m_Doc);
        }

        [UnityTest]
        public IEnumerator EachPiece_GetsItsOwnLabel_AtItsOwnPlace()
        {
            m_Doc = SvgParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 200 200'><text fill='#fff' xml:space='preserve' font-size='40' letter-spacing='4px'>" +
                                    "<tspan x='10' y='60'>HH&#10;</tspan><tspan x='110' y='140' text-decoration='underline'>HH</tspan></text></svg>");
            var e = m_H.Place(new VectraSvgElement { Document = m_Doc }, 0, 0, 200, 200);
            yield return m_H.Frames(6);
            var labels = e.Query<Label>().ToList();
            Assert.AreEqual(2, labels.Count);
            StringAssert.Contains("<cspace=", labels[0].text, "letter spacing reaches the label as rich text");
            StringAssert.Contains("<u>", labels[1].text);
            int lowest = -1, left = 9999, left2 = 9999;
            for (int y = 0; y < 100; y++)
            for (int x = 0; x < 200; x++)
            {
                var c = m_H.Px(x, y);
                if (c.r > 160 && c.g > 160 && c.b > 160)
                {
                    lowest = Mathf.Max(lowest, y);
                    left = Mathf.Min(left, x);
                }
            }

            for (int y = 100; y < 200; y++)
            for (int x = 0; x < 200; x++)
            {
                var c = m_H.Px(x, y);
                if (c.r > 160 && c.g > 160 && c.b > 160) left2 = Mathf.Min(left2, x);
            }

            Assert.AreEqual(60, lowest, 5, "the first piece sits on y = 60");
            Assert.That(left, Is.InRange(9, 22), "the first piece starts at x = 10");
            Assert.That(left2, Is.InRange(109, 122), "the second piece starts at x = 110");
        }
    }
}
