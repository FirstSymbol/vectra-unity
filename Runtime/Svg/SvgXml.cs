using System;
using System.Collections.Generic;
using System.Text;

namespace FirstSymbol.Vectra.Svg
{
    /// <summary>Minimal XML element tree used by the SVG parser.</summary>
    public sealed class SvgNode
    {
        public string name;
        /// <summary>Namespace prefix as written (<c>sodipodi</c> in <c>sodipodi:namedview</c>), or null. The name is the part after it.</summary>
        public string prefix;
        public readonly Dictionary<string, string> attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly List<SvgNode> children = new List<SvgNode>();
        public SvgNode parent;
        /// <summary>Character data directly inside the element (text, CSS of &lt;style&gt;).</summary>
        public string text = "";
        internal StringBuilder textBuilder;

        /// <summary>
        /// Character data of text-bearing elements (text, tspan, a, textPath) with the number of child elements that came
        /// before each piece, so mixed content keeps its order.
        /// </summary>
        public List<KeyValuePair<int, string>> textParts;

        /// <summary>Computed style after CSS, presentation attributes and the style attribute were merged.</summary>
        public Dictionary<string, string> style;

        public string Attr(string key, string fallback = null) => attributes.TryGetValue(key, out var v) ? v : fallback;

        public override string ToString() => $"<{name}> ({children.Count} children)";
    }

    /// <summary>
    /// Small, allocation-light XML reader (no System.Xml dependency, which keeps WebGL/IL2CPP builds small).
    /// Supports elements, attributes, text, CDATA, comments, processing instructions, DOCTYPE and entities.
    /// </summary>
    public static class SvgXml
    {
        public static SvgNode Parse(string xml) => Parse(xml, null, null);

        /// <summary>Parses with budgets; a hit budget stops the parse, keeps the tree so far and adds a line to <paramref name="report"/>.</summary>
        public static SvgNode Parse(string xml, SvgParseOptions options, List<string> report)
        {
            if (string.IsNullOrEmpty(xml)) return null;
            options ??= new SvgParseOptions();
            var root = new SvgNode { name = "#document" };
            if (xml.Length > options.maxInputLength)
            {
                report?.Add($"SVG limit reached: the file has more than {options.maxInputLength} characters; nothing was imported.");
                return root;
            }

            var current = root;
            int i = 0, n = xml.Length;
            int depth = 0, count = 0;
            var text = new StringBuilder();

            while (i < n)
            {
                char c = xml[i];
                if (c != '<')
                {
                    int start = i;
                    while (i < n && xml[i] != '<') i++;
                    text.Append(Decode(xml, start, i - start));
                    continue;
                }

                if (Starts(xml, i, "<!--"))
                {
                    int end = xml.IndexOf("-->", i + 4, StringComparison.Ordinal);
                    i = end < 0 ? n : end + 3;
                    continue;
                }

                if (Starts(xml, i, "<![CDATA["))
                {
                    int end = xml.IndexOf("]]>", i + 9, StringComparison.Ordinal);
                    int stop = end < 0 ? n : end;
                    text.Append(xml, i + 9, stop - i - 9);
                    i = end < 0 ? n : end + 3;
                    continue;
                }

                if (Starts(xml, i, "<?"))
                {
                    int end = xml.IndexOf("?>", i + 2, StringComparison.Ordinal);
                    i = end < 0 ? n : end + 2;
                    continue;
                }

                if (Starts(xml, i, "<!"))
                {
                    // DOCTYPE, possibly with an internal subset in brackets.
                    int brackets = 0;
                    i += 2;
                    while (i < n)
                    {
                        if (xml[i] == '[') brackets++;
                        else if (xml[i] == ']') brackets--;
                        else if (xml[i] == '>' && brackets <= 0) break;
                        i++;
                    }

                    i++;
                    continue;
                }

                FlushText(current, text);

                if (i + 1 < n && xml[i + 1] == '/')
                {
                    int end = xml.IndexOf('>', i);
                    i = end < 0 ? n : end + 1;
                    if (current.parent != null)
                    {
                        Finish(current);
                        current = current.parent;
                        depth--;
                    }

                    continue;
                }

                if (depth >= options.maxDepth)
                {
                    report?.Add($"SVG limit reached: elements nested deeper than {options.maxDepth} levels; the rest of the file is ignored.");
                    break;
                }

                if (++count > options.maxElements)
                {
                    report?.Add($"SVG limit reached: more than {options.maxElements} elements; the rest of the file is ignored.");
                    break;
                }

                i++;
                int nameStart = i;
                while (i < n && !char.IsWhiteSpace(xml[i]) && xml[i] != '>' && xml[i] != '/') i++;
                string qualified = xml.Substring(nameStart, i - nameStart);
                var node = new SvgNode { name = LocalName(qualified), parent = current };
                int colonAt = qualified.IndexOf(':');
                if (colonAt > 0 && !qualified.StartsWith("xml", StringComparison.Ordinal)) node.prefix = qualified.Substring(0, colonAt);
                current.children.Add(node);

                bool selfClosing = false;
                while (i < n)
                {
                    while (i < n && char.IsWhiteSpace(xml[i])) i++;
                    if (i >= n) break;
                    if (xml[i] == '/')
                    {
                        selfClosing = true;
                        i++;
                        continue;
                    }

                    if (xml[i] == '>')
                    {
                        i++;
                        break;
                    }

                    int an = i;
                    while (i < n && xml[i] != '=' && !char.IsWhiteSpace(xml[i]) && xml[i] != '>' && xml[i] != '/') i++;
                    string attrName = xml.Substring(an, i - an);
                    while (i < n && char.IsWhiteSpace(xml[i])) i++;
                    string value = "";
                    if (i < n && xml[i] == '=')
                    {
                        i++;
                        while (i < n && char.IsWhiteSpace(xml[i])) i++;
                        if (i < n && (xml[i] == '"' || xml[i] == '\''))
                        {
                            char q = xml[i++];
                            int vs = i;
                            while (i < n && xml[i] != q) i++;
                            value = Decode(xml, vs, i - vs);
                            i++;
                        }
                        else
                        {
                            int vs = i;
                            while (i < n && !char.IsWhiteSpace(xml[i]) && xml[i] != '>') i++;
                            value = Decode(xml, vs, i - vs);
                        }
                    }

                    node.attributes[NormalizeAttr(attrName)] = value;
                }

                if (!selfClosing)
                {
                    current = node;
                    depth++;
                }
            }

            FlushText(current, text);
            for (var open = current; open != null; open = open.parent) Finish(open);
            return root;
        }

        static void FlushText(SvgNode node, StringBuilder text)
        {
            if (text.Length == 0) return;
            (node.textBuilder ??= new StringBuilder()).Append(text);
            if (node.name == "text" || node.name == "tspan" || node.name == "a" || node.name == "textPath")
                (node.textParts ??= new List<KeyValuePair<int, string>>()).Add(new KeyValuePair<int, string>(node.children.Count, text.ToString()));
            text.Clear();
        }

        static void Finish(SvgNode node)
        {
            if (node.textBuilder == null) return;
            node.text += node.textBuilder.ToString();
            node.textBuilder = null;
        }

        static bool Starts(string s, int i, string prefix) => string.CompareOrdinal(s, i, prefix, 0, prefix.Length) == 0;

        static string LocalName(string name)
        {
            int colon = name.IndexOf(':');
            return colon >= 0 && !name.StartsWith("xml", StringComparison.Ordinal) ? name.Substring(colon + 1) : name;
        }

        static string NormalizeAttr(string name) => name == "xlink:href" ? "href" : name;

        public static string Decode(string s, int start, int length)
        {
            int amp = s.IndexOf('&', start, length);
            if (amp < 0) return s.Substring(start, length);
            var sb = new StringBuilder(length);
            int end = start + length;
            int i = start;
            while (i < end)
            {
                char c = s[i];
                if (c != '&')
                {
                    sb.Append(c);
                    i++;
                    continue;
                }

                int semi = s.IndexOf(';', i, Math.Min(12, end - i));
                if (semi < 0)
                {
                    sb.Append(c);
                    i++;
                    continue;
                }

                string ent = s.Substring(i + 1, semi - i - 1);
                switch (ent)
                {
                    case "amp": sb.Append('&'); break;
                    case "lt": sb.Append('<'); break;
                    case "gt": sb.Append('>'); break;
                    case "quot": sb.Append('"'); break;
                    case "apos": sb.Append('\''); break;
                    case "nbsp": sb.Append(' '); break;
                    default:
                        if (ent.Length > 1 && ent[0] == '#')
                        {
                            try
                            {
                                int code = ent[1] == 'x' || ent[1] == 'X'
                                    ? Convert.ToInt32(ent.Substring(2), 16)
                                    : int.Parse(ent.Substring(1), System.Globalization.CultureInfo.InvariantCulture);
                                sb.Append(char.ConvertFromUtf32(code));
                            }
                            catch
                            {
                                sb.Append('&').Append(ent).Append(';');
                            }
                        }
                        else sb.Append('&').Append(ent).Append(';');

                        break;
                }

                i = semi + 1;
            }

            return sb.ToString();
        }
    }
}
