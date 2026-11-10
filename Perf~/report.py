"""Tables from the runs on disk: median over the rounds, spread (max - min) beside it."""
import json
import os
import statistics

SIDES = ["vectra", "ref", "vg"]
import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
SIDE_NAMES = {"vectra": "Vectra", "ref": "uGUI/built-in", "vg": "Unity VG"}
COUNTERS = ("batches", "setpass_calls", "draw_calls", "triangles", "vertices")


def derive(res):
    """Adds the deltas a reader wants: memory before and after, and between the memory snapshots."""
    for row in res["rows"]:
        m = {x["k"]: x["v"] for x in row["m"]}
        extra = {}
        for a, b in (("pre", "post"), ("pre", "rel"), ("m0", "m1"), ("m0", "m1c"), ("m0", "m2"), ("m1", "m1c")):
            for k, v in m.items():
                if k.startswith(a + "_"):
                    suffix = k[len(a) + 1:]
                    other = m.get(b + "_" + suffix)
                    if other is not None and v >= 0 and other >= 0:
                        extra[f"{b}-{a}_{suffix}"] = other - v
        for k, v in extra.items():
            row["m"].append({"k": k, "v": v})
    return res


def med(v):
    return statistics.median(v) if v else None


def fmt(v, key):
    if v is None:
        return "-"
    if key.endswith("_b") or key.endswith("_bytes") or "count" in key or key in COUNTERS or key.split("_")[-1] in COUNTERS:
        return f"{v:,.0f}"
    if abs(v) >= 100:
        return f"{v:,.0f}"
    if abs(v) >= 10:
        return f"{v:.1f}"
    return f"{v:.3f}"


def cell(vals, key):
    if not vals:
        return "-"
    m = med(vals)
    s = max(vals) - min(vals)
    base = fmt(m, key)
    if len(vals) > 1 and s > 0:
        return f"{base} ±{fmt(s, key)}"
    return base


def rows_of(results):
    rows, notes = {}, {}
    for res in results:
        derive(res)
        for row in res["rows"]:
            slot = rows.setdefault(row["id"], {})
            notes.setdefault(row["id"], row.get("note", ""))
            for m in row["m"]:
                slot.setdefault(m["k"], []).append(m["v"])
    return rows, notes


def build(data, folder, work, groups):
    out = []
    summary = {}
    meta = {}
    for (backend, kind), by in sorted(data.items()):
        for (group, side), results in by.items():
            rows, notes = rows_of(results)
            for rid, metrics in rows.items():
                for k, vals in metrics.items():
                    summary.setdefault(group, {}).setdefault(rid, {}).setdefault(k, {}).setdefault(side, {})[f"{backend}_{kind}"] = {"median": med(vals), "values": vals, "n": len(vals)}
            meta[f"{backend}_{kind}_{group}_{side}"] = {
                "n": len(results), "gpu": results[0].get("gpu"), "unity": results[0].get("unity"), "errors": [r.get("error") for r in results if r.get("error")],
                "battery": any(r.get("battery") for r in results), "load_max": max(r.get("load_before", 0) for r in results),
                "seconds": statistics.median([r.get("seconds", 0) for r in results]),
                "notes": notes,
            }
    json.dump({"summary": summary, "meta": meta}, open(os.path.join(folder, "summary.json"), "w"), indent=1)

    # One table per group: a line per row and metric, a column per side and build.
    for group in groups:
        if group not in summary:
            continue
        out.append(f"\n### {group}\n")
        cols = []
        for side in SIDES:
            for build_kind in ("il2cpp_rel", "mono_rel", "il2cpp_dev"):
                if any(side in summary[group][r].get(k, {}) and build_kind in summary[group][r][k][side] for r in summary[group] for k in summary[group][r]):
                    cols.append((side, build_kind))
        out.append("| row | metric | " + " | ".join(f"{SIDE_NAMES[s]} {b}" for s, b in cols) + " |")
        out.append("|---|---|" + "---|" * len(cols))
        for rid, metrics in summary[group].items():
            for k, by_side in metrics.items():
                cells = []
                for side, b in cols:
                    entry = by_side.get(side, {}).get(b)
                    cells.append(cell(entry["values"], k) if entry else "")
                if any(cells):
                    out.append(f"| {rid} | {k} | " + " | ".join(cells) + " |")
    text = "\n".join(out) + "\n"
    open(os.path.join(folder, "tables.md"), "w", encoding="utf-8").write(text)
    import curated
    curated.write(summary, os.path.join(folder, "curated.md"), fmt)
    print("tables:", os.path.join(folder, "tables.md"), " curated:", os.path.join(folder, "curated.md"))
