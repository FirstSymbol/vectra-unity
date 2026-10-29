using System;
using FirstSymbol.Vectra.Geometry;
using FirstSymbol.Vectra.Rendering;
using Unity.Mathematics;
using UnityEngine;

namespace FirstSymbol.Vectra
{
    public enum CornerStyle
    {
        Round = 0,
        Chamfer = 1,
        Inverted = 2,
        Notch = 3,
        /// <summary>Continuous-curvature ("iOS") corners.</summary>
        Squircle = 4
    }

    public enum SegmentCap
    {
        Butt = 0,
        Round = 1,
        Square = 2
    }

    [Serializable]
    public sealed class RectangleShape : VectraShapeGeometry
    {
        /// <summary>Corner radii: x = top-left, y = top-right, z = bottom-right, w = bottom-left.</summary>
        public Vector4 cornerRadii = new Vector4(12f, 12f, 12f, 12f);
        public CornerStyle cornerStyle = CornerStyle.Round;
        [Range(0f, 1f)] public float smoothing = 0.6f;

        public override string DisplayName => "Rectangle";

        public float4 ClampedRadii(in ShapeFrame frame) =>
            math.clamp((float4)cornerRadii, 0f, math.max(frame.MinHalf, 0f));

        public override bool TryGetAnalytic(in ShapeFrame frame, out AnalyticShape shape)
        {
            float2 hs = frame.HalfSize;
            float m = math.min(hs.x, hs.y);
            if (m <= 1e-6f)
            {
                shape = default;
                return false;
            }

            float4 r = ClampedRadii(frame) / m;
            float4 s1 = new float4(hs, VectraEncoding.Pack12(r.y, r.z), VectraEncoding.Pack12(r.x, r.w));
            bool sharp = cornerStyle != CornerStyle.Round && cornerStyle != CornerStyle.Squircle ||
                         math.any(ClampedRadii(frame) < frame.pixelSize);
            shape = Make(frame, ShapeKind.Rect, s1, new float2(smoothing, 0f), hs, (int)cornerStyle, sharp);
            return true;
        }

        public override void BuildPath(in ShapeFrame frame, PathBuilder b)
        {
            var saved = EnterShapeSpace(frame, b);
            float2 hs = frame.HalfSize;
            float4 r = ClampedRadii(frame);
            if (cornerStyle == CornerStyle.Round)
            {
                b.AddRoundedRect(-hs, hs * 2f, r);
                b.transform = saved;
                return;
            }

            // Corners in counter-clockwise order: bottom-left, bottom-right, top-right, top-left.
            float2[] corners = { new float2(-hs.x, -hs.y), new float2(hs.x, -hs.y), hs, new float2(-hs.x, hs.y) };
            float[] radii = { r.w, r.z, r.y, r.x };
            for (int i = 0; i < 4; i++)
            {
                float2 c = corners[i];
                float2 din = math.normalizesafe(c - corners[(i + 3) % 4]);
                float2 dout = math.normalizesafe(corners[(i + 1) % 4] - c);
                float rr = radii[i];
                float edge = math.min(math.distance(c, corners[(i + 3) % 4]), math.distance(c, corners[(i + 1) % 4]));
                if (cornerStyle == CornerStyle.Squircle)
                {
                    float l = math.min(rr * (1f + 0.6f * smoothing), edge * 0.5f);
                    float k = math.min(PathBuilder.Kappa + 0.35f * smoothing, 0.95f) * l;
                    float2 a = c - din * l, e = c + dout * l;
                    if (i == 0) b.MoveTo(a);
                    else b.LineTo(a);
                    if (l > 0f) b.CubicTo(a + din * k, e - dout * k, e);
                    continue;
                }

                float2 pa = c - din * rr, pb = c + dout * rr;
                if (i == 0) b.MoveTo(rr > 0f ? pa : c);
                else b.LineTo(rr > 0f ? pa : c);
                if (rr <= 0f) continue;
                switch (cornerStyle)
                {
                    case CornerStyle.Chamfer:
                        b.LineTo(pb);
                        break;
                    case CornerStyle.Inverted:
                        b.CubicTo(pa + dout * rr * PathBuilder.Kappa, pb - din * rr * PathBuilder.Kappa, pb);
                        break;
                    case CornerStyle.Notch:
                        b.LineTo(pa + dout * rr);
                        b.LineTo(pb);
                        break;
                }
            }

            b.Close();
            b.transform = saved;
        }

        protected override void Hash(ref HashBuilder h)
        {
            h.Add(cornerRadii);
            h.Add((int)cornerStyle);
            h.Add(smoothing);
        }
    }

    [Serializable]
    public sealed class EllipseShape : VectraShapeGeometry
    {
        public override string DisplayName => "Ellipse";

        public override bool TryGetAnalytic(in ShapeFrame frame, out AnalyticShape shape)
        {
            float2 hs = math.max(frame.HalfSize, 1e-6f);
            shape = Make(frame, ShapeKind.Ellipse, new float4(hs, 0f, 0f), float2.zero, hs);
            return true;
        }

        public override void BuildPath(in ShapeFrame frame, PathBuilder b)
        {
            var saved = EnterShapeSpace(frame, b);
            b.AddEllipse(float2.zero, frame.HalfSize);
            b.transform = saved;
        }

        protected override void Hash(ref HashBuilder h) { }
    }

    /// <summary>Ring, arc, pie slice or annular sector (circular).</summary>
    [Serializable]
    public sealed class ArcShape : VectraShapeGeometry
    {
        /// <summary>Inner radius relative to the outer radius; 0 makes a pie/disk.</summary>
        [Range(0f, 0.999f)] public float innerRadius = 0.6f;
        /// <summary>Start angle in degrees, counter-clockwise from +X.</summary>
        public float startAngle = 90f;
        [Range(0f, 360f)] public float sweepAngle = 270f;
        public bool roundCaps = true;

        public override string DisplayName => "Arc / Ring / Pie";

        void Radii(in ShapeFrame frame, out float outer, out float inner)
        {
            outer = math.max(frame.MinHalf, 1e-6f);
            inner = outer * math.saturate(innerRadius);
        }

        public override bool TryGetAnalytic(in ShapeFrame frame, out AnalyticShape shape)
        {
            Radii(frame, out float outer, out float inner);
            float ra = (outer + inner) * 0.5f, rb = (outer - inner) * 0.5f;
            float ha = math.radians(math.clamp(sweepAngle, 0f, 360f)) * 0.5f;
            float centre = math.radians(startAngle) + ha;
            float rot = centre - math.PI * 0.5f;
            int style = roundCaps && inner > 0f ? 1 : 0;
            shape = Make(frame, ShapeKind.Arc, new float4(ra, rb, ha, rot), float2.zero, new float2(outer),
                style, !roundCaps && sweepAngle < 360f);
            return true;
        }

        public override void BuildPath(in ShapeFrame frame, PathBuilder b)
        {
            var saved = EnterShapeSpace(frame, b);
            Radii(frame, out float outer, out float inner);
            float s = math.radians(startAngle);
            float w = math.radians(math.clamp(sweepAngle, 0f, 360f));
            if (!roundCaps || inner <= 0f || w >= 2f * math.PI - 1e-4f)
            {
                b.AddSector(float2.zero, new float2(outer), new float2(inner), s, w);
            }
            else
            {
                float ra = (outer + inner) * 0.5f, rb = (outer - inner) * 0.5f;
                float e = s + w;
                b.AppendEllipticArc(float2.zero, new float2(outer), 0f, s, w, true);
                b.AppendEllipticArc(ra * new float2(math.cos(e), math.sin(e)), new float2(rb), 0f, e, math.PI, false);
                b.AppendEllipticArc(float2.zero, new float2(inner), 0f, e, -w, false);
                b.AppendEllipticArc(ra * new float2(math.cos(s), math.sin(s)), new float2(rb), 0f, s + math.PI, math.PI, false);
                b.Close();
            }

            b.transform = saved;
        }

        protected override void Hash(ref HashBuilder h)
        {
            h.Add(innerRadius);
            h.Add(startAngle);
            h.Add(sweepAngle);
            h.Add(roundCaps);
        }
    }

    [Serializable]
    public sealed class PolygonShape : VectraShapeGeometry
    {
        [Range(3, 64)] public int sides = 6;
        [Min(0f)] public float cornerRadius = 4f;

        public override string DisplayName => "Polygon";

        public override bool TryGetAnalytic(in ShapeFrame frame, out AnalyticShape shape)
        {
            float R = math.max(frame.MinHalf, 1e-6f);
            int n = math.max(sides, 3);
            float k = math.cos(math.PI / n);
            float r = math.min(cornerRadius, R * 0.99f);
            shape = Make(frame, ShapeKind.Star, new float4(R, n, k, 0f), new float2(r, 0f), new float2(R), 0, r < frame.pixelSize);
            return true;
        }

        public override void BuildPath(in ShapeFrame frame, PathBuilder b)
        {
            var saved = EnterShapeSpace(frame, b);
            float R = math.max(frame.MinHalf, 1e-6f);
            int n = math.max(sides, 3);
            float r = math.min(cornerRadius, R * 0.99f);
            ShapePaths.InflatedPolygon(b, StarShape.StarPoints(R - r, n, math.cos(math.PI / n), true), r);
            b.transform = saved;
        }

        protected override void Hash(ref HashBuilder h)
        {
            h.Add(sides);
            h.Add(cornerRadius);
        }
    }

    [Serializable]
    public sealed class StarShape : VectraShapeGeometry
    {
        [Range(2, 64)] public int points = 5;
        [Range(0.05f, 1f)] public float innerRatio = 0.45f;
        [Min(0f)] public float cornerRadius = 2f;

        public override string DisplayName => "Star";

        /// <summary>Star vertices (first one at +Y), counter-clockwise. A polygon omits the inner vertices.</summary>
        public static float2[] StarPoints(float R, int n, float k, bool polygon)
        {
            int count = polygon ? n : n * 2;
            var pts = new float2[count];
            float an = math.PI / n;
            for (int i = 0; i < count; i++)
            {
                float a = math.PI * 0.5f + (polygon ? 2f * an * i : an * i);
                float rad = polygon || (i & 1) == 0 ? R : R * k;
                pts[i] = new float2(math.cos(a), math.sin(a)) * rad;
            }

            return pts;
        }

        public override bool TryGetAnalytic(in ShapeFrame frame, out AnalyticShape shape)
        {
            float R = math.max(frame.MinHalf, 1e-6f);
            float r = math.min(cornerRadius, R * 0.5f);
            shape = Make(frame, ShapeKind.Star, new float4(R, math.max(points, 2), innerRatio, 0f), new float2(r, 0f),
                new float2(R), 0, true);
            return true;
        }

        public override void BuildPath(in ShapeFrame frame, PathBuilder b)
        {
            var saved = EnterShapeSpace(frame, b);
            float R = math.max(frame.MinHalf, 1e-6f);
            float r = math.min(cornerRadius, R * 0.5f);
            ShapePaths.InflatedPolygon(b, StarPoints(R - r, math.max(points, 2), innerRatio, false), r);
            b.transform = saved;
        }

        protected override void Hash(ref HashBuilder h)
        {
            h.Add(points);
            h.Add(innerRatio);
            h.Add(cornerRadius);
        }
    }

    [Serializable]
    public sealed class TriangleShape : VectraShapeGeometry
    {
        /// <summary>Vertices in normalized rect coordinates (0..1).</summary>
        public Vector2 a = new Vector2(0.5f, 1f);
        public Vector2 b = new Vector2(0f, 0f);
        public Vector2 c = new Vector2(1f, 0f);
        [Min(0f)] public float cornerRadius;

        public override string DisplayName => "Triangle";

        float2 N(Vector2 v) => new float2(v.x, v.y) * 2f - 1f;

        public override bool TryGetAnalytic(in ShapeFrame frame, out AnalyticShape shape)
        {
            float2 hs = math.max(frame.HalfSize, 1e-6f);
            float2 pa = N(a), pb = N(b), pc = N(c);
            float r = cornerRadius;
            // Shrink towards the centroid so that rounding stays inside the rect.
            float2 centroid = (pa + pb + pc) / 3f;
            float shrink = 1f - math.saturate(r / math.max(math.cmin(hs), 1e-6f));
            pa = centroid + (pa - centroid) * shrink;
            pb = centroid + (pb - centroid) * shrink;
            pc = centroid + (pc - centroid) * shrink;
            var s1 = new float4(hs, VectraEncoding.Pack12(pa.x * 0.5f + 0.5f, pa.y * 0.5f + 0.5f),
                VectraEncoding.Pack12(pb.x * 0.5f + 0.5f, pb.y * 0.5f + 0.5f));
            var s2 = new float2(VectraEncoding.Pack12(pc.x * 0.5f + 0.5f, pc.y * 0.5f + 0.5f), r);
            shape = Make(frame, ShapeKind.Triangle, s1, s2, hs, 0, true);
            return true;
        }

        public override void BuildPath(in ShapeFrame frame, PathBuilder builder)
        {
            var saved = EnterShapeSpace(frame, builder);
            float2 hs = math.max(frame.HalfSize, 1e-6f);
            float2 pa = N(a), pb = N(b), pc = N(c);
            float r = cornerRadius;
            float2 centroid = (pa + pb + pc) / 3f;
            float shrink = 1f - math.saturate(r / math.max(math.cmin(hs), 1e-6f));
            float2[] pts =
            {
                (centroid + (pa - centroid) * shrink) * hs,
                (centroid + (pb - centroid) * shrink) * hs,
                (centroid + (pc - centroid) * shrink) * hs
            };
            ShapePaths.InflatedPolygon(builder, pts, r);
            builder.transform = saved;
        }

        protected override void Hash(ref HashBuilder h)
        {
            h.Add(a);
            h.Add(b);
            h.Add(c);
            h.Add(cornerRadius);
        }
    }

    /// <summary>A thick line segment between two points of the rect (analytic capsule/box).</summary>
    [Serializable]
    public sealed class LineShape : VectraShapeGeometry
    {
        public Vector2 start = new Vector2(0.1f, 0.5f);
        public Vector2 end = new Vector2(0.9f, 0.5f);
        [Min(0f)] public float thickness = 8f;
        public SegmentCap cap = SegmentCap.Round;

        public override string DisplayName => "Line";

        public override bool TryGetAnalytic(in ShapeFrame frame, out AnalyticShape shape)
        {
            float2 hs = frame.HalfSize;
            float2 p0 = (new float2(start.x, start.y) * 2f - 1f) * hs;
            float2 p1 = (new float2(end.x, end.y) * 2f - 1f) * hs;
            float hw = thickness * 0.5f;
            shape = Make(frame, ShapeKind.Segment, new float4(p0, p1), new float2(hw, 0f), hs + hw, (int)cap,
                cap != SegmentCap.Round);
            return true;
        }

        public override void BuildPath(in ShapeFrame frame, PathBuilder b)
        {
            var saved = EnterShapeSpace(frame, b);
            float2 hs = frame.HalfSize;
            float2 p0 = (new float2(start.x, start.y) * 2f - 1f) * hs;
            float2 p1 = (new float2(end.x, end.y) * 2f - 1f) * hs;
            float hw = thickness * 0.5f;
            float2 d = math.normalizesafe(p1 - p0, new float2(1f, 0f));
            float2 n = new float2(-d.y, d.x);
            if (cap == SegmentCap.Round)
            {
                float a = math.atan2(n.y, n.x);
                b.AppendEllipticArc(p1, new float2(hw), 0f, a - math.PI, math.PI, true);
                b.AppendEllipticArc(p0, new float2(hw), 0f, a, math.PI, false);
                b.Close();
            }
            else
            {
                float ext = cap == SegmentCap.Square ? hw : 0f;
                float2 a0 = p0 - d * ext, a1 = p1 + d * ext;
                ShapePaths.Polygon(b, new[] { a0 - n * hw, a1 - n * hw, a1 + n * hw, a0 + n * hw });
            }

            b.transform = saved;
        }

        protected override void Hash(ref HashBuilder h)
        {
            h.Add(start);
            h.Add(end);
            h.Add(thickness);
            h.Add((int)cap);
        }
    }

    [Serializable]
    public sealed class CrossShape : VectraShapeGeometry
    {
        /// <summary>Arm thickness relative to the smaller rect dimension.</summary>
        [Range(0.02f, 1f)] public float thickness = 0.33f;
        [Min(0f)] public float cornerRadius = 2f;

        public override string DisplayName => "Cross";

        public override bool TryGetAnalytic(in ShapeFrame frame, out AnalyticShape shape)
        {
            float2 hs = math.max(frame.HalfSize, 1e-6f);
            float t = math.cmin(hs) * thickness;
            float r = math.min(cornerRadius, t * 0.999f);
            shape = Make(frame, ShapeKind.Cross, new float4(hs, t, r), float2.zero, hs, 0, true);
            return true;
        }

        public override void BuildPath(in ShapeFrame frame, PathBuilder b)
        {
            var saved = EnterShapeSpace(frame, b);
            float2 hs = math.max(frame.HalfSize, 1e-6f);
            float t = math.cmin(hs) * thickness;
            float r = math.min(cornerRadius, t * 0.999f);
            b.AddRoundedRect(new float2(-hs.x, -t), new float2(hs.x * 2f, t * 2f), new float4(r));
            b.AddRoundedRect(new float2(-t, -hs.y), new float2(t * 2f, hs.y * 2f), new float4(r));
            b.transform = saved;
        }

        protected override void Hash(ref HashBuilder h)
        {
            h.Add(thickness);
            h.Add(cornerRadius);
        }
    }

    [Serializable]
    public sealed class HeartShape : VectraShapeGeometry
    {
        const float NormWidth = 1.2071068f;
        const float NormHeight = 1.1035534f;

        public override string DisplayName => "Heart";

        float Scale(in ShapeFrame frame) => math.max(math.min(frame.rect.width / NormWidth, frame.rect.height / NormHeight), 1e-6f);

        public override bool TryGetAnalytic(in ShapeFrame frame, out AnalyticShape shape)
        {
            float s = Scale(frame);
            shape = Make(frame, ShapeKind.Heart, new float4(s, -NormHeight * 0.5f * s, 0f, 0f), float2.zero,
                new float2(NormWidth, NormHeight) * 0.5f * s, 0, true);
            return true;
        }

        public override void BuildPath(in ShapeFrame frame, PathBuilder b)
        {
            var saved = EnterShapeSpace(frame, b);
            float s = Scale(frame);
            b.transform = b.transform * Affine2.Translate(new float2(0f, -NormHeight * 0.5f * s)) * Affine2.Scale(new float2(s));
            float r = math.sqrt(2f) / 4f;
            b.MoveTo(float2.zero);
            b.LineTo(new float2(0.5f, 0.5f));
            b.AppendEllipticArc(new float2(0.25f, 0.75f), new float2(r), 0f, -math.PI * 0.25f, math.PI, false);
            b.AppendEllipticArc(new float2(-0.25f, 0.75f), new float2(r), 0f, math.PI * 0.25f, math.PI, false);
            b.LineTo(float2.zero);
            b.Close();
            b.transform = saved;
        }

        protected override void Hash(ref HashBuilder h) { }
    }

    [Serializable]
    public sealed class RhombusShape : VectraShapeGeometry
    {
        [Min(0f)] public float cornerRadius;

        public override string DisplayName => "Rhombus";

        float2 Half(in ShapeFrame frame, out float r)
        {
            float2 hs = math.max(frame.HalfSize, 1e-6f);
            r = math.min(cornerRadius, math.cmin(hs) * 0.5f);
            // Shrinking each half-diagonal by r * hypot / other keeps the rounded shape inside the rect.
            float hyp = math.length(hs);
            return math.max(hs - r * new float2(hyp / hs.y, hyp / hs.x), 1e-5f);
        }

        public override bool TryGetAnalytic(in ShapeFrame frame, out AnalyticShape shape)
        {
            float2 b = Half(frame, out float r);
            shape = Make(frame, ShapeKind.Rhombus, new float4(b, r, 0f), float2.zero, frame.HalfSize, 0, true);
            return true;
        }

        public override void BuildPath(in ShapeFrame frame, PathBuilder builder)
        {
            var saved = EnterShapeSpace(frame, builder);
            float2 b = Half(frame, out float r);
            ShapePaths.InflatedPolygon(builder, new[] { new float2(0, -b.y), new float2(b.x, 0), new float2(0, b.y), new float2(-b.x, 0) }, r);
            builder.transform = saved;
        }

        protected override void Hash(ref HashBuilder h) => h.Add(cornerRadius);
    }

    [Serializable]
    public sealed class TrapezoidShape : VectraShapeGeometry
    {
        /// <summary>Top width relative to the bottom width.</summary>
        [Range(0f, 2f)] public float topRatio = 0.6f;
        [Min(0f)] public float cornerRadius;

        public override string DisplayName => "Trapezoid";

        void Params(in ShapeFrame frame, out float r1, out float r2, out float he, out float r)
        {
            float2 hs = math.max(frame.HalfSize, 1e-6f);
            r = math.min(cornerRadius, math.cmin(hs) * 0.5f);
            float maxW = hs.x - r;
            float t = math.max(topRatio, 0f);
            r1 = t <= 1f ? maxW : maxW / t;
            r2 = t <= 1f ? maxW * t : maxW;
            he = math.max(hs.y - r, 1e-5f);
        }

        public override bool TryGetAnalytic(in ShapeFrame frame, out AnalyticShape shape)
        {
            Params(frame, out float r1, out float r2, out float he, out float r);
            shape = Make(frame, ShapeKind.Trapezoid, new float4(r1, r2, he, r), float2.zero, frame.HalfSize, 0, true);
            return true;
        }

        public override void BuildPath(in ShapeFrame frame, PathBuilder b)
        {
            var saved = EnterShapeSpace(frame, b);
            Params(frame, out float r1, out float r2, out float he, out float r);
            ShapePaths.InflatedPolygon(b, new[] { new float2(-r1, -he), new float2(r1, -he), new float2(r2, he), new float2(-r2, he) }, r);
            b.transform = saved;
        }

        protected override void Hash(ref HashBuilder h)
        {
            h.Add(topRatio);
            h.Add(cornerRadius);
        }
    }

    [Serializable]
    public sealed class ParallelogramShape : VectraShapeGeometry
    {
        /// <summary>Horizontal skew of the top edge relative to the half width (-1..1).</summary>
        [Range(-1f, 1f)] public float skew = 0.3f;
        [Min(0f)] public float cornerRadius;

        public override string DisplayName => "Parallelogram";

        void Params(in ShapeFrame frame, out float wi, out float he, out float sk, out float r)
        {
            float2 hs = math.max(frame.HalfSize, 1e-6f);
            r = math.min(cornerRadius, math.cmin(hs) * 0.5f);
            float w = hs.x - r;
            sk = skew * w * 0.5f;
            wi = math.max(w - math.abs(sk), 1e-5f);
            he = math.max(hs.y - r, 1e-5f);
        }

        public override bool TryGetAnalytic(in ShapeFrame frame, out AnalyticShape shape)
        {
            Params(frame, out float wi, out float he, out float sk, out float r);
            shape = Make(frame, ShapeKind.Parallelogram, new float4(wi, he, sk, r), float2.zero, frame.HalfSize, 0, true);
            return true;
        }

        public override void BuildPath(in ShapeFrame frame, PathBuilder b)
        {
            var saved = EnterShapeSpace(frame, b);
            Params(frame, out float wi, out float he, out float sk, out float r);
            ShapePaths.InflatedPolygon(b, new[]
            {
                new float2(-wi - sk, -he), new float2(wi - sk, -he), new float2(wi + sk, he), new float2(-wi + sk, he)
            }, r);
            b.transform = saved;
        }

        protected override void Hash(ref HashBuilder h)
        {
            h.Add(skew);
            h.Add(cornerRadius);
        }
    }

    /// <summary>Crescent: a disk with a second, offset disk cut out of it.</summary>
    [Serializable]
    public sealed class CrescentShape : VectraShapeGeometry
    {
        [Range(0.05f, 1.5f)] public float cutRadius = 0.85f;
        [Range(0f, 2f)] public float cutOffset = 0.45f;

        public override string DisplayName => "Crescent";
        public override FillRule FillRule => FillRule.Positive;

        public override bool TryGetAnalytic(in ShapeFrame frame, out AnalyticShape shape)
        {
            float R = math.max(frame.MinHalf, 1e-6f);
            shape = Make(frame, ShapeKind.Moon, new float4(R, R * cutRadius, R * math.max(cutOffset, 1e-3f), 0f),
                float2.zero, new float2(R), 0, true);
            return true;
        }

        public override void BuildPath(in ShapeFrame frame, PathBuilder b)
        {
            var saved = EnterShapeSpace(frame, b);
            float R = math.max(frame.MinHalf, 1e-6f);
            b.AddCircle(float2.zero, R);
            float rb = R * cutRadius;
            float2 c = new float2(R * math.max(cutOffset, 1e-3f), 0f);
            b.AppendEllipticArc(c, new float2(rb), 0f, 0f, -2f * math.PI, true);
            b.Close();
            b.transform = saved;
        }

        protected override void Hash(ref HashBuilder h)
        {
            h.Add(cutRadius);
            h.Add(cutOffset);
        }
    }

    [Serializable]
    public sealed class EggShape : VectraShapeGeometry
    {
        const float K = 1.7320508f;

        public override string DisplayName => "Egg";

        void Params(in ShapeFrame frame, out float ra, out float rb, out float shift)
        {
            float2 hs = math.max(frame.HalfSize, 1e-6f);
            float h = hs.y * 2f;
            ra = math.min(hs.x, h / 2f);
            rb = math.clamp((ra * (1f + K) - h) / (K - 1f), 0f, ra);
            float top = K * (ra - rb) + rb;
            shift = (top - ra) * 0.5f;
        }

        public override bool TryGetAnalytic(in ShapeFrame frame, out AnalyticShape shape)
        {
            Params(frame, out float ra, out float rb, out float shift);
            shape = Make(frame, ShapeKind.Egg, new float4(ra, rb, -shift, 0f), float2.zero, frame.HalfSize);
            return true;
        }

        public override void BuildPath(in ShapeFrame frame, PathBuilder b)
        {
            var saved = EnterShapeSpace(frame, b);
            Params(frame, out float ra, out float rb, out float shift);
            b.transform = b.transform * Affine2.Translate(new float2(0f, -shift));
            float rr = ra - rb;
            float big = 2f * rr + rb;
            float a60 = math.PI / 3f;
            b.MoveTo(new float2(ra, 0f));
            if (rr > 1e-6f)
            {
                b.AppendEllipticArc(new float2(-rr, 0f), new float2(big), 0f, 0f, a60, false);
                b.AppendEllipticArc(new float2(0f, K * rr), new float2(math.max(rb, 1e-6f)), 0f, a60, a60, false);
                b.AppendEllipticArc(new float2(rr, 0f), new float2(big), 0f, 2f * a60, a60, false);
            }
            else
            {
                b.AppendEllipticArc(float2.zero, new float2(ra), 0f, 0f, math.PI, false);
            }

            b.AppendEllipticArc(float2.zero, new float2(ra), 0f, math.PI, math.PI, false);
            b.Close();
            b.transform = saved;
        }

        protected override void Hash(ref HashBuilder h) { }
    }

    /// <summary>Vesica piscis (lens): intersection of two circles. Orientation follows the longer rect side.</summary>
    [Serializable]
    public sealed class VesicaShape : VectraShapeGeometry
    {
        public override string DisplayName => "Vesica";

        void Params(in ShapeFrame frame, out float r, out float d, out bool horizontal)
        {
            float2 hs = math.max(frame.HalfSize, 1e-6f);
            horizontal = hs.x > hs.y;
            float w = horizontal ? hs.y : hs.x;
            float b = horizontal ? hs.x : hs.y;
            r = (w + b * b / w) * 0.5f;
            d = r - w;
        }

        public override bool TryGetAnalytic(in ShapeFrame frame, out AnalyticShape shape)
        {
            Params(frame, out float r, out float d, out bool horizontal);
            shape = Make(frame, ShapeKind.Vesica, new float4(r, d, 0f, 0f), float2.zero,
                horizontal ? new float2(frame.HalfSize.y, frame.HalfSize.x) : frame.HalfSize, 0, true);
            if (horizontal) shape.localToShape = Affine2.Rotate(-math.PI * 0.5f) * shape.localToShape;
            return true;
        }

        public override void BuildPath(in ShapeFrame frame, PathBuilder b)
        {
            var saved = EnterShapeSpace(frame, b);
            Params(frame, out float r, out float d, out bool horizontal);
            if (horizontal) b.transform = b.transform * Affine2.Rotate(math.PI * 0.5f);
            float bb = math.sqrt(math.max(r * r - d * d, 0f));
            float a = math.atan2(bb, d);
            b.MoveTo(new float2(0f, -bb));
            b.AppendEllipticArc(new float2(-d, 0f), new float2(r), 0f, -a, 2f * a, false);
            b.AppendEllipticArc(new float2(d, 0f), new float2(r), 0f, math.PI - a, 2f * a, false);
            b.Close();
            b.transform = saved;
        }

        protected override void Hash(ref HashBuilder h) { }
    }

    /// <summary>Superellipse |x/a|^n + |y/b|^n = 1.</summary>
    [Serializable]
    public sealed class SquircleShape : VectraShapeGeometry
    {
        [Range(1f, 12f)] public float exponent = 4f;

        public override string DisplayName => "Squircle";

        public override bool TryGetAnalytic(in ShapeFrame frame, out AnalyticShape shape)
        {
            float2 hs = math.max(frame.HalfSize, 1e-6f);
            shape = Make(frame, ShapeKind.Squircle, new float4(hs, math.max(exponent, 1f), 0f), float2.zero, hs, 0,
                exponent < 2f);
            return true;
        }

        public override void BuildPath(in ShapeFrame frame, PathBuilder b)
        {
            var saved = EnterShapeSpace(frame, b);
            float2 hs = math.max(frame.HalfSize, 1e-6f);
            float e = 2f / math.max(exponent, 1f);
            const int steps = 96;
            for (int i = 0; i < steps; i++)
            {
                float t = 2f * math.PI * i / steps;
                math.sincos(t, out float s, out float c);
                var p = new float2(math.sign(c) * math.pow(math.abs(c), e), math.sign(s) * math.pow(math.abs(s), e)) * hs;
                if (i == 0) b.MoveTo(p);
                else b.LineTo(p);
            }

            b.Close();
            b.transform = saved;
        }

        protected override void Hash(ref HashBuilder h) => h.Add(exponent);
    }

    [Serializable]
    public sealed class FlowerShape : VectraShapeGeometry
    {
        [Range(2, 32)] public int petals = 6;
        [Range(0f, 1f)] public float depth = 0.35f;

        public override string DisplayName => "Flower";

        public override bool TryGetAnalytic(in ShapeFrame frame, out AnalyticShape shape)
        {
            float R = math.max(frame.MinHalf, 1e-6f);
            shape = Make(frame, ShapeKind.Flower, new float4(R, petals, depth, math.PI * 0.5f), float2.zero, new float2(R));
            return true;
        }

        public override void BuildPath(in ShapeFrame frame, PathBuilder b)
        {
            var saved = EnterShapeSpace(frame, b);
            float R = math.max(frame.MinHalf, 1e-6f);
            int steps = math.max(petals * 24, 48);
            for (int i = 0; i < steps; i++)
            {
                float a = 2f * math.PI * i / steps;
                float r = R * (1f - depth * 0.5f * (1f - math.cos(petals * (a - math.PI * 0.5f))));
                var p = new float2(math.cos(a), math.sin(a)) * r;
                if (i == 0) b.MoveTo(p);
                else b.LineTo(p);
            }

            b.Close();
            b.transform = saved;
        }

        protected override void Hash(ref HashBuilder h)
        {
            h.Add(petals);
            h.Add(depth);
        }
    }

    /// <summary>Arrow pointing to +X in its rect (use rotation for other directions).</summary>
    [Serializable]
    public sealed class ArrowShape : VectraShapeGeometry
    {
        [Range(0.05f, 1f)] public float headLength = 0.4f;
        [Range(0.05f, 1f)] public float headWidth = 1f;
        [Range(0.02f, 1f)] public float shaftWidth = 0.4f;

        public override string DisplayName => "Arrow";

        float4 Params(in ShapeFrame frame)
        {
            float2 hs = math.max(frame.HalfSize, 1e-6f);
            return new float4(hs.x, hs.y * headWidth, hs.x * 2f * headLength, hs.y * shaftWidth * headWidth);
        }

        public override bool TryGetAnalytic(in ShapeFrame frame, out AnalyticShape shape)
        {
            shape = Make(frame, ShapeKind.Arrow, Params(frame), float2.zero, frame.HalfSize, 0, true);
            return true;
        }

        public override void BuildPath(in ShapeFrame frame, PathBuilder b)
        {
            var saved = EnterShapeSpace(frame, b);
            float4 p = Params(frame);
            float hl = p.x, hw = p.y, head = math.min(p.z, 2f * hl), sw = math.min(p.w, hw);
            float bx = hl - head;
            ShapePaths.Polygon(b, new[]
            {
                new float2(-hl, -sw), new float2(bx, -sw), new float2(bx, -hw), new float2(hl, 0f),
                new float2(bx, hw), new float2(bx, sw), new float2(-hl, sw)
            });
            b.transform = saved;
        }

        protected override void Hash(ref HashBuilder h)
        {
            h.Add(headLength);
            h.Add(headWidth);
            h.Add(shaftWidth);
        }
    }

    /// <summary>Chevron pointing to +X with round joins and caps.</summary>
    [Serializable]
    public sealed class ChevronShape : VectraShapeGeometry
    {
        [Min(0.01f)] public float thickness = 8f;

        public override string DisplayName => "Chevron";

        public override bool TryGetAnalytic(in ShapeFrame frame, out AnalyticShape shape)
        {
            float2 hs = math.max(frame.HalfSize, 1e-6f);
            float ht = math.min(thickness * 0.5f, math.cmin(hs) * 0.5f);
            shape = Make(frame, ShapeKind.Chevron, new float4(hs, ht, 0f), float2.zero, hs);
            return true;
        }

        public override void BuildPath(in ShapeFrame frame, PathBuilder b)
        {
            var saved = EnterShapeSpace(frame, b);
            float2 hs = math.max(frame.HalfSize, 1e-6f);
            float ht = math.min(thickness * 0.5f, math.cmin(hs) * 0.5f);
            float2 pa = new float2(-hs.x + ht, hs.y - ht), pb = new float2(hs.x - ht, 0f), pc = new float2(-hs.x + ht, -hs.y + ht);
            Capsule(b, pa, pb, ht);
            Capsule(b, pb, pc, ht);
            b.transform = saved;
        }

        static void Capsule(PathBuilder b, float2 p0, float2 p1, float hw)
        {
            float2 d = math.normalizesafe(p1 - p0, new float2(1f, 0f));
            float a = math.atan2(d.x, -d.y);
            b.AppendEllipticArc(p1, new float2(hw), 0f, a - math.PI, math.PI, true);
            b.AppendEllipticArc(p0, new float2(hw), 0f, a, math.PI, false);
            b.Close();
        }

        protected override void Hash(ref HashBuilder h) => h.Add(thickness);
    }
}
