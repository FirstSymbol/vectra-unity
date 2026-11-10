using System.Collections.Generic;
using FirstSymbol.Vectra.Geometry;
using FirstSymbol.Vectra.Rendering;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Vectra.UIToolkit
{
    /// <summary>The shapes a <see cref="VectraElement"/> can be given from UXML. Anything else is set from code or a shape asset.</summary>
    public enum VectraElementShape
    {
        Rectangle,
        Ellipse,
        Triangle,
        Polygon,
        Star,
        Heart,
        Squircle,
        /// <summary>Geometry set from code (<see cref="VectraElement.Geometry"/>) or from a shape asset.</summary>
        Custom
    }

    /// <summary>Shared plumbing of the UI Toolkit elements.</summary>
    static class VectraUitk
    {
        /// <summary>Screen pixels per point of an element: panel scaling times the scale of its world transform.</summary>
        public static float Density(VisualElement ve)
        {
            float ppp = ve.scaledPixelsPerPoint;
            if (!(ppp > 0f)) ppp = 1f;
            var m = ve.worldTransform;
            float det = Mathf.Abs(m.m00 * m.m11 - m.m01 * m.m10);
            float sc = Mathf.Sqrt(det);
            if (!(sc > 1e-3f)) sc = 1f;
            return ppp * sc;
        }

        public static bool Usable(Rect r) => r.width > 0.01f && r.height > 0.01f && !float.IsNaN(r.width) && !float.IsNaN(r.height);

        // Static events of the model, one subscription for all elements.
        static bool s_Hooked;
        static readonly List<VectraElement> s_Shapes = new List<VectraElement>();
        static readonly List<VectraSvgElement> s_Svgs = new List<VectraSvgElement>();

        public static void Register(VectraElement e)
        {
            Hook();
            s_Shapes.Add(e);
        }

        public static void Unregister(VectraElement e) => s_Shapes.Remove(e);

        public static void Register(VectraSvgElement e)
        {
            Hook();
            s_Svgs.Add(e);
        }

        public static void Unregister(VectraSvgElement e) => s_Svgs.Remove(e);

        public static int LiveShapes => s_Shapes.Count;
        public static int LiveSvgs => s_Svgs.Count;

        static void Hook()
        {
            if (s_Hooked) return;
            s_Hooked = true;
            VectraStyle.Changed += style =>
            {
                for (int i = 0; i < s_Shapes.Count; i++)
                    if (s_Shapes[i].Content.style == style) s_Shapes[i].SetShapeDirty();
            };
            VectraShapeAsset.Changed += asset =>
            {
                for (int i = 0; i < s_Shapes.Count; i++)
                    if (s_Shapes[i].Content.shapeAsset == asset) s_Shapes[i].SetShapeDirty();
            };
            VectraPalette.Changed += () =>
            {
                for (int i = 0; i < s_Shapes.Count; i++) s_Shapes[i].SetShapeDirty();
                for (int i = 0; i < s_Svgs.Count; i++) s_Svgs[i].SetDirty();
            };
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Shapes.Clear();
            s_Svgs.Clear();
            s_Hooked = false;
        }
    }

    /// <summary>
    /// A Vectra shape for UI Toolkit: any primitive or path with fills, strokes and effects. It draws with the same
    /// material and shader as the uGUI <see cref="VectraShape"/>, into a render texture the element shows, so
    /// anti-aliasing, gradients, strokes, shadows, glows and the other effects look the same. The picture is redrawn
    /// only when the shape, its size or the screen density changes.
    /// </summary>
    /// <remarks>
    /// In UXML: <c>&lt;fs:VectraElement shape="Star" fill="#ffcc00" stroke="#222" stroke-width="3" /&gt;</c>. In USS:
    /// <c>--vectra-fill</c>, <c>--vectra-stroke</c>, <c>--vectra-stroke-width</c> and <c>--vectra-corner-radius</c>
    /// override the attributes (and change with :hover and the other pseudo states). Size comes from the usual
    /// width and height; with none the element is 100 by 100 (class <c>vectra-element</c> of the default style sheet).
    /// </remarks>
    [UxmlElement("VectraElement")]
    public partial class VectraElement : VisualElement, IVectraAtlasClient
    {
        public const string ussClassName = "vectra-element";

        static readonly CustomStyleProperty<Color> s_UssFill = new CustomStyleProperty<Color>("--vectra-fill");
        static readonly CustomStyleProperty<Color> s_UssStroke = new CustomStyleProperty<Color>("--vectra-stroke");
        static readonly CustomStyleProperty<float> s_UssStrokeWidth = new CustomStyleProperty<float>("--vectra-stroke-width");
        static readonly CustomStyleProperty<float> s_UssRadius = new CustomStyleProperty<float>("--vectra-corner-radius");

        VectraShapeContent m_Content = NewContent();
        VectraRasterLayer m_Layer;
        VectraHitTester m_HitTester;
        IVisualElementScheduledItem m_Rebuild, m_Poll;
        bool m_Dirty = true;
        bool m_Appearance;
        bool m_Scheduled;
        Vector2 m_Center;
        Vector2 m_BuiltSize;

        Color? m_Fill, m_Stroke, m_UssFill, m_UssStroke;
        float? m_StrokeWidth, m_Radius, m_UssStrokeWidth, m_UssRadius;

        /// <summary>Counts the times the shape was rasterized (a test and profiling aid).</summary>
        public int RasterCount { get; private set; }

        /// <summary>Statistics of the last mesh build.</summary>
        public VectraBuildStats Stats => m_Layer?.renderer.stats ?? default;

        /// <summary>Size in pixels of the region the shape was rasterized into (zero when nothing is drawn).</summary>
        public Vector2Int SurfaceSize => m_Layer != null ? m_Layer.pixelSize : default;

        internal Texture SurfaceTexture => m_Layer?.Texture;

        static VectraShapeContent NewContent()
        {
            var c = new VectraShapeContent();
            c.fills[0].paint = VectraPaint.Solid(new Color(0.24f, 0.55f, 0.96f));
            return c;
        }

        public VectraElement()
        {
            AddToClassList(ussClassName);
            var sheet = Resources.Load<StyleSheet>("Vectra/VectraElement");
            if (sheet != null) styleSheets.Add(sheet);
            generateVisualContent = OnGenerate;
            RegisterCallback<AttachToPanelEvent>(OnAttach);
            RegisterCallback<DetachFromPanelEvent>(OnDetach);
            RegisterCallback<GeometryChangedEvent>(OnGeometry);
            RegisterCallback<CustomStyleResolvedEvent>(OnStyleResolved);
        }

        // ----- content -----

        /// <summary>The shape description shared with the uGUI and world components. Call <see cref="SetShapeDirty"/> after editing it.</summary>
        public VectraShapeContent Content => m_Content;

        public VectraShapeGeometry Geometry
        {
            get => m_Content.ActiveGeometry;
            set
            {
                m_Content.shapeAsset = null;
                m_Content.geometry = value;
                SetShapeDirty();
            }
        }

        public List<FillLayer> Fills => m_Content.ActiveFills;
        public List<StrokeLayer> Strokes => m_Content.ActiveStrokes;
        public List<VectraEffect> Effects => m_Content.ActiveEffects;

        /// <summary>Returns the geometry as <typeparamref name="T"/>, replacing it when it is of another type.</summary>
        public T SetGeometry<T>() where T : VectraShapeGeometry, new()
        {
            m_Content.shapeAsset = null;
            if (!(m_Content.geometry is T g))
            {
                g = new T();
                m_Content.geometry = g;
            }

            SetShapeDirty();
            return g;
        }

        public T AddEffect<T>() where T : VectraEffect, new()
        {
            var e = new T();
            m_Content.ActiveEffects.Add(e);
            SetShapeDirty();
            return e;
        }

        /// <summary>Call after changing geometry, fills, strokes or effects from code.</summary>
        public void SetShapeDirty()
        {
            m_Dirty = true;
            Schedule();
        }

        [UxmlAttribute("shape")]
        public VectraElementShape Shape
        {
            get
            {
                switch (m_Content.ActiveGeometry)
                {
                    case RectangleShape _: return VectraElementShape.Rectangle;
                    case EllipseShape _: return VectraElementShape.Ellipse;
                    case TriangleShape _: return VectraElementShape.Triangle;
                    case PolygonShape _: return VectraElementShape.Polygon;
                    case StarShape _: return VectraElementShape.Star;
                    case HeartShape _: return VectraElementShape.Heart;
                    case SquircleShape _: return VectraElementShape.Squircle;
                    default: return VectraElementShape.Custom;
                }
            }
            set
            {
                if (value == VectraElementShape.Custom || (value == Shape && m_Content.shapeAsset == null)) return;
                m_Content.shapeAsset = null;
                switch (value)
                {
                    case VectraElementShape.Rectangle: m_Content.geometry = new RectangleShape(); break;
                    case VectraElementShape.Ellipse: m_Content.geometry = new EllipseShape(); break;
                    case VectraElementShape.Triangle: m_Content.geometry = new TriangleShape(); break;
                    case VectraElementShape.Polygon: m_Content.geometry = new PolygonShape(); break;
                    case VectraElementShape.Star: m_Content.geometry = new StarShape(); break;
                    case VectraElementShape.Heart: m_Content.geometry = new HeartShape(); break;
                    case VectraElementShape.Squircle: m_Content.geometry = new SquircleShape(); break;
                }

                SetShapeDirty();
            }
        }

        /// <summary>Shared geometry; overrides <see cref="Shape"/> when set.</summary>
        [UxmlAttribute("shape-asset")]
        public VectraShapeAsset ShapeAsset
        {
            get => m_Content.shapeAsset;
            set
            {
                m_Content.shapeAsset = value;
                SetShapeDirty();
            }
        }

        /// <summary>Shared fills, strokes and effects; replace the element's own.</summary>
        [UxmlAttribute("style-asset")]
        public VectraStyle StyleAsset
        {
            get => m_Content.style;
            set
            {
                m_Content.style = value;
                SetShapeDirty();
            }
        }

        [UxmlAttribute("fill")]
        public Color Fill
        {
            get => m_UssFill ?? m_Fill ?? FirstFillColor();
            set
            {
                m_Fill = value;
                m_Appearance = true;
                SetShapeDirty();
            }
        }

        [UxmlAttribute("stroke")]
        public Color Stroke
        {
            get => m_UssStroke ?? m_Stroke ?? FirstStrokeColor();
            set
            {
                m_Stroke = value;
                m_Appearance = true;
                SetShapeDirty();
            }
        }

        [UxmlAttribute("stroke-width")]
        public float StrokeWidth
        {
            get => m_UssStrokeWidth ?? m_StrokeWidth ?? (m_Content.ActiveStrokes.Count > 0 && m_Content.ActiveStrokes[0].enabled ? m_Content.ActiveStrokes[0].width : 0f);
            set
            {
                m_StrokeWidth = value;
                m_Appearance = true;
                SetShapeDirty();
            }
        }

        /// <summary>Corner radius of a rectangle, polygon or star, in points.</summary>
        [UxmlAttribute("corner-radius")]
        public float CornerRadius
        {
            get => m_UssRadius ?? m_Radius ?? (m_Content.ActiveGeometry is RectangleShape r ? r.cornerRadii.x : 0f);
            set
            {
                m_Radius = value;
                m_Appearance = true;
                SetShapeDirty();
            }
        }

        /// <summary>Number of sides of a polygon or points of a star.</summary>
        [UxmlAttribute("sides")]
        public int Sides
        {
            get => m_Content.ActiveGeometry is StarShape s ? s.points : m_Content.ActiveGeometry is PolygonShape p ? p.sides : 5;
            set
            {
                if (m_Content.geometry is StarShape s && m_Content.shapeAsset == null) s.points = Mathf.Clamp(value, 2, 64);
                else if (m_Content.geometry is PolygonShape p && m_Content.shapeAsset == null) p.sides = Mathf.Clamp(value, 3, 64);
                else return;
                SetShapeDirty();
            }
        }

        Color FirstFillColor()
        {
            var f = m_Content.ActiveFills;
            return f.Count > 0 && f[0]?.paint != null ? f[0].paint.color : Color.white;
        }

        Color FirstStrokeColor()
        {
            var s = m_Content.ActiveStrokes;
            return s.Count > 0 && s[0]?.paint != null ? s[0].paint.color : Color.black;
        }

        // Writes the attributes and USS values into the element's own layers. Shared styles stay untouched.
        void ApplyAppearance()
        {
            if (m_Content.style == null)
            {
                var fills = m_Content.fills;
                var fillColor = m_UssFill ?? m_Fill;
                if (fillColor.HasValue)
                {
                    if (fills.Count == 0) fills.Add(new FillLayer());
                    var p = fills[0].paint;
                    if (p == null || p.type != PaintType.Solid || p.color != fillColor.Value) fills[0].paint = VectraPaint.Solid(fillColor.Value);
                    fills[0].enabled = true;
                }

                var strokeColor = m_UssStroke ?? m_Stroke;
                var strokeWidth = m_UssStrokeWidth ?? m_StrokeWidth;
                if (strokeColor.HasValue || strokeWidth.HasValue)
                {
                    var strokes = m_Content.strokes;
                    if (strokes.Count == 0) strokes.Add(new StrokeLayer());
                    var st = strokes[0];
                    if (strokeColor.HasValue)
                    {
                        var p = st.paint;
                        if (p == null || p.type != PaintType.Solid || p.color != strokeColor.Value) st.paint = VectraPaint.Solid(strokeColor.Value);
                    }

                    if (strokeWidth.HasValue) st.width = Mathf.Max(0f, strokeWidth.Value);
                    st.enabled = st.width > 0f;
                }
            }

            var radius = m_UssRadius ?? m_Radius;
            if (radius.HasValue && m_Content.shapeAsset == null)
            {
                float r = Mathf.Max(0f, radius.Value);
                switch (m_Content.geometry)
                {
                    case RectangleShape rect: rect.cornerRadii = new Vector4(r, r, r, r); break;
                    case PolygonShape poly: poly.cornerRadius = r; break;
                    case StarShape star: star.cornerRadius = r; break;
                    case TriangleShape tri: tri.cornerRadius = r; break;
                }
            }
        }

        // ----- lifecycle -----

        void OnAttach(AttachToPanelEvent evt)
        {
            VectraAtlases.Register(this);
            VectraUitk.Register(this);
            m_Rebuild = schedule.Execute(RebuildNow);
            m_Poll = schedule.Execute(Poll).Every(250);
            m_Dirty = true;
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
            m_Layer?.Dispose();
            m_Layer = null;
            m_HitTester?.Dispose();
            m_HitTester = null;
        }

        void OnGeometry(GeometryChangedEvent evt)
        {
            var size = contentRect.size;
            if (size == m_BuiltSize) return;
            m_Dirty = true;
            Schedule();
        }

        void OnStyleResolved(CustomStyleResolvedEvent evt)
        {
            var cs = evt.customStyle;
            Color? fill = cs.TryGetValue(s_UssFill, out var f) ? f : (Color?)null;
            Color? stroke = cs.TryGetValue(s_UssStroke, out var s) ? s : (Color?)null;
            float? width = cs.TryGetValue(s_UssStrokeWidth, out var w) ? w : (float?)null;
            float? radius = cs.TryGetValue(s_UssRadius, out var r) ? r : (float?)null;
            if (fill == m_UssFill && stroke == m_UssStroke && width == m_UssStrokeWidth && radius == m_UssRadius) return;
            m_UssFill = fill;
            m_UssStroke = stroke;
            m_UssStrokeWidth = width;
            m_UssRadius = radius;
            m_Appearance = true;
            SetShapeDirty();
        }

        void IVectraAtlasClient.OnAtlasInvalidated()
        {
            m_Layer?.renderer.Invalidate();
            SetShapeDirty();
        }

        void Schedule()
        {
            if (m_Scheduled || m_Rebuild == null) return;
            m_Scheduled = true;
            m_Rebuild.ExecuteLater(0);
        }

        // Screen density drifted a lot (panel scale, a scale transform) or the texture was lost.
        void Poll()
        {
            if (m_Layer == null || m_Dirty) return;
            float built = m_Layer.builtScale;
            if (!(built > 0f)) return;
            float now = VectraUitk.Density(this);
            if (now > built * 1.25f || now < built * 0.8f || !m_Layer.IsAlive) SetShapeDirty();
        }

        /// <summary>Builds and rasterizes the shape now instead of before the next repaint. Needs the element to be in a panel with a layout.</summary>
        public void RebuildNow()
        {
            m_Scheduled = false;
            m_Dirty = false;
            if (panel == null) return;
            var cr = contentRect;
            m_BuiltSize = cr.size;
            if (!VectraUitk.Usable(cr))
            {
                m_Layer?.Release();
                MarkDirtyRepaint();
                return;
            }

            m_Layer ??= new VectraRasterLayer();
            float density = VectraUitk.Density(this);
            if (m_Appearance)
            {
                m_Appearance = false;
                ApplyAppearance();
            }

            var frame = new ShapeFrame
            {
                rect = new Rect(-cr.width * 0.5f, -cr.height * 0.5f, cr.width, cr.height),
                pixelSize = 1f / density
            };
            m_Layer.renderer.Build(m_Content.ToInput(frame, Color.white, true));
            m_Center = cr.center;
            m_Layer.Rasterize(m_Center, density);
            RasterCount++;
            MarkDirtyRepaint();
        }

        void OnGenerate(MeshGenerationContext mgc)
        {
            if (m_Layer == null || !m_Layer.HasImage) return;
            m_Layer.Emit(mgc, new Color32(255, 255, 255, 255));
        }

        // ----- picking -----

        public override bool ContainsPoint(Vector2 localPoint)
        {
            if (!base.ContainsPoint(localPoint)) return false;
            var cr = contentRect;
            if (!VectraUitk.Usable(cr)) return false;
            var c = cr.center;
            // Element space (y down) to the shape's local space (y up).
            var local = new Vector2(localPoint.x - c.x, c.y - localPoint.y);
            float reach = 0f;
            foreach (var s in m_Content.ActiveStrokes)
                if (s != null && s.enabled)
                    reach = math.max(reach, s.alignment == StrokeAlignment.Center ? s.width * 0.5f : s.alignment == StrokeAlignment.Outside ? s.width : 0f);
            m_HitTester ??= new VectraHitTester();
            var frame = new ShapeFrame { rect = new Rect(-cr.width * 0.5f, -cr.height * 0.5f, cr.width, cr.height), pixelSize = 1f / VectraUitk.Density(this) };
            return m_HitTester.Contains(m_Content.ActiveGeometry, frame, local, reach);
        }
    }
}
