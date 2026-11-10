using System.Collections.Generic;
using FirstSymbol.Vectra.Geometry;
using Unity.Mathematics;
using UnityEngine;

namespace FirstSymbol.Vectra.Svg
{
    // Compositing layers: which subtrees are drawn on their own (see SvgLayerData) and how masks are read.
    public static partial class SvgParser
    {
        sealed class LayerScope
        {
            public int start, layerStart;
            /// <summary>The element's own opacity, not yet applied to the paths below it.</summary>
            public float own = 1f;
            public SvgBlendMode blend;
            public SvgMaskKind mask;
            public List<SvgElementData> maskContent;
            public float blur;
            public float prevTextExtra;
            /// <summary>The element's filter (shadows, blur) as compositor steps, or null.</summary>
            public List<SvgFilterStep> filter;
        }

        /// <summary>
        /// A filter on an element is computed from the picture of everything the element draws, so it needs a layer; inside masks and
        /// pattern tiles, and past the layer limits, it stays a native effect of each shape.
        /// </summary>
        static bool CanDeferFilter(SvgNode n, Context ctx) =>
            n.name != "foreignObject" && ctx.sink == null && ctx.patterns.capture == 0 &&
            ctx.layerDepth < MaxLayerDepth && ctx.doc.layers.Count < MaxLayers;

        static bool HasFilterWork(List<SvgFilterStep> steps)
        {
            foreach (var s in steps)
                if (s.kind != SvgFilterKind.Source) return true;
            return false;
        }

        const float OpaqueEps = 1e-3f;
        // Every nested layer needs a texture of the size of the output and every layer a pass over it: hostile files must not make hundreds.
        const int MaxLayerDepth = 8, MaxLayers = 256;

        /// <summary>
        /// Looks at the compositing properties of an element before its children are drawn: opacity, mask, mix-blend-mode and the
        /// background blur of Figma exports. Returns null when the element needs no layer decision. Otherwise the element's own
        /// opacity is taken out of <paramref name="op"/> (see <see cref="EndLayer"/>), and a mask that is a plain white shape is
        /// turned into a geometric clip (cheap and exact).
        /// </summary>
        static LayerScope BeginLayer(SvgNode n, Affine2 local, float parentOpacity, ref float op, ref List<SvgPathData> clips,
            List<SvgFilterStep> filter, Context ctx)
        {
            // The content of a foreignObject is not drawn; Figma's background blur is read from the shape that follows it.
            if (n.name == "foreignObject") return null;
            var maskRef = Url(Style(n, "mask"));
            if (ctx.sink != null || ctx.patterns.capture > 0)
            {
                // Inside a mask or a pattern tile: nested masks stay geometric, opacity goes to the paths.
                if (maskRef != null)
                {
                    ctx.Warn("SVG: a mask inside a mask is applied as a geometric clip.");
                    clips = AddClip(maskRef, local, n, clips, ctx);
                }

                return null;
            }

            float own = Number(Style(n, "opacity"), 1f);
            var blend = SvgLayers.ParseBlend(Style(n, "mix-blend-mode"));
            if (ctx.layerDepth >= MaxLayerDepth || ctx.doc.layers.Count >= MaxLayers)
            {
                bool wanted = own < 1f - OpaqueEps || blend != SvgBlendMode.Normal || maskRef != null || n.attributes.ContainsKey("data-figma-bg-blur-radius");
                if (wanted)
                    ctx.Warn($"SVG limit reached: more than {MaxLayers} layers or layers nested deeper than {MaxLayerDepth} (group opacity, masks, blend modes); the rest is drawn without them (opacity per shape, masks as clips).");
                if (maskRef != null) clips = AddClip(maskRef, local, n, clips, ctx);
                return null;
            }

            float blur = Number(n.Attr("data-figma-bg-blur-radius"), 0f) * 0.5f * FigmaBlurScale;
            var kind = SvgMaskKind.None;
            List<SvgElementData> maskContent = null;
            if (maskRef != null && !ReadMask(maskRef, n, local, ctx, out kind, out maskContent))
            {
                kind = SvgMaskKind.None;
                clips = AddClip(maskRef, local, n, clips, ctx);
            }

            if (kind != SvgMaskKind.None) blur = 0f;
            bool tentative = own < 1f - OpaqueEps;
            if (!tentative && kind == SvgMaskKind.None && blend == SvgBlendMode.Normal && blur <= 0f && filter == null) return null;
            op = parentOpacity;
            var scope = new LayerScope
            {
                start = ctx.doc.elements.Count,
                layerStart = ctx.doc.layers.Count,
                own = tentative ? Mathf.Clamp01(own) : 1f,
                blend = blend,
                mask = kind,
                maskContent = maskContent,
                blur = blur,
                filter = filter,
                prevTextExtra = ctx.textExtra
            };
            ctx.textExtra *= scope.own;
            ctx.layerDepth++;
            return scope;
        }

        static bool HasBackdropFilter(SvgNode n, int depth)
        {
            if (depth > 4) return false;
            var style = n.Attr("style");
            if (style != null && style.IndexOf("backdrop-filter", System.StringComparison.Ordinal) >= 0) return true;
            foreach (var c in n.children)
                if (HasBackdropFilter(c, depth + 1)) return true;
            return false;
        }

        /// <summary>Decides after the children are drawn whether the subtree becomes a layer, and records it.</summary>
        static void EndLayer(LayerScope s, Context ctx)
        {
            ctx.textExtra = s.prevTextExtra;
            ctx.layerDepth--;
            var doc = ctx.doc;
            int end = doc.elements.Count;
            int paths = 0, first = -1;
            bool complex = false;
            for (int i = s.start; i < end; i++)
            {
                var e = doc.elements[i];
                if (e.isText || e.isImage) continue;
                paths++;
                if (first < 0) first = i;
                if ((e.hasFill && e.hasStroke) || e.effects.Count > 0) complex = true;
            }

            if (paths == 0) return;
            if (s.filter != null)
            {
                // One simple shape keeps its native, resolution-free effects.
                if (paths == 1 && s.mask == SvgMaskKind.None && s.blend == SvgBlendMode.Normal && s.blur <= 0f && s.own >= 1f &&
                    doc.layers.Count == s.layerStart && CanDrawNatively(s.filter, doc.elements[first]))
                {
                    AddNativeEffects(s.filter, doc.elements[first]);
                    return;
                }
            }

            bool forced = s.mask != SvgMaskKind.None || s.blend != SvgBlendMode.Normal || s.blur > 0f || s.filter != null;
            if (!forced)
            {
                // One path with a fill or a stroke (not both) cannot overlap itself: the opacity goes into its paint.
                if (paths == 1 && !complex && doc.layers.Count == s.layerStart)
                {
                    PushOpacity(doc.elements[first], s.own);
                    return;
                }
            }

            var layer = new SvgLayerData { from = s.start, to = end, opacity = s.own, blend = s.blend, mask = s.mask, blurSigma = s.blur, filter = s.filter };
            if (s.mask != SvgMaskKind.None && s.maskContent != null)
            {
                layer.maskFrom = doc.maskElements.Count;
                doc.maskElements.AddRange(s.maskContent);
                layer.maskTo = doc.maskElements.Count;
            }
            else if (s.blur > 0f)
            {
                // The blurred background is limited to the silhouette of the shape.
                layer.mask = SvgMaskKind.Alpha;
                layer.maskFrom = doc.maskElements.Count;
                for (int i = s.start; i < end; i++)
                {
                    var e = doc.elements[i];
                    if (e.isText || e.isImage || e.path == null || e.path.IsEmpty) continue;
                    var sil = new SvgElementData { tag = e.tag, path = e.path, hasFill = true, fill = VectraPaint.Solid(Color.white) };
                    sil.clips.AddRange(e.clips);
                    doc.maskElements.Add(sil);
                }

                layer.maskTo = doc.maskElements.Count;
                if (layer.maskTo <= layer.maskFrom)
                {
                    layer.blurSigma = 0f;
                    layer.mask = SvgMaskKind.None;
                }
            }

            int index = doc.layers.Count;
            for (int i = s.layerStart; i < index; i++)
                if (doc.layers[i].parent < 0) doc.layers[i].parent = index;
            doc.layers.Add(layer);
        }

        static void PushOpacity(SvgElementData e, float opacity)
        {
            if (opacity >= 1f) return;
            if (e.fill != null)
            {
                e.fill.opacity *= opacity;
                e.hasFill = e.hasFill && e.fill.opacity > 0f;
            }

            if (e.stroke != null && e.stroke.paint != null)
            {
                e.stroke.paint.opacity *= opacity;
                e.hasStroke = e.hasStroke && e.stroke.paint.opacity > 0f;
            }
        }

        /// <summary>
        /// Reads the content of a mask. Returns false when the mask is a plain geometric one (opaque white fills in a luminance
        /// mask, opaque fills in an alpha mask, no strokes): those are applied as clips, which is exact and cheaper than a layer.
        /// </summary>
        static bool ReadMask(string id, SvgNode element, Affine2 local, Context ctx, out SvgMaskKind kind, out List<SvgElementData> content)
        {
            kind = SvgMaskKind.None;
            content = null;
            if (!ctx.ids.TryGetValue(id, out var maskNode) || maskNode.name != "mask") return false;
            if (maskNode.Attr("maskContentUnits") == "objectBoundingBox")
            {
                ctx.Warn("SVG: a mask with maskContentUnits=\"objectBoundingBox\" is applied as a geometric clip (luminance is ignored).");
                return false;
            }

            string type = Style(maskNode, "mask-type") ?? maskNode.Attr("mask-type");
            var maskKind = type != null && type.Trim() == "alpha" ? SvgMaskKind.Alpha : SvgMaskKind.Luminance;
            var list = new List<SvgElementData>();
            var saved = ctx.sink;
            ctx.sink = list;
            try
            {
                foreach (var c in maskNode.children) Render(c, local, 1f, null, null, ctx);
            }
            finally
            {
                ctx.sink = saved;
            }

            if (list.Count == 0 || IsGeometricMask(list, maskKind)) return false;
            if (maskNode.Attr("maskUnits") == "userSpaceOnUse")
            {
                float vw = ctx.viewportW, vh = ctx.viewportH;
                float x = Length(maskNode.Attr("x"), -0.1f * vw, vw, ctx), y = Length(maskNode.Attr("y"), -0.1f * vh, vh, ctx);
                float w = Length(maskNode.Attr("width"), 1.2f * vw, vw, ctx), h = Length(maskNode.Attr("height"), 1.2f * vh, vh, ctx);
                var region = RectPath(local, x, y, w, h);
                foreach (var e in list)
                    e.clips = e.clips.Count > 0 ? new List<SvgPathData> { Intersect(e.clips[0], region) } : new List<SvgPathData> { region };
            }

            kind = maskKind;
            content = list;
            return true;
        }

        static bool IsGeometricMask(List<SvgElementData> list, SvgMaskKind kind)
        {
            foreach (var e in list)
            {
                if (e.isText || e.isImage) return false;
                if (!e.hasFill || e.fill == null || e.fill.type != PaintType.Solid || e.hasStroke) return false;
                if (e.effects.Count > 0 || e.clips.Count > 0) return false;
                var c = e.fill.color;
                if (e.fill.opacity * c.a < 1f - OpaqueEps) return false;
                if (kind == SvgMaskKind.Luminance && (c.r < 1f - OpaqueEps || c.g < 1f - OpaqueEps || c.b < 1f - OpaqueEps)) return false;
            }

            return true;
        }

        static SvgPathData RectPath(Affine2 m, float x, float y, float w, float h)
        {
            var d = new SvgPathData();
            d.verbs.Add((byte)PathVerb.MoveTo);
            d.verbs.Add((byte)PathVerb.LineTo);
            d.verbs.Add((byte)PathVerb.LineTo);
            d.verbs.Add((byte)PathVerb.LineTo);
            d.verbs.Add((byte)PathVerb.Close);
            d.points.Add(m.Apply(new float2(x, y)));
            d.points.Add(m.Apply(new float2(x + w, y)));
            d.points.Add(m.Apply(new float2(x + w, y + h)));
            d.points.Add(m.Apply(new float2(x, y + h)));
            return d;
        }
    }
}
