using System.Collections.Generic;
using FirstSymbol.Vectra.Geometry;
using FirstSymbol.Vectra.Rendering;
using FirstSymbol.Vectra.Svg;
using Unity.Mathematics;
using UnityEngine;

namespace FirstSymbol.Vectra
{
    /// <summary>Draws a <see cref="VectraSvgDocument"/> with a MeshRenderer in world space.</summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [AddComponentMenu("Vectra/Vectra SVG Renderer (World)")]
    public class VectraSvgRenderer : MonoBehaviour, IVectraAtlasClient
    {
        [SerializeField] VectraSvgDocument m_Document;
        [Tooltip("Height of the document in world units; width follows the aspect ratio.")]
        [SerializeField] float m_Height = 1f;
        [SerializeField] Vector2 m_Pivot = new Vector2(0.5f, 0.5f);
        [SerializeField] Color m_Color = Color.white;
        [SerializeField] VectraQuality m_Quality = VectraQuality.High;
        [SerializeField] int m_SortingOrder;
        [SerializeField] List<SvgOverride> m_Overrides = new List<SvgOverride>();

        VectraShapeRenderer m_Renderer;
        Mesh m_Mesh;
        bool m_Dirty = true;
        float m_BuiltPixelSize;

        public VectraSvgDocument Document
        {
            get => m_Document;
            set
            {
                m_Document = value;
                m_Dirty = true;
            }
        }

        public List<SvgOverride> Overrides => m_Overrides;

        public void SetDirty() => m_Dirty = true;

        Rect LocalRect
        {
            get
            {
                float aspect = m_Document != null ? m_Document.AspectRatio : 1f;
                var size = new Vector2(m_Height * aspect, m_Height);
                return new Rect(-size.x * m_Pivot.x, -size.y * m_Pivot.y, size.x, size.y);
            }
        }

        void OnEnable()
        {
            VectraAtlases.Register(this);
            m_Dirty = true;
        }

        void OnDisable() => VectraAtlases.Unregister(this);

        void OnDestroy()
        {
            m_Renderer?.Dispose();
            if (m_Mesh != null) DestroyImmediate(m_Mesh);
        }

        void OnValidate() => m_Dirty = true;

        void IVectraAtlasClient.OnAtlasInvalidated()
        {
            m_Renderer?.Invalidate();
            m_Dirty = true;
        }

        float PixelSize()
        {
            var s = transform.lossyScale;
            return VectraRenderer.EstimatePixelSize(Camera.main, transform.position, math.max(math.abs(s.x), math.abs(s.y)));
        }

        void LateUpdate()
        {
            float px = PixelSize();
            if (!m_Dirty && m_BuiltPixelSize > 0f && (px > m_BuiltPixelSize * 1.5f || px < m_BuiltPixelSize / 1.5f)) m_Dirty = true;
            if (!m_Dirty) return;
            m_Dirty = false;
            m_BuiltPixelSize = px;
            m_Renderer ??= new VectraShapeRenderer();
            if (m_Mesh == null) m_Mesh = new Mesh { name = "Vectra SVG", hideFlags = HideFlags.HideAndDontSave };
            VectraSvgBuilder.Build(m_Document, m_Renderer, LocalRect, px, m_Color, true, m_Quality, m_Overrides);
            m_Renderer.buffer.Upload(m_Mesh);
            GetComponent<MeshFilter>().sharedMesh = m_Mesh;
            var mr = GetComponent<MeshRenderer>();
            mr.sharedMaterial = VectraMaterials.GetWorld();
            mr.sortingOrder = m_SortingOrder;
        }
    }
}
