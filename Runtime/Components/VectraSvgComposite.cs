using FirstSymbol.Vectra.Rendering;
using UnityEngine;
using UnityEngine.UI;

namespace FirstSymbol.Vectra
{
    /// <summary>
    /// A composited part of a <see cref="VectraSvgImage"/>: elements with group opacity, a mask, a blend mode or a background blur,
    /// drawn by <see cref="SvgCompositor"/> into a texture at the screen's pixel density and shown as one quad. Created and
    /// destroyed by the image; not saved.
    /// </summary>
    [AddComponentMenu("")]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class VectraSvgComposite : MaskableGraphic
    {
        VectraSvgImage m_Owner;
        int m_From, m_To;
        // The picture may be shared with other components that draw the same pixels (SvgPictures).
        SvgPicture m_Picture;

        /// <summary>The composited picture (premultiplied alpha), null until it was rendered.</summary>
        public RenderTexture Texture => m_Picture?.texture;

        internal void Setup(VectraSvgImage owner, int from, int to)
        {
            m_Owner = owner;
            m_From = from;
            m_To = to;
            raycastTarget = false;
            SetAllDirty();
        }

        // The owner calls this when it changed; the picture is only drawn again when the size, density or overrides differ.
        internal void Refresh()
        {
            SetVerticesDirty();
            SetMaterialDirty();
        }

        public override Texture mainTexture
        {
            get
            {
                var t = m_Picture?.texture;
                return t != null ? t : Texture2D.whiteTexture;
            }
        }

        public override Material defaultMaterial => SvgCompositor.LayerMaterial ?? base.defaultMaterial;

        protected override void OnDestroy()
        {
            SvgPictures.Release(ref m_Picture);
            base.OnDestroy();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (m_Picture?.texture == null) return;
            var r = rectTransform.rect;
            var c = m_Owner != null ? m_Owner.color : color;
            vh.AddVert(new Vector3(r.xMin, r.yMin), c, new Vector2(0f, 0f));
            vh.AddVert(new Vector3(r.xMin, r.yMax), c, new Vector2(0f, 1f));
            vh.AddVert(new Vector3(r.xMax, r.yMax), c, new Vector2(1f, 1f));
            vh.AddVert(new Vector3(r.xMax, r.yMin), c, new Vector2(1f, 0f));
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(2, 3, 0);
        }

        protected override void UpdateGeometry()
        {
            if (m_Owner != null) m_Owner.RenderComposite(m_From, m_To, ref m_Picture);
            base.UpdateGeometry();
            canvasRenderer.SetMaterial(materialForRendering, mainTexture);
        }
    }
}
