using System.Collections.Generic;
using FirstSymbol.Vectra.Rendering;
using Unity.Collections;
using Unity.Jobs;
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
            int regionX = region.x, regionY = region.y;
            region = default;
            if (page == null) return;
            if (s_PendingCount > 0) CancelPending(page, regionX, regionY);
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
            DestroyHelper(s_BatchMesh);
            s_BatchMesh = null;
            if (s_StageVertices.IsCreated) s_StageVertices.Dispose();
            if (s_StageIndices.IsCreated) s_StageIndices.Dispose();
            s_Blocks.Clear();
            s_ShelfPool.Clear();
            for (int i = s_Pages.Count - 1; i >= 0; i--) DestroyPage(s_Pages[i]);
            s_Pages.Clear();
            s_Cmd?.Release();
            s_Cmd = null;
            DestroyHelper(s_Quad);
            DestroyHelper(s_ClearMaterial);
            for (int i = 0; i < s_ClearMeshes.Length; i++)
            {
                DestroyHelper(s_ClearMeshes[i]);
                s_ClearMeshes[i] = null;
            }

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

        // The slots of one run of draws into a page are cleared by one draw of a mesh of quads (one per slot) instead of one draw each.
        const int MaxClearRuns = 8;
        static readonly Mesh[] s_ClearMeshes = new Mesh[MaxClearRuns];
        static readonly Vector3[] s_ClearVertices = new Vector3[BatchSize * 4];
        static int[] s_ClearIndices;

        /// <summary>
        /// A region that is given back no longer needs the draw that is still waiting for it. Without this the batch, which clears all the
        /// slots of a run before it draws, would draw the old owner's picture under the picture of the slot's next owner.
        /// </summary>
        static void CancelPending(SurfacePage page, int x, int y)
        {
            for (int i = 0; i < s_PendingCount; i++)
            {
                ref var d = ref s_Pending[i];
                if (d.material != null && ReferenceEquals(d.region.page, page) && d.region.x == x && d.region.y == y) d.material = null;
            }
        }
        // The shapes of a batch are staged side by side and uploaded to one mesh, one submesh each: one upload instead of one per shape.
        static Mesh s_BatchMesh;
        static NativeList<VectraVertex> s_StageVertices;
        static NativeList<int> s_StageIndices;
        static readonly SubMeshDescriptor[] s_Submeshes = new SubMeshDescriptor[BatchSize];
        const int MaxStagedVertices = 262144;
        static int s_PendingCount;
        static readonly Dictionary<Texture, MaterialPropertyBlock> s_Blocks = new Dictionary<Texture, MaterialPropertyBlock>();

        /// <summary>Draws still waiting for <see cref="Flush"/>.</summary>
        public static int PendingCount => s_PendingCount;

        static bool s_Subscribed;

        /// <summary>
        /// Queues the draw of the shape in <paramref name="buffer"/> into the region (the vertices are copied: the buffer may change at once); false
        /// when it cannot be drawn. <paramref name="area"/> is the local-space rectangle (y up) that the region's pixels cover. The batch runs before
        /// any element shows a region (<see cref="Flush"/>).
        /// </summary>
        public static bool Queue(in SurfaceRegion region, Material material, Texture main, Rect area, VectraMeshBuffer buffer)
        {
            if (!s_Subscribed)
            {
                // Queued draws were recorded against the atlas layout of the moment: they run before an atlas moves anything.
                s_Subscribed = true;
                VectraAtlases.BeforeLayoutChange -= Flush;
                VectraAtlases.BeforeLayoutChange += Flush;
            }

            if (s_PendingCount >= BatchSize || (s_StageVertices.IsCreated && s_StageVertices.Length > MaxStagedVertices)) Flush();
            int vc = buffer.VertexCount, ic = buffer.indices.IsCreated ? buffer.indices.Length : 0;
            if (!region.IsValid || material == null || ClearMaterial == null || vc == 0 || ic == 0) return false;
            if (!s_StageVertices.IsCreated)
            {
                s_StageVertices = new NativeList<VectraVertex>(1024, Allocator.Persistent);
                s_StageIndices = new NativeList<int>(2048, Allocator.Persistent);
            }

            int k = s_PendingCount++;
            s_Submeshes[k] = new SubMeshDescriptor(s_StageIndices.Length, ic, MeshTopology.Triangles)
            {
                baseVertex = s_StageVertices.Length,
                firstVertex = s_StageVertices.Length,
                vertexCount = vc
            };
            s_StageVertices.AddRange(buffer.vertices.AsArray());
            s_StageIndices.AddRange(buffer.indices.AsArray());
            s_Pending[k] = new PendingDraw { region = region, material = material, main = main, area = area };
            return true;
        }

        const MeshUpdateFlags k_StageFlags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontResetBoneBounds;

        // Uploads what the batch staged. Never hand non-finite data to the GPU (the same rule as the mesh buffer's own upload).
        static Mesh UploadStage(int count)
        {
            var mesh = s_BatchMesh;
            if (mesh == null) s_BatchMesh = mesh = new Mesh { name = "Vectra UI Toolkit", hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
            var verts = s_StageVertices.AsArray();
            new SanitizeVerticesJob { vertices = verts }.Run();
            mesh.SetVertexBufferParams(verts.Length, VectraVertex.Layout);
            mesh.SetVertexBufferData(verts, 0, 0, verts.Length, 0, k_StageFlags);
            mesh.SetIndexBufferParams(s_StageIndices.Length, IndexFormat.UInt32);
            mesh.SetIndexBufferData(s_StageIndices.AsArray(), 0, 0, s_StageIndices.Length, k_StageFlags);
            mesh.SetSubMeshes(s_Submeshes, 0, count, k_StageFlags);
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(4f, 4f, 4f));
            return mesh;
        }

        static readonly Unity.Profiling.ProfilerMarker s_DrawMarker = new Unity.Profiling.ProfilerMarker("Vectra.Uitk.Draw");

        static MaterialPropertyBlock BlockFor(Texture main)
        {
            var key = main != null ? main : Texture2D.whiteTexture;
            if (s_Blocks.TryGetValue(key, out var block) && block != null) return block;
            if (s_Blocks.Count > 64) s_Blocks.Clear(); // also drops the keys of destroyed textures
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
                    var identity = Matrix4x4.identity;
                    var batchMesh = UploadStage(n);
                    int runMeshes = 0;
                    Texture lastMain = null;
                    MaterialPropertyBlock lastBlock = null;
                    // Elements of one size (a list of rows, a grid) rasterize the same local area: the projection is set once for them.
                    bool projectionSet = false;
                    Rect projectionArea = default;
                    for (int i = 0; i < n;)
                    {
                        var page = s_Pending[i].region.page;
                        if (s_Pending[i].material == null || page == null || page.texture == null)
                        {
                            i++;
                            continue;
                        }

                        // A run: the draws that follow each other into the same page. The slots of a shared page do not overlap and the draws of a
                        // run stay inside their own slots, so every slot can be cleared before the first picture of the run is drawn.
                        int end = i + 1;
                        while (end < n && ReferenceEquals(s_Pending[end].region.page, page)) end++;
                        cmd.SetRenderTarget(page.texture);
                        projectionSet = false;
                        bool together = !page.dedicated && runMeshes < MaxClearRuns;
                        if (together)
                        {
                            cmd.SetViewport(new Rect(0f, 0f, page.width, page.height));
                            cmd.SetViewProjectionMatrices(identity, identity);
                            cmd.DrawMesh(ClearRunMesh(runMeshes++, page, i, end), identity, clear, 0, 0);
                        }

                        for (int k = i; k < end; k++)
                        {
                            ref var d = ref s_Pending[k];
                            if (d.material == null) continue;
                            // Clear the whole slot, not only the picture: the quad of the picture is filtered and reads half a texel beyond it.
                            var r = d.region;
                            if (!together)
                            {
                                cmd.SetViewport(new Rect(r.x, r.y, r.slotWidth, r.shelf != null ? r.shelf.height : r.h));
                                cmd.SetViewProjectionMatrices(identity, identity);
                                projectionSet = false;
                                cmd.DrawMesh(s_Quad, identity, clear, 0, 0);
                            }

                            cmd.SetViewport(new Rect(r.x, r.y, r.w, r.h));
                            var area = d.area;
                            if (!projectionSet || area.xMin != projectionArea.xMin || area.xMax != projectionArea.xMax ||
                                area.yMin != projectionArea.yMin || area.yMax != projectionArea.yMax)
                            {
                                var proj = Matrix4x4.Ortho(area.xMin, area.xMax, area.yMin, area.yMax, -1f, 1f);
                                cmd.SetViewProjectionMatrices(identity, GL.GetGPUProjectionMatrix(proj, true));
                                projectionArea = area;
                                projectionSet = true;
                            }
                            if (!ReferenceEquals(d.main, lastMain) || lastBlock == null)
                            {
                                lastMain = d.main;
                                lastBlock = BlockFor(d.main);
                            }

                            cmd.DrawMesh(batchMesh, identity, d.material, k, 0, lastBlock);
                            DrawCount++;
                        }

                        i = end;
                    }

                    Graphics.ExecuteCommandBuffer(cmd);
                    cmd.Clear();
                    Graphics.SetRenderTarget(previous);
                }
            }

            System.Array.Clear(s_Pending, 0, n);
            if (s_StageVertices.IsCreated)
            {
                s_StageVertices.Clear();
                s_StageIndices.Clear();
            }
        }

        // One mesh of quads for the slots of a run, in the clip space of the page. The corners go through the projection the pictures use, so
        // that the slot is the same rows on every platform whichever way its render textures are flipped; page sizes are powers of two and
        // slots are whole pixels, so the corners are exact.
        static Mesh ClearRunMesh(int index, SurfacePage page, int first, int end)
        {
            if (s_ClearIndices == null)
            {
                s_ClearIndices = new int[BatchSize * 6];
                for (int q = 0; q < BatchSize; q++)
                {
                    int b = q * 4, o = q * 6;
                    s_ClearIndices[o] = b;
                    s_ClearIndices[o + 1] = b + 2;
                    s_ClearIndices[o + 2] = b + 1;
                    s_ClearIndices[o + 3] = b;
                    s_ClearIndices[o + 4] = b + 3;
                    s_ClearIndices[o + 5] = b + 2;
                }
            }

            var mesh = s_ClearMeshes[index];
            if (mesh == null) s_ClearMeshes[index] = mesh = new Mesh { name = "Vectra UI Toolkit clear", hideFlags = HideFlags.HideAndDontSave };

            var proj = GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(0f, page.width, 0f, page.height, -1f, 1f), true);
            int count = 0;
            for (int k = first; k < end; k++)
            {
                ref var d = ref s_Pending[k];
                if (d.material == null) continue;
                var r = d.region;
                float x0 = r.x, x1 = r.x + r.slotWidth;
                float y0 = r.y, y1 = r.y + (r.shelf != null ? r.shelf.height : r.h);
                int v = count * 4;
                s_ClearVertices[v] = proj.MultiplyPoint(new Vector3(x0, y0, 0f));
                s_ClearVertices[v + 1] = proj.MultiplyPoint(new Vector3(x1, y0, 0f));
                s_ClearVertices[v + 2] = proj.MultiplyPoint(new Vector3(x1, y1, 0f));
                s_ClearVertices[v + 3] = proj.MultiplyPoint(new Vector3(x0, y1, 0f));
                count++;
            }

            const MeshUpdateFlags flags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;
            mesh.Clear();
            mesh.SetVertices(s_ClearVertices, 0, count * 4, flags);
            mesh.SetIndices(s_ClearIndices, 0, count * 6, MeshTopology.Triangles, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(4f, 4f, 4f));
            return mesh;
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
            s_Subscribed = false;
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
