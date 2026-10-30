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
        public int key;
        public int row;
        public int count;
        public bool IsValid => count > 0;
    }

    /// <summary>
    /// 256-texel-wide RGBAHalf texture whose rows are baked gradients (and pattern colour pairs and freeform blocks).
    /// Identical rows are shared and reference counted.
    /// </summary>
    public static class VectraGradientAtlas
    {
        public const int Width = 256;
        public const int FreeformRows = 32;
        const int InitialHeight = 64;
        const int MaxHeight = 4096;

        struct Entry
        {
            public int row;
            public int count;
            public int refs;
        }

        static Texture2D s_Texture;
        static Color[] s_Pixels;
        static bool[] s_Used;
        static int s_Height;
        static bool s_Dirty;
        static bool s_UseHalf;
        static readonly Dictionary<int, Entry> s_Entries = new Dictionary<int, Entry>();
        static readonly Color[] s_RowBuffer = new Color[Width * FreeformRows];

        public static Texture2D Texture
        {
            get
            {
                Ensure();
                return s_Texture;
            }
        }

        public static int Height => s_Height;

        /// <summary>Number of rows currently in use (diagnostics).</summary>
        public static int UsedRows => s_Used != null ? CountUsed() : 0;

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
            int key = paint.ComputeRowHash(adjust.hue, adjust.saturation, adjust.brightness, adjust.contrast);
            if (s_Entries.TryGetValue(key, out var e))
            {
                e.refs++;
                s_Entries[key] = e;
                return new GradientRows { key = key, row = e.row, count = e.count };
            }

            int row = FindRows(count);
            if (row < 0)
            {
                Grow(count);
                row = FindRows(count);
                if (row < 0) return default;
            }

            for (int i = 0; i < count; i++) s_Used[row + i] = true;
            Bake(paint, adjust, row, count);
            s_Entries[key] = new Entry { row = row, count = count, refs = 1 };
            return new GradientRows { key = key, row = row, count = count };
        }

        public static void Release(ref GradientRows rows)
        {
            if (!rows.IsValid) return;
            if (s_Entries.TryGetValue(rows.key, out var e))
            {
                e.refs--;
                if (e.refs <= 0)
                {
                    for (int i = 0; i < e.count && e.row + i < s_Used.Length; i++) s_Used[e.row + i] = false;
                    s_Entries.Remove(rows.key);
                }
                else s_Entries[rows.key] = e;
            }

            rows = default;
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

        static void Bake(VectraPaint paint, in ColorAdjust adjust, int row, int count)
        {
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
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

        internal static void Flush()
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
        static int s_NextId = 1;
        static int s_Generation = 1;
        static int s_ShelfTop;
        static readonly List<Shelf> s_Shelves = new List<Shelf>();
        static readonly Dictionary<int, Alloc> s_Allocs = new Dictionary<int, Alloc>();

        public static int Generation => s_Generation;
        public static int Size => s_Size;
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

        public static void MarkDirty() => s_Dirty = true;

        internal static void Flush()
        {
            if (!s_Data.IsCreated) return;
            if (s_Texture == null) CreateTexture();
            Shader.SetGlobalTexture(VectraAtlases.SdfId, s_Texture);
            if (!s_Dirty) return;
            s_Dirty = false;
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
        }

        internal static void Dispose()
        {
            if (s_Texture != null) UnityEngine.Object.DestroyImmediate(s_Texture);
            s_Texture = null;
            if (s_Data.IsCreated) s_Data.Dispose();
            ResetShelves();
            s_Generation++;
        }
    }
}
