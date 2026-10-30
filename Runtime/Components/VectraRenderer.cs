using System.Collections.Generic;
using FirstSymbol.Vectra.Geometry;
using FirstSymbol.Vectra.Rendering;
using Unity.Mathematics;
using UnityEngine;

namespace FirstSymbol.Vectra
{
    /// <summary>
    /// Vector shape rendered by a <see cref="MeshRenderer"/> in world space (2D or 3D scenes). Uses the same shapes,
    /// paints, strokes and effects as <see cref="VectraShape"/>. Sizes are in world units of the object.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [AddComponentMenu("Vectra/Vectra Renderer (World)")]
    public class VectraRenderer : MonoBehaviour, IVectraAtlasClient, IVectraShape
    {
        [SerializeField] VectraShapeContent m_Content = new VectraShapeContent();
        [SerializeField] Vector2 m_Size = new Vector2(1f, 1f);
        [SerializeField] Vector2 m_Pivot = new Vector2(0.5f, 0.5f);
        [SerializeField] Color m_Color = Color.white;
        [Tooltip("Size of a screen pixel in local units (0 = estimate from the camera every frame).")]
        [SerializeField] float m_PixelSize;
        [SerializeField] int m_SortingOrder;
        [SerializeField] string m_SortingLayer = "Default";
        [Tooltip("Generate a PolygonCollider2D outline when the shape changes.")]
        [SerializeField] bool m_GenerateCollider;

        VectraShapeRenderer m_Renderer;
        VectraHitTester m_HitTester;
        Mesh m_Mesh;
        MeshRenderer m_MeshRenderer;
        MeshFilter m_MeshFilter;
        MaterialPropertyBlock m_Block;
        bool m_Dirty = true;
        float m_BuiltPixelSize;

        static readonly int s_MainTex = Shader.PropertyToID("_MainTex");
        static readonly List<VectraRenderer> s_StyleUsers = new List<VectraRenderer>();
        static bool s_StyleHooked;

        public VectraShapeContent Content => m_Content;
        public VectraBuildStats Stats => m_Renderer?.stats ?? default;

        public VectraShapeGeometry Geometry
        {
            get => m_Content.geometry;
            set
            {
                m_Content.geometry = value;
                SetDirty();
            }
        }

        public Vector2 Size
        {
            get => m_Size;
            set
            {
                if (m_Size == value) return;
                m_Size = value;
                SetDirty();
            }
        }

        public Vector2 Pivot
        {
            get => m_Pivot;
            set
            {
                m_Pivot = value;
                SetDirty();
            }
        }

        public Color Color
        {
            get => m_Color;
            set
            {
                m_Color = value;
                SetDirty();
            }
        }

        public int SortingOrder
        {
            get => m_SortingOrder;
            set
            {
                m_SortingOrder = value;
                ApplyRendererSettings();
            }
        }

        public List<FillLayer> Fills => m_Content.ActiveFills;
        public List<StrokeLayer> Strokes => m_Content.ActiveStrokes;
        public List<VectraEffect> Effects => m_Content.ActiveEffects;

        public Rect LocalRect => new Rect(-m_Size.x * m_Pivot.x, -m_Size.y * m_Pivot.y, m_Size.x, m_Size.y);

        public T SetGeometry<T>() where T : VectraShapeGeometry, new()
        {
            if (!(m_Content.geometry is T g))
            {
                g = new T();
                m_Content.geometry = g;
            }

            SetDirty();
            return g;
        }

        public void SetDirty() => m_Dirty = true;

        /// <summary>Call after changing geometry, fills, strokes or effects from code.</summary>
        public void SetShapeDirty() => m_Dirty = true;

        // Animator / Timeline wrote serialized fields directly.
        void OnDidApplyAnimationProperties() => m_Dirty = true;

        void OnEnable()
        {
            m_MeshFilter = GetComponent<MeshFilter>();
            m_MeshRenderer = GetComponent<MeshRenderer>();
            VectraAtlases.Register(this);
            if (!s_StyleHooked)
            {
                s_StyleHooked = true;
                VectraStyle.Changed += style =>
                {
                    foreach (var r in s_StyleUsers)
                        if (r != null && r.m_Content.style == style) r.SetDirty();
                };
                VectraPalette.Changed += () =>
                {
                    foreach (var r in s_StyleUsers)
                        if (r != null) r.SetDirty();
                };
            }

            s_StyleUsers.Add(this);
            ApplyRendererSettings();
            m_Dirty = true;
            Rebuild();
        }

        void OnDisable()
        {
            VectraAtlases.Unregister(this);
            s_StyleUsers.Remove(this);
        }

        void OnDestroy()
        {
            m_Renderer?.Dispose();
            m_Renderer = null;
            m_HitTester?.Dispose();
            m_HitTester = null;
            if (m_Mesh != null)
            {
                if (Application.isPlaying) Destroy(m_Mesh);
                else DestroyImmediate(m_Mesh);
            }
        }

        void IVectraAtlasClient.OnAtlasInvalidated()
        {
            m_Renderer?.Invalidate();
            m_Dirty = true;
        }

        void ApplyRendererSettings()
        {
            if (m_MeshRenderer == null) return;
            m_MeshRenderer.sharedMaterial = VectraMaterials.GetWorld(m_Content.blendMode);
            m_MeshRenderer.sortingOrder = m_SortingOrder;
            if (!string.IsNullOrEmpty(m_SortingLayer)) m_MeshRenderer.sortingLayerName = m_SortingLayer;
            m_MeshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_MeshRenderer.receiveShadows = false;
        }

        void LateUpdate()
        {
            if (!m_Dirty && m_PixelSize <= 0f)
            {
                float px = CurrentPixelSize();
                if (m_BuiltPixelSize > 0f && (px > m_BuiltPixelSize * 1.5f || px < m_BuiltPixelSize / 1.5f)) m_Dirty = true;
            }

            if (m_Dirty) Rebuild();
        }

        float CurrentPixelSize()
        {
            if (m_PixelSize > 0f) return m_PixelSize;
            var s = transform.lossyScale;
            return EstimatePixelSize(Camera.main, transform.position, math.max(math.abs(s.x), math.abs(s.y)));
        }

        /// <summary>Estimates the size of a screen pixel, in local units, for an object seen by <paramref name="cam"/>.</summary>
        public static float EstimatePixelSize(Camera cam, Vector3 worldPos, float lossyScale)
        {
            lossyScale = math.max(lossyScale, 1e-6f);
            if (cam == null) return 0.01f / lossyScale;
            float h = math.max(cam.pixelHeight, 1);
            float worldPerPixel;
            if (cam.orthographic) worldPerPixel = cam.orthographicSize * 2f / h;
            else
            {
                float dist = math.max(math.abs(Vector3.Dot(worldPos - cam.transform.position, cam.transform.forward)), cam.nearClipPlane);
                worldPerPixel = 2f * dist * math.tan(math.radians(cam.fieldOfView) * 0.5f) / h;
            }

            return worldPerPixel / lossyScale;
        }

        public ShapeFrame Frame => new ShapeFrame { rect = LocalRect, pixelSize = CurrentPixelSize() };

        /// <summary>Rebuilds the mesh immediately.</summary>
        public void Rebuild()
        {
            m_Dirty = false;
            if (m_Renderer == null) m_Renderer = new VectraShapeRenderer();
            if (m_Mesh == null)
            {
                m_Mesh = new Mesh { name = "Vectra Renderer", hideFlags = HideFlags.HideAndDontSave };
                m_Mesh.MarkDynamic();
            }

            if (m_MeshFilter == null) m_MeshFilter = GetComponent<MeshFilter>();
            var frame = Frame;
            m_BuiltPixelSize = frame.pixelSize;
            m_Renderer.Build(m_Content.ToInput(frame, m_Color, true));
            m_Renderer.buffer.Upload(m_Mesh);
            m_MeshFilter.sharedMesh = m_Mesh;

            if (m_MeshRenderer != null)
            {
                if (m_MeshRenderer.sharedMaterial == null || m_MeshRenderer.sharedMaterial != VectraMaterials.GetWorld(m_Content.blendMode))
                    ApplyRendererSettings();
                m_Block ??= new MaterialPropertyBlock();
                m_MeshRenderer.GetPropertyBlock(m_Block);
                m_Block.SetTexture(s_MainTex, m_Renderer.mainTexture != null ? m_Renderer.mainTexture : Texture2D.whiteTexture);
                m_MeshRenderer.SetPropertyBlock(m_Block);
            }

            if (m_GenerateCollider) UpdateCollider();
        }

        /// <summary>Exact hit test in local coordinates.</summary>
        public bool ContainsLocalPoint(Vector2 local)
        {
            m_HitTester ??= new VectraHitTester();
            float reach = 0f;
            foreach (var s in m_Content.ActiveStrokes)
                if (s != null && s.enabled) reach = math.max(reach, s.alignment == StrokeAlignment.Inside ? 0f : s.width * 0.5f);
            return m_HitTester.Contains(m_Content.geometry, Frame, local, reach);
        }

        public bool ContainsWorldPoint(Vector3 world) => ContainsLocalPoint(transform.InverseTransformPoint(world));

        void UpdateCollider()
        {
            if (!TryGetComponent(out PolygonCollider2D col)) col = gameObject.AddComponent<PolygonCollider2D>();
            var paths = VectraOutline.GetOutline(m_Content.geometry, Frame);
            col.pathCount = paths.Count;
            for (int i = 0; i < paths.Count; i++) col.SetPath(i, paths[i]);
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            m_Dirty = true;
            m_Renderer?.Invalidate();
            if (m_MeshRenderer == null) m_MeshRenderer = GetComponent<MeshRenderer>();
            ApplyRendererSettings();
        }

        void Reset()
        {
            m_Content = new VectraShapeContent();
            m_Content.fills[0].paint = VectraPaint.Solid(new Color(0.24f, 0.55f, 0.96f));
            if (m_Content.geometry is RectangleShape r) r.cornerRadii = new Vector4(0.15f, 0.15f, 0.15f, 0.15f);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
            var r = LocalRect;
            Gizmos.DrawWireCube(r.center, r.size);
        }
#endif
    }

    /// <summary>Helpers to extract outlines (for colliders, exports and editor tools).</summary>
    public static class VectraOutline
    {
        /// <summary>Returns the filled area's outline as closed polygons (counter-clockwise outer, clockwise holes).</summary>
        public static List<Vector2[]> GetOutline(VectraShapeGeometry geometry, in ShapeFrame frame, float tolerance = 0f)
        {
            var result = new List<Vector2[]>();
            if (geometry == null) return result;
            var path = new NativePath(Unity.Collections.Allocator.TempJob, 32);
            var poly = new Polylines(Unity.Collections.Allocator.TempJob, 64);
            var traps = new Unity.Collections.NativeList<Trapezoid>(64, Unity.Collections.Allocator.TempJob);
            var outline = new Polylines(Unity.Collections.Allocator.TempJob, 64);
            var b = new PathBuilder(path);
            geometry.BuildPath(frame, b);
            float tol = tolerance > 0f ? tolerance : math.max(frame.pixelSize, 1e-4f);
            VectraGeometry.Flatten(path, Affine2.Identity, tol, 0, true, ref poly);
            VectraGeometry.Tessellate(poly, geometry.FillRule, ref traps);
            VectraGeometry.BuildContours(traps.AsArray(), ref outline);
            for (int ci = 0; ci < outline.contours.Length; ci++)
            {
                var c = outline.contours[ci];
                var arr = new Vector2[c.count];
                for (int i = 0; i < c.count; i++) arr[i] = outline.points[c.start + i];
                result.Add(arr);
            }

            path.Dispose();
            poly.Dispose();
            traps.Dispose();
            outline.Dispose();
            return result;
        }
    }
}
