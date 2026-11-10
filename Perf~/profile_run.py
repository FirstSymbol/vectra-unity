"""Profiler captures of a development player and the causes per row. Used by perf.py profile."""
import json
import os
import sys


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
        data = json.load(open(out, encoding="utf-8"))
        for row in data["rows"]:
            lines.append(f"\n#### {args.group}/{side} {row['row']}: {row['frames']} frames, main thread {row['mainThreadMs']:.2f} ms per frame\n")
            lines.append("| thread | sample | self ms/frame | total ms/frame | calls/frame | GC B/frame |")
            lines.append("|---|---|---|---|---|---|")
            for s in row["top"][:14]:
                lines.append(f"| {s['thread']} | {s['name']} | {s['selfMsPerFrame']:.3f} | {s['totalMsPerFrame']:.3f} | {s['callsPerFrame']:.1f} | {s['gcBytesPerFrame']:.0f} |")
            if row.get("worst"):
                lines.append(f"\nslowest frame of the row: main thread {row['worstFrameMs']:.2f} ms\n")
                lines.append("| thread | sample | self ms | total ms | calls | GC B |")
                lines.append("|---|---|---|---|---|---|")
                for s in row["worst"][:12]:
                    lines.append(f"| {s['thread']} | {s['name']} | {s['selfMsPerFrame']:.3f} | {s['totalMsPerFrame']:.3f} | {s['callsPerFrame']:.0f} | {s['gcBytesPerFrame']:.0f} |")
    text = "\n".join(lines) + "\n"
    open(os.path.join(folder, f"causes_{args.group}.md"), "w", encoding="utf-8").write(text)
    print(text[:4000])
