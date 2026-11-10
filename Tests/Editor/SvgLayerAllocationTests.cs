using FirstSymbol.Vectra.Rendering;
using FirstSymbol.Vectra.Svg;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace FirstSymbol.Vectra.Editor.Tests
{
    /// <summary>A Canvas image with composited layers allocates nothing in the frames where nothing changed, and draws its layers only when something did.</summary>
    public class SvgLayerAllocationTests
    {
        const string Svg = "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100'><rect width='100' height='100' fill='#fff'/>" +
                           "<g opacity='0.5'><circle cx='35' cy='50' r='25' fill='#E63946'/><circle cx='65' cy='50' r='25' fill='#264653'/></g></svg>";

        [Test]
        public void IdleFrames_AllocateNothing_AndDoNotRedrawTheLayer()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("needs a graphics device");
            var canvasGo = new GameObject("canvas", typeof(Canvas));
            var go = new GameObject("svg", typeof(RectTransform));
            var doc = SvgParser.Parse(Svg);
            try
            {
                go.transform.SetParent(canvasGo.transform, false);
                var rt = (RectTransform)go.transform;
                rt.sizeDelta = new Vector2(200, 200);
                var image = go.AddComponent<VectraSvgImage>();
                image.raycastTarget = false;
                image.Document = doc;
                Canvas.ForceUpdateCanvases();
                image.RebuildGenerated();
                Canvas.ForceUpdateCanvases();
                Assert.IsNotNull(image.GetComponentInChildren<VectraSvgComposite>(), "the group is a composited part");
                Assert.IsNotNull(image.GetComponentInChildren<VectraSvgComposite>().Texture, "and it was drawn");

                int steps = SvgCompositor.StepsRendered;
                var (bytes, count) = GcProbe.Measure(_ => image.RefreshGenerated(), 20, out string all);
                Assert.AreEqual(0, bytes, $"{count} allocations in 20 idle frames ({all})");
                Assert.AreEqual(steps, SvgCompositor.StepsRendered, "nothing changed, nothing was drawn");
            }
            finally
            {
                Object.DestroyImmediate(canvasGo);
                Object.DestroyImmediate(doc);
            }
        }
    }
}
