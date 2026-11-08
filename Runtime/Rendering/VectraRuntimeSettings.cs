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

        public static VectraRuntimeSettings Create(int sdfAtlasMaxSize)
        {
            var s = CreateInstance<VectraRuntimeSettings>();
            s.name = "Vectra Runtime Settings";
            s.SdfAtlasMaxSize = sdfAtlasMaxSize;
            return s;
        }

        public void Apply() => VectraSdfAtlas.MaxSize = SdfAtlasMaxSize;

        // Preloaded assets are enabled before the first scene loads, ahead of the first shape.
        void OnEnable() => Apply();
    }
}
