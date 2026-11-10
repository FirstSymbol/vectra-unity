using System;
using System.Collections;
using System.Collections.Generic;
using FirstSymbol.Vectra.Rendering;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace FirstSymbol.Vectra.UIToolkit.Tests
{
    /// <summary>Dirty-only rebuilds, batching, resizing, picking and allocation of the UI Toolkit elements.</summary>
    public class VectraElementBehaviourTests
    {
        UitkHarness m_H;

        [SetUp]
        public void SetUp() => m_H = new UitkHarness(256, 256);

        [TearDown]
        public void TearDown() => m_H.Dispose();

        [UnityTest]
        public IEnumerator RebuildsOnlyWhenSomethingChanges()
        {
            var e = m_H.Place(new VectraElement { Fill = Color.red }, 20, 20, 80, 80);
            yield return m_H.Frames();
            int first = e.RasterCount;
            Assert.GreaterOrEqual(first, 1);
            yield return m_H.Frames(30);
            Assert.AreEqual(first, e.RasterCount, "an idle element is not redrawn");

            e.Fill = Color.blue;
            yield return m_H.Frames();
            Assert.AreEqual(first + 1, e.RasterCount, "one change, one redraw");

            e.style.width = 120;
            yield return m_H.Frames();
            Assert.AreEqual(first + 2, e.RasterCount, "a new size redraws");
            Assert.GreaterOrEqual(e.SurfaceSize.x, 120);
        }

        [UnityTest]
        public IEnumerator ChangeInTheModelIsPickedUpAfterSetShapeDirty()
        {
            var e = m_H.Place(new VectraElement { Fill = Color.red, CornerRadius = 0 }, 40, 40, 80, 80);
            yield return m_H.Frames();
            Assert.IsTrue(UitkHarness.Near(m_H.Px(80, 80), 255, 0, 0));
            e.Fills[0].paint = VectraPaint.Solid(Color.green);
            e.SetShapeDirty();
            yield return m_H.Frames();
            m_H.Invalidate();
            Assert.IsTrue(UitkHarness.Near(m_H.Px(80, 80), 0, 255, 0), UitkHarness.Hex(m_H.Px(80, 80)));
        }

        [UnityTest]
        public IEnumerator ManySmallElementsShareOnePage()
        {
            var list = new List<VectraElement>();
            for (int i = 0; i < 24; i++)
            {
                var e = m_H.Place(new VectraElement { Shape = i % 2 == 0 ? VectraElementShape.Ellipse : VectraElementShape.Star, Fill = Color.HSVToRGB(i / 24f, 1, 1) },
                    8 + i % 6 * 40, 8 + i / 6 * 40, 32, 32);
                list.Add(e);
            }

            yield return m_H.Frames();
            Assert.AreEqual(1, VectraSurfaces.PageCount, "24 small shapes, one page");
            var tex = list[0].SurfaceTexture;
            Assert.IsNotNull(tex);
            foreach (var e in list) Assert.AreSame(tex, e.SurfaceTexture, "same texture, so UI Toolkit can batch the quads");
            // Each one still shows its own colour.
            for (int i = 0; i < 24; i++)
            {
                var c = m_H.Px(8 + i % 6 * 40 + 16, 8 + i / 6 * 40 + 16);
                var want = (Color32)Color.HSVToRGB(i / 24f, 1, 1);
                Assert.IsTrue(UitkHarness.Near(c, want.r, want.g, want.b, 10), $"element {i}: {UitkHarness.Hex(c)} wanted {UitkHarness.Hex(want)}");
            }
        }

        [UnityTest]
        public IEnumerator LargeElementGetsATextureOfItsOwnAndPagesAreFreed()
        {
            using var h = new UitkHarness(700, 700);
            int before = VectraSurfaces.PageCount;
            var big = h.Place(new VectraElement { Fill = Color.white }, 0, 0, 600, 600);
            yield return h.Frames();
            Assert.AreEqual(before + 1, VectraSurfaces.PageCount);
            big.RemoveFromHierarchy();
            yield return h.Frames();
            Assert.AreEqual(before, VectraSurfaces.PageCount, "the texture goes with the element");
        }

        [UnityTest]
        public IEnumerator ElementsReleaseTheirResourcesWhenTheyLeaveThePanel()
        {
            var e = m_H.Place(new VectraElement(), 10, 10, 50, 50);
            yield return m_H.Frames();
            Assert.AreEqual(1, VectraUitk.LiveShapes);
            e.RemoveFromHierarchy();
            Assert.AreEqual(0, VectraUitk.LiveShapes);
            Assert.AreEqual(Vector2Int.zero, e.SurfaceSize);
        }

        [UnityTest]
        public IEnumerator PanelScaleRedrawsAtTheNewDensity()
        {
            using var h2 = new UitkHarness(256, 256, 2f);
            var e = h2.Place(new VectraElement { Fill = Color.white, CornerRadius = 0 }, 10, 10, 40, 40);
            yield return h2.Frames();
            // 40 points at scale 2 are 80 pixels (plus a pixel of border each side).
            Assert.GreaterOrEqual(e.SurfaceSize.x, 80);
            Assert.LessOrEqual(e.SurfaceSize.x, 90);
            Assert.IsTrue(UitkHarness.Near(h2.Px(60, 60), 255, 255, 255));
            Assert.IsTrue(UitkHarness.Near(h2.Px(16, 60), 0, 0, 0), "x = 8 points is outside the element");
        }

        [UnityTest]
        public IEnumerator PickingFollowsTheOutline()
        {
            var e = m_H.Place(new VectraElement { Shape = VectraElementShape.Ellipse }, 40, 40, 100, 100);
            yield return m_H.Frames();
            Assert.IsTrue(e.ContainsPoint(new Vector2(50, 50)), "centre");
            Assert.IsFalse(e.ContainsPoint(new Vector2(3, 3)), "corner of the box is outside the ellipse");
            Assert.IsFalse(e.ContainsPoint(new Vector2(-10, 50)), "outside the box");
        }

        [UnityTest]
        public IEnumerator TriangleHitTestUsesTheSameOrientationAsTheDrawing()
        {
            var e = m_H.Place(new VectraElement { Shape = VectraElementShape.Triangle }, 40, 40, 100, 100);
            yield return m_H.Frames();
            Assert.IsTrue(e.ContainsPoint(new Vector2(50, 15)), "near the apex, top centre");
            Assert.IsFalse(e.ContainsPoint(new Vector2(5, 15)), "top left corner is empty");
            Assert.IsTrue(e.ContainsPoint(new Vector2(5, 95)), "bottom left corner is filled");
        }

        [UnityTest]
        public IEnumerator UssCustomPropertiesStyleTheElement()
        {
#if UNITY_EDITOR
            const string dir = "Assets/VectraUitkTmp";
            if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
            const string path = dir + "/test.uss";
            System.IO.File.WriteAllText(path,
                ".t { --vectra-fill: rgb(0, 200, 0); --vectra-corner-radius: 0; }\n.t:hover { --vectra-fill: rgb(200, 0, 0); }\n");
            UnityEditor.AssetDatabase.ImportAsset(path);
            var sheet = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
            Assert.IsNotNull(sheet);
            m_H.Root.styleSheets.Add(sheet);
            var e = m_H.Place(new VectraElement(), 40, 40, 80, 80);
            e.AddToClassList("t");
            yield return m_H.Frames();
            var c = m_H.Px(80, 80);
            Assert.IsTrue(UitkHarness.Near(c, 0, 200, 0, 8), "fill from USS: " + UitkHarness.Hex(c));
            Assert.IsTrue(UitkHarness.Near(m_H.Px(42, 42), 0, 200, 0, 8), "corner radius 0 from USS");
            Assert.AreEqual(new Color(0, 200 / 255f, 0, 1), e.Fill);
            UnityEditor.AssetDatabase.DeleteAsset(dir);
#else
            yield break;
#endif
        }

        // ----- allocation -----

        static long GcBytesOver(Action step, int frames)
        {
            using var rec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            long best = long.MaxValue;
            for (int r = 0; r < 4; r++)
            {
                long b0 = rec.CurrentValue;
                for (int i = 0; i < frames; i++) step();
                best = Math.Min(best, rec.CurrentValue - b0);
            }

            return best;
        }

        [UnityTest]
        public IEnumerator RebuildingAnimatedShapeAllocatesNothingAfterWarmUp()
        {
            var e = m_H.Place(new VectraElement { Shape = VectraElementShape.Ellipse, Fill = Color.red, Stroke = Color.white, StrokeWidth = 3 }, 40, 40, 100, 100);
            e.AddEffect<DropShadowEffect>();
            yield return m_H.Frames();
            int n = 0;
            Action step = () =>
            {
                e.Fills[0].paint.color = new Color((n++ % 10) / 10f, 0.3f, 0.6f, 1f);
                e.SetShapeDirty();
                e.RebuildNow();
            };
            for (int i = 0; i < 8; i++) step();
            long bytes = GcBytesOver(step, 20);
            Assert.AreEqual(0, bytes, "managed bytes allocated by 20 rebuilds + rasterizations");
        }

        [UnityTest]
        public IEnumerator Diag_Allocation_Stages()
        {
            var e = m_H.Place(new VectraElement { Shape = VectraElementShape.Ellipse, Fill = Color.red }, 40, 40, 100, 100);
            yield return m_H.Frames();
            var layerField = typeof(VectraElement).GetField("m_Layer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var layer = (VectraRasterLayer)layerField.GetValue(e);
            var input = e.Content.ToInput(new ShapeFrame { rect = new Rect(-50, -50, 100, 100), pixelSize = 1f }, Color.white, true);
            long build = GcBytesOver(() => layer.renderer.Build(input), 20);
            long raster = GcBytesOver(() => layer.Rasterize(new Vector2(50, 50), 1f), 20);
            long rebuild = GcBytesOver(() => e.RebuildNow(), 20);
            e.AddEffect<DropShadowEffect>();
            e.RebuildNow();
            var input2 = e.Content.ToInput(new ShapeFrame { rect = new Rect(-50, -50, 100, 100), pixelSize = 1f }, Color.white, true);
            long buildFx = GcBytesOver(() => layer.renderer.Build(input2), 20);
            long rasterFx = GcBytesOver(() => layer.Rasterize(new Vector2(50, 50), 1f), 20);
            Debug.Log($"DIAG bytes per 20: build {build}, raster {raster}, rebuild {rebuild}, buildWithShadow {buildFx}, rasterWithShadow {rasterFx}");
        }

        [UnityTest]
        public IEnumerator FramesWithAnimatedShapeCostNoMoreThanAPlainElement()
        {
            var shape = m_H.Place(new VectraElement { Fill = Color.red }, 20, 20, 80, 80);
            var plain = new VisualElement();
            plain.style.backgroundColor = Color.red;
            m_H.Place(plain, 120, 20, 80, 80);
            yield return m_H.Frames(8);

            // A frame where only the plain element changes, then a frame where only the Vectra element changes.
            var samples = new List<long>();
            int n = 0;
            using (var rec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame"))
            {
                for (int i = 0; i < 12; i++)
                {
                    shape.Fills[0].paint.color = new Color((n++ % 8) / 8f, 0, 0, 1);
                    shape.SetShapeDirty();
                    yield return null;
                    if (i >= 4) samples.Add(rec.LastValue);
                }
            }

            var plainSamples = new List<long>();
            using (var rec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame"))
            {
                for (int i = 0; i < 12; i++)
                {
                    plain.style.backgroundColor = new Color((n++ % 8) / 8f, 0, 0, 1);
                    yield return null;
                    if (i >= 4) plainSamples.Add(rec.LastValue);
                }
            }

            samples.Sort();
            plainSamples.Sort();
            long vectra = samples[0], baseline = plainSamples[0];
            Debug.Log($"UITK frame allocation, smallest of 8 frames: vectra element {vectra} B, plain element {baseline} B");
            Assert.LessOrEqual(vectra, baseline + 64, "a redrawn Vectra element allocates no more than UI Toolkit does itself");
        }
    }
}
