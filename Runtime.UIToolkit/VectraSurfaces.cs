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
        static readonly Stack<SurfaceShelf> s_ShelfPool = new Stack<SurfaceShelf>();
        static CommandBuffer s_Cmd;
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

                // Slots are a multiple of eight wide like the rows are tall, so that a size that moves by a pixel or two (an animation) finds the
                // slot it freed instead of scanning a list of slots that are one pixel too small.
                int slot = (w + 7) & ~7;
                if (shelf.cursor + slot <= page.width) return Take(page, shelf, shelf.cursor, slot, w, h, out region, true);
            }

            if (page.nextY + hr > page.height) return false;
            // Shelves come and go as sizes change (an animated size frees a row and asks for another); reuse them with their free lists.
            var s = s_ShelfPool.Count > 0 ? s_ShelfPool.Pop() : new SurfaceShelf();
            s.y = page.nextY;
            s.height = hr;
            s.cursor = 0;
            s.live = 0;
            page.nextY += hr;
            page.shelves.Add(s);
            return Take(page, s, 0, (w + 7) & ~7, w, h, out region, true);
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
                last.free.Clear();
                s_ShelfPool.Push(last);
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
            System.Array.Clear(s_Pending, 0, s_Pending.Length);
            s_PendingCount = 0;
            for (int i = 0; i < s_BatchMeshes.Length; i++)
            {
                DestroyHelper(s_BatchMeshes[i]);
                s_BatchMeshes[i] = null;
            }

            s_Blocks.Clear();
            s_ShelfPool.Clear();
            for (int i = s_Pages.Count - 1; i >= 0; i--) DestroyPage(s_Pages[i]);
            s_Pages.Clear();
            s_Cmd?.Release();
            s_Cmd = null;
            DestroyHelper(s_Quad);
            DestroyHelper(s_ClearMaterial);
            s_Quad = null;
            s_ClearMaterial = null;
        }

        /// <summary>Scratch meshes a batch of draws is built over; a full pool runs the batch.</summary>
        public const int BatchSize = 32;

        struct PendingDraw
        {
            public SurfaceRegion region;
            public Material material;
            public Texture main;
            public Rect area;
        }

        static readonly PendingDraw[] s_Pending = new PendingDraw[BatchSize];
        static readonly Mesh[] s_BatchMeshes = new Mesh[BatchSize];
        static int s_PendingCount;
        static readonly Dictionary<Texture, MaterialPropertyBlock> s_Blocks = new Dictionary<Texture, MaterialPropertyBlock>();

        /// <summary>Draws still waiting for <see cref="Flush"/>.</summary>
        public static int PendingCount => s_PendingCount;

        /// <summary>
        /// The mesh the next <see cref="Queue"/> call draws: upload the shape into it first. Meshes are shared by every element (a draw only
        /// needs its mesh until the batch runs), so nothing is kept per element.
        /// </summary>
        public static Mesh NextMesh()
        {
            if (s_PendingCount >= BatchSize) Flush();
            var mesh = s_BatchMeshes[s_PendingCount];
            if (mesh == null) s_BatchMeshes[s_PendingCount] = mesh = new Mesh { name = "Vectra UI Toolkit", hideFlags = HideFlags.HideAndDontSave };
            return mesh;
        }

        /// <summary>
        /// Queues the draw of the mesh from <see cref="NextMesh"/> into the region; false when it cannot be drawn. <paramref name="area"/> is the
        /// local-space rectangle (y up) that the region's pixels cover. The batch runs before any element shows a region (<see cref="Flush"/>).
        /// </summary>
        public static bool Queue(in SurfaceRegion region, Material material, Texture main, Rect area)
        {
            if (!region.IsValid || material == null || ClearMaterial == null || s_PendingCount >= BatchSize) return false;
            s_Pending[s_PendingCount++] = new PendingDraw { region = region, material = material, main = main, area = area };
            return true;
        }

        static readonly Unity.Profiling.ProfilerMarker s_DrawMarker = new Unity.Profiling.ProfilerMarker("Vectra.Uitk.Draw");

        static MaterialPropertyBlock BlockFor(Texture main)
        {
            var key = main != null ? main : Texture2D.whiteTexture;
            if (s_Blocks.TryGetValue(key, out var block) && block != null) return block;
            if (s_Blocks.Count > 64) s_Blocks.Clear();
            block = new MaterialPropertyBlock();
            block.SetTexture(s_MainTex, key);
            s_Blocks[key] = block;
            return block;
        }

        /// <summary>
        /// Draws the queued shapes into their regions with one command buffer. A region is cleared by drawing a transparent quad into its
        /// viewport and the shape is drawn straight into the page: no scissor clear and no texture copy, which some platforms (WebGL 2) do not
        /// offer for render textures.
        /// </summary>
        public static void Flush()
        {
            int n = s_PendingCount;
            if (n == 0) return;
            s_PendingCount = 0;
            using (s_DrawMarker.Auto())
            {
                var clear = ClearMaterial;
                if (clear != null)
                {
                    VectraAtlases.Flush();
                    s_Cmd ??= new CommandBuffer { name = "Vectra UI Toolkit" };
                    s_Quad ??= MakeQuad();
                    var cmd = s_Cmd;
                    var previous = RenderTexture.active;
                    SurfacePage current = null;
                    var identity = Matrix4x4.identity;
                    for (int i = 0; i < n; i++)
                    {
                        ref var d = ref s_Pending[i];
                        var page = d.region.page;
                        if (page == null || page.texture == null) continue;
                        if (!ReferenceEquals(page, current))
                        {
                            cmd.SetRenderTarget(page.texture);
                            current = page;
                        }

                        cmd.SetViewport(new Rect(d.region.x, d.region.y, d.region.w, d.region.h));
                        cmd.SetViewProjectionMatrices(identity, identity);
                        cmd.DrawMesh(s_Quad, identity, clear, 0, 0);
                        var area = d.area;
                        var proj = Matrix4x4.Ortho(area.xMin, area.xMax, area.yMin, area.yMax, -1f, 1f);
                        cmd.SetViewProjectionMatrices(identity, GL.GetGPUProjectionMatrix(proj, true));
                        cmd.DrawMesh(s_BatchMeshes[i], identity, d.material, 0, 0, BlockFor(d.main));
                        DrawCount++;
                    }

                    Graphics.ExecuteCommandBuffer(cmd);
                    cmd.Clear();
                    Graphics.SetRenderTarget(previous);
                }
            }

            System.Array.Clear(s_Pending, 0, n);
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
