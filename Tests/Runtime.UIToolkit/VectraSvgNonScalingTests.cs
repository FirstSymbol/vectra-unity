using System.Collections;
using FirstSymbol.Vectra.Svg;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FirstSymbol.Vectra.UIToolkit.Tests
{
    /// <summary>vector-effect="non-scaling-stroke" in UI Toolkit: one SVG px is one panel pixel, at any panel scale and element size.</summary>
    public class VectraSvgNonScalingTests
    {
        const string Ns = "vector-effect=\"non-scaling-stroke\"";
        readonly System.Collections.Generic.List<Object> m_Objects = new System.Collections.Generic.List<Object>();
        UitkHarness m_H;

        [TearDown]
        public void TearDown()
        {
            m_H?.Dispose();
            m_H = null;
            foreach (var o in m_Objects)
                if (o != null) Object.DestroyImmediate(o);
            m_Objects.Clear();
        }

        static string Svg(string attrs) =>
            "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100'><line x1='50' y1='0' x2='50' y2='100' stroke='#fff' stroke-width='4' " + attrs + "/></svg>";

        float RowWidth(int row)
        {
            float sum = 0f;
            for (int x = 0; x < m_H.width; x++) sum += m_H.Px(x, row).r / 255f;
            return sum;
        }

        IEnumerator Measure(string svg, float scale, float size, System.Action<float> done)
        {
            m_H?.Dispose();
            m_H = new UitkHarness(512, 512, scale);
            var doc = SvgParser.Parse(svg);
            m_Objects.Add(doc);
            m_H.Place(new VectraSvgElement { Document = doc }, 0, 0, size, size);
            yield return m_H.Frames(6);
            m_H.Invalidate();
            done(RowWidth(Mathf.RoundToInt(size * scale * 0.5f)));
        }

        [UnityTest]
        public IEnumerator Stroke_IsFourPanelPixels_AtEveryPanelScaleAndElementSize()
        {
            foreach (float scale in new[] { 1f, 2f })
                foreach (float size in new[] { 50f, 100f, 200f })
                {
                    float fixedWidth = 0f, normalWidth = 0f;
                    yield return Measure(Svg(Ns), scale, size, w => fixedWidth = w);
                    yield return Measure(Svg(""), scale, size, w => normalWidth = w);
                    Assert.AreEqual(4f * scale, fixedWidth, 0.8f, $"non-scaling at panel scale {scale}, element {size}");
                    Assert.AreEqual(4f * size / 100f * scale, normalWidth, 0.8f, $"normal at panel scale {scale}, element {size}");
                }
        }
    }
}
