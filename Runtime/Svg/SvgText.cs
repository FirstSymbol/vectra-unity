using System;
using System.Collections.Generic;
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using FirstSymbol.Vectra.Geometry;

namespace FirstSymbol.Vectra.Svg
{
    // SVG text layout. Vectra does not measure glyphs here, so the text is cut into "chunks": pieces that start at an absolute
    // position (SVG calls them text chunks too). Each chunk becomes one SvgElementData at that position, whatever text engine
    // draws it, and carries its style runs (tspans with their own look, letter spacing, decoration).
    public static partial class SvgParser
    {
        // A text with per-glyph x or y lists makes a chunk per glyph; stop before it makes thousands of text objects.
        const int MaxTextChunks = 512;

        sealed class TextChunk
        {
            public float x, y;
            public SvgTextAnchor anchor;
            public readonly List<SvgTextRun> runs = new List<SvgTextRun>();
        }

        // The x, y, dx and dy lists of one text-content element; they apply to the characters from `start` on.
        sealed class TextFrame
        {
            public int start;
            public float[] x, y, dx, dy;
        }

        sealed class TextBuild
        {
            public Context ctx;
            public float scale, opacity;
            public readonly List<TextChunk> chunks = new List<TextChunk>();
            public TextChunk chunk;
            public SvgTextRun open;
            public readonly StringBuilder sb = new StringBuilder();
            public readonly List<TextFrame> frames = new List<TextFrame>();
            public int index;
            public float curX, curY, shiftY;
            public bool lastSpace, any, lastPreserve, full;
        }

        static void RenderText(SvgNode n, Affine2 ctm, float opacity, Context ctx)
        {
            var b = new TextBuild { ctx = ctx, scale = ctm.UniformScale, opacity = opacity };
            WalkText(n, b, false, 0, 0);
            FlushRun(b);
            FinishChunks(b);
            float rotation = math.degrees(math.atan2(ctm.b, ctm.a));
            foreach (var c in b.chunks)
            {
                if (c.runs.Count == 0) continue;
                var text = new StringBuilder();
                foreach (var r in c.runs) text.Append(r.text);
                var first = c.runs[0];
                ctx.doc.elements.Add(new SvgElementData
                {
                    tag = "text",
                    id = n.Attr("id"),
                    className = n.Attr("class"),
                    isText = true,
                    text = text.ToString(),
                    runs = c.runs,
                    textPosition = ctm.Apply(new float2(c.x, c.y)),
                    fontSize = first.fontSize,
                    fontFamily = first.fontFamily,
                    bold = first.bold,
                    italic = first.italic,
                    anchor = c.anchor,
                    textColor = first.color,
                    textRotation = rotation
                });
            }
        }

        static void WalkText(SvgNode n, TextBuild b, bool preserve, int decoration, int depth)
        {
            if (depth > 16 || b.full) return;
            if (depth > 0 && Style(n, "display") == "none") return;
            if (n.name == "textPath") b.ctx.Warn("SVG: <textPath> is drawn as ordinary text; the text does not follow the path.");
            if (n.attributes.ContainsKey("rotate")) b.ctx.Warn("SVG: per-glyph rotate on text is not supported and is ignored.");

            var space = Style(n, "white-space");
            if (space != null) preserve = space.StartsWith("pre", StringComparison.Ordinal) || space == "break-spaces";
            else
            {
                var xmlSpace = n.Attr("xml:space");
                if (xmlSpace == "preserve") preserve = true;
                else if (xmlSpace == "default") preserve = false;
            }

            decoration |= TextDecorationOf(n, b.ctx);
            var style = TextStyle(n, b, decoration, out float fontUser);
            var anchorValue = Style(n, "text-anchor", "start");
            var anchor = anchorValue == "middle" ? SvgTextAnchor.Middle : anchorValue == "end" ? SvgTextAnchor.End : SvgTextAnchor.Start;

            TextFrame frame = null;
            if (n.attributes.ContainsKey("x") || n.attributes.ContainsKey("y") || n.attributes.ContainsKey("dx") || n.attributes.ContainsKey("dy"))
            {
                var f = new TextFrame
                {
                    start = b.index,
                    x = TextList(n.Attr("x"), fontUser, b.ctx.viewportW, b.ctx),
                    y = TextList(n.Attr("y"), fontUser, b.ctx.viewportH, b.ctx),
                    dx = TextList(n.Attr("dx"), fontUser, b.ctx.viewportW, b.ctx),
                    dy = TextList(n.Attr("dy"), fontUser, b.ctx.viewportH, b.ctx)
                };
                if (f.x != null || f.y != null || f.dx != null || f.dy != null)
                {
                    frame = f;
                    b.frames.Add(frame);
                }
            }

            // Character data and child elements in document order (mixed content).
            var parts = n.textParts;
            int p = 0;
            for (int k = 0; k <= n.children.Count && !b.full; k++)
            {
                while (parts != null && p < parts.Count && parts[p].Key <= k) AddText(b, style, anchor, preserve, parts[p++].Value);
                if (k == n.children.Count) break;
                var c = n.children[k];
                if (c.name == "tspan" || c.name == "textPath" || c.name == "a") WalkText(c, b, preserve, decoration, depth + 1);
            }

            if (parts == null && !string.IsNullOrEmpty(n.text)) AddText(b, style, anchor, preserve, n.text);
            if (frame != null) b.frames.Remove(frame);
        }

        // The style a piece of text draws with: that of the element holding it.
        static SvgTextRun TextStyle(SvgNode n, TextBuild b, int decoration, out float fontUser)
        {
            var fillValue = Style(n, "fill", "black");
            var color = fillValue == "none" ? Color.clear : ColorOf(fillValue.StartsWith("url") ? "black" : fillValue, n);
            color.a *= Number(Style(n, "fill-opacity"), 1f) * b.opacity;
            string weight = Style(n, "font-weight", "normal");
            string fontStyle = Style(n, "font-style");
            fontUser = Length(Style(n, "font-size", "16"), 16, b.ctx.viewportH, b.ctx);
            return new SvgTextRun
            {
                color = color,
                fontSize = fontUser * b.scale,
                fontFamily = Style(n, "font-family", ""),
                bold = weight == "bold" || weight == "bolder" || (int.TryParse(weight, out int wv) && wv >= 600),
                italic = fontStyle == "italic" || fontStyle == "oblique",
                letterSpacing = TextLength(Style(n, "letter-spacing"), fontUser, b.ctx.viewportW, b.ctx) * b.scale,
                underline = (decoration & 1) != 0,
                lineThrough = (decoration & 2) != 0
            };
        }

        // text-decoration is not inherited, but a decoration set on a parent is drawn across its children, so the walk adds them up.
        static int TextDecorationOf(SvgNode n, Context ctx)
        {
            var v = Style(n, "text-decoration-line") ?? Style(n, "text-decoration");
            if (string.IsNullOrEmpty(v) || v == "none") return 0;
            int flags = 0;
            if (v.IndexOf("underline", StringComparison.Ordinal) >= 0) flags |= 1;
            if (v.IndexOf("line-through", StringComparison.Ordinal) >= 0) flags |= 2;
            if (v.IndexOf("overline", StringComparison.Ordinal) >= 0) ctx.Warn("SVG: text-decoration overline is not supported and is ignored.");
            return flags;
        }

        // A length where em and ex follow the font size of the element (Length() has no font to refer to).
        static float TextLength(string v, float fontUser, float percentOf, Context ctx)
        {
            if (string.IsNullOrEmpty(v) || v == "normal") return 0f;
            v = v.Trim();
            if (v.EndsWith("em", StringComparison.Ordinal) && !v.EndsWith("rem", StringComparison.Ordinal))
                return Number(v.Substring(0, v.Length - 2), 0f) * fontUser;
            if (v.EndsWith("ex", StringComparison.Ordinal)) return Number(v.Substring(0, v.Length - 2), 0f) * fontUser * 0.5f;
            return Length(v, 0f, percentOf, ctx);
        }

        static float[] TextList(string v, float fontUser, float percentOf, Context ctx)
        {
            if (string.IsNullOrEmpty(v)) return null;
            var parts = SplitList(v);
            if (parts.Count == 0) return null;
            var r = new float[parts.Count];
            for (int i = 0; i < r.Length; i++) r[i] = TextLength(parts[i], fontUser, percentOf, ctx);
            return r;
        }

        static void AddText(TextBuild b, SvgTextRun style, SvgTextAnchor anchor, bool preserve, string raw)
        {
            b.lastPreserve = preserve;
            for (int i = 0; i < raw.Length && !b.full; i++)
            {
                char ch = raw[i];
                if (ch == ' ' || ch == '\n' || ch == '\r' || ch == '\t')
                {
                    if (!preserve)
                    {
                        // Default spacing: runs of white space collapse to one space, and none starts the text.
                        if (!b.any || b.lastSpace) continue;
                        ch = ' ';
                    }
                    else if (ch == '\t') ch = ' ';

                    b.lastSpace = true;
                }
                else b.lastSpace = false;

                int index = b.index++;
                bool hasX = false, hasY = false;
                float x = 0f, y = 0f, dx = 0f, dy = 0f;
                if (b.frames.Count > 0) LookupPosition(b, index, ref hasX, ref x, ref hasY, ref y, ref dx, ref dy);

                if (hasX || hasY || b.chunk == null)
                {
                    FlushRun(b);
                    if (b.chunks.Count >= MaxTextChunks)
                    {
                        b.full = true;
                        b.ctx.Warn($"SVG: a text with more than {MaxTextChunks} separately positioned pieces is cut off.");
                        return;
                    }

                    if (hasY && !hasX && b.chunk != null)
                        b.ctx.Warn("SVG: a text position with y but no x continues from the start of the previous piece; Vectra does not measure glyphs.");
                    if (hasX) b.curX = x;
                    if (hasY) b.curY = y;
                    b.curX += dx;
                    b.curY += dy;
                    b.chunk = new TextChunk { x = b.curX, y = b.curY, anchor = anchor };
                    b.chunks.Add(b.chunk);
                    b.shiftY = 0f;
                    dx = 0f;
                }
                else if (dy != 0f)
                {
                    FlushRun(b);
                    b.shiftY += dy;
                }

                if (b.open == null || dx != 0f || !SameTextStyle(b.open, style) || !Mathf.Approximately(b.open.dy, b.shiftY))
                {
                    FlushRun(b);
                    b.open = new SvgTextRun
                    {
                        bold = style.bold, italic = style.italic, color = style.color, fontSize = style.fontSize,
                        fontFamily = style.fontFamily, letterSpacing = style.letterSpacing, underline = style.underline,
                        lineThrough = style.lineThrough, dx = dx, dy = b.shiftY
                    };
                }

                b.sb.Append(ch);
                b.any = true;
            }
        }

        // The innermost element that lists a value for this character wins.
        static void LookupPosition(TextBuild b, int index, ref bool hasX, ref float x, ref bool hasY, ref float y, ref float dx, ref float dy)
        {
            bool gotDx = false, gotDy = false;
            for (int f = b.frames.Count - 1; f >= 0; f--)
            {
                var fr = b.frames[f];
                int k = index - fr.start;
                if (k < 0) continue;
                if (!hasX && fr.x != null && k < fr.x.Length) { hasX = true; x = fr.x[k]; }
                if (!hasY && fr.y != null && k < fr.y.Length) { hasY = true; y = fr.y[k]; }
                if (!gotDx && fr.dx != null && k < fr.dx.Length) { gotDx = true; dx = fr.dx[k]; }
                if (!gotDy && fr.dy != null && k < fr.dy.Length) { gotDy = true; dy = fr.dy[k]; }
            }
        }

        static bool SameTextStyle(SvgTextRun a, SvgTextRun b) =>
            a.bold == b.bold && a.italic == b.italic && a.color == b.color && a.fontFamily == b.fontFamily &&
            a.underline == b.underline && a.lineThrough == b.lineThrough &&
            Mathf.Approximately(a.fontSize, b.fontSize) && Mathf.Approximately(a.letterSpacing, b.letterSpacing);

        static void FlushRun(TextBuild b)
        {
            if (b.open == null) return;
            b.open.text = b.sb.ToString();
            b.sb.Clear();
            b.chunk.runs.Add(b.open);
            b.open = null;
        }

        // Cleans the runs of every chunk: a line break at the end of a piece (Figma ends each paragraph's pieces with one) draws
        // nothing and moves nothing; one inside a piece is the space XML makes of it.
        static void FinishChunks(TextBuild b)
        {
            if (!b.lastPreserve && b.chunk != null) TrimEnd(b.chunk.runs, ' ');
            foreach (var c in b.chunks)
            {
                TrimEnd(c.runs, '\n', '\r');
                foreach (var r in c.runs) r.text = r.text.Replace('\n', ' ').Replace('\r', ' ');
                bool visible = false;
                foreach (var r in c.runs)
                    if (r.text.Trim().Length > 0) { visible = true; break; }
                if (!visible) c.runs.Clear();
            }
        }

        static void TrimEnd(List<SvgTextRun> runs, params char[] chars)
        {
            while (runs.Count > 0)
            {
                var last = runs[runs.Count - 1];
                last.text = last.text.TrimEnd(chars);
                if (last.text.Length > 0) return;
                runs.RemoveAt(runs.Count - 1);
            }
        }
    }
}
