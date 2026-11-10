using FirstSymbol.Vectra.Rendering;
using UnityEngine;
using UnityEngine.UI;

namespace FirstSymbol.Vectra
{
    /// <summary>
    /// A run of path elements of a <see cref="VectraSvgImage"/> that follows a text or an image in the document, drawn as a
    /// child so that it paints above that text. Created and destroyed by the image; not saved.
    /// </summary>
    [AddComponentMenu("")]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class VectraSvgLayer : MaskableGraphic, IVectraAtlasClient
    {
        VectraSvgImage m_Owner;
        int m_From, m_To;
        VectraShapeRenderer m_Renderer;
        Mesh m_Mesh;

        internal void Setup(VectraSvgImage owner, int from, int to)
        {
            m_Owner = owner;
            m_From = from;
            m_To = to;
            raycastTarget = false;
            SetAllDirty();
        }

        public override Material defaultMaterial =>
            (m_Owner != null ? VectraMaterials.GetUI(m_Owner.BlendMode) : null) ?? base.defaultMaterial;

        public override Material GetModifiedMaterial(Material baseMaterial)
        {
            var m = base.GetModifiedMaterial(baseMaterial);
            var soft = VectraMask.FindSoftMask(this);
            return soft != null ? soft.GetSoftMaterial(m) : m;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            VectraAtlases.Register(this);
        }

        protected override void OnDisable()
        {
            VectraAtlases.Unregister(this);
            base.OnDisable();
        }

        protected override void OnDestroy()
        {
            m_Renderer?.Dispose();
            m_Renderer = null;
            if (m_Mesh != null)
            {
                if (Application.isPlaying) Destroy(m_Mesh);
                else DestroyImmediate(m_Mesh);
            }

            base.OnDestroy();
        }

        void IVectraAtlasClient.OnAtlasInvalidated()
        {
            m_Renderer?.Invalidate();
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh) => vh.Clear();

        protected override void UpdateGeometry()
        {
            if (m_Owner == null) return;
            m_Renderer ??= new VectraShapeRenderer();
            if (m_Mesh == null) m_Mesh = new Mesh { name = "Vectra SVG layer", hideFlags = HideFlags.HideAndDontSave };
            m_Owner.BuildMesh(m_Renderer, m_Mesh, m_From, m_To);
            canvasRenderer.SetMesh(m_Mesh);
            if (m_LastTexture != m_Renderer.mainTexture)
            {
                m_LastTexture = m_Renderer.mainTexture;
                canvasRenderer.SetTexture(mainTexture);
            }
        }

        Texture m_LastTexture;

        public override Texture mainTexture
        {
            get
            {
                var t = m_Renderer?.mainTexture;
                return t != null ? t : s_WhiteTexture;
            }
        }
    }
}
