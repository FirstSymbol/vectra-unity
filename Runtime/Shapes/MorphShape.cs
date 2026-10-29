using System;
using FirstSymbol.Vectra.Geometry;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace FirstSymbol.Vectra
{
    /// <summary>
    /// Interpolates between two shapes. Each contour is resampled by arc length to the same number of points and the
    /// start points are aligned, so any pair of shapes morphs smoothly (animate <see cref="t"/>).
    /// </summary>
    [Serializable]
    public sealed class MorphShape : VectraShapeGeometry
    {
        [SerializeReference] public VectraShapeGeometry from = new RectangleShape();
        [SerializeReference] public VectraShapeGeometry to = new StarShape();
        [Range(0f, 1f)] public float t = 0.5f;
        [Range(16, 1024)] public int resolution = 256;

        public override string DisplayName => "Morph";

        public override VectraShapeGeometry Clone()
        {
            var m = (MorphShape)MemberwiseClone();
            m.from = from?.Clone();
            m.to = to?.Clone();
            return m;
        }

        public override void BuildPath(in ShapeFrame frame, PathBuilder b)
        {
            if (from == null || to == null || from == this || to == this) return;
            if (t <= 0f)
            {
                from.BuildPath(frame, b);
                return;
            }

            if (t >= 1f)
            {
                to.BuildPath(frame, b);
                return;
            }

            float tol = math.max(frame.pixelSize, 1e-4f) * 0.25f;
            var pa = Outline(from, frame, tol);
            var pb = Outline(to, frame, tol);
            int n = math.max(resolution, 16);
            int contours = math.max(pa.contours.Length, pb.contours.Length);
            var ra = new NativeArray<float2>(n, Allocator.Temp);
            var rb = new NativeArray<float2>(n, Allocator.Temp);
            var saved = b.transform;
            if (rotation != 0f) b.transform = saved * Affine2.Rotate(math.radians(rotation), frame.Center);
            for (int ci = 0; ci < contours; ci++)
            {
                bool hasA = ci < pa.contours.Length, hasB = ci < pb.contours.Length;
                // A missing contour collapses to (or grows from) the centre of its counterpart.
                if (hasA) Resample(pa, pa.contours[ci], ra);
                if (hasB) Resample(pb, pb.contours[ci], rb);
                if (!hasA) Collapse(rb, ra);
                if (!hasB) Collapse(ra, rb);
                int shift = BestShift(ra, rb);
                for (int i = 0; i < n; i++)
                {
                    float2 p = math.lerp(ra[i], rb[(i + shift) % n], t);
                    if (i == 0) b.MoveTo(p);
                    else b.LineTo(p);
                }

                b.Close();
            }

            b.transform = saved;
            ra.Dispose();
            rb.Dispose();
            pa.Dispose();
            pb.Dispose();
        }

        static Polylines Outline(VectraShapeGeometry g, in ShapeFrame frame, float tol)
        {
            var path = new NativePath(Allocator.TempJob, 32);
            var poly = new Polylines(Allocator.TempJob, 128);
            g.BuildPath(frame, new PathBuilder(path));
            VectraGeometry.Flatten(path, Affine2.Identity, tol, 0, true, ref poly);
            path.Dispose();
            // Orientation must match for a clean morph: make every contour counter-clockwise.
            for (int ci = 0; ci < poly.contours.Length; ci++)
            {
                var c = poly.contours[ci];
                float area = 0f;
                for (int i = 0, j = c.count - 1; i < c.count; j = i++)
                    area += poly.points[c.start + j].x * poly.points[c.start + i].y - poly.points[c.start + i].x * poly.points[c.start + j].y;
                if (area >= 0f) continue;
                for (int i = 0; i < c.count / 2; i++)
                {
                    int a = c.start + i, z = c.start + c.count - 1 - i;
                    (poly.points[a], poly.points[z]) = (poly.points[z], poly.points[a]);
                }
            }

            return poly;
        }

        static void Resample(in Polylines p, Contour c, NativeArray<float2> outPts)
        {
            int n = outPts.Length;
            float total = 0f;
            for (int i = 0; i < c.count; i++)
                total += math.distance(p.points[c.start + i], p.points[c.start + (i + 1) % c.count]);
            if (total <= 0f || c.count == 0)
            {
                for (int i = 0; i < n; i++) outPts[i] = c.count > 0 ? p.points[c.start] : float2.zero;
                return;
            }

            float step = total / n;
            int seg = 0;
            float segStart = 0f;
            float segLen = math.distance(p.points[c.start], p.points[c.start + 1 % c.count]);
            for (int k = 0; k < n; k++)
            {
                float s = k * step;
                while (segStart + segLen < s && seg < c.count - 1)
                {
                    segStart += segLen;
                    seg++;
                    segLen = math.distance(p.points[c.start + seg], p.points[c.start + (seg + 1) % c.count]);
                }

                float f = segLen > 0f ? (s - segStart) / segLen : 0f;
                outPts[k] = math.lerp(p.points[c.start + seg], p.points[c.start + (seg + 1) % c.count], math.saturate(f));
            }
        }

        static void Collapse(NativeArray<float2> src, NativeArray<float2> dst)
        {
            float2 centre = float2.zero;
            for (int i = 0; i < src.Length; i++) centre += src[i];
            centre /= math.max(src.Length, 1);
            for (int i = 0; i < dst.Length; i++) dst[i] = centre;
        }

        static int BestShift(NativeArray<float2> a, NativeArray<float2> b)
        {
            int n = a.Length, best = 0;
            float bestCost = float.MaxValue;
            int stride = math.max(1, n / 64);
            for (int s = 0; s < n; s += stride)
            {
                float cost = 0f;
                for (int i = 0; i < n; i += stride) cost += math.distancesq(a[i], b[(i + s) % n]);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = s;
                }
            }

            return best;
        }

        protected override void Hash(ref HashBuilder h)
        {
            h.Add(t);
            h.Add(resolution);
            h.Add(from != null && from != this ? from.ComputeHash() : 0);
            h.Add(to != null && to != this ? to.ComputeHash() : 0);
        }
    }
}
