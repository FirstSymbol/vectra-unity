using System.Collections.Generic;
using FirstSymbol.Vectra.Geometry;
using FirstSymbol.Vectra.Rendering;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

namespace FirstSymbol.Vectra
{
    /// <summary>
    /// Vector shape for uGUI. Draws any analytic primitive or path with fills, strokes and effects in a single
    /// batchable draw. Works with masks, RectMask2D (including softness), layout and precise raycasts.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(CanvasRenderer))]
    [AddComponentMenu("Vectra/Vectra Shape (UI)")]
    public class VectraShape : MaskableGraphic, ILayoutElement, IVectraAtlasClient, IVectraShape, IVectraTicked
    {
        [SerializeField] VectraShapeContent m_Content = new VectraShapeContent();
        [Tooltip("Use the exact outline for raycasts instead of the rect.")]
        [SerializeField] bool m_PreciseRaycast = true;
        [Tooltip("Pixel size override for world-space canvases (0 = estimate from the camera).")]
        [SerializeField] float m_WorldPixelSize;

        VectraShapeRenderer m_Renderer;
        VectraHitTester m_HitTester;
        // Like uGUI's own graphics, every shape fills one shared worker mesh and hands it to its CanvasRenderer, which
        // copies the data: no mesh per shape.
        static Mesh s_WorkerMesh;

        // Shapes that have filled the worker mesh and are not destroyed yet; the mesh goes with the last of them and comes back with the next.
        static int s_MeshUsers;
        bool m_UsesWorkerMesh;

        Mesh WorkerMesh()
        {
            if (!m_UsesWorkerMesh)
            {
                m_UsesWorkerMesh = true;
                s_MeshUsers++;
            }

            if (s_WorkerMesh == null)
            {
                s_WorkerMesh = new Mesh { name = "Vectra Shape", hideFlags = HideFlags.HideAndDontSave };
                s_WorkerMesh.MarkDynamic();
            }

            return s_WorkerMesh;
        }

        void ReleaseWorkerMesh()
        {
            if (!m_UsesWorkerMesh) return;
            m_UsesWorkerMesh = false;
            if (--s_MeshUsers > 0 || s_WorkerMesh == null) return;
            if (Application.isPlaying) Destroy(s_WorkerMesh);
            else DestroyImmediate(s_WorkerMesh);
            s_WorkerMesh = null;
        }
        float m_LastScale;
        Texture m_LastTexture;
        int m_WatchSlot = -1;
        bool m_Follows;

        static readonly HashSet<VectraShape> s_StyleUsers = new HashSet<VectraShape>();
        static int s_StyleUsersPeak;
        static bool s_StyleHooked;

        public VectraShapeContent Content => m_Content;

        /// <summary>The drawn geometry (the shared <see cref="VectraShapeContent.shapeAsset"/> when set). Setting it detaches the asset.</summary>
        public VectraShapeGeometry Geometry
        {
            get => m_Content.ActiveGeometry;
            set
            {
                m_Content.shapeAsset = null;
                m_Content.geometry = value;
                SetVerticesDirty();
            }
        }

        public List<FillLayer> Fills => m_Content.ActiveFills;
        public List<StrokeLayer> Strokes => m_Content.ActiveStrokes;
        public List<VectraEffect> Effects => m_Content.ActiveEffects;

        public VectraStyle Style
        {
            get => m_Content.style;
            set
            {
                m_Content.style = value;
                SetVerticesDirty();
            }
        }

        public ShapeBlendMode BlendMode
        {
            get => m_Content.blendMode;
            set
            {
                if (m_Content.blendMode == value) return;
                m_Content.blendMode = value;
                SetMaterialDirty();
            }
        }

        public bool PreciseRaycast
        {
            get => m_PreciseRaycast;
            set => m_PreciseRaycast = value;
        }

        public VectraBuildStats Stats => m_Renderer?.stats ?? default;

        /// <summary>Distance function of the last build (used by <see cref="VectraMask"/>).</summary>
        public VectraDistanceShape DistanceShape => m_Renderer?.Distance ?? default;

        /// <summary>Returns the geometry as <typeparamref name="T"/>, replacing it when it is of another type.</summary>
        public T SetGeometry<T>() where T : VectraShapeGeometry, new()
        {
            m_Content.shapeAsset = null;
            if (!(m_Content.geometry is T g))
            {
                g = new T();
                m_Content.geometry = g;
            }

            SetVerticesDirty();
            return g;
        }

        /// <summary>Call after changing geometry, fills, strokes or effects from code.</summary>
        public void SetShapeDirty() => SetVerticesDirty();

        public VectraShape SetFill(Color color)
        {
            var fills = m_Content.ActiveFills;
            if (fills.Count == 0) fills.Add(new FillLayer());
            fills[0].paint = VectraPaint.Solid(color);
            fills[0].enabled = true;
            SetVerticesDirty();
            return this;
        }

        public VectraShape SetFill(VectraPaint paint)
        {
            var fills = m_Content.ActiveFills;
            if (fills.Count == 0) fills.Add(new FillLayer());
            fills[0].paint = paint;
            fills[0].enabled = true;
            SetVerticesDirty();
            return this;
        }

        public VectraShape SetStroke(Color color, float width, StrokeAlignment alignment = StrokeAlignment.Center)
        {
            var strokes = m_Content.ActiveStrokes;
            if (strokes.Count == 0) strokes.Add(new StrokeLayer());
            strokes[0].paint = VectraPaint.Solid(color);
            strokes[0].width = width;
            strokes[0].alignment = alignment;
            strokes[0].enabled = true;
            SetVerticesDirty();
            return this;
        }

        public T AddEffect<T>() where T : VectraEffect, new()
        {
            var e = new T();
            m_Content.ActiveEffects.Add(e);
            SetVerticesDirty();
            return e;
        }

        public T GetEffect<T>() where T : VectraEffect
        {
            foreach (var e in m_Content.ActiveEffects)
                if (e is T t) return t;
            return null;
        }

        public override Material defaultMaterial => VectraMaterials.GetUI(m_Content.blendMode) ?? base.defaultMaterial;

        public override Material GetModifiedMaterial(Material baseMaterial)
        {
            var m = base.GetModifiedMaterial(baseMaterial);
            var soft = VectraMask.FindSoftMask(this);
            return soft != null ? soft.GetSoftMaterial(m) : m;
        }

        public override Texture mainTexture
        {
            get
            {
                var t = m_Renderer?.mainTexture;
                return t != null ? t : s_WhiteTexture;
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            EnsureChannels();
            VectraAtlases.Register(this);
            HookStyles();
            s_StyleUsers.Add(this);
            if (s_StyleUsers.Count > s_StyleUsersPeak) s_StyleUsersPeak = s_StyleUsers.Count;
            m_Follows = false;
            VectraWatch.Add(this, transform, VectraWatch.GroupShape, false);
        }

        protected override void OnDisable()
        {
            VectraWatch.Remove(this);
            VectraAtlases.Unregister(this);
            s_StyleUsers.Remove(this);
            // The set keeps the size of its largest day: give it back when the last shape is gone.
            if (s_StyleUsers.Count == 0 && s_StyleUsersPeak > 256)
            {
                s_StyleUsers.TrimExcess();
                s_StyleUsersPeak = 0;
            }
            base.OnDisable();
        }

        protected override void OnDestroy()
        {
            VectraWatch.Discard(this);
            ReleaseWorkerMesh();
            m_Renderer?.Dispose();
            m_Renderer = null;
            m_HitTester?.Dispose();
            m_HitTester = null;
            base.OnDestroy();
        }

        static void HookStyles()
        {
            if (s_StyleHooked) return;
            s_StyleHooked = true;
            VectraStyle.Changed += style =>
            {
                foreach (var s in s_StyleUsers)
                    if (s != null && s.m_Content.style == style) s.SetVerticesDirty();
            };
            VectraPalette.Changed += () =>
            {
                foreach (var s in s_StyleUsers)
                    if (s != null) s.SetVerticesDirty();
            };
            VectraShapeAsset.Changed += asset =>
            {
                foreach (var s in s_StyleUsers)
                    if (s != null && s.m_Content.shapeAsset == asset) s.SetVerticesDirty();
            };
        }

        void IVectraAtlasClient.OnAtlasInvalidated()
        {
            m_Renderer?.Invalidate();
            SetVerticesDirty();
        }

        protected override void OnCanvasHierarchyChanged()
        {
            base.OnCanvasHierarchyChanged();
            EnsureChannels();
            VectraWatch.Request(this);
        }

        protected override void OnTransformParentChanged()
        {
            base.OnTransformParentChanged();
            EnsureChannels();
        }

        void EnsureChannels()
        {
            var c = canvas;
            if (c == null) return;
            const AdditionalCanvasShaderChannels needed = AdditionalCanvasShaderChannels.TexCoord1 |
                                                          AdditionalCanvasShaderChannels.TexCoord2 |
                                                          AdditionalCanvasShaderChannels.TexCoord3;
            if ((c.additionalShaderChannels & needed) != needed) c.additionalShaderChannels |= needed;
        }

        float CurrentScale()
        {
            var s = transform.lossyScale;
            return math.max(math.abs(s.x), math.abs(s.y));
        }

        // True until a build has run for the current content; anything that dirties the vertices other than the colour
        // property sets it. A tint change alone rewrites the vertex colours of the last build.
        bool m_FullBuild = true;
        bool m_InColorSetter;

        public override Color color
        {
            get => base.color;
            set
            {
                m_InColorSetter = true;
                try { base.color = value; }
                finally { m_InColorSetter = false; }
            }
        }

        // What the last full build read besides the content; a tint-only rewrite needs all of it unchanged.
        Rect m_BuiltRect;
        float m_BuiltPixel, m_BuiltDetailPixel, m_BuiltMaskReach;
        bool m_BuiltRequireDistance;

        void BeginFrame(out ShapeFrame frame, out bool requireDistance, out float reach)
        {
            frame = Frame;
            frame.detailPixelSize = DetailPixelSize(frame.pixelSize);
            requireDistance = false;
            reach = 0f;
            if (TryGetComponent(out VectraMask mask) && mask.IsSoft)
            {
                requireDistance = true;
                reach = mask.Softness * 2f + Mathf.Abs(mask.Inset);
            }
        }

        public override void SetVerticesDirty()
        {
            // The flag covers only the colour property's own call, not callbacks that run inside base.SetVerticesDirty.
            bool colorOnly = m_InColorSetter;
            m_InColorSetter = false;
            if (!colorOnly) m_FullBuild = true;
            base.SetVerticesDirty();
        }

        internal VectraShapeRenderer BuiltRenderer => m_Renderer;

        int IVectraTicked.WatchSlot
        {
            get => m_WatchSlot;
            set => m_WatchSlot = value;
        }

        bool IVectraTicked.WatchPoll() => false;

        // The old LateUpdate. VectraWatch calls it when the transform moved, every frame while a camera or an editor view can change what
        // the shape needs, and once in a while for every shape (state that has no notification, such as a canvas changing its render mode).
        bool IVectraTicked.WatchTick()
        {
            bool follows = VectraScreenDensity.CanvasFollowsViews(canvas);
            if (canvas != null) VectraWatch.ObserveCanvas(canvas.rootCanvas);
            if (follows != m_Follows)
            {
                m_Follows = follows;
                VectraWatch.SetAlways(this, follows);
            }

            // Pixel-size dependent tessellation and anti-aliasing margins follow large scale changes.
            float s = CurrentScale();
            bool fine = m_Content.HasPixelStrokes;
            if (m_LastScale > 0f && (fine ? Mathf.Abs(s - m_LastScale) > m_LastScale * 0.01f : (s > m_LastScale * 1.25f || s < m_LastScale * 0.8f)))
            {
                m_LastScale = s;
                SetVerticesDirty();
            }
            else if (m_BuiltDetail > 0f && !fine && VectraScreenDensity.CanvasFollowsViews(canvas))
            {
                // Curves follow the densest view (a Scene view magnifying the Canvas); small steps at most ten times a second.
                float px = PixelSize();
                float now = VectraScreenDensity.CanvasDensest(transform, canvas, px);
                if (VectraScreenDensity.Drifted(now < px ? now : px, m_BuiltDetail, ref m_NextDetailCheck, Time.unscaledTime)) SetVerticesDirty();
            }

            return false;
        }

        /// <summary>Size of a screen pixel in this graphic's local units.</summary>
        public float PixelSize()
        {
            var c = canvas;
            if (c == null) return 1f;
            var root = c.rootCanvas;
            float local = CurrentScale();
            if (root.renderMode == RenderMode.WorldSpace)
            {
                if (m_WorldPixelSize > 0f) return m_WorldPixelSize;
                var cam = root.worldCamera != null ? root.worldCamera : Camera.main;
                return VectraRenderer.EstimatePixelSize(cam, transform.position, local);
            }

            var rs = root.transform.lossyScale;
            float rootScale = math.max(math.max(math.abs(rs.x), math.abs(rs.y)), 1e-6f);
            float ppu = root.scaleFactor * local / rootScale;
            return ppu > 1e-6f ? 1f / ppu : 1f;
        }

        public ShapeFrame Frame => new ShapeFrame { rect = rectTransform.rect, pixelSize = PixelSize() };

        float m_BuiltDetail;
        float m_NextDetailCheck;

        /// <summary>Pixel size of the densest view (Scene view, zoomed camera) when it is finer than the main view's, else 0; non-scaling strokes keep the main view.</summary>
        float DetailPixelSize(float px)
        {
            if (m_Content.HasPixelStrokes) return 0f;
            float d = VectraScreenDensity.CanvasDensest(transform, canvas, px);
            return d < px ? d : 0f;
        }

        protected override void OnPopulateMesh(VertexHelper vh) => vh.Clear();

        static readonly Unity.Profiling.ProfilerMarker s_UpdateMarker = new Unity.Profiling.ProfilerMarker("Vectra.VectraShape.UpdateGeometry");

        protected override void UpdateGeometry()
        {
            using (s_UpdateMarker.Auto()) UpdateGeometryCore();
        }

        /// <summary>Meshes built by all shapes since the domain loaded (diagnostics, tests).</summary>
        internal static int s_Rebuilds;

        void UpdateGeometryCore()
        {
            s_Rebuilds++;
            if (m_Renderer == null)
            {
                m_Renderer = new VectraShapeRenderer { recordColors = true };
                m_FullBuild = true;
            }

            if (rectTransform == null || rectTransform.rect.width < 0f || rectTransform.rect.height < 0f)
            {
                canvasRenderer.Clear();
                return;
            }

            BeginFrame(out var frame, out bool requireDistance, out float reach);
            // The geometry depends on the pixel size (margins, tolerance, pixel strokes): only an identical frame may keep it.
            if (!m_FullBuild && frame.rect == m_BuiltRect && frame.pixelSize == m_BuiltPixel &&
                frame.detailPixelSize == m_BuiltDetailPixel && requireDistance == m_BuiltRequireDistance &&
                reach == m_BuiltMaskReach && m_Renderer.TryRecolor(color))
            {
                var recolored = WorkerMesh();
                m_Renderer.buffer.Upload(recolored);
                canvasRenderer.SetMesh(recolored);
                return;
            }

            m_BuiltRect = frame.rect;
            m_BuiltPixel = frame.pixelSize;
            m_BuiltDetailPixel = frame.detailPixelSize;
            m_BuiltRequireDistance = requireDistance;
            m_BuiltMaskReach = reach;
            m_BuiltDetail = frame.FlattenSize;
            var input = m_Content.ToInput(frame, color, false);
            if (requireDistance)
            {
                input.requireDistance = true;
                input.distanceReach = reach;
            }

            m_Renderer.Build(input);
            m_FullBuild = false;
            var mesh = WorkerMesh();
            m_Renderer.buffer.Upload(mesh);
            canvasRenderer.SetMesh(mesh);
            m_LastScale = CurrentScale();

            if (m_LastTexture != m_Renderer.mainTexture)
            {
                m_LastTexture = m_Renderer.mainTexture;
                canvasRenderer.SetTexture(mainTexture);
            }
        }

        public override bool Raycast(Vector2 sp, Camera eventCamera)
        {
            if (!base.Raycast(sp, eventCamera)) return false;
            if (!m_PreciseRaycast) return true;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, sp, eventCamera, out var local))
                return false;
            return ContainsLocalPoint(local);
        }

        /// <summary>Exact hit test against the filled area and the strokes (local coordinates).</summary>
        public bool ContainsLocalPoint(Vector2 local)
        {
            var frame = Frame;
            float strokeReach = 0f;
            foreach (var s in m_Content.ActiveStrokes)
            {
                if (s == null || !s.enabled) continue;
                float sw = s.EffectiveWidth(frame.pixelSize);
                float w = s.alignment == StrokeAlignment.Center ? sw * 0.5f : s.alignment == StrokeAlignment.Outside ? sw : 0f;
                strokeReach = math.max(strokeReach, w);
            }

            m_HitTester ??= new VectraHitTester();
            return m_HitTester.Contains(m_Content.ActiveGeometry, frame, local, strokeReach);
        }

        // ILayoutElement: shapes have no intrinsic size.
        public virtual void CalculateLayoutInputHorizontal() { }
        public virtual void CalculateLayoutInputVertical() { }
        public virtual float minWidth => 0f;
        public virtual float preferredWidth => -1f;
        public virtual float flexibleWidth => -1f;
        public virtual float minHeight => 0f;
        public virtual float preferredHeight => -1f;
        public virtual float flexibleHeight => -1f;
        public virtual int layoutPriority => 0;

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            m_Renderer?.Invalidate();
            SetMaterialDirty();
        }

        protected override void Reset()
        {
            base.Reset();
            m_Content = new VectraShapeContent();
            m_Content.fills[0].paint = VectraPaint.Solid(new Color(0.24f, 0.55f, 0.96f));
        }
#endif
    }
}
