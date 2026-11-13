using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace FirstSymbol.Vectra.UIToolkit.Tests
{
    /// <summary>Every Vectra element of a panel is rebuilt and polled by one service of that panel.</summary>
    public class VectraPanelServiceTests
    {
        [UnityTest]
        public IEnumerator ManyElementsResizedTogether_AllRebuildAndTheServiceGoesWithTheLastOne()
        {
            using var h = new UitkHarness(256, 256);
            Assert.AreEqual(0, VectraPanelService.ServiceCount);
            var list = new List<VectraElement>();
            for (int i = 0; i < 200; i++) list.Add(h.Place(new VectraElement { Fill = Color.red }, 4 + i % 20 * 12, 4 + i / 20 * 12, 10, 10));
            yield return h.Frames(6);
            Assert.AreEqual(1, VectraPanelService.ServiceCount, "one service for the panel");
            int widthBefore = list[0].SurfaceSize.x;
            var counts = new int[list.Count];
            for (int i = 0; i < list.Count; i++) counts[i] = list[i].RasterCount;
            foreach (var e in list)
            {
                e.style.width = 8;
                e.style.height = 8;
            }

            yield return h.Frames(6);
            for (int i = 0; i < list.Count; i++) Assert.Greater(list[i].RasterCount, counts[i], $"element {i} was rasterized again");
            Assert.Less(list[0].SurfaceSize.x, widthBefore, "the picture follows the new size");

            foreach (var e in list) e.RemoveFromHierarchy();
            yield return h.Frames();
            Assert.AreEqual(0, VectraPanelService.ServiceCount, "no element left in the panel");

            // and it starts again
            var again = h.Place(new VectraElement { Fill = Color.green }, 10, 10, 20, 20);
            yield return h.Frames();
            Assert.AreEqual(1, VectraPanelService.ServiceCount);
            Assert.IsTrue(UitkHarness.Near(h.Px(20, 20), 0, 255, 0), UitkHarness.Hex(h.Px(20, 20)));
            Assert.Greater(again.RasterCount, 0);
        }

        [UnityTest]
        public IEnumerator AnElementRemovedBeforeItsRebuildRuns_IsNotRebuilt()
        {
            using var h = new UitkHarness(128, 128);
            var keep = h.Place(new VectraElement { Fill = Color.red }, 4, 4, 20, 20);
            var gone = h.Place(new VectraElement { Fill = Color.blue }, 40, 4, 20, 20);
            yield return h.Frames(4);
            int before = gone.RasterCount;
            gone.style.width = 30; // asks for a rebuild in the next update ...
            gone.RemoveFromHierarchy(); // ... which it leaves the panel before
            keep.style.width = 30;
            yield return h.Frames(4);
            Assert.AreEqual(before, gone.RasterCount);
            Assert.IsTrue(UitkHarness.Near(h.Px(20, 14), 255, 0, 0), UitkHarness.Hex(h.Px(20, 14)));
        }

        [UnityTest]
        public IEnumerator TwoPanels_EachGetTheirOwnService()
        {
            using var a = new UitkHarness(64, 64);
            using var b = new UitkHarness(64, 64);
            a.Place(new VectraElement { Fill = Color.red }, 4, 4, 20, 20);
            b.Place(new VectraElement { Fill = Color.blue }, 4, 4, 20, 20);
            yield return a.Frames(5);
            Assert.AreEqual(2, VectraPanelService.ServiceCount);
            Assert.IsTrue(UitkHarness.Near(a.Px(14, 14), 255, 0, 0), UitkHarness.Hex(a.Px(14, 14)));
            Assert.IsTrue(UitkHarness.Near(b.Px(14, 14), 0, 0, 255), UitkHarness.Hex(b.Px(14, 14)));
        }
    }
}
