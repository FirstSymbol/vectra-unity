// Reads a profiler capture (.raw) of a harness development player and names the costs per row.
// A frame belongs to the row whose marker "PerfRow.<row>.<phase>" it contains. Per row: the samples with the most self time per frame
// on each thread (main, render, jobs), their calls and GC allocation.
// Arguments: -perfRaw <file.raw> -perfOut <json>
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace FirstSymbol.VectraPerf.Editor
{
    public static class VectraPerfProfile
    {
        [Serializable]
        class Sample
        {
            public string thread, name;
            public double selfMsPerFrame, totalMsPerFrame, callsPerFrame, gcBytesPerFrame;
        }

        [Serializable]
        class RowProfile
        {
            public string row;
            public int frames;
            public double mainThreadMs, worstFrameMs;
            public List<Sample> top = new List<Sample>();
            public List<Sample> worst = new List<Sample>();
        }

        [Serializable]
        class Output
        {
            public List<RowProfile> rows = new List<RowProfile>();
        }

        class Acc
        {
            public double self, total, calls, gc;
        }

        static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, name);
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
        }

        public static void Run()
        {
            int code = 0;
            try { Analyze(); }
            catch (Exception e)
            {
                Debug.LogError("VPERF_PROFILE failed: " + e);
                code = 1;
            }

            EditorApplication.Exit(code);
        }

        static void Analyze()
        {
            string raw = Arg("-perfRaw");
            if (!ProfilerDriver.LoadProfile(raw, false)) throw new InvalidOperationException("cannot load " + raw);
            int first = ProfilerDriver.firstFrameIndex, last = ProfilerDriver.lastFrameIndex;
            Debug.Log("VPERF_PROFILE frames " + first + ".." + last);
            var perRow = new Dictionary<string, Dictionary<string, Acc>>();
            var frames = new Dictionary<string, int>();
            var mainMs = new Dictionary<string, double>();
            var worstMs = new Dictionary<string, double>();
            var worstFound = new Dictionary<string, Dictionary<string, Acc>>();
            var children = new List<int>();
            for (int frame = first; frame <= last; frame++)
            {
                string row = null;
                double frameMain = 0;
                var found = new Dictionary<string, Acc>();
                for (int thread = 0; thread < 16; thread++)
                {
                    using (var view = ProfilerDriver.GetHierarchyFrameDataView(frame, thread, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnTotalTime, false))
                    {
                        if (view == null || !view.valid) break;
                        string threadName = view.threadName;
                        if (thread == 0 && row == null) row = FindRow(view);
                        if (row == null && thread == 0) break;
                        Walk(view, view.GetRootItemID(), threadName, found, children, "");
                        if (thread == 0) frameMain = view.GetItemColumnDataAsFloat(view.GetRootItemID(), HierarchyFrameDataView.columnTotalTime);
                    }
                }

                if (row == null) continue;
                // The deactivation and the reactivation frame of V11 carry the same marker; a deactivation runs OnDisable.
                if (row.StartsWith("V11", StringComparison.Ordinal))
                {
                    bool off = false;
                    foreach (var key in found.Keys)
                        if (key.Contains("OnDisable")) { off = true; break; }
                    row += off ? ".off" : ".on";
                }

                if (!perRow.TryGetValue(row, out var acc)) perRow[row] = acc = new Dictionary<string, Acc>();
                foreach (var pair in found)
                {
                    if (!acc.TryGetValue(pair.Key, out var a)) acc[pair.Key] = a = new Acc();
                    a.self += pair.Value.self;
                    a.total += pair.Value.total;
                    a.calls += pair.Value.calls;
                    a.gc += pair.Value.gc;
                }

                if (!worstMs.TryGetValue(row, out double wm) || frameMain > wm)
                {
                    worstMs[row] = frameMain;
                    worstFound[row] = found;
                }

                frames[row] = (frames.TryGetValue(row, out int n) ? n : 0) + 1;
                mainMs[row] = (mainMs.TryGetValue(row, out double m) ? m : 0) + frameMain;
            }

            var output = new Output();
            foreach (var pair in perRow)
            {
                int n = frames[pair.Key];
                var profile = new RowProfile { row = pair.Key, frames = n, mainThreadMs = mainMs[pair.Key] / n, worstFrameMs = worstMs[pair.Key] };
                var worstList = new List<KeyValuePair<string, Acc>>(worstFound[pair.Key]);
                worstList.Sort((a, b) => b.Value.self.CompareTo(a.Value.self));
                for (int i = 0; i < Math.Min(25, worstList.Count); i++)
                {
                    string[] parts = worstList[i].Key.Split(new[] { '|' }, 2);
                    var a = worstList[i].Value;
                    profile.worst.Add(new Sample { thread = parts[0], name = parts[1], selfMsPerFrame = a.self, totalMsPerFrame = a.total, callsPerFrame = a.calls, gcBytesPerFrame = a.gc });
                }

                var list = new List<KeyValuePair<string, Acc>>(pair.Value);
                list.Sort((a, b) => b.Value.self.CompareTo(a.Value.self));
                for (int i = 0; i < Math.Min(40, list.Count); i++)
                {
                    string[] parts = list[i].Key.Split(new[] { '|' }, 2);
                    var a = list[i].Value;
                    profile.top.Add(new Sample { thread = parts[0], name = parts[1], selfMsPerFrame = a.self / n, totalMsPerFrame = a.total / n, callsPerFrame = a.calls / n, gcBytesPerFrame = a.gc / n });
                }

                // The largest allocators too, whatever their time.
                list.Sort((a, b) => b.Value.gc.CompareTo(a.Value.gc));
                for (int i = 0; i < Math.Min(8, list.Count); i++)
                {
                    if (list[i].Value.gc <= 0) break;
                    string[] parts = list[i].Key.Split(new[] { '|' }, 2);
                    bool have = profile.top.Exists(s => s.thread == parts[0] && s.name == parts[1]);
                    if (have) continue;
                    var a = list[i].Value;
                    profile.top.Add(new Sample { thread = parts[0], name = parts[1], selfMsPerFrame = a.self / n, totalMsPerFrame = a.total / n, callsPerFrame = a.calls / n, gcBytesPerFrame = a.gc / n });
                }

                output.rows.Add(profile);
            }

            File.WriteAllText(Arg("-perfOut"), JsonUtility.ToJson(output, true));
            Debug.Log("VPERF_PROFILE done rows=" + output.rows.Count);
        }

        static string FindRow(HierarchyFrameDataView view)
        {
            var stack = new List<int> { view.GetRootItemID() };
            var kids = new List<int>();
            int guard = 0;
            while (stack.Count > 0 && guard++ < 20000)
            {
                int id = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);
                string name = view.GetItemName(id);
                if (name.StartsWith("PerfRow.", StringComparison.Ordinal)) return name.Substring(8);
                kids.Clear();
                view.GetItemChildren(id, kids);
                stack.AddRange(kids);
            }

            return null;
        }

        // Self time and self GC allocation per sample name; the GC column of the view is inclusive, so the children are taken off.
        static void Walk(HierarchyFrameDataView view, int id, string thread, Dictionary<string, Acc> into, List<int> scratch, string parent)
        {
            var kids = new List<int>();
            view.GetItemChildren(id, kids);
            double gcChildren = 0;
            foreach (int kid in kids) gcChildren += view.GetItemColumnDataAsFloat(kid, HierarchyFrameDataView.columnGcMemory);
            string name = view.GetItemName(id);
            if (!name.StartsWith("PerfRow.", StringComparison.Ordinal))
            {
                // An allocation is named after the sample it happens in, so that its cause can be read.
                string key = thread + "|" + (name == "GC.Alloc" ? "GC.Alloc <- " + parent : name);
                if (!into.TryGetValue(key, out var acc)) into[key] = acc = new Acc();
                acc.self += view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnSelfTime);
                acc.total += view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnTotalTime);
                acc.calls += view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnCalls);
                acc.gc += Math.Max(0, view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnGcMemory) - gcChildren);
            }

            foreach (int kid in kids) Walk(view, kid, thread, into, scratch, name);
        }
    }
}
