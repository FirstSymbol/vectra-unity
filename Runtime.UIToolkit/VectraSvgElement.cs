using System.Collections.Generic;
using FirstSymbol.Vectra.Geometry;
using FirstSymbol.Vectra.Rendering;
using FirstSymbol.Vectra.Svg;
using FirstSymbol.Vectra.TextEngines;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Vectra.UIToolkit
{
    /// <summary>A mesh step of an SVG document that follows a text: its own child element, so paint order holds.</summary>
    sealed class VectraSvgLayerElement : VisualElement
    {
        public VectraRasterLayer layer;

        public VectraSvgLayerElement()
        {
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = style.top = style.right = style.bottom = 0;
            generateVisualContent = OnGenerate;
        }

        void OnGenerate(MeshGenerationContext mgc)
        {
            if (layer != null && layer.HasImage) layer.Emit(mgc, new Color32(255, 255, 255, 255));
        }
    }

    /// <summary>
    /// Draws a <see cref="VectraSvgDocument"/> in UI Toolkit. Shapes are drawn like a <see cref="VectraElement"/> (the
    /// run of shapes before the first text is the element's own picture; every later run is a child so it paints over
    /// the text before it). Text is drawn by UI Toolkit labels with the panel's font; Lettera and TextMesh Pro have no
    /// UI Toolkit element, so the text-engine seam is not used here.
    /// </summary>
    [UxmlElement("VectraSvgElement")]
    public partial class VectraSvgElement : VisualElement, IVectraAtlasClient
    {
        public const string ussClassName = "vectra-svg-element";

        /// <summary>Distance from the top of a label to its baseline, as a fraction of the font size.</summary>
        internal const float BaselineRatio = 0.97f;

        VectraSvgDocument m_Document;
        bool m_PreserveAspect = true;
        VectraQuality m_Quality = VectraQuality.High;
        bool m_GenerateText = true;
        SvgColorBlendingChoice m_ColorBlending;
        readonly List<SvgOverride> m_Overrides = new List<SvgOverride>();
        readonly List<SvgDrawItem> m_Items = new List<SvgDrawItem>();
        readonly List<VectraRasterLayer> m_Layers = new List<VectraRasterLayer>();
        readonly List<VisualElement> m_Generated = new List<VisualElement>();
        VectraRasterLayer m_Own;
        IVisualElementScheduledItem m_Rebuild, m_Poll;
        bool m_Dirty = true, m_Scheduled, m_Structure = true;
        int m_Signature;
        Vector2 m_BuiltSize;
        int m_BuiltVersion;

        public int RasterCount { get; private set; }

        /// <summary>Number of child elements generated for the document (later shape runs and texts).</summary>
        public int GeneratedCount => m_Generated.Count;

        public VectraSvgElement()
        {
            AddToClassList(ussClassName);
            AddToClassList(VectraElement.ussClassName);
            var sheet = Resources.Load<StyleSheet>("Vectra/VectraElement");
            if (sheet != null) styleSheets.Add(sheet);
            generateVisualContent = OnGenerate;
            RegisterCallback<AttachToPanelEvent>(OnAttach);
            RegisterCallback<DetachFromPanelEvent>(OnDetach);
            RegisterCallback<GeometryChangedEvent>(OnGeometry);
        }

        [UxmlAttribute("document")]
        public VectraSvgDocument Document
        {
            get => m_Document;
            set
            {
                if (m_Document == value) return;
                m_Document = value;
                m_Structure = true;
                SetDirty();
            }
        }

        [UxmlAttribute("preserve-aspect")]
        public bool PreserveAspect
        {
            get => m_PreserveAspect;
            set
            {
                if (m_PreserveAspect == value) return;
                m_PreserveAspect = value;
                m_Structure = true;
                SetDirty();
            }
        }

        [UxmlAttribute("quality")]
        public VectraQuality Quality
        {
            get => m_Quality;
            set
            {
                if (m_Quality == value) return;
                m_Quality = value;
                SetDirty();
            }
        }

        /// <summary>Draw the document's text with UI Toolkit labels.</summary>
        [UxmlAttribute("generate-text")]
        public bool GenerateText
        {
            get => m_GenerateText;
            set
            {
                if (m_GenerateText == value) return;
                m_GenerateText = value;
                m_Structure = true;
                SetDirty();
            }
        }

        /// <summary>Colour and visibility overrides by selector; call <see cref="SetDirty"/> after editing.</summary>
        public List<SvgOverride> Overrides => m_Overrides;

        /// <summary>In a Linear colour space project: blend like browsers and Figma (sRGB) or in linear light; follows the project setting by default.</summary>
        public SvgColorBlendingChoice ColorBlending
        {
            get => m_ColorBlending;
            set
            {
                if (m_ColorBlending == value) return;
                m_ColorBlending = value;
                m_Structure = true;
                SetDirty();
            }
        }

        bool Gamma => VectraColorSpace.UseGamma(m_ColorBlending);

        /// <summary>Call after changing the document's content or the overrides from code.</summary>
        public void SetDirty()
        {
            m_Dirty = true;
            Schedule();
        }

        // ----- lifecycle -----

        void OnAttach(AttachToPanelEvent evt)
        {
            VectraAtlases.Register(this);
            VectraUitk.Register(this);
            m_Rebuild = schedule.Execute(RebuildNow);
            m_Poll = schedule.Execute(Poll).Every(250);
            m_Dirty = m_Structure = true;
            m_Scheduled = false;
            Schedule();
        }

        void OnDetach(DetachFromPanelEvent evt)
        {
            VectraAtlases.Unregister(this);
            VectraUitk.Unregister(this);
            m_Rebuild?.Pause();
            m_Poll?.Pause();
            m_Rebuild = m_Poll = null;
            m_Scheduled = false;
            ClearGenerated();
        }

        void OnGeometry(GeometryChangedEvent evt)
        {
            if (contentRect.size == m_BuiltSize) return;
            SetDirty();
        }

        void IVectraAtlasClient.OnAtlasInvalidated()
        {
            foreach (var l in m_Layers) l.renderer.Invalidate();
            SetDirty();
        }

        void Schedule()
        {
            if (m_Scheduled || m_Rebuild == null) return;
            m_Scheduled = true;
            m_Rebuild.ExecuteLater(0);
        }

        void Poll()
        {
            if (m_Dirty) return;
            if (m_Document != null && m_Document.contentVersion != m_BuiltVersion)
            {
                m_BuiltVersion = m_Document.contentVersion;
                m_Structure = true;
                SetDirty();
                return;
            }

            float built = m_Layers.Count > 0 ? m_Layers[0].builtScale : 0f;
            bool alive = true;
            foreach (var l in m_Layers) alive &= l.IsAlive;
            foreach (var g in m_Generated)
                if (g is VectraSvgCompositeElement c)
                {
                    alive &= c.IsAlive;
                    if (!(built > 0f)) built = c.builtScale;
                }

            if (!(built > 0f)) return;
            float now = VectraUitk.Density(this);
            if (now > built * 1.25f || now < built * 0.8f || !alive) SetDirty();
        }

        void ClearGenerated()
        {
            foreach (var g in m_Generated)
            {
                if (g is VectraSvgCompositeElement composite) composite.Release();
                g.RemoveFromHierarchy();
            }

            m_Generated.Clear();
            foreach (var l in m_Layers) l.Dispose();
            m_Layers.Clear();
            m_Own = null;
            m_Signature = 0;
            m_Structure = true;
        }

        // ----- building -----

        void BuildStructure()
        {
            ClearGenerated();
            m_Structure = false;
            if (m_Items.Count > 0 && !m_Items[0].overlay)
            {
                m_Own = new VectraRasterLayer();
                m_Layers.Add(m_Own);
            }

            for (int i = 0; i < m_Items.Count; i++)
            {
                var item = m_Items[i];
                if (item.composite)
                {
                    var composite = new VectraSvgCompositeElement();
                    hierarchy.Add(composite);
                    m_Generated.Add(composite);
                    continue;
                }

                if (!item.overlay)
                {
                    if (i == 0) continue;
                    var layer = new VectraRasterLayer();
                    m_Layers.Add(layer);
                    var host = new VectraSvgLayerElement { layer = layer };
                    hierarchy.Add(host);
                    m_Generated.Add(host);
                }
                else
                {
                    var label = new Label { pickingMode = PickingMode.Ignore };
                    var s = label.style;
                    s.position = Position.Absolute;
                    s.marginLeft = s.marginRight = s.marginTop = s.marginBottom = 0;
                    s.paddingLeft = s.paddingRight = s.paddingTop = s.paddingBottom = 0;
                    s.whiteSpace = WhiteSpace.NoWrap;
                    s.unityTextAlign = TextAnchor.UpperLeft;
                    hierarchy.Add(label);
                    m_Generated.Add(label);
                }
            }
        }

        /// <summary>Builds and rasterizes the document now instead of before the next repaint.</summary>
        public void RebuildNow()
        {
            m_Scheduled = false;
            m_Dirty = false;
            if (panel == null) return;
            var cr = contentRect;
            m_BuiltSize = cr.size;
            m_BuiltVersion = m_Document != null ? m_Document.contentVersion : 0;
            if (m_Document == null || !VectraUitk.Usable(cr))
            {
                foreach (var l in m_Layers) l.Release();
                MarkDirtyRepaint();
                return;
            }

            int signature = SvgDrawOrder.Split(m_Document, m_GenerateText, false, m_Items, Gamma);
            if (signature != m_Signature || m_Structure)
            {
                BuildStructure();
                m_Signature = signature;
            }

            float density = VectraUitk.Density(this);
            var rect = new Rect(-cr.width * 0.5f, -cr.height * 0.5f, cr.width, cr.height);
            SvgAlign? align = m_PreserveAspect ? (SvgAlign?)null : SvgAlign.None;
            int layerIndex = 0, childIndex = 0;
            var toLocal = m_Document.DocumentToLocal(rect, align);
            float scale = toLocal.UniformScale;
            for (int i = 0; i < m_Items.Count; i++)
            {
                var item = m_Items[i];
                if (item.composite)
                {
                    ((VectraSvgCompositeElement)m_Generated[childIndex++]).Render(m_Document, item.from, item.to, rect, cr, density, m_Quality, m_Overrides, align, Gamma);
                    continue;
                }

                if (!item.overlay)
                {
                    var layer = m_Layers[layerIndex++];
                    VectraSvgBuilder.Build(m_Document, layer.renderer, rect, 1f / density, Color.white, true, m_Quality, m_Overrides, align, null, item.from, item.to, density);
                    layer.Rasterize(cr.center, density);
                    if (i != 0) ((VectraSvgLayerElement)m_Generated[childIndex++]).MarkDirtyRepaint();
                    continue;
                }

                var label = (Label)m_Generated[childIndex++];
                ConfigureLabel(label, m_Document.elements[item.from], toLocal, scale, cr.center);
            }

            RasterCount++;
            MarkDirtyRepaint();
        }

        static void ConfigureLabel(Label label, SvgElementData e, Affine2 toLocal, float scale, Vector2 center)
        {
            var p = toLocal.Apply(e.textPosition);
            float fs = Mathf.Max(e.fontSize * scale, 1f);
            float ax = e.anchor == SvgTextAnchor.Middle ? 50f : e.anchor == SvgTextAnchor.End ? 100f : 0f;
            float asc = fs * BaselineRatio;
            bool styled = VectraRichText.HasStyledRuns(e);
            label.enableRichText = styled;
            label.text = styled ? VectraRichText.Build(e, VectraMarkupDialect.TextMeshPro, Color.white, scale, e.bold, e.italic) : (e.text ?? "");
            var s = label.style;
            s.left = center.x + p.x;
            s.top = center.y - p.y - asc;
            s.fontSize = fs;
            s.color = e.textColor;
            s.unityFontStyleAndWeight = e.bold && e.italic ? FontStyle.BoldAndItalic : e.bold ? FontStyle.Bold : e.italic ? FontStyle.Italic : FontStyle.Normal;
            s.translate = new Translate(Length.Percent(-ax), 0f);
            s.transformOrigin = new TransformOrigin(Length.Percent(ax), asc);
            s.rotate = new Rotate(new Angle(-e.textRotation, AngleUnit.Degree));
        }

        void OnGenerate(MeshGenerationContext mgc)
        {
            if (m_Own != null && m_Own.HasImage && m_Items.Count > 0 && !m_Items[0].overlay)
                m_Own.Emit(mgc, new Color32(255, 255, 255, 255));
        }
    }
}
