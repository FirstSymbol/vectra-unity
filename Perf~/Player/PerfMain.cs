// Vectra performance harness: one player process runs one group of rows and writes a JSON result.
// Arguments: -perf <group> -side vectra|ref|vg -variants a,b -out <file.json> [-order rev] [-profile <file.raw>]
// Groups: cold canvas v1n v4n fx svg bool world uitk sprite parse
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace FirstSymbol.VectraPerf
{
    [Serializable] public class PerfMetric { public string k; public double v; }

    [Serializable]
    public class PerfRow
    {
        public string id;
        public string note = "";
        public List<PerfMetric> m = new List<PerfMetric>();
        public void Put(string k, double v) => m.Add(new PerfMetric { k = k, v = v });
    }

    [Serializable]
    public class PerfResult
    {
        public string group, side, variant, unity, backend, gpu, device, graphicsApi, colorSpace, error;
        public bool dev;
        public double startMs;
        public List<PerfRow> rows = new List<PerfRow>();
        public List<string> log = new List<string>();
    }

    /// <summary>The numbers of a one-off action.</summary>
    public struct Once
    {
        public double ms;
        public long alloc;
    }

    public sealed partial class PerfMain : MonoBehaviour
    {
        public static PerfMain I;
        public PerfResult R = new PerfResult();
        public string Side = "vectra";
        public string[] Variants = new string[0];
        public bool Reverse;
        public Transform Root;

        static readonly WaitForEndOfFrame s_Eof = new WaitForEndOfFrame();
        static readonly FrameTiming[] s_Timing = new FrameTiming[1];
        // GC.GetAllocatedBytesForCurrentThread is not implemented by every Unity runtime (Mono returns 0): then a development player's
        // "GC Allocated In Frame" counter gives the allocation of whole frames, and one-off actions have no allocation figure (-1).
        static bool s_ApiWorks;
        static ProfilerRecorder s_GcFrame;

        public static long AllocNow() => s_ApiWorks ? GC.GetAllocatedBytesForCurrentThread() : -1;

        static void ProbeAlloc()
        {
            long a = GC.GetAllocatedBytesForCurrentThread();
            var junk = new byte[1 << 20];
            GC.KeepAlive(junk);
            s_ApiWorks = GC.GetAllocatedBytesForCurrentThread() - a >= (1 << 20);
            s_GcFrame = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
        }

        public static long FrameAlloc() => s_GcFrame.Valid ? s_GcFrame.LastValue : -1;

        static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, name);
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
        }

        static bool HasArg(string name) => Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Arg("-perf") == null) return;
            R0 = Time.realtimeSinceStartupAsDouble * 1000.0;
            new GameObject("VectraPerf").AddComponent<PerfMain>();
        }

        static double R0;
        int m_Errors;

        void OnEnable() => Application.logMessageReceived += OnLog;
        void OnDisable() => Application.logMessageReceived -= OnLog;

        void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error)
            {
                m_Errors++;
                if (R.log.Count < 30) R.log.Add(type + ": " + message);
            }

            if (type != LogType.Exception) return;
            R.error = message;
            Flush();
            Application.Quit(3);
        }

        void Flush()
        {
            string path = Arg("-out");
            if (path == null) return;
            File.WriteAllText(path + ".part", JsonUtility.ToJson(R, true));
            if (File.Exists(path)) File.Delete(path);
            File.Move(path + ".part", path);
        }

        IEnumerator Start()
        {
            I = this;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            Application.runInBackground = true;
            R.group = Arg("-perf");
            R.side = Side = Arg("-side") ?? "vectra";
            R.variant = Arg("-variants") ?? "";
            Variants = string.IsNullOrEmpty(R.variant) ? new string[0] : R.variant.Split(',');
            Reverse = Arg("-order") == "rev";
#if ENABLE_PROFILER
            string profile = Arg("-profile");
            if (profile != null)
            {
                Profiler.logFile = profile;
                Profiler.enableBinaryLog = true;
                Profiler.maxUsedMemory = 768 * 1024 * 1024;
                Profiler.enabled = true;
            }
#endif
            R.unity = Application.unityVersion;
            R.backend =
#if ENABLE_IL2CPP
                "il2cpp";
#else
                "mono";
#endif
            R.dev = Debug.isDebugBuild;
            R.gpu = SystemInfo.graphicsDeviceName;
            R.device = SystemInfo.processorType;
            R.graphicsApi = SystemInfo.graphicsDeviceType.ToString();
            R.colorSpace = QualitySettings.activeColorSpace.ToString();
            R.startMs = Time.realtimeSinceStartupAsDouble * 1000.0 - R0;
            // A development player draws its console over the scene and re-lays it out every frame: with error lines in it that is allocation and time
            // that belongs to the player, not to what is measured.
            Debug.developerConsoleVisible = false;
            ProbeAlloc();
            R.log.Add("alloc_api=" + s_ApiWorks + " gc_frame_counter=" + s_GcFrame.Valid + " debug=" + Debug.isDebugBuild);
            Root = new GameObject("PerfRoot").transform;
            // Rows start from a settled player: no startup work left in the next frames.
            for (int i = 0; i < 30; i++) yield return null;
            string group = R.group;
            // The control of every process: the empty player's frame (the floor under every row) and its allocation.
            if (group != "empty") yield return Frames(Row("CTRL", "empty scene, no content: the frame floor of this process"), "ctrl", 90, null);
            IEnumerator body = null;
            switch (group)
            {
                case "cold": body = Cold(); break;
                case "canvas": body = Canvas1000(); break;
                case "v1n": body = CanvasNoGradient(); break;
                case "v4n": body = CanvasNoDash(); break;
                case "fx": body = Fx(); break;
                case "svg": body = SvgIcons(); break;
                case "bool": body = Booleans(); break;
                case "world": body = World(); break;
                case "uitk": body = Uitk(); break;
                case "sprite": body = Sprites(); break;
                case "parse": body = Parse(); break;
                case "empty": body = Empty(); break;
                default: R.error = "unknown group " + group; break;
            }

            if (body != null)
            {
                bool more = true;
                while (more)
                {
                    try { more = body.MoveNext(); }
                    catch (Exception e)
                    {
                        R.error = e.ToString();
                        more = false;
                        break;
                    }

                    if (more) yield return body.Current;
                }
            }

#if ENABLE_PROFILER
            if (Arg("-profile") != null)
            {
                Profiler.enabled = false;
                Profiler.enableBinaryLog = false;
            }
#endif
            R.log.Add("errors logged: " + m_Errors);
            Flush();
            Application.Quit(R.error == null ? 0 : 2);
        }

        // Nested enumerators run through Unity's own coroutine scheduling so that yield instructions behave.
        IEnumerator Empty()
        {
            var row = Row("EMPTY");
            yield return Frames(row, "e", 120, null);
            Mem(row, "m0");
        }

        public PerfRow Row(string id, string note = "")
        {
            var r = new PerfRow { id = id, note = note };
            R.rows.Add(r);
            return r;
        }

        // ---------------------------------------------------------------- timing

        public static double Median(List<double> v)
        {
            if (v.Count == 0) return -1;
            var s = new List<double>(v);
            s.Sort();
            return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) * 0.5;
        }

        public static double Pct(List<double> v, double p)
        {
            if (v.Count == 0) return -1;
            var s = new List<double>(v);
            s.Sort();
            return s[Math.Min(s.Count - 1, (int)(p * (s.Count - 1) + 0.5))];
        }

        static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

        /// <summary>Time and managed allocation of an action, measured as a whole.</summary>
        public static Once Measure(Action a)
        {
            long a0 = AllocNow();
            long t0 = Stopwatch.GetTimestamp();
            a();
            double ms = Ms(Stopwatch.GetTimestamp() - t0);
            long a1 = AllocNow();
            return new Once { ms = ms, alloc = a0 < 0 || a1 < 0 ? -1 : a1 - a0 };
        }

        public static void PutOnce(PerfRow row, string key, Once o)
        {
            row.Put(key + "_ms", o.ms);
            if (o.alloc >= 0) row.Put(key + "_alloc_b", o.alloc);
        }

        // Names as a Unity 6 development player lists them (Render category).
        static readonly string[] CounterNames =
        {
            "SetPass Calls Count", "Standard Draw Calls Count", "SRP Batcher Draw Calls Count", "Dynamic Batches Count", "Triangles Count", "Vertices Count",
            "Render Textures Changes Count", "Vertex Buffer Upload In Frame Bytes", "Index Buffer Upload In Frame Bytes",
        };

        /// <summary>
        /// Runs <paramref name="frames"/> frames, calling <paramref name="each"/> at the start of each. Records per frame:
        /// wall time from one frame's start to the next (what the user sees, GPU back-pressure included), the span from the action to the
        /// end of the same frame (the CPU work of the frame on the main thread), managed allocation, and what the FrameTimingManager reports for GPU,
        /// main and render threads. Frame 0 is reported apart (first_*): it is where one-off work lands. Development players add draw counters.
        /// </summary>
        public IEnumerator Frames(PerfRow row, string prefix, int frames, Action<int> each, int warm = 3)
        {
            var marker = new ProfilerMarker("PerfRow." + row.id + "." + prefix);
            var wall = new List<double>(frames);
            var span = new List<double>(frames);
            var alloc = new List<double>(frames);
            var gpu = new List<double>(frames);
            var main = new List<double>(frames);
            var render = new List<double>(frames);
            ProfilerRecorder[] counters = null;
            if (Debug.isDebugBuild)
            {
                counters = new ProfilerRecorder[CounterNames.Length];
                for (int i = 0; i < counters.Length; i++) counters[i] = ProfilerRecorder.StartNew(ProfilerCategory.Render, CounterNames[i], Math.Max(frames, 4));
            }

            // Warm-up frames run the action too, but are not recorded (the first frame of the row is recorded below).
            for (int i = 0; i < warm; i++)
            {
                each?.Invoke(-1 - i);
                yield return null;
            }

            long lastStart = 0;
            int collections0 = GC.CollectionCount(0);
            double firstSpan = 0, firstWall = 0;
            long firstAlloc = -1;
            for (int i = 0; i < frames; i++)
            {
                long t0 = Stopwatch.GetTimestamp();
                if (i == 1) firstWall = Ms(t0 - lastStart);
                else if (i > 1) wall.Add(Ms(t0 - lastStart));
                lastStart = t0;
                long a0 = AllocNow();
                using (marker.Auto()) each?.Invoke(i);
                yield return s_Eof;
                double sp = Ms(Stopwatch.GetTimestamp() - t0);
                long al = a0 < 0 ? -1 : AllocNow() - a0;
                if (i == 0) firstSpan = sp;
                else span.Add(sp);

                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, s_Timing) > 0)
                {
                    if (s_Timing[0].gpuFrameTime > 0) gpu.Add(s_Timing[0].gpuFrameTime);
                    if (s_Timing[0].cpuMainThreadFrameTime > 0) main.Add(s_Timing[0].cpuMainThreadFrameTime);
                    if (s_Timing[0].cpuRenderThreadFrameTime > 0) render.Add(s_Timing[0].cpuRenderThreadFrameTime);
                }

                yield return null;
                // The counter of the frame that just ended is read at the start of the next one.
                if (al < 0) al = FrameAlloc();
                if (al >= 0)
                {
                    if (i == 0) firstAlloc = al;
                    else alloc.Add(al);
                }
            }

            row.Put(prefix + "_gc_collections", GC.CollectionCount(0) - collections0);
            row.Put(prefix + "_first_span_ms", firstSpan);
            row.Put(prefix + "_first_wall_ms", firstWall);
            if (firstAlloc >= 0) row.Put(prefix + "_first_alloc_b", firstAlloc);
            if (wall.Count > 0)
            {
                row.Put(prefix + "_wall_ms", Median(wall));
                row.Put(prefix + "_wall_p95_ms", Pct(wall, 0.95));
                row.Put(prefix + "_span_ms", Median(span));
                row.Put(prefix + "_span_p95_ms", Pct(span, 0.95));
                if (alloc.Count > 0)
                {
                    row.Put(prefix + "_alloc_b", Median(alloc));
                    row.Put(prefix + "_alloc_max_b", Pct(alloc, 1.0));
                }
            }

            if (gpu.Count > 0) row.Put(prefix + "_gpu_ms", Median(gpu));
            if (main.Count > 0) row.Put(prefix + "_cpumain_ms", Median(main));
            if (render.Count > 0) row.Put(prefix + "_cpurender_ms", Median(render));
            if (counters != null)
            {
                for (int i = 0; i < counters.Length; i++)
                {
                    if (counters[i].Valid && counters[i].Count > 0)
                    {
                        var vals = new List<double>();
                        for (int j = 0; j < counters[i].Count; j++) vals.Add(counters[i].GetSample(j).Value);
                        row.Put(prefix + "_" + CounterNames[i].Replace(" Count", "").Replace(" In Frame", "").Replace(' ', '_').ToLowerInvariant(), Median(vals));
                    }

                    counters[i].Dispose();
                }
            }
        }

        public IEnumerator Settle(int frames = 3)
        {
            for (int i = 0; i < frames; i++) yield return null;
        }

        /// <summary>Destroys what a row made and lets the engine drop it, so that the next row starts clean.</summary>
        public IEnumerator Clear(Transform t)
        {
            if (t != null) Destroy(t.gameObject);
            yield return null;
            yield return null;
        }

        public IEnumerator Collect()
        {
            yield return null;
            var op = Resources.UnloadUnusedAssets();
            while (!op.isDone) yield return null;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            yield return null;
        }

        // ---------------------------------------------------------------- memory

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [StructLayout(LayoutKind.Sequential)]
        struct ProcessMemory
        {
            public uint cb, pageFaults;
            public UIntPtr peakWorking, working, a, b, c, d, pagefile, peakPagefile, privateUsage;
        }

        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll", EntryPoint = "K32GetProcessMemoryInfo")] static extern bool GetProcessMemoryInfo(IntPtr h, out ProcessMemory m, uint cb);
#endif

        public static void ProcessMemoryMb(out double priv, out double working, out double peak)
        {
            priv = working = peak = -1;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            var m = new ProcessMemory { cb = (uint)Marshal.SizeOf(typeof(ProcessMemory)) };
            if (GetProcessMemoryInfo(GetCurrentProcess(), out m, m.cb))
            {
                priv = (double)(ulong)m.privateUsage / 1048576.0;
                working = (double)(ulong)m.working / 1048576.0;
                peak = (double)(ulong)m.peakWorking / 1048576.0;
            }
#endif
        }

        static double Mb(long bytes) => bytes / 1048576.0;

        static double Recorder(ProfilerCategory cat, string name)
        {
            var r = ProfilerRecorder.StartNew(cat, name);
            double v = r.Valid ? r.LastValue : -1;
            r.Dispose();
            return v;
        }

        /// <summary>
        /// Memory now: the process (private bytes, working set), the managed heap, and what the engine says its objects take
        /// (meshes, textures, render textures, summed over every live object). The largest textures go into the row's note.
        /// </summary>
        public void Mem(PerfRow row, string tag, bool top = false)
        {
            ProcessMemoryMb(out double priv, out double ws, out double peak);
            row.Put(tag + "_private_mb", priv);
            row.Put(tag + "_working_mb", ws);
            row.Put(tag + "_peak_working_mb", peak);
            row.Put(tag + "_managed_heap_mb", Mb(GC.GetTotalMemory(false)));
            row.Put(tag + "_mono_used_mb", Mb(Profiler.GetMonoUsedSizeLong()));
            double v = Recorder(ProfilerCategory.Memory, "Total Used Memory");
            if (v >= 0) row.Put(tag + "_total_used_mb", Mb((long)v));
            v = Recorder(ProfilerCategory.Memory, "Gfx Used Memory");
            if (v >= 0) row.Put(tag + "_gfx_used_mb", Mb((long)v));
            v = Recorder(ProfilerCategory.Memory, "GC Used Memory");
            if (v >= 0) row.Put(tag + "_gc_used_mb", Mb((long)v));
            long mesh = 0, tex = 0, rt = 0;
            int meshes = 0, texs = 0, rts = 0;
            var topTex = new List<KeyValuePair<long, string>>();
            foreach (var o in Resources.FindObjectsOfTypeAll<Mesh>())
            {
                meshes++;
                mesh += Profiler.GetRuntimeMemorySizeLong(o);
            }

            foreach (var o in Resources.FindObjectsOfTypeAll<Texture>())
            {
                long size = Profiler.GetRuntimeMemorySizeLong(o);
                if (o is RenderTexture)
                {
                    rts++;
                    rt += size;
                }
                else
                {
                    texs++;
                    tex += size;
                }

                if (top && size >= 256 * 1024) topTex.Add(new KeyValuePair<long, string>(size, o.name + " " + o.width + "x" + o.height + (o is RenderTexture ? " RT" : "")));
            }

            row.Put(tag + "_meshes", meshes);
            row.Put(tag + "_meshes_mb", Mb(mesh));
            row.Put(tag + "_textures", texs);
            row.Put(tag + "_textures_mb", Mb(tex));
            row.Put(tag + "_rts", rts);
            row.Put(tag + "_rts_mb", Mb(rt));
            if (top)
            {
                topTex.Sort((a, b) => b.Key.CompareTo(a.Key));
                var parts = new List<string>();
                for (int i = 0; i < Math.Min(6, topTex.Count); i++) parts.Add(topTex[i].Value + " " + Mb(topTex[i].Key).ToString("0.0") + "MB");
                row.note += (row.note.Length > 0 ? " | " : "") + tag + " largest: " + string.Join("; ", parts);
            }
        }

        public static int CountOf<T>() where T : UnityEngine.Object => Resources.FindObjectsOfTypeAll<T>().Length;
    }
}
