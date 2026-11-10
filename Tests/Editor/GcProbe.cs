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
        /// Counts on the calling thread only, so an allocation by the editor on another thread cannot show up as noise. A real allocation
        /// repeats in every round, hence the smallest round is returned. <paramref name="allThreads"/> is the same figure over every
        /// thread, for the failure message.
        /// </summary>
        public static (long bytes, long count) Measure(Action<int> step, int iterations, int warmUp = 4, int rounds = 5) =>
            Measure(step, iterations, out _, warmUp, rounds);

        public static (long bytes, long count) Measure(Action<int> step, int iterations, out string allThreads, int warmUp = 4, int rounds = 5)
        {
            for (int i = 0; i < warmUp; i++) step(i);
            const ProfilerRecorderOptions Current = ProfilerRecorderOptions.Default | ProfilerRecorderOptions.CollectOnlyOnCurrentThread;
            using var bytes = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1, Current);
            using var count = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocation In Frame Count", 1, Current);
            using var bytesAll = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            long bestBytes = long.MaxValue, bestCount = long.MaxValue, bestAll = long.MaxValue;
            for (int round = 0; round < rounds; round++)
            {
                long b0 = bytes.CurrentValue, c0 = count.CurrentValue, a0 = bytesAll.CurrentValue;
                for (int i = 0; i < iterations; i++) step(100 + round * iterations + i);
                bestBytes = Math.Min(bestBytes, bytes.CurrentValue - b0);
                bestCount = Math.Min(bestCount, count.CurrentValue - c0);
                bestAll = Math.Min(bestAll, bytesAll.CurrentValue - a0);
            }

            allThreads = bestAll + " bytes over all threads";
            return (bestBytes, bestCount);
        }
    }
}
