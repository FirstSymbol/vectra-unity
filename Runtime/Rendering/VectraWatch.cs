using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;
using UnityEngine.Rendering;

namespace FirstSymbol.Vectra.Rendering
{
    /// <summary>A component that looks at its own transform or at the view once a frame (what its LateUpdate used to do).</summary>
    internal interface IVectraTicked
    {
        /// <summary>
        /// Owned by <see cref="VectraWatch"/>: a slot (0 and up) while registered, -1 not registered, -2 waiting for the next tick to be
        /// registered, -3 - slot while the registration is kept for a quick re-enable.
        /// </summary>
        int WatchSlot { get; set; }

        /// <summary>The per-frame work. Returns true when it wants another tick next frame even if nothing changed (a debounce is running).</summary>
        bool WatchTick();

        /// <summary>Cheap check of managed state only (no Unity calls); true = tick now. Only called for items added with <c>polled</c>.</summary>
        bool WatchPoll();
    }

    /// <summary>
    /// One check per frame for every Vectra component that used to poll in LateUpdate. A job reads the world matrices of all registered
    /// transforms; only components whose matrix changed since the previous frame (or that asked to be ticked every frame, or that have a
    /// debounce running, or whose state was marked dirty) run their per-frame check, so a scene of unchanging shapes costs one job.
    /// Disabling a component keeps its slot for a while (adding and removing a transform costs about a microsecond each): enabling it
    /// again, as a pool or a toggled panel does, takes the slot back; slots nobody takes back are removed a few frames later.
    /// </summary>
    internal static class VectraWatch
    {
        public const byte GroupShape = 0, GroupRenderer = 1, GroupOther = 2;

        [BurstCompile]
        struct ChangedJob : IJobParallelForTransform
        {
            [NativeDisableParallelForRestriction] public NativeArray<float4x4> last;
            [NativeDisableParallelForRestriction] public NativeArray<byte> flags;
            [NativeDisableParallelForRestriction] public NativeArray<byte> always;
            [NativeDisableParallelForRestriction] public NativeArray<byte> carry;
            [NativeDisableParallelForRestriction] public NativeArray<byte> retired;

            public void Execute(int i, TransformAccess t)
            {
                if (retired[i] != 0)
                {
                    // The component is disabled (or destroyed): its transform is not read, which is also what keeps a destroyed one safe.
                    flags[i] = 0;
                    return;
                }

                bool changed = true;
                if (t.isValid)
                {
                    float4x4 m = t.localToWorldMatrix;
                    float4x4 p = last[i];
                    // Bitwise: a NaN matrix is "changed" every frame, which is the old behaviour (the check ran every frame).
                    changed = !(math.all(m.c0 == p.c0) & math.all(m.c1 == p.c1) & math.all(m.c2 == p.c2) & math.all(m.c3 == p.c3));
                    last[i] = m;
                }

                flags[i] = (byte)(changed | always[i] != 0 | carry[i] != 0 ? 1 : 0);
                carry[i] = 0;
            }
        }

        struct PendingEntry
        {
            public IVectraTicked item;
            public Transform transform;
            public byte flags;
        }

        static readonly List<IVectraTicked> s_Items = new List<IVectraTicked>();
        static readonly List<byte> s_Groups = new List<byte>();
        static readonly List<byte> s_Modes = new List<byte>();
        static readonly List<int> s_RetiredAt = new List<int>();
        static readonly List<PendingEntry> s_Pending = new List<PendingEntry>();
        static readonly List<IVectraTicked> s_Polled = new List<IVectraTicked>();
        static readonly List<IVectraTicked> s_Run = new List<IVectraTicked>();
        static TransformAccessArray s_Transforms;
        static NativeArray<float4x4> s_Last;
        static NativeArray<byte> s_Flags, s_Always, s_Carry, s_Retired;
        static int s_Capacity;
        static int s_RetiredCount;
        static bool s_Hooked;
        static VectraWatchDriver s_Driver;
        static int s_Frame;

        const byte PolledBit = 1, AlwaysBit = 2;
        // Disabled components keep their slot this many frames; at most this many slots are removed per tick (a removal costs about a microsecond).
        const int KeepFrames = 16, MaxRemovalsPerTick = 256;
        static readonly Predicate<IVectraTicked> s_IsGone = item => item.WatchSlot == -1;

        /// <summary>Called once at the start of every tick (before the job); components with shared per-frame state (the main camera) use it.</summary>
        public static event Action PreTick;

        /// <summary>Registered components that are enabled.</summary>
        public static int Count => s_Items.Count - s_RetiredCount;

        /// <summary>Slots kept for components that were disabled a moment ago (diagnostics, tests).</summary>
        public static int RetiredCount => s_RetiredCount;

        public static int PendingCount => s_Pending.Count;

        /// <summary>How many items ran their check in the latest tick (diagnostics, tests).</summary>
        public static int LastTicked { get; private set; }

        static int s_TickedNow;

        /// <summary>Registers <paramref name="item"/>; it is ticked for the first time in the next tick.</summary>
        public static void Add(IVectraTicked item, Transform transform, byte group, bool polled)
        {
            int slot = item.WatchSlot;
            if (slot >= 0 || slot == -2) return;
            EnsureHooks();
            if (slot <= -3)
            {
                // Enabled again before the slot was given up: nothing to add to the transform array.
                slot = -3 - slot;
                if (slot < s_Items.Count && s_Retired[slot] != 0 && ReferenceEquals(s_Items[slot], item))
                {
                    s_Retired[slot] = 0;
                    s_RetiredCount--;
                    s_Last[slot] = new float4x4(float.NaN);
                    s_Always[slot] = 0;
                    s_Modes[slot] = (byte)(s_Modes[slot] & ~AlwaysBit);
                    s_Carry[slot] = 1;
                    item.WatchSlot = slot;
                    return;
                }
            }

            item.WatchSlot = -2;
            s_Pending.Add(new PendingEntry { item = item, transform = transform, flags = (byte)((polled ? PolledBit : 0) | (group << 4)) });
        }

        public static void Remove(IVectraTicked item)
        {
            int slot = item.WatchSlot;
            if (slot == -1 || slot <= -3) return;
            if (slot == -2)
            {
                item.WatchSlot = -1; // still waiting: the pending entry is skipped when the slot is not -2
                return;
            }

            if (!s_Transforms.isCreated || slot >= s_Items.Count || !ReferenceEquals(s_Items[slot], item))
            {
                item.WatchSlot = -1;
                return;
            }

            s_Retired[slot] = 1;
            s_Always[slot] = 0;
            s_Carry[slot] = 0;
            s_Flags[slot] = 0;
            s_RetiredAt[slot] = s_Frame;
            s_RetiredCount++;
            item.WatchSlot = -3 - slot;
        }

        /// <summary>Ticks the item every frame (or stops doing so).</summary>
        public static void SetAlways(IVectraTicked item, bool always)
        {
            int slot = item.WatchSlot;
            if (slot < 0 || !s_Always.IsCreated || slot >= s_Items.Count) return;
            s_Always[slot] = (byte)(always ? 1 : 0);
            s_Modes[slot] = (byte)(always ? s_Modes[slot] | AlwaysBit : s_Modes[slot] & ~AlwaysBit);
        }

        /// <summary>Ticks the item in the next tick (state it depends on changed). Safe to call at any time.</summary>
        public static void Request(IVectraTicked item)
        {
            int slot = item.WatchSlot;
            if (slot < 0 || !s_Carry.IsCreated || slot >= s_Items.Count) return;
            s_Carry[slot] = 1;
        }

        /// <summary>Ticks every item of a group in the next tick.</summary>
        public static void RequestGroup(byte group)
        {
            if (!s_Carry.IsCreated) return;
            for (int i = 0; i < s_Items.Count; i++)
                if (s_Groups[i] == group && s_Retired[i] == 0) s_Carry[i] = 1;
        }

        static void EnsureHooks()
        {
            if (!s_Hooked)
            {
                s_Hooked = true;
                Application.quitting += Dispose;
#if UNITY_EDITOR
                UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Dispose;
                UnityEditor.EditorApplication.quitting += Dispose;
#endif
                // A mesh asked for before the first frame ends (Camera.Render by hand, a test) is built by whoever renders it.
                Camera.onPreCull += OnPreCull;
                RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
            }

            if (s_Driver == null)
            {
                var go = new GameObject("Vectra Watch") { hideFlags = HideFlags.HideAndDontSave };
                if (Application.isPlaying) UnityEngine.Object.DontDestroyOnLoad(go);
                s_Driver = go.AddComponent<VectraWatchDriver>();
            }
        }

        static void OnPreCull(Camera cam) => FlushRequests();
        static void OnBeginContextRendering(ScriptableRenderContext ctx, List<Camera> cams) => FlushRequests();

        /// <summary>Runs the ticks of items that were marked dirty since the last tick, before something renders them.</summary>
        static void FlushRequests()
        {
            if (s_Running || (s_Items.Count == 0 && s_Pending.Count == 0)) return;
            RunDirty();
        }

        static void FlushPending()
        {
            int n = s_Pending.Count;
            if (n == 0) return;
            Reserve(s_Items.Count + n);
            for (int i = 0; i < n; i++)
            {
                var entry = s_Pending[i];
                var item = entry.item;
                if (item.WatchSlot != -2) continue;
                var t = entry.transform;
                if (t == null)
                {
                    item.WatchSlot = -1;
                    continue;
                }

                int slot = s_Items.Count;
                s_Transforms.Add(t);
                s_Items.Add(item);
                byte f = entry.flags;
                s_Groups.Add((byte)(f >> 4));
                s_Modes.Add((byte)(f & 0xF));
                s_RetiredAt.Add(0);
                // NaN: the first job run reports "changed", so the item is ticked once like the first LateUpdate.
                s_Last[slot] = new float4x4(float.NaN);
                s_Always[slot] = 0;
                s_Carry[slot] = 1;
                s_Flags[slot] = 0;
                s_Retired[slot] = 0;
                item.WatchSlot = slot;
                if ((f & PolledBit) != 0) s_Polled.Add(item);
            }

            s_Pending.Clear();
        }

        // Gives up the slots of components that stayed disabled: oldest rules do not matter, the last item takes the freed place.
        static void RemoveRetired()
        {
            int removed = 0;
            bool polledGone = false;
            for (int slot = s_Items.Count - 1; slot >= 0 && removed < MaxRemovalsPerTick; slot--)
            {
                if (s_Retired[slot] == 0 || s_Frame - s_RetiredAt[slot] < KeepFrames) continue;
                var item = s_Items[slot];
                if ((s_Modes[slot] & PolledBit) != 0) polledGone = true;
                int last = s_Items.Count - 1;
                s_Transforms.RemoveAtSwapBack(slot);
                if (slot != last)
                {
                    var moved = s_Items[last];
                    s_Items[slot] = moved;
                    s_Groups[slot] = s_Groups[last];
                    s_Modes[slot] = s_Modes[last];
                    s_RetiredAt[slot] = s_RetiredAt[last];
                    s_Last[slot] = s_Last[last];
                    s_Always[slot] = s_Always[last];
                    s_Carry[slot] = s_Carry[last];
                    s_Flags[slot] = s_Flags[last];
                    s_Retired[slot] = s_Retired[last];
                    moved.WatchSlot = s_Retired[slot] != 0 ? -3 - slot : slot;
                }

                s_Items.RemoveAt(last);
                s_Groups.RemoveAt(last);
                s_Modes.RemoveAt(last);
                s_RetiredAt.RemoveAt(last);
                s_Retired[last] = 0;
                s_RetiredCount--;
                item.WatchSlot = -1;
                removed++;
            }

            if (polledGone) s_Polled.RemoveAll(s_IsGone);
        }

        static void Reserve(int needed)
        {
            if (!s_Transforms.isCreated) s_Transforms = new TransformAccessArray(math.max(needed, 256));
            if (needed <= s_Capacity) return;
            int cap = math.max(256, s_Capacity);
            while (cap < needed) cap *= 2;
            s_Last = Grow(s_Last, cap);
            s_Flags = Grow(s_Flags, cap);
            s_Always = Grow(s_Always, cap);
            s_Carry = Grow(s_Carry, cap);
            s_Retired = Grow(s_Retired, cap);
            s_Capacity = cap;
        }

        static NativeArray<T> Grow<T>(NativeArray<T> old, int cap) where T : struct
        {
            var a = new NativeArray<T>(cap, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            if (old.IsCreated)
            {
                NativeArray<T>.Copy(old, a, old.Length);
                old.Dispose();
            }

            return a;
        }

        static readonly Unity.Profiling.ProfilerMarker s_TickMarker = new Unity.Profiling.ProfilerMarker("Vectra.Watch.Tick");
        static readonly Unity.Profiling.ProfilerMarker s_JobMarker = new Unity.Profiling.ProfilerMarker("Vectra.Watch.Job");

        /// <summary>The per-frame entry, called by the driver in LateUpdate.</summary>
        internal static void Tick()
        {
            using (s_TickMarker.Auto()) TickCore();
        }

        static void TickCore()
        {
            s_Frame++;
            s_TickedNow = 0;
            PreTick?.Invoke();
            if (s_RetiredCount > 0 && (s_Frame & 3) == 0) RemoveRetired();
            FlushPending();
            int count = s_Items.Count;
            if (count == 0)
            {
                LastTicked = 0;
                return;
            }

            for (int i = 0; i < s_Polled.Count; i++)
            {
                var p = s_Polled[i];
                int slot = p.WatchSlot;
                if (slot >= 0 && p.WatchPoll()) s_Carry[slot] = 1;
            }

            // A slice of the items is ticked whatever the job says: it re-reads state that has no notification (a canvas that changed
            // its render mode, a component enabled later) within a second at 60 fps.
            const int slices = 64;
            int part = s_Frame % slices;
            for (int i = part; i < count; i += slices)
                if (s_Retired[i] == 0) s_Carry[i] = 1;

            using (s_JobMarker.Auto())
            {
                var job = new ChangedJob { last = s_Last, flags = s_Flags, always = s_Always, carry = s_Carry, retired = s_Retired };
                job.ScheduleReadOnly(s_Transforms, 128).Complete();
            }

            Dispatch();
            // Components that dirtied each other while ticking (a boolean group and its owner) settle in the same frame.
            for (int round = 0; round < 3 && DispatchCarry(); round++) { }
            LastTicked = s_TickedNow;
        }

        /// <summary>Ticks items whose state was marked dirty, without running the job (called before rendering).</summary>
        static void RunDirty()
        {
            FlushPending();
            if (s_Items.Count == 0) return;
            for (int round = 0; round < 3 && DispatchCarry(); round++) { }
        }

        static unsafe void Dispatch()
        {
            s_Run.Clear();
            int count = s_Items.Count;
            var flags = (byte*)s_Flags.GetUnsafePtr();
            int i = 0;
            // Eight flags at a time: a scene of unchanging shapes is a loop over zero words.
            for (; i + 8 <= count; i += 8)
            {
                if (*(ulong*)(flags + i) == 0UL) continue;
                for (int k = i; k < i + 8; k++)
                    if (flags[k] != 0) s_Run.Add(s_Items[k]);
            }

            for (; i < count; i++)
                if (flags[i] != 0) s_Run.Add(s_Items[i]);
            UnsafeUtility.MemClear(flags, count);
            RunScratch();
        }

        static unsafe bool DispatchCarry()
        {
            s_Run.Clear();
            int count = s_Items.Count;
            var carry = (byte*)s_Carry.GetUnsafePtr();
            int i = 0;
            for (; i + 8 <= count; i += 8)
            {
                if (*(ulong*)(carry + i) == 0UL) continue;
                for (int k = i; k < i + 8; k++)
                    if (carry[k] != 0)
                    {
                        s_Run.Add(s_Items[k]);
                        carry[k] = 0;
                    }
            }

            for (; i < count; i++)
                if (carry[i] != 0)
                {
                    s_Run.Add(s_Items[i]);
                    carry[i] = 0;
                }

            if (s_Run.Count == 0) return false;
            RunScratch();
            return true;
        }

        static bool s_Running;

        static void RunScratch()
        {
            // Ticking may register or remove items; s_Run is a copy of who to tick, items that left are skipped.
            s_Running = true;
            s_TickedNow += s_Run.Count;
            try
            {
                for (int k = 0; k < s_Run.Count; k++)
                {
                    var item = s_Run[k];
                    if (item.WatchSlot < 0) continue;
                    try
                    {
                        if (item.WatchTick() && item.WatchSlot >= 0 && s_Carry.IsCreated) s_Carry[item.WatchSlot] = 1;
                    }
                    catch (Exception e)
                    {
                        // One component failing must not stop the others (each LateUpdate was isolated by Unity before).
                        Debug.LogException(e, item as UnityEngine.Object);
                    }
                }
            }
            finally
            {
                s_Running = false;
                s_Run.Clear();
            }
        }

        internal static void Dispose()
        {
            for (int i = 0; i < s_Items.Count; i++) s_Items[i].WatchSlot = -1;
            for (int i = 0; i < s_Pending.Count; i++) s_Pending[i].item.WatchSlot = -1;
            s_Items.Clear();
            s_Groups.Clear();
            s_Modes.Clear();
            s_RetiredAt.Clear();
            s_Pending.Clear();
            s_Polled.Clear();
            s_Run.Clear();
            s_RetiredCount = 0;
            if (s_Transforms.isCreated) s_Transforms.Dispose();
            if (s_Last.IsCreated) s_Last.Dispose();
            if (s_Flags.IsCreated) s_Flags.Dispose();
            if (s_Always.IsCreated) s_Always.Dispose();
            if (s_Carry.IsCreated) s_Carry.Dispose();
            if (s_Retired.IsCreated) s_Retired.Dispose();
            s_Capacity = 0;
            if (s_Driver != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(s_Driver.gameObject);
                else UnityEngine.Object.DestroyImmediate(s_Driver.gameObject);
            }

            s_Driver = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            // Supports "Enter Play Mode" without domain reload.
            Dispose();
            PreTick = null;
        }
    }

    /// <summary>Runs <see cref="VectraWatch.Tick"/> in the LateUpdate phase.</summary>
    [ExecuteAlways]
    [AddComponentMenu("")]
    internal sealed class VectraWatchDriver : MonoBehaviour
    {
        void LateUpdate() => VectraWatch.Tick();
    }
}
