using FirstSymbol.Vectra.Geometry;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Vectra.Editor.Tests
{
    public class HitTestTests
    {
        [Test]
        public void WorldRenderer_OutsideStrokeCountsItsWholeWidth()
        {
            var go = new GameObject("Hit", typeof(VectraRenderer));
            try
            {
                var r = go.GetComponent<VectraRenderer>();
                r.SetGeometry<RectangleShape>();
                r.Size = new Vector2(100f, 100f);
                r.Strokes.Clear();
                r.Strokes.Add(new StrokeLayer(Color.black, 20f) { alignment = StrokeAlignment.Outside });
                var rect = r.LocalRect;
                float edge = rect.xMax, mid = rect.center.y;
                Assert.IsTrue(r.ContainsLocalPoint(new Vector2(edge - 5f, mid)), "inside the fill");
                Assert.IsTrue(r.ContainsLocalPoint(new Vector2(edge + 15f, mid)), "on the outside stroke, 15 from the edge");
                Assert.IsFalse(r.ContainsLocalPoint(new Vector2(edge + 25f, mid)), "beyond the stroke");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
