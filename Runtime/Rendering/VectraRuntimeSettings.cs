using FirstSymbol.Vectra.TextEngines;
using UnityEngine;

namespace FirstSymbol.Vectra.Rendering
{
    /// <summary>
    /// Settings a player needs. The editor keeps the project's values in <c>ProjectSettings/VectraSettings.asset</c>,
    /// which does not exist in a build; while a player is built the editor adds one of these assets to the
    /// Preloaded Assets (and removes it again afterwards), so the player starts with the same values.
    /// </summary>
    public sealed class VectraRuntimeSettings : ScriptableObject
    {
        public const int DefaultAtlasSize = 2048;
        public const int MinAtlasSize = 512;
        public const int MaxAtlasSize = 8192;

        [SerializeField, Tooltip("Largest size of the shared signed-distance atlas.")]
        int m_SdfAtlasMaxSize = DefaultAtlasSize;

        [SerializeField, Tooltip("Which engine draws SVG text. Auto is Lettera when installed, else TextMesh Pro, else the legacy uGUI Text.")]
        VectraTextEngine m_TextEngine = VectraTextEngine.Auto;

        [SerializeField, Tooltip("How SVG documents blend colours in a Linear project: like browsers and Figma (sRGB) or in linear light.")]
        SvgColorBlending m_SvgColorBlending = SvgColorBlending.Browser;

        /// <summary>
        /// The engine SVG components use when they follow the project. The editor sets it from the project settings;
        /// a player takes it from this asset (Auto when the project has none).
        /// </summary>
        public static VectraTextEngine ProjectTextEngine { get; set; } = VectraTextEngine.Auto;

        public VectraTextEngine TextEngine
        {
            get => m_TextEngine;
            set
            {
                m_TextEngine = value;
                Apply();
            }
        }

        public SvgColorBlending SvgColorBlending
        {
            get => m_SvgColorBlending;
            set
            {
                m_SvgColorBlending = value;
                Apply();
            }
        }

        public int SdfAtlasMaxSize
        {
            get => NormalizeAtlasSize(m_SdfAtlasMaxSize);
            set
            {
                m_SdfAtlasMaxSize = NormalizeAtlasSize(value);
                Apply();
            }
        }

        /// <summary>Nearest power of two between 512 and 8192.</summary>
        public static int NormalizeAtlasSize(int size) => Mathf.ClosestPowerOfTwo(Mathf.Clamp(size, MinAtlasSize, MaxAtlasSize));

        public static VectraRuntimeSettings Create(int sdfAtlasMaxSize, VectraTextEngine textEngine = VectraTextEngine.Auto, SvgColorBlending blending = SvgColorBlending.Browser)
        {
            var s = CreateInstance<VectraRuntimeSettings>();
            s.name = "Vectra Runtime Settings";
            s.m_TextEngine = textEngine;
            s.m_SvgColorBlending = blending;
            s.SdfAtlasMaxSize = sdfAtlasMaxSize;
            return s;
        }

        public void Apply()
        {
            VectraSdfAtlas.MaxSize = SdfAtlasMaxSize;
            ProjectTextEngine = m_TextEngine;
            VectraColorSpace.ProjectBlending = m_SvgColorBlending;
        }

        // Preloaded assets are enabled before the first scene loads, ahead of the first shape.
        void OnEnable() => Apply();
    }
}
