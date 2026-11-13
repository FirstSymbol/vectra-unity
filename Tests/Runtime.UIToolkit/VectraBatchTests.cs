using System.Collections;
using System.Collections.Generic;
using FirstSymbol.Vectra.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace FirstSymbol.Vectra.UIToolkit.Tests
{
    /// <summary>Queued surface draws are recorded against the atlas layout of the moment: they must run before an atlas moves anything.</summary>
    public class VectraBatchTests
    {
        static VectraElement Gradient(int i)
        {
            var e = new VectraElement { Shape = VectraElementShape.Rectangle };
            e.Fills[0].paint = VectraPaint.Linear(Color.HSVToRGB((i * 0.0137f) % 1f, 0.8f, 0.9f), Color.HSVToRGB((i * 0.031f + 0.3f) % 1f, 0.7f, 0.5f), 90f);
            e.SetShapeDirty();
            return e;
        }

        [UnityTest]
        public IEnumerator GradientAtlasGrowth_RunsTheQueuedDrawsFirst()
        {
            using var h = new UitkHarness(256, 256);
            var e = h.Place(Gradient(1), 10, 10, 40, 40);
            yield return h.Frames(6);
            e.style.width = 50; // layout asks for a rebuild, which the test runs itself
            yield return null;
            e.RebuildNow();
            Assert.Greater(VectraSurfaces.PendingCount, 0, "the draw waits in the batch");
            // Held distinct gradients until the atlas grows: earlier tests of the same run may have left it larger, with cold rows to evict first.
            int height = VectraGradientAtlas.Height;
            var held = new List<GradientRows>();
            for (int i = 0; i < 5000 && VectraGradientAtlas.Height == height; i++)
            {
                var p = VectraPaint.Linear(new Color((i % 997) / 997f, 0.1f, (i / 997) / 6f), new Color(0.9f, (i % 991) / 991f, 0.4f), 45f);
                held.Add(VectraGradientAtlas.Acquire(p, ColorAdjust.Identity));
            }

            Assert.AreNotEqual(height, VectraGradientAtlas.Height, "the atlas never grew");
            Assert.AreEqual(0, VectraSurfaces.PendingCount, "the batch ran before the atlas grew");
            for (int i = 0; i < held.Count; i++)
            {
                var r = held[i];
                VectraGradientAtlas.Release(ref r);
            }
        }

        [UnityTest]
        public IEnumerator SdfAtlasReset_RunsTheQueuedDrawsFirst()
        {
            using var h = new UitkHarness(128, 128);
            var e = h.Place(new VectraElement { Shape = VectraElementShape.Star, Fill = Color.red }, 10, 10, 40, 40);
            yield return h.Frames(6);
            e.style.width = 50;
            yield return null;
            e.RebuildNow();
            Assert.Greater(VectraSurfaces.PendingCount, 0);
            VectraSdfAtlas.Reset();
            Assert.AreEqual(0, VectraSurfaces.PendingCount);
        }

        [UnityTest]
        public IEnumerator ManyGradientElementsAtOnce_LookLikeTheirOwnRebuild()
        {
            using var h = new UitkHarness(640, 480);
            var list = new List<VectraElement>();
            for (int i = 0; i < 300; i++) list.Add(h.Place(Gradient(i), 4 + i % 30 * 20, 4 + i / 30 * 20, 16, 16));
            for (int f = 0; f < 60; f++) yield return null;
            foreach (var e in list) e.RebuildNow();
            for (int f = 0; f < 4; f++) yield return null;
            var first = new Color32[list.Count];
            for (int i = 0; i < list.Count; i++) first[i] = h.Px(4 + i % 30 * 20 + 8, 4 + i / 30 * 20 + 3);
            h.Invalidate();
            // the same elements, built again with the atlas at rest
            foreach (var e in list) e.RebuildNow();
            for (int f = 0; f < 4; f++) yield return null;
            h.Invalidate();
            for (int i = 0; i < list.Count; i++)
            {
                var c = h.Px(4 + i % 30 * 20 + 8, 4 + i / 30 * 20 + 3);
                Assert.IsTrue(UitkHarness.Near(c, first[i].r, first[i].g, first[i].b, 3), $"element {i}: {UitkHarness.Hex(c)} against {UitkHarness.Hex(first[i])}");
            }
        }

        // The fill is written to the content directly: Fill is applied by the element's own rebuild.
        static VectraElement Solid(VectraElementShape shape, Color color)
        {
            var e = new VectraElement { Shape = shape };
            e.Fills[0].paint = VectraPaint.Solid(color);
            e.SetShapeDirty();
            return e;
        }

        static Color32[] ReadPage(RenderTexture rt)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply(false);
            var pixels = tex.GetPixels32();
            Object.DestroyImmediate(tex);
            RenderTexture.active = prev;
            return pixels;
        }

        // Every slot of a run is cleared before the first picture is drawn, so a picture that is still queued for a slot that was given back
        // must not be drawn under the next owner's picture.
        [Test]
        public void ASlotGivenBackAndTakenAgainInOneBatch_ShowsOnlyTheNewPicture()
        {
            VectraSurfaces.DestroyAll();
            var red = Solid(VectraElementShape.Rectangle, Color.red);
            var star = Solid(VectraElementShape.Star, Color.blue);
            var frame = new ShapeFrame { rect = new Rect(-12f, -12f, 24f, 24f), pixelSize = 1f };
            using var a = new VectraRasterLayer();
            using var b = new VectraRasterLayer();
            // Both are built first: a build can grow an atlas, which runs the batch (the draw of the first would be gone before the second is queued).
            a.renderer.Build(red.Content.ToInput(frame, Color.white, true));
            b.renderer.Build(star.Content.ToInput(frame, Color.white, true));
            VectraSurfaces.Flush();
            a.Rasterize(new Vector2(12f, 12f), 1f);
            Assert.AreEqual(1, VectraSurfaces.PendingCount);
            a.Release();
            b.Rasterize(new Vector2(12f, 12f), 1f);
            Assert.AreEqual(2, VectraSurfaces.PendingCount, "the draw of the old owner is still counted (it is skipped when the batch runs)");
            VectraSurfaces.Flush();
            Assert.AreEqual(0, VectraSurfaces.PendingCount);
            var page = (RenderTexture)b.Texture;
            var pixels = ReadPage(page);
            int redCount = 0, blueCount = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                var c = pixels[i];
                if (c.a > 0 && c.r > 128 && c.b < 128) redCount++;
                if (c.a > 0 && c.b > 128 && c.r < 128) blueCount++;
            }

            Assert.AreEqual(0, redCount, "the picture of the slot's old owner shows");
            Assert.Greater(blueCount, 50, "the new picture is missing");
        }

        // The slots of many elements are cleared by one draw: what each element shows must be what it showed when its slot was cleared on its own.
        [Test]
        public void ManySlotsOfOnePage_ClearedTogether_LookLikeClearedOneByOne()
        {
            VectraSurfaces.DestroyAll();
            var faint = Solid(VectraElementShape.Rectangle, new Color(0f, 0f, 1f, 0.004f));
            var red = Solid(VectraElementShape.Rectangle, Color.red);
            var frame = new ShapeFrame { rect = new Rect(-12f, -12f, 24f, 24f), pixelSize = 1f };
            var layers = new List<VectraRasterLayer>();
            try
            {
                // The first round leaves red pictures; the second takes the same slots (same size) with a nearly transparent picture, which leaves what is under it: the red must have been cleared.
                for (int round = 0; round < 2; round++)
                {
                    foreach (var l in layers) l.Dispose();
                    layers.Clear();
                    for (int i = 0; i < 40; i++)
                    {
                        var l = new VectraRasterLayer();
                        l.renderer.Build((round == 0 ? red : faint).Content.ToInput(frame, Color.white, true));
                        l.Rasterize(new Vector2(12f, 12f), 1f);
                        layers.Add(l);
                    }

                    VectraSurfaces.Flush();
                }

                var pixels = ReadPage((RenderTexture)layers[0].Texture);
                int redCount = 0;
                for (int i = 0; i < pixels.Length; i++)
                {
                    var c = pixels[i];
                    if (c.a > 0 && c.r > 128 && c.b < 128) redCount++;
                }

                Assert.AreEqual(0, redCount, "stale pictures of the first round show");
            }
            finally
            {
                foreach (var l in layers) l.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator ARebuildThatQueuesAnotherElement_DoesNotStallThePanel()
        {
            using var h = new UitkHarness(128, 128);
            var a = h.Place(new VectraElement { Fill = Color.red }, 4, 4, 20, 20);
            var b = h.Place(new VectraElement { Fill = Color.blue }, 40, 4, 20, 20);
            yield return h.Frames(6);
            // a's rebuild (inside the service's pass) changes b, which asks for its own
            a.RegisterCallback<GeometryChangedEvent>(_ => { });
            int before = b.RasterCount;
            a.style.width = 30;
            b.style.width = 30;
            yield return h.Frames(6);
            b.Fill = Color.green;
            yield return h.Frames(6);
            Assert.Greater(b.RasterCount, before);
            Assert.IsTrue(UitkHarness.Near(h.Px(50, 14), 0, 255, 0), UitkHarness.Hex(h.Px(50, 14)));
        }
    }
}
