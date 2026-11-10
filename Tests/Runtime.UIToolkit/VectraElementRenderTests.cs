using System.Collections;
using FirstSymbol.Vectra.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace FirstSymbol.Vectra.UIToolkit.Tests
{
    /// <summary>Pixel checks of Vectra elements in a runtime panel rendered into a render texture.</summary>
    public class VectraElementRenderTests
    {
        UitkHarness m_H;

        [SetUp]
        public void SetUp() => m_H = new UitkHarness(256, 256);

        [TearDown]
        public void TearDown() => m_H.Dispose();

        static VectraElement Rect(Color fill, float radius = 0f)
        {
            var e = new VectraElement { Fill = fill, CornerRadius = radius };
            return e;
        }

        [UnityTest]
        public IEnumerator SolidRectangleFillsItsLayoutBox()
        {
            m_H.Place(Rect(Color.red), 40, 50, 80, 60);
            yield return m_H.Frames();
            Assert.IsTrue(UitkHarness.Near(m_H.Px(80, 80), 255, 0, 0), UitkHarness.Hex(m_H.Px(80, 80)));
            Assert.IsTrue(UitkHarness.Near(m_H.Px(42, 52), 255, 0, 0), "inside the corner " + UitkHarness.Hex(m_H.Px(42, 52)));
            Assert.IsTrue(UitkHarness.Near(m_H.Px(118, 108), 255, 0, 0), "inside the far corner " + UitkHarness.Hex(m_H.Px(118, 108)));
            Assert.IsTrue(UitkHarness.Near(m_H.Px(36, 80), 0, 0, 0), "left of the box " + UitkHarness.Hex(m_H.Px(36, 80)));
            Assert.IsTrue(UitkHarness.Near(m_H.Px(80, 46), 0, 0, 0), "above the box " + UitkHarness.Hex(m_H.Px(80, 46)));
            Assert.IsTrue(UitkHarness.Near(m_H.Px(124, 80), 0, 0, 0), "right of the box " + UitkHarness.Hex(m_H.Px(124, 80)));
            Assert.IsTrue(UitkHarness.Near(m_H.Px(80, 114), 0, 0, 0), "below the box " + UitkHarness.Hex(m_H.Px(80, 114)));
        }

        [UnityTest]
        public IEnumerator TriangleKeepsItsOrientation()
        {
            var t = m_H.Place(new VectraElement { Shape = VectraElementShape.Triangle, Fill = Color.green }, 40, 40, 100, 100);
            yield return m_H.Frames();
            // Apex at the top centre: narrow near the top, full width at the bottom.
            Assert.IsTrue(UitkHarness.Near(m_H.Px(90, 60), 0, 255, 0), "centre near the top " + UitkHarness.Hex(m_H.Px(90, 60)));
            Assert.IsTrue(UitkHarness.Near(m_H.Px(50, 60), 0, 0, 0), "left near the top " + UitkHarness.Hex(m_H.Px(50, 60)));
            Assert.IsTrue(UitkHarness.Near(m_H.Px(50, 135), 0, 255, 0), "left at the bottom " + UitkHarness.Hex(m_H.Px(50, 135)));
            Assert.IsTrue(UitkHarness.Near(m_H.Px(130, 135), 0, 255, 0), "right at the bottom " + UitkHarness.Hex(m_H.Px(130, 135)));
        }

        [UnityTest]
        public IEnumerator Spike_Orientation_Dump()
        {
            using var h = new UitkHarness(700, 700);
            h.Place(new VectraElement { Shape = VectraElementShape.Triangle, Fill = Color.green }, 10, 10, 600, 600);
            yield return h.Frames();
            Debug.Log("DUMP big " + h.Dump("tri_big") + " apexTop=" + UitkHarness.Hex(h.Px(310, 40)) + " bottomEdge=" + UitkHarness.Hex(h.Px(30, 580)) + " topLeft=" + UitkHarness.Hex(h.Px(30, 40)));
        }

        [UnityTest]
        public IEnumerator Spike_Orientation_Dump_Small()
        {
            m_H.Place(new VectraElement { Shape = VectraElementShape.Triangle, Fill = Color.green }, 40, 40, 100, 100);
            var e = m_H.Place(Rect(Color.red), 160, 40, 40, 40);
            e.AddEffect<DropShadowEffect>().offset = new Vector2(0, -20);
            yield return m_H.Frames();
            Debug.Log("DUMP small " + m_H.Dump("tri_small"));
        }

        // What "half transparent" looks like depends on the project's colour space, so the Vectra element is compared with
        // a plain UI Toolkit element given the same colour.
        [UnityTest]
        public IEnumerator TranslucentFillBlendsLikeAPlainElement()
        {
            m_H.Place(Rect(new Color(1f, 0f, 0f, 0.5f)), 20, 40, 80, 80);
            var plain = new VisualElement();
            plain.style.backgroundColor = new Color(1f, 0f, 0f, 0.5f);
            m_H.Place(plain, 140, 40, 80, 80);
            yield return m_H.Frames();
            var v = m_H.Px(60, 80);
            var p = m_H.Px(180, 80);
            Assert.Greater(v.r, 100, "translucent, not empty " + UitkHarness.Hex(v));
            Assert.Less(v.r, 230, "translucent, not opaque " + UitkHarness.Hex(v));
            Assert.AreEqual(p.r, v.r, 8, $"vectra {UitkHarness.Hex(v)} plain {UitkHarness.Hex(p)}");
            Assert.AreEqual(0, v.g, 4);
        }

        [UnityTest]
        public IEnumerator ElementOpacityAppliesLikeToAPlainElement()
        {
            var e = m_H.Place(Rect(Color.white), 20, 40, 80, 80);
            e.style.opacity = 0.5f;
            var plain = new VisualElement();
            plain.style.backgroundColor = Color.white;
            plain.style.opacity = 0.5f;
            m_H.Place(plain, 140, 40, 80, 80);
            yield return m_H.Frames();
            var v = m_H.Px(60, 80);
            var p = m_H.Px(180, 80);
            Assert.Greater(v.r, 100, UitkHarness.Hex(v));
            Assert.Less(v.r, 230, UitkHarness.Hex(v));
            Assert.AreEqual(p.r, v.r, 8, $"vectra {UitkHarness.Hex(v)} plain {UitkHarness.Hex(p)}");
        }

        [UnityTest]
        public IEnumerator EllipseIsAntiAliasedAndRound()
        {
            m_H.Place(new VectraElement { Shape = VectraElementShape.Ellipse, Fill = Color.white }, 40, 40, 100, 100);
            yield return m_H.Frames();
            Assert.IsTrue(UitkHarness.Near(m_H.Px(90, 90), 255, 255, 255), "centre");
            Assert.IsTrue(UitkHarness.Near(m_H.Px(44, 44), 0, 0, 0), "the corner of the box is outside the circle");
            // Along the rim some pixels are neither black nor white.
            int partial = 0;
            for (int x = 40; x < 140; x++)
            for (int y = 40; y < 140; y++)
            {
                var c = m_H.Px(x, y);
                if (c.r > 30 && c.r < 225) partial++;
            }

            Assert.Greater(partial, 100, "soft edge pixels");
        }

        [UnityTest]
        public IEnumerator GradientRunsAcrossTheElement()
        {
            var e = m_H.Place(new VectraElement(), 20, 20, 200, 40);
            e.Fills[0].paint = VectraPaint.Linear(Color.black, Color.white);
            e.SetShapeDirty();
            yield return m_H.Frames();
            var left = m_H.Px(24, 40);
            var mid = m_H.Px(120, 40);
            var right = m_H.Px(216, 40);
            Assert.Less(left.r, 40, "left " + UitkHarness.Hex(left));
            Assert.Greater(right.r, 215, "right " + UitkHarness.Hex(right));
            Assert.IsTrue(mid.r > 90 && mid.r < 170, "middle " + UitkHarness.Hex(mid));
        }

        [UnityTest]
        public IEnumerator StrokeIsDrawnOnTheOutline()
        {
            m_H.Place(new VectraElement { Fill = new Color(0, 0, 0, 0), Stroke = Color.blue, StrokeWidth = 8 }, 40, 40, 100, 100);
            yield return m_H.Frames();
            Assert.IsTrue(UitkHarness.Near(m_H.Px(90, 41), 0, 0, 255), "top stroke " + UitkHarness.Hex(m_H.Px(90, 41)));
            Assert.IsTrue(UitkHarness.Near(m_H.Px(90, 90), 0, 0, 0), "hollow middle " + UitkHarness.Hex(m_H.Px(90, 90)));
        }

        [UnityTest]
        public IEnumerator ShadowReachesOutsideTheLayoutBox()
        {
            var e = m_H.Place(Rect(Color.white), 80, 60, 60, 60);
            var sh = e.AddEffect<DropShadowEffect>();
            sh.color = new Color(1f, 0f, 0f, 1f);
            sh.offset = new Vector2(0f, -20f);
            sh.blur = 2f;
            yield return m_H.Frames();
            // The shadow lies below the box (y down in the panel): the box ends at 120, the shadow reaches about 140.
            var c = m_H.Px(110, 132);
            Assert.IsTrue(UitkHarness.Near(c, 255, 0, 0, 20), "below the box " + UitkHarness.Hex(c));
            Assert.IsTrue(UitkHarness.Near(m_H.Px(110, 90), 255, 255, 255), "the box stays on top");
        }
    }
}
