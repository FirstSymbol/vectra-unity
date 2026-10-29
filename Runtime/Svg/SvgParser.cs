using System;
using System.Collections.Generic;
using System.Globalization;
using FirstSymbol.Vectra.Geometry;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace FirstSymbol.Vectra.Svg
{
    /// <summary>
    /// Converts SVG text into a <see cref="VectraSvgDocument"/>. Works at runtime as well as in the importer.
    /// Supported: shapes, paths, groups, use/symbol, nested svg, viewBox/preserveAspectRatio, transforms, CSS
    /// (style elements and attributes), inheritance, solid colours, linear/radial gradients (units, transforms,
    /// focal points, spread, href inheritance), strokes (dashes, joins, caps, miter, non-scaling), opacity,
    /// clip paths, masks (as geometric clips), common filters (blur, drop shadow, colour matrix), text and images.
    /// </summary>
    public static class SvgParser
    {
        static readonly HashSet<string> s_Inherited = new HashSet<string>
        {
            "fill", "fill-opacity", "fill-rule", "stroke", "stroke-width", "stroke-opacity", "stroke-linecap",
            "stroke-linejoin", "stroke-miterlimit", "stroke-dasharray", "stroke-dashoffset", "font-family",
            "font-size", "font-weight", "font-style", "text-anchor", "visibility", "color", "clip-rule", "vector-effect"
        };

        static readonly HashSet<string> s_Presentation = new HashSet<string>
        {
            "fill", "fill-opacity", "fill-rule", "stroke", "stroke-width", "stroke-opacity", "stroke-linecap",
            "stroke-linejoin", "stroke-miterlimit", "stroke-dasharray", "stroke-dashoffset", "font-family",
            "font-size", "font-weight", "font-style", "text-anchor", "visibility", "color", "clip-rule", "opacity",
            "display", "clip-path", "mask", "filter", "stop-color", "stop-opacity", "vector-effect", "flood-color",
            "flood-opacity"
        };

        sealed class Context
        {
            public VectraSvgDocument doc;
            public SvgCss css = new SvgCss();
            public readonly Dictionary<string, SvgNode> ids = new Dictionary<string, SvgNode>();
            public float viewportW = 100, viewportH = 100;
            public NativePath path;
            public PathBuilder builder;
            public int depth;
            public readonly HashSet<string> warned = new HashSet<string>();

            public void Warn(string msg)
            {
                if (warned.Add(msg)) doc.warnings.Add(msg);
            }
        }

        public static VectraSvgDocument Parse(string svg)
        {
            var doc = ScriptableObject.CreateInstance<VectraSvgDocument>();
            doc.name = "SVG Document";
            Parse(svg, doc);
            return doc;
        }

        /// <summary>Parses into an existing document (its previous content is replaced).</summary>
        public static void Parse(string svg, VectraSvgDocument doc)
        {
            doc.elements.Clear();
            doc.warnings.Clear();
            var xml = SvgXml.Parse(svg);
            var root = FindFirst(xml, "svg");
            if (root == null)
            {
                doc.warnings.Add("No <svg> element found.");
                return;
            }

            var ctx = new Context { doc = doc };
            ctx.path = new NativePath(Allocator.TempJob, 64);
            ctx.builder = new PathBuilder(ctx.path);
            try
            {
                Index(xml, ctx);
                ComputeStyles(root, null, ctx);

                // Root viewport and viewBox.
                float w = Length(root.Attr("width"), 0f, 100f, ctx);
                float h = Length(root.Attr("height"), 0f, 100f, ctx);
                var vb = ParseViewBox(root.Attr("viewBox"));
                if (vb.HasValue)
                {
                    doc.viewBox = vb.Value;
                    if (w <= 0f) w = vb.Value.width;
                    if (h <= 0f) h = vb.Value.height;
                }
                else
                {
                    if (w <= 0f) w = 100f;
                    if (h <= 0f) h = 100f;
                    doc.viewBox = new Rect(0, 0, w, h);
                }

                doc.size = new Vector2(w, h);
                ParseAspect(root.Attr("preserveAspectRatio"), out doc.align, out doc.slice);
                ctx.viewportW = doc.viewBox.width;
                ctx.viewportH = doc.viewBox.height;
                foreach (var child in root.children) Render(child, Affine2.Identity, 1f, null, null, ctx);
            }
            finally
            {
                ctx.path.Dispose();
            }
        }

        static SvgNode FindFirst(SvgNode n, string name)
        {
            if (n == null) return null;
            if (n.name == name) return n;
            foreach (var c in n.children)
            {
                var r = FindFirst(c, name);
                if (r != null) return r;
            }

            return null;
        }

        static void Index(SvgNode n, Context ctx)
        {
            var id = n.Attr("id");
            if (!string.IsNullOrEmpty(id) && !ctx.ids.ContainsKey(id)) ctx.ids[id] = n;
            if (n.name == "style") ctx.css.AddStylesheet(n.text);
            foreach (var c in n.children) Index(c, ctx);
        }

        // ---------------------------------------------------------------------------------------------------
        // Styles
        // ---------------------------------------------------------------------------------------------------

        static void ComputeStyles(SvgNode n, Dictionary<string, string> parent, Context ctx)
        {
            var s = new Dictionary<string, string>(StringComparer.Ordinal);
            if (parent != null)
                foreach (var kv in parent)
                    if (s_Inherited.Contains(kv.Key)) s[kv.Key] = kv.Value;

            foreach (var kv in n.attributes)
                if (s_Presentation.Contains(kv.Key)) s[kv.Key] = kv.Value;

            var important = new HashSet<string>();
            ctx.css.Apply(n, s, important);
            var inline = n.Attr("style");
            if (!string.IsNullOrEmpty(inline))
                foreach (var kv in SvgCss.ParseDeclarations(inline, out _))
                    if (!important.Contains(kv.Key)) s[kv.Key] = kv.Value;

            // Explicit "inherit" falls back to the parent value.
            var keys = new List<string>(s.Keys);
            foreach (var k in keys)
                if (s[k] == "inherit")
                {
                    if (parent != null && parent.TryGetValue(k, out var pv)) s[k] = pv;
                    else s.Remove(k);
                }

            n.style = s;
            foreach (var c in n.children) ComputeStyles(c, s, ctx);
        }

        static string Style(SvgNode n, string key, string fallback = null) =>
            n.style != null && n.style.TryGetValue(key, out var v) ? v : fallback;

        // ---------------------------------------------------------------------------------------------------
        // Rendering tree
        // ---------------------------------------------------------------------------------------------------

        static void Render(SvgNode n, Affine2 ctm, float opacity, List<SvgPathData> clips, List<VectraEffect> effects,
            Context ctx)
        {
            if (ctx.depth > 64) return;
            if (Style(n, "display") == "none") return;
            switch (n.name)
            {
                case "defs": case "clipPath": case "mask": case "linearGradient": case "radialGradient":
                case "pattern": case "filter": case "style": case "title": case "desc": case "metadata":
                case "symbol": case "marker": case "script": case "#document":
                    return;
            }

            var local = ctm * ParseTransform(n.Attr("transform"));
            float op = opacity * Number(Style(n, "opacity"), 1f);
            var elementClips = clips;
            var clipRef = Url(Style(n, "clip-path"));
            if (clipRef != null) elementClips = AddClip(clipRef, local, n, clips, ctx);
            var maskRef = Url(Style(n, "mask"));
            if (maskRef != null)
            {
                ctx.Warn("Masks are applied as geometric clips (luminance is ignored).");
                elementClips = AddClip(maskRef, local, n, elementClips, ctx);
            }

            var elementEffects = effects;
            var filterRef = Url(Style(n, "filter"));
            if (filterRef != null)
            {
                elementEffects = new List<VectraEffect>(effects ?? new List<VectraEffect>());
                AddFilter(filterRef, local, elementEffects, ctx);
            }

            ctx.depth++;
            switch (n.name)
            {
                case "svg":
                {
                    float x = Length(n.Attr("x"), 0f, ctx.viewportW, ctx), y = Length(n.Attr("y"), 0f, ctx.viewportH, ctx);
                    float w = Length(n.Attr("width"), ctx.viewportW, ctx.viewportW, ctx);
                    float h = Length(n.Attr("height"), ctx.viewportH, ctx.viewportH, ctx);
                    var m = local * ViewBoxTransform(n, x, y, w, h);
                    foreach (var c in n.children) Render(c, m, op, elementClips, elementEffects, ctx);
                    break;
                }
                case "g":
                case "a":
                case "switch":
                    foreach (var c in n.children) Render(c, local, op, elementClips, elementEffects, ctx);
                    break;
                case "use":
                {
                    var href = n.Attr("href");
                    if (href == null || !href.StartsWith("#") || !ctx.ids.TryGetValue(href.Substring(1), out var target)) break;
                    float x = Length(n.Attr("x"), 0f, ctx.viewportW, ctx), y = Length(n.Attr("y"), 0f, ctx.viewportH, ctx);
                    var m = local * Affine2.Translate(new float2(x, y));
                    // The referenced content inherits the <use> element's style.
                    ComputeStyles(target, n.style, ctx);
                    if (target.name == "symbol")
                    {
                        float w = Length(n.Attr("width"), Length(target.Attr("width"), ctx.viewportW, ctx.viewportW, ctx), ctx.viewportW, ctx);
                        float h = Length(n.Attr("height"), Length(target.Attr("height"), ctx.viewportH, ctx.viewportH, ctx), ctx.viewportH, ctx);
                        var sm = m * ViewBoxTransform(target, 0, 0, w, h);
                        foreach (var c in target.children) Render(c, sm, op, elementClips, elementEffects, ctx);
                    }
                    else Render(target, m, op, elementClips, elementEffects, ctx);

                    break;
                }
                case "text":
                    RenderText(n, local, op, ctx);
                    break;
                case "image":
                    RenderImage(n, local, op, ctx);
                    break;
                default:
                    if (BuildShape(n, ctx))
                        Emit(n, local, op, elementClips, elementEffects, ctx);
                    else if (n.children.Count > 0 && n.name != "foreignObject")
                        foreach (var c in n.children) Render(c, local, op, elementClips, elementEffects, ctx);
                    break;
            }

            ctx.depth--;
        }

        static Affine2 ViewBoxTransform(SvgNode n, float x, float y, float w, float h)
        {
            var vb = ParseViewBox(n.Attr("viewBox"));
            if (!vb.HasValue) return Affine2.Translate(new float2(x, y));
            ParseAspect(n.Attr("preserveAspectRatio"), out var align, out bool slice);
            var v = vb.Value;
            float sx = w / Mathf.Max(v.width, 1e-6f), sy = h / Mathf.Max(v.height, 1e-6f);
            float tx = 0, ty = 0;
            if (align != SvgAlign.None)
            {
                float s = slice ? Mathf.Max(sx, sy) : Mathf.Min(sx, sy);
                sx = sy = s;
                int ix = ((int)align - 1) % 3, iy = ((int)align - 1) / 3;
                tx = (w - v.width * s) * ix * 0.5f;
                ty = (h - v.height * s) * iy * 0.5f;
            }

            return Affine2.Translate(new float2(x + tx, y + ty)) * Affine2.Scale(new float2(sx, sy)) *
                   Affine2.Translate(new float2(-v.x, -v.y));
        }

        /// <summary>Writes the element's outline (user space, untransformed) into ctx.path.</summary>
        static bool BuildShape(SvgNode n, Context ctx)
        {
            ctx.path.Clear();
            var b = ctx.builder.Reset(ctx.path, Affine2.Identity);
            float vw = ctx.viewportW, vh = ctx.viewportH;
            switch (n.name)
            {
                case "path":
                    SvgPathParser.Parse(n.Attr("d"), b);
                    break;
                case "rect":
                {
                    float x = Length(n.Attr("x"), 0, vw, ctx), y = Length(n.Attr("y"), 0, vh, ctx);
                    float w = Length(n.Attr("width"), 0, vw, ctx), h = Length(n.Attr("height"), 0, vh, ctx);
                    if (w <= 0 || h <= 0) return false;
                    string rxs = n.Attr("rx") ?? Style(n, "rx"), rys = n.Attr("ry") ?? Style(n, "ry");
                    float rx = rxs != null ? Length(rxs, 0, vw, ctx) : -1, ry = rys != null ? Length(rys, 0, vh, ctx) : -1;
                    if (rx < 0) rx = ry;
                    if (ry < 0) ry = rx;
                    rx = Mathf.Clamp(rx, 0, w * 0.5f);
                    ry = Mathf.Clamp(ry, 0, h * 0.5f);
                    if (rx <= 0 || ry <= 0) b.AddRect(new float2(x, y), new float2(w, h));
                    else
                    {
                        b.MoveTo(new float2(x + rx, y));
                        b.LineTo(new float2(x + w - rx, y));
                        b.ArcTo(new float2(rx, ry), 0, false, true, new float2(x + w, y + ry));
                        b.LineTo(new float2(x + w, y + h - ry));
                        b.ArcTo(new float2(rx, ry), 0, false, true, new float2(x + w - rx, y + h));
                        b.LineTo(new float2(x + rx, y + h));
                        b.ArcTo(new float2(rx, ry), 0, false, true, new float2(x, y + h - ry));
                        b.LineTo(new float2(x, y + ry));
                        b.ArcTo(new float2(rx, ry), 0, false, true, new float2(x + rx, y));
                        b.Close();
                    }

                    break;
                }
                case "circle":
                {
                    float r = Length(n.Attr("r"), 0, Diag(vw, vh), ctx);
                    if (r <= 0) return false;
                    b.AddEllipse(new float2(Length(n.Attr("cx"), 0, vw, ctx), Length(n.Attr("cy"), 0, vh, ctx)), new float2(r, r));
                    break;
                }
                case "ellipse":
                {
                    float rx = Length(n.Attr("rx"), 0, vw, ctx), ry = Length(n.Attr("ry"), 0, vh, ctx);
                    if (rx <= 0 || ry <= 0) return false;
                    b.AddEllipse(new float2(Length(n.Attr("cx"), 0, vw, ctx), Length(n.Attr("cy"), 0, vh, ctx)), new float2(rx, ry));
                    break;
                }
                case "line":
                    b.MoveTo(new float2(Length(n.Attr("x1"), 0, vw, ctx), Length(n.Attr("y1"), 0, vh, ctx)));
                    b.LineTo(new float2(Length(n.Attr("x2"), 0, vw, ctx), Length(n.Attr("y2"), 0, vh, ctx)));
                    break;
                case "polyline":
                case "polygon":
                {
                    var pts = Numbers(n.Attr("points"));
                    if (pts.Count < 4) return false;
                    b.MoveTo(new float2(pts[0], pts[1]));
                    for (int i = 2; i + 1 < pts.Count; i += 2) b.LineTo(new float2(pts[i], pts[i + 1]));
                    if (n.name == "polygon") b.Close();
                    break;
                }
                default:
                    return false;
            }

            return !ctx.path.IsEmpty;
        }

        static void Emit(SvgNode n, Affine2 ctm, float opacity, List<SvgPathData> clips, List<VectraEffect> effects, Context ctx)
        {
            if (Style(n, "visibility") == "hidden" || Style(n, "visibility") == "collapse") return;
            var bbox = PathFlattener.ControlBounds(ctx.path.points.AsArray(), Affine2.Identity);
            var e = new SvgElementData
            {
                tag = n.name,
                id = n.Attr("id"),
                className = n.Attr("class")
            };

            // Store the outline in document space.
            var docPath = new NativePath(Allocator.Temp, ctx.path.verbs.Length);
            docPath.verbs.AddRange(ctx.path.verbs.AsArray());
            for (int i = 0; i < ctx.path.points.Length; i++) docPath.points.Add(ctm.Apply(ctx.path.points[i]));
            e.path.CopyFrom(docPath);
            docPath.Dispose();
            e.path.rule = Style(n, "fill-rule") == "evenodd" ? FillRule.EvenOdd : FillRule.NonZero;

            var inverse = ctm.Inverse();
            string fill = Style(n, "fill", "black");
            bool isLine = n.name == "line" || n.name == "polyline";
            if (!isLine && fill != "none")
            {
                var paint = ResolvePaint(fill, n, bbox, inverse, ctx);
                if (paint != null)
                {
                    paint.opacity *= Mathf.Clamp01(Number(Style(n, "fill-opacity"), 1f) * opacity);
                    e.fill = paint;
                    e.hasFill = paint.opacity > 0f;
                }
            }

            string stroke = Style(n, "stroke", "none");
            if (stroke != "none")
            {
                var paint = ResolvePaint(stroke, n, bbox, inverse, ctx);
                if (paint != null)
                {
                    bool nonScaling = Style(n, "vector-effect") == "non-scaling-stroke";
                    float scale = nonScaling ? 1f : ctm.UniformScale;
                    var s = new StrokeLayer
                    {
                        paint = paint,
                        width = Length(Style(n, "stroke-width"), 1f, Diag(ctx.viewportW, ctx.viewportH), ctx) * scale,
                        cap = ParseCap(Style(n, "stroke-linecap")),
                        join = ParseJoin(Style(n, "stroke-linejoin")),
                        miterLimit = Mathf.Max(1f, Number(Style(n, "stroke-miterlimit"), 4f))
                    };
                    paint.opacity *= Mathf.Clamp01(Number(Style(n, "stroke-opacity"), 1f) * opacity);
                    var dashes = Style(n, "stroke-dasharray");
                    if (!string.IsNullOrEmpty(dashes) && dashes != "none")
                    {
                        var d = new List<float>();
                        foreach (var v in SplitList(dashes)) d.Add(Mathf.Max(0f, Length(v, 0f, Diag(ctx.viewportW, ctx.viewportH), ctx)) * scale);
                        float sum = 0;
                        foreach (var v in d) sum += v;
                        if (sum > 0f)
                        {
                            s.dashed = true;
                            s.dashPattern = d;
                            s.dashOffset = Length(Style(n, "stroke-dashoffset"), 0f, Diag(ctx.viewportW, ctx.viewportH), ctx) * scale;
                        }
                    }

                    e.stroke = s;
                    e.hasStroke = s.width > 0f && paint.opacity > 0f;
                }
            }

            if (!e.hasFill && !e.hasStroke) return;
            if (clips != null) e.clips.AddRange(clips);
            if (effects != null)
                foreach (var fx in effects) e.effects.Add(fx.Clone());
            ctx.doc.elements.Add(e);
        }

        // ---------------------------------------------------------------------------------------------------
        // Paint servers
        // ---------------------------------------------------------------------------------------------------

        static VectraPaint ResolvePaint(string value, SvgNode n, Bounds2 bbox, Affine2 docToUser, Context ctx)
        {
            value = value.Trim();
            string url = Url(value);
            if (url != null)
            {
                if (ctx.ids.TryGetValue(url, out var server))
                {
                    var p = GradientPaint(server, bbox, docToUser, ctx);
                    if (p != null) return p;
                }

                // Fallback colour after the url(), e.g. "url(#g) red".
                int close = value.IndexOf(')');
                string rest = close >= 0 ? value.Substring(close + 1).Trim() : "";
                if (rest.Length > 0 && rest != "none") return VectraPaint.Solid(ColorOf(rest, n));
                return rest == "none" ? null : VectraPaint.Solid(Color.black);
            }

            if (value == "currentColor") return VectraPaint.Solid(ColorOf(Style(n, "color", "black"), n));
            return SvgColor.TryParse(value, out var c) ? VectraPaint.Solid(c) : VectraPaint.Solid(Color.black);
        }

        static Color ColorOf(string v, SvgNode n)
        {
            if (v == "currentColor") v = Style(n, "color", "black");
            return SvgColor.TryParse(v, out var c) ? c : Color.black;
        }

        /// <summary>Attribute lookup following the href chain of gradients.</summary>
        static string GradAttr(SvgNode g, string key, Context ctx)
        {
            for (int i = 0; g != null && i < 16; i++)
            {
                var v = g.Attr(key);
                if (v != null) return v;
                var href = g.Attr("href");
                g = href != null && href.StartsWith("#") && ctx.ids.TryGetValue(href.Substring(1), out var next) ? next : null;
            }

            return null;
        }

        static List<SvgNode> GradStops(SvgNode g, Context ctx)
        {
            for (int i = 0; g != null && i < 16; i++)
            {
                var stops = g.children.FindAll(c => c.name == "stop");
                if (stops.Count > 0) return stops;
                var href = g.Attr("href");
                g = href != null && href.StartsWith("#") && ctx.ids.TryGetValue(href.Substring(1), out var next) ? next : null;
            }

            return new List<SvgNode>();
        }

        static VectraPaint GradientPaint(SvgNode g, Bounds2 bbox, Affine2 docToUser, Context ctx)
        {
            if (g.name == "pattern")
            {
                ctx.Warn("Pattern fills are approximated with the colour of their first shape.");
                var first = FindFirstShape(g);
                var fill = first != null ? Style(first, "fill", "black") : "black";
                return VectraPaint.Solid(SvgColor.TryParse(fill, out var pc) ? pc : Color.gray);
            }

            if (g.name != "linearGradient" && g.name != "radialGradient") return null;
            var grad = new VectraGradient { interpolation = GradientInterpolation.Gamma };
            float last = 0f;
            foreach (var s in GradStops(g, ctx))
            {
                if (s.style == null) ComputeStyles(s, g.style, ctx);
                float off = Mathf.Clamp01(Mathf.Max(last, Fraction(s.Attr("offset"), 0f)));
                last = off;
                var c = ColorOf(Style(s, "stop-color", "black"), s);
                c.a *= Mathf.Clamp01(Number(Style(s, "stop-opacity"), 1f));
                grad.stops.Add(new VectraGradientStop(off, c));
            }

            if (grad.stops.Count == 0) return null;
            if (grad.stops.Count == 1) return VectraPaint.Solid(grad.stops[0].color);

            bool userSpace = GradAttr(g, "gradientUnits", ctx) == "userSpaceOnUse";
            var gt = ParseTransform(GradAttr(g, "gradientTransform", ctx));
            Affine2 units = Affine2.Identity;
            if (!userSpace)
            {
                if (!bbox.IsValid || bbox.Size.x <= 0f || bbox.Size.y <= 0f) return VectraPaint.Solid(grad.stops[grad.stops.Count - 1].color);
                units = Affine2.Translate(bbox.min) * Affine2.Scale(bbox.Size);
            }

            // document -> user -> gradient units -> gradient space
            var toGradient = (units * gt).Inverse() * docToUser;
            float vw = userSpace ? ctx.viewportW : 1f, vh = userSpace ? ctx.viewportH : 1f;
            var spread = GradAttr(g, "spreadMethod", ctx);
            var paint = new VectraPaint
            {
                gradient = grad,
                useCustomMapping = true,
                spread = spread == "reflect" ? SpreadMode.Reflect : spread == "repeat" ? SpreadMode.Repeat : SpreadMode.Pad
            };

            if (g.name == "linearGradient")
            {
                float2 p1 = new float2(Coord(GradAttr(g, "x1", ctx), 0f, vw), Coord(GradAttr(g, "y1", ctx), 0f, vh));
                float2 p2 = new float2(Coord(GradAttr(g, "x2", ctx), 1f * vw, vw), Coord(GradAttr(g, "y2", ctx), 0f, vh));
                float2 d = p2 - p1;
                float l2 = math.max(math.dot(d, d), 1e-12f);
                var lin = new Affine2(d.x / l2, -d.y / l2, d.y / l2, d.x / l2,
                    -(d.x * p1.x + d.y * p1.y) / l2, (d.y * p1.x - d.x * p1.y) / l2);
                paint.type = PaintType.LinearGradient;
                paint.customMapping = lin * toGradient;
            }
            else
            {
                float diag = userSpace ? Diag(vw, vh) : 1f;
                float cx = Coord(GradAttr(g, "cx", ctx), 0.5f * vw, vw), cy = Coord(GradAttr(g, "cy", ctx), 0.5f * vh, vh);
                float r = Coord(GradAttr(g, "r", ctx), 0.5f * diag, diag);
                float fx = Coord(GradAttr(g, "fx", ctx), cx, vw), fy = Coord(GradAttr(g, "fy", ctx), cy, vh);
                r = Mathf.Max(r, 1e-6f);
                var unit = Affine2.Scale(new float2(1f / r)) * Affine2.Translate(new float2(-cx, -cy));
                paint.type = PaintType.RadialGradient;
                paint.customMapping = unit * toGradient;
                paint.focalPoint = new Vector2((fx - cx) / r, (fy - cy) / r);
            }

            return paint;
        }

        static SvgNode FindFirstShape(SvgNode n)
        {
            foreach (var c in n.children)
            {
                if (c.name == "rect" || c.name == "circle" || c.name == "path" || c.name == "ellipse" || c.name == "polygon") return c;
                var r = FindFirstShape(c);
                if (r != null) return r;
            }

            return null;
        }

        // ---------------------------------------------------------------------------------------------------
        // Clips and filters
        // ---------------------------------------------------------------------------------------------------

        static List<SvgPathData> AddClip(string id, Affine2 ctm, SvgNode element, List<SvgPathData> existing, Context ctx)
        {
            if (!ctx.ids.TryGetValue(id, out var clipNode)) return existing;
            bool bboxUnits = (clipNode.Attr("clipPathUnits") ?? clipNode.Attr("maskContentUnits")) == "objectBoundingBox";
            var clipTransform = ctm * ParseTransform(clipNode.Attr("transform"));
            if (bboxUnits && BuildShape(element, ctx))
            {
                var bb = PathFlattener.ControlBounds(ctx.path.points.AsArray(), Affine2.Identity);
                clipTransform = clipTransform * Affine2.Translate(bb.min) * Affine2.Scale(bb.Size);
            }

            // Union of every shape of the clip path, in document space.
            var union = new NativePath(Allocator.Temp, 64);
            var rule = FillRule.NonZero;
            CollectClip(clipNode, clipTransform, ref union, ref rule, ctx, 0);
            if (union.IsEmpty)
            {
                union.Dispose();
                return existing;
            }

            var data = new SvgPathData { rule = rule };
            data.CopyFrom(union);
            union.Dispose();
            var list = new List<SvgPathData>();
            if (existing != null && existing.Count > 0) list.Add(Intersect(existing[0], data));
            else list.Add(data);
            return list;
        }

        static void CollectClip(SvgNode n, Affine2 m, ref NativePath into, ref FillRule rule, Context ctx, int depth)
        {
            if (depth > 16) return;
            foreach (var c in n.children)
            {
                if (c.style == null) ComputeStyles(c, n.style, ctx);
                if (Style(c, "display") == "none") continue;
                var cm = m * ParseTransform(c.Attr("transform"));
                if (c.name == "g" || c.name == "a")
                {
                    CollectClip(c, cm, ref into, ref rule, ctx, depth + 1);
                    continue;
                }

                if (c.name == "use")
                {
                    var href = c.Attr("href");
                    if (href != null && href.StartsWith("#") && ctx.ids.TryGetValue(href.Substring(1), out var t))
                    {
                        var wrapper = new SvgNode { name = "g", style = c.style };
                        wrapper.children.Add(t);
                        CollectClip(wrapper, cm * Affine2.Translate(new float2(Number(c.Attr("x"), 0), Number(c.Attr("y"), 0))), ref into, ref rule, ctx, depth + 1);
                    }

                    continue;
                }

                if (!BuildShape(c, ctx)) continue;
                if (Style(c, "clip-rule") == "evenodd") rule = FillRule.EvenOdd;
                int pi = 0;
                for (int i = 0; i < ctx.path.verbs.Length; i++)
                {
                    var v = (PathVerb)ctx.path.verbs[i];
                    int count = v == PathVerb.Close ? 0 : v == PathVerb.QuadTo ? 2 : v == PathVerb.CubicTo ? 3 : 1;
                    into.verbs.Add((byte)v);
                    for (int k = 0; k < count; k++) into.points.Add(cm.Apply(ctx.path.points[pi++]));
                }
            }
        }

        /// <summary>Geometric intersection of two clip paths (nested clips).</summary>
        static SvgPathData Intersect(SvgPathData a, SvgPathData b)
        {
            var pa = new NativePath(Allocator.TempJob, a.verbs.Count);
            var pb = new NativePath(Allocator.TempJob, b.verbs.Count);
            a.WriteTo(new PathBuilder(pa));
            b.WriteTo(new PathBuilder(pb));
            var poly = new Polylines(Allocator.TempJob, 128);
            var bb = PathFlattener.ControlBounds(pa.points.AsArray(), Affine2.Identity);
            float tol = math.max(math.cmax(bb.Size) * 1e-4f, 1e-4f);
            VectraGeometry.Flatten(pa, Affine2.Identity, tol, 0, true, ref poly);
            VectraGeometry.Flatten(pb, Affine2.Identity, tol, 1, true, ref poly);
            var ops = new NativeArray<Operand>(2, Allocator.TempJob);
            ops[0] = new Operand(a.rule);
            ops[1] = new Operand(b.rule, BooleanOp.Intersect);
            var traps = new NativeList<Trapezoid>(64, Allocator.TempJob);
            VectraGeometry.Tessellate(poly, ops, ref traps);
            var outline = new Polylines(Allocator.TempJob, 64);
            VectraGeometry.BuildContours(traps.AsArray(), ref outline);
            var result = new SvgPathData();
            for (int ci = 0; ci < outline.contours.Length; ci++)
            {
                var c = outline.contours[ci];
                result.verbs.Add((byte)PathVerb.MoveTo);
                result.points.Add(outline.points[c.start]);
                for (int i = 1; i < c.count; i++)
                {
                    result.verbs.Add((byte)PathVerb.LineTo);
                    result.points.Add(outline.points[c.start + i]);
                }

                result.verbs.Add((byte)PathVerb.Close);
            }

            pa.Dispose();
            pb.Dispose();
            poly.Dispose();
            ops.Dispose();
            traps.Dispose();
            outline.Dispose();
            return result;
        }

        static void AddFilter(string id, Affine2 ctm, List<VectraEffect> effects, Context ctx)
        {
            if (!ctx.ids.TryGetValue(id, out var f) || f.name != "filter") return;
            float scale = ctm.UniformScale;
            float dx = 0, dy = 0, blur = -1;
            Color flood = new Color(0, 0, 0, 1);
            bool merge = false, offset = false;
            foreach (var p in f.children)
            {
                if (p.style == null) ComputeStyles(p, f.style, ctx);
                switch (p.name)
                {
                    case "feGaussianBlur":
                        blur = Numbers(p.Attr("stdDeviation") ?? "0").Count > 0 ? Numbers(p.Attr("stdDeviation"))[0] : 0;
                        break;
                    case "feOffset":
                        dx = Number(p.Attr("dx"), 0);
                        dy = Number(p.Attr("dy"), 0);
                        offset = true;
                        break;
                    case "feFlood":
                        flood = ColorOf(Style(p, "flood-color", p.Attr("flood-color") ?? "black"), p);
                        flood.a *= Number(Style(p, "flood-opacity", p.Attr("flood-opacity")), 1f);
                        break;
                    case "feMerge":
                        merge = true;
                        break;
                    case "feDropShadow":
                    {
                        var c = ColorOf(Style(p, "flood-color", p.Attr("flood-color") ?? "black"), p);
                        c.a *= Number(Style(p, "flood-opacity", p.Attr("flood-opacity")), 1f);
                        var sd = Numbers(p.Attr("stdDeviation") ?? "2");
                        effects.Add(new DropShadowEffect
                        {
                            color = c,
                            offset = new Vector2(Number(p.Attr("dx"), 2f), Number(p.Attr("dy"), 2f)) * scale,
                            blur = (sd.Count > 0 ? sd[0] : 2f) * 2f * scale
                        });
                        break;
                    }
                    case "feColorMatrix":
                    {
                        string type = p.Attr("type", "matrix");
                        var vals = Numbers(p.Attr("values") ?? "");
                        if (type == "saturate" && vals.Count > 0) effects.Add(new ColorAdjustEffect { saturation = vals[0] });
                        else if (type == "hueRotate" && vals.Count > 0) effects.Add(new ColorAdjustEffect { hueShift = vals[0] });
                        else ctx.Warn("feColorMatrix type '" + type + "' is not supported.");
                        break;
                    }
                    default:
                        ctx.Warn("Filter primitive <" + p.name + "> is not supported.");
                        break;
                }
            }

            if (offset || merge)
            {
                if (flood.a >= 1f && flood.r == 0 && flood.g == 0 && flood.b == 0) flood.a = 0.5f;
                effects.Add(new DropShadowEffect
                {
                    color = flood,
                    offset = new Vector2(dx, dy) * scale,
                    blur = Mathf.Max(blur, 0f) * 2f * scale
                });
            }
            else if (blur > 0f)
            {
                effects.Add(new LayerBlurEffect { blur = blur * 2f * scale });
            }
        }

        // ---------------------------------------------------------------------------------------------------
        // Text and images
        // ---------------------------------------------------------------------------------------------------

        static void RenderText(SvgNode n, Affine2 ctm, float opacity, Context ctx)
        {
            var text = CollectText(n).Trim();
            if (text.Length == 0) return;
            float x = Length(FirstListValue(n.Attr("x")), 0, ctx.viewportW, ctx);
            float y = Length(FirstListValue(n.Attr("y")), 0, ctx.viewportH, ctx);
            var span = n.children.Find(c => c.name == "tspan");
            if (span != null)
            {
                if (span.Attr("x") != null) x = Length(FirstListValue(span.Attr("x")), x, ctx.viewportW, ctx);
                if (span.Attr("y") != null) y = Length(FirstListValue(span.Attr("y")), y, ctx.viewportH, ctx);
            }

            float scale = ctm.UniformScale;
            var fillValue = Style(n, "fill", "black");
            var color = fillValue == "none" ? Color.clear : ColorOf(fillValue.StartsWith("url") ? "black" : fillValue, n);
            color.a *= Number(Style(n, "fill-opacity"), 1f) * opacity;
            string anchor = Style(n, "text-anchor", "start");
            string weight = Style(n, "font-weight", "normal");
            var e = new SvgElementData
            {
                tag = "text",
                id = n.Attr("id"),
                className = n.Attr("class"),
                isText = true,
                text = text,
                textPosition = ctm.Apply(new float2(x, y)),
                fontSize = Length(Style(n, "font-size", "16"), 16, ctx.viewportH, ctx) * scale,
                fontFamily = Style(n, "font-family", ""),
                bold = weight == "bold" || weight == "bolder" || (int.TryParse(weight, out int wv) && wv >= 600),
                italic = Style(n, "font-style") == "italic",
                anchor = anchor == "middle" ? SvgTextAnchor.Middle : anchor == "end" ? SvgTextAnchor.End : SvgTextAnchor.Start,
                textColor = color,
                textRotation = math.degrees(math.atan2(ctm.b, ctm.a))
            };
            ctx.doc.elements.Add(e);
        }

        static string CollectText(SvgNode n)
        {
            var sb = new System.Text.StringBuilder(n.text);
            foreach (var c in n.children)
                if (c.name == "tspan" || c.name == "textPath" || c.name == "a") sb.Append(' ').Append(CollectText(c));
            return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), "\\s+", " ");
        }

        static string FirstListValue(string v)
        {
            if (string.IsNullOrEmpty(v)) return v;
            var list = SplitList(v);
            return list.Count > 0 ? list[0] : v;
        }

        static void RenderImage(SvgNode n, Affine2 ctm, float opacity, Context ctx)
        {
            var href = n.Attr("href");
            if (string.IsNullOrEmpty(href)) return;
            float x = Length(n.Attr("x"), 0, ctx.viewportW, ctx), y = Length(n.Attr("y"), 0, ctx.viewportH, ctx);
            float w = Length(n.Attr("width"), 0, ctx.viewportW, ctx), h = Length(n.Attr("height"), 0, ctx.viewportH, ctx);
            byte[] bytes = null;
            int comma = href.IndexOf(',');
            if (href.StartsWith("data:") && comma > 0 && href.Substring(0, comma).Contains("base64"))
            {
                try { bytes = Convert.FromBase64String(href.Substring(comma + 1)); }
                catch { ctx.Warn("Invalid base64 image data."); }
            }
            else ctx.Warn("External image references are not loaded: " + href);

            if (bytes == null) return;
            var p0 = ctm.Apply(new float2(x, y));
            var p1 = ctm.Apply(new float2(x + w, y + h));
            ctx.doc.elements.Add(new SvgElementData
            {
                tag = "image",
                id = n.Attr("id"),
                className = n.Attr("class"),
                isImage = true,
                imageData = bytes,
                imageRect = Rect.MinMaxRect(math.min(p0.x, p1.x), math.min(p0.y, p1.y), math.max(p0.x, p1.x), math.max(p0.y, p1.y)),
                textColor = new Color(1, 1, 1, opacity)
            });
        }

        // ---------------------------------------------------------------------------------------------------
        // Value parsing
        // ---------------------------------------------------------------------------------------------------

        static float Diag(float w, float h) => Mathf.Sqrt((w * w + h * h) * 0.5f);

        static string Url(string v)
        {
            if (string.IsNullOrEmpty(v)) return null;
            int i = v.IndexOf("url(", StringComparison.Ordinal);
            if (i < 0) return null;
            int close = v.IndexOf(')', i);
            if (close < 0) return null;
            string inner = v.Substring(i + 4, close - i - 4).Trim().Trim('"', '\'');
            return inner.StartsWith("#") ? inner.Substring(1) : null;
        }

        public static float Number(string v, float fallback)
        {
            if (string.IsNullOrEmpty(v)) return fallback;
            int i = 0;
            return SvgPathParser.ReadNumber(v, ref i, out float f) ? f : fallback;
        }

        static float Fraction(string v, float fallback)
        {
            if (string.IsNullOrEmpty(v)) return fallback;
            v = v.Trim();
            if (v.EndsWith("%")) return Number(v.Substring(0, v.Length - 1), fallback * 100f) / 100f;
            return Number(v, fallback);
        }

        /// <summary>Gradient coordinate: plain numbers or percentages of the reference size.</summary>
        static float Coord(string v, float fallback, float reference)
        {
            if (string.IsNullOrEmpty(v)) return fallback;
            v = v.Trim();
            if (v.EndsWith("%")) return Number(v.Substring(0, v.Length - 1), 0f) / 100f * reference;
            return Number(v, fallback);
        }

        /// <summary>Length with units (px, pt, pc, mm, cm, in, em, ex, %).</summary>
        static float Length(string v, float fallback, float percentOf, Context ctx)
        {
            if (string.IsNullOrEmpty(v)) return fallback;
            v = v.Trim();
            int i = 0;
            if (!SvgPathParser.ReadNumber(v, ref i, out float f)) return fallback;
            string unit = v.Substring(i).Trim();
            switch (unit)
            {
                case "": case "px": return f;
                case "%": return f / 100f * percentOf;
                case "pt": return f * 4f / 3f;
                case "pc": return f * 16f;
                case "mm": return f * 3.7795276f;
                case "cm": return f * 37.795276f;
                case "in": return f * 96f;
                case "em": case "rem": return f * 16f;
                case "ex": return f * 8f;
                default: return f;
            }
        }

        static List<string> SplitList(string v)
        {
            var r = new List<string>();
            foreach (var part in v.Split(new[] { ' ', ',', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)) r.Add(part);
            return r;
        }

        static List<float> Numbers(string v)
        {
            var r = new List<float>();
            if (string.IsNullOrEmpty(v)) return r;
            int i = 0;
            while (SvgPathParser.ReadNumber(v, ref i, out float f)) r.Add(f);
            return r;
        }

        static Rect? ParseViewBox(string v)
        {
            var n = Numbers(v);
            if (n.Count < 4 || n[2] <= 0 || n[3] <= 0) return null;
            return new Rect(n[0], n[1], n[2], n[3]);
        }

        static void ParseAspect(string v, out SvgAlign align, out bool slice)
        {
            align = SvgAlign.XMidYMid;
            slice = false;
            if (string.IsNullOrEmpty(v)) return;
            var parts = v.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int k = parts.Length > 0 && parts[0] == "defer" ? 1 : 0;
            if (parts.Length > k)
            {
                switch (parts[k])
                {
                    case "none": align = SvgAlign.None; break;
                    case "xMinYMin": align = SvgAlign.XMinYMin; break;
                    case "xMidYMin": align = SvgAlign.XMidYMin; break;
                    case "xMaxYMin": align = SvgAlign.XMaxYMin; break;
                    case "xMinYMid": align = SvgAlign.XMinYMid; break;
                    case "xMaxYMid": align = SvgAlign.XMaxYMid; break;
                    case "xMinYMax": align = SvgAlign.XMinYMax; break;
                    case "xMidYMax": align = SvgAlign.XMidYMax; break;
                    case "xMaxYMax": align = SvgAlign.XMaxYMax; break;
                }
            }

            slice = parts.Length > k + 1 && parts[k + 1] == "slice";
        }

        static LineCap ParseCap(string v) => v == "round" ? LineCap.Round : v == "square" ? LineCap.Square : LineCap.Butt;

        static LineJoin ParseJoin(string v) => v == "round" ? LineJoin.Round : v == "bevel" ? LineJoin.Bevel : LineJoin.Miter;

        /// <summary>Parses an SVG transform list.</summary>
        public static Affine2 ParseTransform(string v)
        {
            var m = Affine2.Identity;
            if (string.IsNullOrEmpty(v)) return m;
            int i = 0;
            while (i < v.Length)
            {
                while (i < v.Length && (char.IsWhiteSpace(v[i]) || v[i] == ',')) i++;
                int nameStart = i;
                while (i < v.Length && char.IsLetter(v[i])) i++;
                if (i == nameStart) break;
                string name = v.Substring(nameStart, i - nameStart);
                int open = v.IndexOf('(', i);
                int close = open >= 0 ? v.IndexOf(')', open) : -1;
                if (open < 0 || close < 0) break;
                var a = Numbers(v.Substring(open + 1, close - open - 1));
                i = close + 1;
                Affine2 t = Affine2.Identity;
                switch (name)
                {
                    case "matrix":
                        if (a.Count >= 6) t = new Affine2(a[0], a[1], a[2], a[3], a[4], a[5]);
                        break;
                    case "translate":
                        t = Affine2.Translate(new float2(a.Count > 0 ? a[0] : 0, a.Count > 1 ? a[1] : 0));
                        break;
                    case "scale":
                        t = Affine2.Scale(new float2(a.Count > 0 ? a[0] : 1, a.Count > 1 ? a[1] : (a.Count > 0 ? a[0] : 1)));
                        break;
                    case "rotate":
                        if (a.Count >= 3) t = Affine2.Rotate(math.radians(a[0]), new float2(a[1], a[2]));
                        else if (a.Count >= 1) t = Affine2.Rotate(math.radians(a[0]));
                        break;
                    case "skewX":
                        if (a.Count >= 1) t = Affine2.SkewX(math.radians(a[0]));
                        break;
                    case "skewY":
                        if (a.Count >= 1) t = Affine2.SkewY(math.radians(a[0]));
                        break;
                }

                m = m * t;
            }

            return m;
        }
    }

    /// <summary>CSS/SVG colour parsing (named colours, hex, rgb(a), hsl(a)).</summary>
    public static class SvgColor
    {
        static Dictionary<string, Color32> s_Named;

        const string Names =
            "aliceblue:f0f8ff,antiquewhite:faebd7,aqua:00ffff,aquamarine:7fffd4,azure:f0ffff,beige:f5f5dc,bisque:ffe4c4,black:000000," +
            "blanchedalmond:ffebcd,blue:0000ff,blueviolet:8a2be2,brown:a52a2a,burlywood:deb887,cadetblue:5f9ea0,chartreuse:7fff00," +
            "chocolate:d2691e,coral:ff7f50,cornflowerblue:6495ed,cornsilk:fff8dc,crimson:dc143c,cyan:00ffff,darkblue:00008b," +
            "darkcyan:008b8b,darkgoldenrod:b8860b,darkgray:a9a9a9,darkgreen:006400,darkgrey:a9a9a9,darkkhaki:bdb76b," +
            "darkmagenta:8b008b,darkolivegreen:556b2f,darkorange:ff8c00,darkorchid:9932cc,darkred:8b0000,darksalmon:e9967a," +
            "darkseagreen:8fbc8f,darkslateblue:483d8b,darkslategray:2f4f4f,darkslategrey:2f4f4f,darkturquoise:00ced1," +
            "darkviolet:9400d3,deeppink:ff1493,deepskyblue:00bfff,dimgray:696969,dimgrey:696969,dodgerblue:1e90ff," +
            "firebrick:b22222,floralwhite:fffaf0,forestgreen:228b22,fuchsia:ff00ff,gainsboro:dcdcdc,ghostwhite:f8f8ff," +
            "gold:ffd700,goldenrod:daa520,gray:808080,grey:808080,green:008000,greenyellow:adff2f,honeydew:f0fff0," +
            "hotpink:ff69b4,indianred:cd5c5c,indigo:4b0082,ivory:fffff0,khaki:f0e68c,lavender:e6e6fa,lavenderblush:fff0f5," +
            "lawngreen:7cfc00,lemonchiffon:fffacd,lightblue:add8e6,lightcoral:f08080,lightcyan:e0ffff," +
            "lightgoldenrodyellow:fafad2,lightgray:d3d3d3,lightgreen:90ee90,lightgrey:d3d3d3,lightpink:ffb6c1," +
            "lightsalmon:ffa07a,lightseagreen:20b2aa,lightskyblue:87cefa,lightslategray:778899,lightslategrey:778899," +
            "lightsteelblue:b0c4de,lightyellow:ffffe0,lime:00ff00,limegreen:32cd32,linen:faf0e6,magenta:ff00ff,maroon:800000," +
            "mediumaquamarine:66cdaa,mediumblue:0000cd,mediumorchid:ba55d3,mediumpurple:9370db,mediumseagreen:3cb371," +
            "mediumslateblue:7b68ee,mediumspringgreen:00fa9a,mediumturquoise:48d1cc,mediumvioletred:c71585," +
            "midnightblue:191970,mintcream:f5fffa,mistyrose:ffe4e1,moccasin:ffe4b5,navajowhite:ffdead,navy:000080," +
            "oldlace:fdf5e6,olive:808000,olivedrab:6b8e23,orange:ffa500,orangered:ff4500,orchid:da70d6,palegoldenrod:eee8aa," +
            "palegreen:98fb98,paleturquoise:afeeee,palevioletred:db7093,papayawhip:ffefd5,peachpuff:ffdab9,peru:cd853f," +
            "pink:ffc0cb,plum:dda0dd,powderblue:b0e0e6,purple:800080,rebeccapurple:663399,red:ff0000,rosybrown:bc8f8f," +
            "royalblue:4169e1,saddlebrown:8b4513,salmon:fa8072,sandybrown:f4a460,seagreen:2e8b57,seashell:fff5ee," +
            "sienna:a0522d,silver:c0c0c0,skyblue:87ceeb,slateblue:6a5acd,slategray:708090,slategrey:708090,snow:fffafa," +
            "springgreen:00ff7f,steelblue:4682b4,tan:d2b48c,teal:008080,thistle:d8bfd8,tomato:ff6347,turquoise:40e0d0," +
            "violet:ee82ee,wheat:f5deb3,white:ffffff,whitesmoke:f5f5f5,yellow:ffff00,yellowgreen:9acd32";

        public static bool TryParse(string v, out Color color)
        {
            color = Color.black;
            if (string.IsNullOrEmpty(v)) return false;
            v = v.Trim();
            string lower = v.ToLowerInvariant();
            if (lower == "transparent")
            {
                color = new Color(0, 0, 0, 0);
                return true;
            }

            if (lower[0] == '#') return Hex(lower.Substring(1), out color);
            if (lower.StartsWith("rgb")) return Func(lower, false, out color);
            if (lower.StartsWith("hsl")) return Func(lower, true, out color);
            if (s_Named == null)
            {
                s_Named = new Dictionary<string, Color32>();
                foreach (var e in Names.Split(','))
                {
                    var kv = e.Split(':');
                    Hex(kv[1], out var c);
                    s_Named[kv[0]] = c;
                }
            }

            if (s_Named.TryGetValue(lower, out var nc))
            {
                color = nc;
                return true;
            }

            return false;
        }

        static bool Hex(string h, out Color c)
        {
            c = Color.black;
            int Digit(char ch) => ch <= '9' ? ch - '0' : ch - 'a' + 10;
            for (int i = 0; i < h.Length; i++)
                if (!Uri.IsHexDigit(h[i])) return false;
            if (h.Length == 3 || h.Length == 4)
            {
                c = new Color(Digit(h[0]) * 17 / 255f, Digit(h[1]) * 17 / 255f, Digit(h[2]) * 17 / 255f,
                    h.Length == 4 ? Digit(h[3]) * 17 / 255f : 1f);
                return true;
            }

            if (h.Length == 6 || h.Length == 8)
            {
                c = new Color((Digit(h[0]) * 16 + Digit(h[1])) / 255f, (Digit(h[2]) * 16 + Digit(h[3])) / 255f,
                    (Digit(h[4]) * 16 + Digit(h[5])) / 255f, h.Length == 8 ? (Digit(h[6]) * 16 + Digit(h[7])) / 255f : 1f);
                return true;
            }

            return false;
        }

        static bool Func(string v, bool hsl, out Color c)
        {
            c = Color.black;
            int open = v.IndexOf('('), close = v.LastIndexOf(')');
            if (open < 0 || close < open) return false;
            var parts = v.Substring(open + 1, close - open - 1).Replace('/', ' ')
                .Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) return false;
            float Comp(string s, float scale)
            {
                s = s.Trim();
                if (s.EndsWith("%")) return float.Parse(s.TrimEnd('%'), CultureInfo.InvariantCulture) / 100f;
                return float.Parse(s.Replace("deg", ""), CultureInfo.InvariantCulture) / scale;
            }

            try
            {
                float a = parts.Length > 3 ? Comp(parts[3], 1f) : 1f;
                if (!hsl)
                {
                    c = new Color(Mathf.Clamp01(Comp(parts[0], 255f)), Mathf.Clamp01(Comp(parts[1], 255f)),
                        Mathf.Clamp01(Comp(parts[2], 255f)), Mathf.Clamp01(a));
                }
                else
                {
                    float h = float.Parse(parts[0].Replace("deg", ""), CultureInfo.InvariantCulture) / 360f;
                    h -= Mathf.Floor(h);
                    float s = Comp(parts[1], 100f), l = Comp(parts[2], 100f);
                    float q = l < 0.5f ? l * (1 + s) : l + s - l * s;
                    float p = 2 * l - q;
                    float Hue(float t)
                    {
                        if (t < 0) t += 1;
                        if (t > 1) t -= 1;
                        if (t < 1f / 6) return p + (q - p) * 6 * t;
                        if (t < 0.5f) return q;
                        if (t < 2f / 3) return p + (q - p) * (2f / 3 - t) * 6;
                        return p;
                    }

                    c = new Color(Hue(h + 1f / 3), Hue(h), Hue(h - 1f / 3), Mathf.Clamp01(a));
                }

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
