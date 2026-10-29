using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;

namespace FirstSymbol.Vectra.Geometry
{
    /// <summary>
    /// Robust scanline tessellator. Converts closed polylines into horizontal-band trapezoids that exactly cover the
    /// filled area. Handles self-intersections, every <see cref="FillRule"/> and chained boolean operations between any
    /// number of operands in one sweep. Each output trapezoid marks which of its sides lie on the true boundary so
    /// the shader can anti-alias them analytically without seams between neighbouring trapezoids.
    /// </summary>
    public static class ScanlineTessellator
    {
        struct Edge
        {
            public double x0, y0, x1, y1, dxdy;
            public int wind;
            public int operand;

            public double XAt(double y)
            {
                if (y <= y0) return x0;
                if (y >= y1) return x1;
                return x0 + (y - y0) * dxdy;
            }
        }

        struct EdgeStartComparer : IComparer<Edge>
        {
            public int Compare(Edge a, Edge b) => a.y0.CompareTo(b.y0);
        }

        struct OpenTrap
        {
            public int l, r;
            public double yb;
            public bool bottomBoundary;
        }

        struct SlabTrap
        {
            public int l, r;
        }

        /// <summary>
        /// Tessellates <paramref name="contours"/> (all treated as closed). When <paramref name="operands"/> is empty a
        /// single non-zero operand is assumed and every contour's operand index is ignored.
        /// </summary>
        public static void Tessellate(NativeArray<float2> points, NativeArray<Contour> contours,
            NativeArray<Operand> operands, ref NativeList<Trapezoid> output)
        {
            int operandCount = math.max(1, operands.Length);
            var edges = new NativeList<Edge>(points.Length + 8, Allocator.Temp);
            double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;

            for (int ci = 0; ci < contours.Length; ci++)
            {
                var c = contours[ci];
                if (c.count < 2) continue;
                int operand = operands.Length == 0 ? 0 : math.clamp(c.operand, 0, operandCount - 1);
                for (int i = 0; i < c.count; i++)
                {
                    float2 a = points[c.start + i];
                    float2 b = points[c.start + (i + 1) % c.count];
                    if (!math.all(math.isfinite(a)) || !math.all(math.isfinite(b))) continue;
                    minX = math.min(minX, a.x);
                    maxX = math.max(maxX, a.x);
                    minY = math.min(minY, a.y);
                    maxY = math.max(maxY, a.y);
                    if (a.y == b.y) continue;
                    var e = new Edge { operand = operand };
                    if (a.y < b.y)
                    {
                        e.x0 = a.x; e.y0 = a.y; e.x1 = b.x; e.y1 = b.y; e.wind = -1;
                    }
                    else
                    {
                        e.x0 = b.x; e.y0 = b.y; e.x1 = a.x; e.y1 = a.y; e.wind = 1;
                    }

                    e.dxdy = (e.x1 - e.x0) / (e.y1 - e.y0);
                    edges.Add(e);
                }
            }

            if (edges.Length < 2)
            {
                edges.Dispose();
                return;
            }

            double extent = math.max(math.max(maxX - minX, maxY - minY), 1e-6);
            double epsX = extent * 1e-9;
            double epsY = extent * 1e-9;

            edges.Sort(new EdgeStartComparer());

            var ys = new NativeList<double>(edges.Length * 2, Allocator.Temp);
            for (int i = 0; i < edges.Length; i++)
            {
                ys.Add(edges[i].y0);
                ys.Add(edges[i].y1);
            }

            ys.Sort();
            int unique = 0;
            for (int i = 0; i < ys.Length; i++)
                if (unique == 0 || ys[i] != ys[unique - 1])
                    ys[unique++] = ys[i];
            ys.ResizeUninitialized(unique);

            var active = new NativeList<int>(64, Allocator.Temp);
            var xs0 = new NativeList<double>(64, Allocator.Temp);
            var xs1 = new NativeList<double>(64, Allocator.Temp);
            var open = new NativeList<OpenTrap>(32, Allocator.Temp);
            var nextOpen = new NativeList<OpenTrap>(32, Allocator.Temp);
            var slab = new NativeList<SlabTrap>(32, Allocator.Temp);
            var openMatched = new NativeList<bool>(32, Allocator.Temp);
            var prevIntervals = new NativeList<double2>(32, Allocator.Temp);
            var curIntervals = new NativeList<double2>(32, Allocator.Temp);
            var uncovered = new NativeList<double2>(8, Allocator.Temp);
            var leftOwner = new NativeArray<int>(edges.Length, Allocator.Temp);
            for (int i = 0; i < leftOwner.Length; i++) leftOwner[i] = -1;
            var windings = new NativeArray<int>(operandCount, Allocator.Temp);

            int ei = 0, yi = 0;
            double y = ys[0];
            int guard = 0;
            int guardLimit = edges.Length * 64 + 1024;

            while (guard++ < guardLimit)
            {
                while (ei < edges.Length && edges[ei].y0 <= y) active.Add(ei++);

                int w = 0;
                for (int k = 0; k < active.Length; k++)
                    if (edges[active[k]].y1 > y)
                        active[w++] = active[k];
                active.ResizeUninitialized(w);

                while (yi < ys.Length && ys[yi] <= y) yi++;

                if (active.Length == 0)
                {
                    for (int i = 0; i < open.Length; i++)
                        Close(edges, open[i], y, true, default, epsX, ref output);
                    open.Clear();
                    if (ei >= edges.Length || yi >= ys.Length) break;
                    y = edges[ei].y0;
                    continue;
                }

                if (yi >= ys.Length) break;
                double yNext = ys[yi];

                // Order the active edges by x at the slab bottom, then at the slab top (insertion sort: the order
                // changes little from one slab to the next).
                int m = active.Length;
                xs0.ResizeUninitialized(m);
                xs1.ResizeUninitialized(m);
                for (int k = 0; k < m; k++)
                {
                    var e = edges[active[k]];
                    xs0[k] = e.XAt(y);
                    xs1[k] = e.XAt(yNext);
                }

                for (int k = 1; k < m; k++)
                {
                    int id = active[k];
                    double a0 = xs0[k], a1 = xs1[k];
                    int wind = edges[id].wind;
                    int j = k - 1;
                    while (j >= 0 && Less(a0, a1, wind, xs0[j], xs1[j], edges[active[j]].wind, epsX))
                    {
                        active[j + 1] = active[j];
                        xs0[j + 1] = xs0[j];
                        xs1[j + 1] = xs1[j];
                        j--;
                    }

                    active[j + 1] = id;
                    xs0[j + 1] = a0;
                    xs1[j + 1] = a1;
                }

                // The first crossing between neighbours bounds the slab so that the order stays valid inside it.
                double yTop = yNext;
                for (int k = 0; k < m - 1; k++)
                {
                    if (xs1[k] - xs1[k + 1] <= epsX) continue;
                    var ea = edges[active[k]];
                    var eb = edges[active[k + 1]];
                    double denom = ea.dxdy - eb.dxdy;
                    if (denom <= 0) continue;
                    double yc = y + (xs0[k + 1] - xs0[k]) / denom;
                    if (yc < yTop) yTop = yc;
                }

                if (yTop <= y + epsY) yTop = math.min(yNext, y + epsY);

                // Walk the slab left to right, accumulating winding per operand.
                slab.Clear();
                for (int i = 0; i < operandCount; i++) windings[i] = 0;
                bool inside = false;
                int leftK = -1;
                for (int k = 0; k < m; k++)
                {
                    var e = edges[active[k]];
                    windings[e.operand] += e.wind;
                    bool now = Evaluate(windings, operands);
                    if (now == inside) continue;
                    if (now)
                    {
                        leftK = k;
                    }
                    else if (leftK >= 0)
                    {
                        int l = active[leftK], r = active[k];
                        var el = edges[l];
                        var er = edges[r];
                        double lb = el.XAt(y), lt = el.XAt(yTop), rb = er.XAt(y), rt = er.XAt(yTop);
                        if (rb - lb > epsX || rt - lt > epsX)
                        {
                            // Glue to the previous trapezoid when they touch along their whole height (shared edges).
                            if (slab.Length > 0)
                            {
                                var prev = slab[slab.Length - 1];
                                var ep = edges[prev.r];
                                if (math.abs(ep.XAt(y) - lb) <= epsX && math.abs(ep.XAt(yTop) - lt) <= epsX)
                                {
                                    prev.r = r;
                                    slab[slab.Length - 1] = prev;
                                    inside = now;
                                    continue;
                                }
                            }

                            slab.Add(new SlabTrap { l = l, r = r });
                        }
                    }

                    inside = now;
                }

                // Match the slab against the trapezoids that are still open from the slab below.
                prevIntervals.Clear();
                for (int i = 0; i < open.Length; i++)
                {
                    var o = open[i];
                    prevIntervals.Add(new double2(edges[o.l].XAt(y), edges[o.r].XAt(y)));
                    leftOwner[o.l] = i;
                }

                curIntervals.Clear();
                for (int j = 0; j < slab.Length; j++)
                    curIntervals.Add(new double2(edges[slab[j].l].XAt(y), edges[slab[j].r].XAt(y)));

                openMatched.Clear();
                openMatched.Resize(open.Length, NativeArrayOptions.ClearMemory);
                nextOpen.Clear();
                int prevCursor = 0;
                for (int j = 0; j < slab.Length; j++)
                {
                    var s = slab[j];
                    int owner = leftOwner[s.l];
                    if (owner >= 0 && open[owner].r == s.r)
                    {
                        openMatched[owner] = true;
                        nextOpen.Add(open[owner]);
                        continue;
                    }

                    // New trapezoid: its bottom is boundary wherever the slab below was not filled.
                    double a = curIntervals[j].x, b = curIntervals[j].y;
                    Subtract(a, b, prevIntervals, ref prevCursor, epsX, ref uncovered);
                    var opened = new OpenTrap { l = s.l, r = s.r, yb = y, bottomBoundary = false };
                    if (uncovered.Length == 0)
                    {
                        opened.bottomBoundary = false;
                    }
                    else if (uncovered.Length == 1 && uncovered[0].x <= a + epsX && uncovered[0].y >= b - epsX)
                    {
                        opened.bottomBoundary = true;
                    }
                    else
                    {
                        double h = SplitHeight(edges[s.l], edges[s.r], a, b, uncovered, 0.5 * (yTop - y), false, epsX);
                        EmitBand(edges[s.l], edges[s.r], y, y + h, a, b, uncovered, false, epsX, ref output);
                        opened.yb = y + h;
                        opened.bottomBoundary = false;
                    }

                    nextOpen.Add(opened);
                }

                int curCursor = 0;
                for (int i = 0; i < open.Length; i++)
                {
                    leftOwner[open[i].l] = -1;
                    if (openMatched[i]) continue;
                    var o = open[i];
                    double a = prevIntervals[i].x, b = prevIntervals[i].y;
                    Subtract(a, b, curIntervals, ref curCursor, epsX, ref uncovered);
                    Close(edges, o, y, false, uncovered, epsX, ref output);
                }

                var swap = open;
                open = nextOpen;
                nextOpen = swap;
                y = yTop;
            }

            for (int i = 0; i < open.Length; i++)
                Close(edges, open[i], y, true, default, epsX, ref output);

            edges.Dispose();
            ys.Dispose();
            active.Dispose();
            xs0.Dispose();
            xs1.Dispose();
            open.Dispose();
            nextOpen.Dispose();
            slab.Dispose();
            openMatched.Dispose();
            prevIntervals.Dispose();
            curIntervals.Dispose();
            uncovered.Dispose();
            leftOwner.Dispose();
            windings.Dispose();
        }

        static bool Less(double a0, double a1, int aw, double b0, double b1, int bw, double eps)
        {
            if (a0 < b0 - eps) return true;
            if (a0 > b0 + eps) return false;
            if (a1 < b1 - eps) return true;
            if (a1 > b1 + eps) return false;
            // Coincident edges: entering edges first, which keeps shared edges of unions seamless.
            return aw > bw;
        }

        static bool Inside(FillRule rule, int w)
        {
            switch (rule)
            {
                case FillRule.EvenOdd: return (w & 1) != 0;
                case FillRule.Positive: return w > 0;
                case FillRule.Negative: return w < 0;
                default: return w != 0;
            }
        }

        static bool Evaluate(NativeArray<int> windings, NativeArray<Operand> operands)
        {
            if (operands.Length == 0) return windings[0] != 0;
            bool r = Inside(operands[0].rule, windings[0]);
            for (int i = 1; i < operands.Length; i++)
            {
                bool b = Inside(operands[i].rule, windings[i]);
                switch (operands[i].op)
                {
                    case BooleanOp.Union: r |= b; break;
                    case BooleanOp.Subtract: r &= !b; break;
                    case BooleanOp.Intersect: r &= b; break;
                    case BooleanOp.Exclude: r ^= b; break;
                }
            }

            return r;
        }

        /// <summary>
        /// Parts of [a, b] not covered by the sorted, disjoint <paramref name="intervals"/>. A zero-width [a, b] (a
        /// spike tip) is reported as uncovered when no interval touches it.
        /// </summary>
        static void Subtract(double a, double b, NativeList<double2> intervals, ref int cursor, double eps,
            ref NativeList<double2> result)
        {
            result.Clear();
            while (cursor < intervals.Length && intervals[cursor].y < a - eps) cursor++;

            if (b - a <= eps)
            {
                for (int i = cursor; i < intervals.Length; i++)
                {
                    var iv = intervals[i];
                    if (iv.x > b + eps) break;
                    if (iv.x <= a + eps && iv.y >= b - eps) return;
                }

                result.Add(new double2(a, b));
                return;
            }

            double pos = a;
            for (int i = cursor; i < intervals.Length; i++)
            {
                var iv = intervals[i];
                if (iv.x >= b - eps) break;
                if (iv.x > pos + eps) result.Add(new double2(pos, iv.x));
                pos = math.max(pos, iv.y);
                if (pos >= b - eps) break;
            }

            if (pos < b - eps) result.Add(new double2(pos, b));
        }

        static void Close(NativeList<Edge> edges, OpenTrap o, double yTop, bool fullTop, NativeList<double2> uncovered,
            double eps, ref NativeList<Trapezoid> output)
        {
            var el = edges[o.l];
            var er = edges[o.r];
            if (yTop <= o.yb) return;
            var flags = TrapezoidEdges.Left | TrapezoidEdges.Right;
            if (o.bottomBoundary) flags |= TrapezoidEdges.Bottom;

            if (fullTop)
            {
                Emit(el, er, o.yb, yTop, flags | TrapezoidEdges.Top, ref output);
                return;
            }

            double a = el.XAt(yTop), b = er.XAt(yTop);
            if (uncovered.Length == 0)
            {
                Emit(el, er, o.yb, yTop, flags, ref output);
                return;
            }

            if (uncovered.Length == 1 && uncovered[0].x <= a + eps && uncovered[0].y >= b - eps)
            {
                Emit(el, er, o.yb, yTop, flags | TrapezoidEdges.Top, ref output);
                return;
            }

            double h = SplitHeight(el, er, a, b, uncovered, 0.5 * (yTop - o.yb), true, eps);
            Emit(el, er, o.yb, yTop - h, flags, ref output);
            EmitBand(el, er, yTop - h, yTop, a, b, uncovered, true, eps, ref output);
        }

        static void Emit(in Edge el, in Edge er, double yb, double yt, TrapezoidEdges flags, ref NativeList<Trapezoid> output)
        {
            if (yt <= yb) return;
            output.Add(new Trapezoid
            {
                yb = (float)yb,
                yt = (float)yt,
                xlb = (float)el.XAt(yb),
                xlt = (float)el.XAt(yt),
                xrb = (float)er.XAt(yb),
                xrt = (float)er.XAt(yt),
                flags = flags
            });
        }

        /// <summary>
        /// Height of the band that is split with vertical internal sides so that only the uncovered parts of a
        /// horizontal side get anti-aliased. The band must stay inside the trapezoid at every split position.
        /// </summary>
        static double SplitHeight(in Edge el, in Edge er, double a, double b, NativeList<double2> uncovered,
            double hMax, bool top, double eps)
        {
            double sMin = double.MaxValue, sMax = double.MinValue;
            for (int i = 0; i < uncovered.Length; i++)
            {
                var u = uncovered[i];
                if (u.x > a + eps) { sMin = math.min(sMin, u.x); sMax = math.max(sMax, u.x); }
                if (u.y < b - eps) { sMin = math.min(sMin, u.y); sMax = math.max(sMax, u.y); }
            }

            double h = hMax;
            if (sMin > sMax) return h;
            // Moving away from the split line (down for a top band, up for a bottom band) the sides must not cross the
            // split positions.
            double sl = top ? -el.dxdy : el.dxdy;
            double sr = top ? -er.dxdy : er.dxdy;
            if (sl > 0) h = math.min(h, 0.5 * (sMin - a) / sl);
            if (sr < 0) h = math.min(h, 0.5 * (b - sMax) / -sr);
            return math.max(h, 0);
        }

        static void EmitBand(in Edge el, in Edge er, double y0, double y1, double a, double b,
            NativeList<double2> uncovered, bool top, double eps, ref NativeList<Trapezoid> output)
        {
            if (y1 <= y0) return;
            double xl0 = el.XAt(y0), xl1 = el.XAt(y1), xr0 = er.XAt(y0), xr1 = er.XAt(y1);

            // Split positions: inner endpoints of uncovered runs, in order.
            var splits = new NativeList<double>(uncovered.Length * 2, Allocator.Temp);
            for (int i = 0; i < uncovered.Length; i++)
            {
                var u = uncovered[i];
                if (u.x > a + eps && u.x < b - eps) splits.Add(u.x);
                if (u.y > a + eps && u.y < b - eps) splits.Add(u.y);
            }

            var lineFlag = top ? TrapezoidEdges.Top : TrapezoidEdges.Bottom;
            for (int i = 0; i <= splits.Length; i++)
            {
                double lb, lt, rb, rt;
                var flags = TrapezoidEdges.None;
                if (i == 0)
                {
                    lb = xl0; lt = xl1; flags |= TrapezoidEdges.Left;
                }
                else
                {
                    lb = lt = splits[i - 1];
                }

                if (i == splits.Length)
                {
                    rb = xr0; rt = xr1; flags |= TrapezoidEdges.Right;
                }
                else
                {
                    rb = rt = splits[i];
                }

                double mid = top ? 0.5 * (lt + rt) : 0.5 * (lb + rb);
                for (int k = 0; k < uncovered.Length; k++)
                {
                    if (mid >= uncovered[k].x - eps && mid <= uncovered[k].y + eps)
                    {
                        flags |= lineFlag;
                        break;
                    }
                }

                output.Add(new Trapezoid
                {
                    yb = (float)y0,
                    yt = (float)y1,
                    xlb = (float)lb,
                    xlt = (float)lt,
                    xrb = (float)rb,
                    xrt = (float)rt,
                    flags = flags
                });
            }

            splits.Dispose();
        }

        /// <summary>Total filled area of a trapezoid list.</summary>
        public static double Area(NativeArray<Trapezoid> traps)
        {
            double a = 0;
            for (int i = 0; i < traps.Length; i++) a += traps[i].Area;
            return a;
        }

        /// <summary>Appends every boundary side of the trapezoids as a segment (for distance fields and hit tests).</summary>
        public static void BoundarySegments(NativeArray<Trapezoid> traps, ref NativeList<Segment> output)
        {
            for (int i = 0; i < traps.Length; i++)
            {
                var t = traps[i];
                if ((t.flags & TrapezoidEdges.Left) != 0) output.Add(new Segment(t.BottomLeft, t.TopLeft));
                if ((t.flags & TrapezoidEdges.Right) != 0) output.Add(new Segment(t.BottomRight, t.TopRight));
                if ((t.flags & TrapezoidEdges.Top) != 0) output.Add(new Segment(t.TopLeft, t.TopRight));
                if ((t.flags & TrapezoidEdges.Bottom) != 0) output.Add(new Segment(t.BottomLeft, t.BottomRight));
            }
        }

        /// <summary>Point-in-area test against a trapezoid list.</summary>
        public static bool Contains(NativeArray<Trapezoid> traps, float2 p)
        {
            for (int i = 0; i < traps.Length; i++)
            {
                var t = traps[i];
                if (p.y < t.yb || p.y >= t.yt) continue;
                float f = (p.y - t.yb) / math.max(t.yt - t.yb, 1e-20f);
                float xl = math.lerp(t.xlb, t.xlt, f);
                float xr = math.lerp(t.xrb, t.xrt, f);
                if (p.x >= xl && p.x < xr) return true;
            }

            return false;
        }
    }
}
