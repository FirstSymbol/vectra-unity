using Unity.Collections;
using Unity.Mathematics;

namespace FirstSymbol.Vectra.Geometry
{
    /// <summary>Converts curves into polylines with a bounded maximum deviation (Wang's formula).</summary>
    public static class PathFlattener
    {
        public const int MaxSegmentsPerCurve = 1024;

        /// <summary>
        /// Flattens <paramref name="path"/> into <paramref name="output"/>. Every sub-path becomes a contour tagged with
        /// <paramref name="operand"/>. When <paramref name="closeAll"/> is set, open sub-paths are closed (fill semantics).
        /// </summary>
        public static void Flatten(in NativePath path, in Affine2 transform, float tolerance, int operand, bool closeAll,
            ref Polylines output)
        {
            Flatten(path.verbs.AsArray(), path.points.AsArray(), transform, tolerance, operand, closeAll, ref output);
        }

        public static void Flatten(NativeArray<byte> verbs, NativeArray<float2> points, in Affine2 transform,
            float tolerance, int operand, bool closeAll, ref Polylines output)
        {
            tolerance = math.max(tolerance, 1e-5f);
            int pi = 0;
            float2 current = float2.zero;
            float2 start = float2.zero;
            bool open = false;
            bool identity = transform.IsIdentity;

            for (int vi = 0; vi < verbs.Length; vi++)
            {
                var verb = (PathVerb)verbs[vi];
                switch (verb)
                {
                    case PathVerb.MoveTo:
                    {
                        if (open) output.EndContour(closeAll);
                        current = Xf(points[pi++], transform, identity);
                        start = current;
                        output.BeginContour(operand);
                        output.AddPoint(current);
                        open = true;
                        break;
                    }
                    case PathVerb.LineTo:
                    {
                        EnsureOpen(ref open, ref output, current, operand);
                        current = Xf(points[pi++], transform, identity);
                        output.AddPoint(current);
                        break;
                    }
                    case PathVerb.QuadTo:
                    {
                        EnsureOpen(ref open, ref output, current, operand);
                        float2 c = Xf(points[pi++], transform, identity);
                        float2 p = Xf(points[pi++], transform, identity);
                        FlattenQuad(current, c, p, tolerance, ref output);
                        current = p;
                        break;
                    }
                    case PathVerb.CubicTo:
                    {
                        EnsureOpen(ref open, ref output, current, operand);
                        float2 c1 = Xf(points[pi++], transform, identity);
                        float2 c2 = Xf(points[pi++], transform, identity);
                        float2 p = Xf(points[pi++], transform, identity);
                        FlattenCubic(current, c1, c2, p, tolerance, ref output);
                        current = p;
                        break;
                    }
                    case PathVerb.Close:
                    {
                        if (open)
                        {
                            output.EndContour(true);
                            open = false;
                        }

                        current = start;
                        break;
                    }
                }
            }

            if (open) output.EndContour(closeAll);
        }

        static void EnsureOpen(ref bool open, ref Polylines output, float2 current, int operand)
        {
            if (open) return;
            output.BeginContour(operand);
            output.AddPoint(current);
            open = true;
        }

        static float2 Xf(float2 p, in Affine2 t, bool identity) => identity ? p : t.Apply(p);

        public static int QuadSegments(float2 p0, float2 p1, float2 p2, float tolerance)
        {
            float m = math.length(p0 - 2f * p1 + p2);
            int n = (int)math.ceil(math.sqrt(m / (4f * tolerance)));
            return math.clamp(n, 1, MaxSegmentsPerCurve);
        }

        public static int CubicSegments(float2 p0, float2 p1, float2 p2, float2 p3, float tolerance)
        {
            float m = math.max(math.length(p0 - 2f * p1 + p2), math.length(p1 - 2f * p2 + p3));
            int n = (int)math.ceil(math.sqrt(0.75f * m / tolerance));
            return math.clamp(n, 1, MaxSegmentsPerCurve);
        }

        public static void FlattenQuad(float2 p0, float2 p1, float2 p2, float tolerance, ref Polylines output)
        {
            int n = QuadSegments(p0, p1, p2, tolerance);
            float inv = 1f / n;
            for (int i = 1; i < n; i++)
            {
                float t = i * inv;
                float mt = 1f - t;
                output.AddPoint(mt * mt * p0 + 2f * mt * t * p1 + t * t * p2);
            }

            output.AddPoint(p2);
        }

        public static void FlattenCubic(float2 p0, float2 p1, float2 p2, float2 p3, float tolerance, ref Polylines output)
        {
            int n = CubicSegments(p0, p1, p2, p3, tolerance);
            float inv = 1f / n;
            for (int i = 1; i < n; i++)
            {
                float t = i * inv;
                float mt = 1f - t;
                float a = mt * mt * mt, b = 3f * mt * mt * t, c = 3f * mt * t * t, d = t * t * t;
                output.AddPoint(a * p0 + b * p1 + c * p2 + d * p3);
            }

            output.AddPoint(p3);
        }

        /// <summary>Computes tight-enough bounds of a path from its control points (hull bounds).</summary>
        public static Bounds2 ControlBounds(NativeArray<float2> points, in Affine2 transform)
        {
            var b = Bounds2.Empty;
            for (int i = 0; i < points.Length; i++) b.Encapsulate(transform.Apply(points[i]));
            return b;
        }
    }
}
