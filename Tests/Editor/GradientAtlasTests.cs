using System.Diagnostics;
using System.Text.RegularExpressions;
using FirstSymbol.Vectra.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace FirstSymbol.Vectra.Editor.Tests
{
    public class GradientAtlasTests
    {
        static readonly ColorAdjust k_Identity = ColorAdjust.Identity;

        [SetUp]
        public void SetUp() => VectraGradientAtlas.ResetForTests();

        [TearDown]
        public void TearDown() => VectraGradientAtlas.ResetForTests();

        // Two different colours per index: distinct content for as many paints as a test needs.
        static VectraPaint Paint(int i) =>
            VectraPaint.Linear(new Color((i & 255) / 255f, ((i >> 8) & 255) / 255f, 0.25f), new Color(1f - (i & 255) / 255f, 0.5f, ((i >> 8) & 15) / 15f));

        // Holds n distinct paints at once, so the atlas has to grow to hold them, then lets them all go in order: paint 0 is the oldest cold row.
        static void FillCold(int n)
        {
            var held = new GradientRows[n];
            for (int i = 0; i < n; i++) held[i] = VectraGradientAtlas.Acquire(Paint(i), k_Identity);
            for (int i = 0; i < n; i++) VectraGradientAtlas.Release(ref held[i]);
        }

        [Test]
        public void EqualPaints_ShareOneRow()
        {
            var a = VectraGradientAtlas.Acquire(Paint(1), k_Identity);
            var b = VectraGradientAtlas.Acquire(Paint(1), k_Identity);
            Assert.AreEqual(a.row, b.row);
            Assert.AreEqual(1, VectraGradientAtlas.BakeCount, "baked once");
            Assert.AreEqual(1, VectraGradientAtlas.LiveRows);
        }

        [Test]
        public void LinearAndRadialWithTheSameStops_ShareARow()
        {
            var linear = VectraPaint.Linear(Color.red, Color.blue);
            var radial = VectraPaint.Radial(Color.red, Color.blue);
            Assert.AreEqual(VectraGradientAtlas.Acquire(linear, k_Identity).row, VectraGradientAtlas.Acquire(radial, k_Identity).row);
        }

        [Test]
        public void CollidingHashes_DoNotMergeDifferentPaints()
        {
            VectraGradientAtlas.ForceHashCollisions = true;
            var p1 = Paint(1);
            var p2 = Paint(2);
            var r1 = VectraGradientAtlas.Acquire(p1, k_Identity);
            var r2 = VectraGradientAtlas.Acquire(p2, k_Identity);
            Assert.AreEqual(r1.key, r2.key, "the test hook makes every hash equal");
            Assert.AreNotEqual(r1.row, r2.row, "different paints must not share a row");
            Assert.AreEqual(2, VectraGradientAtlas.BakeCount);

            var again1 = VectraGradientAtlas.Acquire(p1, k_Identity);
            var again2 = VectraGradientAtlas.Acquire(p2, k_Identity);
            Assert.AreEqual(r1.row, again1.row, "each finds its own row in the chain");
            Assert.AreEqual(r2.row, again2.row);
            Assert.AreEqual(2, VectraGradientAtlas.BakeCount);
        }

        [Test]
        public void ReleasingOneOfCollidingPaints_KeepsTheOther()
        {
            VectraGradientAtlas.ForceHashCollisions = true;
            var r1 = VectraGradientAtlas.Acquire(Paint(1), k_Identity);
            var r2 = VectraGradientAtlas.Acquire(Paint(2), k_Identity);
            int row2 = r2.row;
            VectraGradientAtlas.Release(ref r1);
            Assert.AreEqual(1, VectraGradientAtlas.LiveRows);
            var again = VectraGradientAtlas.Acquire(Paint(2), k_Identity);
            Assert.AreEqual(row2, again.row);
            Assert.AreEqual(2, VectraGradientAtlas.BakeCount, "paint 2 was never baked twice");
        }

        [Test]
        public void ColourAdjustment_IsPartOfTheKey()
        {
            var plain = VectraGradientAtlas.Acquire(Paint(3), k_Identity);
            var shifted = VectraGradientAtlas.Acquire(Paint(3), new ColorAdjust { hue = 40f, saturation = 1f, contrast = 1f });
            Assert.AreNotEqual(plain.row, shifted.row);
        }

        [Test]
        public void FreeformPaints_DifferInPoints()
        {
            VectraPaint Freeform(float x)
            {
                var p = new VectraPaint { type = PaintType.Freeform };
                p.freeformPoints.Add(new FreeformPoint(new Vector2(x, 0.5f), Color.red));
                p.freeformPoints.Add(new FreeformPoint(new Vector2(0.9f, 0.1f), Color.blue));
                return p;
            }

            var a = VectraGradientAtlas.Acquire(Freeform(0.1f), k_Identity);
            var b = VectraGradientAtlas.Acquire(Freeform(0.2f), k_Identity);
            var c = VectraGradientAtlas.Acquire(Freeform(0.1f), k_Identity);
            Assert.AreNotEqual(a.row, b.row);
            Assert.AreEqual(a.row, c.row);
            Assert.AreEqual(VectraGradientAtlas.FreeformRows, a.count);
        }

        [Test]
        public void ReleasedRows_StayBakedUntilTheSpaceIsNeeded()
        {
            var r = VectraGradientAtlas.Acquire(Paint(7), k_Identity);
            int row = r.row;
            VectraGradientAtlas.Release(ref r);
            Assert.AreEqual(0, VectraGradientAtlas.LiveRows);
            Assert.AreEqual(1, VectraGradientAtlas.ColdRows);
            var back = VectraGradientAtlas.Acquire(Paint(7), k_Identity);
            Assert.AreEqual(row, back.row, "the cold row is found again");
            Assert.AreEqual(1, VectraGradientAtlas.BakeCount, "not baked a second time");
        }

        [Test]
        public void ChurningThroughMoreThanTheAtlasHolds_NeverOverflows()
        {
            int capacity = VectraGradientAtlas.MaxHeight - 1;
            for (int i = 0; i < capacity * 2; i++)
            {
                var r = VectraGradientAtlas.Acquire(Paint(i), k_Identity);
                Assert.IsTrue(r.IsValid);
                VectraGradientAtlas.Release(ref r);
            }

            Assert.AreEqual(0, VectraGradientAtlas.OverflowCount);
            Assert.Greater(VectraGradientAtlas.EvictionCount, 0, "old rows were evicted to make room");
            Assert.LessOrEqual(VectraGradientAtlas.Height, VectraGradientAtlas.MaxHeight);
        }

        [Test]
        public void Eviction_TakesTheLeastRecentlyUsedRowFirst()
        {
            int capacity = VectraGradientAtlas.MaxHeight - 1;
            FillCold(capacity);
            Assert.AreEqual(capacity, VectraGradientAtlas.ColdRows, "full of cold rows");
            var touched = VectraGradientAtlas.Acquire(Paint(0), k_Identity); // 0 was the oldest; using it makes it the newest
            VectraGradientAtlas.Release(ref touched);
            var fresh = VectraGradientAtlas.Acquire(Paint(capacity + 5), k_Identity);
            Assert.IsTrue(fresh.IsValid);
            Assert.AreEqual(1, VectraGradientAtlas.EvictionCount, "one row made room");
            int baked = VectraGradientAtlas.BakeCount;

            var again0 = VectraGradientAtlas.Acquire(Paint(0), k_Identity);
            Assert.AreEqual(baked, VectraGradientAtlas.BakeCount, "the recently used row survived");
            VectraGradientAtlas.Release(ref again0);
            var again1 = VectraGradientAtlas.Acquire(Paint(1), k_Identity);
            Assert.AreEqual(baked + 1, VectraGradientAtlas.BakeCount, "paint 1 was the oldest, so it was evicted and is baked again");
            Assert.IsTrue(again1.IsValid);
        }

        [Test]
        public void LiveRowsAreNeverEvicted_AndOverflowFallsBackToAWhiteRow()
        {
            int capacity = VectraGradientAtlas.MaxHeight - 1;
            var held = new GradientRows[capacity];
            for (int i = 0; i < capacity; i++) held[i] = VectraGradientAtlas.Acquire(Paint(i), k_Identity);
            Assert.AreEqual(0, VectraGradientAtlas.OverflowCount);
            Assert.AreEqual(capacity, VectraGradientAtlas.LiveRows);

            LogAssert.Expect(LogType.Warning, new Regex("gradient atlas is full"));
            var extra = VectraGradientAtlas.Acquire(Paint(capacity + 1), k_Identity);
            Assert.AreEqual(1, VectraGradientAtlas.OverflowCount);
            Assert.IsTrue(extra.IsValid, "a valid reference to the shared row, not a bad index");
            Assert.AreEqual(0, extra.row);
            Assert.AreEqual(0, VectraGradientAtlas.EvictionCount, "nothing live was evicted");

            VectraGradientAtlas.Release(ref extra); // releasing the fallback changes nothing
            Assert.AreEqual(capacity, VectraGradientAtlas.LiveRows);
            for (int i = 0; i < 10; i++)
            {
                var again = VectraGradientAtlas.Acquire(Paint(i), k_Identity);
                Assert.AreEqual(held[i].row, again.row, "live rows keep their place");
            }

            for (int i = 0; i < capacity; i++) VectraGradientAtlas.Release(ref held[i]);
        }

        [Test]
        public void FreeformBlocks_NeedContiguousRows_AndEvictUntilTheyFit()
        {
            // Fill with single rows, make them all cold, then ask for 32-row blocks over and over.
            FillCold(VectraGradientAtlas.MaxHeight - 1);

            for (int k = 0; k < 6; k++)
            {
                var p = new VectraPaint { type = PaintType.Freeform };
                p.freeformPoints.Add(new FreeformPoint(new Vector2(0.1f * k, 0.5f), Color.green));
                var block = VectraGradientAtlas.Acquire(p, k_Identity);
                Assert.AreEqual(VectraGradientAtlas.FreeformRows, block.count);
                Assert.AreEqual(0, VectraGradientAtlas.OverflowCount);
                VectraGradientAtlas.Release(ref block);
            }
        }

        [Test]
        public void Perf_FillAndEvict()
        {
            int capacity = VectraGradientAtlas.MaxHeight - 1;
            var paints = new VectraPaint[capacity * 2];
            for (int i = 0; i < paints.Length; i++) paints[i] = Paint(i);

            var sw = Stopwatch.StartNew();
            var held0 = new GradientRows[capacity];
            for (int i = 0; i < capacity; i++) held0[i] = VectraGradientAtlas.Acquire(paints[i], k_Identity);
            for (int i = 0; i < capacity; i++) VectraGradientAtlas.Release(ref held0[i]);

            double fillMs = sw.Elapsed.TotalMilliseconds;
            int bakedAfterFill = VectraGradientAtlas.BakeCount;

            sw.Restart();
            for (int i = capacity; i < paints.Length; i++)
            {
                var r = VectraGradientAtlas.Acquire(paints[i], k_Identity);
                VectraGradientAtlas.Release(ref r);
            }

            double evictMs = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            const int hits = 100000;
            var held = VectraGradientAtlas.Acquire(paints[paints.Length - 1], k_Identity);
            for (int i = 0; i < hits; i++)
            {
                var r = VectraGradientAtlas.Acquire(paints[paints.Length - 1], k_Identity);
                VectraGradientAtlas.Release(ref r);
            }

            double hitMs = sw.Elapsed.TotalMilliseconds;
            VectraGradientAtlas.Release(ref held);
            Debug.Log($"VECTRA-METRIC gradient atlas: fill {capacity} rows {fillMs:F1} ms ({fillMs * 1000.0 / capacity:F2} us/row); " +
                      $"{capacity} more with eviction {evictMs:F1} ms ({evictMs * 1000.0 / capacity:F2} us/row, {VectraGradientAtlas.EvictionCount} evictions); " +
                      $"{hits} cache hits {hitMs:F1} ms ({hitMs * 1000000.0 / hits:F0} ns each); baked {bakedAfterFill} after fill");
            Assert.AreEqual(0, VectraGradientAtlas.OverflowCount);
        }
    }
}
