using System;
using System.Collections.Generic;
using FirstSymbol.Vectra.Geometry;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace FirstSymbol.Vectra
{
    public enum BezierPointMode
    {
        /// <summary>Tangents are independent (sharp or broken corner).</summary>
        Corner,
        /// <summary>Tangents are collinear but may have different lengths.</summary>
        Smooth,
        /// <summary>Tangents are collinear with equal lengths.</summary>
        Symmetric
    }

    public enum PathCoordinates
    {
        /// <summary>Points are normalized rect coordinates (0..1); the path stretches with the rect.</summary>
        Normalized,
        /// <summary>Points are local coordinates (the same units as the component).</summary>
        Local
    }

    [Serializable]
    public struct BezierPoint
    {
        public Vector2 position;
        /// <summary>Incoming handle, relative to the position.</summary>
        public Vector2 inTangent;
        /// <summary>Outgoing handle, relative to the position.</summary>
        public Vector2 outTangent;
        public BezierPointMode mode;

        public BezierPoint(Vector2 position, Vector2 inTangent = default, Vector2 outTangent = default,
            BezierPointMode mode = BezierPointMode.Corner)
        {
            this.position = position;
            this.inTangent = inTangent;
            this.outTangent = outTangent;
            this.mode = mode;
        }
    }

    [Serializable]
    public class BezierContour
    {
        public List<BezierPoint> points = new List<BezierPoint>();
        public bool closed = true;

        public BezierContour Clone() => new BezierContour { points = new List<BezierPoint>(points), closed = closed };
    }

    /// <summary>Freely editable Bezier path made of any number of contours (the Scene view path tool edits it).</summary>
    [Serializable]
    public sealed class PathShape : VectraShapeGeometry
    {
        public List<BezierContour> contours = new List<BezierContour>();
        public PathCoordinates coordinates = PathCoordinates.Normalized;
        public FillRule fillRule = FillRule.NonZero;

        public override string DisplayName => "Path";
        public override FillRule FillRule => fillRule;

        public override bool IsClosed
        {
            get
            {
                if (contours == null) return true;
                foreach (var c in contours)
                    if (c != null && !c.closed) return false;
                return true;
            }
        }

        public PathShape()
        {
            contours.Add(new BezierContour
            {
                points = new List<BezierPoint>
                {
                    new BezierPoint(new Vector2(0.5f, 1f), new Vector2(-0.25f, 0f), new Vector2(0.25f, 0f), BezierPointMode.Symmetric),
                    new BezierPoint(new Vector2(1f, 0f)),
                    new BezierPoint(new Vector2(0f, 0f))
                },
                closed = true
            });
        }

        public override VectraShapeGeometry Clone()
        {
            var p = (PathShape)MemberwiseClone();
            p.contours = new List<BezierContour>();
            foreach (var c in contours) p.contours.Add(c.Clone());
            return p;
        }

        /// <summary>Converts a stored point/handle to local coordinates.</summary>
        public float2 ToLocal(in ShapeFrame frame, Vector2 v, bool isVector)
        {
            if (coordinates == PathCoordinates.Local) return new float2(v.x, v.y);
            return isVector ? new float2(v.x, v.y) * frame.Size : frame.FromNormalized(v);
        }

        public Vector2 FromLocal(in ShapeFrame frame, float2 v, bool isVector)
        {
            if (coordinates == PathCoordinates.Local) return new Vector2(v.x, v.y);
            float2 size = math.max(frame.Size, 1e-6f);
            float2 n = isVector ? v / size : (v - frame.Min) / size;
            return new Vector2(n.x, n.y);
        }

        public override void BuildPath(in ShapeFrame frame, PathBuilder b)
        {
            if (contours == null) return;
            var saved = b.transform;
            if (rotation != 0f) b.transform = saved * Affine2.Rotate(math.radians(rotation), frame.Center);
            foreach (var c in contours)
            {
                if (c?.points == null || c.points.Count == 0) continue;
                int n = c.points.Count;
                b.MoveTo(ToLocal(frame, c.points[0].position, false));
                int segs = c.closed ? n : n - 1;
                for (int i = 0; i < segs; i++)
                {
                    var p0 = c.points[i];
                    var p1 = c.points[(i + 1) % n];
                    float2 a = ToLocal(frame, p0.position, false);
                    float2 d = ToLocal(frame, p1.position, false);
                    if (p0.outTangent == Vector2.zero && p1.inTangent == Vector2.zero)
                        b.LineTo(d);
                    else
                        b.CubicTo(a + ToLocal(frame, p0.outTangent, true), d + ToLocal(frame, p1.inTangent, true), d);
                }

                if (c.closed) b.Close();
            }

            b.transform = saved;
        }

        protected override void Hash(ref HashBuilder h)
        {
            h.Add((int)coordinates);
            h.Add((int)fillRule);
            if (contours == null) return;
            foreach (var c in contours)
            {
                if (c == null) continue;
                h.Add(c.closed);
                foreach (var p in c.points)
                {
                    h.Add(p.position);
                    h.Add(p.inTangent);
                    h.Add(p.outTangent);
                }
            }
        }
    }

    /// <summary>Polyline or polygon through points, with optional rounded corners.</summary>
    [Serializable]
    public sealed class PolylineShape : VectraShapeGeometry
    {
        public List<Vector2> points = new List<Vector2>
        {
            new Vector2(0f, 0f), new Vector2(0.35f, 1f), new Vector2(0.65f, 0.3f), new Vector2(1f, 0.8f)
        };
        public bool closed;
        [Min(0f)] public float cornerRadius;
        public PathCoordinates coordinates = PathCoordinates.Normalized;

        public override string DisplayName => "Polyline / Polygon";
        public override bool IsClosed => closed;

        public override VectraShapeGeometry Clone()
        {
            var p = (PolylineShape)MemberwiseClone();
            p.points = new List<Vector2>(points);
            return p;
        }

        public override void BuildPath(in ShapeFrame frame, PathBuilder b)
        {
            if (points == null || points.Count < 2) return;
            var saved = b.transform;
            if (rotation != 0f) b.transform = saved * Affine2.Rotate(math.radians(rotation), frame.Center);
            var pts = new float2[points.Count];
            for (int i = 0; i < pts.Length; i++)
                pts[i] = coordinates == PathCoordinates.Local ? (float2)points[i] : frame.FromNormalized(points[i]);

            if (closed && pts.Length >= 3) ShapePaths.RoundedPolygon(b, pts, cornerRadius);
            else OpenRounded(b, pts, cornerRadius);
            b.transform = saved;
        }

        static void OpenRounded(PathBuilder b, float2[] pts, float radius)
        {
            b.MoveTo(pts[0]);
            for (int i = 1; i < pts.Length - 1; i++)
            {
                float2 prev = pts[i - 1], cur = pts[i], next = pts[i + 1];
                if (radius <= 0f)
                {
                    b.LineTo(cur);
                    continue;
                }

                float2 d0 = math.normalizesafe(prev - cur), d1 = math.normalizesafe(next - cur);
                float half = math.acos(math.clamp(math.dot(d0, d1), -1f, 1f)) * 0.5f;
                float t = math.tan(half);
                float maxR = math.min(math.distance(prev, cur), math.distance(next, cur)) * 0.5f * t;
                float r = math.min(radius, maxR);
                float dist = t > 1e-6f ? r / t : 0f;
                float2 a = cur + d0 * dist, c = cur + d1 * dist;
                b.LineTo(a);
                if (dist > 1e-6f)
                {
                    float k = 4f / 3f * math.tan((math.PI - 2f * half) * 0.25f) * r;
                    b.CubicTo(a - d0 * k, c - d1 * k, c);
                }
            }

            b.LineTo(pts[pts.Length - 1]);
        }

        protected override void Hash(ref HashBuilder h)
        {
            h.Add(closed);
            h.Add(cornerRadius);
            h.Add((int)coordinates);
            if (points != null)
                foreach (var p in points) h.Add(p);
        }
    }

    /// <summary>Archimedean spiral (an open path; give it a stroke).</summary>
    [Serializable]
    public sealed class SpiralShape : VectraShapeGeometry
    {
        [Min(0.1f)] public float turns = 3f;
        [Range(0f, 1f)] public float innerRadius = 0.05f;
        public bool clockwise;

        public override string DisplayName => "Spiral";
        public override bool IsClosed => false;

        public override void BuildPath(in ShapeFrame frame, PathBuilder b)
        {
            var saved = EnterShapeSpace(frame, b);
            float2 hs = math.max(frame.HalfSize, 1e-6f);
            int steps = math.max(8, (int)math.ceil(turns * 72f));
            float total = turns * 2f * math.PI;
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                float a = t * total * (clockwise ? -1f : 1f);
                float r = math.lerp(innerRadius, 1f, t);
                var p = new float2(math.cos(a), math.sin(a)) * r * hs;
                if (i == 0) b.MoveTo(p);
                else b.LineTo(p);
            }

            b.transform = saved;
        }

        protected override void Hash(ref HashBuilder h)
        {
            h.Add(turns);
            h.Add(innerRadius);
            h.Add(clockwise);
        }
    }

    [Serializable]
    public sealed class GearShape : VectraShapeGeometry
    {
        [Range(3, 128)] public int teeth = 12;
        /// <summary>Root radius relative to the outer radius.</summary>
        [Range(0.3f, 0.98f)] public float rootRadius = 0.8f;
        /// <summary>Tooth width at the tip relative to the tooth pitch.</summary>
        [Range(0.05f, 0.9f)] public float toothWidth = 0.35f;
        /// <summary>Width of the tooth base relative to the pitch (larger than the tip gives tapered teeth).</summary>
        [Range(0.05f, 1f)] public float baseWidth = 0.55f;
        [Range(0f, 0.95f)] public float holeRadius = 0.25f;

        public override string DisplayName => "Gear";

        public override void BuildPath(in ShapeFrame frame, PathBuilder b)
        {
            var saved = EnterShapeSpace(frame, b);
            float R = math.max(frame.MinHalf, 1e-6f);
            float Rr = R * rootRadius;
            float pitch = 2f * math.PI / teeth;
            for (int i = 0; i < teeth; i++)
            {
                float c = i * pitch + math.PI * 0.5f;
                float hb = pitch * baseWidth * 0.5f;
                float ht = pitch * toothWidth * 0.5f;
                float2 p0 = Polar(c - hb, Rr), p1 = Polar(c - ht, R), p3 = Polar(c + hb, Rr);
                if (i == 0) b.MoveTo(p0);
                else b.LineTo(p0);
                b.LineTo(p1);
                b.AppendEllipticArc(float2.zero, new float2(R), 0f, c - ht, 2f * ht, false);
                b.LineTo(p3);
                float nextStart = (i + 1) * pitch + math.PI * 0.5f - hb;
                b.AppendEllipticArc(float2.zero, new float2(Rr), 0f, c + hb, nextStart - (c + hb), false);
            }

            b.Close();
            if (holeRadius > 0f)
            {
                b.AppendEllipticArc(float2.zero, new float2(R * holeRadius), 0f, 0f, -2f * math.PI, true);
                b.Close();
            }

            b.transform = saved;
        }

        static float2 Polar(float a, float r) => new float2(math.cos(a), math.sin(a)) * r;

        protected override void Hash(ref HashBuilder h)
        {
            h.Add(teeth);
            h.Add(rootRadius);
            h.Add(toothWidth);
            h.Add(baseWidth);
            h.Add(holeRadius);
        }
    }

    [Serializable]
    public sealed class WaveShape : VectraShapeGeometry
    {
        [Min(0.1f)] public float frequency = 2f;
        [Range(0f, 1f)] public float amplitude = 0.4f;
        public float phase;
        /// <summary>Fills the area below the wave instead of drawing an open line.</summary>
        public bool filled;

        public override string DisplayName => "Wave";
        public override bool IsClosed => filled;

        public override void BuildPath(in ShapeFrame frame, PathBuilder b)
        {
            var saved = EnterShapeSpace(frame, b);
            float2 hs = math.max(frame.HalfSize, 1e-6f);
            int steps = math.max(16, (int)math.ceil(frequency * 48f));
            float ph = math.radians(phase);
            float midY = filled ? hs.y * (1f - amplitude) : 0f;
            float amp = hs.y * amplitude;
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                float x = math.lerp(-hs.x, hs.x, t);
                float y = midY + math.sin(t * frequency * 2f * math.PI + ph) * amp;
                if (i == 0) b.MoveTo(new float2(x, y));
                else b.LineTo(new float2(x, y));
            }

            if (filled)
            {
                b.LineTo(new float2(hs.x, -hs.y));
                b.LineTo(new float2(-hs.x, -hs.y));
                b.Close();
            }

            b.transform = saved;
        }

        protected override void Hash(ref HashBuilder h)
        {
            h.Add(frequency);
            h.Add(amplitude);
            h.Add(phase);
            h.Add(filled);
        }
    }

    public enum SvgPathFit
    {
        /// <summary>SVG units map 1:1 to local units, origin at the rect's top-left corner.</summary>
        None,
        /// <summary>Uniformly scaled to fit inside the rect, centred.</summary>
        Fit,
        /// <summary>Stretched to the rect.</summary>
        Stretch
    }

    /// <summary>Shape defined by SVG path data (the <c>d</c> attribute).</summary>
    [Serializable]
    public sealed class SvgPathShape : VectraShapeGeometry
    {
        [TextArea(2, 8)] public string data = "M12 2 L15 9 L22 9 L16.5 13.5 L18.5 21 L12 16.5 L5.5 21 L7.5 13.5 L2 9 L9 9 Z";
        public SvgPathFit fit = SvgPathFit.Fit;
        public FillRule fillRule = FillRule.NonZero;

        public override string DisplayName => "SVG Path";
        public override FillRule FillRule => fillRule;

        public override void BuildPath(in ShapeFrame frame, PathBuilder b)
        {
            if (string.IsNullOrEmpty(data)) return;
            var saved = b.transform;
            var rot = rotation != 0f ? Affine2.Rotate(math.radians(rotation), frame.Center) : Affine2.Identity;
            Affine2 map;
            if (fit == SvgPathFit.None)
            {
                map = Affine2.Translate(new float2(frame.rect.xMin, frame.rect.yMax)) * Affine2.Scale(new float2(1f, -1f));
            }
            else
            {
                // Measure the control bounds first (no transform), then map them into the rect.
                var tmp = new NativePath(Allocator.Temp, 64);
                var tb = new PathBuilder(tmp);
                SvgPathParser.Parse(data, tb);
                var bounds = PathFlattener.ControlBounds(tmp.points.AsArray(), Affine2.Identity);
                tmp.Dispose();
                if (!bounds.IsValid) return;
                float2 size = math.max(bounds.Size, 1e-6f);
                float2 scale = frame.Size / size;
                if (fit == SvgPathFit.Fit) scale = new float2(math.cmin(scale));
                float2 offset = frame.Center - new float2(bounds.Center.x * scale.x, -bounds.Center.y * scale.y);
                map = Affine2.Translate(offset) * Affine2.Scale(new float2(scale.x, -scale.y));
            }

            b.transform = saved * rot * map;
            SvgPathParser.Parse(data, b);
            b.transform = saved;
        }

        protected override void Hash(ref HashBuilder h)
        {
            h.Add(data);
            h.Add((int)fit);
            h.Add((int)fillRule);
        }
    }

    [Serializable]
    public sealed class SpeechBubbleShape : VectraShapeGeometry
    {
        [Min(0f)] public float cornerRadius = 16f;
        /// <summary>Horizontal position of the tail along the bottom edge (0..1).</summary>
        [Range(0f, 1f)] public float tailPosition = 0.25f;
        [Min(0f)] public float tailWidth = 24f;
        /// <summary>Tail height relative to the rect height (taken from the bottom of the rect).</summary>
        [Range(0f, 0.9f)] public float tailHeight = 0.25f;
        /// <summary>Horizontal lean of the tail tip in local units.</summary>
        public float tailSkew = -12f;

        public override string DisplayName => "Speech Bubble";

        public override void BuildPath(in ShapeFrame frame, PathBuilder b)
        {
            var saved = EnterShapeSpace(frame, b);
            float2 hs = math.max(frame.HalfSize, 1e-6f);
            float th = hs.y * 2f * tailHeight;
            float bottom = -hs.y + th;
            float r = math.min(cornerRadius, math.min(hs.x, (hs.y * 2f - th) * 0.5f));
            float k = r * (1f - PathBuilder.Kappa);
            float tx = math.lerp(-hs.x + r, hs.x - r, tailPosition);
            float tw = math.min(tailWidth, hs.x * 2f - 2f * r);
            float t0 = math.clamp(tx - tw * 0.5f, -hs.x + r, hs.x - r);
            float t1 = math.clamp(tx + tw * 0.5f, -hs.x + r, hs.x - r);

            b.MoveTo(new float2(-hs.x + r, bottom));
            if (tw > 0f && th > 0f)
            {
                b.LineTo(new float2(t0, bottom));
                b.LineTo(new float2(tx + tailSkew, -hs.y));
                b.LineTo(new float2(t1, bottom));
            }

            b.LineTo(new float2(hs.x - r, bottom));
            b.CubicTo(new float2(hs.x - k, bottom), new float2(hs.x, bottom + k), new float2(hs.x, bottom + r));
            b.LineTo(new float2(hs.x, hs.y - r));
            b.CubicTo(new float2(hs.x, hs.y - k), new float2(hs.x - k, hs.y), new float2(hs.x - r, hs.y));
            b.LineTo(new float2(-hs.x + r, hs.y));
            b.CubicTo(new float2(-hs.x + k, hs.y), new float2(-hs.x, hs.y - k), new float2(-hs.x, hs.y - r));
            b.LineTo(new float2(-hs.x, bottom + r));
            b.CubicTo(new float2(-hs.x, bottom + k), new float2(-hs.x + k, bottom), new float2(-hs.x + r, bottom));
            b.Close();
            b.transform = saved;
        }

        protected override void Hash(ref HashBuilder h)
        {
            h.Add(cornerRadius);
            h.Add(tailPosition);
            h.Add(tailWidth);
            h.Add(tailHeight);
            h.Add(tailSkew);
        }
    }

    /// <summary>Check mark polyline (an open path; give it a round-capped stroke).</summary>
    [Serializable]
    public sealed class CheckmarkShape : VectraShapeGeometry
    {
        public Vector2 start = new Vector2(0.1f, 0.5f);
        public Vector2 corner = new Vector2(0.4f, 0.2f);
        public Vector2 end = new Vector2(0.9f, 0.85f);

        public override string DisplayName => "Checkmark";
        public override bool IsClosed => false;

        public override void BuildPath(in ShapeFrame frame, PathBuilder b)
        {
            var saved = b.transform;
            if (rotation != 0f) b.transform = saved * Affine2.Rotate(math.radians(rotation), frame.Center);
            b.MoveTo(frame.FromNormalized(start));
            b.LineTo(frame.FromNormalized(corner));
            b.LineTo(frame.FromNormalized(end));
            b.transform = saved;
        }

        protected override void Hash(ref HashBuilder h)
        {
            h.Add(start);
            h.Add(corner);
            h.Add(end);
        }
    }
}
