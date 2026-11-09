using System.Collections.Generic;
using FirstSymbol.Vectra.Geometry;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace FirstSymbol.Vectra.Svg
{
    // Markers (marker-start, marker-mid, marker-end) on path, line, polyline and polygon.
    public static partial class SvgParser
    {
        /// <summary>A vertex of a path with the directions of the segments that meet there.</summary>
        internal struct MarkerVertex
        {
            public float2 position;
            public float2 incoming, outgoing;
            public bool hasIncoming, hasOutgoing;
        }

        sealed class MarkerSet
        {
            public SvgNode start, mid, end;
            public readonly List<MarkerVertex> vertices = new List<MarkerVertex>();
        }

        static bool IsMarkerHost(SvgNode n) => n.name == "path" || n.name == "line" || n.name == "polyline" || n.name == "polygon";

        static SvgNode FindMarker(SvgNode n, string key, Context ctx)
        {
            var value = Style(n, key) ?? Style(n, "marker");
            if (!HasMarker(value)) return null;
            var id = Url(value);
            if (id != null && ctx.ids.TryGetValue(id, out var m) && m.name == "marker") return m;
            ctx.Warn($"A marker referenced by '{value.Trim()}' was not found; it is not drawn.");
            return null;
        }

        /// <summary>Reads the markers of a shape and the vertices of its path (ctx.path). Null when there is nothing to draw.</summary>
        static MarkerSet CaptureMarkers(SvgNode n, Context ctx)
        {
            if (!IsMarkerHost(n) || ctx.markerDepth > 0) return null;
            if (!HasMarker(Style(n, "marker")) && !HasMarker(Style(n, "marker-start")) && !HasMarker(Style(n, "marker-mid")) &&
                !HasMarker(Style(n, "marker-end"))) return null;
            var set = new MarkerSet
            {
                start = FindMarker(n, "marker-start", ctx), mid = FindMarker(n, "marker-mid", ctx), end = FindMarker(n, "marker-end", ctx)
            };
            if (set.start == null && set.mid == null && set.end == null) return null;
            CollectVertices(ctx.path, set.vertices);
            return set.vertices.Count > 0 ? set : null;
        }

        static float2 Nonzero(float2 a, float2 b, float2 c) => math.lengthsq(a) > 1e-12f ? a : math.lengthsq(b) > 1e-12f ? b : c;

        /// <summary>Vertices of a path in order, each with the tangent that arrives and the one that leaves (SVG 1.1 section 11.6.2).</summary>
        internal static void CollectVertices(NativePath path, List<MarkerVertex> output)
        {
            output.Clear();
            int pi = 0;
            float2 current = default, subStart = default;
            int subFirst = -1; // index in output of the first vertex of the open subpath
            bool open = false;
            float2 firstOut = default;
            bool hasFirstOut = false;

            void Arrive(float2 to, float2 tangentIn, float2 tangentOut)
            {
                // The segment that ends here is the one that leaves the previous vertex.
                if (output.Count > 0)
                {
                    var prev = output[output.Count - 1];
                    if (!prev.hasOutgoing)
                    {
                        prev.outgoing = tangentOut;
                        prev.hasOutgoing = true;
                        output[output.Count - 1] = prev;
                        if (output.Count - 1 == subFirst)
                        {
                            firstOut = tangentOut;
                            hasFirstOut = true;
                        }
                    }
                }

                output.Add(new MarkerVertex { position = to, incoming = tangentIn, hasIncoming = true });
                current = to;
            }

            for (int i = 0; i < path.verbs.Length; i++)
            {
                switch ((PathVerb)path.verbs[i])
                {
                    case PathVerb.MoveTo:
                        current = subStart = path.points[pi++];
                        subFirst = output.Count;
                        hasFirstOut = false;
                        open = true;
                        output.Add(new MarkerVertex { position = current });
                        break;
                    case PathVerb.LineTo:
                    {
                        var p = path.points[pi++];
                        var t = p - current;
                        Arrive(p, t, t);
                        break;
                    }
                    case PathVerb.QuadTo:
                    {
                        var c = path.points[pi];
                        var p = path.points[pi + 1];
                        pi += 2;
                        Arrive(p, Nonzero(p - c, p - current, default), Nonzero(c - current, p - current, default));
                        break;
                    }
                    case PathVerb.CubicTo:
                    {
                        var c1 = path.points[pi];
                        var c2 = path.points[pi + 1];
                        var p = path.points[pi + 2];
                        pi += 3;
                        Arrive(p, Nonzero(p - c2, p - c1, p - current), Nonzero(c1 - current, c2 - current, p - current));
                        break;
                    }
                    case PathVerb.Close:
                    {
                        if (!open) break;
                        var t = subStart - current;
                        if (math.lengthsq(t) < 1e-12f && output.Count > 0)
                        {
                            // Already back at the start: the closing segment has no length; the direction is that of the last segment.
                            var last = output[output.Count - 1];
                            t = last.hasIncoming ? last.incoming : default;
                        }

                        Arrive(subStart, t, t);
                        // The closing vertex leaves along the first segment, and the first vertex of the subpath arrives along the closing one.
                        var closing = output[output.Count - 1];
                        if (hasFirstOut)
                        {
                            closing.outgoing = firstOut;
                            closing.hasOutgoing = true;
                            output[output.Count - 1] = closing;
                        }

                        if (subFirst >= 0)
                        {
                            var first = output[subFirst];
                            first.incoming = closing.incoming;
                            first.hasIncoming = true;
                            output[subFirst] = first;
                        }

                        current = subStart;
                        open = false;
                        break;
                    }
                }
            }
        }

        static float2 Unit(float2 v) => math.lengthsq(v) > 1e-12f ? math.normalize(v) : default;

        internal static float MarkerAngle(in MarkerVertex v)
        {
            float2 d;
            if (v.hasIncoming && v.hasOutgoing)
            {
                d = Unit(v.incoming) + Unit(v.outgoing);
                if (math.lengthsq(d) < 1e-8f) d = Unit(v.outgoing);
            }
            else d = v.hasOutgoing ? v.outgoing : v.hasIncoming ? v.incoming : default;

            return math.lengthsq(d) < 1e-12f ? 0f : math.atan2(d.y, d.x);
        }

        // orient: "auto", "auto-start-reverse", or an angle (deg by default, also rad, grad, turn).
        static bool ParseOrient(string v, out bool auto, out bool reverseAtStart, out float radians)
        {
            auto = false;
            reverseAtStart = false;
            radians = 0f;
            if (string.IsNullOrEmpty(v)) return true;
            v = v.Trim();
            if (v == "auto") { auto = true; return true; }
            if (v == "auto-start-reverse") { auto = true; reverseAtStart = true; return true; }
            int i = 0;
            if (!SvgPathParser.ReadNumber(v, ref i, out float f)) return false;
            switch (v.Substring(i).Trim())
            {
                case "": case "deg": radians = f * Mathf.Deg2Rad; break;
                case "rad": radians = f; break;
                case "grad": radians = f * Mathf.PI / 200f; break;
                case "turn": radians = f * Mathf.PI * 2f; break;
                default: return false;
            }

            return true;
        }

        static void DrawMarkers(SvgNode shape, MarkerSet set, Affine2 local, float op, List<SvgPathData> clips,
            List<VectraEffect> effects, Context ctx)
        {
            var visibility = Style(shape, "visibility");
            if (visibility == "hidden" || visibility == "collapse") return;
            float strokeWidth = Length(Style(shape, "stroke-width"), 1f, Diag(ctx.viewportW, ctx.viewportH), ctx);
            var verts = set.vertices;
            int last = verts.Count - 1;
            for (int i = 0; i <= last; i++)
            {
                if (i == 0 && set.start != null) DrawMarker(set.start, verts[i], true, strokeWidth, local, op, clips, effects, ctx);
                if (i > 0 && i < last && set.mid != null) DrawMarker(set.mid, verts[i], false, strokeWidth, local, op, clips, effects, ctx);
                if (i == last && set.end != null) DrawMarker(set.end, verts[i], false, strokeWidth, local, op, clips, effects, ctx);
                if (ctx.stopped) return;
            }
        }

        static void DrawMarker(SvgNode marker, in MarkerVertex vertex, bool isStart, float strokeWidth, Affine2 local, float op,
            List<SvgPathData> clips, List<VectraEffect> effects, Context ctx)
        {
            if (ctx.uses >= ctx.opt.maxUseExpansions)
            {
                ctx.Warn($"SVG limit reached: more than {ctx.opt.maxUseExpansions} <use> copies and markers; further markers are skipped.");
                ctx.stopped = true;
                return;
            }

            float mw = Length(marker.Attr("markerWidth"), 3f, ctx.viewportW, ctx);
            float mh = Length(marker.Attr("markerHeight"), 3f, ctx.viewportH, ctx);
            if (mw <= 0f || mh <= 0f) return;
            if (!ParseOrient(marker.Attr("orient"), out bool auto, out bool reverseAtStart, out float fixedAngle))
            {
                ctx.Warn($"Marker orient='{marker.Attr("orient")}' is not understood; the marker is drawn upright.");
                auto = false;
                fixedAngle = 0f;
            }

            float angle = auto ? MarkerAngle(vertex) + (reverseAtStart && isStart ? Mathf.PI : 0f) : fixedAngle;
            bool userUnits = marker.Attr("markerUnits") == "userSpaceOnUse";
            float unit = userUnits ? 1f : strokeWidth;
            var fit = ViewBoxTransform(marker, 0f, 0f, mw, mh);
            float2 reference = fit.Apply(new float2(Length(marker.Attr("refX"), 0f, ctx.viewportW, ctx),
                Length(marker.Attr("refY"), 0f, ctx.viewportH, ctx)));

            // Marker viewport space: its origin at the top left corner of the viewport, the reference point on the vertex.
            var toDoc = local * Affine2.Translate(vertex.position) * Affine2.Rotate(angle) * Affine2.Scale(new float2(unit, unit)) *
                        Affine2.Translate(-reference);
            var content = toDoc * fit;

            var childClips = clips;
            var overflow = Style(marker, "overflow") ?? marker.Attr("overflow");
            if (overflow != "visible" && overflow != "auto")
            {
                var clip = RectClip(toDoc, 0f, 0f, mw, mh);
                childClips = new List<SvgPathData>();
                if (clips != null && clips.Count > 0) childClips.Add(Intersect(clips[0], clip));
                else childClips.Add(clip);
            }

            ctx.uses++;
            ctx.markerDepth++;
            foreach (var c in marker.children) Render(c, content, op, childClips, effects, ctx);
            ctx.markerDepth--;
        }

        static SvgPathData RectClip(Affine2 m, float x, float y, float w, float h)
        {
            var data = new SvgPathData { rule = FillRule.NonZero };
            data.verbs.Add((byte)PathVerb.MoveTo);
            data.points.Add(m.Apply(new float2(x, y)));
            data.verbs.Add((byte)PathVerb.LineTo);
            data.points.Add(m.Apply(new float2(x + w, y)));
            data.verbs.Add((byte)PathVerb.LineTo);
            data.points.Add(m.Apply(new float2(x + w, y + h)));
            data.verbs.Add((byte)PathVerb.LineTo);
            data.points.Add(m.Apply(new float2(x, y + h)));
            data.verbs.Add((byte)PathVerb.Close);
            return data;
        }
    }
}
