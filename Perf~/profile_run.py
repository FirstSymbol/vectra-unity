"""Profiler captures of a development player and the causes per row. Used by perf.py profile.
perf.py profile --group canvas --sides vectra,ref runs the player with the profiler recording, has a batch editor read the capture
(VectraPerfProfile) and writes <work>/Profiles/<tag>/causes_<group>.md; an existing analysis is read again, not recomputed (--fresh redoes it)."""
import json
import os

# Threads that wait, and samples that only say a thread is waiting or are the profiler / development overlay itself: left out of the cause tables.
THREADS = ("Main Thread", "Render Thread")
WAITS = {
    "Semaphore.WaitForSignal", "DXGI.WaitOnSwapChain", "Idle", "WaitForJobGroupID", "Gfx.WaitForPresentOnGfxThread", "Gfx.WaitForRenderThread", "PlayerLoop", "Main Thread",
    "Profiler.FlushMemoryCounters", "PlayerConnection.Poll", "GUI.Repaint", "WaitForLastPresentation", "Gfx.PresentFrame", "RenderLoop", "GpuRecorder.FrameTick", "PlayerEndOfFrame",
    "UnityEngine.IMGUIModule.dll!UnityEngine::GUIStyle.GetDimensions() [Invoke]", "Inl_On Record Render Graph", "Render Thread", "GfxDeviceD3D12.WaitForGPU",
}


def keep(sample):
    return sample["thread"] in THREADS and sample["name"] not in WAITS and sample["name"] != ""


def render(data, group, side):
    lines = []
    for row in data["rows"]:
        if row["row"].startswith("CTRL") and group != "canvas":
            continue
        lines.append(f"\n#### {group}/{side} {row['row']}: {row['frames']} frames, main thread {row['mainThreadMs']:.2f} ms per frame, slowest frame {row['worstFrameMs']:.1f} ms\n")
        lines.append("| thread | sample | self ms/frame | total ms/frame | calls/frame | GC B/frame |")
        lines.append("|---|---|---|---|---|---|")
        for s in [s for s in row["top"] if keep(s)][:10]:
            lines.append(f"| {s['thread']} | {s['name']} | {s['selfMsPerFrame']:.3f} | {s['totalMsPerFrame']:.3f} | {s['callsPerFrame']:.1f} | {s['gcBytesPerFrame']:.0f} |")
        worst = [s for s in row.get("worst", []) if keep(s)][:8]
        if worst:
            lines.append("\nslowest frame:\n")
            lines.append("| thread | sample | self ms | total ms | calls | GC B |")
            lines.append("|---|---|---|---|---|---|")
            for s in worst:
                lines.append(f"| {s['thread']} | {s['name']} | {s['selfMsPerFrame']:.3f} | {s['totalMsPerFrame']:.3f} | {s['callsPerFrame']:.0f} | {s['gcBytesPerFrame']:.0f} |")
    return lines


def run(args, work, build, run_player, sync_harness, unity):
    backend = args.backends.split(",")[0]
    exe = build(backend, "dev", deep=getattr(args, "deep", False))
    sync_harness(args.sandbox, bool(args.vg), with_corpus=False)
    folder = os.path.join(work, "Profiles", args.tag)
    os.makedirs(folder, exist_ok=True)
    lines = []
    for side in (args.sides.split(",") if args.sides else ["vectra"]):
        base = os.path.join(folder, f"{backend}_{args.group}_{side}")
        raw, out = base + ".raw", base + ".json"
        if not os.path.exists(out) or args.fresh:
            for p in (raw, out):
                if os.path.exists(p):
                    os.remove(p)
            result = base + ".run.json"
            status, took = run_player(exe, args.group, side, result, base + ".log", 0, ["-profile", raw])
            print(f"profile {args.group}/{side}: {status} {took:.0f} s, raw {os.path.getsize(raw) // 1048576 if os.path.exists(raw) else 'missing'} MB", flush=True)
            if not os.path.exists(raw):
                continue
            unity(args.sandbox, "FirstSymbol.VectraPerf.Editor.VectraPerfProfile.Run", ["-perfRaw", raw, "-perfOut", out], base + ".analyze.log")
        if not os.path.exists(out):
            print("no analysis for", side, flush=True)
            continue
        lines += render(json.load(open(out, encoding="utf-8")), args.group, side)
    text = "\n".join(lines) + "\n"
    open(os.path.join(folder, f"causes_{args.group}.md"), "w", encoding="utf-8").write(text)
    print(text[:3000])
