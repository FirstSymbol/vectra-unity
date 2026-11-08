using System;
using Unity.Profiling;

namespace FirstSymbol.Vectra.Editor.Tests
{
    /// <summary>
    /// Counts managed allocations through the profiler's "GC Allocated In Frame" counters. GC.GetAllocatedBytesForCurrentThread
    /// and GC.GetTotalMemory report 0 on Unity's Mono, so they cannot prove "allocation free".
    /// </summary>
    static class GcProbe
    {
        /// <summary>
        /// The counters cover every thread, so a background allocation in the editor would show up as noise. A real
        /// allocation repeats in every round, hence the smallest round is returned.
        /// </summary>
        public static (long bytes, long count) Measure(Action<int> step, int iterations, int warmUp = 4, int rounds = 5)
        {
            for (int i = 0; i < warmUp; i++) step(i);
            using var bytes = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            using var count = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocation In Frame Count");
            long bestBytes = long.MaxValue, bestCount = long.MaxValue;
            for (int round = 0; round < rounds; round++)
            {
                long b0 = bytes.CurrentValue, c0 = count.CurrentValue;
                for (int i = 0; i < iterations; i++) step(100 + round * iterations + i);
                bestBytes = Math.Min(bestBytes, bytes.CurrentValue - b0);
                bestCount = Math.Min(bestCount, count.CurrentValue - c0);
            }

            return (bestBytes, bestCount);
        }
    }
}
