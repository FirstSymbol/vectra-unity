using System.Collections.Generic;
using FirstSymbol.Vectra.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace FirstSymbol.Vectra.UIToolkit
{
    sealed class SurfaceShelf
    {
        public int y, height, cursor, live;
        public readonly List<Vector2Int> free = new List<Vector2Int>();
    }

    sealed class SurfacePage
    {
        public RenderTexture texture;
        public bool dedicated;
        public int width, height, nextY, live;
        public readonly List<SurfaceShelf> shelves = new List<SurfaceShelf>();
    }

    /// <summary>A rectangle of a surface page that holds the pixels of one rasterized layer.</summary>
    struct SurfaceRegion
    {
        internal SurfacePage page;
        internal SurfaceShelf shelf;
        internal int slotWidth;
        public int x, y, w, h;
        public bool IsValid => page != null;
        public RenderTexture Texture => page?.texture;
        public Vector2Int PageSize => page != null ? new Vector2Int(page.width, page.height) : default;
    }

    /// <summary>
    /// Pixel storage for Vectra in UI Toolkit. UI Toolkit meshes carry only position, colour and one uv, while a Vectra
    /// shape needs four float4 per vertex, so the shape is drawn by its own material into a render texture and the
    /// element shows that texture. Small layers share pages (shelf packing), so UI Toolkit batches their quads into one
    /// draw call; large ones get a render texture of their own.
    /// </summary>
    static class VectraSurfaces
    {
        public const int PageSize = 2048;
        public const int MaxShared = 512;
        public const int MaxSize = 4096;

        static readonly List<SurfacePage> s_Pages = new List<SurfacePage>();
        static CommandBuffer s_Cmd;
        static MaterialPropertyBlock s_Block;
        static Material s_ClearMaterial;
        static Mesh s_Quad;
        static readonly int s_MainTex = Shader.PropertyToID("_MainTex");

        /// <summary>Number of render textures alive (shared pages and dedicated ones).</summary>
        public static int PageCount => s_Pages.Count;

        public static int DrawCount { get; private set; }

        public static bool TryAllocate(int w, int h, out SurfaceRegion region)
        {
            region = default;
            if (w <= 0 || h <= 0 || w > MaxSize || h > MaxSize) return false;
            if (w > MaxShared || h > MaxShared)
            {
                var p = NewPage(w, h, true);
                region = new SurfaceRegion { page = p, x = 0, y = 0, w = w, h = h, slotWidth = w };
                p.live = 1;
                return true;
            }

            int hr = (h + 7) & ~7;
            foreach (var page in s_Pages)
            {
                if (page.dedicated) continue;
                if (TryShelf(page, w, hr, h, out region)) return true;
            }

            var np = NewPage(PageSize, PageSize, false);
            return TryShelf(np, w, hr, h, out region);
        }

        static bool TryShelf(SurfacePage page, int w, int hr, int h, out SurfaceRegion region)
        {
            region = default;
            foreach (var shelf in page.shelves)
            {
                if (shelf.height < hr || shelf.height > hr + 16) continue;
                for (int i = 0; i < shelf.free.Count; i++)
                {
                    var seg = shelf.free[i];
                    if (seg.y < w) continue;
                    shelf.free.RemoveAt(i);
                    return Take(page, shelf, seg.x, seg.y, w, h, out region);
                }

                if (shelf.cursor + w <= page.width) return Take(page, shelf, shelf.cursor, w, w, h, out region, true);
            }

            if (page.nextY + hr > page.height) return false;
            var s = new SurfaceShelf { y = page.nextY, height = hr };
            page.nextY += hr;
            page.shelves.Add(s);
            return Take(page, s, 0, w, w, h, out region, true);
        }

        static bool Take(SurfacePage page, SurfaceShelf shelf, int x, int slot, int w, int h, out SurfaceRegion region, bool advance = false)
        {
            if (advance) shelf.cursor = x + slot;
            shelf.live++;
            page.live++;
            region = new SurfaceRegion { page = page, shelf = shelf, slotWidth = slot, x = x, y = shelf.y, w = w, h = h };
            return true;
        }

        static SurfacePage NewPage(int w, int h, bool dedicated)
        {
            var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = dedicated ? "Vectra UI Toolkit surface" : "Vectra UI Toolkit page",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false
            };
            rt.Create();
            var page = new SurfacePage { texture = rt, dedicated = dedicated, width = w, height = h };
            s_Pages.Add(page);
            return page;
        }

        /// <summary>Gives a region back; its shelf slot becomes reusable.</summary>
        public static void Free(ref SurfaceRegion region)
        {
            var page = region.page;
            var shelf = region.shelf;
            var slot = new Vector2Int(region.x, region.slotWidth);
            region = default;
            if (page == null) return;
            page.live--;
            if (page.dedicated)
            {
                DestroyPage(page);
                return;
            }

            shelf.live--;
            shelf.free.Add(slot);
            // An empty last shelf gives its rows back so the page can serve another height.
            while (page.shelves.Count > 0)
            {
                var last = page.shelves[page.shelves.Count - 1];
                if (last.live > 0) break;
                page.shelves.RemoveAt(page.shelves.Count - 1);
                page.nextY = last.y;
            }

            if (page.live <= 0 && s_Pages.Count > 1) DestroyPage(page);
        }

        static void DestroyPage(SurfacePage page)
        {
            s_Pages.Remove(page);
            if (page.texture == null) return;
            page.texture.Release();
            if (Application.isPlaying) Object.Destroy(page.texture);
            else Object.DestroyImmediate(page.texture);
            page.texture = null;
        }

        static void DestroyHelper(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }

        public static void DestroyAll()
        {
            for (int i = s_Pages.Count - 1; i >= 0; i--) DestroyPage(s_Pages[i]);
            s_Pages.Clear();
            s_Cmd?.Release();
            s_Cmd = null;
            DestroyHelper(s_Quad);
            DestroyHelper(s_ClearMaterial);
            s_Quad = null;
            s_ClearMaterial = null;
        }

        /// <summary>
        /// Draws <paramref name="mesh"/> into the region. <paramref name="area"/> is the local-space rectangle (y up) that
        /// the region's pixels cover. The region is cleared by drawing a transparent quad into its viewport and the shape is
        /// drawn straight into the page: no scissor clear and no texture copy, which some platforms (WebGL 2) do not offer
        /// for render textures.
        /// </summary>
        public static bool Draw(in SurfaceRegion region, Mesh mesh, Material material, Texture main, Rect area)
        {
            if (!region.IsValid || mesh == null || material == null) return false;
            var clear = ClearMaterial;
            if (clear == null) return false;
            VectraAtlases.Flush();
            s_Cmd ??= new CommandBuffer { name = "Vectra UI Toolkit" };
            s_Block ??= new MaterialPropertyBlock();
            s_Quad ??= MakeQuad();
            var cmd = s_Cmd;
            var page = region.page;
            var previous = RenderTexture.active;
            cmd.SetRenderTarget(page.texture);
            cmd.SetViewport(new Rect(region.x, region.y, region.w, region.h));
            cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            cmd.DrawMesh(s_Quad, Matrix4x4.identity, clear, 0, 0);
            var proj = Matrix4x4.Ortho(area.xMin, area.xMax, area.yMin, area.yMax, -1f, 1f);
            cmd.SetViewProjectionMatrices(Matrix4x4.identity, GL.GetGPUProjectionMatrix(proj, true));
            s_Block.Clear();
            s_Block.SetTexture(s_MainTex, main != null ? main : Texture2D.whiteTexture);
            cmd.DrawMesh(mesh, Matrix4x4.identity, material, 0, 0, s_Block);
            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            Graphics.SetRenderTarget(previous);
            DrawCount++;
            return true;
        }

        static Material ClearMaterial
        {
            get
            {
                if (s_ClearMaterial != null) return s_ClearMaterial;
                var shader = Resources.Load<Shader>("Vectra/Vectra-UitkClear") ?? Shader.Find("Hidden/Vectra/UitkClear");
                if (shader == null) return null;
                s_ClearMaterial = new Material(shader) { name = "Vectra UI Toolkit clear", hideFlags = HideFlags.HideAndDontSave };
                return s_ClearMaterial;
            }
        }

        static Mesh MakeQuad()
        {
            var m = new Mesh { name = "Vectra UI Toolkit quad", hideFlags = HideFlags.HideAndDontSave };
            m.vertices = new[] { new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f), new Vector3(1f, 1f, 0f), new Vector3(-1f, 1f, 0f) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.bounds = new Bounds(Vector3.zero, new Vector3(4f, 4f, 4f));
            return m;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            // Without a domain reload (Enter Play Mode options) the pages of the last session are still alive; elements that
            // still hold a region notice the lost texture in their next poll and draw again.
            DestroyAll();
            s_Block = null;
            DrawCount = 0;
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void HookReload()
        {
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += DestroyAll;
            UnityEditor.EditorApplication.quitting += DestroyAll;
        }
#endif
    }
}
