using Unity.Collections;
using Unity.Mathematics;

namespace FirstSymbol.Vectra.Geometry
{
    /// <summary>
    /// Bakes an exact signed distance field (negative inside) of a tessellated area into a half-float texel block.
    /// Distances are normalized by <c>range</c> and clamped to [-1, 1].
    /// </summary>
    public static class SdfBaker
    {
        const int CellSize = 8;

        /// <param name="region">Area of local space covered by the texel block (texel centers are inset by half a texel).</param>
        /// <param name="dst">Destination texels; the block is written at (<paramref name="dstX"/>, <paramref name="dstY"/>).</param>
        public static void Bake(NativeArray<Trapezoid> traps, Bounds2 region, int width, int height, float range,
            NativeArray<half> dst, int dstStride, int dstX, int dstY)
        {
            if (width <= 0 || height <= 0) return;
            range = math.max(range, 1e-6f);
            float2 size = math.max(region.Size, new float2(1e-6f));
            float2 texel = size / new float2(width, height);
            float2 origin = region.min + texel * 0.5f;

            var inside = new NativeArray<byte>(width * height, Allocator.Temp, NativeArrayOptions.ClearMemory);
            Rasterize(traps, origin, texel, width, height, ref inside);

            var segments = new NativeList<Segment>(traps.Length * 2, Allocator.Temp);
            ScanlineTessellator.BoundarySegments(traps, ref segments);

            int cw = (width + CellSize - 1) / CellSize;
            int ch = (height + CellSize - 1) / CellSize;
            var cellStart = new NativeArray<int>(cw * ch + 1, Allocator.Temp, NativeArrayOptions.ClearMemory);
            float2 cellSizeLocal = texel * CellSize;

            // Pass 1: count segments per cell (segment bounds grown by the range).
            for (int s = 0; s < segments.Length; s++)
            {
                GetCellRange(segments[s], range, region.min, cellSizeLocal, cw, ch, out int4 r);
                for (int cy = r.y; cy <= r.w; cy++)
                for (int cx = r.x; cx <= r.z; cx++)
                    cellStart[cy * cw + cx + 1]++;
            }

            for (int i = 1; i < cellStart.Length; i++) cellStart[i] += cellStart[i - 1];
            var cellItems = new NativeArray<int>(math.max(cellStart[cellStart.Length - 1], 1), Allocator.Temp);
            var fill = new NativeArray<int>(cw * ch, Allocator.Temp, NativeArrayOptions.ClearMemory);
            for (int s = 0; s < segments.Length; s++)
            {
                GetCellRange(segments[s], range, region.min, cellSizeLocal, cw, ch, out int4 r);
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
                        best = math.min(best, DistanceSq(p, sg.a, sg.b));
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

        static void GetCellRange(in Segment s, float range, float2 regionMin, float2 cellSize, int cw, int ch, out int4 r)
        {
            float2 mn = math.min(s.a, s.b) - range - regionMin;
            float2 mx = math.max(s.a, s.b) + range - regionMin;
            int2 a = (int2)math.floor(mn / cellSize);
            int2 b = (int2)math.floor(mx / cellSize);
            a = math.clamp(a, 0, new int2(cw - 1, ch - 1));
            b = math.clamp(b, 0, new int2(cw - 1, ch - 1));
            r = new int4(a.x, a.y, b.x, b.y);
        }

        public static float DistanceSq(float2 p, float2 a, float2 b)
        {
            float2 ab = b - a;
            float2 ap = p - a;
            float l2 = math.dot(ab, ab);
            float t = l2 > 0f ? math.saturate(math.dot(ap, ab) / l2) : 0f;
            float2 q = ap - ab * t;
            return math.dot(q, q);
        }

        /// <summary>Marks texels whose centers are inside the area.</summary>
        public static void Rasterize(NativeArray<Trapezoid> traps, float2 origin, float2 texel, int width, int height,
            ref NativeArray<byte> mask)
        {
            for (int i = 0; i < traps.Length; i++)
            {
                var t = traps[i];
                int y0 = (int)math.ceil((t.yb - origin.y) / texel.y);
                int y1 = (int)math.ceil((t.yt - origin.y) / texel.y) - 1;
                y0 = math.max(y0, 0);
                y1 = math.min(y1, height - 1);
                float h = math.max(t.yt - t.yb, 1e-20f);
                for (int y = y0; y <= y1; y++)
                {
                    float py = origin.y + y * texel.y;
                    float f = (py - t.yb) / h;
                    float xl = math.lerp(t.xlb, t.xlt, f);
                    float xr = math.lerp(t.xrb, t.xrt, f);
                    int x0 = math.max((int)math.ceil((xl - origin.x) / texel.x), 0);
                    int x1 = math.min((int)math.ceil((xr - origin.x) / texel.x) - 1, width - 1);
                    for (int x = x0; x <= x1; x++) mask[y * width + x] = 1;
                }
            }
        }
    }
}
