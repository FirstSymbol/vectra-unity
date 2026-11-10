using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace FirstSymbol.Vectra.Rendering
{
    /// <summary>Implemented by components that store atlas coordinates in their meshes.</summary>
    public interface IVectraAtlasClient
    {
        /// <summary>The atlas layout changed; the client must rebuild its geometry.</summary>
        void OnAtlasInvalidated();
    }

    /// <summary>
    /// Shared GPU resources of all Vectra shapes: the gradient atlas and the signed-distance atlas. Both are bound as
    /// global textures so every shape uses the same material and batches with the others.
    /// </summary>
    public static class VectraAtlases
    {
        public static readonly int GradientsId = Shader.PropertyToID("_VectraGradients");
        public static readonly int SdfId = Shader.PropertyToID("_VectraSdf");

        static readonly HashSet<IVectraAtlasClient> s_Clients = new HashSet<IVectraAtlasClient>();
        static readonly List<IVectraAtlasClient> s_Scratch = new List<IVectraAtlasClient>();
        static bool s_Hooked;

        public static void Register(IVectraAtlasClient client)
        {
            EnsureHooks();
            s_Clients.Add(client);
        }

        public static void Unregister(IVectraAtlasClient client) => s_Clients.Remove(client);

        internal static void InvalidateClients()
        {
            s_Scratch.Clear();
            s_Scratch.AddRange(s_Clients);
            foreach (var c in s_Scratch) c?.OnAtlasInvalidated();
            s_Scratch.Clear();
        }

        static void EnsureHooks()
        {
            if (s_Hooked) return;
            s_Hooked = true;
            // Registered after the canvas update registry, so graphics rebuilt this frame are uploaded before drawing.
            Canvas.willRenderCanvases += Flush;
            RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
            Camera.onPreCull += OnPreCull;
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += DisposeAll;
            UnityEditor.EditorApplication.quitting += DisposeAll;
#endif
            Application.quitting += DisposeAll;
        }

        static void OnBeginContextRendering(ScriptableRenderContext ctx, List<Camera> cams) => Flush();
        static void OnPreCull(Camera cam) => Flush();

        /// <summary>Uploads pending atlas changes and (re)binds the global textures.</summary>
        public static void Flush()
        {
            VectraGradientAtlas.Flush();
            VectraSdfAtlas.Flush();
        }

        static void DisposeAll()
        {
            VectraGradientAtlas.Dispose();
            VectraSdfAtlas.Dispose();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            // Supports "Enter Play Mode" without domain reload.
            s_Clients.Clear();
        }
    }

    /// <summary>A reference to rows of the gradient atlas.</summary>
    public struct GradientRows
    {
        /// <summary>Hash of the baked content; with <see cref="row"/> it identifies the entry.</summary>
        public int key;
        public int row;
        public int count;
        public bool IsValid => count > 0;
    }

    /// <summary>
    /// 256-texel-wide RGBAHalf texture whose rows are baked gradients (and pattern colour pairs and freeform blocks).
    /// Rows with equal baked content are shared and reference counted; the sharing compares the content itself, not
    /// only a hash, so two different paints can never end up on one row. A row nobody uses any more stays baked
    /// ("cold") until the space is needed; then the least recently used cold row is evicted. Only when every row is
    /// held by a live paint does the atlas grow, up to <see cref="MaxHeight"/>; past that, new paints share a reserved
    /// white row and one warning is logged.
    /// </summary>
    public static class VectraGradientAtlas
    {
        public const int Width = 256;
        public const int FreeformRows = 32;
        const int InitialHeight = 64;
        internal const int MaxHeight = 4096;

        sealed class Entry
        {
            public int hash;
            public float[] key;
            public int row;
            public int count;
            public int refs;
            public Entry next; // hash collision chain
            public LinkedListNode<Entry> cold; // set while refs == 0
        }

        static Texture2D s_Texture;
        static Color[] s_Pixels;
        static bool[] s_Used;
        static int s_Height;
        static bool s_Dirty;
        static bool s_UseHalf;
        static bool s_Warned;
        static readonly Dictionary<int, Entry> s_Entries = new Dictionary<int, Entry>();
        static readonly LinkedList<Entry> s_Cold = new LinkedList<Entry>(); // oldest first
        static readonly List<float> s_KeyScratch = new List<float>(64);
        static readonly Color[] s_RowBuffer = new Color[Width * FreeformRows];

        /// <summary>Test hook: forces every hash to one value so all lookups collide.</summary>
        internal static bool ForceHashCollisions;

        public static Texture2D Texture
        {
            get
            {
                Ensure();
                return s_Texture;
            }
        }

        public static int Height => s_Height;

        /// <summary>Number of rows holding a baked gradient, live or cold (diagnostics).</summary>
        public static int UsedRows => s_Used != null ? CountUsed() : 0;

        /// <summary>Rows held by paints that are in use right now.</summary>
        public static int LiveRows { get; private set; }

        /// <summary>Rows kept baked for reuse but held by no paint (evicted first).</summary>
        public static int ColdRows { get; private set; }

        /// <summary>Cold rows evicted since the last reset (diagnostics, tests).</summary>
        public static int EvictionCount { get; private set; }

        /// <summary>Rows baked since the last reset (diagnostics, tests).</summary>
        public static int BakeCount { get; private set; }

        /// <summary>Requests that found no room and fell back to the shared row.</summary>
        public static int OverflowCount { get; private set; }

        static void Ensure()
        {
            if (s_Texture != null) return;
            s_UseHalf = SystemInfo.SupportsTextureFormat(TextureFormat.RGBAHalf);
            if (s_Pixels == null || s_Height == 0)
            {
                s_Height = InitialHeight;
                s_Pixels = new Color[Width * s_Height];
                s_Used = new bool[s_Height];
                s_Entries.Clear();
                s_Cold.Clear();
                LiveRows = 0;
                ColdRows = 0;
                // Row 0 is the fallback for requests that cannot be served: plain white, never evicted.
                s_Used[0] = true;
                for (int x = 0; x < Width; x++) s_Pixels[x] = Color.white;
            }

            CreateTexture();
        }

        static void CreateTexture()
        {
            if (s_Texture != null) UnityEngine.Object.DestroyImmediate(s_Texture);
            s_Texture = new Texture2D(Width, s_Height, s_UseHalf ? TextureFormat.RGBAHalf : TextureFormat.RGBA32, false, true)
            {
                name = "Vectra Gradient Atlas",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 0
            };
            s_Dirty = true;
            Shader.SetGlobalTexture(VectraAtlases.GradientsId, s_Texture);
        }

        /// <summary>V coordinate of the centre of the first row.</summary>
        public static float RowV(in GradientRows rows) => (rows.row + 0.5f) / math.max(s_Height, 1);

        /// <summary>V distance between the first and the last row of a block.</summary>
        public static float BlockSpanV(in GradientRows rows) => (rows.count - 1f) / math.max(s_Height, 1);

        /// <summary>Acquires (or shares) the rows needed by <paramref name="paint"/>.</summary>
        public static GradientRows Acquire(VectraPaint paint, in ColorAdjust adjust)
        {
            Ensure();
            int count = paint.type == PaintType.Freeform ? FreeformRows : 1;
            var scratch = s_KeyScratch;
            WriteKey(scratch, paint, adjust);
            int hash = HashKey(scratch);

            if (s_Entries.TryGetValue(hash, out var chain))
            {
                for (var e = chain; e != null; e = e.next)
                {
                    if (!SameKey(e.key, scratch)) continue;
                    if (e.refs == 0)
                    {
                        s_Cold.Remove(e.cold);
                        e.cold = null;
                        LiveRows += e.count;
                        ColdRows -= e.count;
                    }

                    e.refs++;
                    return new GradientRows { key = hash, row = e.row, count = e.count };
                }
            }

            int row = FindRows(count);
            // Cold rows are a cache. They are dropped when the atlas cannot grow any more, or when they take half of it;
            // otherwise growing is cheaper than baking them again later.
            if (row < 0 && (s_Height >= MaxHeight || ColdRows * 2 >= s_Height)) row = EvictFor(count);
            if (row < 0)
            {
                Grow(count);
                row = FindRows(count);
            }

            if (row < 0) row = EvictFor(count);

            if (row < 0)
            {
                OverflowCount++;
                if (!s_Warned)
                {
                    s_Warned = true;
                    Debug.LogWarning($"Vectra: the gradient atlas is full ({LiveRows} rows in use by live paints); extra paints draw white. Reduce the number of distinct gradients on screen.");
                }

                return new GradientRows { key = 0, row = 0, count = 1 };
            }

            for (int i = 0; i < count; i++) s_Used[row + i] = true;
            Bake(paint, adjust, row, count);
            BakeCount++;
            LiveRows += count;
            var entry = new Entry { hash = hash, key = scratch.ToArray(), row = row, count = count, refs = 1 };
            if (s_Entries.TryGetValue(hash, out var head)) entry.next = head;
            s_Entries[hash] = entry;
            return new GradientRows { key = hash, row = row, count = count };
        }

        public static void Release(ref GradientRows rows)
        {
            if (!rows.IsValid) return;
            if (rows.row != 0 && s_Entries.TryGetValue(rows.key, out var chain))
            {
                for (var e = chain; e != null; e = e.next)
                {
                    if (e.row != rows.row) continue;
                    if (e.refs > 0 && --e.refs == 0)
                    {
                        LiveRows -= e.count;
                        ColdRows += e.count;
                        e.cold = s_Cold.AddLast(e);
                    }

                    break;
                }
            }

            rows = default;
        }

        static void RemoveEntry(Entry target)
        {
            if (!s_Entries.TryGetValue(target.hash, out var head)) return;
            if (head == target)
            {
                if (target.next != null) s_Entries[target.hash] = target.next;
                else s_Entries.Remove(target.hash);
            }
            else
            {
                for (var e = head; e != null; e = e.next)
                {
                    if (e.next != target) continue;
                    e.next = target.next;
                    break;
                }
            }

            for (int i = 0; i < target.count && target.row + i < s_Used.Length; i++) s_Used[target.row + i] = false;
            ColdRows -= target.count;
            EvictionCount++;
        }

        /// <summary>Evicts the least recently used cold rows until <paramref name="count"/> contiguous rows are free.</summary>
        static int EvictFor(int count)
        {
            while (s_Cold.First != null)
            {
                var e = s_Cold.First.Value;
                s_Cold.RemoveFirst();
                e.cold = null;
                RemoveEntry(e);
                int row = FindRows(count);
                if (row >= 0) return row;
            }

            return -1;
        }

        static int FindRows(int count)
        {
            int run = 0;
            for (int i = 0; i < s_Height; i++)
            {
                run = s_Used[i] ? 0 : run + 1;
                if (run == count) return i - count + 1;
            }

            return -1;
        }

        static void Grow(int needed)
        {
            int newHeight = s_Height;
            while (newHeight < MaxHeight && newHeight - CountUsed() < needed + 1) newHeight *= 2;
            if (newHeight == s_Height) newHeight = math.min(s_Height * 2, MaxHeight);
            if (newHeight == s_Height) return;
            var pixels = new Color[Width * newHeight];
            Array.Copy(s_Pixels, pixels, s_Pixels.Length);
            var used = new bool[newHeight];
            Array.Copy(s_Used, used, s_Used.Length);
            s_Pixels = pixels;
            s_Used = used;
            s_Height = newHeight;
            CreateTexture();
            // Row V coordinates depend on the height: everybody rebuilds.
            VectraAtlases.InvalidateClients();
        }

        static int CountUsed()
        {
            int n = 0;
            for (int i = 0; i < s_Used.Length; i++)
                if (s_Used[i]) n++;
            return n;
        }

        // The key lists every input of Bake as floats, so two paints are the same row exactly when their keys are equal.
        static void WriteKey(List<float> k, VectraPaint paint, in ColorAdjust adjust)
        {
            k.Clear();
            k.Add(VectraColorSpace.Linear ? 1f : 0f);
            k.Add(adjust.hue);
            k.Add(adjust.saturation);
            k.Add(adjust.brightness);
            k.Add(adjust.contrast);
            if (paint.type == PaintType.Freeform)
            {
                k.Add(1f);
                AddColor(k, paint.ResolvedColor);
                var pts = paint.freeformPoints;
                int n = pts?.Count ?? 0;
                k.Add(n);
                for (int i = 0; i < n; i++)
                {
                    k.Add(pts[i].position.x);
                    k.Add(pts[i].position.y);
                    AddColor(k, pts[i].color);
                    k.Add(pts[i].falloff);
                }
            }
            else if (paint.IsGradient)
            {
                k.Add(2f);
                var g = paint.gradient;
                if (g == null)
                {
                    k.Add(-1f);
                    return;
                }

                int n = g.stops?.Count ?? 0;
                k.Add(n);
                k.Add((int)g.interpolation);
                for (int i = 0; i < n; i++)
                {
                    var s = g.stops[i];
                    k.Add(s.position);
                    AddColor(k, s.color);
                    k.Add(s.midpoint);
                }
            }
            else
            {
                k.Add(3f);
                AddColor(k, paint.ResolvedColor);
                AddColor(k, paint.ResolvedSecondaryColor);
            }
        }

        static void AddColor(List<float> k, Color c)
        {
            k.Add(c.r);
            k.Add(c.g);
            k.Add(c.b);
            k.Add(c.a);
        }

        static bool SameKey(float[] a, List<float> b)
        {
            if (a.Length != b.Count) return false;
            for (int i = 0; i < a.Length; i++)
                if (BitConverter.SingleToInt32Bits(a[i]) != BitConverter.SingleToInt32Bits(b[i]))
                    return false;
            return true;
        }

        static int HashKey(List<float> k)
        {
            if (ForceHashCollisions) return 1;
            unchecked
            {
                uint h = 2166136261u;
                for (int i = 0; i < k.Count; i++) h = (h ^ (uint)BitConverter.SingleToInt32Bits(k[i])) * 16777619u;
                return (int)h;
            }
        }

        static void Bake(VectraPaint paint, in ColorAdjust adjust, int row, int count)
        {
            bool linear = VectraColorSpace.Linear;
            if (paint.type == PaintType.Freeform)
            {
                var pts = paint.freeformPoints;
                for (int y = 0; y < count; y++)
                for (int x = 0; x < Width; x++)
                {
                    var uv = new float2(x / (Width - 1f), y / (count - 1f));
                    Color sum = Color.clear;
                    float wsum = 0f;
                    if (pts != null)
                    {
                        for (int i = 0; i < pts.Count; i++)
                        {
                            float d = math.distance(uv, new float2(pts[i].position.x, pts[i].position.y));
                            float w = 1f / (math.pow(d, math.max(pts[i].falloff, 0.5f)) + 1e-5f);
                            sum += pts[i].color * w;
                            wsum += w;
                        }
                    }

                    var c = wsum > 0f ? sum / wsum : paint.ResolvedColor;
                    s_RowBuffer[y * Width + x] = c;
                }
            }
            else
            {
                for (int x = 0; x < Width; x++)
                {
                    float t = x / (Width - 1f);
                    Color c;
                    if (paint.IsGradient) c = paint.gradient != null ? paint.gradient.Evaluate(t) : Color.white;
                    else c = Color.Lerp(paint.ResolvedColor, paint.ResolvedSecondaryColor, t);
                    s_RowBuffer[x] = c;
                }
            }

            for (int y = 0; y < count; y++)
            for (int x = 0; x < Width; x++)
            {
                var c = s_RowBuffer[y * Width + x];
                c = ColorUtility2.AdjustHsv(c, adjust.hue, adjust.saturation, adjust.brightness, adjust.contrast);
                if (linear)
                {
                    float a = c.a;
                    c = c.linear;
                    c.a = a;
                }

                s_Pixels[(row + y) * Width + x] = c;
            }

            s_Dirty = true;
        }

        static readonly Unity.Profiling.ProfilerMarker s_FlushMarker = new Unity.Profiling.ProfilerMarker("Vectra.GradientAtlas.Flush");

        internal static void Flush()
        {
            using (s_FlushMarker.Auto()) FlushCore();
        }

        static void FlushCore()
        {
            if (s_Texture == null)
            {
                if (s_Pixels == null) return;
                CreateTexture();
            }

            Shader.SetGlobalTexture(VectraAtlases.GradientsId, s_Texture);
            if (!s_Dirty) return;
            s_Dirty = false;
            if (s_UseHalf)
            {
                var data = new NativeArray<half>(Width * s_Height * 4, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                for (int i = 0; i < s_Pixels.Length; i++)
                {
                    var c = s_Pixels[i];
                    data[i * 4] = (half)c.r;
                    data[i * 4 + 1] = (half)c.g;
                    data[i * 4 + 2] = (half)c.b;
                    data[i * 4 + 3] = (half)c.a;
                }

                s_Texture.SetPixelData(data, 0);
                data.Dispose();
            }
            else
            {
                s_Texture.SetPixels(s_Pixels);
            }

            s_Texture.Apply(false, false);
        }

        internal static void Dispose()
        {
            if (s_Texture != null) UnityEngine.Object.DestroyImmediate(s_Texture);
            s_Texture = null;
            s_Pixels = null;
            s_Used = null;
            s_Height = 0;
            s_Entries.Clear();
            s_Cold.Clear();
            LiveRows = 0;
            ColdRows = 0;
        }

        /// <summary>Drops every row and counter. For tests; live paints must be rebuilt afterwards.</summary>
        internal static void ResetForTests()
        {
            Dispose();
            EvictionCount = 0;
            BakeCount = 0;
            OverflowCount = 0;
            s_Warned = false;
            ForceHashCollisions = false;
        }
    }

    /// <summary>Colour adjustments baked into paints (from <see cref="ColorAdjustEffect"/>).</summary>
    public struct ColorAdjust
    {
        public float hue, saturation, brightness, contrast;

        public static ColorAdjust Identity => new ColorAdjust { saturation = 1f, contrast = 1f };

        public bool IsIdentity => hue == 0f && saturation == 1f && brightness == 0f && contrast == 1f;

        public Color Apply(Color c) => IsIdentity ? c : ColorUtility2.AdjustHsv(c, hue, saturation, brightness, contrast);
    }

    /// <summary>A region of the SDF atlas.</summary>
    public struct SdfRegion
    {
        public int id;
        public RectInt rect;
        public int generation;
        public bool IsValid => id > 0 && generation == VectraSdfAtlas.Generation;
    }

    /// <summary>
    /// Single-channel half-float atlas of signed distance fields (and SVG masks). Regions are addressed in texel
    /// units, so growing the atlas never invalidates existing meshes; only a full reset does.
    /// </summary>
    public static class VectraSdfAtlas
    {
        const int InitialSize = 512;
        public static int MaxSize = 2048;
        const int Padding = 1;

        class Shelf
        {
            public int y, height, cursor;
            public readonly List<int2> free = new List<int2>();
        }

        struct Alloc
        {
            public RectInt outer;
            public int shelf;
        }

        static Texture2D s_Texture;
        static NativeArray<half> s_Data;
        static int s_Size;
        static bool s_Dirty;
        static Texture2D s_Staging;
        static readonly List<RectInt> s_DirtyRects = new List<RectInt>();
        const int MaxDirtyRects = 16;
        static int s_NextId = 1;
        static int s_Generation = 1;
        static int s_ShelfTop;
        static readonly List<Shelf> s_Shelves = new List<Shelf>();
        static readonly Dictionary<int, Alloc> s_Allocs = new Dictionary<int, Alloc>();

        public static int Generation => s_Generation;
        public static int Size => s_Size;

        /// <summary>Texels sent to the GPU by the most recent upload (diagnostics).</summary>
        public static long LastUploadTexels { get; private set; }
        public static int AllocationCount => s_Allocs.Count;

        /// <summary>Collects the allocated regions, padding included (diagnostics).</summary>
        public static void GetAllocations(List<RectInt> result)
        {
            result.Clear();
            foreach (var a in s_Allocs.Values) result.Add(a.outer);
        }
        public static Texture2D Texture
        {
            get
            {
                Ensure();
                return s_Texture;
            }
        }

        /// <summary>Raw texel data; bake jobs write into it directly.</summary>
        public static NativeArray<half> Data
        {
            get
            {
                Ensure();
                return s_Data;
            }
        }

        static void Ensure()
        {
            if (s_Data.IsCreated && s_Texture != null) return;
            if (!s_Data.IsCreated)
            {
                s_Size = InitialSize;
                s_Data = new NativeArray<half>(s_Size * s_Size, Allocator.Persistent);
                Fill(s_Data, (half)1f);
                ResetShelves();
            }

            CreateTexture();
        }

        static void Fill(NativeArray<half> a, half v)
        {
            for (int i = 0; i < a.Length; i++) a[i] = v;
        }

        static void CreateTexture()
        {
            if (s_Texture != null) UnityEngine.Object.DestroyImmediate(s_Texture);
            var format = SystemInfo.SupportsTextureFormat(TextureFormat.RHalf) ? TextureFormat.RHalf : TextureFormat.RFloat;
            s_Texture = new Texture2D(s_Size, s_Size, format, false, true)
            {
                name = "Vectra SDF Atlas",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 0
            };
            s_Dirty = true;
            s_DirtyRects.Clear();
            Shader.SetGlobalTexture(VectraAtlases.SdfId, s_Texture);
        }

        static void ResetShelves()
        {
            s_Shelves.Clear();
            s_Allocs.Clear();
            s_ShelfTop = 0;
        }

        /// <summary>Allocates a block of <paramref name="width"/> x <paramref name="height"/> texels.</summary>
        public static bool Allocate(int width, int height, out SdfRegion region)
        {
            Ensure();
            region = default;
            int w = width + Padding * 2, h = height + Padding * 2;
            if (w > MaxSize || h > MaxSize) return false;
            if (!TryPlace(w, h, out var outer, out int shelf))
            {
                while (s_Size < MaxSize)
                {
                    GrowTo(s_Size * 2);
                    if (TryPlace(w, h, out outer, out shelf)) goto placed;
                }

                // Out of space: start over; every client re-allocates on its next rebuild.
                Reset();
                if (!TryPlace(w, h, out outer, out shelf)) return false;
            }

            placed:
            int id = s_NextId++;
            s_Allocs[id] = new Alloc { outer = outer, shelf = shelf };
            region = new SdfRegion
            {
                id = id,
                generation = s_Generation,
                rect = new RectInt(outer.x + Padding, outer.y + Padding, width, height)
            };
            return true;
        }

        public static void Free(ref SdfRegion region)
        {
            if (region.id > 0 && region.generation == s_Generation && s_Allocs.TryGetValue(region.id, out var a))
            {
                s_Allocs.Remove(region.id);
                var shelf = s_Shelves[a.shelf];
                if (a.outer.x + a.outer.width == shelf.cursor) shelf.cursor = a.outer.x;
                else shelf.free.Add(new int2(a.outer.x, a.outer.width));
                MergeFree(shelf);
            }

            region = default;
        }

        static void MergeFree(Shelf shelf)
        {
            if (shelf.free.Count == 0) return;
            shelf.free.Sort((a, b) => a.x.CompareTo(b.x));
            for (int i = shelf.free.Count - 2; i >= 0; i--)
            {
                var a = shelf.free[i];
                var b = shelf.free[i + 1];
                if (a.x + a.y == b.x)
                {
                    shelf.free[i] = new int2(a.x, a.y + b.y);
                    shelf.free.RemoveAt(i + 1);
                }
            }

            // A free run touching the cursor gives its space back to the cursor.
            for (int i = shelf.free.Count - 1; i >= 0; i--)
            {
                var f = shelf.free[i];
                if (f.x + f.y != shelf.cursor) continue;
                shelf.cursor = f.x;
                shelf.free.RemoveAt(i);
            }
        }

        static bool TryPlace(int w, int h, out RectInt rect, out int shelfIndex)
        {
            rect = default;
            shelfIndex = -1;
            int best = -1, bestWaste = int.MaxValue;
            for (int i = 0; i < s_Shelves.Count; i++)
            {
                var s = s_Shelves[i];
                if (s.height < h || s.height > h * 2 + 8) continue;
                int waste = s.height - h;
                bool fits = s.cursor + w <= s_Size;
                if (!fits)
                {
                    foreach (var f in s.free)
                        if (f.y >= w) { fits = true; break; }
                }

                if (fits && waste < bestWaste)
                {
                    best = i;
                    bestWaste = waste;
                }
            }

            if (best < 0)
            {
                if (s_ShelfTop + h > s_Size) return false;
                s_Shelves.Add(new Shelf { y = s_ShelfTop, height = h, cursor = 0 });
                s_ShelfTop += h;
                best = s_Shelves.Count - 1;
            }

            var shelf = s_Shelves[best];
            for (int i = 0; i < shelf.free.Count; i++)
            {
                var f = shelf.free[i];
                if (f.y < w) continue;
                rect = new RectInt(f.x, shelf.y, w, h);
                if (f.y == w) shelf.free.RemoveAt(i);
                else shelf.free[i] = new int2(f.x + w, f.y - w);
                shelfIndex = best;
                return true;
            }

            if (shelf.cursor + w > s_Size) return false;
            rect = new RectInt(shelf.cursor, shelf.y, w, h);
            shelf.cursor += w;
            shelfIndex = best;
            return true;
        }

        static void GrowTo(int size)
        {
            var data = new NativeArray<half>(size * size, Allocator.Persistent);
            Fill(data, (half)1f);
            for (int y = 0; y < s_Size; y++)
                NativeArray<half>.Copy(s_Data, y * s_Size, data, y * size, s_Size);
            s_Data.Dispose();
            s_Data = data;
            s_Size = size;
            CreateTexture();
        }

        /// <summary>Drops every region; clients rebuild and allocate again.</summary>
        public static void Reset()
        {
            Ensure();
            ResetShelves();
            Fill(s_Data, (half)1f);
            s_Generation++;
            s_Dirty = true;
            VectraAtlases.InvalidateClients();
        }

        /// <summary>Marks the whole atlas as changed.</summary>
        public static void MarkDirty() => s_Dirty = true;

        /// <summary>Marks a block of texels as changed; only the marked blocks are uploaded on the next flush.</summary>
        public static void MarkDirty(RectInt texels)
        {
            if (s_Dirty) return;
            int x0 = Mathf.Max(0, texels.xMin - Padding), y0 = Mathf.Max(0, texels.yMin - Padding);
            int x1 = Mathf.Min(s_Size, texels.xMax + Padding), y1 = Mathf.Min(s_Size, texels.yMax + Padding);
            if (x1 <= x0 || y1 <= y0) return;
            var r = new RectInt(x0, y0, x1 - x0, y1 - y0);
            for (int i = 0; i < s_DirtyRects.Count; i++)
            {
                var o = s_DirtyRects[i];
                if (r.xMin > o.xMax || o.xMin > r.xMax || r.yMin > o.yMax || o.yMin > r.yMax) continue;
                // Touching or overlapping blocks become one.
                int ux0 = Mathf.Min(r.xMin, o.xMin), uy0 = Mathf.Min(r.yMin, o.yMin);
                int ux1 = Mathf.Max(r.xMax, o.xMax), uy1 = Mathf.Max(r.yMax, o.yMax);
                s_DirtyRects.RemoveAt(i);
                MarkDirty(new RectInt(ux0, uy0, ux1 - ux0, uy1 - uy0));
                return;
            }

            if (s_DirtyRects.Count >= MaxDirtyRects) s_Dirty = true;
            else s_DirtyRects.Add(r);
        }

        static readonly Unity.Profiling.ProfilerMarker s_FlushMarker = new Unity.Profiling.ProfilerMarker("Vectra.SdfAtlas.Flush");

        internal static void Flush()
        {
            using (s_FlushMarker.Auto()) FlushCore();
        }

        static void FlushCore()
        {
            if (!s_Data.IsCreated) return;
            if (s_Texture == null) CreateTexture();
            Shader.SetGlobalTexture(VectraAtlases.SdfId, s_Texture);
            LastUploadTexels = 0;
            if (!s_Dirty && s_DirtyRects.Count == 0) return;

            long area = 0;
            foreach (var r in s_DirtyRects) area += (long)r.width * r.height;
            if (s_Dirty || area * 2 > (long)s_Size * s_Size)
            {
                s_Dirty = false;
                s_DirtyRects.Clear();
                LastUploadTexels = (long)s_Size * s_Size;
                if (s_Texture.format == TextureFormat.RHalf)
                {
                    s_Texture.SetPixelData(s_Data, 0);
                }
                else
                {
                    var f = new NativeArray<float>(s_Data.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                    for (int i = 0; i < f.Length; i++) f[i] = s_Data[i];
                    s_Texture.SetPixelData(f, 0);
                    f.Dispose();
                }

                s_Texture.Apply(false, false);
                return;
            }

            foreach (var r in s_DirtyRects) UploadRegion(r);
            s_DirtyRects.Clear();
        }

        /// <summary>Sends one block to the GPU through a small staging texture; the rest of the atlas is not touched.</summary>
        static void UploadRegion(RectInt r)
        {
            bool half = s_Texture.format == TextureFormat.RHalf;
            // The staging texture only grows (in steps of 16 texels), so a block that changes by a texel from one rebuild to the next
            // does not make a new texture, which would allocate managed memory every time.
            if (s_Staging == null || s_Staging.width < r.width || s_Staging.height < r.height || s_Staging.format != s_Texture.format)
            {
                int w = Mathf.Max((r.width + 15) & ~15, s_Staging != null ? s_Staging.width : 0);
                int h = Mathf.Max((r.height + 15) & ~15, s_Staging != null ? s_Staging.height : 0);
                if (s_Staging != null) UnityEngine.Object.DestroyImmediate(s_Staging);
                s_Staging = new Texture2D(w, h, s_Texture.format, false, true)
                {
                    name = "Vectra SDF Staging",
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point
                };
            }

            if (half)
            {
                var rows = new NativeArray<half>(s_Staging.width * s_Staging.height, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                for (int y = 0; y < r.height; y++)
                    NativeArray<half>.Copy(s_Data, (r.y + y) * s_Size + r.x, rows, y * s_Staging.width, r.width);
                s_Staging.SetPixelData(rows, 0);
                rows.Dispose();
            }
            else
            {
                var rows = new NativeArray<float>(s_Staging.width * s_Staging.height, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                for (int y = 0; y < r.height; y++)
                {
                    int src = (r.y + y) * s_Size + r.x, dst = y * s_Staging.width;
                    for (int x = 0; x < r.width; x++) rows[dst + x] = s_Data[src + x];
                }

                s_Staging.SetPixelData(rows, 0);
                rows.Dispose();
            }

            s_Staging.Apply(false, false);
            Graphics.CopyTexture(s_Staging, 0, 0, 0, 0, r.width, r.height, s_Texture, 0, 0, r.x, r.y);
            LastUploadTexels += (long)r.width * r.height;
        }

        internal static void Dispose()
        {
            if (s_Texture != null) UnityEngine.Object.DestroyImmediate(s_Texture);
            s_Texture = null;
            if (s_Staging != null) UnityEngine.Object.DestroyImmediate(s_Staging);
            s_Staging = null;
            s_DirtyRects.Clear();
            if (s_Data.IsCreated) s_Data.Dispose();
            ResetShelves();
            s_Generation++;
        }
    }
}
