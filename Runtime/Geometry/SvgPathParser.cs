using System;
using Unity.Mathematics;

namespace FirstSymbol.Vectra.Geometry
{
    /// <summary>Parser of SVG path data (the <c>d</c> attribute): every command, absolute and relative, including arcs.</summary>
    public static class SvgPathParser
    {
        /// <summary>Parses <paramref name="d"/> into <paramref name="builder"/>. Returns false on malformed data (the
        /// commands parsed before the error are kept, as required by the SVG specification).</summary>
        public static bool Parse(string d, PathBuilder builder)
        {
            if (string.IsNullOrEmpty(d)) return true;
            int i = 0;
            char cmd = '\0';
            float2 cur = float2.zero, start = float2.zero, lastCtrl = float2.zero;
            char prevCmd = '\0';
            int n = d.Length;

            while (true)
            {
                SkipSeparators(d, ref i);
                if (i >= n) break;
                char c = d[i];
                if (IsCommand(c))
                {
                    cmd = c;
                    i++;
                }
                else if (cmd == '\0')
                {
                    return false;
                }

                bool rel = char.IsLower(cmd);
                char uc = char.ToUpperInvariant(cmd);
                float2 o = rel ? cur : float2.zero;

                switch (uc)
                {
                    case 'M':
                    {
                        if (!ReadPoint(d, ref i, out float2 p)) return false;
                        cur = p + o;
                        start = cur;
                        builder.MoveTo(cur);
                        lastCtrl = cur;
                        // Subsequent pairs are implicit line-to commands.
                        cmd = rel ? 'l' : 'L';
                        prevCmd = 'M';
                        continue;
                    }
                    case 'L':
                    {
                        if (!ReadPoint(d, ref i, out float2 p)) return false;
                        cur = p + o;
                        builder.LineTo(cur);
                        lastCtrl = cur;
                        break;
                    }
                    case 'H':
                    {
                        if (!ReadNumber(d, ref i, out float x)) return false;
                        cur = new float2(rel ? cur.x + x : x, cur.y);
                        builder.LineTo(cur);
                        lastCtrl = cur;
                        break;
                    }
                    case 'V':
                    {
                        if (!ReadNumber(d, ref i, out float y)) return false;
                        cur = new float2(cur.x, rel ? cur.y + y : y);
                        builder.LineTo(cur);
                        lastCtrl = cur;
                        break;
                    }
                    case 'C':
                    {
                        if (!ReadPoint(d, ref i, out float2 c1) || !ReadPoint(d, ref i, out float2 c2) ||
                            !ReadPoint(d, ref i, out float2 p)) return false;
                        c1 += o;
                        c2 += o;
                        p += o;
                        builder.CubicTo(c1, c2, p);
                        lastCtrl = c2;
                        cur = p;
                        break;
                    }
                    case 'S':
                    {
                        if (!ReadPoint(d, ref i, out float2 c2) || !ReadPoint(d, ref i, out float2 p)) return false;
                        char pu = char.ToUpperInvariant(prevCmd);
                        float2 c1 = (pu == 'C' || pu == 'S') ? 2f * cur - lastCtrl : cur;
                        c2 += o;
                        p += o;
                        builder.CubicTo(c1, c2, p);
                        lastCtrl = c2;
                        cur = p;
                        break;
                    }
                    case 'Q':
                    {
                        if (!ReadPoint(d, ref i, out float2 c1) || !ReadPoint(d, ref i, out float2 p)) return false;
                        c1 += o;
                        p += o;
                        builder.QuadTo(c1, p);
                        lastCtrl = c1;
                        cur = p;
                        break;
                    }
                    case 'T':
                    {
                        if (!ReadPoint(d, ref i, out float2 p)) return false;
                        char pu = char.ToUpperInvariant(prevCmd);
                        float2 c1 = (pu == 'Q' || pu == 'T') ? 2f * cur - lastCtrl : cur;
                        p += o;
                        builder.QuadTo(c1, p);
                        lastCtrl = c1;
                        cur = p;
                        break;
                    }
                    case 'A':
                    {
                        if (!ReadNumber(d, ref i, out float rx) || !ReadNumber(d, ref i, out float ry) ||
                            !ReadNumber(d, ref i, out float rot) || !ReadFlag(d, ref i, out bool large) ||
                            !ReadFlag(d, ref i, out bool sweep) || !ReadPoint(d, ref i, out float2 p)) return false;
                        p += o;
                        builder.ArcTo(new float2(rx, ry), rot, large, sweep, p);
                        cur = p;
                        lastCtrl = cur;
                        break;
                    }
                    case 'Z':
                    {
                        builder.Close();
                        cur = start;
                        lastCtrl = cur;
                        prevCmd = 'Z';
                        // Z takes no arguments; a following number would be an error, a following command is fine.
                        cmd = '\0';
                        continue;
                    }
                    default:
                        return false;
                }

                prevCmd = cmd;
            }

            return true;
        }

        static bool IsCommand(char c)
        {
            switch (c)
            {
                case 'M': case 'm': case 'L': case 'l': case 'H': case 'h': case 'V': case 'v':
                case 'C': case 'c': case 'S': case 's': case 'Q': case 'q': case 'T': case 't':
                case 'A': case 'a': case 'Z': case 'z':
                    return true;
                default:
                    return false;
            }
        }

        static void SkipSeparators(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == ',' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r' || s[i] == '\f'))
                i++;
        }

        static bool ReadPoint(string s, ref int i, out float2 p)
        {
            p = float2.zero;
            if (!ReadNumber(s, ref i, out float x)) return false;
            if (!ReadNumber(s, ref i, out float y)) return false;
            p = new float2(x, y);
            return true;
        }

        static bool ReadFlag(string s, ref int i, out bool flag)
        {
            SkipSeparators(s, ref i);
            flag = false;
            if (i >= s.Length) return false;
            char c = s[i];
            if (c != '0' && c != '1') return false;
            flag = c == '1';
            i++;
            return true;
        }

        /// <summary>Reads an SVG number (supports exponents and compact forms such as "1.5.5" or "1-2").</summary>
        public static bool ReadNumber(string s, ref int i, out float value)
        {
            SkipSeparators(s, ref i);
            value = 0f;
            int n = s.Length;
            if (i >= n) return false;
            int startIdx = i;
            if (s[i] == '+' || s[i] == '-') i++;
            bool digits = false, dot = false;
            while (i < n)
            {
                char c = s[i];
                if (c >= '0' && c <= '9')
                {
                    digits = true;
                    i++;
                }
                else if (c == '.' && !dot)
                {
                    dot = true;
                    i++;
                }
                else break;
            }

            if (!digits)
            {
                i = startIdx;
                return false;
            }

            if (i < n && (s[i] == 'e' || s[i] == 'E'))
            {
                int save = i;
                i++;
                if (i < n && (s[i] == '+' || s[i] == '-')) i++;
                bool expDigits = false;
                while (i < n && s[i] >= '0' && s[i] <= '9')
                {
                    i++;
                    expDigits = true;
                }

                if (!expDigits) i = save;
            }

            value = ParseFloat(s, startIdx, i - startIdx);
            return true;
        }

        /// <summary>Culture-invariant float parsing without allocating substrings.</summary>
        public static float ParseFloat(string s, int start, int length)
        {
            int i = start, end = start + length;
            bool neg = false;
            if (i < end && (s[i] == '+' || s[i] == '-'))
            {
                neg = s[i] == '-';
                i++;
            }

            double mant = 0;
            int exp10 = 0;
            while (i < end && s[i] >= '0' && s[i] <= '9') mant = mant * 10 + (s[i++] - '0');
            if (i < end && s[i] == '.')
            {
                i++;
                while (i < end && s[i] >= '0' && s[i] <= '9')
                {
                    mant = mant * 10 + (s[i++] - '0');
                    exp10--;
                }
            }

            if (i < end && (s[i] == 'e' || s[i] == 'E'))
            {
                i++;
                bool eneg = false;
                if (i < end && (s[i] == '+' || s[i] == '-'))
                {
                    eneg = s[i] == '-';
                    i++;
                }

                int e = 0;
                while (i < end && s[i] >= '0' && s[i] <= '9') e = e * 10 + (s[i++] - '0');
                exp10 += eneg ? -e : e;
            }

            double v = mant * Math.Pow(10, exp10);
            return (float)(neg ? -v : v);
        }
    }
}
