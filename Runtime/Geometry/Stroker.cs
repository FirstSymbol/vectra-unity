using Unity.Collections;
using Unity.Mathematics;

namespace FirstSymbol.Vectra.Geometry
{
    /// <summary>Parameters of a stroke in the coordinate space of the polylines being stroked.</summary>
    public struct StrokeParams
    {
        public float width;
        public LineCap cap;
        public LineJoin join;
        public float miterLimit;
        /// <summary>Maximum deviation used when approximating round joins and caps.</summary>
        public float tolerance;

        /// <summary>Normalized trim range (0..1 along every contour) and its offset, After Effects style.</summary>
        public float trimStart, trimEnd, trimOffset;

        public float dashOffset;
        /// <summary>When set, dash lengths are fractions of each contour's length instead of absolute lengths.</summary>
        public bool dashRelative;

        public static StrokeParams Default(float width) => new StrokeParams
        {
            width = width,
            cap = LineCap.Butt,
            join = LineJoin.Miter,
            miterLimit = 4f,
            tolerance = 0.1f,
            trimStart = 0f,
            trimEnd = 1f
        };

        public bool HasTrim => trimStart > 0f || trimEnd < 1f;
    }

    /// <summary>
    /// Generates the outline of a stroke as a set of counter-clockwise pieces (segment quads, joins, caps). The pieces
    /// overlap; tessellating them with the non-zero rule yields the exact stroke area without double coverage.
    /// </summary>
    public static class Stroker
    {
        const float Epsilon = 1e-6f;

        /// <param name="dash">Alternating dash/gap lengths. Empty for a solid stroke.</param>
        /// <param name="widthProfile">Width multipliers sampled uniformly along each contour (0..1). Empty for constant width.</param>
        public static void Stroke(in Polylines input, in StrokeParams p, NativeArray<float> dash,
            NativeArray<float> widthProfile, ref Polylines output)
        {
            if (p.width <= 0f) return;
            var pts = new NativeList<float2>(256, Allocator.Temp);
            var cum = new NativeList<float>(256, Allocator.Temp);
            var piece = new NativeList<float2>(256, Allocator.Temp);
            var pieceArc = new NativeList<float>(256, Allocator.Temp);
            var poly = new NativeList<float2>(64, Allocator.Temp);
            var ranges = new NativeList<float2>(16, Allocator.Temp);

            float dashSum = 0f;
            for (int i = 0; i < dash.Length; i++) dashSum += math.max(dash[i], 0f);
            bool dashed = dash.Length > 0 && dashSum > Epsilon;

            for (int ci = 0; ci < input.contours.Length; ci++)
            {
                var c = input.contours[ci];
                if (c.count == 0) continue;

                // Contour as an open polyline; closed contours repeat their first point at the end.
                pts.Clear();
                cum.Clear();
                float len = 0f;
                for (int i = 0; i < c.count; i++)
                {
                    float2 q = input.points[c.start + i];
                    if (pts.Length > 0)
                    {
                        float d = math.distance(pts[pts.Length - 1], q);
                        if (d < Epsilon) continue;
                        len += d;
                    }

                    pts.Add(q);
                    cum.Add(len);
                }

                bool closed = c.closed && pts.Length > 2;
                if (closed)
                {
                    float d = math.distance(pts[pts.Length - 1], pts[0]);
                    if (d > Epsilon)
                    {
                        len += d;
                        pts.Add(pts[0]);
                        cum.Add(len);
                    }
                }

                if (pts.Length == 1 || len < Epsilon)
                {
                    float hw = HalfWidth(p, widthProfile, 0f);
                    AddDot(pts[0], hw, p, ref poly, ref output);
                    continue;
                }

                // Ranges of arc length to keep after trimming.
                ranges.Clear();
                bool fullClosed = closed;
                if (p.HasTrim || p.trimOffset != 0f)
                {
                    float t0 = math.min(p.trimStart, p.trimEnd), t1 = math.max(p.trimStart, p.trimEnd);
                    if (t1 - t0 >= 1f - 1e-6f)
                    {
                        ranges.Add(new float2(0f, len));
                    }
                    else
                    {
                        fullClosed = false;
                        float o = p.trimOffset - math.floor(p.trimOffset);
                        t0 += o;
                        t1 += o;
                        if (closed)
                        {
                            AddWrapped(t0, t1, len, ref ranges);
                        }
                        else
                        {
                            // Open paths do not wrap: offset slides the window and clips at the ends.
                            float a = math.min(p.trimStart, p.trimEnd) + p.trimOffset;
                            float b = math.max(p.trimStart, p.trimEnd) + p.trimOffset;
                            t0 = math.clamp(a, 0f, 1f);
                            t1 = math.clamp(b, 0f, 1f);
                            if (t1 > t0) ranges.Add(new float2(t0 * len, t1 * len));
                        }
                    }
                }
                else
                {
                    ranges.Add(new float2(0f, len));
                }

                if (!dashed)
                {
                    for (int r = 0; r < ranges.Length; r++)
                    {
                        var rg = ranges[r];
                        if (fullClosed && r == 0)
                        {
                            StrokeRun(pts, cum, 0f, len, true, len, p, widthProfile, ref piece, ref pieceArc, ref poly, ref output);
                            break;
                        }

                        StrokeRun(pts, cum, rg.x, rg.y, false, len, p, widthProfile, ref piece, ref pieceArc, ref poly, ref output);
                    }

                    continue;
                }

                // Dashing inside every kept range.
                float scale = p.dashRelative ? len : 1f;
                for (int r = 0; r < ranges.Length; r++)
                {
                    var rg = ranges[r];
                    // Odd-length patterns repeat once more to become even (SVG semantics).
                    int period = dash.Length % 2 == 0 ? dash.Length : dash.Length * 2;
                    float periodLen = (period == dash.Length ? dashSum : dashSum * 2f) * scale;
                    float phase = p.dashOffset * scale;
                    phase -= math.floor(phase / periodLen) * periodLen;
                    int idx = 0;
                    float remain = math.max(dash[0], 0f) * scale;
                    int skip = 0;
                    while (phase > 0f && skip++ < 4 * period)
                    {
                        if (phase < remain)
                        {
                            remain -= phase;
                            break;
                        }

                        phase -= remain;
                        idx = (idx + 1) % period;
                        remain = math.max(dash[idx % dash.Length], 0f) * scale;
                    }

                    float s = rg.x;
                    int safety = 0;
                    while (s < rg.y && safety++ < 200000)
                    {
                        float e = math.min(rg.y, s + remain);
                        if ((idx & 1) == 0)
                        {
                            if (e > s + Epsilon)
                                StrokeRun(pts, cum, s, e, false, len, p, widthProfile, ref piece, ref pieceArc, ref poly, ref output);
                            else if (p.cap != LineCap.Butt)
                                AddDot(PointAt(pts, cum, s), HalfWidth(p, widthProfile, s / len), p, ref poly, ref output);
                        }

                        s = e;
                        idx = (idx + 1) % period;
                        remain = math.max(dash[idx % dash.Length], 0f) * scale;
                    }
                }
            }

            pts.Dispose();
            cum.Dispose();
            piece.Dispose();
            pieceArc.Dispose();
            poly.Dispose();
            ranges.Dispose();
        }

        static void AddWrapped(float t0, float t1, float len, ref NativeList<float2> ranges)
        {
            // t0 in [0, 1), t1 in [t0, t0 + 1)
            if (t1 <= 1f)
            {
                ranges.Add(new float2(t0 * len, t1 * len));
                return;
            }

            if (t0 >= 1f)
            {
                ranges.Add(new float2((t0 - 1f) * len, (t1 - 1f) * len));
                return;
            }

            ranges.Add(new float2(t0 * len, len));
            ranges.Add(new float2(0f, (t1 - 1f) * len));
        }

        static float HalfWidth(in StrokeParams p, NativeArray<float> profile, float t)
        {
            float w = p.width;
            if (profile.Length == 1) w *= profile[0];
            else if (profile.Length > 1)
            {
                float f = math.saturate(t) * (profile.Length - 1);
                int i = math.min((int)f, profile.Length - 2);
                w *= math.lerp(profile[i], profile[i + 1], f - i);
            }

            return math.max(w, 0f) * 0.5f;
        }

        static float2 PointAt(NativeList<float2> pts, NativeList<float> cum, float s)
        {
            int i = FindSegment(cum, s);
            float segLen = cum[i + 1] - cum[i];
            float f = segLen > 0f ? (s - cum[i]) / segLen : 0f;
            return math.lerp(pts[i], pts[i + 1], math.saturate(f));
        }

        static int FindSegment(NativeList<float> cum, float s)
        {
            int lo = 0, hi = cum.Length - 2;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (cum[mid] <= s) lo = mid;
                else hi = mid - 1;
            }

            return math.clamp(lo, 0, math.max(cum.Length - 2, 0));
        }

        /// <summary>Strokes the part [s0, s1] of the polyline (or the whole closed loop).</summary>
        static void StrokeRun(NativeList<float2> pts, NativeList<float> cum, float s0, float s1, bool closedLoop,
            float totalLen, in StrokeParams p, NativeArray<float> profile, ref NativeList<float2> run,
            ref NativeList<float> runArc, ref NativeList<float2> poly, ref Polylines output)
        {
            run.Clear();
            runArc.Clear();
            if (closedLoop)
            {
                for (int i = 0; i < pts.Length - 1; i++)
                {
                    run.Add(pts[i]);
                    runArc.Add(cum[i]);
                }
            }
            else
            {
                int i0 = FindSegment(cum, s0);
                int i1 = FindSegment(cum, s1);
                run.Add(PointAt(pts, cum, s0));
                runArc.Add(s0);
                for (int i = i0 + 1; i <= i1; i++)
                {
                    if (cum[i] <= s0 || cum[i] >= s1) continue;
                    run.Add(pts[i]);
                    runArc.Add(cum[i]);
                }

                float2 end = PointAt(pts, cum, s1);
                if (math.distance(end, run[run.Length - 1]) > Epsilon || run.Length == 1)
                {
                    run.Add(end);
                    runArc.Add(s1);
                }
            }

            int n = run.Length;
            float invLen = totalLen > 0f ? 1f / totalLen : 0f;
            if (n < 2 || (!closedLoop && math.distance(run[0], run[n - 1]) < Epsilon && n == 2))
            {
                AddDot(run[0], HalfWidth(p, profile, runArc[0] * invLen), p, ref poly, ref output);
                return;
            }

            int segCount = closedLoop ? n : n - 1;
            for (int i = 0; i < segCount; i++)
            {
                float2 a = run[i], b = run[(i + 1) % n];
                float ta = runArc[i] * invLen;
                float tb = (closedLoop && i == n - 1) ? 1f : runArc[i + 1] * invLen;
                float2 d = b - a;
                float l = math.length(d);
                if (l < Epsilon) continue;
                d /= l;
                float2 nrm = new float2(-d.y, d.x);
                float wa = HalfWidth(p, profile, ta), wb = HalfWidth(p, profile, tb);
                poly.Clear();
                poly.Add(a - nrm * wa);
                poly.Add(b - nrm * wb);
                poly.Add(b + nrm * wb);
                poly.Add(a + nrm * wa);
                EmitPolygon(ref poly, ref output);
            }

            // Joins.
            int jStart = closedLoop ? 0 : 1;
            int jEnd = closedLoop ? n : n - 1;
            for (int i = jStart; i < jEnd; i++)
            {
                float2 v = run[i];
                float2 prev = run[(i - 1 + n) % n];
                float2 next = run[(i + 1) % n];
                float2 d1 = v - prev, d2 = next - v;
                float l1 = math.length(d1), l2 = math.length(d2);
                if (l1 < Epsilon || l2 < Epsilon) continue;
                AddJoin(v, d1 / l1, d2 / l2, HalfWidth(p, profile, runArc[i] * invLen), p, ref poly, ref output);
            }

            if (!closedLoop && p.cap != LineCap.Butt)
            {
                float2 d0 = math.normalizesafe(run[1] - run[0]);
                float2 d1 = math.normalizesafe(run[n - 1] - run[n - 2]);
                AddCap(run[0], -d0, HalfWidth(p, profile, runArc[0] * invLen), p, ref poly, ref output);
                AddCap(run[n - 1], d1, HalfWidth(p, profile, runArc[n - 1] * invLen), p, ref poly, ref output);
            }
        }

        static int ArcSteps(float radius, float angle, float tolerance)
        {
            if (radius <= tolerance) return math.max(2, (int)math.ceil(angle / (math.PI * 0.5f)));
            float step = 2f * math.acos(math.clamp(1f - tolerance / radius, -1f, 1f));
            step = math.max(step, 0.01f);
            return math.clamp((int)math.ceil(angle / step), 1, 512);
        }

        static void AddJoin(float2 v, float2 d1, float2 d2, float hw, in StrokeParams p, ref NativeList<float2> poly,
            ref Polylines output)
        {
            float cross = d1.x * d2.y - d1.y * d2.x;
            float dot = math.dot(d1, d2);
            if (dot > 0.999999f && math.abs(cross) < 1e-5f) return;
            if (hw <= 0f) return;

            float2 n1 = new float2(-d1.y, d1.x), n2 = new float2(-d2.y, d2.x);
            bool leftTurn = cross > 0f;
            float2 o1 = leftTurn ? -n1 : n1;
            float2 o2 = leftTurn ? -n2 : n2;

            poly.Clear();
            poly.Add(v);
            poly.Add(v + o1 * hw);
            switch (p.join)
            {
                case LineJoin.Round:
                {
                    float angle = math.acos(math.clamp(math.dot(o1, o2), -1f, 1f));
                    int steps = ArcSteps(hw, angle, p.tolerance);
                    float start = math.atan2(o1.y, o1.x);
                    float dir = leftTurn ? 1f : -1f;
                    if (dot < -0.999999f) dir = 1f;
                    for (int s = 1; s < steps; s++)
                    {
                        float a = start + dir * angle * s / steps;
                        math.sincos(a, out float sn, out float cs);
                        poly.Add(v + new float2(cs, sn) * hw);
                    }

                    break;
                }
                case LineJoin.Miter:
                {
                    float2 m = math.normalizesafe(o1 + o2);
                    float cosHalf = math.dot(m, o1);
                    if (cosHalf > Epsilon && 1f / cosHalf <= math.max(p.miterLimit, 1f))
                        poly.Add(v + m * (hw / cosHalf));
                    break;
                }
            }

            poly.Add(v + o2 * hw);
            EmitPolygon(ref poly, ref output);
        }

        static void AddCap(float2 e, float2 d, float hw, in StrokeParams p, ref NativeList<float2> poly, ref Polylines output)
        {
            if (hw <= 0f) return;
            float2 n = new float2(-d.y, d.x);
            poly.Clear();
            if (p.cap == LineCap.Square)
            {
                poly.Add(e + n * hw);
                poly.Add(e - n * hw);
                poly.Add(e - n * hw + d * hw);
                poly.Add(e + n * hw + d * hw);
            }
            else
            {
                int steps = math.max(ArcSteps(hw, math.PI, p.tolerance), 2);
                for (int s = 0; s <= steps; s++)
                {
                    float a = math.PI * s / steps;
                    math.sincos(a, out float sn, out float cs);
                    poly.Add(e + (n * cs + d * sn) * hw);
                }
            }

            EmitPolygon(ref poly, ref output);
        }

        static void AddDot(float2 c, float hw, in StrokeParams p, ref NativeList<float2> poly, ref Polylines output)
        {
            if (hw <= 0f || p.cap == LineCap.Butt) return;
            poly.Clear();
            if (p.cap == LineCap.Square)
            {
                poly.Add(c + new float2(-hw, -hw));
                poly.Add(c + new float2(hw, -hw));
                poly.Add(c + new float2(hw, hw));
                poly.Add(c + new float2(-hw, hw));
            }
            else
            {
                int steps = math.max(ArcSteps(hw, 2f * math.PI, p.tolerance), 8);
                for (int s = 0; s < steps; s++)
                {
                    math.sincos(2f * math.PI * s / steps, out float sn, out float cs);
                    poly.Add(c + new float2(cs, sn) * hw);
                }
            }

            EmitPolygon(ref poly, ref output);
        }

        /// <summary>Emits a polygon as a closed contour, reversed when needed so that it is counter-clockwise.</summary>
        public static void EmitPolygon(ref NativeList<float2> poly, ref Polylines output, int operand = 0)
        {
            if (poly.Length < 3) return;
            float area = SignedArea(poly);
            if (math.abs(area) < 1e-12f) return;
            output.BeginContour(operand);
            if (area > 0f)
                for (int i = 0; i < poly.Length; i++) output.AddPoint(poly[i]);
            else
                for (int i = poly.Length - 1; i >= 0; i--) output.AddPoint(poly[i]);
            output.EndContour(true);
        }

        public static float SignedArea(NativeList<float2> poly)
        {
            float a = 0f;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                a += (poly[j].x * poly[i].y) - (poly[i].x * poly[j].y);
            return 0.5f * a;
        }

        /// <summary>Total length of the contours (closed contours include their closing segment).</summary>
        public static float Length(in Polylines input)
        {
            float len = 0f;
            for (int ci = 0; ci < input.contours.Length; ci++)
            {
                var c = input.contours[ci];
                for (int i = 1; i < c.count; i++)
                    len += math.distance(input.points[c.start + i - 1], input.points[c.start + i]);
                if (c.closed && c.count > 2)
                    len += math.distance(input.points[c.start + c.count - 1], input.points[c.start]);
            }

            return len;
        }
    }
}
