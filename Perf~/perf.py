"""Vectra performance harness runner. See README.md.

python -I perf.py build   [--backends il2cpp,mono] [--kinds rel,dev]
python -I perf.py run     [--backends il2cpp,mono] [--kinds rel] [--rounds 5] [--groups a,b] [--sides ...]
python -I perf.py import  [--rounds 6]            editor import of the corpus (I1)
python -I perf.py size                            player size above an empty URP uGUI project
python -I perf.py profile --group canvas          dev player with the profiler on, then a per-row cause table
python -I perf.py report                          tables from the runs on disk
python -I perf.py all                             everything above in order

Everything is resumable: a run that has its JSON on disk is not repeated (--fresh removes the runs).
"""
import argparse
import ctypes
import json
import os
import shutil
import statistics
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ap = argparse.ArgumentParser()
ap.add_argument("command", choices=["build", "run", "import", "size", "profile", "report", "all", "sync"])
ap.add_argument("--editor", default=os.environ.get("VPERF_EDITOR", "D:/General/Other/Unity/Editors/6000.4.8f1/Editor/Unity.exe"))
ap.add_argument("--sandbox", default=os.environ.get("VPERF_SANDBOX", "D:/UnityBuilds/VectraPerfSb"))
ap.add_argument("--sizea", default=os.environ.get("VPERF_SIZEA", "D:/UnityBuilds/VectraPerfSizeA"), help="URP + uGUI project without Vectra (made when missing) for the size row")
ap.add_argument("--sizeb", default=os.environ.get("VPERF_SIZEB", "D:/UnityBuilds/VectraPerfSizeB"), help="the same project with Vectra")
ap.add_argument("--gamma", default=os.environ.get("VPERF_GAMMA", "D:/UnityBuilds/VectraPerfSbG"), help="Gamma colour space copy of the sandbox, for row V8 (made when missing; kind relgamma)")
ap.add_argument("--work", default=os.environ.get("VPERF_WORK", "D:/UnityBuilds/LetteraTools/v080/P/work"))
ap.add_argument("--hidden", default=os.environ.get("VPERF_HIDDEN", os.path.join(HERE, "hidden_run.py")))
ap.add_argument("--corpus", default=os.environ.get("VPERF_CORPUS", "D:/UnityBuilds/LetteraTools/v070/figma/svg;D:/UnityBuilds/LetteraTools/v070/figma2/svg"), help="folders of .svg, separated by ;")
ap.add_argument("--backends", default="il2cpp,mono")
ap.add_argument("--kinds", default="rel")
ap.add_argument("--rounds", type=int, default=5)
ap.add_argument("--groups", default="")
ap.add_argument("--sides", default="")
ap.add_argument("--group", default="canvas")
ap.add_argument("--tag", default="baseline", help="name of this measurement (a label for the result folder)")
ap.add_argument("--vg", type=int, default=1, help="1 when com.unity.vectorgraphics is in the sandbox")
ap.add_argument("--fresh", action="store_true")
ap.add_argument("--timeout", type=int, default=420)
args = ap.parse_args()

WORK = args.work
GROUPS = {
    "cold": ["vectra", "ref", "vg"],
    "canvas": ["vectra", "ref"],
    "fx": ["vectra", "ref"],
    "svg": ["vectra", "ref", "vg"],
    "bool": ["vectra"],
    "world": ["vectra", "ref"],
    "uitk": ["vectra", "ref"],
    "sprite": ["vectra", "vg"],
    "parse": ["vectra", "vg"],
}
GAMMA_GROUPS = ["svg", "canvas"]
FLAGS_IDLE = 0x00000040
FLAGS_NOWINDOW = 0x08000000


def say(*a):
    print(*a, flush=True)


# ------------------------------------------------------------------ machine state

class PowerStatus(ctypes.Structure):
    _fields_ = [("ac", ctypes.c_ubyte), ("flag", ctypes.c_ubyte), ("percent", ctypes.c_ubyte), ("saver", ctypes.c_ubyte), ("life", ctypes.c_uint32), ("full", ctypes.c_uint32)]


def on_battery():
    status = PowerStatus()
    ctypes.windll.kernel32.GetSystemPowerStatus(ctypes.byref(status))
    return status.ac == 0


class FileTime(ctypes.Structure):
    _fields_ = [("lo", ctypes.c_uint32), ("hi", ctypes.c_uint32)]


def cpu_times():
    idle, kern, user = FileTime(), FileTime(), FileTime()
    ctypes.windll.kernel32.GetSystemTimes(ctypes.byref(idle), ctypes.byref(kern), ctypes.byref(user))
    v = lambda t: t.hi << 32 | t.lo
    return v(idle), v(kern) + v(user)


def cpu_load(seconds=2.0):
    i0, t0 = cpu_times()
    time.sleep(seconds)
    i1, t1 = cpu_times()
    total = t1 - t0
    return 100.0 * (1 - (i1 - i0) / total) if total else 0.0


def wait_quiet(limit=25.0, tries=30):
    """A timing run needs an idle machine: waits for the load to fall under limit percent."""
    for _ in range(tries):
        load = cpu_load(2.0)
        if load < limit:
            return load
        time.sleep(10)
    return cpu_load(2.0)


# ------------------------------------------------------------------ files

def sync_tree(src, dst):
    """Copies src into dst changing only what differs; removes files that are gone (with their .meta)."""
    os.makedirs(dst, exist_ok=True)
    wanted = set()
    for root, _, files in os.walk(src):
        rel = os.path.relpath(root, src)
        os.makedirs(os.path.join(dst, rel), exist_ok=True)
        for f in files:
            s, d = os.path.join(root, f), os.path.normpath(os.path.join(dst, rel, f))
            wanted.add(d)
            if not os.path.exists(d) or open(s, "rb").read() != open(d, "rb").read():
                shutil.copyfile(s, d)
    for root, _, files in os.walk(dst):
        for f in files:
            p = os.path.normpath(os.path.join(root, f))
            base = p[:-5] if p.endswith(".meta") else p
            if p not in wanted and base not in wanted and not p.endswith(".meta"):
                os.remove(p)
                if os.path.exists(p + ".meta"):
                    os.remove(p + ".meta")


def corpus_files():
    out, seen = [], set()
    for i, folder in enumerate(args.corpus.split(";")):
        for f in sorted(os.listdir(folder)):
            if f.lower().endswith(".svg"):
                name = f
                if name in seen:
                    name = "b_" + name
                seen.add(name)
                out.append((name, os.path.join(folder, f)))
    return out


def sync_harness(project, vg, with_corpus=True, with_import=True):
    base = os.path.join(project, "Assets", "Perf")
    sync_tree(os.path.join(HERE, "Player"), os.path.join(base, "Player"))
    if vg:
        sync_tree(os.path.join(HERE, "Player.Vg"), os.path.join(base, "Player.Vg"))
    elif os.path.exists(os.path.join(base, "Player.Vg")):
        shutil.rmtree(os.path.join(base, "Player.Vg"))
    sync_tree(os.path.join(HERE, "Editor", "Build"), os.path.join(base, "Editor", "Build"))
    if with_import:
        sync_tree(os.path.join(HERE, "Editor", "Import"), os.path.join(base, "Editor", "Import"))
    if with_corpus:
        folder = os.path.join(base, "Resources", "PerfCorpus")
        os.makedirs(folder, exist_ok=True)
        keep = set()
        for name, path in corpus_files():
            dst = os.path.join(folder, name[:-4] + ".txt")
            keep.add(dst)
            if not os.path.exists(dst) or open(dst, "rb").read() != open(path, "rb").read():
                shutil.copyfile(path, dst)
        for f in os.listdir(folder):
            p = os.path.join(folder, f)
            if p not in keep and not f.endswith(".meta"):
                os.remove(p)
                if os.path.exists(p + ".meta"):
                    os.remove(p + ".meta")


def harness_stamp():
    """What a built player depends on: the harness sources that go into the sandbox, and the package (commit and uncommitted changes)."""
    import hashlib
    h = hashlib.sha1()
    # Player code, and the one editor script that sets the player's settings; the analyzers and importers do not change a player.
    paths = [os.path.join(HERE, "Editor", "Build", "VectraPerfBuild.cs")]
    for sub in ("Player", "Player.Vg"):
        for root, _, files in sorted(os.walk(os.path.join(HERE, sub))):
            paths += [os.path.join(root, f) for f in sorted(files)]
    for path in paths:
        h.update(os.path.basename(path).encode())
        h.update(open(path, "rb").read())
    try:
        h.update(subprocess.check_output(["git", "-C", REPO, "diff", "HEAD", "--", "Runtime", "Runtime.UIToolkit", "Runtime.URP", "Runtime.HDRP", "Runtime.Lettera", "Runtime.TextMeshPro"]))
    except Exception:
        pass
    return h.hexdigest()[:12]


def unity(project, method, extra, log, timeout=3600):
    cmd = [args.editor, "-batchmode", "-quit", "-projectPath", project, "-executeMethod", method, "-logFile", log] + extra
    say("unity", method, "->", log)
    t = time.time()
    code = subprocess.run(cmd, creationflags=FLAGS_IDLE | FLAGS_NOWINDOW, timeout=timeout).returncode
    say("  exit", code, "%.0f s" % (time.time() - t))
    return code


def log_has(log, needle):
    return os.path.exists(log) and needle in open(log, encoding="utf-8", errors="replace").read()


def player_dir(backend, kind):
    return os.path.join(WORK, "Players", f"{backend}_{kind}")


def player_exe(backend, kind):
    return os.path.join(player_dir(backend, kind), "VectraPerf.exe")


# ------------------------------------------------------------------ build

def project_of(kind):
    return args.gamma if kind.endswith("gamma") else args.sandbox


def make_gamma():
    """A copy of the sandbox with the colour space switched to Gamma: no Library (it imports afresh), no assets the harness does not need."""
    g = args.gamma
    if os.path.exists(os.path.join(g, "ProjectSettings", "ProjectSettings.asset")):
        return
    say("making", g)
    robocopy(args.sandbox, g, ["/XD", "Library", "Temp", "Logs", "UserSettings", "Assets", "obj"])
    os.makedirs(os.path.join(g, "Assets"), exist_ok=True)
    for name in os.listdir(os.path.join(args.sandbox, "Assets")):
        if name in ("Resources", "Resources.meta", "DefaultVolumeProfile.asset", "DefaultVolumeProfile.asset.meta", "UniversalRenderPipelineGlobalSettings.asset", "UniversalRenderPipelineGlobalSettings.asset.meta", "Perf", "Perf.meta"):
            src = os.path.join(args.sandbox, "Assets", name)
            if os.path.isdir(src):
                robocopy(src, os.path.join(g, "Assets", name), [])
            else:
                shutil.copyfile(src, os.path.join(g, "Assets", name))
    sync_harness(g, bool(args.vg))
    log = os.path.join(WORK, "Logs", "gamma_set.log")
    os.makedirs(os.path.dirname(log), exist_ok=True)
    unity(g, "FirstSymbol.VectraPerf.Editor.VectraPerfBuild.SetGamma", [], log)
    if not log_has(log, "VPERF_GAMMA set"):
        sys.exit("could not switch to Gamma: " + log)


def build(backend, kind, deep=False):
    if kind.endswith("gamma"):
        make_gamma()
    project = project_of(kind)
    exe = player_exe(backend, kind + ("_deep" if deep else ""))
    stamp_file = os.path.join(os.path.dirname(exe), "harness.stamp")
    stamp = harness_stamp() + "+" + git_head()
    if os.path.exists(exe) and os.path.exists(stamp_file) and open(stamp_file).read() == stamp:
        return exe
    if os.path.exists(os.path.dirname(exe)):
        shutil.rmtree(os.path.dirname(exe))
    os.makedirs(os.path.dirname(exe))
    sync_harness(project, bool(args.vg))
    log = os.path.join(WORK, "Logs", f"build_{backend}_{kind}{'_deep' if deep else ''}.log")
    os.makedirs(os.path.dirname(log), exist_ok=True)
    extra = ["-perfOut", exe, "-perfBackend", backend, "-perfDev", "1" if kind.startswith("dev") else "0", "-perfMode", "harness", "-perfDeep", "1" if deep else "0"]
    for attempt in range(2):
        unity(project, "FirstSymbol.VectraPerf.Editor.VectraPerfBuild.Run", extra, log)
        if log_has(log, "VPERF_BUILD Succeeded"):
            open(stamp_file, "w").write(stamp)
            return exe
        say("  build attempt", attempt, "failed")
    for line in open(log, encoding="utf-8", errors="replace").read().splitlines():
        if "error CS" in line or "VPERF_BUILD" in line:
            say("   ", line[:240])
    sys.exit("build failed: " + log)


def git_head():
    try:
        return subprocess.check_output(["git", "-C", REPO, "rev-parse", "--short", "HEAD"], text=True).strip()
    except Exception:
        return "nogit"


# ------------------------------------------------------------------ run

def run_player(exe, group, side, out, log, rounds_index, extra=()):
    cmd = [sys.executable, "-I", args.hidden, str(args.timeout), exe, "-screen-width", "1280", "-screen-height", "720", "-screen-fullscreen", "0", "-logFile", log, "-perf", group, "-side", side, "-out", out] + list(extra)
    if rounds_index % 2 == 1 and group == "svg":
        cmd += ["-order", "rev"]
    t = time.time()
    done = subprocess.run(cmd, capture_output=True, text=True)
    return (done.stdout or "").strip(), time.time() - t


def runs_dir(backend, kind):
    return os.path.join(WORK, "Runs", args.tag, f"{backend}_{kind}")


def command_run():
    groups_arg = [g for g in (args.groups.split(",") if args.groups else GROUPS)]
    sides_filter = args.sides.split(",") if args.sides else None
    for backend in args.backends.split(","):
        for kind in args.kinds.split(","):
            exe = build(backend, kind)
            groups = [g for g in groups_arg if not kind.endswith("gamma") or g in GAMMA_GROUPS]
            folder = runs_dir(backend, kind)
            if args.fresh and os.path.exists(folder):
                shutil.rmtree(folder)
            os.makedirs(folder, exist_ok=True)
            rounds = args.rounds if kind in ("rel", "relgamma") else min(args.rounds, 2)
            for r in range(rounds):
                for group in groups:
                    sides = [s for s in GROUPS[group] if (not sides_filter or s in sides_filter) and (s != "vg" or args.vg)]
                    # The two sides alternate in order from round to round, so that neither always runs second.
                    if r % 2 == 1:
                        sides = sides[::-1]
                    for side in sides:
                        out = os.path.join(folder, f"{group}_{side}_{r}.json")
                        if os.path.exists(out):
                            continue
                        load = wait_quiet()
                        say(f"{backend}/{kind} round {r} {group}/{side} load {load:.0f}%{' BATTERY' if on_battery() else ''}")
                        status, took = run_player(exe, group, side, out, os.path.join(folder, f"{group}_{side}_{r}.log"), r)
                        ok = os.path.exists(out)
                        say(f"  {status} {took:.0f} s {'ok' if ok else 'NO RESULT'}")
                        if ok:
                            data = json.load(open(out, encoding="utf-8"))
                            data["load_before"] = load
                            data["battery"] = on_battery()
                            data["seconds"] = took
                            json.dump(data, open(out, "w", encoding="utf-8"))
                            if data.get("error"):
                                say("  error:", data["error"][:200].replace("\n", " "))


# ------------------------------------------------------------------ editor import (I1)

def command_import():
    sync_harness(args.sandbox, bool(args.vg), with_corpus=False)
    corpus = os.path.join(WORK, "ImportCorpus")
    os.makedirs(corpus, exist_ok=True)
    for name, path in corpus_files():
        shutil.copyfile(path, os.path.join(corpus, name))
    out = os.path.join(WORK, "Runs", args.tag, "import.json")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    if os.path.exists(out) and not args.fresh:
        say("import.json exists")
        return
    log = os.path.join(WORK, "Logs", "import.log")
    os.makedirs(os.path.dirname(log), exist_ok=True)
    wait_quiet()
    unity(args.sandbox, "FirstSymbol.VectraPerf.Editor.VectraPerfImport.Run", ["-perfCorpus", corpus, "-perfOut", out, "-perfRounds", str(args.rounds + 1)], log)
    say("import", "ok" if os.path.exists(out) else "FAILED " + log)


# ------------------------------------------------------------------ size

def folder_size(path):
    total = 0
    for root, _, files in os.walk(path):
        for f in files:
            total += os.path.getsize(os.path.join(root, f))
    return total


def robocopy(src, dst, extra):
    code = subprocess.run(["robocopy", src, dst, "/E", "/MT:2", "/NFL", "/NDL", "/NJH", "/NJS", "/NP"] + extra, capture_output=True).returncode
    if code >= 8:
        sys.exit(f"robocopy {src} -> {dst} failed ({code})")


def make_size_projects():
    """Two projects that differ only in the package: URP + uGUI, and the same with Vectra (no Lettera, no tests, no extra assets)."""
    a, b = args.sizea, args.sizeb
    if not os.path.exists(os.path.join(a, "Packages", "manifest.json")):
        say("making", a)
        robocopy(args.sandbox, a, ["/XD", "Library", "Temp", "Logs", "UserSettings", "Assets", "Packages", "obj"])
        os.makedirs(os.path.join(a, "Assets"), exist_ok=True)
        for name in os.listdir(os.path.join(args.sandbox, "Assets")):
            if name in ("Resources", "Resources.meta", "DefaultVolumeProfile.asset", "DefaultVolumeProfile.asset.meta", "UniversalRenderPipelineGlobalSettings.asset", "UniversalRenderPipelineGlobalSettings.asset.meta"):
                src = os.path.join(args.sandbox, "Assets", name)
                if os.path.isdir(src):
                    robocopy(src, os.path.join(a, "Assets", name), [])
                else:
                    shutil.copyfile(src, os.path.join(a, "Assets", name))
        os.makedirs(os.path.join(a, "Packages"), exist_ok=True)
        manifest = {"dependencies": {
            "com.unity.render-pipelines.universal": "17.4.0", "com.unity.ugui": "2.0.0",
            "com.unity.modules.imageconversion": "1.0.0", "com.unity.modules.physics": "1.0.0", "com.unity.modules.physics2d": "1.0.0",
            "com.unity.modules.ui": "1.0.0", "com.unity.modules.uielements": "1.0.0", "com.unity.modules.imgui": "1.0.0", "com.unity.modules.jsonserialize": "1.0.0",
            "com.unity.modules.animation": "1.0.0", "com.unity.modules.unitywebrequest": "1.0.0", "com.unity.modules.assetbundle": "1.0.0",
        }}
        json.dump(manifest, open(os.path.join(a, "Packages", "manifest.json"), "w"), indent=2)
        # Opens it once so that its Library exists (and is what B is copied from).
        unity_open(a, os.path.join(WORK, "Logs", "size_a_open.log"))
    if not os.path.exists(os.path.join(b, "Packages", "manifest.json")):
        say("making", b)
        robocopy(a, b, ["/XD", "Temp", "Logs", "UserSettings"])
        manifest = json.load(open(os.path.join(a, "Packages", "manifest.json")))
        manifest["dependencies"]["com.firstsymbol.vectra"] = "file:" + REPO.replace("\\", "/")
        manifest["dependencies"].pop("com.unity.modules.screencapture", None)
        json.dump(manifest, open(os.path.join(b, "Packages", "manifest.json"), "w"), indent=2)
        unity_open(b, os.path.join(WORK, "Logs", "size_b_open.log"))


def unity_open(project, log):
    os.makedirs(os.path.dirname(log), exist_ok=True)
    subprocess.run([args.editor, "-batchmode", "-quit", "-projectPath", project, "-logFile", log], creationflags=FLAGS_IDLE | FLAGS_NOWINDOW, timeout=3600)


def shader_variants(log):
    """Per Vectra shader: variants in the space and variants kept, summed over its passes (from the build log)."""
    out, name, full, kept = {}, None, 0, 0
    import re
    for line in open(log, encoding="utf-8", errors="replace").read().splitlines():
        m = re.match(r'Compiling shader "(.*)"', line)
        if m:
            if name:
                out[name] = {"full": full, "kept": kept}
            name, full, kept = m.group(1), 0, 0
            continue
        m = re.match(r"\s+Full variant space:\s+(\d+)", line)
        if m and name:
            full += int(m.group(1))
        m = re.match(r"\s+After scriptable stripping:\s+(\d+)", line)
        if m and name:
            kept += int(m.group(1))
    if name:
        out[name] = {"full": full, "kept": kept}
    return {k: v for k, v in out.items() if "Vectra" in k}


def command_size():
    make_size_projects()
    results = {}
    for name, project, no_vectra in (("empty", args.sizea, True), ("vectra", args.sizeb, False)):
        out = os.path.join(WORK, "Players", f"size_{name}_il2cpp", "Size.exe")
        if os.path.exists(os.path.dirname(out)):
            shutil.rmtree(os.path.dirname(out))
        os.makedirs(os.path.dirname(out))
        sync_tree(os.path.join(HERE, "Editor", "Build"), os.path.join(project, "Assets", "Perf", "Editor", "Build"))
        log = os.path.join(WORK, "Logs", f"size_{name}.log")
        unity(project, "FirstSymbol.VectraPerf.Editor.VectraPerfBuild.Run",
              ["-perfOut", out, "-perfBackend", "il2cpp", "-perfDev", "0", "-perfMode", "size", "-perfNoVectra", "1" if no_vectra else "0"], log)
        info = json.load(open(out + ".buildinfo.json")) if os.path.exists(out + ".buildinfo.json") else {}
        data = os.path.dirname(out)
        results[name] = {
            "folder_bytes": folder_size(data),
            "game_assembly_bytes": os.path.getsize(os.path.join(data, "GameAssembly.dll")) if os.path.exists(os.path.join(data, "GameAssembly.dll")) else -1,
            "unity_player_bytes": os.path.getsize(os.path.join(data, "UnityPlayer.dll")) if os.path.exists(os.path.join(data, "UnityPlayer.dll")) else -1,
            "data_folder_bytes": folder_size(os.path.join(data, "Size_Data")),
            "report_total": info.get("totalSize", -1),
            "build_seconds": info.get("totalSeconds", -1),
            "largest": info.get("largest", [])[:40],
            "vectra_shaders": shader_variants(log),
        }
    json.dump(results, open(os.path.join(WORK, "Runs", args.tag, "size.json"), "w"), indent=1)
    say(json.dumps({k: {a: b for a, b in v.items() if a not in ("largest",)} for k, v in results.items()}, indent=1))


# ------------------------------------------------------------------ aggregation and report

def load_runs():
    """{(backend, kind): {(group, side): [result per round]}}"""
    base = os.path.join(WORK, "Runs", args.tag)
    data = {}
    if not os.path.isdir(base):
        return data
    for entry in sorted(os.listdir(base)):
        folder = os.path.join(base, entry)
        if not os.path.isdir(folder) or "_" not in entry:
            continue
        backend, kind = entry.split("_", 1)
        for f in sorted(os.listdir(folder)):
            if not f.endswith(".json"):
                continue
            group, side, r = f[:-5].split("_")
            res = json.load(open(os.path.join(folder, f), encoding="utf-8"))
            data.setdefault((backend, kind), {}).setdefault((group, side), []).append(res)
    return data


def collect(results):
    """{row id: {metric: [values]}} over a list of results of one group and side."""
    rows = {}
    for res in results:
        for row in res["rows"]:
            slot = rows.setdefault(row["id"], {"_note": row.get("note", "")})
            for m in row["m"]:
                slot.setdefault(m["k"], []).append(m["v"])
    return rows


def med(v):
    return statistics.median(v) if v else None


def spread(v):
    return max(v) - min(v) if v else None


def command_report():
    import report
    report.build(load_runs(), os.path.join(WORK, "Runs", args.tag), WORK, GROUPS)


def command_profile():
    import profile_run
    profile_run.run(args, WORK, build, run_player, sync_harness, unity)


if __name__ == "__main__":
    sys.path.insert(0, HERE)
    os.makedirs(os.path.join(WORK, "Runs", args.tag), exist_ok=True)
    if args.command == "sync":
        sync_harness(args.sandbox, bool(args.vg))
    elif args.command == "build":
        for b in args.backends.split(","):
            for k in args.kinds.split(","):
                say("built", build(b, k))
    elif args.command == "run":
        command_run()
    elif args.command == "import":
        command_import()
    elif args.command == "size":
        command_size()
    elif args.command == "profile":
        command_profile()
    elif args.command == "report":
        command_report()
    elif args.command == "all":
        args.kinds = "rel,dev"
        command_run()
        command_import()
        command_size()
        command_report()
