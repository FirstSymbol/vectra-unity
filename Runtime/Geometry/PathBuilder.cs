using Unity.Mathematics;

namespace FirstSymbol.Vectra.Geometry
{
    /// <summary>
    /// Fluent writer for <see cref="NativePath"/>. Every point is transformed by <see cref="transform"/> when written,
    /// so shapes can be emitted in their own frame. Also tracks the current point and last control point for SVG
    /// smooth-curve commands.
    /// </summary>
    public sealed class PathBuilder
    {
        /// <summary>Circle approximation constant for cubic arcs.</summary>
        public const float Kappa = 0.5522847498f;

        public NativePath path;
        public Affine2 transform;

        float2 m_Current;
        float2 m_Start;
        float2 m_LastControl;
        bool m_HasCurrent;
        bool m_HasOpenFigure;

        public PathBuilder(NativePath path) : this(path, Affine2.Identity) { }

        public PathBuilder(NativePath path, Affine2 transform)
        {
            Reset(path, transform);
        }

        /// <summary>Re-targets the builder (allows reusing one instance without allocations).</summary>
        public PathBuilder Reset(NativePath target, Affine2 xf)
        {
            path = target;
            transform = xf;
            m_Current = m_Start = m_LastControl = float2.zero;
            m_HasCurrent = false;
            m_HasOpenFigure = false;
            return this;
        }

        /// <summary>Current point in untransformed (builder) coordinates.</summary>
        public float2 Current => m_Current;
        public float2 LastControl => m_LastControl;
        public bool HasCurrent => m_HasCurrent;

        float2 X(float2 p) => transform.Apply(p);

        public PathBuilder MoveTo(float2 p)
        {
            path.MoveTo(X(p));
            m_Current = m_Start = m_LastControl = p;
            m_HasCurrent = true;
            m_HasOpenFigure = true;
            return this;
        }

        void EnsureFigure()
        {
            if (m_HasOpenFigure) return;
            path.MoveTo(X(m_Current));
            m_Start = m_Current;
            m_HasOpenFigure = true;
            m_HasCurrent = true;
        }

        public PathBuilder LineTo(float2 p)
        {
            EnsureFigure();
            path.LineTo(X(p));
            m_Current = m_LastControl = p;
            return this;
        }

        public PathBuilder QuadTo(float2 c, float2 p)
        {
            EnsureFigure();
            path.QuadTo(X(c), X(p));
            m_LastControl = c;
            m_Current = p;
            return this;
        }

        public PathBuilder CubicTo(float2 c1, float2 c2, float2 p)
        {
            EnsureFigure();
            path.CubicTo(X(c1), X(c2), X(p));
            m_LastControl = c2;
            m_Current = p;
            return this;
        }

        public PathBuilder Close()
        {
            if (!m_HasOpenFigure) return this;
            path.Close();
            m_Current = m_LastControl = m_Start;
            m_HasOpenFigure = false;
            return this;
        }

        /// <summary>SVG elliptical arc from the current point to <paramref name="end"/>.</summary>
        public PathBuilder ArcTo(float2 radii, float xAxisRotationDeg, bool largeArc, bool sweep, float2 end)
        {
            EnsureFigure();
            float2 start = m_Current;
            float rx = math.abs(radii.x), ry = math.abs(radii.y);
            if (math.all(start == end)) return this;
            if (rx < 1e-9f || ry < 1e-9f) return LineTo(end);

            float phi = math.radians(xAxisRotationDeg);
            math.sincos(phi, out float sinPhi, out float cosPhi);
            float2 d = (start - end) * 0.5f;
            float x1 = cosPhi * d.x + sinPhi * d.y;
            float y1 = -sinPhi * d.x + cosPhi * d.y;

            float lambda = (x1 * x1) / (rx * rx) + (y1 * y1) / (ry * ry);
            if (lambda > 1f)
            {
                float s = math.sqrt(lambda);
                rx *= s;
                ry *= s;
            }

            float num = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1;
            float den = rx * rx * y1 * y1 + ry * ry * x1 * x1;
            float coef = den > 0f ? math.sqrt(math.max(num / den, 0f)) : 0f;
            if (largeArc == sweep) coef = -coef;
            float cx1 = coef * rx * y1 / ry;
            float cy1 = -coef * ry * x1 / rx;
            float2 mid = (start + end) * 0.5f;
            float2 center = new float2(cosPhi * cx1 - sinPhi * cy1, sinPhi * cx1 + cosPhi * cy1) + mid;

            float theta1 = Angle(new float2(1, 0), new float2((x1 - cx1) / rx, (y1 - cy1) / ry));
            float dTheta = Angle(new float2((x1 - cx1) / rx, (y1 - cy1) / ry),
                new float2((-x1 - cx1) / rx, (-y1 - cy1) / ry));
            if (!sweep && dTheta > 0f) dTheta -= 2f * math.PI;
            else if (sweep && dTheta < 0f) dTheta += 2f * math.PI;

            AppendEllipticArc(center, new float2(rx, ry), phi, theta1, dTheta, false);
            m_Current = m_LastControl = end;
            return this;
        }

        static float Angle(float2 u, float2 v)
        {
            float dot = math.dot(u, v);
            float len = math.length(u) * math.length(v);
            float a = math.acos(math.clamp(len > 0f ? dot / len : 1f, -1f, 1f));
            return (u.x * v.y - u.y * v.x) < 0f ? -a : a;
        }

        /// <summary>
        /// Appends an elliptic arc as cubic segments of at most 90°. When <paramref name="moveToStart"/> is set the arc
        /// starts a new figure, otherwise a line joins the current point to the arc start.
        /// </summary>
        public PathBuilder AppendEllipticArc(float2 center, float2 radii, float rotation, float startAngle, float sweep,
            bool moveToStart)
        {
            int segs = math.max(1, (int)math.ceil(math.abs(sweep) / (math.PI * 0.5f) - 1e-4f));
            float delta = sweep / segs;
            float k = 4f / 3f * math.tan(delta * 0.25f);
            math.sincos(rotation, out float sr, out float cr);

            float2 P(float a)
            {
                math.sincos(a, out float s, out float c);
                float2 e = new float2(c * radii.x, s * radii.y);
                return center + new float2(cr * e.x - sr * e.y, sr * e.x + cr * e.y);
            }

            float2 T(float a)
            {
                math.sincos(a, out float s, out float c);
                float2 e = new float2(-s * radii.x, c * radii.y);
                return new float2(cr * e.x - sr * e.y, sr * e.x + cr * e.y);
            }

            float a0 = startAngle;
            float2 p0 = P(a0);
            if (moveToStart || !m_HasCurrent) MoveTo(p0);
            else if (math.distancesq(p0, m_Current) > 1e-12f) LineTo(p0);
            else EnsureFigure();

            for (int i = 0; i < segs; i++)
            {
                float a1 = a0 + delta;
                float2 p1 = P(a1);
                CubicTo(p0 + T(a0) * k, p1 - T(a1) * k, p1);
                a0 = a1;
                p0 = p1;
            }

            return this;
        }

        public PathBuilder AddRect(float2 min, float2 size)
        {
            MoveTo(min);
            LineTo(new float2(min.x + size.x, min.y));
            LineTo(min + size);
            LineTo(new float2(min.x, min.y + size.y));
            return Close();
        }

        /// <summary>Rounded rectangle. Radii are (top-left, top-right, bottom-right, bottom-left) in a y-up frame.</summary>
        public PathBuilder AddRoundedRect(float2 min, float2 size, float4 radii)
        {
            float2 max = min + size;
            float limit = math.min(size.x, size.y) * 0.5f;
            radii = math.clamp(radii, 0f, limit);
            if (math.all(radii <= 0f)) return AddRect(min, size);
            float tl = radii.x, tr = radii.y, br = radii.z, bl = radii.w;
            float k = 1f - Kappa;

            MoveTo(new float2(min.x + bl, min.y));
            LineTo(new float2(max.x - br, min.y));
            if (br > 0f) CubicTo(new float2(max.x - br * k, min.y), new float2(max.x, min.y + br * k), new float2(max.x, min.y + br));
            LineTo(new float2(max.x, max.y - tr));
            if (tr > 0f) CubicTo(new float2(max.x, max.y - tr * k), new float2(max.x - tr * k, max.y), new float2(max.x - tr, max.y));
            LineTo(new float2(min.x + tl, max.y));
            if (tl > 0f) CubicTo(new float2(min.x + tl * k, max.y), new float2(min.x, max.y - tl * k), new float2(min.x, max.y - tl));
            LineTo(new float2(min.x, min.y + bl));
            if (bl > 0f) CubicTo(new float2(min.x, min.y + bl * k), new float2(min.x + bl * k, min.y), new float2(min.x + bl, min.y));
            return Close();
        }

        public PathBuilder AddEllipse(float2 center, float2 radii)
        {
            float2 r = radii;
            float2 k = r * Kappa;
            MoveTo(center + new float2(r.x, 0));
            CubicTo(center + new float2(r.x, k.y), center + new float2(k.x, r.y), center + new float2(0, r.y));
            CubicTo(center + new float2(-k.x, r.y), center + new float2(-r.x, k.y), center + new float2(-r.x, 0));
            CubicTo(center + new float2(-r.x, -k.y), center + new float2(-k.x, -r.y), center + new float2(0, -r.y));
            CubicTo(center + new float2(k.x, -r.y), center + new float2(r.x, -k.y), center + new float2(r.x, 0));
            return Close();
        }

        public PathBuilder AddCircle(float2 center, float radius) => AddEllipse(center, new float2(radius));

        /// <summary>Open circular arc (angles in radians, counter-clockwise positive).</summary>
        public PathBuilder AddArc(float2 center, float2 radii, float startAngle, float sweep) =>
            AppendEllipticArc(center, radii, 0f, startAngle, sweep, true);

        /// <summary>Closed pie slice or, with an inner radius, an annular sector.</summary>
        public PathBuilder AddSector(float2 center, float2 outer, float2 inner, float startAngle, float sweep)
        {
            sweep = math.clamp(sweep, -2f * math.PI, 2f * math.PI);
            bool full = math.abs(sweep) >= 2f * math.PI - 1e-5f;
            bool hasInner = inner.x > 0f && inner.y > 0f;
            if (full)
            {
                AppendEllipticArc(center, outer, 0f, startAngle, 2f * math.PI, true);
                Close();
                if (hasInner)
                {
                    AppendEllipticArc(center, inner, 0f, startAngle, -2f * math.PI, true);
                    Close();
                }

                return this;
            }

            AppendEllipticArc(center, outer, 0f, startAngle, sweep, true);
            if (hasInner) AppendEllipticArc(center, inner, 0f, startAngle + sweep, -sweep, false);
            else LineTo(center);
            return Close();
        }

        public PathBuilder AddPolygon(float2[] points, bool closed = true)
        {
            if (points == null || points.Length == 0) return this;
            MoveTo(points[0]);
            for (int i = 1; i < points.Length; i++) LineTo(points[i]);
            return closed ? Close() : this;
        }
    }
}
