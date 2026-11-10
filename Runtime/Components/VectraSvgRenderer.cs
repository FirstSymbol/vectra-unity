using System.Collections.Generic;
using FirstSymbol.Vectra.Geometry;
using FirstSymbol.Vectra.Rendering;
using FirstSymbol.Vectra.Svg;
using FirstSymbol.Vectra.TextEngines;
using Unity.Mathematics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FirstSymbol.Vectra
{
    /// <summary>
    /// Draws a <see cref="VectraSvgDocument"/> with MeshRenderers in world space. Text is drawn by the project's text
    /// engine through its Canvas-free text (Lettera World Text, TextMesh Pro 3D); the legacy uGUI Text cannot draw without a
    /// Canvas, so with only that engine the text is skipped. Text and the shapes after it keep their paint order through
    /// sorting orders (this component's sorting order, plus one per step of the document).
    /// </summary>
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
        [SerializeField] bool m_GenerateText = true;
        [Tooltip("Which engine draws the text of this document. Use Project Setting to follow Project Settings > FirstSymbol > Vectra.")]
        [SerializeField] VectraTextEngineChoice m_TextEngine;
        [Tooltip("Maps SVG font-family names to fonts of the text engines.")]
        [SerializeField] VectraFontMap m_FontMap;
        [Tooltip("TextMesh Pro font used when the font map has no match (empty = TextMesh Pro default).")]
        [SerializeField] Object m_Font;
        [Tooltip("Lettera font asset or font stack used when the font map has no match (empty = Lettera default).")]
        [SerializeField] Object m_LetteraFont;

        // A run of path elements that follows a text, drawn by a child MeshRenderer.
        sealed class WorldLayer
        {
            public GameObject go;
            public VectraShapeRenderer renderer;
            public Mesh mesh;
            public int from, to;
            // A step drawn by the compositor: the picture is a render texture shown on a quad (renderer and mesh are not used).
            public bool composite;
            public RenderTexture texture;
            public MaterialPropertyBlock block;
        }

        sealed class WorldText
        {
            public GameObject go;
            public SvgElementData element;
            public int order;
        }

        VectraShapeRenderer m_Renderer;
        Mesh m_Mesh;
        readonly List<SvgDrawItem> m_Items = new List<SvgDrawItem>();
        readonly List<WorldLayer> m_Layers = new List<WorldLayer>();
        readonly List<WorldText> m_Texts = new List<WorldText>();
        IVectraTextBackend m_Backend;
        bool m_Dirty = true;
        bool m_StructureDirty = true;
        bool m_TextDirty = true;
        int m_BuiltSignature;
        float m_BuiltPixelSize;
        VectraTextEngine m_WarnedFor = (VectraTextEngine)(-1);
        VectraSvgDocument m_WarnedDocument;

        public VectraSvgDocument Document
        {
            get => m_Document;
            set
            {
                m_Document = value;
                m_Dirty = m_StructureDirty = m_TextDirty = true;
            }
        }

        public List<SvgOverride> Overrides => m_Overrides;

        /// <summary>Sorting order of the first step of the document; each later step (text, shapes after text) adds one.</summary>
        public int SortingOrder
        {
            get => m_SortingOrder;
            set
            {
                if (m_SortingOrder == value) return;
                m_SortingOrder = value;
                m_Dirty = m_StructureDirty = true;
            }
        }

        /// <summary>Height of the document in world units.</summary>
        public float Height
        {
            get => m_Height;
            set
            {
                m_Height = value;
                m_Dirty = m_TextDirty = true;
            }
        }

        public void SetDirty() => m_Dirty = true;

        /// <summary>The text engine this component asks for; <see cref="VectraTextEngineChoice.UseProjectSetting"/> follows the project.</summary>
        public VectraTextEngineChoice TextEngine
        {
            get => m_TextEngine;
            set
            {
                if (m_TextEngine == value) return;
                m_TextEngine = value;
                m_StructureDirty = m_Dirty = true;
            }
        }

        public bool GenerateText
        {
            get => m_GenerateText;
            set
            {
                if (m_GenerateText == value) return;
                m_GenerateText = value;
                m_StructureDirty = m_Dirty = true;
            }
        }

        public VectraFontMap FontMap
        {
            get => m_FontMap;
            set
            {
                m_FontMap = value;
                m_StructureDirty = m_Dirty = true;
            }
        }

        /// <summary>TextMesh Pro font used when the font map has no match.</summary>
        public Object FallbackFont
        {
            get => m_Font;
            set
            {
                m_Font = value;
                m_StructureDirty = m_Dirty = true;
            }
        }

        /// <summary>Lettera font asset or stack used when the font map has no match.</summary>
        public Object LetteraFallbackFont
        {
            get => m_LetteraFont;
            set
            {
                m_LetteraFont = value;
                m_StructureDirty = m_Dirty = true;
            }
        }

        /// <summary>The engine asked for after the project setting is applied (may still be Auto).</summary>
        public VectraTextEngine RequestedTextEngine => VectraTextEngineChoices.Effective(m_TextEngine, VectraRuntimeSettings.ProjectTextEngine);

        /// <summary>The backend that draws this document's text now (world space needs an engine without a Canvas).</summary>
        public VectraTextChoice ResolveTextBackend() =>
            VectraTextBackends.Resolve(RequestedTextEngine, m_FontMap, m_Font, m_LetteraFont, true);

        /// <summary>Rebuilds the meshes and the text objects now instead of in the next LateUpdate.</summary>
        public void RebuildNow()
        {
            m_Dirty = m_StructureDirty = m_TextDirty = true;
            Rebuild();
        }

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
            m_Dirty = m_StructureDirty = m_TextDirty = true;
        }

        void OnDisable()
        {
            VectraAtlases.Unregister(this);
            ClearGenerated();
        }

        void OnDestroy()
        {
            m_Renderer?.Dispose();
            DestroyMesh(m_Mesh);
        }

        static void DestroyMesh(Mesh mesh)
        {
            if (mesh == null) return;
            if (Application.isPlaying) Destroy(mesh);
            else DestroyImmediate(mesh);
        }

        void OnValidate() => m_Dirty = m_StructureDirty = m_TextDirty = true;

        void IVectraAtlasClient.OnAtlasInvalidated()
        {
            m_Renderer?.Invalidate();
            foreach (var l in m_Layers) l.renderer?.Invalidate();
            m_Dirty = true;
        }

        float PixelSize()
        {
            var s = transform.lossyScale;
            return VectraRenderer.EstimatePixelSize(Camera.main, transform.position, math.max(math.abs(s.x), math.abs(s.y)));
        }

        VectraWorldShaderKind m_ShaderKind;

        void LateUpdate()
        {
            // The render pipeline can change while the document is shown: give the meshes the shader of the pipeline in use.
            var kind = VectraMaterials.CurrentWorldKind;
            if (kind != m_ShaderKind)
            {
                m_ShaderKind = kind;
                if (TryGetComponent(out MeshRenderer main)) main.sharedMaterial = VectraMaterials.GetWorld(ShapeBlendMode.Normal, m_Renderer?.mainTexture);
                foreach (var l in m_Layers)
                    if (l.go != null && !l.composite && l.go.TryGetComponent(out MeshRenderer lr)) lr.sharedMaterial = VectraMaterials.GetWorld(ShapeBlendMode.Normal, l.renderer?.mainTexture);
            }

            float px = PixelSize();
            if (!m_Dirty && m_BuiltPixelSize > 0f && (px > m_BuiltPixelSize * Band || px < m_BuiltPixelSize / Band)) m_Dirty = true;
            if (!m_Dirty) return;
            Rebuild(px);
        }

        // Non-scaling strokes follow the screen scale closely; other pixel-size dependencies only need a rough match.
        float Band => m_Document != null && m_Document.HasPixelStrokes ? 1.01f : 1.5f;

        void Rebuild() => Rebuild(PixelSize());

        void Rebuild(float px)
        {
            m_Dirty = false;
            m_BuiltPixelSize = px;
            int signature = SvgDrawOrder.Split(m_Document, m_GenerateText, false, m_Items);
            if (signature != m_BuiltSignature)
            {
                m_BuiltSignature = signature;
                m_StructureDirty = true;
            }

            if (m_StructureDirty)
            {
                ClearGenerated();
                CreateGenerated();
                m_StructureDirty = false;
                m_TextDirty = true;
            }

            m_Renderer ??= new VectraShapeRenderer();
            if (m_Mesh == null) m_Mesh = new Mesh { name = "Vectra SVG", hideFlags = HideFlags.HideAndDontSave };
            SvgDrawOrder.MainRange(m_Items, out int from, out int to);
            var rect = LocalRect;
            VectraSvgBuilder.Build(m_Document, m_Renderer, rect, px, m_Color, true, m_Quality, m_Overrides, null, null, from, to);
            m_Renderer.buffer.Upload(m_Mesh);
            GetComponent<MeshFilter>().sharedMesh = m_Mesh;
            var mr = GetComponent<MeshRenderer>();
            m_ShaderKind = VectraMaterials.CurrentWorldKind;
            mr.sharedMaterial = VectraMaterials.GetWorld(ShapeBlendMode.Normal, m_Renderer.mainTexture);
            mr.sortingOrder = m_SortingOrder;

            foreach (var layer in m_Layers)
            {
                if (layer.composite)
                {
                    RenderComposite(layer, rect, px);
                    continue;
                }

                VectraSvgBuilder.Build(m_Document, layer.renderer, rect, px, m_Color, true, m_Quality, m_Overrides, null, null, layer.from, layer.to);
                layer.renderer.buffer.Upload(layer.mesh);
                if (layer.go != null && layer.go.TryGetComponent(out MeshRenderer lr))
                    lr.sharedMaterial = VectraMaterials.GetWorld(ShapeBlendMode.Normal, layer.renderer.mainTexture);
            }

            if (m_TextDirty)
            {
                m_TextDirty = false;
                LayoutTexts(rect);
            }
        }

        void ClearGenerated()
        {
            foreach (var l in m_Layers)
            {
                l.renderer?.Dispose();
                DestroyMesh(l.mesh);
                SvgCompositor.Free(ref l.texture);
                DestroyObject(l.go);
            }

            foreach (var t in m_Texts) DestroyObject(t.go);
            m_Layers.Clear();
            m_Texts.Clear();
            m_Backend = null;
        }

        static void DestroyObject(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }

        void CreateGenerated()
        {
            VectraTextChoice choice = default;
            bool chosen = false;
            for (int i = 0; i < m_Items.Count; i++)
            {
                var item = m_Items[i];
                int order = m_SortingOrder + i;
                if (item.composite)
                {
                    CreateComposite(item, order);
                    continue;
                }

                if (!item.overlay)
                {
                    if (i == 0) continue; // the component's own mesh
                    CreateLayer(item, order);
                    continue;
                }

                var e = m_Document.elements[item.from];
                if (!chosen)
                {
                    choice = ResolveTextBackend();
                    chosen = true;
                    WarnOnce(choice);
                    m_Backend = choice.backend;
                }

                if (choice.backend == null) continue;
                var go = new GameObject("SVG Text", typeof(RectTransform)) { hideFlags = HideFlags.DontSave | HideFlags.NotEditable };
                go.transform.SetParent(transform, false);
                m_Texts.Add(new WorldText { go = go, element = e, order = order });
            }
        }

        void CreateComposite(SvgDrawItem item, int order)
        {
            var go = new GameObject("SVG Composite", typeof(MeshFilter), typeof(MeshRenderer)) { hideFlags = HideFlags.DontSave | HideFlags.NotEditable };
            go.transform.SetParent(transform, false);
            var layer = new WorldLayer
            {
                go = go,
                mesh = new Mesh { name = "Vectra SVG composite", hideFlags = HideFlags.HideAndDontSave },
                from = item.from,
                to = item.to,
                composite = true,
                block = new MaterialPropertyBlock()
            };
            go.GetComponent<MeshFilter>().sharedMesh = layer.mesh;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = SvgCompositor.LayerMaterial;
            mr.sortingOrder = order;
            m_Layers.Add(layer);
        }

        // Draws the layers of the step into its texture at the current pixel size and puts the texture on a quad covering the document.
        void RenderComposite(WorldLayer layer, Rect rect, float px)
        {
            if (!SvgCompositor.RenderStep(m_Document, layer.from, layer.to, rect, px, m_Quality, m_Overrides, null, 1f, ref layer.texture)) return;
            var m = layer.mesh;
            m.Clear();
            m.SetVertices(new[] { new Vector3(rect.xMin, rect.yMin), new Vector3(rect.xMin, rect.yMax), new Vector3(rect.xMax, rect.yMax), new Vector3(rect.xMax, rect.yMin) });
            m.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) });
            m.SetColors(new[] { m_Color, m_Color, m_Color, m_Color });
            m.SetTriangles(new[] { 0, 1, 2, 2, 3, 0 }, 0);
            m.RecalculateBounds();
            layer.block.SetTexture("_MainTex", layer.texture);
            layer.go.GetComponent<MeshRenderer>().SetPropertyBlock(layer.block);
        }

        void CreateLayer(SvgDrawItem item, int order)
        {
            var go = new GameObject("SVG Layer", typeof(MeshFilter), typeof(MeshRenderer)) { hideFlags = HideFlags.DontSave | HideFlags.NotEditable };
            go.transform.SetParent(transform, false);
            var layer = new WorldLayer
            {
                go = go,
                renderer = new VectraShapeRenderer(),
                mesh = new Mesh { name = "Vectra SVG layer", hideFlags = HideFlags.HideAndDontSave },
                from = item.from,
                to = item.to
            };
            go.GetComponent<MeshFilter>().sharedMesh = layer.mesh;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = VectraMaterials.GetWorld();
            mr.sortingOrder = order;
            m_Layers.Add(layer);
        }

        void LayoutTexts(Rect rect)
        {
            if (m_Backend == null || m_Document == null) return;
            var toLocal = m_Document.DocumentToLocal(rect);
            float scale = toLocal.UniformScale;
            var fallback = VectraTextBackends.FontFor(m_Backend, m_Font, m_LetteraFont);
            foreach (var t in m_Texts)
            {
                if (t.go == null) continue;
                var e = t.element;
                float2 p = toLocal.Apply(e.textPosition);
                var spec = new VectraTextSpec
                {
                    element = e,
                    fontSize = e.fontSize * scale,
                    tint = m_Color,
                    color = e.textColor * m_Color,
                    origin = new Vector2(p.x, p.y),
                    rotation = -e.textRotation,
                    fontMap = m_FontMap,
                    fallbackFont = fallback,
                    sortingOrder = t.order
                };
                m_Backend.BuildWorld(t.go, spec);
            }
        }

        // One console line per document and engine, not one per rebuild.
        void WarnOnce(VectraTextChoice choice)
        {
            if (!HasText() || (!choice.fellBack && choice.backend != null)) return;
            if (choice.requested == VectraTextEngine.None) return;
            if (m_WarnedDocument == m_Document && m_WarnedFor == choice.requested) return;
            m_WarnedDocument = m_Document;
            m_WarnedFor = choice.requested;
            if (choice.backend == null)
                Debug.LogWarning($"Vectra: no text engine can draw the text of '{m_Document.name}' in world space (asked for {choice.requested}); the text is skipped. World text needs Lettera or TextMesh Pro.", this);
            else
                Debug.LogWarning($"Vectra: the {choice.requested} text engine cannot draw '{m_Document.name}' in world space; using {choice.Used}.", this);
        }

        bool HasText()
        {
            if (m_Document == null) return false;
            foreach (var e in m_Document.elements)
                if (e != null && e.isText) return true;
            return false;
        }
    }
}
