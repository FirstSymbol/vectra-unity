using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace FirstSymbol.Vectra.Svg
{
    /// <summary>Maps SVG <c>font-family</c> names to TextMeshPro fonts.</summary>
    [CreateAssetMenu(menuName = "Vectra/SVG Font Map", fileName = "VectraFontMap", order = 22)]
    public class VectraFontMap : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            [Tooltip("Font family as written in the SVG (case-insensitive), e.g. \"Inter\" or \"sans-serif\".")]
            public string family = "sans-serif";
            public TMP_FontAsset regular;
            [Tooltip("Optional bold face; without it TextMeshPro synthesizes bold.")]
            public TMP_FontAsset bold;
        }

        public TMP_FontAsset fallback;
        public List<Entry> entries = new List<Entry>();

        /// <summary>Finds a font for a CSS font-family list ("Inter, Arial, sans-serif"); null when nothing matches.</summary>
        public TMP_FontAsset Resolve(string fontFamily, bool bold, out bool exactBold)
        {
            exactBold = false;
            if (!string.IsNullOrEmpty(fontFamily))
                foreach (var raw in fontFamily.Split(','))
                {
                    string family = raw.Trim().Trim('"', '\'');
                    foreach (var e in entries)
                    {
                        if (e == null || !string.Equals(e.family, family, StringComparison.OrdinalIgnoreCase)) continue;
                        if (bold && e.bold != null)
                        {
                            exactBold = true;
                            return e.bold;
                        }

                        if (e.regular != null) return e.regular;
                    }
                }

            return fallback;
        }
    }
}
