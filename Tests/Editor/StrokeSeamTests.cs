using System.Collections.Generic;
using FirstSymbol.Vectra.Geometry;
using FirstSymbol.Vectra.Rendering;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace FirstSymbol.Vectra.Editor.Tests
{
    /// <summary>Strokes are tessellated from overlapping pieces; float noise must not leave slivers whose two sides are both anti-aliased.</summary>
    public class StrokeSeamTests
    {
        static bool Covered(NativeList<Trapezoid> traps, float x, float y, int skip)
        {
            for (int i = 0; i < traps.Length; i++)
            {
                if (i == skip) continue;
                var t = traps[i];
                if (y < t.yb || y >= t.yt) continue;
                float f = (y - t.yb) / (t.yt - t.yb);
                if (x >= math.lerp(t.xlb, t.xlt, f) && x < math.lerp(t.xrb, t.xrt, f)) return true;
            }

            return false;
        }

        [TestCase(LineJoin.Miter, 0.25f)]
        [TestCase(LineJoin.Miter, 0.03f)]
        [TestCase(LineJoin.Round, 0.1f)]
        [TestCase(LineJoin.Bevel, 0.03f)]
        public void WideRing_HasNoBoundarySidesInsideTheStroke(LineJoin join, float tolerance)
        {
            const float radius = 42f;
            var poly = new Polylines(Allocator.TempJob, 64);
            int n = math.max(8, (int)math.ceil(math.PI / math.acos(1f - tolerance / radius)));
            poly.BeginContour(0);
            for (int i = 0; i < n; i++)
            {
                float a = 2f * math.PI * i / n;
                poly.AddPoint(new float2(math.cos(a), math.sin(a)) * radius);
            }

            poly.EndContour(true);
            var sp = StrokeParams.Default(16f);
            sp.join = join;
            sp.tolerance = tolerance;
            var traps = new NativeList<Trapezoid>(256, Allocator.TempJob);
            VectraGeometry.StrokeAndTessellate(poly, sp, default, default, default, FillRule.NonZero, 0, ref traps);
            int inner = 0;
            for (int i = 0; i < traps.Length; i++)
            {
                var t = traps[i];
                float ym = 0.5f * (t.yb + t.yt);
                float xl = 0.5f * (t.xlb + t.xlt), xr = 0.5f * (t.xrb + t.xrt);
                float xm = 0.5f * (xl + xr);
                if ((t.flags & TrapezoidEdges.Left) != 0 && Covered(traps, xl - 0.02f, ym, i)) inner++;
                if ((t.flags & TrapezoidEdges.Right) != 0 && Covered(traps, xr + 0.02f, ym, i)) inner++;
                if ((t.flags & TrapezoidEdges.Top) != 0 && Covered(traps, xm, t.yt + 0.02f, i)) inner++;
                if ((t.flags & TrapezoidEdges.Bottom) != 0 && Covered(traps, xm, t.yb - 0.02f, i)) inner++;
            }

            traps.Dispose();
            poly.Dispose();
            Assert.AreEqual(0, inner, "boundary sides that lie inside the stroke show as lighter seams");
        }

        [Test]
        public void FlattenSize_IsTheFinerOfTheMainViewAndTheDensestView()
        {
            Assert.AreEqual(1f, new ShapeFrame { pixelSize = 1f }.FlattenSize);
            Assert.AreEqual(0.25f, new ShapeFrame { pixelSize = 1f, detailPixelSize = 0.25f }.FlattenSize);
            Assert.AreEqual(1f, new ShapeFrame { pixelSize = 1f, detailPixelSize = 3f }.FlattenSize);
        }

        [Test]
        public void DetailPixelSize_RefinesCurves()
        {
            using var r = new VectraShapeRenderer();
            var input = new VectraBuildInput
            {
                geometry = new RectangleShape { cornerRadii = new Vector4(30f, 30f, 30f, 30f) },
                fills = new List<FillLayer>(),
                strokes = new List<StrokeLayer> { new StrokeLayer(Color.black, 10f) { dashed = true } },
                effects = new List<VectraEffect>(),
                frame = new ShapeFrame { rect = new Rect(-50, -50, 100, 100), pixelSize = 1f },
                tint = Color.white,
                quality = VectraQuality.High
            };
            r.Build(input);
            int coarse = r.buffer.VertexCount;
            input.frame.detailPixelSize = 0.25f;
            r.Build(input);
            Assert.Greater(r.buffer.VertexCount, coarse * 1.3f);
        }

        [Test]
        public void Drifted_FollowsTheDebounceRule()
        {
            float next = 0f;
            Assert.IsFalse(VectraScreenDensity.Drifted(1.02f, 1f, ref next, 0f), "under 3 %");
            Assert.IsTrue(VectraScreenDensity.Drifted(1.1f, 1f, ref next, 0f), "a small step is taken when the clock allows");
            Assert.IsFalse(VectraScreenDensity.Drifted(1.2f, 1f, ref next, 0.05f), "but not twice within 0.1 s");
            Assert.IsTrue(VectraScreenDensity.Drifted(0.5f, 1f, ref next, 0.05f), "a step over 25 % goes at once");
            Assert.IsTrue(VectraScreenDensity.Drifted(1.2f, 1f, ref next, 0.3f));
        }
    }
}
