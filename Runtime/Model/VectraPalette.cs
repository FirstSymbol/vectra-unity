using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Vectra
{
    /// <summary>
    /// Named colours with one value per theme (for example Light and Dark). Paints reference a colour by name
    /// (<see cref="VectraPaint.colorToken"/>); switching the theme of the active palette recolours every shape.
    /// </summary>
    [CreateAssetMenu(menuName = "Vectra/Palette", fileName = "VectraPalette", order = 21)]
    public class VectraPalette : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public string name = "Primary";
            /// <summary>One colour per theme, in the order of <see cref="VectraPalette.themes"/>.</summary>
            [ColorUsage(true, true)] public List<Color> colors = new List<Color>();
        }

        public List<string> themes = new List<string> { "Light", "Dark" };
        public List<Entry> entries = new List<Entry>();
        [SerializeField] int m_Theme;

        static VectraPalette s_Active;

        /// <summary>Raised when the active palette, its theme or its colours change.</summary>
        public static event Action Changed;

        /// <summary>The palette paint tokens are resolved against (set by <see cref="VectraTheme"/> or from code).</summary>
        public static VectraPalette Active
        {
            get => s_Active;
            set
            {
                if (s_Active == value) return;
                s_Active = value;
                Changed?.Invoke();
            }
        }

        public int ThemeIndex
        {
            get => Mathf.Clamp(m_Theme, 0, Mathf.Max(themes.Count - 1, 0));
            set
            {
                value = Mathf.Clamp(value, 0, Mathf.Max(themes.Count - 1, 0));
                if (m_Theme == value) return;
                m_Theme = value;
                NotifyChanged();
            }
        }

        public string ThemeName => themes.Count > 0 ? themes[ThemeIndex] : string.Empty;

        /// <summary>Switches to the theme with the given name. Returns false when there is no such theme.</summary>
        public bool SetTheme(string themeName)
        {
            int i = themes.IndexOf(themeName);
            if (i < 0) return false;
            ThemeIndex = i;
            return true;
        }

        public bool TryGetColor(string token, out Color color) => TryGetColor(token, ThemeIndex, out color);

        public bool TryGetColor(string token, int theme, out Color color)
        {
            color = default;
            if (string.IsNullOrEmpty(token)) return false;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e == null || e.colors == null || e.colors.Count == 0 || !string.Equals(e.name, token, StringComparison.Ordinal))
                    continue;
                color = e.colors[Mathf.Clamp(theme, 0, e.colors.Count - 1)];
                return true;
            }

            return false;
        }

        /// <summary>Adds or replaces a colour of the given theme.</summary>
        public void SetColor(string token, int theme, Color color)
        {
            var e = entries.Find(x => x != null && x.name == token);
            if (e == null) entries.Add(e = new Entry { name = token });
            while (e.colors.Count < themes.Count) e.colors.Add(e.colors.Count > 0 ? e.colors[e.colors.Count - 1] : Color.white);
            e.colors[Mathf.Clamp(theme, 0, e.colors.Count - 1)] = color;
            NotifyChanged();
        }

        /// <summary>Resolves a token against the active palette, or returns <paramref name="fallback"/>.</summary>
        public static Color Resolve(string token, Color fallback) =>
            !string.IsNullOrEmpty(token) && s_Active != null && s_Active.TryGetColor(token, out var c) ? c : fallback;

        public void NotifyChanged()
        {
            if (this == s_Active) Changed?.Invoke();
        }

        void OnValidate()
        {
            foreach (var e in entries)
                if (e != null)
                    while (e.colors.Count < themes.Count) e.colors.Add(e.colors.Count > 0 ? e.colors[e.colors.Count - 1] : Color.white);
            NotifyChanged();
        }
    }
}
