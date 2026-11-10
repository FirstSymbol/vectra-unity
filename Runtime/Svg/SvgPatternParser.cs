using System;
using System.Collections.Generic;
using System.Globalization;
using FirstSymbol.Vectra.Geometry;
using Unity.Mathematics;
using UnityEngine;

namespace FirstSymbol.Vectra.Svg
{
    /// <summary>Parse-time state of pattern paints: the patterns being read (to stop cycles) and the entries already made.</summary>
    sealed class SvgPatternState
    {
        public struct Tile
        {
            public int index;
            /// <summary>The tile is exactly one image: the paint uses the image itself and no tile is drawn.</summary>
            public bool direct;
            public float opacity;
        }

        public readonly HashSet<SvgNode> active = new HashSet<SvgNode>();
        public readonly Dictionary<string, Tile> tiles = new Dictionary<string, Tile>();
        public readonly Dictionary<string, int> images = new Dictionary<string, int>();
        public readonly Dictionary<SvgNode, int> nodes = new Dictionary<SvgNode, int>();
        /// <summary>Greater than zero while the content of a pattern is read.</summary>
        public int capture;

        public int Id(SvgNode n)
        {
            if (!nodes.TryGetValue(n, out int id)) nodes[n] = id = nodes.Count;
            return id;
        }
    }

    /// <summary>
    /// SVG pattern paints. The tile (patternUnits, x, y, width, height, patternTransform) and its content (patternContentUnits,
    /// viewBox, preserveAspectRatio, href inheritance) are resolved here into a texture paint whose mapping goes straight from
    /// document space to the tile's texture. A tile that is one image filling it exactly uses that image (no resampling); any other
    /// content (shapes, gradients, nested patterns, images that are cropped, fitted or rotated) is drawn into a tile texture the first
    /// time the document is built.
    /// </summary>
    public static partial class SvgParser
    {
        static readonly Affine2 s_FlipV = new Affine2(1f, 0f, 0f, -1f, 0f, 1f);

        static VectraPaint InvisiblePaint() => VectraPaint.Solid(Color.clear);

        static VectraPaint PatternPaint(SvgNode p, Bounds2 bbox, Affine2 docToUser, Context ctx)
        {
            var st = ctx.patterns;
            if (st.active.Contains(p))
            {
                ctx.Warn("SVG: a pattern is used inside its own content; the inner use is skipped.");
                return InvisiblePaint();
            }

            var content = PatternContent(p, ctx);
            if (content == null) return InvisiblePaint();
            bool unitsBox = GradAttr(p, "patternUnits", ctx) != "userSpaceOnUse";
            bool contentBox = GradAttr(p, "patternContentUnits", ctx) == "objectBoundingBox";
            var vb = ParseViewBox(GradAttr(p, "viewBox", ctx));
            bool boxValid = bbox.IsValid && bbox.Size.x > 0f && bbox.Size.y > 0f;
            if ((unitsBox || contentBox && !vb.HasValue) && !boxValid) return InvisiblePaint();
            float2 bs = boxValid ? bbox.Size : new float2(1f);
            float2 bo = boxValid ? bbox.min : float2.zero;

            string xs = GradAttr(p, "x", ctx), ys = GradAttr(p, "y", ctx), ws = GradAttr(p, "width", ctx), hs = GradAttr(p, "height", ctx);
            float tx, ty, tw, th;
            if (unitsBox)
            {
                tx = bo.x + Coord(xs, 0f, 1f) * bs.x;
                ty = bo.y + Coord(ys, 0f, 1f) * bs.y;
                tw = Coord(ws, 0f, 1f) * bs.x;
                th = Coord(hs, 0f, 1f) * bs.y;
            }
            else
            {
                tx = Length(xs, 0f, ctx.viewportW, ctx);
                ty = Length(ys, 0f, ctx.viewportH, ctx);
                tw = Length(ws, 0f, ctx.viewportW, ctx);
                th = Length(hs, 0f, ctx.viewportH, ctx);
            }

            if (!(tw > 0f) || !(th > 0f) || float.IsInfinity(tw) || float.IsInfinity(th)) return InvisiblePaint();

            // pattern content -> tile (origin at the tile's top-left corner)
            Affine2 c = Affine2.Identity;
            if (vb.HasValue) c = PatternViewBox(vb.Value, GradAttr(p, "preserveAspectRatio", ctx), tw, th);
            else if (contentBox) c = Affine2.Scale(bs);

            // tile -> user space of the painted element
            var toUser = ParseTransform(GradAttr(p, "patternTransform", ctx)) * Affine2.Translate(new float2(tx, ty));
            if (math.abs(toUser.Determinant) < 1e-12f) return InvisiblePaint();

            var ctm = docToUser.Inverse();
            var toDoc = ctm * toUser;
            int pw = TilePixels(tw * math.length(toDoc.ApplyVector(new float2(1f, 0f))));
            int ph = TilePixels(th * math.length(toDoc.ApplyVector(new float2(0f, 1f))));
            var inv = CultureInfo.InvariantCulture;
            string key = st.Id(p).ToString(inv) + "|" + tw.ToString("R", inv) + "|" + th.ToString("R", inv) + "|" + pw + "|" + ph +
                         (contentBox && !vb.HasValue ? "|" + bs.x.ToString("R", inv) + "|" + bs.y.ToString("R", inv) : "");
            if (!st.tiles.TryGetValue(key, out var tile))
            {
                tile = ReadTile(p, content, c, tw, th, pw, ph, ctx);
                st.tiles[key] = tile;
            }

            if (tile.index < 0) return InvisiblePaint();
            return new VectraPaint
            {
                type = PaintType.Texture,
                textureFit = TextureFit.Tile,
                useCustomMapping = true,
                textureSource = tile.index,
                opacity = tile.direct ? tile.opacity : 1f,
                customMapping = s_FlipV * Affine2.Scale(new float2(1f / tw, 1f / th)) * toUser.Inverse() * docToUser
            };
        }

        static int TilePixels(float size)
        {
            if (float.IsNaN(size) || float.IsInfinity(size)) return 64;
            return Mathf.Clamp(Mathf.CeilToInt(size * SvgPatterns.PixelsPerUnit), 1, SvgPatterns.MaxTileSide);
        }

        // The first pattern in the href chain that has children supplies the content.
        static SvgNode PatternContent(SvgNode p, Context ctx)
        {
            var n = p;
            for (int i = 0; n != null && i < 16; i++)
            {
                if (n.children.Exists(ch => ch.name != "title" && ch.name != "desc" && ch.name != "metadata")) return n;
                var href = n.Attr("href");
                n = href != null && href.StartsWith("#") && ctx.ids.TryGetValue(href.Substring(1), out var next) && next.name == "pattern" ? next : null;
            }

            return null;
        }

        static Affine2 PatternViewBox(Rect v, string aspect, float w, float h)
        {
            ParseAspect(aspect, out var align, out bool slice);
            float sx = w / Mathf.Max(v.width, 1e-6f), sy = h / Mathf.Max(v.height, 1e-6f);
            float tx = 0f, ty = 0f;
            if (align != SvgAlign.None)
            {
                float s = slice ? Mathf.Max(sx, sy) : Mathf.Min(sx, sy);
                sx = sy = s;
                int ix = ((int)align - 1) % 3, iy = ((int)align - 1) / 3;
                tx = (w - v.width * s) * ix * 0.5f;
                ty = (h - v.height * s) * iy * 0.5f;
            }

            return Affine2.Translate(new float2(tx, ty)) * Affine2.Scale(new float2(sx, sy)) * Affine2.Translate(new float2(-v.x, -v.y));
        }

        static SvgPatternState.Tile ReadTile(SvgNode p, SvgNode content, Affine2 c, float tw, float th, int pw, int ph, Context ctx)
        {
            var st = ctx.patterns;
            var saved = ctx.doc.elements;
            var list = new List<SvgElementData>();
            ctx.doc.elements = list;
            st.active.Add(p);
            st.capture++;
            try
            {
                foreach (var child in content.children) Render(child, c, 1f, null, null, ctx);
            }
            finally
            {
                st.capture--;
                st.active.Remove(p);
                ctx.doc.elements = saved;
            }

            if (list.RemoveAll(e => e.isText) > 0) ctx.Warn("SVG: text inside a pattern is not drawn.");
            list.RemoveAll(e => e.isImage);
            if (list.Count == 0) return new SvgPatternState.Tile { index = -1 };
            if (list.Count == 1 && IsWholeTileImage(list[0], ctx.doc.patterns, tw, th))
                return new SvgPatternState.Tile { index = list[0].fill.textureSource, direct = true, opacity = list[0].fill.opacity };

            ctx.doc.patterns.Add(new SvgPatternData { tileSize = new Vector2(tw, th), pixelWidth = pw, pixelHeight = ph, elements = list });
            return new SvgPatternState.Tile { index = ctx.doc.patterns.Count - 1 };
        }

        // One image whose rectangle is the tile and whose texture coordinates run over the tile: its texture is the tile.
        static bool IsWholeTileImage(SvgElementData e, List<SvgPatternData> patterns, float tw, float th)
        {
            var f = e.fill;
            if (!e.hasFill || e.hasStroke || f == null || f.type != PaintType.Texture || !f.useCustomMapping) return false;
            if (f.textureSource < 0 || f.textureSource >= patterns.Count || !patterns[f.textureSource].IsImage) return false;
            if (e.clips.Count > 0 || e.effects.Count > 0 || e.path.points.Count != 4) return false;
            float tol = 1e-3f * Mathf.Max(tw, th) + 1e-5f;
            foreach (var pt in e.path.points)
            {
                if (Mathf.Abs(pt.x) > tol && Mathf.Abs(pt.x - tw) > tol) return false;
                if (Mathf.Abs(pt.y) > tol && Mathf.Abs(pt.y - th) > tol) return false;
            }

            float2 a = f.customMapping.Apply(new float2(0f, 0f)), b = f.customMapping.Apply(new float2(tw, th));
            return math.abs(a.x) < 1e-3f && math.abs(a.y - 1f) < 1e-3f && math.abs(b.x - 1f) < 1e-3f && math.abs(b.y) < 1e-3f &&
                   math.abs(f.customMapping.Apply(new float2(tw, 0f)).y - 1f) < 1e-3f;
        }

        static int ImageEntry(byte[] bytes, Context ctx)
        {
            unchecked
            {
                uint h = 2166136261;
                for (int i = 0; i < bytes.Length; i++) h = (h ^ bytes[i]) * 16777619;
                string key = bytes.Length.ToString(CultureInfo.InvariantCulture) + ":" + h;
                if (ctx.patterns.images.TryGetValue(key, out int index)) return index;
                ctx.doc.patterns.Add(new SvgPatternData { imageData = bytes });
                index = ctx.doc.patterns.Count - 1;
                ctx.patterns.images[key] = index;
                return index;
            }
        }

        /// <summary>An &lt;image&gt; inside a pattern: a rectangle of its visible part with the picture as a texture, so it can be fitted, cropped, rotated or skewed.</summary>
        static void CaptureImage(SvgNode n, Affine2 ctm, float opacity, List<SvgPathData> clips, List<VectraEffect> effects, Context ctx)
        {
            var href = n.Attr("href");
            if (string.IsNullOrEmpty(href)) return;
            if (Style(n, "visibility") == "hidden" || Style(n, "visibility") == "collapse") return;
            byte[] bytes = null;
            int comma = href.IndexOf(',');
            if (href.StartsWith("data:") && comma > 0 && href.Substring(0, comma).Contains("base64"))
            {
                try { bytes = Convert.FromBase64String(href.Substring(comma + 1)); }
                catch { ctx.Warn("Invalid base64 image data."); }
            }
            else ctx.Warn("External image references are not loaded: " + href);

            if (bytes == null || math.abs(ctm.Determinant) < 1e-12f) return;
            bool known = SvgPatterns.TryGetImageSize(bytes, out int iw, out int ih);
            float x = Length(n.Attr("x"), 0f, ctx.viewportW, ctx), y = Length(n.Attr("y"), 0f, ctx.viewportH, ctx);
            float w = Length(n.Attr("width"), 0f, ctx.viewportW, ctx), h = Length(n.Attr("height"), 0f, ctx.viewportH, ctx);
            if (known)
            {
                if (!(w > 0f) && !(h > 0f)) { w = iw; h = ih; }
                else if (!(w > 0f)) w = h * iw / ih;
                else if (!(h > 0f)) h = w * ih / iw;
            }

            if (!(w > 0f) || !(h > 0f)) return;

            // The picture's rectangle inside the viewport (x, y, w, h) per preserveAspectRatio.
            ParseAspect(n.Attr("preserveAspectRatio"), out var align, out bool slice);
            float cw = w, ch = h, cx = x, cy = y;
            if (known && align != SvgAlign.None)
            {
                float s = slice ? Mathf.Max(w / iw, h / ih) : Mathf.Min(w / iw, h / ih);
                cw = iw * s;
                ch = ih * s;
                int ix = ((int)align - 1) % 3, iy = ((int)align - 1) / 3;
                cx = x + (w - cw) * ix * 0.5f;
                cy = y + (h - ch) * iy * 0.5f;
            }

            // Only the part inside the viewport is visible (slice crops the picture).
            float x0 = Mathf.Max(x, cx), y0 = Mathf.Max(y, cy), x1 = Mathf.Min(x + w, cx + cw), y1 = Mathf.Min(y + h, cy + ch);
            if (!(x1 > x0) || !(y1 > y0)) return;

            var e = new SvgElementData { tag = "image", id = n.Attr("id"), className = n.Attr("class") };
            e.path.verbs.Add((byte)PathVerb.MoveTo);
            e.path.verbs.Add((byte)PathVerb.LineTo);
            e.path.verbs.Add((byte)PathVerb.LineTo);
            e.path.verbs.Add((byte)PathVerb.LineTo);
            e.path.verbs.Add((byte)PathVerb.Close);
            e.path.points.Add(ctm.Apply(new float2(x0, y0)));
            e.path.points.Add(ctm.Apply(new float2(x1, y0)));
            e.path.points.Add(ctm.Apply(new float2(x1, y1)));
            e.path.points.Add(ctm.Apply(new float2(x0, y1)));
            e.fill = new VectraPaint
            {
                type = PaintType.Texture,
                textureFit = TextureFit.Stretch,
                useCustomMapping = true,
                textureSource = ImageEntry(bytes, ctx),
                opacity = Mathf.Clamp01(opacity),
                customMapping = s_FlipV * Affine2.Scale(new float2(1f / cw, 1f / ch)) * Affine2.Translate(new float2(-cx, -cy)) * ctm.Inverse()
            };
            e.hasFill = e.fill.opacity > 0f;
            if (!e.hasFill) return;
            if (clips != null) e.clips.AddRange(clips);
            if (effects != null)
                foreach (var fx in effects) e.effects.Add(fx.Clone());
            ctx.doc.elements.Add(e);
        }

        /// <summary>Adds an element; a fill and a stroke with different pattern textures (a mesh has one) become two elements, fill below stroke.</summary>
        static void AddElement(Context ctx, SvgElementData e)
        {
            var f = e.hasFill ? e.fill : null;
            var s = e.hasStroke ? e.stroke?.paint : null;
            if (f != null && s != null && f.type == PaintType.Texture && s.type == PaintType.Texture && f.textureSource >= 0 &&
                s.textureSource >= 0 && f.textureSource != s.textureSource)
            {
                if (e.effects.Count == 0)
                {
                    var strokeOnly = new SvgElementData
                    {
                        tag = e.tag, id = e.id, className = e.className, path = e.path, hasStroke = true, stroke = e.stroke, hasFill = false
                    };
                    strokeOnly.clips.AddRange(e.clips);
                    e.hasStroke = false;
                    e.stroke = null;
                    ctx.doc.elements.Add(e);
                    ctx.doc.elements.Add(strokeOnly);
                    return;
                }

                ctx.Warn("SVG: a fill and a stroke with different patterns and an effect on one element: the stroke is drawn grey.");
                s.type = PaintType.Solid;
                s.color = Color.gray;
            }

            ctx.doc.elements.Add(e);
        }
    }
}
