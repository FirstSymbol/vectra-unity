using FirstSymbol.Vectra.Geometry;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace FirstSymbol.Vectra.Editor.Tests
{
    /// <summary>
    /// The distance-field baker and the smooth boolean combine run vectorized Burst code; these tests hold them to the arithmetic of the plain
    /// scalar loops they replaced, texel for texel (the half-float bits are compared, not a tolerance). The old loops run as Burst jobs here, the
    /// way they ran in the package (Mono would evaluate float expressions at a higher precision and is not the reference).
    /// </summary>
    public class SdfBitIdentityTests
    {
        [BurstCompile(FloatMode = FloatMode.Strict)]
        struct ReferenceBakeJob : IJob
        {
            [ReadOnly] public NativeArray<Trapezoid> traps;
            public Bounds2 region;
            public int width, height, stride, x, y;
            public float range;
            [NativeDisableContainerSafetyRestriction] public NativeArray<half> dst;

            public void Execute() => ReferenceBake(traps, region, width, height, range, dst, stride, x, y);
        }

        [BurstCompile(FloatMode = FloatMode.Strict)]
        struct ReferenceCombineJob : IJob
        {
            public NativeArray<half> acc;
            [ReadOnly] public NativeArray<half> tmp;
            public BooleanOp op;
            public float kn;

            public void Execute() => ReferenceCombine(acc, tmp, op, kn);
        }

        // ---- the scalar baker as it was before 0.8 ----
        static void ReferenceBake(NativeArray<Trapezoid> traps, Bounds2 region, int width, int height, float range, NativeArray<half> dst, int dstStride, int dstX, int dstY)
        {
            const int CellSize = 8;
            if (width <= 0 || height <= 0) return;
            range = math.max(range, 1e-6f);
            float2 size = math.max(region.Size, new float2(1e-6f));
            float2 texel = size / new float2(width, height);
            float2 origin = region.min + texel * 0.5f;
            var inside = new NativeArray<byte>(width * height, Allocator.Temp, NativeArrayOptions.ClearMemory);
            SdfBaker.Rasterize(traps, origin, texel, width, height, ref inside);
            var segments = new NativeList<Segment>(traps.Length * 2, Allocator.Temp);
            ScanlineTessellator.BoundarySegments(traps, ref segments);
            int cw = (width + CellSize - 1) / CellSize;
            int ch = (height + CellSize - 1) / CellSize;
            var cellStart = new NativeArray<int>(cw * ch + 1, Allocator.Temp, NativeArrayOptions.ClearMemory);
            float2 cellSizeLocal = texel * CellSize;
            for (int s = 0; s < segments.Length; s++)
            {
                Cells(segments[s], range, region.min, cellSizeLocal, cw, ch, out int4 r);
                for (int cy = r.y; cy <= r.w; cy++)
                for (int cx = r.x; cx <= r.z; cx++)
                    cellStart[cy * cw + cx + 1]++;
            }

            for (int i = 1; i < cellStart.Length; i++) cellStart[i] += cellStart[i - 1];
            var cellItems = new NativeArray<int>(math.max(cellStart[cellStart.Length - 1], 1), Allocator.Temp);
            var fill = new NativeArray<int>(cw * ch, Allocator.Temp, NativeArrayOptions.ClearMemory);
            for (int s = 0; s < segments.Length; s++)
            {
                Cells(segments[s], range, region.min, cellSizeLocal, cw, ch, out int4 r);
                for (int cy = r.y; cy <= r.w; cy++)
                for (int cx = r.x; cx <= r.z; cx++)
                {
                    int cell = cy * cw + cx;
                    cellItems[cellStart[cell] + fill[cell]++] = s;
                }
            }

            float range2 = range * range;
            float invRange = 1f / range;
            for (int y = 0; y < height; y++)
            {
                int cy = y / CellSize;
                for (int x = 0; x < width; x++)
                {
                    int cell = cy * cw + x / CellSize;
                    float2 p = origin + texel * new float2(x, y);
                    float best = range2;
                    for (int k = cellStart[cell]; k < cellStart[cell + 1]; k++)
                    {
                        var sg = segments[cellItems[k]];
                        best = math.min(best, SdfBaker.DistanceSq(p, sg.a, sg.b));
                    }

                    float d = math.sqrt(best) * invRange;
                    if (inside[y * width + x] != 0) d = -d;
                    dst[(dstY + y) * dstStride + dstX + x] = (half)math.clamp(d, -1f, 1f);
                }
            }

            inside.Dispose();
            segments.Dispose();
            cellStart.Dispose();
            cellItems.Dispose();
            fill.Dispose();
        }

        static void Cells(in Segment s, float range, float2 regionMin, float2 cellSize, int cw, int ch, out int4 r)
        {
            float2 mn = math.min(s.a, s.b) - range - regionMin;
            float2 mx = math.max(s.a, s.b) + range - regionMin;
            int2 a = (int2)math.floor(mn / cellSize);
            int2 b = (int2)math.floor(mx / cellSize);
            a = math.clamp(a, 0, new int2(cw - 1, ch - 1));
            b = math.clamp(b, 0, new int2(cw - 1, ch - 1));
            r = new int4(a.x, a.y, b.x, b.y);
        }

        // ---- test shapes ----
        static void Blob(ref Polylines p, ref Unity.Mathematics.Random rng, int operand, float2 center, float radius, int points)
        {
            p.BeginContour(operand);
            for (int i = 0; i < points; i++)
            {
                float a = i * math.PI * 2f / points;
                float r = radius * rng.NextFloat(0.35f, 1f);
                p.AddPoint(center + new float2(math.cos(a), math.sin(a)) * r);
            }

            p.EndContour(true);
        }

        static NativeList<Trapezoid> Shape(uint seed, FillRule rule, out Bounds2 bounds)
        {
            var rng = new Unity.Mathematics.Random(seed);
            var p = new Polylines(Allocator.TempJob, 64);
            int contours = rng.NextInt(1, 4);
            for (int c = 0; c < contours; c++) Blob(ref p, ref rng, 0, rng.NextFloat2(new float2(-10f), new float2(10f)), rng.NextFloat(3f, 30f), rng.NextInt(3, 40));
            var traps = new NativeList<Trapezoid>(64, Allocator.TempJob);
            VectraGeometry.Tessellate(p, rule, ref traps);
            p.Dispose();
            bounds = VectraGeometry.Bounds(traps.AsArray());
            return traps;
        }

        [Test]
        public void BakeMatchesTheScalarLoopTexelForTexel()
        {
            int shapes = 0, texels = 0;
            double refMs = 0, newMs = 0;
            for (uint seed = 1; seed <= 120; seed++)
            {
                var rng = new Unity.Mathematics.Random(seed * 7919u);
                var traps = Shape(seed, seed % 3 == 0 ? FillRule.EvenOdd : FillRule.NonZero, out var bounds);
                if (!bounds.IsValid || traps.Length == 0)
                {
                    traps.Dispose();
                    continue;
                }

                float range = rng.NextFloat(0.5f, 14f);
                var region = bounds.Expanded(range);
                // sizes that are not multiples of the cell: narrow, odd and wide rows
                int w = rng.NextInt(2, 200), h = rng.NextInt(2, 120);
                int stride = w + rng.NextInt(0, 9), ox = rng.NextInt(0, 5), oy = rng.NextInt(0, 4);
                var a = new NativeArray<half>(stride * (h + oy + 1), Allocator.TempJob);
                var b = new NativeArray<half>(a.Length, Allocator.TempJob);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                new ReferenceBakeJob { traps = traps.AsArray(), region = region, width = w, height = h, range = range, dst = a, stride = stride, x = ox, y = oy }.Run();
                refMs += sw.Elapsed.TotalMilliseconds;
                sw.Restart();
                VectraGeometry.BakeSdf(traps.AsArray(), region, w, h, range, b, stride, ox, oy);
                newMs += sw.Elapsed.TotalMilliseconds;
                for (int i = 0; i < a.Length; i++)
                    Assert.AreEqual(a[i].value, b[i].value, $"seed {seed} {w}x{h} stride {stride} off {ox},{oy} texel {i} (x {i % stride - ox} y {i / stride - oy}): scalar {(float)a[i]} vectorized {(float)b[i]}");
                shapes++;
                texels += w * h;
                a.Dispose();
                b.Dispose();
                traps.Dispose();
            }

            Assert.Greater(shapes, 100);
            UnityEngine.Debug.Log($"SDFBENCH {shapes} shapes, {texels} texels identical; scalar {refMs:F1} ms, vectorized {newMs:F1} ms");
        }

        // ---- the combine ----
        static float SMin(float a, float b, float k)
        {
            float h = math.max(k - math.abs(a - b), 0f) / k;
            return math.min(a, b) - h * h * k * 0.25f;
        }

        static void ReferenceCombine(NativeArray<half> acc, NativeArray<half> tmp, BooleanOp op, float kn)
        {
            for (int i = 0; i < acc.Length; i++)
            {
                float a = acc[i], b = tmp[i];
                float r;
                switch (op)
                {
                    case BooleanOp.Subtract: r = -SMin(-a, b, kn); break;
                    case BooleanOp.Intersect: r = -SMin(-a, -b, kn); break;
                    case BooleanOp.Exclude: r = math.max(math.min(a, b), -math.max(a, b)); break;
                    default: r = SMin(a, b, kn); break;
                }

                acc[i] = (half)math.clamp(r, -1f, 1f);
            }
        }

        [Test]
        public void CombineMatchesTheScalarLoopBitForBit([Values(BooleanOp.Union, BooleanOp.Subtract, BooleanOp.Intersect, BooleanOp.Exclude)] BooleanOp op)
        {
            var rng = new Unity.Mathematics.Random(12345u + (uint)op);
            const int n = 70001;
            var a = new NativeArray<half>(n, Allocator.TempJob);
            var b = new NativeArray<half>(n, Allocator.TempJob);
            for (int i = 0; i < n; i++)
            {
                // fields in [-1, 1], with exact zeros, signed zeros, the extremes, half subnormals and values that make ties between two halves
                switch (i % 11)
                {
                    case 0: a[i] = (half)0f; b[i] = (half)(-0f); break;
                    case 1: a[i] = (half)1f; b[i] = (half)(-1f); break;
                    case 2: a[i] = new half { value = (ushort)rng.NextInt(1, 0x400) }; b[i] = new half { value = (ushort)(0x8000 | rng.NextInt(1, 0x400)) }; break;
                    case 3: a[i] = (half)rng.NextFloat(-1f, 1f); b[i] = a[i]; break;
                    default: a[i] = (half)rng.NextFloat(-1f, 1f); b[i] = (half)rng.NextFloat(-1f, 1f); break;
                }
            }

            foreach (float kn in new[] { 0.01f, 0.05f, 0.1f, 0.3f, 0.99f })
            {
                var x = new NativeArray<half>(a, Allocator.TempJob);
                var y = new NativeArray<half>(a, Allocator.TempJob);
                new ReferenceCombineJob { acc = x, tmp = b, op = op, kn = kn }.Run();
                SdfCombine.Run(y, b, op, kn);
                for (int i = 0; i < n; i++) Assert.AreEqual(x[i].value, y[i].value, $"{op} k={kn} at {i}: a {(float)a[i]} b {(float)b[i]}");
                x.Dispose();
                y.Dispose();
            }
        }
    }
}
