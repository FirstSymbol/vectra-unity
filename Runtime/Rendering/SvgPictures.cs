using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FirstSymbol.Vectra.Svg;
using UnityEngine;

namespace FirstSymbol.Vectra.Rendering
{
    /// <summary>
    /// What decides a composited SVG picture: everything <see cref="SvgCompositor.RenderStep"/> reads. Two components with equal keys draw the
    /// same pixels and show one render texture (see <see cref="SvgPictures"/>). The overrides are part of the key as a hash; a picture also keeps a
    /// copy of them, which is compared before it is shared.
    /// </summary>
    readonly struct SvgPictureKey : IEquatable<SvgPictureKey>
    {
        readonly VectraSvgDocument m_Doc;
        readonly int m_Version, m_From, m_To, m_Width, m_Height, m_Quality, m_Align, m_MaxSize, m_OverrideCount;
        readonly Rect m_Rect;
        readonly float m_PointScale;
        readonly bool m_Gamma, m_Linear;
        readonly ulong m_Overrides;

        public SvgPictureKey(VectraSvgDocument doc, int from, int to, Rect rect, int width, int height, VectraQuality quality, List<SvgOverride> overrides,
            SvgAlign? align, float pointScale, bool gamma, int maxSize)
        {
            m_Doc = doc;
            m_Version = doc.contentVersion;
            m_From = from;
            m_To = to;
            m_Rect = rect;
            m_Width = width;
            m_Height = height;
            m_Quality = (int)quality;
            m_Align = align.HasValue ? (int)align.Value : -1;
            m_PointScale = pointScale;
            m_Gamma = gamma;
            m_Linear = VectraColorSpace.Linear;
            m_MaxSize = maxSize;
            m_Overrides = 14695981039346656037UL;
            m_OverrideCount = 0;
            if (overrides == null) return;
            foreach (var o in overrides)
            {
                if (o == null) continue;
                m_OverrideCount++;
                Mix(ref m_Overrides, o.selector != null ? o.selector.GetHashCode() : 0);
                Mix(ref m_Overrides, o.hide ? 1 : 0);
                Mix(ref m_Overrides, o.overrideFill ? 1 : 0);
                Mix(ref m_Overrides, o.overrideStroke ? 1 : 0);
                Mix(ref m_Overrides, o.fill);
                Mix(ref m_Overrides, o.stroke);
            }
        }

        static void Mix(ref ulong h, Color c)
        {
            Mix(ref h, BitConverter.SingleToInt32Bits(c.r));
            Mix(ref h, BitConverter.SingleToInt32Bits(c.g));
            Mix(ref h, BitConverter.SingleToInt32Bits(c.b));
            Mix(ref h, BitConverter.SingleToInt32Bits(c.a));
        }

        static void Mix(ref ulong h, int v)
        {
            h ^= (uint)v;
            h *= 1099511628211UL;
        }

        public bool Equals(SvgPictureKey o) =>
            ReferenceEquals(m_Doc, o.m_Doc) && m_Version == o.m_Version && m_From == o.m_From && m_To == o.m_To && m_Width == o.m_Width && m_Height == o.m_Height &&
            m_Quality == o.m_Quality && m_Align == o.m_Align && m_MaxSize == o.m_MaxSize && m_Gamma == o.m_Gamma && m_Linear == o.m_Linear &&
            m_Rect == o.m_Rect && m_PointScale == o.m_PointScale && m_Overrides == o.m_Overrides && m_OverrideCount == o.m_OverrideCount;

        public override bool Equals(object obj) => obj is SvgPictureKey k && Equals(k);

        public override int GetHashCode()
        {
            var h = new HashCode();
            h.Add(RuntimeHelpers.GetHashCode(m_Doc));
            h.Add(m_Version);
            h.Add(m_From);
            h.Add(m_To);
            h.Add(m_Width);
            h.Add(m_Height);
            h.Add(m_Overrides);
            h.Add(m_Rect);
            return h.ToHashCode();
        }
    }

    /// <summary>One shared picture: a render texture and the number of components that show it.</summary>
    sealed class SvgPicture
    {
        internal RenderTexture texture;
        internal SvgPictureKey key;
        internal SvgOverride[] overrides;
        internal int users;
        internal bool listed;

        internal bool Alive => texture != null && texture.IsCreated();

        internal bool SameOverrides(List<SvgOverride> list)
        {
            int n = 0;
            if (list != null)
                foreach (var o in list)
                {
                    if (o == null) continue;
                    if (n >= overrides.Length) return false;
                    var c = overrides[n++];
                    if (c.selector != o.selector || c.hide != o.hide || c.overrideFill != o.overrideFill || c.overrideStroke != o.overrideStroke ||
                        c.fill != o.fill || c.stroke != o.stroke) return false;
                }

            return n == overrides.Length;
        }

        internal static SvgOverride[] Copy(List<SvgOverride> list)
        {
            int n = 0;
            if (list != null)
                foreach (var o in list)
                    if (o != null) n++;
            if (n == 0) return Array.Empty<SvgOverride>();
            var copy = new SvgOverride[n];
            n = 0;
            foreach (var o in list)
                if (o != null)
                    copy[n++] = new SvgOverride { selector = o.selector, hide = o.hide, overrideFill = o.overrideFill, fill = o.fill, overrideStroke = o.overrideStroke, stroke = o.stroke };
            return copy;
        }
    }

    /// <summary>
    /// The pictures of composited SVG parts, shared between components that would draw the same pixels (an icon repeated in a list): one render
    /// texture and one composite per key, counted by users and released when the last user lets go.
    /// </summary>
    static class SvgPictures
    {
        static readonly Dictionary<SvgPictureKey, SvgPicture> s_Map = new Dictionary<SvgPictureKey, SvgPicture>();

        /// <summary>Pictures alive now.</summary>
        public static int Count => s_Map.Count;

        /// <summary>
        /// Makes <paramref name="current"/> the picture of the part [from, to) of <paramref name="doc"/> for this size and density. Keeps it when its
        /// key still matches; otherwise takes the shared picture of the new key (drawing it first when nobody has) and lets go of the old one.
        /// </summary>
        public static bool Acquire(ref SvgPicture current, VectraSvgDocument doc, int from, int to, Rect rect, float pixelSize, VectraQuality quality,
            List<SvgOverride> overrides, SvgAlign? align, float pointScale, bool gamma, int maxSize)
        {
            if (doc == null || rect.width <= 0f || rect.height <= 0f) return false;
            SvgCompositor.PictureSize(rect, pixelSize, maxSize, out int w, out int h);
            var key = new SvgPictureKey(doc, from, to, rect, w, h, quality, overrides, align, pointScale, gamma, maxSize);
            if (current != null && current.listed && current.Alive && current.key.Equals(key) && current.SameOverrides(overrides)) return true;

            SvgPicture picture;
            if (s_Map.TryGetValue(key, out picture) && !picture.SameOverrides(overrides))
            {
                // Two override lists with the same hash: draw a picture of its own, not shared.
                picture = new SvgPicture { key = key, overrides = SvgPicture.Copy(overrides) };
            }
            else if (picture == null)
            {
                picture = new SvgPicture { key = key, overrides = SvgPicture.Copy(overrides), listed = true };
                s_Map.Add(key, picture);
            }

            // Take the new picture before letting go of the old one: a change that lands on the same picture must not free it.
            if (!ReferenceEquals(picture, current))
            {
                picture.users++;
                var old = current;
                current = picture;
                Release(ref old);
            }

            if (!picture.Alive)
            {
                // A new picture, or one whose texture was lost (a device reset): draw it (again). Components that share it read the texture from it.
                var texture = picture.texture;
                if (texture != null && !texture.IsCreated())
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                    texture = null;
                }

                bool ok = SvgCompositor.RenderStep(doc, from, to, rect, pixelSize, quality, overrides, align, pointScale, ref texture, maxSize, gamma);
                picture.texture = texture;
                return ok;
            }

            return true;
        }

        /// <summary>Lets go of a picture; its render texture goes with the last user.</summary>
        public static void Release(ref SvgPicture picture)
        {
            var p = picture;
            picture = null;
            if (p == null) return;
            if (--p.users > 0) return;
            if (p.listed)
            {
                p.listed = false;
                s_Map.Remove(p.key);
            }

            SvgCompositor.Free(ref p.texture);
        }

        /// <summary>Destroys every picture (domain reload, leaving play mode); components that still hold one draw it again.</summary>
        public static void DestroyAll()
        {
            foreach (var p in s_Map.Values)
            {
                p.listed = false;
                SvgCompositor.Free(ref p.texture);
            }

            s_Map.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => DestroyAll();

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void HookReload()
        {
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += DestroyAll;
            UnityEditor.EditorApplication.quitting += DestroyAll;
        }
#endif
    }
}
