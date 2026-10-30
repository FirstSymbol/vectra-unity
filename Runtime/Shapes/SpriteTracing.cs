using System;
using System.Collections.Generic;
using FirstSymbol.Vectra.Geometry;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace FirstSymbol.Vectra
{
    /// <summary>
    /// Traces the alpha silhouette of a sprite into closed contours (marching squares with interpolated edges,
    /// then Ramer-Douglas-Peucker simplification). Works with textures that are not readable.
    /// </summary>
    public static class VectraSpriteTracer
    {
        static readonly Dictionary<int, List<Vector2[]>> s_Cache = new Dictionary<int, List<Vector2[]>>();

        /// <summary>
        /// Contours in normalized sprite-rect coordinates (0..1, y up). Holes are separate contours; draw them with
        /// the even-odd rule. <paramref name="resolution"/> is the size of the sampling grid along the longer side,
        /// <paramref name="simplify"/> the allowed deviation in grid cells.
        /// </summary>
        public static List<Vector2[]> Trace(Sprite sprite, float alphaThreshold = 0.5f, int resolution = 128, float simplify = 0.75f)
        {
            if (sprite == null || sprite.texture == null) return new List<Vector2[]>();
            int key;
            unchecked
            {
                key = sprite.GetHashCode(); // instance-based, available in every supported Unity version
                key = key * 397 ^ alphaThreshold.GetHashCode();
                key = key * 397 ^ resolution;
                key = key * 397 ^ simplify.GetHashCode();
                key = key * 397 ^ sprite.texture.imageContentsHash.GetHashCode();
            }

            if (s_Cache.TryGetValue(key, out var cached)) return cached;
            if (s_Cache.Count > 64) s_Cache.Clear();
            var result = TraceAlpha(ReadAlpha(sprite, out int w, out int h), w, h, alphaThreshold, resolution, simplify);
            s_Cache[key] = result;
            return result;
        }

        /// <summary>Forgets cached contours (call after a sprite's pixels changed at runtime).</summary>
        public static void ClearCache() => s_Cache.Clear();

        static float[] ReadAlpha(Sprite sprite, out int w, out int h)
        {
            var tex = sprite.texture;
            var r = sprite.textureRect;
            int x0 = Mathf.FloorToInt(r.x), y0 = Mathf.FloorToInt(r.y);
            w = Mathf.Max(1, Mathf.RoundToInt(r.width));
            h = Mathf.Max(1, Mathf.RoundToInt(r.height));
            Color[] px;
            if (tex.isReadable) px = tex.GetPixels(x0, y0, w, h);
            else
            {
                // Copy through a render texture: works for compressed and non-readable textures.
                var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                var prev = RenderTexture.active;
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                var tmp = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
                tmp.ReadPixels(new Rect(x0, y0, w, h), 0, 0);
                tmp.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                px = tmp.GetPixels();
                if (Application.isPlaying) UnityEngine.Object.Destroy(tmp);
                else UnityEngine.Object.DestroyImmediate(tmp);
            }

            var a = new float[w * h];
            for (int i = 0; i < a.Length; i++) a[i] = px[i].a;
            return a;
        }

        /// <summary>Traces an alpha grid (row-major, bottom row first). Exposed for custom image sources.</summary>
        public static List<Vector2[]> TraceAlpha(float[] alpha, int w, int h, float threshold, int resolution, float simplify)
        {
            // Sampling grid with a one-cell empty border so every contour closes.
            float scale = Mathf.Min(1f, resolution / (float)Mathf.Max(w, h));
            int gw = Mathf.Max(1, Mathf.RoundToInt(w * scale)), gh = Mathf.Max(1, Mathf.RoundToInt(h * scale));
            int vw = gw + 2, vh = gh + 2;
            var v = new float[vw * vh];
            for (int y = 0; y < gh; y++)
            for (int x = 0; x < gw; x++)
            {
                // Box filter over the source pixels covered by this cell.
                int sx0 = x * w / gw, sx1 = Mathf.Max(sx0 + 1, (x + 1) * w / gw);
                int sy0 = y * h / gh, sy1 = Mathf.Max(sy0 + 1, (y + 1) * h / gh);
                float sum = 0f;
                for (int sy = sy0; sy < sy1; sy++)
                for (int sx = sx0; sx < sx1; sx++)
                    sum += alpha[sy * w + sx];
                v[(y + 1) * vw + x + 1] = sum / ((sx1 - sx0) * (sy1 - sy0));
            }

            // Marching squares: segments between edge keys. Edge key = (x, y, horizontal?) packed in a long.
            var adjacency = new Dictionary<long, List<long>>();
            var points = new Dictionary<long, Vector2>();
            float t = threshold;

            long H(int x, int y) => ((long)y * vw + x) * 2;
            long V(int x, int y) => ((long)y * vw + x) * 2 + 1;
            float At(int x, int y) => v[y * vw + x];

            Vector2 EdgePoint(long k)
            {
                long idx = k >> 1;
                int x = (int)(idx % vw), y = (int)(idx / vw);
                bool vertical = (k & 1) != 0;
                float a0 = At(x, y), a1 = vertical ? At(x, y + 1) : At(x + 1, y);
                float f = Mathf.Abs(a1 - a0) > 1e-6f ? Mathf.Clamp01((t - a0) / (a1 - a0)) : 0.5f;
                return vertical ? new Vector2(x, y + f) : new Vector2(x + f, y);
            }

            void Link(long a, long b)
            {
                if (!adjacency.TryGetValue(a, out var la)) adjacency[a] = la = new List<long>(2);
                if (!adjacency.TryGetValue(b, out var lb)) adjacency[b] = lb = new List<long>(2);
                la.Add(b);
                lb.Add(a);
                if (!points.ContainsKey(a)) points[a] = EdgePoint(a);
                if (!points.ContainsKey(b)) points[b] = EdgePoint(b);
            }

            for (int y = 0; y < vh - 1; y++)
            for (int x = 0; x < vw - 1; x++)
            {
                float bl = At(x, y), br = At(x + 1, y), tr = At(x + 1, y + 1), tl = At(x, y + 1);
                int c = (bl >= t ? 1 : 0) | (br >= t ? 2 : 0) | (tr >= t ? 4 : 0) | (tl >= t ? 8 : 0);
                if (c == 0 || c == 15) continue;
                long B = H(x, y), T = H(x, y + 1), L = V(x, y), R = V(x + 1, y);
                bool centre = (bl + br + tr + tl) * 0.25f >= t;
                switch (c)
                {
                    case 1: case 14: Link(L, B); break;
                    case 2: case 13: Link(B, R); break;
                    case 3: case 12: Link(L, R); break;
                    case 4: case 11: Link(R, T); break;
                    case 6: case 9: Link(B, T); break;
                    case 7: case 8: Link(L, T); break;
                    case 5:
                        if (centre) { Link(B, R); Link(L, T); }
                        else { Link(L, B); Link(R, T); }
                        break;
                    case 10:
                        if (centre) { Link(L, B); Link(R, T); }
                        else { Link(B, R); Link(L, T); }
                        break;
                }
            }

            // Walk the loops.
            var result = new List<Vector2[]>();
            var visited = new HashSet<long>();
            var loop = new List<Vector2>();
            foreach (var start in adjacency.Keys)
            {
                if (visited.Contains(start)) continue;
                loop.Clear();
                long prev = -1, cur = start;
                while (cur >= 0 && visited.Add(cur))
                {
                    loop.Add(points[cur]);
                    var n = adjacency[cur];
                    long next = -1;
                    foreach (var k in n)
                        if (k != prev && !visited.Contains(k))
                        {
                            next = k;
                            break;
                        }

                    prev = cur;
                    cur = next;
                }

                if (loop.Count < 3) continue;
                var simplified = SimplifyClosed(loop, Mathf.Max(simplify, 0f));
                if (simplified.Count < 3 || Mathf.Abs(Area(simplified)) < 1f) continue;
                var arr = new Vector2[simplified.Count];
                for (int i = 0; i < arr.Length; i++)
                    arr[i] = new Vector2((simplified[i].x - 1f + 0.5f) / gw, (simplified[i].y - 1f + 0.5f) / gh);
                result.Add(arr);
            }

            return result;
        }

        static float Area(List<Vector2> p)
        {
            float a = 0f;
            for (int i = 0, j = p.Count - 1; i < p.Count; j = i++) a += (p[j].x - p[i].x) * (p[j].y + p[i].y);
            return a * 0.5f;
        }

        static List<Vector2> SimplifyClosed(List<Vector2> pts, float tolerance)
        {
            if (tolerance <= 0f || pts.Count < 4) return new List<Vector2>(pts);
            // Split the loop at its two farthest-apart points and simplify both halves.
            int far = 0;
            float best = -1f;
            for (int i = 1; i < pts.Count; i++)
            {
                float d = (pts[i] - pts[0]).sqrMagnitude;
                if (d > best)
                {
                    best = d;
                    far = i;
                }
            }

            var keep = new bool[pts.Count];
            keep[0] = keep[far] = true;
            Rdp(pts, 0, far, tolerance, keep);
            Rdp(pts, far, pts.Count, tolerance, keep);
            var result = new List<Vector2>();
            for (int i = 0; i < pts.Count; i++)
                if (keep[i]) result.Add(pts[i]);
            return result;
        }

        static void Rdp(List<Vector2> pts, int a, int b, float tolerance, bool[] keep)
        {
            // b may equal pts.Count (wraps to the first point).
            var stack = new Stack<(int, int)>();
            stack.Push((a, b));
            while (stack.Count > 0)
            {
                var (s, e) = stack.Pop();
                if (e - s < 2) continue;
                Vector2 p0 = pts[s], p1 = pts[e % pts.Count];
                Vector2 d = p1 - p0;
                float len = d.magnitude;
                int index = -1;
                float max = tolerance;
                for (int i = s + 1; i < e; i++)
                {
                    var q = pts[i] - p0;
                    float dist = len > 1e-6f ? Mathf.Abs(d.x * q.y - d.y * q.x) / len : q.magnitude;
                    if (dist > max)
                    {
                        max = dist;
                        index = i;
                    }
                }

                if (index < 0) continue;
                keep[index] = true;
                stack.Push((s, index));
                stack.Push((index, e));
            }
        }
    }

    /// <summary>
    /// The silhouette of a sprite as an editable vector shape: alpha threshold, sampling resolution, simplification
    /// and optional smoothing. Use "Convert to editable path" in the inspector to edit the contour points.
    /// </summary>
    [Serializable]
    public sealed class TracedSpriteShape : VectraShapeGeometry
    {
        public Sprite sprite;
        [Range(0.01f, 0.99f)] public float alphaThreshold = 0.5f;
        [Tooltip("Sampling grid size along the longer side of the sprite.")]
        [Range(16, 512)] public int resolution = 128;
        [Tooltip("Allowed deviation from the traced outline, in grid cells (higher = fewer points).")]
        [Range(0f, 8f)] public float simplify = 0.75f;
        [Tooltip("Round the contour with curves through the edge midpoints.")]
        public bool smooth = true;
        public bool preserveAspect = true;

        public override string DisplayName => "Traced Sprite";
        public override FillRule FillRule => FillRule.EvenOdd;

        /// <summary>The traced contours mapped into <paramref name="frame"/> (local coordinates).</summary>
        public List<float2[]> Contours(in ShapeFrame frame)
        {
            var result = new List<float2[]>();
            if (sprite == null) return result;
            var contours = VectraSpriteTracer.Trace(sprite, alphaThreshold, resolution, simplify);
            var map = Mapping(frame);
            foreach (var c in contours)
            {
                var arr = new float2[c.Length];
                for (int i = 0; i < c.Length; i++) arr[i] = map.Apply(c[i]);
                result.Add(arr);
            }

            return result;
        }

        Affine2 Mapping(in ShapeFrame frame)
        {
            float2 size = frame.Size, min = frame.Min;
            if (preserveAspect && sprite != null && sprite.rect.height > 0f)
            {
                float aspect = sprite.rect.width / sprite.rect.height;
                float2 fit = size.x / math.max(size.y, 1e-6f) > aspect ? new float2(size.y * aspect, size.y) : new float2(size.x, size.x / aspect);
                min += (size - fit) * 0.5f;
                size = fit;
            }

            var m = Affine2.Translate(min) * Affine2.Scale(size);
            if (rotation != 0f) m = Affine2.Rotate(math.radians(rotation), frame.Center) * m;
            return m;
        }

        public override void BuildPath(in ShapeFrame frame, PathBuilder b)
        {
            foreach (var pts in Contours(frame))
            {
                int n = pts.Length;
                if (!smooth || n < 3)
                {
                    b.MoveTo(pts[0]);
                    for (int i = 1; i < n; i++) b.LineTo(pts[i]);
                    b.Close();
                    continue;
                }

                // Quadratic curves through the edge midpoints, with the traced points as control points.
                b.MoveTo((pts[n - 1] + pts[0]) * 0.5f);
                for (int i = 0; i < n; i++) b.QuadTo(pts[i], (pts[i] + pts[(i + 1) % n]) * 0.5f);
                b.Close();
            }
        }

        protected override void Hash(ref HashBuilder h)
        {
            h.Add(sprite != null ? sprite.GetHashCode() : 0);
            h.Add(alphaThreshold);
            h.Add(resolution);
            h.Add(simplify);
            h.Add(smooth);
            h.Add(preserveAspect);
        }
    }

    /// <summary>Converts any shape into an editable <see cref="PathShape"/> (Bezier points and handles).</summary>
    public static class VectraPathConverter
    {
        public static PathShape ToPathShape(VectraShapeGeometry geometry, in ShapeFrame frame)
        {
            var result = new PathShape { fillRule = geometry?.FillRule ?? FillRule.NonZero, coordinates = PathCoordinates.Normalized };
            result.contours.Clear();
            if (geometry == null) return result;
            var path = new NativePath(Allocator.Temp, 64);
            try
            {
                geometry.BuildPath(frame, new PathBuilder(path));
                BezierContour contour = null;
                int pi = 0;
                float2 size = math.max(frame.Size, 1e-6f), min = frame.Min;
                Vector2 N(float2 p) => (Vector2)((p - min) / size);
                Vector2 NV(float2 v) => (Vector2)(v / size);

                void Finish(bool closed)
                {
                    if (contour == null) return;
                    var pts = contour.points;
                    // A closing segment back onto the first point: merge the duplicate.
                    if (closed && pts.Count > 1 && (pts[pts.Count - 1].position - pts[0].position).sqrMagnitude < 1e-10f)
                    {
                        var first = pts[0];
                        first.inTangent = pts[pts.Count - 1].inTangent;
                        pts[0] = first;
                        pts.RemoveAt(pts.Count - 1);
                    }

                    contour.closed = closed;
                    if (pts.Count > 1) result.contours.Add(contour);
                    contour = null;
                }

                for (int i = 0; i < path.verbs.Length; i++)
                {
                    switch ((PathVerb)path.verbs[i])
                    {
                        case PathVerb.MoveTo:
                            Finish(false);
                            contour = new BezierContour();
                            contour.points.Add(new BezierPoint(N(path.points[pi++])));
                            break;
                        case PathVerb.LineTo:
                            contour?.points.Add(new BezierPoint(N(path.points[pi++])));
                            break;
                        case PathVerb.QuadTo:
                        {
                            float2 q = path.points[pi], p = path.points[pi + 1];
                            pi += 2;
                            AddCubic(contour, q, q, p, true);
                            break;
                        }
                        case PathVerb.CubicTo:
                        {
                            float2 c1 = path.points[pi], c2 = path.points[pi + 1], p = path.points[pi + 2];
                            pi += 3;
                            AddCubic(contour, c1, c2, p, false);
                            break;
                        }
                        case PathVerb.Close:
                            Finish(true);
                            break;
                    }
                }

                Finish(false);

                void AddCubic(BezierContour c, float2 c1, float2 c2, float2 p, bool quadratic)
                {
                    if (c == null || c.points.Count == 0) return;
                    var last = c.points[c.points.Count - 1];
                    float2 p0 = (float2)last.position * size + min;
                    if (quadratic)
                    {
                        // Degree elevation: quadratic control q becomes two cubic controls.
                        float2 q = c1;
                        c1 = p0 + (q - p0) * (2f / 3f);
                        c2 = p + (q - p) * (2f / 3f);
                    }

                    last.outTangent = NV(c1 - p0);
                    c.points[c.points.Count - 1] = last;
                    c.points.Add(new BezierPoint(N(p), NV(c2 - p)));
                }
            }
            finally
            {
                path.Dispose();
            }

            return result;
        }
    }
}
