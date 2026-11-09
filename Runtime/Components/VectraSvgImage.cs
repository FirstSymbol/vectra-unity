using System.Collections.Generic;
using FirstSymbol.Vectra.Geometry;
using FirstSymbol.Vectra.Rendering;
using FirstSymbol.Vectra.Svg;
using FirstSymbol.Vectra.TextEngines;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

namespace FirstSymbol.Vectra
{
    /// <summary>
    /// Draws a <see cref="VectraSvgDocument"/> in a uGUI Canvas as one batchable mesh. Text elements become
    /// children of the project's text engine (Lettera, TextMesh Pro or the legacy uGUI Text) and embedded images
    /// become RawImage children (both generated, not saved).
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
        [Tooltip("Which engine draws the text of this document. Use Project Setting to follow Project Settings > Vectra.")]
        [SerializeField] VectraTextEngineChoice m_TextEngine;
        [Tooltip("Maps SVG font-family names to fonts of the text engines.")]
        [SerializeField] VectraFontMap m_FontMap;
        [Tooltip("TextMesh Pro font used when the font map has no match (empty = TextMesh Pro default).")]
        [SerializeField] Object m_Font;
        [Tooltip("Lettera font asset or font stack used when the font map has no match (empty = Lettera default).")]
        [SerializeField] Object m_LetteraFont;
        [SerializeField] List<SvgOverride> m_Overrides = new List<SvgOverride>();

        VectraShapeRenderer m_Renderer;
        Mesh m_Mesh;
        // Everything generated for the document, in paint order: texts, images and the mesh runs that follow them.
        sealed class Generated
        {
            public GameObject go;
            public SvgElementData element;
            public VectraSvgLayer layer;
            public bool isText;
        }

        readonly List<Generated> m_Generated = new List<Generated>();
        readonly List<SvgDrawItem> m_Items = new List<SvgDrawItem>();
        int m_Signature, m_BuiltSignature;
        bool m_ChildrenDirty = true;
        bool m_LayoutDirty;
        bool m_LayersDirty;
        Vector2 m_LaidOutSize;
        IVectraTextBackend m_Backend;
        VectraTextEngine m_WarnedFor = (VectraTextEngine)(-1);
        VectraSvgDocument m_WarnedDocument;

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
        internal ShapeBlendMode BlendMode => m_BlendMode;
        public VectraBuildStats Stats => m_Renderer?.stats ?? default;

        public bool PreserveAspect
        {
            get => m_PreserveAspect;
            set
            {
                m_PreserveAspect = value;
                m_ChildrenDirty = true;
                SetVerticesDirty();
            }
        }

        public VectraFontMap FontMap
        {
            get => m_FontMap;
            set
            {
                m_FontMap = value;
                m_ChildrenDirty = true;
            }
        }

        /// <summary>The text engine this component asks for; <see cref="VectraTextEngineChoice.UseProjectSetting"/> follows the project.</summary>
        public VectraTextEngineChoice TextEngine
        {
            get => m_TextEngine;
            set
            {
                if (m_TextEngine == value) return;
                m_TextEngine = value;
                m_ChildrenDirty = true;
            }
        }

        /// <summary>The engine asked for after the project setting is applied (may still be Auto).</summary>
        public VectraTextEngine RequestedTextEngine => VectraTextEngineChoices.Effective(m_TextEngine, VectraRuntimeSettings.ProjectTextEngine);

        /// <summary>TextMesh Pro font used when the font map has no match.</summary>
        public Object FallbackFont
        {
            get => m_Font;
            set
            {
                m_Font = value;
                m_ChildrenDirty = true;
            }
        }

        /// <summary>Lettera font asset or stack used when the font map has no match.</summary>
        public Object LetteraFallbackFont
        {
            get => m_LetteraFont;
            set
            {
                m_LetteraFont = value;
                m_ChildrenDirty = true;
            }
        }

        /// <summary>The backend that draws this document's text now, and whether it is not the one asked for.</summary>
        public VectraTextChoice ResolveTextBackend() =>
            VectraTextBackends.Resolve(RequestedTextEngine, m_FontMap, m_Font, m_LetteraFont);

        /// <summary>Loads SVG text at runtime (creates a document owned by this component).</summary>
        public void LoadSvg(string svgText)
        {
            Document = SvgParser.Parse(svgText);
        }

        public override Material defaultMaterial => VectraMaterials.GetUI(m_BlendMode) ?? base.defaultMaterial;

        public override Material GetModifiedMaterial(Material baseMaterial)
        {
            var m = base.GetModifiedMaterial(baseMaterial);
            var soft = VectraMask.FindSoftMask(this);
            return soft != null ? soft.GetSoftMaterial(m) : m;
        }

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

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            // Texts and images move to their new places; nothing is destroyed.
            m_LayoutDirty = true;
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
            m_Signature = SvgDrawOrder.Split(m_Document, m_GenerateText, true, m_Items);
            SvgDrawOrder.MainRange(m_Items, out int from, out int to);
            BuildMesh(m_Renderer, m_Mesh, from, to);
            canvasRenderer.SetMesh(m_Mesh);
            m_LayersDirty = m_Generated.Count > 0;
        }

        /// <summary>Builds the path elements in [from, to) of the document into <paramref name="mesh"/>.</summary>
        internal void BuildMesh(VectraShapeRenderer renderer, Mesh mesh, int from, int to)
        {
            VectraSvgBuilder.Build(m_Document, renderer, rectTransform.rect, PixelSize(), color, false, m_Quality,
                m_Overrides, m_PreserveAspect ? (SvgAlign?)null : SvgAlign.None, null, from, to);
            renderer.buffer.Upload(mesh);
        }

        void LateUpdate() => RefreshGenerated();

        /// <summary>Brings the generated children up to date: rebuilds them when the document changed, otherwise only moves them.</summary>
        internal void RefreshGenerated()
        {
            // The layout of the document changed (elements added or reordered): the generated objects no longer match it.
            if (m_Signature != m_BuiltSignature) m_ChildrenDirty = true;
            if (rectTransform.rect.size != m_LaidOutSize) m_LayoutDirty = true;
            if (m_ChildrenDirty)
            {
                m_ChildrenDirty = false;
                m_LayoutDirty = false;
                RebuildChildren();
            }
            else if (m_LayoutDirty)
            {
                m_LayoutDirty = false;
                LayoutChildren();
            }

            if (m_LayersDirty)
            {
                m_LayersDirty = false;
                foreach (var g in m_Generated)
                    if (g.layer != null) g.layer.SetVerticesDirty();
            }
        }

        void ClearGenerated()
        {
            foreach (var g in m_Generated)
                if (g.go != null)
                {
                    if (Application.isPlaying) Destroy(g.go);
                    else DestroyImmediate(g.go);
                }

            m_Generated.Clear();
            m_Backend = null;
        }

        /// <summary>Rebuilds the generated text and image children now (otherwise it happens in the next LateUpdate).</summary>
        public void RebuildGenerated()
        {
            m_ChildrenDirty = false;
            m_LayoutDirty = false;
            RebuildChildren();
        }

        void RebuildChildren()
        {
            ClearGenerated();
            m_BuiltSignature = SvgDrawOrder.Split(m_Document, m_GenerateText, true, m_Items);
            m_Signature = m_BuiltSignature;
            if (m_Document == null) return;
            VectraTextChoice choice = default;
            bool chosen = false;
            for (int i = 0; i < m_Items.Count; i++)
            {
                var item = m_Items[i];
                if (!item.overlay)
                {
                    if (i > 0) CreateLayer(item.from, item.to); // the first run, when it opens the document, is the component's own mesh
                    continue;
                }

                var e = m_Document.elements[item.from];
                if (e.isText)
                {
                    if (!chosen)
                    {
                        choice = ResolveTextBackend();
                        chosen = true;
                        WarnOnce(choice);
                        m_Backend = choice.backend;
                    }

                    if (choice.backend != null) CreateChild("SVG Text", e, true);
                }
                else CreateImage(e);
            }

            LayoutChildren();
            m_LayersDirty = true;
        }

        // Puts every generated text and image where the current rect says. Safe to repeat; creates nothing.
        void LayoutChildren()
        {
            if (m_Document == null) return;
            var rect = rectTransform.rect;
            m_LaidOutSize = rect.size;
            var toLocal = m_Document.DocumentToLocal(rect, m_PreserveAspect ? (SvgAlign?)null : SvgAlign.None);
            float scale = toLocal.UniformScale;
            foreach (var g in m_Generated)
            {
                if (g.go == null || g.element == null) continue;
                if (g.isText)
                {
                    if (m_Backend != null) ConfigureText(m_Backend, g.go, g.element, toLocal, scale, rect);
                }
                else PlaceImage(g.go, g.element, toLocal, rect);
            }
        }

        // One console line per document and engine, not one per rebuild.
        void WarnOnce(VectraTextChoice choice)
        {
            if (!choice.fellBack && choice.backend != null) return;
            if (choice.requested == VectraTextEngine.None) return;
            if (m_WarnedDocument == m_Document && m_WarnedFor == choice.requested) return;
            m_WarnedDocument = m_Document;
            m_WarnedFor = choice.requested;
            if (choice.backend == null)
                Debug.LogWarning($"Vectra: no text engine can draw the text of '{m_Document.name}' (asked for {choice.requested}); the text is skipped.", this);
            else
                Debug.LogWarning($"Vectra: the {choice.requested} text engine is not available for '{m_Document.name}'; using {choice.Used}.", this);
        }

        GameObject CreateChild(string name, SvgElementData element, bool isText)
        {
            var go = new GameObject(name, typeof(RectTransform)) { hideFlags = HideFlags.DontSave | HideFlags.NotEditable };
            go.transform.SetParent(transform, false);
            m_Generated.Add(new Generated { go = go, element = element, isText = isText });
            return go;
        }

        void CreateLayer(int from, int to)
        {
            var go = new GameObject("SVG Layer", typeof(RectTransform)) { hideFlags = HideFlags.DontSave | HideFlags.NotEditable };
            var rt = (RectTransform)go.transform;
            rt.SetParent(transform, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var layer = go.AddComponent<VectraSvgLayer>();
            layer.Setup(this, from, to);
            m_Generated.Add(new Generated { go = go, layer = layer });
        }

        /// <summary>Sets up a text object of <paramref name="backend"/> for an SVG text element placed in a rect of this image's size.</summary>
        internal void ConfigureText(IVectraTextBackend backend, GameObject go, SvgElementData e, Affine2 toLocal, float scale, Rect rect)
        {
            float2 p = toLocal.Apply(e.textPosition);
            var fallback = VectraTextBackends.FontFor(backend, m_Font, m_LetteraFont);
            var spec = new VectraTextSpec
            {
                element = e,
                fontSize = e.fontSize * scale,
                tint = color,
                color = e.textColor * color,
                origin = new Vector2(p.x - rect.center.x, p.y - rect.center.y),
                rotation = -e.textRotation,
                fontMap = m_FontMap,
                fallbackFont = fallback
            };
            backend.Build(go, spec);
        }

        void CreateImage(SvgElementData e)
        {
            if (e.image == null && e.imageData != null)
            {
                e.image = new Texture2D(2, 2) { hideFlags = HideFlags.DontSave };
                e.image.LoadImage(e.imageData);
            }

            if (e.image == null) return;
            var go = CreateChild("SVG Image", e, false);
            var img = go.AddComponent<RawImage>();
            img.texture = e.image;
            img.raycastTarget = false;
        }

        void PlaceImage(GameObject go, SvgElementData e, Affine2 toLocal, Rect rect)
        {
            var img = go.GetComponent<RawImage>();
            if (img != null) img.color = e.textColor * color;
            var rt = (RectTransform)go.transform;
            float2 a = toLocal.Apply(new float2(e.imageRect.xMin, e.imageRect.yMin));
            float2 b = toLocal.Apply(new float2(e.imageRect.xMax, e.imageRect.yMax));
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
}
