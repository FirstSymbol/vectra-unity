using System.Collections.Generic;
using FirstSymbol.Vectra.Geometry;
using Unity.Collections;
using Unity.Mathematics;

namespace FirstSymbol.Vectra.Rendering
{
    /// <summary>
    /// Stroke geometry shared between shapes and between rebuilds. The tessellated stroke is a pure function of the
    /// flattened outline, the stroke parameters, the dash and width tables and the clip mode, so equal inputs (many
    /// icons of one size, a shape that alternates between two sizes) reuse one result instead of running the stroker
    /// again. The key is a 64-bit fingerprint of the input bytes and a second, independent 64-bit fingerprint is compared on every hit (128 bits in all); a result is kept only the second time its input
    /// is seen, so animated strokes (a new input every frame) neither allocate nor fill the cache.
    /// </summary>
    static class VectraStrokeCache
    {
        const int MaxEntries = 512;
        const int MaxSeen = 2048;
        const int MaxTrapezoids = 2048;
        const int MaxTotalTrapezoids = 120000;

        struct Entry
        {
            public Trapezoid[] traps;
            public ulong check;
        }

        static readonly Dictionary<ulong, Entry> s_Results = new Dictionary<ulong, Entry>(MaxEntries);
        static readonly HashSet<ulong> s_Seen = new HashSet<ulong>();
        static int s_Total;

        public static ulong Fingerprint(in Polylines poly, in StrokeParams p, NativeArray<float> dash, NativeArray<float> profile,
            int clipMode, FillRule rule, bool clipSelf, out ulong check)
        {
            ulong h = 0xCBF29CE484222325UL, h2 = 0x9E3779B97F4A7C15UL;
            ulong total = 0;
            Mix(ref h, (uint)clipMode);
            Mix(ref h, (uint)rule);
            Mix(ref h, clipSelf ? 1u : 0u);
            Mix(ref h, math.asuint(p.width));
            Mix(ref h, (uint)p.cap);
            Mix(ref h, (uint)p.join);
            Mix(ref h, math.asuint(p.miterLimit));
            Mix(ref h, math.asuint(p.tolerance));
            Mix(ref h, math.asuint(p.trimStart));
            Mix(ref h, math.asuint(p.trimEnd));
            Mix(ref h, math.asuint(p.trimOffset));
            Mix(ref h, math.asuint(p.dashOffset));
            Mix(ref h, p.dashRelative ? 1u : 0u);
            Mix(ref h, (uint)dash.Length);
            for (int i = 0; i < dash.Length; i++) Mix(ref h, math.asuint(dash[i]));
            Mix(ref h, (uint)profile.Length);
            for (int i = 0; i < profile.Length; i++) Mix(ref h, math.asuint(profile[i]));
            var contours = poly.contours;
            Mix(ref h, (uint)contours.Length);
            for (int i = 0; i < contours.Length; i++)
            {
                var c = contours[i];
                Mix(ref h, (uint)c.start);
                Mix(ref h, (uint)c.count);
                Mix(ref h, c.closed ? 1u : 0u);
                Mix(ref h, (uint)c.operand);
            }

            var points = poly.points;
            Mix(ref h, (uint)points.Length);
            for (int i = 0; i < points.Length; i++)
            {
                var pt = points[i];
                Mix(ref h, math.asuint(pt.x));
                Mix(ref h, math.asuint(pt.y));
            }

            check = h2 ^ total;
            return h;

            void Mix(ref ulong a, uint v)
            {
                a = (a ^ v) * 0x100000001B3UL;
                a ^= a >> 29;
                h2 = (h2 + v + 0x632BE59BD9B4E019UL) * 0xFF51AFD7ED558CCDUL;
                h2 ^= h2 >> 32;
                total += v;
            }
        }

        /// <summary>Copies a cached result into <paramref name="traps"/>; false when this input is not cached.</summary>
        public static bool TryGet(ulong key, ulong check, ref NativeList<Trapezoid> traps)
        {
            if (!s_Results.TryGetValue(key, out var entry) || entry.check != check) return false;
            var cached = entry.traps;
            traps.ResizeUninitialized(cached.Length);
            NativeArray<Trapezoid>.Copy(cached, traps.AsArray(), cached.Length);
            return true;
        }

        /// <summary>Remembers a result the second time its input is seen.</summary>
        public static void Offer(ulong key, ulong check, NativeList<Trapezoid> traps)
        {
            int n = traps.Length;
            if (n == 0 || n > MaxTrapezoids) return;
            if (s_Seen.Add(key))
            {
                if (s_Seen.Count > MaxSeen) s_Seen.Clear();
                return;
            }

            // Full: drop single entries (the first the dictionary offers) rather than everything, so a changing set of
            // strokes does not clear and refill the whole cache.
            while (s_Results.Count >= MaxEntries || s_Total + n > MaxTotalTrapezoids)
            {
                if (s_Results.Count == 0) return;
                Dictionary<ulong, Entry>.Enumerator e = s_Results.GetEnumerator();
                e.MoveNext();
                var victim = e.Current;
                s_Total -= victim.Value.traps.Length;
                s_Results.Remove(victim.Key);
            }

            if (s_Results.ContainsKey(key)) return;
            s_Results[key] = new Entry { traps = traps.AsArray().ToArray(), check = check };
            s_Total += n;
        }

        internal static void Clear()
        {
            s_Results.Clear();
            s_Seen.Clear();
            s_Total = 0;
        }
    }
}
