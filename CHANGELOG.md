# Changelog

All notable changes to this package are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and this project adheres to [Semantic Versioning](https://semver.org/).

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
