using System;
using System.Collections.Generic;
using FirstSymbol.Vectra.Rendering;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FirstSymbol.Vectra.Svg
{
    /// <summary>
    /// What a pattern paint draws, stored in <see cref="VectraSvgDocument.patterns"/>: either an image (PNG or JPEG bytes, shown
    /// as it is) or a tile, the pattern content as SVG elements in tile coordinates (drawn into a texture the first time it is used).
    /// Bytes and elements serialize with the document; textures never do.
    /// </summary>
    [Serializable]
    public class SvgPatternData
    {
        /// <summary>PNG or JPEG file; when set, this entry is an image and the fields below are unused.</summary>
        public byte[] imageData;
        /// <summary>Tile size in tile units (the units of <see cref="elements"/>).</summary>
        public Vector2 tileSize;
        /// <summary>Size of the texture the tile is drawn into.</summary>
        public int pixelWidth, pixelHeight;
        /// <summary>Tile content with the origin at the tile's top-left corner, y down.</summary>
        public List<SvgElementData> elements = new List<SvgElementData>();

        public bool IsImage => imageData != null && imageData.Length > 0;
    }

    /// <summary>
    /// Makes and frees the textures of SVG pattern paints. A document makes each texture once, on the first mesh build that needs it,
    /// and destroys it with the document; nothing is created per frame.
    /// </summary>
    public static class SvgPatterns
    {
        /// <summary>Tile pixels per tile unit when a tile is drawn into a texture.</summary>
        public const float PixelsPerUnit = 4f;
        /// <summary>Longest side of a tile texture, in pixels.</summary>
        public const int MaxTileSide = 1024;
        const int MaxNesting = 6;

        /// <summary>Non-zero identity of the pattern texture an element needs (fill or stroke); 0 when it needs none. Used to start a new mesh when it changes.</summary>
        internal static int TextureKey(SvgElementData e)
        {
            var f = e.hasFill ? e.fill : null;
            if (f != null && f.type == PaintType.Texture && f.textureSource >= 0) return f.textureSource + 1;
            var s = e.hasStroke ? e.stroke?.paint : null;
            if (s != null && s.type == PaintType.Texture && s.textureSource >= 0) return s.textureSource + 1;
            return 0;
        }

        /// <summary>Makes the textures the elements in [from, to) need. Call before <c>BeginBatch</c>: drawing a tile builds meshes of its own.</summary>
        internal static void Prepare(VectraSvgDocument doc, int from, int to, List<SvgElementData> source = null)
        {
            var list = source ?? doc.elements;
            if (list == null) return;
            to = Mathf.Min(to, list.Count);
            for (int i = Mathf.Max(from, 0); i < to; i++)
            {
                var e = list[i];
                if (e == null) continue;
                if (e.hasFill) Fill(doc, e.fill);
                if (e.hasStroke && e.stroke != null) Fill(doc, e.stroke.paint);
            }
        }

        static void Fill(VectraSvgDocument doc, VectraPaint p)
        {
            if (p == null || p.type != PaintType.Texture || p.textureSource < 0 || p.texture != null) return;
            p.texture = Resolve(doc, p.textureSource);
        }

        /// <summary>The texture of pattern <paramref name="index"/>, made when needed; null when it cannot be made.</summary>
        public static Texture2D Resolve(VectraSvgDocument doc, int index)
        {
            var owner = doc.patternOwner != null ? doc.patternOwner : doc;
            var list = owner.patterns;
            if (list == null || index < 0 || index >= list.Count || list[index] == null) return null;
            if (owner.patternTextures == null || owner.patternTextures.Length != list.Count)
            {
                DestroyAll(owner.patternTextures);
                owner.patternTextures = new Texture2D[list.Count];
            }

            var t = owner.patternTextures[index];
            if (t != null) return t;
            if (owner.patternDepth >= MaxNesting) return null;
            owner.patternDepth++;
            try
            {
                var data = list[index];
                t = data.IsImage ? FromImage(data.imageData, index) : FromTile(owner, data, index);
            }
            finally
            {
                owner.patternDepth--;
            }

            owner.patternTextures[index] = t;
            return t;
        }

        /// <summary>Empties the pattern table of a document that is about to be parsed again.</summary>
        internal static void Reset(VectraSvgDocument doc)
        {
            Release(doc);
            if (doc.patterns == null) doc.patterns = new List<SvgPatternData>();
            else doc.patterns.Clear();
            doc.patternOwner = null;
        }

        /// <summary>Destroys the textures made for <paramref name="doc"/>; they are made again if the document is drawn again.</summary>
        internal static void Release(VectraSvgDocument doc)
        {
            if (doc == null) return;
            DestroyAll(doc.patternTextures);
            doc.patternTextures = null;
        }

        static void DestroyAll(Texture2D[] textures)
        {
            if (textures == null) return;
            for (int i = 0; i < textures.Length; i++)
            {
                if (textures[i] == null) continue;
                if (Application.isPlaying) Object.Destroy(textures[i]);
                else Object.DestroyImmediate(textures[i]);
                textures[i] = null;
            }
        }

        /// <summary>Largest side and pixel count of an embedded picture that is decoded (a PNG or JPEG header is read first).</summary>
        public const int MaxImageSide = 4096, MaxImagePixels = 16 * 1024 * 1024;

        /// <summary>True when the PNG or JPEG header says the picture is bigger than the limits (unknown formats pass; the decoder decides).</summary>
        public static bool ImageTooLarge(byte[] b)
        {
            if (b == null) return false;
            long w = 0, h = 0;
            if (b.Length > 24 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47)
            {
                w = ((long)b[16] << 24) | ((long)b[17] << 16) | ((long)b[18] << 8) | b[19];
                h = ((long)b[20] << 24) | ((long)b[21] << 16) | ((long)b[22] << 8) | b[23];
            }
            else if (b.Length > 4 && b[0] == 0xFF && b[1] == 0xD8)
            {
                int i = 2;
                while (i + 9 < b.Length)
                {
                    if (b[i] != 0xFF) { i++; continue; }
                    int m = b[i + 1];
                    if (m == 0xFF) { i++; continue; }
                    if (m >= 0xC0 && m <= 0xCF && m != 0xC4 && m != 0xC8 && m != 0xCC)
                    {
                        h = (b[i + 5] << 8) | b[i + 6];
                        w = (b[i + 7] << 8) | b[i + 8];
                        break;
                    }

                    if (m == 0xD8 || (m >= 0xD0 && m <= 0xD7) || m == 0x01) { i += 2; continue; }
                    i += 2 + ((b[i + 2] << 8) | b[i + 3]);
                }
            }

            return w > MaxImageSide || h > MaxImageSide || w * h > MaxImagePixels;
        }

        static Texture2D FromImage(byte[] bytes, int index)
        {
            if (ImageTooLarge(bytes)) return null;
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, true, false) { hideFlags = HideFlags.HideAndDontSave };
            if (!t.LoadImage(bytes, false))
            {
                Destroy(t);
                return null;
            }

            return Finish(t, $"Vectra pattern image {index}", true);
        }

        static Texture2D FromTile(VectraSvgDocument owner, SvgPatternData data, int index)
        {
            int w = Mathf.Clamp(data.pixelWidth, 1, 4096), h = Mathf.Clamp(data.pixelHeight, 1, 4096);
            var tile = ScriptableObject.CreateInstance<VectraSvgDocument>();
            tile.hideFlags = HideFlags.HideAndDontSave;
            tile.name = "Vectra pattern tile";
            tile.viewBox = new Rect(0f, 0f, Mathf.Max(data.tileSize.x, 1e-4f), Mathf.Max(data.tileSize.y, 1e-4f));
            tile.size = tile.viewBox.size;
            tile.align = SvgAlign.None;
            tile.elements = data.elements;
            tile.patterns = owner.patterns;
            tile.patternOwner = owner;
            Texture2D raw;
            try
            {
                raw = VectraRasterizer.Render(tile, w, h);
            }
            finally
            {
                tile.elements = null; // the list belongs to the pattern data
                Destroy(tile);
            }

            return raw == null ? null : Finish(raw, $"Vectra pattern tile {index}", true, true);
        }

        // Straight-alpha textures need colour under their transparent texels, or bilinear filtering and mip levels pull black
        // into the edges (Chrome and Figma filter premultiplied). The result is clamped, filtered and has mips.
        static Texture2D Finish(Texture2D source, string name, bool mips, bool tile = false)
        {
            int w = source.width, h = source.height;
            var px = source.GetPixels32();
            bool bled = Bleed(px, w, h);
            if (tile) bled |= FixEdges(px, w, h);
            Texture2D result = source;
            if (bled || !source.isReadable || source.mipmapCount <= 1 && mips)
            {
                result = new Texture2D(w, h, TextureFormat.RGBA32, mips, false) { hideFlags = HideFlags.HideAndDontSave };
                result.SetPixels32(px);
                result.Apply(mips, true);
                Destroy(source);
            }

            result.name = name;
            result.wrapMode = TextureWrapMode.Clamp;
            result.filterMode = FilterMode.Bilinear;
            return result;
        }

        // Anti-aliasing leaves the outermost pixel row and column of a tile about 10% transparent where a shape reaches the tile
        // edge; repeated, that shows as a grid of faint lines. Where the border pixel is more transparent than the one inside
        // it, it takes the inner pixel (a shape that reaches the edge would fill the pixel in a pixel-aligned renderer).
        internal static bool FixEdges(Color32[] px, int w, int h)
        {
            if (w < 3 || h < 3) return false;
            bool changed = false;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (x != 0 && y != 0 && x != w - 1 && y != h - 1) continue;
                int ix = x == 0 ? 1 : x == w - 1 ? w - 2 : x, iy = y == 0 ? 1 : y == h - 1 ? h - 2 : y;
                var inner = px[iy * w + ix];
                if (inner.a <= px[y * w + x].a) continue;
                px[y * w + x] = inner;
                changed = true;
            }

            return changed;
        }

        /// <summary>Copies colour into fully transparent texels that touch painted ones. True when something changed.</summary>
        internal static bool Bleed(Color32[] px, int w, int h)
        {
            bool anyOpaque = false, anyClear = false;
            for (int i = 0; i < px.Length; i++)
            {
                if (px[i].a == 0) anyClear = true;
                else anyOpaque = true;
            }

            if (!anyOpaque || !anyClear) return false;
            // done[i]: the texel has a colour worth copying (painted, or filled by an earlier pass).
            var done = new bool[px.Length];
            for (int i = 0; i < px.Length; i++) done[i] = px[i].a != 0;
            var fill = new List<int>();
            bool changed = false;
            for (int pass = 0; pass < 6; pass++)
            {
                fill.Clear();
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (done[i]) continue;
                    if (x > 0 && done[i - 1] || x + 1 < w && done[i + 1] || y > 0 && done[i - w] || y + 1 < h && done[i + w]) fill.Add(i);
                }

                if (fill.Count == 0) break;
                // Colours are read from the texels finished before this pass; the new ones are marked afterwards.
                var colors = new Color32[fill.Count];
                for (int k = 0; k < fill.Count; k++)
                {
                    int i = fill[k], x = i % w, y = i / w, r = 0, g = 0, b = 0, n = 0;
                    if (x > 0 && done[i - 1]) { Add(px[i - 1], ref r, ref g, ref b, ref n); }
                    if (x + 1 < w && done[i + 1]) { Add(px[i + 1], ref r, ref g, ref b, ref n); }
                    if (y > 0 && done[i - w]) { Add(px[i - w], ref r, ref g, ref b, ref n); }
                    if (y + 1 < h && done[i + w]) { Add(px[i + w], ref r, ref g, ref b, ref n); }
                    colors[k] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 0);
                }

                for (int k = 0; k < fill.Count; k++)
                {
                    px[fill[k]] = colors[k];
                    done[fill[k]] = true;
                }

                changed = true;
            }

            return changed;
        }

        static void Add(Color32 c, ref int r, ref int g, ref int b, ref int n)
        {
            r += c.r;
            g += c.g;
            b += c.b;
            n++;
        }

        static void Destroy(Object o)
        {
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }

        /// <summary>Pixel size of a PNG or JPEG without decoding it; false for other data.</summary>
        public static bool TryGetImageSize(byte[] b, out int width, out int height)
        {
            width = height = 0;
            if (b == null) return false;
            if (b.Length > 24 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47)
            {
                width = b[16] << 24 | b[17] << 16 | b[18] << 8 | b[19];
                height = b[20] << 24 | b[21] << 16 | b[22] << 8 | b[23];
                return width > 0 && height > 0;
            }

            if (b.Length > 4 && b[0] == 0xFF && b[1] == 0xD8)
            {
                int i = 2;
                while (i + 9 < b.Length)
                {
                    if (b[i] != 0xFF) { i++; continue; }
                    int marker = b[i + 1];
                    if (marker == 0xFF) { i++; continue; }
                    if (marker == 0xD8 || marker >= 0xD0 && marker <= 0xD7 || marker == 0x01) { i += 2; continue; }
                    int len = b[i + 2] << 8 | b[i + 3];
                    if (marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
                    {
                        height = b[i + 5] << 8 | b[i + 6];
                        width = b[i + 7] << 8 | b[i + 8];
                        return width > 0 && height > 0;
                    }

                    if (len < 2) return false;
                    i += 2 + len;
                }
            }

            return false;
        }
    }
}
