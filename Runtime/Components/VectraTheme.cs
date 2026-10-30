using UnityEngine;

namespace FirstSymbol.Vectra
{
    /// <summary>Makes a palette active and selects its theme (e.g. from a settings menu: <c>theme.SetTheme("Dark")</c>).</summary>
    [ExecuteAlways]
    [AddComponentMenu("Vectra/Vectra Theme")]
    public class VectraTheme : MonoBehaviour
    {
        [SerializeField] VectraPalette m_Palette;
        [SerializeField] string m_Theme = "Light";

        public VectraPalette Palette
        {
            get => m_Palette;
            set
            {
                m_Palette = value;
                Apply();
            }
        }

        public string Theme => m_Theme;

        public void SetTheme(string themeName)
        {
            m_Theme = themeName;
            Apply();
        }

        /// <summary>Cycles to the next theme of the palette.</summary>
        public void NextTheme()
        {
            if (m_Palette == null || m_Palette.themes.Count == 0) return;
            SetTheme(m_Palette.themes[(m_Palette.ThemeIndex + 1) % m_Palette.themes.Count]);
        }

        void OnEnable() => Apply();

        void OnValidate()
        {
            if (isActiveAndEnabled) Apply();
        }

        void Apply()
        {
            if (m_Palette == null) return;
            VectraPalette.Active = m_Palette;
            m_Palette.SetTheme(m_Theme);
        }
    }
}
