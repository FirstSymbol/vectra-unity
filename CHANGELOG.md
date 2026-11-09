# Changelog

All notable changes to this package are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- SVG markers: `marker-start`, `marker-mid`, `marker-end` and the `marker` shorthand on `path`, `line`, `polyline` and `polygon`, with `orient` (`auto`, `auto-start-reverse`, an angle), `markerUnits`, `refX` / `refY`, `viewBox`, `markerWidth` / `markerHeight` and `overflow`. They replace the 0.2.3 import-report line.
- Text in world space: `VectraSvgRenderer` draws SVG text through the text backends (Lettera World Text, TextMesh Pro 3D) and has the same text engine, font map and fallback fonts as `VectraSvgImage`. `IVectraTextBackend` gets `SupportsWorld` and `BuildWorld`; `VectraTextBackends.Resolve` takes `world`. `VectraSvgRenderer.SortingOrder`, `Height`, `GenerateText`, `TextEngine`, `RebuildNow()`.
- `Documentation~/Platforms.md` (and `.ru.md`): what was built, run and seen on each platform, as in Lettera's page.
- English `README.md`; the Russian one is `README.ru.md`.
- Tests for all of the above (EditMode 266 / PlayMode 27 in the plain project; 279 / 27 with Lettera; 280 / 27 with TextMesh Pro; 293 / 27 with both; none fail): gradient atlas collisions, eviction order and overflow with timings, markers, paint order in pixels, children kept on resize, cache keys, world text by pixels in every engine, the SVG importer, and a player check of every sample on seven platforms (see Platforms).
- Tests that keep the guides true: every C# block in the guides compiles, and every English guide has a Russian twin with the same headings, code blocks, tables and links.
- Gradient atlas diagnostics: `VectraGradientAtlas.LiveRows`, `ColdRows`, `EvictionCount`, `BakeCount`, `OverflowCount`.

### Changed
- `VectraSvgImage` no longer destroys and recreates its text and image children when its rect changes; it moves them. The children are rebuilt only when the document, the engine or the fonts change.
- Gradient atlas: rows are shared by the content that is baked (linear and radial paints with the same stops share a row), and a row nobody uses stays baked until the space is needed.
- API: `IVectraTextBackend` has two new members (`SupportsWorld`, `BuildWorld`). A backend written for 0.3 must add them (`false` and an empty method if it cannot draw without a Canvas).

### Fixed
- Gradient atlas: two paints whose 32-bit hashes collided shared one row and drew with the wrong colours. Rows are compared by every input of the bake now.
- Gradient atlas: it could not recover when full (4096 rows) and gave out a bad row. Unused rows are evicted least recently used first; when every row is held by a live paint the request gets a reserved white row and the console warns once.
- SVG text and images were drawn above the whole document, so a shape that follows a text in the file was drawn below it. Paint order is kept (see Added in the guide: runs of shapes after a text are children of the image, or sorted meshes in world space).
- A stale outline: the SVG element cache was keyed by element index and document instance, so a document parsed again into the same instance, or a path edited in place, kept the old geometry; two clip paths with as many points as each other were mixed up. The keys hash the path and the clip content now.
- A `<use>` restyled its target for good: an element drawn where it stands after a `<use>` of it took the `<use>`'s inherited style. The target gets its own style back.
- A group's `opacity` is applied to each child (not to the group as a whole); the import report now says so.

### Limits
- Text and images are not clipped by `clip-path`; images are axis-aligned rectangles and ignore `preserveAspectRatio`; embedded images are not drawn in world space.
- Group opacity is not composited as a layer.
- World-space text needs Lettera or TextMesh Pro; the legacy uGUI `Text` cannot draw without a Canvas, so the text is skipped (one console line).
- The gradient atlas holds 4095 different live gradients; the rest draw white.

## [0.3.0] - 2026-11-09

### Added
- Text engines. SVG `<text>` is drawn through a backend seam in the core (`IVectraTextBackend`, `VectraTextBackends`), with no reference to any text package. Backends: `FirstSymbol.Vectra.Lettera` (turns on by itself when `com.firstsymbol.lettera` 1.0.0 or newer is installed), `FirstSymbol.Vectra.TextMeshPro` (the old behaviour), and a built-in legacy uGUI `Text` fallback that needs nothing.
- Project setting **Text engine**: Auto, Lettera, TextMesh Pro, Legacy or None. Auto takes Lettera when installed, else TextMesh Pro when a font resolves, else Legacy. The page shows what the project has and what Auto picks. The choice reaches player builds through `VectraRuntimeSettings`, which now also holds the engine (`TextEngine`, `ProjectTextEngine`).
- Per-component override: `VectraSvgImage.TextEngine` (Project Setting, Auto, Lettera, TextMesh Pro, Legacy, None), plus a fallback font for each engine (`FallbackFont`, `LetteraFallbackFont`) and `RebuildGenerated()`.
- A named engine that is missing or has no font falls back along the Auto order; the console says so once per document.
- `<tspan>` styling survives: the text of a `<text>` is split into runs (`SvgElementData.runs`) and bold, italic, colour and size of a run become markup inside one text in every engine.
- `VectraFontMap` has Lettera fields (`lettera`, `letteraBold`, `letteraFallback`: a Lettera font asset or font stack); the inspector takes only the right type per engine and an edit clears a font of the wrong type.
- `unity command vectra svg_create` and `svg_parse` take `--text_engine`; `stats` reports the engine in use.
- The CLI logic lives in `FirstSymbol.Vectra.Editor.CliCore`, which needs no `com.unity.pipeline` and is covered by tests; `FirstSymbol.Vectra.Editor.Cli` only holds command names and arguments.

### Upgrade notes (from 0.2.x)
- **SVG text moves down.** In 0.2 TextMesh Pro SVG text sat about half a font size too high. It now lands on the SVG's `y`, so a project that keeps TextMesh Pro (the upgrade pins it) still sees its SVG text shift down by about 0.5 em.
- **API break: `VectraFontMap.Resolve` returns `UnityEngine.Object`, not `TMP_FontAsset`.** `VectraFontMap.fallback`, `Entry.regular`, `Entry.bold` and `VectraSvgImage` fallback font are `Object` too. Cast to `TMP_FontAsset` in code that used them. Saved assets are unchanged. `VectraSvgImage.ConfigureText(TextMeshProUGUI, ...)` is gone.
- The text engine is pinned to TextMesh Pro once (console line "Vectra 0.3: ..."); choose Auto or Lettera in Project Settings > Vectra to change it.

### Changed
- Upgrading from 0.2: a project that already used Vectra (a Vectra settings file, a font map, or an SVG with text) gets **TextMesh Pro** written into its settings once, with a console line, so its SVG text does not change. New projects start on Auto.
- `VectraFontMap` fields and `VectraSvgImage` fallback font are plain object references now instead of `TMP_FontAsset`, so the core does not depend on TextMesh Pro. Saved data is unchanged; `Resolve` returns `Object`.
- Whitespace between a `<text>` and its `<tspan>` children follows SVG: `A<tspan>B</tspan>` reads "AB", not "A B".
- The core assembly no longer references `Unity.TextMeshPro`.

### Fixed
- The text engine chosen in the project settings is in force from the first frame of play mode, and after the 0.2 upgrade pins TextMesh Pro the open SVG images rebuild their text at once (before, an image could be built on Auto, which meant Lettera when installed).
- SVG text drawn with TextMesh Pro sat about half a font size too high (the baseline was put on the middle of the text box, not at its bottom) and turned about the wrong point when rotated. The baseline now lands on the SVG's `y`, in every engine; a render test checks the pixels. Projects that keep TextMesh Pro see their SVG text move down by that amount.

### Limits
- Per-`<tspan>` positions (`x`, `y`, `dx`, `dy`), `textPath`, `letter-spacing` and text decoration are not drawn; a tspan with another `font-family` uses the font of the whole text.
- World-space `VectraSvgRenderer` still draws no text (planned with Lettera world text and TextMesh Pro 3D).
- Lettera finds fonts only through the font map or the component's fallback; resolving system fonts by CSS family name needs a Lettera API that does not exist yet.
- The legacy engine ignores font families and uses the built-in font.

## [0.2.3] - 2026-11-09

### Added
- `SvgParseOptions` and `SvgParser.Parse(text, options)`: budgets for untrusted SVG (nesting depth, element count, elements drawn, `<use>` copies, path data length, file size) and the language list used by `<switch>`.
- `VectraRuntimeSettings`: the distance atlas size now travels to player builds.

### Fixed
- The distance atlas uploaded the whole texture whenever one shape changed (8 MiB at the default 2048). It now uploads only the changed blocks: for one changed shape 12 780 texels instead of 4 194 304, and 0.19 ms instead of 3.7 ms of editor CPU time per flush.
- The distance atlas size set in Project Settings applied only in the editor; players always used 2048. A build with another size now adds a `VectraRuntimeSettings` asset to the Preloaded Assets for the duration of the build; with the default size nothing is added.
- Boolean shapes allocated managed memory on every rebuild (a list and an array per call). They allocate nothing after warm-up. Nested boolean shapes threw `ObjectDisposedException`, and a boolean that contained itself overflowed the stack; both work now (a cycle is cut at the repeated shape).
- A hostile SVG could crash or hang the parser: 3000 nested groups overflowed the stack, and a 1 KB file of nested `<use>` elements produced 100 000 elements. The parser now stops at budgets and adds a warning starting with "SVG limit reached" to the import report; a `<use>` that references its own ancestor is skipped.
- `<switch>` drew every child. It draws the first child whose `systemLanguage` and `requiredExtensions` pass and reports the rest.
- `<marker>` was dropped without a word. Markers are still not drawn, but an element that uses `marker-start`, `marker-mid` or `marker-end` now adds a line to the import report.
- A world-space `VectraRenderer` hit test counted half the width of an Outside stroke; it counts the whole width, as `VectraShape` does.
- The SVG importer made a zero-width texture when `Texture Size` was 0.
- `VectraSvgImage` and `VectraSvgRenderer` destroyed their mesh with `DestroyImmediate` in play mode.
- The `Rebuild_IsAllocationFree` test counted nothing: `GC.GetAllocatedBytesForCurrentThread` reports 0 on Unity's Mono. It now reads the profiler's GC counters.

## [0.2.2] - 2026-11-08

### Changed
- Unity 6.3 LTS (6000.3) or newer: 6.0 LTS support has ended, and the package is built and tested on 6000.3 and 6000.4.

### Added
- MIT licence: `LICENSE.md`, and `license` and `licensesUrl` in `package.json`.

### Fixed
- A saved `VectraStyle` came back as a missing script: Unity binds a script to its file by name, and the class lived in `VectraShapeContent.cs`. `VectraStyle`, `VectraBooleanOperand` and `VectraSvgRenderer` now each have a file of their own, with fixed GUIDs.
- Assets, prefabs and scenes already saved with a lost script are relinked once when the package loads, without a prompt, and every repaired file is logged. `Tools > Vectra > Repair Lost Script References` runs it again. Missing scripts of other packages are left alone, and so are open scenes with unsaved changes.
- The package declares `com.unity.modules.physics2d`: `VectraRenderer` builds a `PolygonCollider2D`, and a project without the 2D physics module failed to compile.

## [0.2.1] - 2026-10-30

### Fixed
- Player builds failed to compile: `SpriteTracing` used the editor-only `Texture.imageContentsHash`. It now reads that hash in the editor only; in a player, call `ClearCache` after changing a sprite's pixels at runtime.

## [0.2.0] - 2026-10-30

### Added
- `VectraMask`: stencil mask that follows the exact outline, and a soft mode (feather, inset, invert) evaluated per
  pixel for child Vectra graphics.
- `VectraPalette` and `VectraTheme`: named colours per theme; paints reference them through colour tokens.
- Animation: `VectraTween` fluent tweens (trim, colours, opacity, width, dash offset, rotation, fill amount,
  dissolve, morph, generic floats; eases, delays, loops, yoyo) and `VectraAnimatedProperties` for Animator and
  Timeline.
- Effects: pixelate, outline, colour overlay, gradient overlay. Gradients are dithered.
- SVG: `VectraSvg.Parse / Load / Download / Expand` (document to an editable `VectraShape` hierarchy, clip paths kept)
  and `VectraFontMap` for `font-family` to TextMeshPro fonts.
- Editor: Scene view overlay (point modes, snapping, handle toggles), drop shadow handle, drag-to-reorder cards,
  palette picker, Copy/Paste Vectra Style, Export as Sprite Asset, Expand to Hierarchy, Atlas Debugger and SVG Import
  Report windows, inspectors for the new components, default font map setting.
- `VectraShapeAsset`: shared geometry (shape library) with Save/Detach in the Shape tab and its own inspector.
- `TracedSpriteShape` / `VectraSpriteTracer`: sprite silhouettes as contours (alpha threshold, resolution, simplification, smoothing).
- `VectraPathConverter` and Convert to editable path for any shape.
- `ContourGradient` paint (gradients and bands that follow the outline), `ExtrudeEffect`, `DeformEffect` (GPU-animated).
- `VectraInteractionStates`: hover, pressed, selected and disabled looks with eased transitions.
- `VectraCustomEffect` / `VectraEffectBuilder`: user-defined effects through the standard pipeline.
- Optional `FirstSymbol.Vectra.Editor.Cli` assembly (needs com.unity.pipeline): `unity command vectra help, types, list, get, create, set, add, remove, mask, states, convert_to_path, save_style, save_shape, palette, theme, svg_parse, svg_create, select, export_png, export_svg, expand_svg, use_importer, stats, reset_atlases` with JSON input and output.
- Agent guide (`Documentation~/AgentGuide.md`) and Tools → Vectra → Install Claude Code Skill.
- Play mode tests: rendered pixels, masks, palettes, batching, garbage-free rebuilds, tweens, SVG expansion.
- Samples: Effects & Masks, Boolean Operations, Animation, World Space, URP Backdrop.
- Documentation in English and Russian (`Documentation~`).

### Changed
- `VectraBackdropFeature` captures before transparent objects into persistent targets (default event
  `BeforeRenderingTransparents`). Use a camera stack to blur other UI.

### Fixed
- Backdrop shapes sampled a capture taken after them (stale or released), which darkened blend modes and glass.
- `VectraBooleanGroup` allocated every frame while checking its children.
- FXC warning about a potentially uninitialized variable in the shape distance function.

## [0.1.0] - 2026-10-30

### Added
- Burst geometry kernel: adaptive flattening, scanline trapezoid tessellator (all fill rules, self-intersections,
  N-operand boolean operations, seamless shared edges), stroker (joins, caps, dashes, trim, width profile),
  signed distance field baker, contour reconstruction.
- `VectraShape` (uGUI) and `VectraRenderer` (world) with a single batchable material for UI and world shaders.
- 19 analytic SDF shapes and 9 path generators, boolean shapes (exact and smooth) and morphing.
- Paints: solid (HDR), linear/radial/focal/angular/diamond gradients, textures, procedural patterns, noise and
  freeform gradients; shared gradient atlas.
- Strokes with alignment, caps, joins, dashes, trim path and width profiles.
- Effects: drop/inner shadow, outer/inner glow, bevel, layer blur, grain, color adjust, fill amount, dissolve.
- SVG: runtime parser (CSS, gradients, clip paths, use/symbol, filters, text, images), `VectraSvgDocument`,
  `VectraSvgImage`, `VectraSvgRenderer`, override importer with Document/Sprite/Texture output, SVG writer.
- `VectraBooleanGroup`, `VectraStyle`, GPU rasterizer, outlines for colliders, precise raycasts.
- Editor: UI Toolkit inspector, gradient editor, paint drawer, Scene view editing tool, GameObject menus,
  project settings.
- Optional URP module: `VectraBackdropFeature` and `VectraBackdrop` (frosted glass, 15 advanced blend modes).
- Samples: Shapes Gallery, SVG Showcase. EditMode test suites for geometry, rendering and SVG.
