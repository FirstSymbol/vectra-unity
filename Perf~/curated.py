"""The curated view of the tables: the rows of the plan with the figures a reader wants.
Release IL2CPP with Mono in brackets, development counters beside. Used by report.py."""

SIDES = ["vectra", "ref", "vg"]

# (title, group, row, [(label, metric, kind)]); kind rel = timing from release players, dev = counters and allocation from development players.
CURATED = [
    ("V1 create 1000 shapes + first frame", "canvas", "V1", [("create call ms", "create_ms", "rel"), ("first frame, work ms (span)", "v1_first_span_ms", "rel"), ("first frame, wall ms", "v1_first_wall_ms", "rel"), ("GCs in the 12 frames", "v1_gc_collections", "rel"), ("create alloc B", "create_alloc_b", "dev"), ("first frame alloc B", "v1_first_alloc_b", "dev")]),
    ("V1n the same without the gradient shape (no gradient row is baked)", "v1n", "V1n", [("create call ms", "create_ms", "rel"), ("first frame, work ms (span)", "v1n_first_span_ms", "rel"), ("first frame, wall ms", "v1n_first_wall_ms", "rel"), ("GCs in the 12 frames", "v1n_gc_collections", "rel"), ("steady wall ms", "v1ns_wall_ms", "rel"), ("first frame alloc B", "v1n_first_alloc_b", "dev")]),
    ("V4n the same without the dashed-stroke shape", "v4n", "V4n", [("wall ms", "v4n_wall_ms", "rel"), ("main-thread work ms (span)", "v4n_span_ms", "rel"), ("GPU ms", "v4n_gpu_ms", "rel"), ("SetPass", "v4n_setpass_calls", "dev"), ("draw calls", "v4n_standard_draw_calls", "dev"), ("triangles", "v4n_triangles", "dev")]),
    ("V4 a frame of 1000 unchanging", "canvas", "V4", [("wall ms", "v4_wall_ms", "rel"), ("main-thread work ms (span)", "v4_span_ms", "rel"), ("GPU ms", "v4_gpu_ms", "rel"), ("CPU main (FrameTiming) ms", "v4_cpumain_ms", "rel"), ("CPU render thread ms", "v4_cpurender_ms", "rel"), ("alloc B/frame", "v4_alloc_b", "dev"), ("SetPass", "v4_setpass_calls", "dev"), ("draw calls", "v4_standard_draw_calls", "dev"), ("triangles", "v4_triangles", "dev"), ("triangles of kind 3, the dashed stroke", "kind3_triangles", "dev"), ("triangles of kind 0, a rounded rectangle", "kind0_triangles", "dev")]),
    ("V3 colour of 100 per frame", "canvas", "V3", [("wall ms", "v3_wall_ms", "rel"), ("work ms (span)", "v3_span_ms", "rel"), ("alloc B/frame", "v3_alloc_b", "dev")]),
    ("V5 200 tweened (move, rotate, pulse)", "canvas", "V5", [("wall ms", "v5_wall_ms", "rel"), ("work ms (span)", "v5_span_ms", "rel"), ("alloc B/frame", "v5_alloc_b", "dev")]),
    ("V2 rebuild 1000 (size change)", "canvas", "V2", [("wall ms", "v2_wall_ms", "rel"), ("work ms (span)", "v2_span_ms", "rel"), ("alloc B/frame", "v2_alloc_b", "dev")]),
    ("V11 deactivate / reactivate 1000", "canvas", "V11", [("off: call ms", "off_call_ms", "rel"), ("off: frame work ms", "off_span_ms", "rel"), ("on: call ms", "on_call_ms", "rel"), ("on: frame work ms", "on_span_ms", "rel"), ("on alloc B", "on_alloc_b", "dev")]),
    ("V6a 100 drop shadows", "fx", "V6a", [("create ms", "create_ms", "rel"), ("first frame work ms", "fx_first_span_ms", "rel"), ("steady wall ms", "fx_steady_wall_ms", "rel"), ("GPU ms", "fx_steady_gpu_ms", "rel"), ("SetPass", "fx_steady_setpass_calls", "dev")]),
    ("V6b 100 outer glows", "fx", "V6b", [("create ms", "create_ms", "rel"), ("first frame work ms", "fx_first_span_ms", "rel"), ("steady wall ms", "fx_steady_wall_ms", "rel"), ("GPU ms", "fx_steady_gpu_ms", "rel")]),
    ("V6c 100 layer blurs", "fx", "V6c", [("create ms", "create_ms", "rel"), ("first frame work ms", "fx_first_span_ms", "rel"), ("steady wall ms", "fx_steady_wall_ms", "rel"), ("GPU ms", "fx_steady_gpu_ms", "rel")]),
    ("V7a 200 under a stencil mask", "fx", "V7a", [("create ms", "create_ms", "rel"), ("first frame work ms", "mask_first_span_ms", "rel"), ("steady wall ms", "mask_steady_wall_ms", "rel"), ("GPU ms", "mask_steady_gpu_ms", "rel"), ("SetPass", "mask_steady_setpass_calls", "dev")]),
    ("V7b 200 under a soft mask", "fx", "V7b", [("create ms", "create_ms", "rel"), ("first frame work ms", "mask_first_span_ms", "rel"), ("steady wall ms", "mask_steady_wall_ms", "rel"), ("GPU ms", "mask_steady_gpu_ms", "rel")]),
    ("V7c 200 under RectMask2D", "fx", "V7c", [("create ms", "create_ms", "rel"), ("first frame work ms", "mask_first_span_ms", "rel"), ("steady wall ms", "mask_steady_wall_ms", "rel"), ("GPU ms", "mask_steady_gpu_ms", "rel")]),
    ("V8 300 SVG icons", "svg", "V8", [("create ms", "create_ms", "rel"), ("first frame work ms", "v8_first_span_ms", "rel"), ("first frame wall ms", "v8_first_wall_ms", "rel"), ("steady wall ms", "v8s_wall_ms", "rel"), ("GPU ms", "v8s_gpu_ms", "rel"), ("textures added MB", "post-pre_textures_mb", "rel"), ("render textures added MB", "post-pre_rts_mb", "rel"), ("RTs added", "post-pre_rts", "rel"), ("meshes added MB", "post-pre_meshes_mb", "rel"), ("private bytes added MB", "post-pre_private_mb", "rel"), ("managed heap added MB", "post-pre_managed_heap_mb", "rel"), ("SetPass", "v8s_setpass_calls", "dev"), ("alloc B/frame", "v8s_alloc_b", "dev")]),
    ("V9 scroll list of 500 icons", "svg", "V9", [("create ms", "create_ms", "rel"), ("first frame work ms", "v9_first_span_ms", "rel"), ("scroll wall ms", "v9s_wall_ms", "rel"), ("scroll work ms", "v9s_span_ms", "rel"), ("GPU ms", "v9s_gpu_ms", "rel"), ("alloc B/frame", "v9s_alloc_b", "dev")]),
    ("V10a boolean exact, 100", "bool", "V10a", [("create ms", "create_ms", "rel"), ("first frame work ms", "b_first_span_ms", "rel"), ("rebuild work ms (100)", "rebuild_span_ms", "rel"), ("rebuild alloc B/frame", "rebuild_alloc_b", "dev")]),
    ("V10b boolean smooth, 100", "bool", "V10b", [("create ms", "create_ms", "rel"), ("first frame work ms", "b_first_span_ms", "rel"), ("rebuild work ms (100)", "rebuild_span_ms", "rel"), ("rebuild alloc B/frame", "rebuild_alloc_b", "dev")]),
    ("V10c boolean group, 50", "bool", "V10c", [("create ms", "create_ms", "rel"), ("first frame work ms", "b_first_span_ms", "rel"), ("rebuild work ms (50)", "rebuild_span_ms", "rel"), ("rebuild alloc B/frame", "rebuild_alloc_b", "dev")]),
    ("W1 5000 world shapes", "world", "W1", [("create ms", "create_ms", "rel"), ("first frame work ms", "w1_first_span_ms", "rel"), ("steady wall ms", "w1s_wall_ms", "rel"), ("main-thread work ms", "w1s_span_ms", "rel"), ("GPU ms", "w1s_gpu_ms", "rel"), ("CPU render thread ms", "w1s_cpurender_ms", "rel"), ("SetPass", "w1s_setpass_calls", "dev"), ("SRP batcher draws", "w1s_srp_batcher_draw_calls", "dev"), ("standard draws", "w1s_standard_draw_calls", "dev"), ("alloc B/frame", "w1s_alloc_b", "dev")]),
    ("U1 1000 UI Toolkit elements", "uitk", "U1", [("create ms", "create_ms", "rel"), ("first frame work ms", "u1_first_span_ms", "rel"), ("steady wall ms", "u1s_wall_ms", "rel"), ("GPU ms", "u1s_gpu_ms", "rel"), ("SetPass", "u1s_setpass_calls", "dev"), ("alloc B/frame", "u1s_alloc_b", "dev")]),
    ("U1b resize 1000 UI Toolkit elements", "uitk", "U1b", [("wall ms", "u1r_wall_ms", "rel"), ("work ms", "u1r_span_ms", "rel"), ("alloc B/frame", "u1r_alloc_b", "dev")]),
    ("S1 sprites of the corpus (256 px)", "sprite", "S1", [("42 docs, cold first calls ms", "cold_total_ms", "rel"), ("42 docs, warm ms", "total_ms", "rel"), ("worst doc warm ms", "worst_ms", "rel"), ("failed docs", "failed", "rel")]),
    ("S1b 100 baked shapes (128 px)", "sprite", "S1_shapes", [("median ms per shape", "median_ms", "rel"), ("p95 ms", "p95_ms", "rel")]),
    ("I2 parse of the corpus", "parse", "I2", [("42 docs total ms", "total_ms", "rel"), ("worst doc ms", "worst_ms", "rel"), ("alloc total B", "total_alloc_b", "dev"), ("failed docs", "failed", "rel")]),
    ("I2 parse of the 8 icons", "parse", "I2_icons", [("total ms", "total_ms", "rel"), ("alloc total B", "total_alloc_b", "dev")]),
    ("MEM canvas 1000 shapes", "canvas", "MEM_canvas", [("private MB before", "m0_private_mb", "rel"), ("private MB added with content", "m1-m0_private_mb", "rel"), ("private MB added, after collect", "m1c-m0_private_mb", "rel"), ("private MB left after destroy", "m2-m0_private_mb", "rel"), ("managed heap added MB", "m1-m0_managed_heap_mb", "rel"), ("managed heap left after destroy MB", "m2-m0_managed_heap_mb", "rel"), ("meshes MB added", "m1-m0_meshes_mb", "rel"), ("textures MB added", "m1-m0_textures_mb", "rel"), ("textures left MB", "m2-m0_textures_mb", "rel"), ("meshes left", "m2-m0_meshes", "rel")]),
    ("MEM world 5000 shapes", "world", "MEM_world", [("private MB added with content", "m1-m0_private_mb", "rel"), ("private MB left after destroy", "m2-m0_private_mb", "rel"), ("managed heap added MB", "m1-m0_managed_heap_mb", "rel"), ("meshes MB added", "m1-m0_meshes_mb", "rel"), ("textures MB added", "m1-m0_textures_mb", "rel"), ("meshes left", "m2-m0_meshes", "rel")]),
    ("MEM UI Toolkit 1000", "uitk", "MEM_uitk", [("private MB added with content", "m1-m0_private_mb", "rel"), ("private MB left after destroy", "m2-m0_private_mb", "rel"), ("managed heap added MB", "m1-m0_managed_heap_mb", "rel"), ("meshes MB added", "m1-m0_meshes_mb", "rel"), ("textures MB added", "m1-m0_textures_mb", "rel"), ("RTs added", "m1-m0_rts", "rel"), ("RT MB added", "m1-m0_rts_mb", "rel")]),
    ("I3 first use (frame work ms; wall ms of the frame in brackets of the second table)", "cold", "*", []),
]


def cell(summary, group, row, metric, side, kind, fmt):
    by = summary.get(group, {}).get(row, {}).get(metric, {}).get(side, {})
    suffix = "dev" if kind == "dev" else "rel"
    main = by.get("il2cpp_" + suffix)
    mono = by.get("mono_" + suffix)
    gamma = by.get("il2cpp_relgamma") if kind == "rel" else None
    parts = []
    if main:
        spread = max(main["values"]) - min(main["values"])
        parts.append(fmt(main["median"], metric) + (f" ±{fmt(spread, metric)}" if spread and len(main["values"]) > 1 else ""))
    else:
        parts.append("-")
    if mono:
        differs = not main
        if main:
            tolerance = max(0.1 * abs(main["median"]), max(mono["values"]) - min(mono["values"]), max(main["values"]) - min(main["values"]))
            differs = abs(mono["median"] - main["median"]) > tolerance
        if differs:
            parts.append("[" + fmt(mono["median"], metric) + "]")
    if gamma:
        parts.append("{Gamma project " + fmt(gamma["median"], metric) + "}")
    return " ".join(parts)


def write(summary, path, fmt):
    out = ["Release IL2CPP median of 5 ±spread (max - min); [Mono release] where it differs by more than 10% and the spread; (dev) = development IL2CPP player, median of 2; {Gamma project} = the Gamma colour-space copy.", ""]
    for title, group, row, metrics in CURATED:
        if row == "*":
            out.append("### I3 first use of each feature: the frame it first appears in\n")
            rows = [r for r in summary.get(group, {}) if r.startswith("I3_")]
            out.append("| step | Vectra work / wall ms | uGUI / built-in | Unity VG |")
            out.append("|---|---|---|---|")
            for r in rows:
                cells = []
                for side in SIDES:
                    a = cell(summary, group, r, "c_first_span_ms", side, "rel", fmt)
                    b = cell(summary, group, r, "c_first_wall_ms", side, "rel", fmt)
                    cells.append("-" if a == "-" else a + " / " + b)
                out.append(f"| {r[3:]} | " + " | ".join(cells) + " |")
            out.append("")
            continue
        variants = [row + "_browser", row + "_linear", row] if row in ("V8", "V9") else [row]
        shown = False
        for v in variants:
            if v not in summary.get(group, {}):
                continue
            if not shown:
                out.append(f"### {title}\n")
                shown = True
            label = "" if v == row else f" ({v[len(row) + 1:]})"
            out.append(f"| metric{label} | Vectra | uGUI / built-in | Unity VG |")
            out.append("|---|---|---|---|")
            for name, metric, kind in metrics:
                cells = [cell(summary, group, v, metric, side, kind, fmt) for side in SIDES]
                if all(c == "-" for c in cells):
                    continue
                out.append(f"| {name}{' (dev)' if kind == 'dev' else ''} | " + " | ".join(cells) + " |")
            out.append("")
    open(path, "w", encoding="utf-8").write("\n".join(out))
