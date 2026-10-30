using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

#pragma warning disable CS0618 // instance-id APIs exist in every supported Unity version

namespace FirstSymbol.Vectra.Editor.Cli
{
    /// <summary>Minimal JSON reader: objects become Dictionary&lt;string, object&gt;, arrays List&lt;object&gt;, numbers double.</summary>
    static class MiniJson
    {
        public static object Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            int i = 0;
            var v = Value(json, ref i);
            Skip(json, ref i);
            if (i < json.Length) throw new FormatException($"Unexpected '{json[i]}' at {i} in JSON");
            return v;
        }

        static void Skip(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        static object Value(string s, ref int i)
        {
            Skip(s, ref i);
            if (i >= s.Length) throw new FormatException("Unexpected end of JSON");
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>();
                i++;
                Skip(s, ref i);
                if (s[i] == '}')
                {
                    i++;
                    return d;
                }

                while (true)
                {
                    Skip(s, ref i);
                    var key = (string)Value(s, ref i);
                    Skip(s, ref i);
                    if (s[i] != ':') throw new FormatException($"Expected ':' at {i}");
                    i++;
                    d[key] = Value(s, ref i);
                    Skip(s, ref i);
                    if (s[i] == ',')
                    {
                        i++;
                        continue;
                    }

                    if (s[i] == '}')
                    {
                        i++;
                        return d;
                    }

                    throw new FormatException($"Expected ',' or '}}' at {i}");
                }
            }

            if (c == '[')
            {
                var l = new List<object>();
                i++;
                Skip(s, ref i);
                if (s[i] == ']')
                {
                    i++;
                    return l;
                }

                while (true)
                {
                    l.Add(Value(s, ref i));
                    Skip(s, ref i);
                    if (s[i] == ',')
                    {
                        i++;
                        continue;
                    }

                    if (s[i] == ']')
                    {
                        i++;
                        return l;
                    }

                    throw new FormatException($"Expected ',' or ']' at {i}");
                }
            }

            if (c == '"' || c == '\'')
            {
                char quote = c;
                var sb = new StringBuilder();
                i++;
                while (i < s.Length && s[i] != quote)
                {
                    if (s[i] == '\\' && i + 1 < s.Length)
                    {
                        i++;
                        switch (s[i])
                        {
                            case 'n': sb.Append('\n'); break;
                            case 't': sb.Append('\t'); break;
                            case 'r': sb.Append('\r'); break;
                            case 'u':
                                sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16));
                                i += 4;
                                break;
                            default: sb.Append(s[i]); break;
                        }
                    }
                    else sb.Append(s[i]);

                    i++;
                }

                i++;
                return sb.ToString();
            }

            int start = i;
            while (i < s.Length && ",}]:{[ \t\r\n".IndexOf(s[i]) < 0) i++;
            string word = s.Substring(start, i - start);
            if (word == "true") return true;
            if (word == "false") return false;
            if (word == "null") return null;
            if (double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out double d2)) return d2;
            return word; // unquoted identifiers are accepted as strings
        }
    }

    /// <summary>
    /// Converts Vectra objects to JSON-friendly trees and applies JSON patches to them by reflection.
    /// Colors: "#RRGGBB[AA]" or [r,g,b,a]; vectors: [x,y]; enums: names; assets: paths; polymorphic values: "$type".
    /// </summary>
    static class CliData
    {
        static Dictionary<string, Type> s_Geometry, s_Effects;

        public static Dictionary<string, Type> GeometryTypes => s_Geometry ??= Index<VectraShapeGeometry>("Shape");
        public static Dictionary<string, Type> EffectTypes => s_Effects ??= Index<VectraEffect>("Effect");

        static Dictionary<string, Type> Index<T>(string suffix)
        {
            var d = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in TypeCache.GetTypesDerivedFrom<T>())
            {
                if (t.IsAbstract || t.GetConstructor(Type.EmptyTypes) == null) continue;
                d[t.Name] = t;
                d[t.FullName] = t;
                if (t.Name.EndsWith(suffix) && t.Name.Length > suffix.Length) d[t.Name.Substring(0, t.Name.Length - suffix.Length)] = t;
                try
                {
                    var display = t.GetProperty("DisplayName")?.GetValue(Activator.CreateInstance(t)) as string;
                    if (!string.IsNullOrEmpty(display) && !d.ContainsKey(display)) d[display] = t;
                }
                catch (Exception)
                {
                    // Types whose constructor or DisplayName throw are listed by class name only.
                }
            }

            return d;
        }

        public static string ShortName(Type t)
        {
            if (typeof(VectraShapeGeometry).IsAssignableFrom(t) && t.Name.EndsWith("Shape") && t.Name != "Shape") return t.Name.Substring(0, t.Name.Length - 5);
            if (typeof(VectraEffect).IsAssignableFrom(t) && t.Name.EndsWith("Effect") && t.Name != "Effect") return t.Name.Substring(0, t.Name.Length - 6);
            return t.Name;
        }

        public static Type ResolveType(string name, Type baseType)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var index = typeof(VectraEffect).IsAssignableFrom(baseType) ? EffectTypes : typeof(VectraShapeGeometry).IsAssignableFrom(baseType) ? GeometryTypes : null;
            if (index != null && index.TryGetValue(name.Trim(), out var t)) return t;
            if (index == null)
                foreach (var tt in TypeCache.GetTypesDerivedFrom(baseType))
                    if (string.Equals(tt.Name, name, StringComparison.OrdinalIgnoreCase)) return tt;
            throw new ArgumentException($"Unknown {baseType.Name} '{name}'. Run 'unity command vectra types' for the list.");
        }

        // ---------------------------------------------------------------------------------------------------
        // Object -> data
        // ---------------------------------------------------------------------------------------------------

        public static IEnumerable<FieldInfo> Fields(Type t)
        {
            var list = new List<FieldInfo>();
            for (var type = t; type != null && type != typeof(object); type = type.BaseType)
            {
                foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (f.IsInitOnly || f.IsNotSerialized) continue;
                    bool serialized = f.IsPublic || f.GetCustomAttribute<SerializeField>() != null || f.GetCustomAttribute<SerializeReference>() != null;
                    if (serialized) list.Add(f);
                }
            }

            // Base fields (e.g. rotation, enabled) after the type's own fields.
            return list;
        }

        /// <summary>When true, class objects only list fields that differ from a fresh instance of their type.</summary>
        [ThreadStatic] public static bool compact;

        public static object ToData(object o, int depth = 0)
        {
            if (o == null) return null;
            if (depth > 12) return "…";
            switch (o)
            {
                case string s: return s;
                case bool b: return b;
                case float f: return Round(f);
                case double d: return d;
                case int i: return i;
                case Enum e: return e.ToString();
                case Color c: return c.maxColorComponent > 1f ? (object)new List<object> { Round(c.r), Round(c.g), Round(c.b), Round(c.a) } : "#" + ColorUtility.ToHtmlStringRGBA(c);
                case Vector2 v: return new List<object> { Round(v.x), Round(v.y) };
                case Vector3 v: return new List<object> { Round(v.x), Round(v.y), Round(v.z) };
                case Vector4 v: return new List<object> { Round(v.x), Round(v.y), Round(v.z), Round(v.w) };
                case Rect r: return new List<object> { Round(r.x), Round(r.y), Round(r.width), Round(r.height) };
                case AnimationCurve curve: return curve.keys.Select(k => (object)new List<object> { Round(k.time), Round(k.value) }).ToList();
                case Geometry.Affine2 a: return new List<object> { Round(a.a), Round(a.b), Round(a.c), Round(a.d), Round(a.e), Round(a.f) };
                case Object uo:
                    if (uo == null) return null;
                    var path = AssetDatabase.GetAssetPath(uo);
                    return string.IsNullOrEmpty(path) ? uo.name : path;
                case IList list:
                {
                    var l = new List<object>();
                    foreach (var item in list) l.Add(ToData(item, depth + 1));
                    return l;
                }
            }

            var t = o.GetType();
            var dict = new Dictionary<string, object>();
            if (typeof(VectraShapeGeometry).IsAssignableFrom(t) || typeof(VectraEffect).IsAssignableFrom(t)) dict["$type"] = ShortName(t);
            object defaults = null;
            if (compact && t.GetConstructor(Type.EmptyTypes) != null)
            {
                try { defaults = Activator.CreateInstance(t); }
                catch (Exception) { defaults = null; }
            }

            foreach (var f in Fields(t))
            {
                var value = ToData(f.GetValue(o), depth + 1);
                if (defaults != null)
                {
                    bool was = compact;
                    compact = false;
                    var def = ToData(f.GetValue(defaults), depth + 1);
                    compact = was;
                    if (DataEquals(value, def)) continue;
                }

                dict[Clean(f.Name)] = value;
            }

            return dict;
        }

        static bool DataEquals(object a, object b)
        {
            if (a is string sa && sa.Length == 0) a = null;
            if (b is string sb && sb.Length == 0) b = null;
            if (a == null || b == null) return a == null && b == null;
            if (a is Dictionary<string, object> da && b is Dictionary<string, object> db)
                return da.Count <= db.Count && da.All(kv => db.TryGetValue(kv.Key, out var v) && DataEquals(kv.Value, v));
            if (a is List<object> la && b is List<object> lb) return la.Count == lb.Count && la.Zip(lb, DataEquals).All(x => x);
            return a.Equals(b);
        }

        static string Clean(string name) => name.StartsWith("m_") ? char.ToLowerInvariant(name[2]) + name.Substring(3) : name;

        static object Round(float f) => float.IsNaN(f) || float.IsInfinity(f) ? 0.0 : Math.Round(f, 4);

        // ---------------------------------------------------------------------------------------------------
        // Data -> object
        // ---------------------------------------------------------------------------------------------------

        /// <summary>Sets the fields named in <paramref name="data"/> on <paramref name="target"/> (unknown keys throw).</summary>
        public static void Apply(object target, Dictionary<string, object> data)
        {
            var t = target.GetType();
            var fields = Fields(t).ToList();
            foreach (var kv in data)
            {
                if (kv.Key == "$type") continue;
                var f = fields.FirstOrDefault(x => string.Equals(x.Name, kv.Key, StringComparison.OrdinalIgnoreCase) ||
                                                   string.Equals(Clean(x.Name), kv.Key, StringComparison.OrdinalIgnoreCase));
                if (f == null)
                    throw new ArgumentException($"{ShortName(t)} has no field '{kv.Key}'. Fields: {string.Join(", ", fields.Select(x => Clean(x.Name)))}");
                f.SetValue(target, Convert(kv.Value, f.FieldType, f.GetValue(target)));
            }
        }

        public static object Convert(object v, Type type, object existing = null)
        {
            if (v == null) return type.IsValueType ? Activator.CreateInstance(type) : null;
            if (type == typeof(string)) return v.ToString();
            if (type == typeof(float)) return (float)ToDouble(v);
            if (type == typeof(double)) return ToDouble(v);
            if (type == typeof(int)) return (int)Math.Round(ToDouble(v));
            if (type == typeof(bool)) return v is bool b ? b : bool.Parse(v.ToString());
            if (type.IsEnum) return v is string s ? Enum.Parse(type, s.Replace(" ", ""), true) : Enum.ToObject(type, (int)ToDouble(v));
            if (type == typeof(Color)) return ToColor(v);
            if (type == typeof(Vector2)) { var a = Floats(v, 2); return new Vector2(a[0], a[1]); }
            if (type == typeof(Vector3)) { var a = Floats(v, 3); return new Vector3(a[0], a[1], a[2]); }
            if (type == typeof(Vector4)) { var a = Floats(v, 4); return new Vector4(a[0], a[1], a[2], a[3]); }
            if (type == typeof(Rect)) { var a = Floats(v, 4); return new Rect(a[0], a[1], a[2], a[3]); }
            if (type == typeof(Geometry.Affine2)) { var a = Floats(v, 6); return new Geometry.Affine2(a[0], a[1], a[2], a[3], a[4], a[5]); }
            if (type == typeof(AnimationCurve))
            {
                var keys = ((List<object>)v).Select(k => Floats(k, 2)).Select(k => new Keyframe(k[0], k[1])).ToArray();
                return new AnimationCurve(keys);
            }

            if (typeof(Object).IsAssignableFrom(type))
            {
                var path = v.ToString();
                var asset = AssetDatabase.LoadAssetAtPath(path, type);
                if (asset == null) asset = AssetDatabase.LoadAllAssetsAtPath(path).FirstOrDefault(type.IsInstanceOfType);
                if (asset == null && typeof(Component).IsAssignableFrom(type)) asset = CliScene.Find(path)?.GetComponent(type);
                if (asset == null && type == typeof(GameObject)) asset = CliScene.Find(path);
                if (asset == null) throw new ArgumentException($"No {type.Name} at '{path}'");
                return asset;
            }

            if (type == typeof(VectraGradient) && (v is string || (v is List<object> gl && gl.Count > 0 && !(gl[0] is Dictionary<string, object>))))
            {
                // Shorthand: "#f00,#00f" or ["#f00", "#0f0", "#00f"] -> evenly spaced stops.
                var colors = v is string gs ? gs.Split(',').Select(x => (object)x.Trim()).ToList() : (List<object>)v;
                var g = new VectraGradient();
                for (int i = 0; i < colors.Count; i++) g.stops.Add(new VectraGradientStop(colors.Count == 1 ? 0f : i / (colors.Count - 1f), ToColor(colors[i])));
                return g;
            }

            if (typeof(IList).IsAssignableFrom(type) && v is List<object> list)
            {
                var elem = type.IsArray ? type.GetElementType() : type.GetGenericArguments()[0];
                var result = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elem));
                var old = existing as IList;
                for (int i = 0; i < list.Count; i++) result.Add(Convert(list[i], elem, old != null && i < old.Count ? old[i] : null));
                if (type.IsArray)
                {
                    var arr = Array.CreateInstance(elem, result.Count);
                    result.CopyTo(arr, 0);
                    return arr;
                }

                return result;
            }

            if (typeof(IList).IsAssignableFrom(type) && v is Dictionary<string, object> patch && existing is IList existingList)
            {
                // {"0": {...}} patches elements by index.
                var elem = type.IsArray ? type.GetElementType() : type.GetGenericArguments()[0];
                foreach (var kv in patch)
                {
                    int index = int.Parse(kv.Key, CultureInfo.InvariantCulture);
                    if (index < 0 || index >= existingList.Count) throw new ArgumentException($"Index {index} is out of range (count {existingList.Count})");
                    existingList[index] = Convert(kv.Value, elem, existingList[index]);
                }

                return existingList;
            }

            if (v is string typeName && (typeof(VectraShapeGeometry).IsAssignableFrom(type) || typeof(VectraEffect).IsAssignableFrom(type)))
                return Activator.CreateInstance(ResolveType(typeName, type));

            if (v is Dictionary<string, object> d)
            {
                object target = existing;
                if (d.TryGetValue("$type", out var tn) && tn is string tname)
                {
                    var concrete = ResolveType(tname, type);
                    if (target == null || target.GetType() != concrete) target = Activator.CreateInstance(concrete);
                }
                else if (target == null)
                {
                    if (type.IsAbstract) throw new ArgumentException($"'{type.Name}' is polymorphic: add \"$type\"");
                    target = Activator.CreateInstance(type);
                }

                Apply(target, d);
                return target;
            }

            throw new ArgumentException($"Cannot convert {v} to {type.Name}");
        }

        static double ToDouble(object v) => v is double d ? d : v is bool b ? (b ? 1 : 0) : double.Parse(v.ToString(), CultureInfo.InvariantCulture);

        static float[] Floats(object v, int n)
        {
            var r = new float[n];
            if (v is string s) v = s.Split(',').Select(x => (object)x.Trim()).ToList();
            if (v is List<object> l)
                for (int i = 0; i < n && i < l.Count; i++) r[i] = (float)ToDouble(l[i]);
            else if (v is Dictionary<string, object> d)
            {
                string[] names = n == 4 && d.ContainsKey("width") ? new[] { "x", "y", "width", "height" } : new[] { "x", "y", "z", "w" };
                for (int i = 0; i < n; i++)
                    if (d.TryGetValue(names[i], out var x)) r[i] = (float)ToDouble(x);
            }
            else throw new ArgumentException($"Expected [{n} numbers], got {v}");

            return r;
        }

        public static Color ToColor(object v)
        {
            if (v is string s)
            {
                if (!s.StartsWith("#") && s.Contains(",")) return ToColor(s.Split(',').Select(x => (object)x.Trim()).ToList());
                if (ColorUtility.TryParseHtmlString(s, out var c)) return c;
                throw new ArgumentException($"Bad colour '{s}' (use #RRGGBB, #RRGGBBAA, a colour name or [r,g,b,a])");
            }

            if (v is Dictionary<string, object> d)
                return new Color(F(d, "r"), F(d, "g"), F(d, "b"), d.ContainsKey("a") ? F(d, "a") : 1f);
            var a = Floats(v, 4);
            if (v is List<object> l && l.Count == 3) a[3] = 1f;
            return new Color(a[0], a[1], a[2], a[3]);

            float F(Dictionary<string, object> dd, string k) => (float)ToDouble(dd[k]);
        }
    }

    /// <summary>Finds scene objects by hierarchy path ("Canvas/Logo"), name or instance id ("#1234").</summary>
    static class CliScene
    {
        public static GameObject Find(string target)
        {
            if (string.IsNullOrEmpty(target)) return null;
            if (target.StartsWith("#") && int.TryParse(target.Substring(1), out int id))
                return EditorUtility.InstanceIDToObject(id) as GameObject ?? (EditorUtility.InstanceIDToObject(id) as Component)?.gameObject;
            var go = GameObject.Find(target);
            if (go != null) return go;
            string trimmed = target.TrimStart('/');
#pragma warning disable CS0618
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
#pragma warning restore CS0618
                if (t.name == target || Path(t.gameObject).TrimStart('/') == trimmed) return t.gameObject;
            return null;
        }

        public static string Path(GameObject go)
        {
            var sb = new StringBuilder(go.name);
            for (var t = go.transform.parent; t != null; t = t.parent) sb.Insert(0, t.name + "/");
            return "/" + sb;
        }
    }
}
