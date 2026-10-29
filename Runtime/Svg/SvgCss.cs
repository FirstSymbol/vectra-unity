using System;
using System.Collections.Generic;
using System.Text;

namespace FirstSymbol.Vectra.Svg
{
    /// <summary>CSS subset for SVG: type, #id, .class, [attr], [attr=value], *, descendant and child combinators.</summary>
    public sealed class SvgCss
    {
        sealed class Compound
        {
            public string type;
            public string id;
            public readonly List<string> classes = new List<string>();
            public readonly List<KeyValuePair<string, string>> attrs = new List<KeyValuePair<string, string>>();
            /// <summary>Combinator to the compound on the left: ' ' descendant, '>' child.</summary>
            public char combinator = ' ';
        }

        sealed class Rule
        {
            public List<Compound> parts;
            public int specificity;
            public int order;
            public Dictionary<string, string> declarations;
            public HashSet<string> important;
        }

        readonly List<Rule> m_Rules = new List<Rule>();

        public bool IsEmpty => m_Rules.Count == 0;

        public void AddStylesheet(string css)
        {
            if (string.IsNullOrEmpty(css)) return;
            css = StripComments(css);
            int i = 0;
            while (i < css.Length)
            {
                int open = css.IndexOf('{', i);
                if (open < 0) break;
                string selectorText = css.Substring(i, open - i).Trim();
                int close = FindClose(css, open);
                string body = css.Substring(open + 1, Math.Max(0, close - open - 1));
                i = close + 1;
                if (selectorText.StartsWith("@", StringComparison.Ordinal))
                {
                    // @media and friends: apply nested rules unconditionally; skip @font-face/@keyframes.
                    if (selectorText.StartsWith("@media", StringComparison.Ordinal)) AddStylesheet(body);
                    continue;
                }

                var decl = ParseDeclarations(body, out var important);
                foreach (var sel in selectorText.Split(','))
                {
                    var parts = ParseSelector(sel.Trim());
                    if (parts == null || parts.Count == 0) continue;
                    m_Rules.Add(new Rule
                    {
                        parts = parts,
                        specificity = Specificity(parts),
                        order = m_Rules.Count,
                        declarations = decl,
                        important = important
                    });
                }
            }
        }

        static int FindClose(string css, int open)
        {
            int depth = 0;
            for (int i = open; i < css.Length; i++)
            {
                if (css[i] == '{') depth++;
                else if (css[i] == '}' && --depth == 0) return i;
            }

            return css.Length;
        }

        static string StripComments(string css)
        {
            var sb = new StringBuilder(css.Length);
            for (int i = 0; i < css.Length; i++)
            {
                if (i + 1 < css.Length && css[i] == '/' && css[i + 1] == '*')
                {
                    int end = css.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    i = end < 0 ? css.Length : end + 1;
                    continue;
                }

                sb.Append(css[i]);
            }

            return sb.ToString();
        }

        /// <summary>Parses "a: b; c: d" declarations (also used for the style attribute).</summary>
        public static Dictionary<string, string> ParseDeclarations(string body, out HashSet<string> important)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            important = null;
            if (string.IsNullOrEmpty(body)) return d;
            foreach (var item in body.Split(';'))
            {
                int colon = item.IndexOf(':');
                if (colon <= 0) continue;
                string key = item.Substring(0, colon).Trim().ToLowerInvariant();
                string value = item.Substring(colon + 1).Trim();
                int imp = value.IndexOf("!important", StringComparison.OrdinalIgnoreCase);
                if (imp >= 0)
                {
                    value = value.Substring(0, imp).Trim();
                    (important ??= new HashSet<string>()).Add(key);
                }

                if (key.Length > 0) d[key] = value;
            }

            return d;
        }

        static List<Compound> ParseSelector(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Contains(":")) return null; // pseudo-classes cannot match static SVG
            var parts = new List<Compound>();
            int i = 0;
            char pending = ' ';
            while (i < s.Length)
            {
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
                if (i >= s.Length) break;
                if (s[i] == '>')
                {
                    pending = '>';
                    i++;
                    continue;
                }

                if (s[i] == '+' || s[i] == '~') return null;
                var c = new Compound { combinator = pending };
                pending = ' ';
                while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != '>')
                {
                    char ch = s[i];
                    if (ch == '#') c.id = ReadIdent(s, ref i);
                    else if (ch == '.') c.classes.Add(ReadIdent(s, ref i));
                    else if (ch == '[')
                    {
                        int end = s.IndexOf(']', i);
                        if (end < 0) return null;
                        string inner = s.Substring(i + 1, end - i - 1);
                        int eq = inner.IndexOf('=');
                        if (eq < 0) c.attrs.Add(new KeyValuePair<string, string>(inner.Trim(), null));
                        else c.attrs.Add(new KeyValuePair<string, string>(inner.Substring(0, eq).Trim(), inner.Substring(eq + 1).Trim().Trim('"', '\'')));
                        i = end + 1;
                    }
                    else if (ch == '*')
                    {
                        i++;
                    }
                    else
                    {
                        int st = i;
                        while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '-' || s[i] == '_')) i++;
                        if (i == st) return null;
                        c.type = s.Substring(st, i - st);
                    }
                }

                parts.Add(c);
            }

            return parts;
        }

        static string ReadIdent(string s, ref int i)
        {
            int st = ++i;
            while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '-' || s[i] == '_')) i++;
            return s.Substring(st, i - st);
        }

        static int Specificity(List<Compound> parts)
        {
            int a = 0, b = 0, c = 0;
            foreach (var p in parts)
            {
                if (p.id != null) a++;
                b += p.classes.Count + p.attrs.Count;
                if (p.type != null) c++;
            }

            return a * 10000 + b * 100 + c;
        }

        static bool MatchCompound(Compound c, SvgNode n)
        {
            if (c.type != null && c.type != n.name) return false;
            if (c.id != null && n.Attr("id") != c.id) return false;
            if (c.classes.Count > 0)
            {
                string cls = n.Attr("class");
                if (cls == null) return false;
                var list = cls.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                foreach (var k in c.classes)
                    if (Array.IndexOf(list, k) < 0) return false;
            }

            foreach (var kv in c.attrs)
            {
                if (!n.attributes.TryGetValue(kv.Key, out var v)) return false;
                if (kv.Value != null && v != kv.Value) return false;
            }

            return true;
        }

        static bool Match(List<Compound> parts, int index, SvgNode n)
        {
            if (!MatchCompound(parts[index], n)) return false;
            if (index == 0) return true;
            char comb = parts[index].combinator;
            var p = n.parent;
            if (comb == '>') return p != null && Match(parts, index - 1, p);
            for (; p != null; p = p.parent)
                if (Match(parts, index - 1, p)) return true;
            return false;
        }

        /// <summary>Applies matching rules (by specificity and order) to <paramref name="target"/>.</summary>
        public void Apply(SvgNode node, Dictionary<string, string> target, HashSet<string> importantOut)
        {
            if (m_Rules.Count == 0) return;
            List<Rule> matched = null;
            foreach (var r in m_Rules)
                if (Match(r.parts, r.parts.Count - 1, node)) (matched ??= new List<Rule>()).Add(r);
            if (matched == null) return;
            matched.Sort((x, y) => x.specificity != y.specificity ? x.specificity.CompareTo(y.specificity) : x.order.CompareTo(y.order));
            foreach (var r in matched)
            foreach (var kv in r.declarations)
            {
                if (importantOut.Contains(kv.Key) && (r.important == null || !r.important.Contains(kv.Key))) continue;
                target[kv.Key] = kv.Value;
                if (r.important != null && r.important.Contains(kv.Key)) importantOut.Add(kv.Key);
            }
        }
    }
}
