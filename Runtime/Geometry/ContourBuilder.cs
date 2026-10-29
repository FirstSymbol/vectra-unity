using Unity.Collections;
using Unity.Mathematics;

namespace FirstSymbol.Vectra.Geometry
{
    /// <summary>
    /// Rebuilds closed outline contours from tessellated trapezoids. Outer contours come out counter-clockwise and
    /// holes clockwise (the filled area is always on the left), so the result can be filled again with any rule.
    /// </summary>
    public static class ContourBuilder
    {
        public static void Build(NativeArray<Trapezoid> traps, ref Polylines output, int operand = 0)
        {
            var segs = new NativeList<Segment>(traps.Length * 2, Allocator.Temp);
            var b = Bounds2.Empty;
            for (int i = 0; i < traps.Length; i++)
            {
                var t = traps[i];
                b.Encapsulate(t.BottomLeft);
                b.Encapsulate(t.TopRight);
                b.Encapsulate(t.TopLeft);
                b.Encapsulate(t.BottomRight);
                if ((t.flags & TrapezoidEdges.Left) != 0) Add(ref segs, t.TopLeft, t.BottomLeft);
                if ((t.flags & TrapezoidEdges.Bottom) != 0) Add(ref segs, t.BottomLeft, t.BottomRight);
                if ((t.flags & TrapezoidEdges.Right) != 0) Add(ref segs, t.BottomRight, t.TopRight);
                if ((t.flags & TrapezoidEdges.Top) != 0) Add(ref segs, t.TopRight, t.TopLeft);
            }

            if (segs.Length == 0)
            {
                segs.Dispose();
                return;
            }

            float extent = math.max(math.cmax(b.Size), 1e-6f);
            float tol = extent * 2e-6f;
            float cell = tol * 4f;
            var map = new NativeParallelMultiHashMap<long, int>(segs.Length, Allocator.Temp);
            for (int i = 0; i < segs.Length; i++) map.Add(Key(Cell(segs[i].a, cell)), i);

            var used = new NativeArray<bool>(segs.Length, Allocator.Temp, NativeArrayOptions.ClearMemory);
            var loop = new NativeList<float2>(64, Allocator.Temp);

            for (int s0 = 0; s0 < segs.Length; s0++)
            {
                if (used[s0]) continue;
                loop.Clear();
                int cur = s0;
                used[cur] = true;
                float2 start = segs[cur].a;
                loop.Add(start);
                int guard = 0;
                while (guard++ < segs.Length + 1)
                {
                    var sg = segs[cur];
                    float2 end = sg.b;
                    if (math.distancesq(end, start) <= tol * tol && loop.Length > 2) break;
                    loop.Add(end);
                    int next = FindNext(map, segs, used, end, sg.b - sg.a, cell, tol);
                    if (next < 0) break;
                    used[next] = true;
                    cur = next;
                }

                Simplify(ref loop, tol);
                if (loop.Length < 3) continue;
                output.BeginContour(operand);
                for (int i = 0; i < loop.Length; i++) output.AddPoint(loop[i]);
                output.EndContour(true);
            }

            segs.Dispose();
            map.Dispose();
            used.Dispose();
            loop.Dispose();
        }

        static void Add(ref NativeList<Segment> segs, float2 a, float2 b)
        {
            if (math.all(a == b)) return;
            segs.Add(new Segment(a, b));
        }

        static int2 Cell(float2 p, float cell) => (int2)math.floor(p / cell);
        static long Key(int2 c) => ((long)c.x << 32) ^ (uint)c.y;

        static int FindNext(NativeParallelMultiHashMap<long, int> map, NativeList<Segment> segs, NativeArray<bool> used,
            float2 p, float2 incoming, float cell, float tol)
        {
            int2 c = Cell(p, cell);
            int best = -1;
            float bestScore = float.MaxValue;
            float2 din = math.normalizesafe(incoming);
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (!map.TryGetFirstValue(Key(c + new int2(dx, dy)), out int idx, out var it)) continue;
                do
                {
                    if (used[idx]) continue;
                    if (math.distancesq(segs[idx].a, p) > tol * tol * 4f) continue;
                    // Prefer the sharpest right turn so touching loops are separated consistently.
                    float2 dout = math.normalizesafe(segs[idx].b - segs[idx].a);
                    float cross = din.x * dout.y - din.y * dout.x;
                    float dot = math.dot(din, dout);
                    float angle = math.atan2(cross, dot);
                    if (angle < bestScore)
                    {
                        bestScore = angle;
                        best = idx;
                    }
                } while (map.TryGetNextValue(out idx, ref it));
            }

            return best;
        }

        /// <summary>Removes duplicate and collinear points of a closed loop.</summary>
        public static void Simplify(ref NativeList<float2> loop, float tol)
        {
            if (loop.Length < 3) return;
            int w = 0;
            for (int i = 0; i < loop.Length; i++)
            {
                if (w > 0 && math.distancesq(loop[w - 1], loop[i]) <= tol * tol) continue;
                loop[w++] = loop[i];
            }

            while (w > 1 && math.distancesq(loop[0], loop[w - 1]) <= tol * tol) w--;
            loop.ResizeUninitialized(w);

            bool changed = true;
            int guard = 0;
            while (changed && loop.Length >= 3 && guard++ < 8)
            {
                changed = false;
                w = 0;
                int n = loop.Length;
                for (int i = 0; i < n; i++)
                {
                    float2 prev = w > 0 ? loop[w - 1] : loop[n - 1];
                    float2 cur = loop[i];
                    float2 next = loop[(i + 1) % n];
                    float2 a = cur - prev, b2 = next - cur;
                    float cross = a.x * b2.y - a.y * b2.x;
                    float len = math.length(a) + math.length(b2);
                    if (math.abs(cross) <= tol * len && math.dot(a, b2) >= 0f)
                    {
                        changed = true;
                        continue;
                    }

                    loop[w++] = cur;
                }

                loop.ResizeUninitialized(w);
            }
        }
    }
}
