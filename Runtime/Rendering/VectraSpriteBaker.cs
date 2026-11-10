using System;
using FirstSymbol.Vectra.Svg;
using Unity.Mathematics;
using UnityEngine;

namespace FirstSymbol.Vectra.Rendering
{
    /// <summary>Settings for turning a Vectra shape or SVG document into a <see cref="Sprite"/>.</summary>
    [Serializable]
    public struct VectraSpriteOptions
    {
        /// <summary>Pixels of the longest side of the shape (the texture is a little larger when effects reach outside the shape).</summary>
        public int resolution;
        /// <summary>Sprite pixels per world unit. 0 makes the sprite as large in world units as the shape is in local units.</summary>
        public float pixelsPerUnit;
        /// <summary>Pivot relative to the shape rectangle (0.5, 0.5 = centre).</summary>
        public Vector2 pivot;
        /// <summary>9-slice border in texture pixels: x = left, y = bottom, z = right, w = top. Added to <see cref="autoBorder"/> when both are set.</summary>
        public Vector4 border;
        /// <summary>Takes the border from the corner radii of a rectangle shape so a frame can be stretched.</summary>
        public bool autoBorder;
        /// <summary>Unity cuts a mesh that hugs the opaque pixels (saves fill rate); off gives a plain quad.</summary>
        public bool tightMesh;
        public bool generateMipmaps;
        /// <summary>Grows the sprite to include shadows, glows and outside strokes. Off keeps exactly the shape rectangle.</summary>
        public bool includeOverflow;
        public FilterMode filterMode;
        public VectraQuality quality;

        public static VectraSpriteOptions Default => new VectraSpriteOptions
        {
            resolution = 256,
            pixelsPerUnit = 100f,
            pivot = new Vector2(0.5f, 0.5f),
            tightMesh = true,
            includeOverflow = true,
            filterMode = FilterMode.Bilinear,
            quality = VectraQuality.High
        };
    }

    /// <summary>
    /// Bakes Vectra content into sprites for <see cref="SpriteRenderer"/> and uGUI <c>Image</c>: the texture is drawn by the
    /// GPU with the same shaders as on screen, the mesh hugs the opaque area, the pivot and 9-slice border are set.
    /// Needs a graphics device (not available under <c>-nographics</c>). The caller owns the returned sprite and its texture.
    /// </summary>
    public static class VectraSpriteBaker
    {
        /// <summary>Bakes a shape. <paramref name="size"/> is the size of the shape rectangle in local units.</summary>
        public static Sprite Bake(VectraShapeContent content, Vector2 size, VectraSpriteOptions options)
        {
            if (content == null || size.x <= 0f || size.y <= 0f) return null;
            options = Normalize(options);
            // Without effects or outside strokes only the anti-aliasing fringe would grow the sprite: keep the exact rectangle.
            options.includeOverflow &= ReachesOutside(content);
            var rect = new Rect(-size.x * 0.5f, -size.y * 0.5f, size.x, size.y);
            float px = math.max(size.x, size.y) / options.resolution;
            using var renderer = new VectraShapeRenderer();
            renderer.Build(content.ToInput(new ShapeFrame { rect = rect, pixelSize = px }, Color.white, true));
            return Finish(renderer, rect, px, options, size, content.ActiveGeometry as RectangleShape);
        }

        public static Sprite Bake(VectraShape shape, VectraSpriteOptions options)
        {
            if (shape == null) return null;
            return Bake(shape.Content, shape.rectTransform.rect.size, options);
        }

        public static Sprite Bake(VectraRenderer shape, VectraSpriteOptions options)
        {
            if (shape == null) return null;
            return Bake(shape.Content, shape.Size, options);
        }

        /// <summary>Bakes an SVG document at its aspect ratio; the shape rectangle is the view box.</summary>
        public static Sprite Bake(VectraSvgDocument doc, VectraSpriteOptions options)
        {
            if (doc == null || doc.elements == null) return null;
            options = Normalize(options);
            float aspect = doc.AspectRatio;
            Vector2 size = aspect >= 1f ? new Vector2(options.resolution, options.resolution / aspect) : new Vector2(options.resolution * aspect, options.resolution);
            size = new Vector2(Mathf.Max(1f, Mathf.Round(size.x)), Mathf.Max(1f, Mathf.Round(size.y)));
            var rect = new Rect(0f, 0f, size.x, size.y);
            using var renderer = new VectraShapeRenderer();
            VectraSvgBuilder.Build(doc, renderer, rect, 1f, Color.white, true, options.quality);
            return Finish(renderer, rect, 1f, options, size, null, SvgLayers.HasLayers(doc) ? doc : null);
        }

        static bool ReachesOutside(VectraShapeContent content)
        {
            foreach (var e in content.ActiveEffects)
                if (e != null && e.enabled) return true;
            foreach (var s in content.ActiveStrokes)
                if (s != null && s.enabled && s.width > 0f && s.alignment != StrokeAlignment.Inside) return true;
            return false;
        }

        static VectraSpriteOptions Normalize(VectraSpriteOptions o)
        {
            o.resolution = Mathf.Clamp(o.resolution <= 0 ? 256 : o.resolution, 4, 4096);
            return o;
        }

        static Sprite Finish(VectraShapeRenderer renderer, Rect rect, float px, VectraSpriteOptions o, Vector2 size, RectangleShape frame, VectraSvgDocument layered = null)
        {
            // The view covers the shape and whatever reaches outside it (shadows, glows, outside strokes) in whole pixels.
            var view = rect;
            var b = renderer.buffer.bounds;
            if (o.includeOverflow && b.IsValid)
                view = Rect.MinMaxRect(Mathf.Min(rect.xMin, b.min.x), Mathf.Min(rect.yMin, b.min.y), Mathf.Max(rect.xMax, b.max.x), Mathf.Max(rect.yMax, b.max.y));
            int padL = Mathf.CeilToInt((rect.xMin - view.xMin) / px - 1e-3f);
            int padB = Mathf.CeilToInt((rect.yMin - view.yMin) / px - 1e-3f);
            int padR = Mathf.CeilToInt((view.xMax - rect.xMax) / px - 1e-3f);
            int padT = Mathf.CeilToInt((view.yMax - rect.yMax) / px - 1e-3f);
            int w = Mathf.Clamp(Mathf.RoundToInt(rect.width / px), 1, 8192);
            int h = Mathf.Clamp(Mathf.RoundToInt(rect.height / px), 1, 8192);
            int tw = Mathf.Clamp(w + padL + padR, 1, 8192), th = Mathf.Clamp(h + padB + padT, 1, 8192);
            // Map whole pixels to the area so the rectangle edges stay on texel edges.
            float sx = rect.width / w, sy = rect.height / h;
            var mapped = new Rect(rect.xMin - padL * sx, rect.yMin - padB * sy, tw * sx, th * sy);

            // Documents with layers are composited; the plain build above only measured the overflow.
            var raw = layered != null ? VectraRasterizer.RenderLayered(layered, rect, mapped, tw, th, o.quality) : VectraRasterizer.Render(renderer, mapped, tw, th);
            if (raw == null) return null;
            var pixels = raw.GetPixels32();
            UnityEngine.Object.DestroyImmediate(raw);
            // Keep the shape rectangle and whatever really drew outside it; cut the empty margin of the bounds estimate.
            int x0 = padL, y0 = padB, x1 = padL + w, y1 = padB + h;
            for (int y = 0; y < th; y++)
            for (int x = 0; x < tw; x++)
            {
                if (pixels[y * tw + x].a == 0) continue;
                if (x < x0) x0 = x;
                if (x + 1 > x1) x1 = x + 1;
                if (y < y0) y0 = y;
                if (y + 1 > y1) y1 = y + 1;
            }

            if (x0 > 0 || y0 > 0 || x1 < tw || y1 < th)
            {
                int nw = x1 - x0, nh = y1 - y0;
                var cropped = new Color32[nw * nh];
                for (int y = 0; y < nh; y++) Array.Copy(pixels, (y + y0) * tw + x0, cropped, y * nw, nw);
                pixels = cropped;
                padL -= x0;
                padB -= y0;
                padR = nw - w - padL;
                padT = nh - h - padB;
                tw = nw;
                th = nh;
            }

            BleedColor(pixels, tw, th);
            var tex = new Texture2D(tw, th, TextureFormat.RGBA32, o.generateMipmaps, false) { name = "Vectra Sprite", filterMode = o.filterMode, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(pixels);
            tex.Apply(o.generateMipmaps, false);

            var border = o.border;
            if (o.autoBorder && frame != null)
            {
                var r = frame.cornerRadii;
                float inv = 1f / px;
                border.x += padL + Mathf.Ceil(Mathf.Max(r.x, r.w) * inv);
                border.y += padB + Mathf.Ceil(Mathf.Max(r.z, r.w) * inv);
                border.z += padR + Mathf.Ceil(Mathf.Max(r.y, r.z) * inv);
                border.w += padT + Mathf.Ceil(Mathf.Max(r.x, r.y) * inv);
            }

            border = ClampBorder(border, tw, th);
            var pivot = new Vector2((padL + o.pivot.x * w) / tw, (padB + o.pivot.y * h) / th);
            float ppu = o.pixelsPerUnit > 0f ? o.pixelsPerUnit : Mathf.Max(w / Mathf.Max(size.x, 1e-4f), 1e-4f);
            var type = o.tightMesh && border == Vector4.zero ? SpriteMeshType.Tight : SpriteMeshType.FullRect;
            var sprite = Sprite.Create(tex, new Rect(0, 0, tw, th), pivot, ppu, 0, type, border, false);
            sprite.name = "Vectra Sprite";
            return sprite;
        }

        static Vector4 ClampBorder(Vector4 b, int w, int h)
        {
            b = new Vector4(Mathf.Max(0f, b.x), Mathf.Max(0f, b.y), Mathf.Max(0f, b.z), Mathf.Max(0f, b.w));
            if (b.x + b.z > w)
            {
                float k = w / (b.x + b.z);
                b.x = Mathf.Floor(b.x * k);
                b.z = Mathf.Floor(b.z * k);
            }

            if (b.y + b.w > h)
            {
                float k = h / (b.y + b.w);
                b.y = Mathf.Floor(b.y * k);
                b.w = Mathf.Floor(b.w * k);
            }

            return b;
        }

        // Transparent texels get the colour of their opaque neighbours so bilinear filtering does not pull in black.
        static void BleedColor(Color32[] px, int w, int h)
        {
            var next = new Color32[px.Length];
            for (int pass = 0; pass < 2; pass++)
            {
                Array.Copy(px, next, px.Length);
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (px[i].a != 0) continue;
                    int r = 0, g = 0, bl = 0, n = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int yy = y + dy;
                        if (yy < 0 || yy >= h) continue;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int xx = x + dx;
                            if (xx < 0 || xx >= w) continue;
                            var c = px[yy * w + xx];
                            if (c.a == 0 && (c.r | c.g | c.b) == 0) continue;
                            if (c.a == 0 && pass == 0) continue;
                            r += c.r;
                            g += c.g;
                            bl += c.b;
                            n++;
                        }
                    }

                    if (n > 0) next[i] = new Color32((byte)(r / n), (byte)(g / n), (byte)(bl / n), 0);
                }

                Array.Copy(next, px, px.Length);
            }
        }
    }
}
