using System;
using FirstSymbol.Vectra.Rendering;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Vectra.UIToolkit
{
    /// <summary>
    /// One drawn layer of a UI Toolkit element: the Vectra renderer that builds the mesh, the surface region the mesh is
    /// rasterized into, and the textured quad that shows the region.
    /// </summary>
    sealed class VectraRasterLayer : IDisposable
    {
        public readonly VectraShapeRenderer renderer = new VectraShapeRenderer();
        Mesh m_Mesh;
        SurfaceRegion m_Region;
        Rect m_Quad;
        Rect m_Uv;
        bool m_Ready;

        /// <summary>Pixels per point the layer was rasterized for.</summary>
        public float builtScale;
        public int rasterCount;
        public Vector2Int pixelSize => new Vector2Int(m_Region.w, m_Region.h);
        public bool HasImage => m_Ready && m_Region.IsValid;
        public Texture Texture => m_Region.Texture;
        /// <summary>The quad in element space (y down), including the one-pixel border.</summary>
        public Rect Quad => m_Quad;
        public Rect Uv => m_Uv;

        static readonly Vertex[] s_Vertices = new Vertex[4];

        /// <summary>
        /// Rasterizes what <see cref="renderer"/> has built. <paramref name="center"/> is where the local origin sits in
        /// element space (y down); <paramref name="pixelsPerPoint"/> is the screen density the layer is drawn for.
        /// </summary>
        static readonly Unity.Profiling.ProfilerMarker s_RasterizeMarker = new Unity.Profiling.ProfilerMarker("Vectra.Uitk.Rasterize");

        public void Rasterize(Vector2 center, float pixelsPerPoint, ShapeBlendMode blend = ShapeBlendMode.Normal)
        {
            using (s_RasterizeMarker.Auto()) RasterizeCore(center, pixelsPerPoint, blend);
        }

        void RasterizeCore(Vector2 center, float pixelsPerPoint, ShapeBlendMode blend)
        {
            rasterCount++;
            var buf = renderer.buffer;
            var b = buf.bounds;
            if (buf.VertexCount == 0 || !b.IsValid)
            {
                Release();
                return;
            }

            float s = math.max(pixelsPerPoint, 1e-3f);
            // Element space, y down.
            float ex0 = center.x + b.min.x, ex1 = center.x + b.max.x;
            float ey0 = center.y - b.max.y, ey1 = center.y - b.min.y;
            float limit = VectraSurfaces.MaxSize - 2f;
            s = math.min(s, limit / math.max(ex1 - ex0, 1e-3f));
            s = math.min(s, limit / math.max(ey1 - ey0, 1e-3f));
            int x0 = (int)math.floor(ex0 * s) - 1, x1 = (int)math.ceil(ex1 * s) + 1;
            int y0 = (int)math.floor(ey0 * s) - 1, y1 = (int)math.ceil(ey1 * s) + 1;
            int w = x1 - x0, h = y1 - y0;
            if (m_Region.IsValid && (m_Region.w != w || m_Region.h != h)) Release();
            if (!m_Region.IsValid && !VectraSurfaces.TryAllocate(w, h, out m_Region)) return;

            builtScale = s;
            m_Quad = new Rect(x0 / s, y0 / s, w / s, h / s);
            // Local space (y up) covered by the region.
            var area = Rect.MinMaxRect(x0 / s - center.x, center.y - y1 / s, x1 / s - center.x, center.y - y0 / s);
            if (m_Mesh == null) m_Mesh = new Mesh { name = "Vectra UI Toolkit", hideFlags = HideFlags.HideAndDontSave };
            buf.Upload(m_Mesh);
            var material = VectraMaterials.GetUI(blend);
            m_Ready = VectraSurfaces.Draw(m_Region, m_Mesh, material, renderer.mainTexture, area);
            var ps = m_Region.PageSize;
            m_Uv = Rect.MinMaxRect((float)m_Region.x / ps.x, (float)m_Region.y / ps.y,
                (float)(m_Region.x + m_Region.w) / ps.x, (float)(m_Region.y + m_Region.h) / ps.y);
        }

        /// <summary>Adds the layer's quad to the element being drawn.</summary>
        public void Emit(MeshGenerationContext mgc, Color32 tint)
        {
            var tex = m_Region.Texture;
            if (!m_Ready || tex == null) return;
            var q = m_Quad;
            var uv = m_Uv;
            // The projection used for the render texture comes out flipped when sampled by UI Toolkit on platforms whose
            // texture origin is the top (Direct3D, Metal, Vulkan, WebGPU).
            float vTop = SystemInfo.graphicsUVStartsAtTop ? uv.yMin : uv.yMax;
            float vBottom = SystemInfo.graphicsUVStartsAtTop ? uv.yMax : uv.yMin;
            mgc.AllocateTempMesh(4, 6, out NativeSlice<Vertex> v, out NativeSlice<ushort> idx);
            s_Vertices[0] = new Vertex { position = new Vector3(q.xMin, q.yMin, Vertex.nearZ), tint = tint, uv = new Vector2(uv.xMin, vTop) };
            s_Vertices[1] = new Vertex { position = new Vector3(q.xMax, q.yMin, Vertex.nearZ), tint = tint, uv = new Vector2(uv.xMax, vTop) };
            s_Vertices[2] = new Vertex { position = new Vector3(q.xMax, q.yMax, Vertex.nearZ), tint = tint, uv = new Vector2(uv.xMax, vBottom) };
            s_Vertices[3] = new Vertex { position = new Vector3(q.xMin, q.yMax, Vertex.nearZ), tint = tint, uv = new Vector2(uv.xMin, vBottom) };
            for (int i = 0; i < 4; i++) v[i] = s_Vertices[i];
            idx[0] = 0;
            idx[1] = 1;
            idx[2] = 2;
            idx[3] = 2;
            idx[4] = 3;
            idx[5] = 0;
            mgc.DrawMesh(v, idx, tex, TextureOptions.PremultipliedAlpha | TextureOptions.SkipDynamicAtlas);
        }

        public void Release()
        {
            m_Ready = false;
            if (m_Region.IsValid) VectraSurfaces.Free(ref m_Region);
            m_Quad = default;
        }

        /// <summary>False when the render texture was lost (a device reset); the layer must be rasterized again.</summary>
        public bool IsAlive => !m_Region.IsValid || (m_Region.Texture != null && m_Region.Texture.IsCreated());

        public void Dispose()
        {
            Release();
            renderer.Dispose();
            if (m_Mesh != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(m_Mesh);
                else UnityEngine.Object.DestroyImmediate(m_Mesh);
            }

            m_Mesh = null;
        }
    }
}
