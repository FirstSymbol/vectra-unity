using UnityEngine;

namespace FirstSymbol.Vectra.URP
{
    public enum BackdropMode
    {
        FrostedGlass = 0,
        Overlay = 1,
        SoftLight = 2,
        HardLight = 3,
        ColorDodge = 4,
        ColorBurn = 5,
        Difference = 6,
        Exclusion = 7,
        Hue = 8,
        Saturation = 9,
        Color = 10,
        Luminosity = 11,
        Darken = 12,
        Lighten = 13,
        LinearBurn = 14,
        LinearDodge = 15
    }

    /// <summary>
    /// Composites a <see cref="VectraShape"/> with the scene behind it: frosted glass (background blur) or one of
    /// the advanced blend modes. Requires <see cref="VectraBackdropFeature"/> on the active URP renderer. Shapes
    /// with a backdrop use their own material, so they do not batch with ordinary Vectra shapes.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(VectraShape))]
    [AddComponentMenu("Vectra/Vectra Backdrop (URP)")]
    public class VectraBackdrop : MonoBehaviour
    {
        [SerializeField] BackdropMode m_Mode = BackdropMode.FrostedGlass;
        [Tooltip("Frosted glass: how much of the paint colour tints the blurred backdrop.")]
        [SerializeField, Range(0f, 1f)] float m_TintStrength = 0.25f;
        [SerializeField, Range(0f, 2f)] float m_Saturation = 1.2f;
        [SerializeField, Range(-1f, 1f)] float m_Brightness;

        static readonly int s_Mode = Shader.PropertyToID("_BackdropMode");
        static readonly int s_Tint = Shader.PropertyToID("_TintStrength");
        static readonly int s_Saturation = Shader.PropertyToID("_Saturation");
        static readonly int s_Brightness = Shader.PropertyToID("_Brightness");

        Material m_Material;
        VectraShape m_Shape;

        public BackdropMode Mode
        {
            get => m_Mode;
            set
            {
                m_Mode = value;
                Apply();
            }
        }

        void OnEnable()
        {
            m_Shape = GetComponent<VectraShape>();
            if (m_Material == null)
            {
                var shader = Resources.Load<Shader>("VectraURP/Vectra-UI-Backdrop") ?? Shader.Find("Vectra/UI Backdrop");
                if (shader == null) return;
                m_Material = new Material(shader) { name = "Vectra Backdrop", hideFlags = HideFlags.HideAndDontSave };
            }

            Apply();
            m_Shape.material = m_Material;
        }

        void OnDisable()
        {
            if (m_Shape != null && m_Shape.material == m_Material) m_Shape.material = null;
        }

        void OnDestroy()
        {
            if (m_Material != null) DestroyImmediate(m_Material);
        }

        void OnValidate() => Apply();

        void Apply()
        {
            if (m_Material == null) return;
            m_Material.SetFloat(s_Mode, (float)m_Mode);
            m_Material.SetFloat(s_Tint, m_TintStrength);
            m_Material.SetFloat(s_Saturation, m_Saturation);
            m_Material.SetFloat(s_Brightness, m_Brightness);
            if (m_Shape != null) m_Shape.SetMaterialDirty();
        }
    }
}
