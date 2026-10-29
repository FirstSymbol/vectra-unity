using System.Collections.Generic;
using FirstSymbol.Vectra.Geometry;
using FirstSymbol.Vectra.Rendering;
using FirstSymbol.Vectra.Svg;
using TMPro;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

namespace FirstSymbol.Vectra
{
    /// <summary>
    /// Draws a <see cref="VectraSvgDocument"/> in a uGUI Canvas as one batchable mesh. Text elements become
    /// TextMeshPro children and embedded images become RawImage children (both generated, not saved).
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(CanvasRenderer))]
    [AddComponentMenu("Vectra/Vectra SVG Image (UI)")]
    public class VectraSvgImage : MaskableGraphic, ILayoutElement, IVectraAtlasClient
    {
        [SerializeField] VectraSvgDocument m_Document;
        [SerializeField] bool m_PreserveAspect = true;
        [SerializeField] VectraQuality m_Quality = VectraQuality.High;
        [SerializeField] ShapeBlendMode m_BlendMode;
        [SerializeField] bool m_GenerateText = true;
        [SerializeField] TMP_FontAsset m_Font;
        [SerializeField] List<SvgOverride> m_Overrides = new List<SvgOverride>();

        VectraShapeRenderer m_Renderer;
        Mesh m_Mesh;
        readonly List<GameObject> m_Generated = new List<GameObject>();
        bool m_ChildrenDirty = true;

        public VectraSvgDocument Document
        {
            get => m_Document;
            set
            {
                if (m_Document == value) return;
                m_Document = value;
                m_ChildrenDirty = true;
                SetAllDirty();
            }
        }

        public List<SvgOverride> Overrides => m_Overrides;
        public VectraBuildStats Stats => m_Renderer?.stats ?? default;

        /// <summary>Loads SVG text at runtime (creates a document owned by this component).</summary>
        public void LoadSvg(string svgText)
        {
            Document = SvgParser.Parse(svgText);
        }

        public override Material defaultMaterial => VectraMaterials.GetUI(m_BlendMode) ?? base.defaultMaterial;

        protected override void OnEnable()
        {
            base.OnEnable();
            var c = canvas;
            if (c != null)
                c.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 |
                                              AdditionalCanvasShaderChannels.TexCoord2 |
                                              AdditionalCanvasShaderChannels.TexCoord3;
            VectraAtlases.Register(this);
            m_ChildrenDirty = true;
        }

        protected override void OnDisable()
        {
            VectraAtlases.Unregister(this);
            ClearGenerated();
            base.OnDisable();
        }

        protected override void OnDestroy()
        {
            m_Renderer?.Dispose();
            m_Renderer = null;
            if (m_Mesh != null) DestroyImmediate(m_Mesh);
            base.OnDestroy();
        }

        void IVectraAtlasClient.OnAtlasInvalidated()
        {
            m_Renderer?.Invalidate();
            SetVerticesDirty();
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            m_ChildrenDirty = true;
        }

        float PixelSize()
        {
            var c = canvas;
            if (c == null) return 1f;
            var root = c.rootCanvas;
            var s = transform.lossyScale;
            float local = math.max(math.abs(s.x), math.abs(s.y));
            if (root.renderMode == RenderMode.WorldSpace)
                return VectraRenderer.EstimatePixelSize(root.worldCamera != null ? root.worldCamera : Camera.main, transform.position, local);
            var rs = root.transform.lossyScale;
            float ppu = root.scaleFactor * local / math.max(math.max(math.abs(rs.x), math.abs(rs.y)), 1e-6f);
            return ppu > 1e-6f ? 1f / ppu : 1f;
        }

        protected override void OnPopulateMesh(VertexHelper vh) => vh.Clear();

        protected override void UpdateGeometry()
        {
            m_Renderer ??= new VectraShapeRenderer();
            if (m_Mesh == null) m_Mesh = new Mesh { name = "Vectra SVG", hideFlags = HideFlags.HideAndDontSave };
            VectraSvgBuilder.Build(m_Document, m_Renderer, rectTransform.rect, PixelSize(), color, false, m_Quality,
                m_Overrides, m_PreserveAspect ? (SvgAlign?)null : SvgAlign.None);
            m_Renderer.buffer.Upload(m_Mesh);
            canvasRenderer.SetMesh(m_Mesh);
        }

        void LateUpdate()
        {
            if (!m_ChildrenDirty) return;
            m_ChildrenDirty = false;
            RebuildChildren();
        }

        void ClearGenerated()
        {
            foreach (var go in m_Generated)
                if (go != null)
                {
                    if (Application.isPlaying) Destroy(go);
                    else DestroyImmediate(go);
                }

            m_Generated.Clear();
        }

        void RebuildChildren()
        {
            ClearGenerated();
            if (m_Document == null) return;
            var toLocal = m_Document.DocumentToLocal(rectTransform.rect, m_PreserveAspect ? (SvgAlign?)null : SvgAlign.None);
            float scale = toLocal.UniformScale;
            foreach (var e in m_Document.elements)
            {
                if (e.isText && m_GenerateText) CreateText(e, toLocal, scale);
                else if (e.isImage) CreateImage(e, toLocal);
            }
        }

        GameObject CreateChild(string name)
        {
            var go = new GameObject(name, typeof(RectTransform)) { hideFlags = HideFlags.DontSave | HideFlags.NotEditable };
            go.transform.SetParent(transform, false);
            m_Generated.Add(go);
            return go;
        }

        void CreateText(SvgElementData e, Affine2 toLocal, float scale)
        {
            var go = CreateChild("SVG Text");
            var t = go.AddComponent<TextMeshProUGUI>();
            if (m_Font != null) t.font = m_Font;
            t.text = e.text;
            t.fontSize = e.fontSize * scale;
            t.color = e.textColor * color;
            t.fontStyle = (e.bold ? FontStyles.Bold : 0) | (e.italic ? FontStyles.Italic : 0);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            var rt = (RectTransform)go.transform;
            float2 p = toLocal.Apply(e.textPosition);
            float pivotX = e.anchor == SvgTextAnchor.Middle ? 0.5f : e.anchor == SvgTextAnchor.End ? 1f : 0f;
            t.alignment = e.anchor == SvgTextAnchor.Middle ? TextAlignmentOptions.Baseline :
                e.anchor == SvgTextAnchor.End ? TextAlignmentOptions.BaselineRight : TextAlignmentOptions.BaselineLeft;
            rt.pivot = new Vector2(pivotX, 0f);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            var rect = rectTransform.rect;
            rt.sizeDelta = new Vector2(t.preferredWidth + 4f, t.fontSize * 1.5f);
            // SVG y is the baseline; TMP baseline alignment puts the baseline at the bottom of the box + descender.
            rt.anchoredPosition = new Vector2(p.x - rect.center.x, p.y - rect.center.y - t.fontSize * 0.25f);
            rt.localEulerAngles = new Vector3(0, 0, -e.textRotation);
        }

        void CreateImage(SvgElementData e, Affine2 toLocal)
        {
            if (e.image == null && e.imageData != null)
            {
                e.image = new Texture2D(2, 2) { hideFlags = HideFlags.DontSave };
                e.image.LoadImage(e.imageData);
            }

            if (e.image == null) return;
            var go = CreateChild("SVG Image");
            var img = go.AddComponent<RawImage>();
            img.texture = e.image;
            img.color = e.textColor * color;
            img.raycastTarget = false;
            var rt = (RectTransform)go.transform;
            float2 a = toLocal.Apply(new float2(e.imageRect.xMin, e.imageRect.yMin));
            float2 b = toLocal.Apply(new float2(e.imageRect.xMax, e.imageRect.yMax));
            var rect = rectTransform.rect;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(math.abs(b.x - a.x), math.abs(b.y - a.y));
            rt.anchoredPosition = new Vector2((a.x + b.x) * 0.5f - rect.center.x, (a.y + b.y) * 0.5f - rect.center.y);
        }

        public override void SetNativeSize()
        {
            if (m_Document == null) return;
            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, m_Document.size.x);
            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, m_Document.size.y);
        }

        public virtual void CalculateLayoutInputHorizontal() { }
        public virtual void CalculateLayoutInputVertical() { }
        public virtual float minWidth => 0f;
        public virtual float preferredWidth => m_Document != null ? m_Document.size.x : -1f;
        public virtual float flexibleWidth => -1f;
        public virtual float minHeight => 0f;
        public virtual float preferredHeight => m_Document != null ? m_Document.size.y : -1f;
        public virtual float flexibleHeight => -1f;
        public virtual int layoutPriority => 0;

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            m_ChildrenDirty = true;
            m_Renderer?.Invalidate();
        }
#endif
    }

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
