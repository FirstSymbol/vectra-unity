using FirstSymbol.Vectra.Geometry;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace FirstSymbol.Vectra.Editor.Tests
{
    public class GeometryTests
    {
        const double AreaTolerance = 1e-3;

        static void AddRect(ref Polylines p, float x, float y, float w, float h, int operand = 0, bool clockwise = false)
        {
            p.BeginContour(operand);
            if (!clockwise)
            {
                p.AddPoint(new float2(x, y));
                p.AddPoint(new float2(x + w, y));
                p.AddPoint(new float2(x + w, y + h));
                p.AddPoint(new float2(x, y + h));
            }
            else
            {
                p.AddPoint(new float2(x, y));
                p.AddPoint(new float2(x, y + h));
                p.AddPoint(new float2(x + w, y + h));
                p.AddPoint(new float2(x + w, y));
            }

            p.EndContour(true);
        }

        static double Fill(in Polylines p, FillRule rule, out double boundaryLength)
        {
            var traps = new NativeList<Trapezoid>(64, Allocator.TempJob);
            VectraGeometry.Tessellate(p, rule, ref traps);
            double area = ScanlineTessellator.Area(traps.AsArray());
            boundaryLength = BoundaryLength(traps);
            traps.Dispose();
            return area;
        }

        static double BoundaryLength(NativeList<Trapezoid> traps)
        {
            var segs = new NativeList<Segment>(64, Allocator.Temp);
            ScanlineTessellator.BoundarySegments(traps.AsArray(), ref segs);
            double len = 0;
            for (int i = 0; i < segs.Length; i++) len += math.distance(segs[i].a, segs[i].b);
            segs.Dispose();
            return len;
        }

        static double Boolean(BooleanOp op)
        {
            var p = new Polylines(Allocator.TempJob);
            AddRect(ref p, 0, 0, 1, 1, 0);
            AddRect(ref p, 0.5f, 0.5f, 1, 1, 1);
            var ops = new NativeArray<Operand>(2, Allocator.TempJob);
            ops[0] = new Operand(FillRule.NonZero);
            ops[1] = new Operand(FillRule.NonZero, op);
            var traps = new NativeList<Trapezoid>(64, Allocator.TempJob);
            VectraGeometry.Tessellate(p, ops, ref traps);
            double area = ScanlineTessellator.Area(traps.AsArray());
            traps.Dispose();
            ops.Dispose();
            p.Dispose();
            return area;
        }

        [Test]
        public void Square_AreaAndPerimeter()
        {
            var p = new Polylines(Allocator.TempJob);
            AddRect(ref p, 0, 0, 2, 3);
            Assert.AreEqual(6.0, Fill(p, FillRule.NonZero, out double len), AreaTolerance);
            Assert.AreEqual(10.0, len, AreaTolerance);
            p.Dispose();
        }

        [Test]
        public void Hole_NonZeroAndEvenOdd()
        {
            var p = new Polylines(Allocator.TempJob);
            AddRect(ref p, 0, 0, 4, 4);
            AddRect(ref p, 1, 1, 2, 2, 0, clockwise: true);
            Assert.AreEqual(12.0, Fill(p, FillRule.NonZero, out double len), AreaTolerance);
            Assert.AreEqual(24.0, len, AreaTolerance);
            p.Dispose();

            p = new Polylines(Allocator.TempJob);
            AddRect(ref p, 0, 0, 4, 4);
            AddRect(ref p, 1, 1, 2, 2);
            Assert.AreEqual(16.0, Fill(p, FillRule.NonZero, out _), AreaTolerance);
            Assert.AreEqual(12.0, Fill(p, FillRule.EvenOdd, out _), AreaTolerance);
            p.Dispose();
        }

        [Test]
        public void Pentagram_SelfIntersection()
        {
            var p = new Polylines(Allocator.TempJob);
            p.BeginContour();
            for (int i = 0; i < 5; i++)
            {
                float a = math.PI * 0.5f + i * 4f * math.PI / 5f;
                p.AddPoint(new float2(math.cos(a), math.sin(a)));
            }

            p.EndContour(true);
            double nonZero = Fill(p, FillRule.NonZero, out _);
            double evenOdd = Fill(p, FillRule.EvenOdd, out _);
            // Inner pentagon of a unit pentagram: circumradius r = 0.381966.
            double inner = 2.5 * 0.381966 * 0.381966 * math.sin(2 * math.PI / 5);
            Assert.Greater(nonZero, evenOdd);
            Assert.AreEqual(inner, nonZero - evenOdd, 1e-3);
            p.Dispose();
        }

        [Test]
        public void Booleans_OverlappingSquares()
        {
            Assert.AreEqual(1.75, Boolean(BooleanOp.Union), AreaTolerance);
            Assert.AreEqual(0.75, Boolean(BooleanOp.Subtract), AreaTolerance);
            Assert.AreEqual(0.25, Boolean(BooleanOp.Intersect), AreaTolerance);
            Assert.AreEqual(1.5, Boolean(BooleanOp.Exclude), AreaTolerance);
        }

        [Test]
        public void SharedEdge_IsSeamless()
        {
            var p = new Polylines(Allocator.TempJob);
            AddRect(ref p, 0, 0, 1, 1);
            AddRect(ref p, 1, 0, 1, 1);
            Assert.AreEqual(2.0, Fill(p, FillRule.NonZero, out double len), AreaTolerance);
            Assert.AreEqual(6.0, len, AreaTolerance);
            p.Dispose();
        }

        [Test]
        public void TJunction_HorizontalBoundaryIsExact()
        {
            // A "T": bar 3x1 on top of a 1x2 stem.
            var p = new Polylines(Allocator.TempJob);
            p.BeginContour();
            p.AddPoint(new float2(1, 0));
            p.AddPoint(new float2(2, 0));
            p.AddPoint(new float2(2, 2));
            p.AddPoint(new float2(3, 2));
            p.AddPoint(new float2(3, 3));
            p.AddPoint(new float2(0, 3));
            p.AddPoint(new float2(0, 2));
            p.AddPoint(new float2(1, 2));
            p.EndContour(true);
            Assert.AreEqual(5.0, Fill(p, FillRule.NonZero, out double len), AreaTolerance);
            Assert.AreEqual(12.0, len, AreaTolerance);
            p.Dispose();
        }

        [Test]
        public void Circle_AreaConverges()
        {
            var path = new NativePath(Allocator.TempJob);
            var b = new PathBuilder(path);
            b.AddEllipse(float2.zero, new float2(10, 10));
            var poly = new Polylines(Allocator.TempJob);
            VectraGeometry.Flatten(path, Affine2.Identity, 0.001f, 0, true, ref poly);
            // Cubic circle approximations overshoot the radius by ~0.027%, so allow 0.1 area units.
            Assert.AreEqual(math.PI * 100.0, Fill(poly, FillRule.NonZero, out double len), 0.1);
            Assert.AreEqual(2 * math.PI * 10.0, len, 0.01);
            poly.Dispose();
            path.Dispose();
        }

        static double StrokeArea(in Polylines input, StrokeParams sp, float[] dash = null)
        {
            var d = new NativeArray<float>(dash ?? new float[0], Allocator.TempJob);
            var traps = new NativeList<Trapezoid>(64, Allocator.TempJob);
            VectraGeometry.StrokeAndTessellate(input, sp, d, default, default, FillRule.NonZero, 0, ref traps);
            double area = ScanlineTessellator.Area(traps.AsArray());
            traps.Dispose();
            d.Dispose();
            return area;
        }

        [Test]
        public void Stroke_Caps()
        {
            var p = new Polylines(Allocator.TempJob);
            p.BeginContour();
            p.AddPoint(new float2(0, 0));
            p.AddPoint(new float2(10, 0));
            p.EndContour(false);
            var sp = StrokeParams.Default(2f);
            sp.tolerance = 0.0005f;
            Assert.AreEqual(20.0, StrokeArea(p, sp), AreaTolerance);
            sp.cap = LineCap.Square;
            Assert.AreEqual(24.0, StrokeArea(p, sp), AreaTolerance);
            sp.cap = LineCap.Round;
            Assert.AreEqual(20.0 + math.PI, StrokeArea(p, sp), 0.01);
            sp.cap = LineCap.Butt;
            Assert.AreEqual(10.0, StrokeArea(p, sp, new[] { 1f, 1f }), AreaTolerance);
            sp.trimStart = 0.25f;
            sp.trimEnd = 0.75f;
            Assert.AreEqual(10.0, StrokeArea(p, sp), AreaTolerance);
            p.Dispose();
        }

        [Test]
        public void Stroke_ClosedSquareJoins()
        {
            var p = new Polylines(Allocator.TempJob);
            AddRect(ref p, 0, 0, 10, 10);
            var sp = StrokeParams.Default(2f);
            Assert.AreEqual(12.0 * 12.0 - 8.0 * 8.0, StrokeArea(p, sp), AreaTolerance);
            sp.join = LineJoin.Bevel;
            Assert.AreEqual(12.0 * 12.0 - 8.0 * 8.0 - 4 * 0.5, StrokeArea(p, sp), AreaTolerance);
            p.Dispose();
        }

        [Test]
        public void Contours_RebuildPreservesArea()
        {
            var p = new Polylines(Allocator.TempJob);
            AddRect(ref p, 0, 0, 1, 1, 0);
            AddRect(ref p, 0.5f, 0.5f, 1, 1, 0);
            var traps = new NativeList<Trapezoid>(64, Allocator.TempJob);
            VectraGeometry.Tessellate(p, FillRule.NonZero, ref traps);
            var rebuilt = new Polylines(Allocator.TempJob);
            VectraGeometry.BuildContours(traps.AsArray(), ref rebuilt);
            Assert.AreEqual(1, rebuilt.contours.Length);
            Assert.AreEqual(8, rebuilt.contours[0].count);
            Assert.AreEqual(1.75, Fill(rebuilt, FillRule.NonZero, out _), AreaTolerance);
            rebuilt.Dispose();
            traps.Dispose();
            p.Dispose();
        }
    }
}
