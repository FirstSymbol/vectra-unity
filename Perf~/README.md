# Vectra performance harness

Measures what Vectra costs: CPU and GPU time, allocation, memory, build size and import time, beside a reference where one exists
(plain uGUI `Image`, `SpriteRenderer`, a UI Toolkit `VisualElement`, and Unity's `com.unity.vectorgraphics`).
Unity does not import `~` folders: the runner copies the sources into a sandbox project.

## One command

    python -I Perf~/perf.py all --tag baseline

builds the players (IL2CPP and Mono, release and development), runs every row 5 times, imports the corpus 6 times in a batch editor, builds the size probes and writes
`<work>/Runs/<tag>/tables.md` and `summary.json`. Every step is resumable (a run with its JSON on disk is skipped; `--fresh` starts over). Individual steps:

    perf.py build   --backends il2cpp,mono --kinds rel,dev
    perf.py run     --backends il2cpp --kinds rel --rounds 5 [--groups canvas,svg] [--sides vectra]
    perf.py import                       # I1
    perf.py size                         # build size above an empty URP uGUI project
    perf.py profile --group canvas --sides vectra,ref   # profiler capture of a development player and the top samples per row
    perf.py report

Defaults are the paths of this machine (editor, sandbox, work folder, `hidden_run.py`, corpus folders); each is an option or a `VPERF_*` environment variable (`perf.py -h`).

## What a sandbox needs

A Unity 6000.x project with URP, uGUI, the package under test (`com.firstsymbol.vectra`, a `file:` path to the worktree) and, for the reference rows,
`com.unity.vectorgraphics` (3.0.0-preview.7 installs on 6000.4). Run with `--vg 0` when it is absent. Linear colour space for the owner's configuration;
a Gamma sandbox (a copy with the colour space switched) gives row V8's Gamma figure.
The harness assemblies carry the define constraint `VECTRA_PERF`; only the harness builds set it (`BuildPlayerOptions.extraScriptingDefines`), so the sandbox's editor and tests never compile them.

## Rules the harness follows

* Timing figures: Windows x64 **release** players, IL2CPP (Mono beside it); counters (batches, SetPass calls, draw calls, triangles) from **development** players.
* Players start through `hidden_run.py` (a copy of the studio tool in this folder, with normal priority instead of Idle): a desktop of its own, no window on the user's screen. The runner waits for CPU load under 25% before each launch and notes battery. Builds and editors run at Idle priority.
* One process per group of rows, the sides alternating in order from round to round. Median of 5; the spread (max - min) is printed beside it; a difference inside the spread is a tie.
* Frames: vSync off, no frame cap. Per frame the harness records the wall time (frame start to next frame start), the span from the row's action to the end of the same frame (the main-thread work),
  managed allocation on the main thread (`GC.GetAllocatedBytesForCurrentThread`), and the `FrameTimingManager` GPU, main-thread and render-thread times.
  Frame 0 of a row is reported apart (`first_*`): it is where creation and first-use work lands.
* Memory: process private bytes (Win32), the managed heap, and the sum of `Profiler.GetRuntimeMemorySizeLong` over every live mesh, texture and render texture (meshes report 0 bytes in release players: read mesh megabytes from the development runs).
* Load: before each launch the runner waits (up to 30 minutes) for the machine's CPU load to fall under 25%, and samples the load during the run (`load_during` in the result JSON, which includes the player itself). The machine is shared with other builds: a run with a high load is discarded and repeated.
* The development player's on-screen console is switched off (`Debug.developerConsoleVisible = false`): with error lines in it the console re-lays out every frame and allocates.
* A row never changes what it measures once published; a new behaviour is a new row.

## Rows

| id | what | group |
|---|---|---|
| V1 | create 1000 shapes (rounded rect, ellipse, star, dashed stroke, gradient) + first frame | canvas |
| V1n | V1 without the gradient shape, in a process of its own (isolates what the gradient atlas costs the first frame) | v1n |
| V2 | size of the 1000 changes every frame (full rebuild) | canvas |
| V3 | colour of 100 changes every frame | canvas |
| V4 | a frame of 1000 unchanging shapes (CPU, GPU, batches, SetPass) | canvas |
| V5 | 200 shapes move, rotate and pulse every frame | canvas |
| V6a-c | 100 shapes with a drop shadow, an outer glow, a layer blur | fx |
| V7a-c | 200 shapes under a stencil mask, a soft mask, a RectMask2D | fx |
| V8 | 300 `VectraSvgImage` icons, Browser and Linear colour blending; pictures memory | svg |
| V9 | a scroll list of 500 icons moved every frame | svg |
| V10a-c | boolean operations: exact, smooth, `VectraBooleanGroup` (time, allocation) | bool |
| V11 | the parent of 1000 shapes deactivated and reactivated | canvas |
| W1 | 5000 `VectraRenderer` in world space (frame, SRP Batcher counters) | world |
| U1, U1b | 1000 UI Toolkit `VectraElement`s: create, frame, resize | uitk |
| S1 | sprite generation for the Figma corpus (`VectraSpriteBaker`), and 100 baked shapes | sprite |
| I1 | editor import of the Figma corpus (total, worst file) | `perf.py import` |
| I2 | `VectraSvg.Parse` per document (time, allocation) | parse |
| I3 | first-use cost of each feature (shader variants, materials, type init) | cold |
| MEM_* | memory with the content on screen and after it is destroyed | canvas, world, uitk |
| size | IL2CPP player above an empty URP uGUI project; shader variant lines of the build log | `perf.py size` |

References per row: uGUI `Image` with a sliced sprite (V1-V7, V11, U1 as `VisualElement`, W1 as `SpriteRenderer`); `com.unity.vectorgraphics` for V8, V9, S1, I1, I2, I3.
The corpus is the 42 Figma SVGs of the 0.7 parity work (`--corpus`); the package's own copy of 19 lives in `Tests/FigmaCorpus~`.

## Files

* `perf.py` build, run, import, size, report; `report.py` and `curated.py` aggregation and the curated tables; `profile_run.py` profiler captures; `hidden_run.py` the hidden-desktop launcher.
* `Player/` rows and helpers (runtime, assembly `FirstSymbol.VectraPerf`); `Player.Vg/` the bridge to `com.unity.vectorgraphics`.
* `Editor/Build/` player build and the profile analyzer; `Editor/Import/` the importer timing.
* `results/baseline-0.7.5-curated.md` the curated tables of the 0.7.5 baseline (every metric of every row is in `tables.md` next to the runs, `<work>/Runs/<tag>/`).
