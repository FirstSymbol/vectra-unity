using System;
using System.Collections.Generic;
using FirstSymbol.Vectra.TextEngines;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FirstSymbol.Vectra.Svg
{
    /// <summary>
    /// Maps SVG <c>font-family</c> names to fonts of the text engines. Each engine has its own fields: the TextMesh Pro
    /// ones (<c>regular</c>, <c>bold</c>, <c>fallback</c>) hold TMP font assets, the Lettera ones hold a Lettera font asset
    /// or font stack. The fields are plain object references so Vectra itself does not depend on either engine; the
    /// inspector only accepts the right type. Maps saved by 0.2 keep their TextMesh Pro fonts as they were.
    /// </summary>
    [CreateAssetMenu(menuName = "Vectra/SVG Font Map", fileName = "VectraFontMap", order = 22)]
    public class VectraFontMap : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            [Tooltip("Font family as written in the SVG (case-insensitive), e.g. \"Inter\" or \"sans-serif\".")]
            public string family = "sans-serif";
            [Tooltip("TextMesh Pro font asset.")]
            public Object regular;
            [Tooltip("Optional bold face for TextMesh Pro; without it TextMesh Pro synthesizes bold.")]
            public Object bold;
            [Tooltip("Lettera font asset or font stack.")]
            public Object lettera;
            [Tooltip("Optional bold face for Lettera; without it Lettera picks the bold cut of the font or synthesizes it.")]
            public Object letteraBold;
        }

        [Tooltip("TextMesh Pro font used when no entry matches.")]
        public Object fallback;
        [Tooltip("Lettera font (asset or stack) used when no entry matches.")]
        public Object letteraFallback;
        public List<Entry> entries = new List<Entry>();

        /// <summary>Finds a TextMesh Pro font for a CSS font-family list ("Inter, Arial, sans-serif"); null when nothing matches.</summary>
        public Object Resolve(string fontFamily, bool bold, out bool exactBold) =>
            Resolve(VectraTextEngine.TextMeshPro, fontFamily, bold, out exactBold);

        /// <summary>
        /// Finds the font of one engine for a CSS font-family list; the engine's fallback when no family matches, null when
        /// that is empty too. <paramref name="exactBold"/> tells that a face made for bold was found.
        /// </summary>
        public Object Resolve(VectraTextEngine engine, string fontFamily, bool bold, out bool exactBold)
        {
            exactBold = false;
            bool lettera = engine == VectraTextEngine.Lettera;
            if (!string.IsNullOrEmpty(fontFamily))
                foreach (var raw in fontFamily.Split(','))
                {
                    string family = raw.Trim().Trim('"', '\'');
                    foreach (var e in entries)
                    {
                        if (e == null || !string.Equals(e.family, family, StringComparison.OrdinalIgnoreCase)) continue;
                        var boldFace = lettera ? e.letteraBold : e.bold;
                        var regular = lettera ? e.lettera : e.regular;
                        if (bold && boldFace != null)
                        {
                            exactBold = true;
                            return boldFace;
                        }

                        if (regular != null) return regular;
                    }
                }

            return lettera ? letteraFallback : fallback;
        }

        /// <summary>
        /// Clears fonts that do not belong in their field (a Lettera font in a TextMesh Pro field and the like), for
        /// every engine that is available. Returns how many it cleared. Runs when the asset is edited.
        /// </summary>
        public int Validate()
        {
            int cleared = 0;
            var tmp = VectraTextBackends.Get(VectraTextEngine.TextMeshPro);
            var lettera = VectraTextBackends.Get(VectraTextEngine.Lettera);
            cleared += Check(ref fallback, tmp);
            cleared += Check(ref letteraFallback, lettera);
            foreach (var e in entries)
            {
                if (e == null) continue;
                cleared += Check(ref e.regular, tmp) + Check(ref e.bold, tmp);
                cleared += Check(ref e.lettera, lettera) + Check(ref e.letteraBold, lettera);
            }

            return cleared;
        }

        int Check(ref Object field, IVectraTextBackend backend)
        {
            if (field == null || backend == null || backend.AcceptsFont(field)) return 0;
            Debug.LogWarning($"Vectra font map '{name}': {field.name} is not a {backend.Engine} font and was cleared.", this);
            field = null;
            return 1;
        }

#if UNITY_EDITOR
        void OnValidate() => Validate();
#endif

        /// <summary>Whether the map holds any font for the engine.</summary>
        public bool HasFonts(VectraTextEngine engine)
        {
            bool lettera = engine == VectraTextEngine.Lettera;
            if ((lettera ? letteraFallback : fallback) != null) return true;
            foreach (var e in entries)
                if (e != null && ((lettera ? e.lettera : e.regular) != null || (lettera ? e.letteraBold : e.bold) != null))
                    return true;
            return false;
        }
    }
}
