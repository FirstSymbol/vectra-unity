using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Vectra.UIToolkit.Tests
{
    /// <summary>The page allocator behind the elements: packing, reuse and cleanup.</summary>
    public class VectraSurfaceTests
    {
        readonly List<SurfaceRegion> m_Regions = new List<SurfaceRegion>();

        [SetUp]
        public void SetUp() => VectraSurfaces.DestroyAll();

        [TearDown]
        public void TearDown()
        {
            VectraSurfaces.DestroyAll();
            m_Regions.Clear();
        }

        SurfaceRegion Alloc(int w, int h)
        {
            Assert.IsTrue(VectraSurfaces.TryAllocate(w, h, out var r), $"allocate {w}x{h}");
            m_Regions.Add(r);
            return r;
        }

        static bool Overlap(in SurfaceRegion a, in SurfaceRegion b) =>
            a.page == b.page && a.x < b.x + b.w && b.x < a.x + a.w && a.y < b.y + b.h && b.y < a.y + a.h;

        [Test]
        public void SmallRegionsShareOnePageWithoutOverlap()
        {
            for (int i = 0; i < 60; i++) Alloc(40 + i % 7 * 9, 30 + i % 5 * 11);
            Assert.AreEqual(1, VectraSurfaces.PageCount);
            for (int i = 0; i < m_Regions.Count; i++)
            {
                var a = m_Regions[i];
                Assert.LessOrEqual(a.x + a.w, VectraSurfaces.PageSize);
                Assert.LessOrEqual(a.y + a.h, VectraSurfaces.PageSize);
                for (int j = i + 1; j < m_Regions.Count; j++)
                    Assert.IsFalse(Overlap(a, m_Regions[j]), $"regions {i} and {j} overlap");
            }
        }

        [Test]
        public void FullPageOpensTheNextOne()
        {
            for (int i = 0; i < 16; i++) Alloc(500, 500);
            Assert.AreEqual(1, VectraSurfaces.PageCount, "16 regions of 500 px fill one 2048 page");
            Alloc(500, 500);
            Assert.AreEqual(2, VectraSurfaces.PageCount);
        }

        [Test]
        public void LargeRegionGetsAnExactTextureOfItsOwn()
        {
            var r = Alloc(900, 700);
            Assert.AreEqual(900, r.Texture.width);
            Assert.AreEqual(700, r.Texture.height);
            var small = Alloc(32, 32);
            Assert.AreNotSame(r.Texture, small.Texture);
            var copy = m_Regions[0];
            VectraSurfaces.Free(ref copy);
            Assert.AreEqual(1, VectraSurfaces.PageCount, "the dedicated texture is destroyed when its region is freed");
        }

        [Test]
        public void FreedSlotIsReused()
        {
            var a = Alloc(64, 64);
            var b = Alloc(64, 64);
            int ax = a.x, ay = a.y;
            var page = a.page;
            VectraSurfaces.Free(ref a);
            var c = Alloc(60, 60);
            Assert.AreSame(page, c.page);
            Assert.AreEqual(ax, c.x, "same slot");
            Assert.AreEqual(ay, c.y);
            Assert.AreNotEqual(b.x, c.x);
        }

        [Test]
        public void EmptyShelvesGiveTheirRowsBack()
        {
            var tall = Alloc(100, 400);
            var tallCopy = tall;
            VectraSurfaces.Free(ref tallCopy);
            m_Regions.Clear();
            var wide = Alloc(400, 30);
            Assert.AreEqual(0, wide.y, "the page starts again from the top");
            Assert.AreEqual(1, VectraSurfaces.PageCount);
        }

        [Test]
        public void ImpossibleSizesAreRefused()
        {
            Assert.IsFalse(VectraSurfaces.TryAllocate(0, 10, out _));
            Assert.IsFalse(VectraSurfaces.TryAllocate(10, -1, out _));
            Assert.IsFalse(VectraSurfaces.TryAllocate(VectraSurfaces.MaxSize + 1, 10, out _));
        }
    }
}
