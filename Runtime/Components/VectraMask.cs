using System.Collections.Generic;
using FirstSymbol.Vectra.Geometry;
using FirstSymbol.Vectra.Rendering;
using UnityEngine;
using UnityEngine.UI;

namespace FirstSymbol.Vectra
{
    public enum VectraMaskMode
    {
        /// <summary>Stencil mask that follows the outline of the shape. Works for every UI graphic.</summary>
        Stencil,
        /// <summary>Feathered mask evaluated from the distance function. Applies to child Vectra graphics only.</summary>
        Soft
    }

    /// <summary>
    /// Masks the children of a <see cref="VectraShape"/> by its outline. Unlike <see cref="Mask"/> on an Image, the
    /// stencil follows the exact outline (not the quad). The soft mode feathers the edge and can be inverted; it is
    /// evaluated per pixel by the Vectra shader, so it masks <see cref="VectraShape"/> and
    /// <see cref="VectraSvgImage"/> children (other graphics are not affected).
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VectraShape))]
    [AddComponentMenu("Vectra/Vectra Mask")]
    public class VectraMask : Mask
    {
        [SerializeField] VectraMaskMode m_Mode = VectraMaskMode.Stencil;
        [Tooltip("Width of the feathered edge in local units (soft mode).")]
        [SerializeField, Min(0f)] float m_Softness = 8f;
        [Tooltip("Moves the mask edge inwards (positive) or outwards (negative), in local units (soft mode).")]
        [SerializeField] float m_Inset;
        [Tooltip("Show the children outside the shape instead of inside (soft mode).")]
        [SerializeField] bool m_Invert;

        static readonly int MatrixId = Shader.PropertyToID("_VectraMaskMatrix");
        static readonly int S1Id = Shader.PropertyToID("_VectraMaskS1");
        static readonly int S2Id = Shader.PropertyToID("_VectraMaskS2");
        static readonly int ParamsId = Shader.PropertyToID("_VectraMaskParams");
        static readonly int ColorMaskId = Shader.PropertyToID("_ColorMask");
        const string SoftKeyword = "VECTRA_SOFT_MASK";
        const string ClipKeyword = "UNITY_UI_ALPHACLIP";

        static readonly List<VectraMask> s_SoftMasks = new List<VectraMask>();
        static readonly List<Graphic> s_Graphics = new List<Graphic>();

        readonly Dictionary<Material, Material> m_SoftMaterials = new Dictionary<Material, Material>();
        Material m_ClipSource, m_ClipMaterial, m_PopSource, m_PopMaterial, m_HiddenSource, m_HiddenMaterial;
        VectraShape m_Shape;
        Matrix4x4 m_LastMatrix;
        VectraDistanceShape m_LastDistance;

        public VectraMaskMode Mode
        {
            get => m_Mode;
            set
            {
                if (m_Mode == value) return;
                m_Mode = value;
                Refresh();
            }
        }

        public float Softness
        {
            get => m_Softness;
            set
            {
                m_Softness = Mathf.Max(0f, value);
                RefreshSoft();
            }
        }

        public float Inset
        {
            get => m_Inset;
            set
            {
                m_Inset = value;
                RefreshSoft();
            }
        }

        public bool Invert
        {
            get => m_Invert;
            set
            {
                m_Invert = value;
                RefreshSoft();
            }
        }

        public bool IsSoft => m_Mode == VectraMaskMode.Soft && isActiveAndEnabled;

        VectraShape Shape => m_Shape != null ? m_Shape : m_Shape = GetComponent<VectraShape>();

        public override bool MaskEnabled() => m_Mode == VectraMaskMode.Stencil && base.MaskEnabled();

        protected override void OnEnable()
        {
            base.OnEnable();
            if (!s_SoftMasks.Contains(this)) s_SoftMasks.Add(this);
            Refresh();
        }

        protected override void OnDisable()
        {
            s_SoftMasks.Remove(this);
            base.OnDisable();
            DirtyChildren();
            Shape?.SetVerticesDirty();
            ReleaseMaterials();
        }

        protected override void OnDestroy()
        {
            ReleaseMaterials();
            base.OnDestroy();
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            if (isActiveAndEnabled) Refresh();
        }
#endif

        void Refresh()
        {
            ReleaseMaterials();
            Shape?.SetMaterialDirty();
            Shape?.SetVerticesDirty();
            DirtyChildren();
        }

        void RefreshSoft()
        {
            if (!IsSoft) return;
            Shape?.SetVerticesDirty();
            m_LastDistance = default;
        }

        void DirtyChildren()
        {
            GetComponentsInChildren(true, s_Graphics);
            foreach (var g in s_Graphics)
                if (g != null && g.gameObject != gameObject) g.SetMaterialDirty();
            s_Graphics.Clear();
        }

        public override Material GetModifiedMaterial(Material baseMaterial)
        {
            if (m_Mode == VectraMaskMode.Soft)
            {
                if (!isActiveAndEnabled || showMaskGraphic) return baseMaterial;
                // Hidden mask graphic: keep the draw (so the mesh stays valid) but write no colour.
                if (m_HiddenSource != baseMaterial || m_HiddenMaterial == null)
                {
                    Destroy(ref m_HiddenMaterial);
                    m_HiddenSource = baseMaterial;
                    m_HiddenMaterial = new Material(baseMaterial) { hideFlags = HideFlags.HideAndDontSave, name = "Vectra Mask (hidden)" };
                    m_HiddenMaterial.SetFloat(ColorMaskId, 0f);
                }

                return m_HiddenMaterial;
            }

            var m = base.GetModifiedMaterial(baseMaterial);
            if (m == baseMaterial) return m;

            // Alpha-clipped copies: the stencil is written (and later cleared) only where the outline is drawn.
            if (m_ClipSource != m || m_ClipMaterial == null)
            {
                Destroy(ref m_ClipMaterial);
                m_ClipSource = m;
                m_ClipMaterial = ClipCopy(m);
            }

            var cr = graphic != null ? graphic.canvasRenderer : null;
            if (cr != null && cr.popMaterialCount > 0)
            {
                var pop = cr.GetPopMaterial(0);
                if (pop != null)
                {
                    if (m_PopSource != pop || m_PopMaterial == null)
                    {
                        Destroy(ref m_PopMaterial);
                        m_PopSource = pop;
                        m_PopMaterial = ClipCopy(pop);
                    }

                    cr.SetPopMaterial(m_PopMaterial, 0);
                }
            }

            return m_ClipMaterial;
        }

        static Material ClipCopy(Material m)
        {
            var copy = new Material(m) { hideFlags = HideFlags.HideAndDontSave, name = m.name + " (Vectra clip)" };
            copy.EnableKeyword(ClipKeyword);
            return copy;
        }

        /// <summary>The nearest active soft mask above <paramref name="graphic"/> (not on the graphic itself).</summary>
        public static VectraMask FindSoftMask(Graphic graphic)
        {
            if (s_SoftMasks.Count == 0 || graphic == null) return null;
            var t = graphic.transform.parent;
            while (t != null)
            {
                for (int i = 0; i < s_SoftMasks.Count; i++)
                {
                    var m = s_SoftMasks[i];
                    if (m != null && m.transform == t && m.IsSoft) return m;
                }

                t = t.parent;
            }

            return null;
        }

        /// <summary>Material of a child graphic under this soft mask (shared by all children with the same base).</summary>
        public Material GetSoftMaterial(Material baseMaterial)
        {
            if (baseMaterial == null) return null;
            if (!m_SoftMaterials.TryGetValue(baseMaterial, out var m) || m == null)
            {
                m = new Material(baseMaterial) { hideFlags = HideFlags.HideAndDontSave, name = baseMaterial.name + " (Vectra soft mask)" };
                m.EnableKeyword(SoftKeyword);
                m_SoftMaterials[baseMaterial] = m;
                Apply(m, CurrentMatrix(), Shape != null ? Shape.DistanceShape : default);
            }

            return m;
        }

        Matrix4x4 CurrentMatrix()
        {
            var shape = Shape;
            if (shape == null || shape.canvas == null) return Matrix4x4.identity;
            var d = shape.DistanceShape;
            var a = d.localToShape;
            var toShape = new Matrix4x4(
                new Vector4(a.a, a.b, 0f, 0f),
                new Vector4(a.c, a.d, 0f, 0f),
                new Vector4(0f, 0f, 1f, 0f),
                new Vector4(a.e, a.f, 0f, 1f));
            return toShape * transform.worldToLocalMatrix * shape.canvas.rootCanvas.transform.localToWorldMatrix;
        }

        void Apply(Material m, in Matrix4x4 matrix, in VectraDistanceShape d)
        {
            m.SetMatrix(MatrixId, matrix);
            if (!d.valid)
            {
                // Nothing to evaluate yet (kind 31 is "far outside"): leave the children visible.
                m.SetVector(S1Id, new Vector4(0f, 0f, 0f, 0f));
                m.SetVector(S2Id, Vector4.zero);
                m.SetVector(ParamsId, new Vector4(0f, 0f, m_Invert ? 0f : 1f, 31f));
                return;
            }

            float scale = d.localToShape.UniformScale;
            m.SetVector(S1Id, d.s1);
            m.SetVector(S2Id, d.s2);
            m.SetVector(ParamsId, new Vector4(m_Softness * scale, m_Inset * scale, m_Invert ? 1f : 0f, (int)d.kind | (d.style << 5)));
        }

        void LateUpdate()
        {
            if (!IsSoft || m_SoftMaterials.Count == 0) return;
            var shape = Shape;
            if (shape == null) return;
            var matrix = CurrentMatrix();
            var dist = shape.DistanceShape;
            if (matrix == m_LastMatrix && SameDistance(dist, m_LastDistance)) return;
            m_LastMatrix = matrix;
            m_LastDistance = dist;
            foreach (var kv in m_SoftMaterials)
                if (kv.Value != null) Apply(kv.Value, matrix, dist);
        }

        static bool SameDistance(in VectraDistanceShape a, in VectraDistanceShape b) =>
            a.valid == b.valid && a.kind == b.kind && a.style == b.style && a.s1.Equals(b.s1) && a.s2.Equals(b.s2) &&
            a.localToShape.Equals(b.localToShape);

        void ReleaseMaterials()
        {
            foreach (var kv in m_SoftMaterials)
            {
                var m = kv.Value;
                Destroy(ref m);
            }

            m_SoftMaterials.Clear();
            Destroy(ref m_ClipMaterial);
            Destroy(ref m_PopMaterial);
            Destroy(ref m_HiddenMaterial);
            m_ClipSource = m_PopSource = m_HiddenSource = null;
            m_LastDistance = default;
        }

        static void Destroy(ref Material m)
        {
            if (m == null) return;
            if (Application.isPlaying) Object.Destroy(m);
            else Object.DestroyImmediate(m);
            m = null;
        }
    }
}
