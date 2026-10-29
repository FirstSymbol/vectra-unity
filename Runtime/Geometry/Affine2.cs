using System;
using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace FirstSymbol.Vectra.Geometry
{
    /// <summary>
    /// 2D affine transform using the SVG convention <c>matrix(a, b, c, d, e, f)</c>:
    /// <c>x' = a*x + c*y + e</c>, <c>y' = b*x + d*y + f</c>.
    /// </summary>
    [Serializable]
    public struct Affine2 : IEquatable<Affine2>
    {
        public float a, b, c, d, e, f;

        public static readonly Affine2 Identity = new Affine2(1, 0, 0, 1, 0, 0);

        public Affine2(float a, float b, float c, float d, float e, float f)
        {
            this.a = a;
            this.b = b;
            this.c = c;
            this.d = d;
            this.e = e;
            this.f = f;
        }

        public bool IsIdentity => a == 1 && b == 0 && c == 0 && d == 1 && e == 0 && f == 0;

        public static Affine2 Translate(float2 t) => new Affine2(1, 0, 0, 1, t.x, t.y);
        public static Affine2 Scale(float2 s) => new Affine2(s.x, 0, 0, s.y, 0, 0);

        public static Affine2 Rotate(float radians)
        {
            math.sincos(radians, out float s, out float co);
            return new Affine2(co, s, -s, co, 0, 0);
        }

        public static Affine2 Rotate(float radians, float2 pivot) =>
            Translate(pivot) * Rotate(radians) * Translate(-pivot);

        public static Affine2 SkewX(float radians) => new Affine2(1, 0, math.tan(radians), 1, 0, 0);
        public static Affine2 SkewY(float radians) => new Affine2(1, math.tan(radians), 0, 1, 0, 0);

        /// <summary>Maps the unit square [0,1]² onto <paramref name="rectMin"/>..<paramref name="rectMax"/>.</summary>
        public static Affine2 FromRect(float2 rectMin, float2 rectMax) =>
            new Affine2(rectMax.x - rectMin.x, 0, 0, rectMax.y - rectMin.y, rectMin.x, rectMin.y);

        /// <summary>Composition: <c>(l * r).Apply(p) == l.Apply(r.Apply(p))</c>.</summary>
        public static Affine2 operator *(Affine2 l, Affine2 r) => new Affine2(
            l.a * r.a + l.c * r.b,
            l.b * r.a + l.d * r.b,
            l.a * r.c + l.c * r.d,
            l.b * r.c + l.d * r.d,
            l.a * r.e + l.c * r.f + l.e,
            l.b * r.e + l.d * r.f + l.f);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float2 Apply(float2 p) => new float2(a * p.x + c * p.y + e, b * p.x + d * p.y + f);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float2 ApplyVector(float2 v) => new float2(a * v.x + c * v.y, b * v.x + d * v.y);

        public float Determinant => a * d - b * c;

        /// <summary>Geometric mean of the axis scales; used to convert tolerances and stroke widths.</summary>
        public float UniformScale => math.sqrt(math.abs(Determinant));

        public Affine2 Inverse()
        {
            float det = Determinant;
            if (math.abs(det) < 1e-20f) return Identity;
            float inv = 1f / det;
            float ia = d * inv, ib = -b * inv, ic = -c * inv, id = a * inv;
            return new Affine2(ia, ib, ic, id, -(ia * e + ic * f), -(ib * e + id * f));
        }

        public bool Equals(Affine2 o) => a == o.a && b == o.b && c == o.c && d == o.d && e == o.e && f == o.f;
        public override bool Equals(object obj) => obj is Affine2 o && Equals(o);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = a.GetHashCode();
                h = h * 397 ^ b.GetHashCode();
                h = h * 397 ^ c.GetHashCode();
                h = h * 397 ^ d.GetHashCode();
                h = h * 397 ^ e.GetHashCode();
                return h * 397 ^ f.GetHashCode();
            }
        }

        public override string ToString() => $"matrix({a}, {b}, {c}, {d}, {e}, {f})";
    }
}
