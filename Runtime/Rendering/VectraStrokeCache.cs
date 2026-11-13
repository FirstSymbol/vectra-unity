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
    /// again. The key is a 64-bit fingerprint of the exact input bytes; a result is kept only the second time its input
    /// is seen, so animated strokes (a new input every frame) neither allocate nor fill the cache.
    /// </summary>
    static class VectraStrokeCache
    {
        const int MaxEntries = 512;
        const int MaxSeen = 2048;
        const int MaxTrapezoids = 2048;
        const int MaxTotalTrapezoids = 120000;

        static readonly Dictionary<ulong, Trapezoid[]> s_Results = new Dictionary<ulong, Trapezoid[]>(MaxEntries);
        static readonly HashSet<ulong> s_Seen = new HashSet<ulong>();
        static int s_Total;

        public static ulong Fingerprint(in Polylines poly, in StrokeParams p, NativeArray<float> dash, NativeArray<float> profile,
            int clipMode, FillRule rule, bool clipSelf)
        {
            ulong h = 0xCBF29CE484222325UL;
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

            return h;
        }

        static void Mix(ref ulong h, uint v)
        {
            h = (h ^ v) * 0x100000001B3UL;
            h ^= h >> 29;
        }

        /// <summary>Copies a cached result into <paramref name="traps"/>; false when this input is not cached.</summary>
        public static bool TryGet(ulong key, ref NativeList<Trapezoid> traps)
        {
            if (!s_Results.TryGetValue(key, out var cached)) return false;
            traps.ResizeUninitialized(cached.Length);
            NativeArray<Trapezoid>.Copy(cached, traps.AsArray(), cached.Length);
            return true;
        }

        /// <summary>Remembers a result the second time its input is seen.</summary>
        public static void Offer(ulong key, NativeList<Trapezoid> traps)
        {
            int n = traps.Length;
            if (n == 0 || n > MaxTrapezoids) return;
            if (s_Seen.Add(key))
            {
                if (s_Seen.Count > MaxSeen) s_Seen.Clear();
                return;
            }

            if (s_Results.Count >= MaxEntries || s_Total + n > MaxTotalTrapezoids)
            {
                s_Results.Clear();
                s_Total = 0;
            }

            if (s_Results.ContainsKey(key)) return;
            s_Results[key] = traps.AsArray().ToArray();
            s_Total += n;
        }
    }
}
