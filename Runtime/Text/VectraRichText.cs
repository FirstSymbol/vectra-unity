using System.Collections.Generic;
using System.Globalization;
using System.Text;
using FirstSymbol.Vectra.Svg;
using UnityEngine;

namespace FirstSymbol.Vectra.TextEngines
{
    /// <summary>The markup dialect of a text engine, for turning SVG text runs into one marked-up string.</summary>
    public enum VectraMarkupDialect
    {
        /// <summary>TextMesh Pro rich text: &lt;b&gt;, &lt;i&gt;, &lt;color=#RRGGBBAA&gt;, &lt;size=150%&gt;; &lt;noparse&gt; protects a literal "&lt;".</summary>
        TextMeshPro,
        /// <summary>uGUI Text rich text: sizes in whole pixels, no escape (a zero-width space breaks a literal "&lt;").</summary>
        Legacy,
        /// <summary>Lettera markup: the same tags as TextMesh Pro; a backslash protects markup characters.</summary>
        Lettera
    }

    /// <summary>Builds the marked-up string of an SVG text element from its runs, so tspan styling survives.</summary>
    public static class VectraRichText
    {
        /// <summary>
        /// The runs of an element: its own list, or one run made from the flat text of a document saved by an older version.
        /// </summary>
        public static IReadOnlyList<SvgTextRun> RunsOf(SvgElementData e)
        {
            if (e.runs != null && e.runs.Count > 0) return e.runs;
            return new[]
            {
                new SvgTextRun { text = e.text ?? "", bold = e.bold, italic = e.italic, color = e.textColor, fontSize = e.fontSize, fontFamily = e.fontFamily }
            };
        }

        /// <summary>
        /// Marks up the runs. What the element itself already is (bold, italic, colour, size) is not repeated per run:
        /// the component carries it. <paramref name="tint"/> multiplies the colour of runs that differ from the element.
        /// </summary>
        /// <param name="scale">Pixels per document unit; only the legacy dialect needs it, for absolute sizes.</param>
        /// <param name="baseBold">Whether the whole text already draws bold (font style or a bold face), so a bold run needs no tag.</param>
        /// <param name="baseItalic">The same for italic.</param>
        public static string Build(SvgElementData e, VectraMarkupDialect dialect, Color tint, float scale, bool baseBold, bool baseItalic)
        {
            var runs = RunsOf(e);
            var sb = new StringBuilder(e.text != null ? e.text.Length + 16 : 16);
            foreach (var r in runs)
            {
                if (string.IsNullOrEmpty(r.text)) continue;
                bool bold = r.bold && !baseBold;
                bool italic = r.italic && !baseItalic;
                bool colored = !SameColor(r.color, e.textColor);
                float ratio = e.fontSize > 1e-4f ? r.fontSize / e.fontSize : 1f;
                bool sized = r.fontSize > 0f && Mathf.Abs(ratio - 1f) > 0.005f;
                if (bold) sb.Append("<b>");
                if (italic) sb.Append("<i>");
                if (colored) sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGBA(r.color * tint)).Append('>');
                if (sized)
                {
                    if (dialect == VectraMarkupDialect.Legacy)
                        sb.Append("<size=").Append(Mathf.Max(1, Mathf.RoundToInt(r.fontSize * scale)).ToString(CultureInfo.InvariantCulture)).Append('>');
                    else
                        sb.Append("<size=").Append((ratio * 100f).ToString("0.##", CultureInfo.InvariantCulture)).Append("%>");
                }

                AppendLiteral(sb, r.text, dialect);
                if (sized) sb.Append("</size>");
                if (colored) sb.Append("</color>");
                if (italic) sb.Append("</i>");
                if (bold) sb.Append("</b>");
            }

            return sb.ToString();
        }

        /// <summary>True when the text has more than one style, so markup is worth the cost.</summary>
        public static bool HasStyledRuns(SvgElementData e)
        {
            if (e.runs == null || e.runs.Count < 2) return false;
            foreach (var r in e.runs)
                if (r.bold != e.bold || r.italic != e.italic || !SameColor(r.color, e.textColor) || Mathf.Abs(r.fontSize - e.fontSize) > 0.005f * e.fontSize)
                    return true;
            return false;
        }

        static bool SameColor(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) < 0.002f && Mathf.Abs(a.g - b.g) < 0.002f && Mathf.Abs(a.b - b.b) < 0.002f && Mathf.Abs(a.a - b.a) < 0.002f;

        static void AppendLiteral(StringBuilder sb, string text, VectraMarkupDialect dialect)
        {
            switch (dialect)
            {
                case VectraMarkupDialect.Lettera:
                    foreach (char c in text)
                    {
                        if (c == '<' || c == '\\' || c == '*' || c == '_' || c == '`' || c == '[' || c == '~') sb.Append('\\');
                        sb.Append(c);
                    }

                    break;
                case VectraMarkupDialect.Legacy:
                    foreach (char c in text)
                    {
                        sb.Append(c);
                        if (c == '<') sb.Append('​');
                    }

                    break;
                default:
                    if (text.IndexOf('<') < 0) sb.Append(text);
                    else sb.Append("<noparse>").Append(text.Replace("</noparse>", "</ noparse>")).Append("</noparse>");
                    break;
            }
        }
    }
}
