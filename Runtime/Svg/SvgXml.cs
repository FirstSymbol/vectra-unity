using System;
using System.Collections.Generic;
using System.Text;

namespace FirstSymbol.Vectra.Svg
{
    /// <summary>Minimal XML element tree used by the SVG parser.</summary>
    public sealed class SvgNode
    {
        public string name;
        public readonly Dictionary<string, string> attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly List<SvgNode> children = new List<SvgNode>();
        public SvgNode parent;
        /// <summary>Character data directly inside the element (text, CSS of &lt;style&gt;).</summary>
        public string text = "";

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
        public static SvgNode Parse(string xml)
        {
            if (string.IsNullOrEmpty(xml)) return null;
            var root = new SvgNode { name = "#document" };
            var current = root;
            int i = 0, n = xml.Length;
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
                    int depth = 0;
                    i += 2;
                    while (i < n)
                    {
                        if (xml[i] == '[') depth++;
                        else if (xml[i] == ']') depth--;
                        else if (xml[i] == '>' && depth <= 0) break;
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
                    if (current.parent != null) current = current.parent;
                    continue;
                }

                i++;
                int nameStart = i;
                while (i < n && !char.IsWhiteSpace(xml[i]) && xml[i] != '>' && xml[i] != '/') i++;
                var node = new SvgNode { name = LocalName(xml.Substring(nameStart, i - nameStart)), parent = current };
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

                if (!selfClosing) current = node;
            }

            FlushText(current, text);
            return root;
        }

        static void FlushText(SvgNode node, StringBuilder text)
        {
            if (text.Length == 0) return;
            node.text += text.ToString();
            text.Clear();
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
