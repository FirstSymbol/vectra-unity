using System;
using FirstSymbol.Vectra.Geometry;
using FirstSymbol.Vectra.Rendering;
using Unity.Mathematics;
using UnityEngine;

namespace FirstSymbol.Vectra
{
    /// <summary>The frame a shape is laid out in: the local rect of the component and the size of a screen pixel.</summary>
    public struct ShapeFrame
    {
        public Rect rect;
        /// <summary>Approximate size of one screen pixel in local units (used for tolerances and anti-aliasing).</summary>
        public float pixelSize;

        public float2 Center => new float2(rect.center.x, rect.center.y);
        public float2 HalfSize => new float2(rect.width, rect.height) * 0.5f;
        public float2 Min => new float2(rect.xMin, rect.yMin);
        public float2 Size => new float2(rect.width, rect.height);
        public float MinHalf => math.min(rect.width, rect.height) * 0.5f;

        /// <summary>Normalized (0..1) rect coordinates to local coordinates.</summary>
        public float2 FromNormalized(Vector2 n) => Min + Size * new float2(n.x, n.y);
    }

    /// <summary>Description of an analytic (signed distance) shape for the shader.</summary>
    public struct AnalyticShape
    {
        public ShapeKind kind;
        public int style;
        public float4 s1;
        public float2 s2;
        /// <summary>Maps local positions into the shape space the distance function is defined in.</summary>
        public Affine2 localToShape;
        /// <summary>Half extents of the shape in shape space (centred at the origin).</summary>
        public float2 halfExtents;
        /// <summary>True when the outline has sharp convex corners (miter strokes must then use geometry).</summary>
        public bool sharpCorners;
    }

    /// <summary>
    /// Base class of all shapes. A shape can describe itself analytically (rendered with an exact distance function
    /// in the shader) and always as a path (used for boolean operations, dashed strokes, exports and colliders).
    /// </summary>
    [Serializable]
    public abstract class VectraShapeGeometry
    {
        /// <summary>Rotation of the shape inside its rect, in degrees (counter-clockwise).</summary>
        public float rotation;

        public abstract string DisplayName { get; }

        public virtual FillRule FillRule => FillRule.NonZero;

        /// <summary>When false the shape is an open path (a line); fills still close it like SVG does.</summary>
        public virtual bool IsClosed => true;

        public virtual bool TryGetAnalytic(in ShapeFrame frame, out AnalyticShape shape)
        {
            shape = default;
            return false;
        }

        /// <summary>Writes the outline in local coordinates.</summary>
        public abstract void BuildPath(in ShapeFrame frame, PathBuilder builder);

        public virtual VectraShapeGeometry Clone() => (VectraShapeGeometry)MemberwiseClone();

        public int ComputeHash()
        {
            var h = new HashBuilder(GetType().GetHashCode());
            h.Add(rotation);
            Hash(ref h);
            return h.Value;
        }

        protected abstract void Hash(ref HashBuilder h);

        /// <summary>Local-to-shape transform: centre on the rect and undo the rotation.</summary>
        protected Affine2 ShapeSpace(in ShapeFrame frame) =>
            Affine2.Rotate(-math.radians(rotation)) * Affine2.Translate(-frame.Center);

        /// <summary>Shape-to-local transform for path generation in shape space.</summary>
        protected Affine2 LocalSpace(in ShapeFrame frame) =>
            Affine2.Translate(frame.Center) * Affine2.Rotate(math.radians(rotation));

        protected AnalyticShape Make(in ShapeFrame frame, ShapeKind kind, float4 s1, float2 s2, float2 halfExtents,
            int style = 0, bool sharp = false)
        {
            return new AnalyticShape
            {
                kind = kind,
                style = style,
                s1 = s1,
                s2 = s2,
                localToShape = ShapeSpace(frame),
                halfExtents = halfExtents,
                sharpCorners = sharp
            };
        }

        /// <summary>Switches the builder to shape space; returns the previous transform to restore afterwards.</summary>
        protected Affine2 EnterShapeSpace(in ShapeFrame frame, PathBuilder b)
        {
            var saved = b.transform;
            b.transform = saved * LocalSpace(frame);
            return saved;
        }
    }

    /// <summary>Allocation-free hash accumulator.</summary>
    public struct HashBuilder
    {
        int m_H;

        public HashBuilder(int seed) => m_H = seed ^ unchecked((int)2166136261);

        public int Value => m_H;

        public void Add(int v) => m_H = unchecked(m_H * 16777619 ^ v);
        public void Add(float v) => Add(v.GetHashCode());
        public void Add(bool v) => Add(v ? 1 : 0);
        public void Add(Vector2 v)
        {
            Add(v.x);
            Add(v.y);
        }

        public void Add(Vector4 v)
        {
            Add(v.x);
            Add(v.y);
            Add(v.z);
            Add(v.w);
        }

        public void Add(Color c)
        {
            Add(c.r);
            Add(c.g);
            Add(c.b);
            Add(c.a);
        }

        public void Add(string s) => Add(s?.GetHashCode() ?? 0);
    }

    /// <summary>Path helpers shared by shapes.</summary>
    public static class ShapePaths
    {
        /// <summary>
        /// Closed polygon whose corners are rounded with circular arcs of up to <paramref name="radius"/> (limited by
        /// half the adjacent edge lengths).
        /// </summary>
        public static void RoundedPolygon(PathBuilder b, ReadOnlySpanFloat2 pts, float radius)
        {
            int n = pts.Length;
            if (n < 3) return;
            if (radius <= 0f)
            {
                b.MoveTo(pts[0]);
                for (int i = 1; i < n; i++) b.LineTo(pts[i]);
                b.Close();
                return;
            }

            for (int i = 0; i < n; i++)
            {
                float2 prev = pts[(i - 1 + n) % n], cur = pts[i], next = pts[(i + 1) % n];
                float2 d0 = math.normalizesafe(prev - cur), d1 = math.normalizesafe(next - cur);
                float cosA = math.clamp(math.dot(d0, d1), -1f, 1f);
                float half = math.acos(cosA) * 0.5f;
                float t = math.tan(half);
                float maxR = math.min(math.distance(prev, cur), math.distance(next, cur)) * 0.5f * t;
                float r = math.min(radius, maxR);
                float dist = t > 1e-6f ? r / t : 0f;
                float2 a = cur + d0 * dist;
                float2 c = cur + d1 * dist;
                if (i == 0) b.MoveTo(a);
                else b.LineTo(a);
                if (dist > 1e-6f && r > 1e-6f)
                {
                    // Circular arc approximated with a cubic (angle = pi - 2*half).
                    float sweep = math.PI - 2f * half;
                    float k = 4f / 3f * math.tan(sweep * 0.25f) * r;
                    float2 ta = -d0;
                    float2 tc = d1;
                    b.CubicTo(a + ta * k, c - tc * k, c);
                }
            }

            b.Close();
        }

        /// <summary>
        /// Minkowski sum of a polygon and a disk of radius <paramref name="r"/>: convex corners become arcs centred on
        /// the original vertices, reflex corners stay sharp. Matches the "distance - r" rounding of the shaders.
        /// </summary>
        public static void InflatedPolygon(PathBuilder b, float2[] pts, float r)
        {
            int n = pts.Length;
            if (n < 3) return;
            float area = 0f;
            for (int i = 0, j = n - 1; i < n; j = i++) area += pts[j].x * pts[i].y - pts[i].x * pts[j].y;
            if (area < 0f) System.Array.Reverse(pts);
            if (r <= 0f)
            {
                Polygon(b, pts);
                return;
            }

            for (int i = 0; i < n; i++)
            {
                float2 v = pts[i];
                float2 dPrev = math.normalizesafe(v - pts[(i - 1 + n) % n]);
                float2 dNext = math.normalizesafe(pts[(i + 1) % n] - v);
                float2 nPrev = new float2(dPrev.y, -dPrev.x);
                float2 nNext = new float2(dNext.y, -dNext.x);
                float cross = dPrev.x * dNext.y - dPrev.y * dNext.x;
                if (cross > 1e-6f)
                {
                    float2 p1 = v + nPrev * r;
                    if (i == 0) b.MoveTo(p1);
                    else b.LineTo(p1);
                    float a0 = math.atan2(nPrev.y, nPrev.x);
                    float sweep = math.acos(math.clamp(math.dot(nPrev, nNext), -1f, 1f));
                    b.AppendEllipticArc(v, new float2(r, r), 0f, a0, sweep, false);
                }
                else
                {
                    float denom = 1f + math.dot(nPrev, nNext);
                    float2 q = denom > 1e-4f ? v + (nPrev + nNext) * (r / denom) : v + nPrev * r;
                    if (i == 0) b.MoveTo(q);
                    else b.LineTo(q);
                }
            }

            b.Close();
        }

        public static void Polygon(PathBuilder b, ReadOnlySpanFloat2 pts, bool close = true)
        {
            if (pts.Length == 0) return;
            b.MoveTo(pts[0]);
            for (int i = 1; i < pts.Length; i++) b.LineTo(pts[i]);
            if (close) b.Close();
        }
    }

    /// <summary>Lightweight read-only view over a float2 array (keeps the API usable from older C# targets).</summary>
    public readonly struct ReadOnlySpanFloat2
    {
        readonly float2[] m_Array;
        readonly int m_Count;

        public ReadOnlySpanFloat2(float2[] array, int count = -1)
        {
            m_Array = array;
            m_Count = count < 0 ? array?.Length ?? 0 : count;
        }

        public int Length => m_Count;
        public float2 this[int i] => m_Array[i];

        public static implicit operator ReadOnlySpanFloat2(float2[] a) => new ReadOnlySpanFloat2(a);
    }
}
