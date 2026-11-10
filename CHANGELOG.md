# Changelog

All notable changes to this package are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and this project adheres to [Semantic Versioning](https://semver.org/).

## [0.7.2] - 2026-11-11

### Fixed
- SVG pictures (composited layers, and whole documents in Linear projects with SVG colour blending set to sRGB) were blurry whenever something magnified them that the Canvas scale factor did not know about: a Scene view zoomed in on a Canvas, a Screen Space - Camera canvas seen by another camera. The picture density now follows the pixels on screen: the Game view's Canvas scale factor or camera, and the editor's Scene views (the closest camera wins; `VectraScreenDensity`). World-space pictures also follow the Scene views. A picture is drawn again when the density changes by more than 3 % (4 % in world space, 5 % in UI Toolkit), looked at ten times a second, so zooming does not redraw every frame; idle frames cost nothing and allocate nothing. Documents with non-scaling strokes keep the Game view density, because their stroke width is measured in its pixels. Pictures are still capped at 4096 px a side (2048 in world space).

## [0.7.1] - 2026-11-11

SVG documents look like Figma and browsers in Linear colour space projects. With 0.7.0 only 28 of the 42 Figma frames matched in Linear (translucent shapes, shadows, gradients and group opacity were paler); with 0.7.1 the same 39 of 42 match as in Gamma, on the rasterizer and on a Canvas.

### Changed
- **SVG colour blending** (Project Settings > FirstSymbol > Vectra > Rendering, default **Browsers and Figma (sRGB)**). In a Linear project an SVG document is drawn into a texture in sRGB space (colours, gradient stops and alpha blending as in a Gamma project; layers composite inside it) and the finished picture is shown once on a Canvas, in UI Toolkit and in world space, and is what sprites and PNGs contain. **Linear (Unity)** keeps the old behaviour. `VectraSvgImage`, `VectraSvgRenderer` and `VectraSvgElement` have a `ColorBlending` override. Gamma projects are not affected; native Vectra shapes are not changed.
- Texts and embedded `<image>` elements stay separate objects above the picture (not blended in sRGB space with it).
- New API: `SvgColorBlending`, `SvgColorBlendingChoice`, `VectraColorSpace`, `VectraRuntimeSettings.SvgColorBlending`, `SvgDrawOrder.Split(..., gamma)`.

### Fixed
- Linear colour space: group opacity, masks, blend modes, background blur, shadows, translucent fills over fills, strokes with opacity and gradients no longer look paler than in Figma.
- A Canvas picture was not redrawn when only the non-scaling-stroke scale (Canvas scale factor) changed.

## [0.7.0] - 2026-11-10

Figma parity: SVGs exported from Figma render like Figma. Checked on a corpus of 42 Figma frames (shadows, blurs, every gradient type, image fills, blend modes, group opacity, masks, live and outlined text, strokes, booleans, noise), each compared with Figma's own PNG: 39 of 42 match (under 2 % of pixels off) in Gamma colour space (in Linear colour space 28 of 42 matched; that is fixed in 0.7.1). The other three: the two live-text frames match on a Canvas, but sprites and PNGs draw no text; noise is close in density and colour but not pixel-exact.

### Added
- **Layers: group opacity, masks, blend modes, background blur.** SVG parts that need a picture of a subtree are composited offscreen instead of being approximated per shape:
  - `opacity` on a `<g>`, `<svg>` or `<use>` with several shapes, and on a shape with both a fill and a stroke, applies to the group as one (no see-through overlaps). A single shape with only a fill or a stroke still gets the opacity in its paint.
  - `<mask>`: luminance (default) and alpha (`mask-type`, also as `style="mask-type:alpha"`), the `maskUnits="userSpaceOnUse"` region, any mask content (gradients, opacity). A mask of plain opaque white shapes stays a geometric clip, so Figma's inside and outside stroke masks stay exact and cost nothing.
  - `mix-blend-mode`: all 16 CSS modes (multiply, screen, overlay, darken, lighten, color-dodge, color-burn, hard-light, soft-light, difference, exclusion, hue, saturation, color, luminosity) plus plus-lighter. The backdrop is the SVG's own content below the element, not the scene behind the component.
  - Figma's background blur (`<foreignObject>` with `backdrop-filter`, then a shape with `data-figma-bg-blur-radius`): the SVG's own content below is blurred inside the shape.
  - Works on a Canvas (`VectraSvgImage`, child `VectraSvgComposite`), in UI Toolkit (`VectraSvgElement`), in world space (`VectraSvgRenderer`, Built-in and URP), in baked sprites, in the importer's PNG and in `VectraRasterizer.Render(doc, ...)`. Layer textures are drawn again only when the size, the pixel density, the overrides or the document's content (`VectraSvgDocument.contentVersion`) change.
  - In Linear colour space the layer maths (opacity, masks, blend modes, background blur) runs on sRGB values, as in Figma and browsers; ordinary translucency and anti-aliasing still blend in linear light, as all Unity UI does there, so soft edges look slightly lighter than in Figma.
  - New API: `VectraSvgDocument.layers` / `maskElements`, `SvgLayerData`, `SvgBlendMode`, `SvgMaskKind`, `SvgLayers`, `SvgCompositor`, `SvgDrawItem.composite`, `VectraRasterizer.Render(doc, w, h, quality, overrides)`.
- **Figma's filters.** Drop shadows (with spread), inner shadows (with choke), layer blur and noise are recognised from the filter chains Figma exports (`feColorMatrix`, `feMorphology`, `feOffset`, `feGaussianBlur`, `feComposite`, `feBlend`, `feTurbulence`) and become Vectra's native effects; several shadows stack in order. Shadows of an element without a fill follow its stroke. Shadow blend modes: screen and plus-lighter (exact for an inner shadow over a solid fill); other modes are drawn normal and reported.
- **Angular and diamond gradients from Figma.** `data-figma-gradient-fill` and `data-figma-gradient-stroke` become native angular and diamond paints; Figma's fallback groups (`data-figma-skip-parse`) are skipped.
- **`<pattern>` fills and strokes**, Figma image fills included (fill and tile modes; cropped, fitted and rotated images through `preserveAspectRatio` and transforms). A pattern that is one picture filling its tile uses the picture itself; other content (shapes, gradients, nested patterns) is drawn into a tile texture the first time the document is built. `patternUnits`, `patternContentUnits`, `viewBox`, `patternTransform`, `href` inheritance. Documents keep the image bytes, never texture objects; textures are made on first use and destroyed with the document. A document whose patterns use different textures is drawn as several meshes.
- **SVG text as Figma exports it** ("Outline text" off): a `<text>` is cut into pieces at every absolute `x` or `y` (one `<tspan x y>` per line, per-glyph `x` / `y` lists, up to 512 pieces), and each piece is a text object at its own baseline and `text-anchor`, with every text engine, on a Canvas, in world space, in UI Toolkit and in `VectraSvg.Expand`. `dx` / `dy`, `letter-spacing` (`em`, `px`, number), `text-decoration` underline and line-through (Lettera, TextMesh Pro and UI Toolkit; the legacy uGUI Text ignores spacing and decoration), `xml:space="preserve"` and `white-space: pre`. New fields on `SvgTextRun`: `letterSpacing`, `underline`, `lineThrough`, `dx`, `dy`.
- `GrainEffect` monotone mode (colour, density, size in local units) for noise like Figma's.
- Tests: 117 new EditMode and 38 new PlayMode tests (layers on the rasterizer, Canvas, world and UI Toolkit; Figma filter, gradient, pattern and text corpus files; text line positions with Lettera; Linear colour space).

### Changed
- An element whose filter has a primitive Vectra cannot place is drawn without that primitive (and reported); before, the whole element disappeared.
- Support table and README limits: `<mask>`, `<g>` opacity, `<pattern>`, `<image>`, `<text>`, `<tspan>`, `<foreignObject>`, the filter primitives above, `mix-blend-mode`, `backdrop-filter`, `letter-spacing`, `text-decoration` updated (EN and RU).

### Fixed
- Shadow offsets of an SVG expanded onto a Canvas (`VectraSvg.Expand`) pointed up instead of down, and inner shadows were not scaled with the document.
- A `VectraSvgImage` added by script before it was parented under a Canvas (`new GameObject(..., typeof(VectraSvgImage))`, then `SetParent`) drew garbage: the Canvas vertex channels it needs were not switched on.
- Stale textured world materials are destroyed when their texture is gone.

### Limits
- `VectraMask` soft masks do not apply to the composited parts of a Canvas image; composited parts in world space do not draw under HDRP; "Expand into objects" ignores layers.
- Text and images inside a layer are drawn above the layer's picture, with the group's opacity but without its mask or blend mode.
- World-space layer textures are at most 2048 px a side (scaled down beyond that). Embedded PNG/JPEG pictures over 4096 px a side or 16 megapixels are not decoded; the import report says so.
- Masks inside masks and `maskContentUnits="objectBoundingBox"` stay geometric clips. A document may have 256 layers, nested 8 deep.
- Noise is close to Figma's in density and colour, but its specks are square cells.
- Sprites and PNGs draw no text (as before).

## [0.6.2] - 2026-11-10

### Added
- **Non-scaling strokes in UI Toolkit, in baked sprites and PNG, and in the SVG export.** In `VectraSvgElement` one SVG px of a `vector-effect="non-scaling-stroke"` stroke (and of its dashes) is one panel pixel, so it is `stroke-width` x the panel's density in device pixels. In `VectraSpriteBaker`, `VectraRasterizer` and the importer's Sprite mode it is one pixel of the baked texture. A pixel-mode stroke is written to SVG as `vector-effect="non-scaling-stroke"` and reads back as the same stroke.
- **SVG support table** in the guide (English and Russian): every SVG 1.1 and SVG 2 element, each filter primitive, key properties, text, animation, conditional processing, CSS selectors, at-rules and units, each marked Supported, Partial (what exactly) or Not supported. The README's SVG limits link to it. `SvgSupport` (public) is the list behind it; a test keeps the table, the list and the parser equal.
- **The import report names what was silent before:** every unsupported element (animation, `<script>`, `<foreignObject>`, SVG fonts, filter primitives such as `feTurbulence`, unknown elements; elements with a foreign namespace prefix such as `sodipodi:` are skipped quietly), `paint-order`, `mix-blend-mode`, `isolation`, text spacing and baseline properties, `textLength`, per-glyph `dx` / `dy` / `rotate`, `<textPath>`, the CSS `transform` property, `var()`, `calc()`, `clip-path` / `filter` values that are not `url(#id)`, `context-fill`, the focal radius `fr`, `<text>` inside a clip path, `preserveAspectRatio` on an image, a `<use>` of another file or of a missing id, and in CSS the selectors that never match (pseudo-classes, `+`, `~`, `[a^=b]`), `@media` (applied without its condition), `@import`, `@font-face`, `@keyframes`.
- Tests: 36 new EditMode (the non-scaling stroke baked at four sizes, dashes, export round trip; the support table in both languages; a report entry for every unsupported element, silent gaps and CSS) and 1 new PlayMode (UI Toolkit at panel scales 1 and 2).

### Fixed
- A CSS `@import url(...);` (or `@charset`, `@namespace`) in an SVG's `<style>` swallowed the rule that followed it.

## [0.6.1] - 2026-11-10

### Added
- **`vector-effect="non-scaling-stroke"` works as in browsers on a Canvas (`VectraSvgImage`) and in world space (`VectraSvgRenderer`).** The stroke keeps its authored width on screen whatever transforms apply (the element's own `transform`, the `viewBox` fit, the Canvas, the object's scale, the camera), and so do its dashes (`stroke-dasharray`, `stroke-dashoffset`). Before, only the element's own scale was skipped and the document's scale still grew or shrank the stroke. The parser makes such a stroke a pixel-mode stroke (`StrokeWidthMode.Pixels` plus the new `StrokeLayer.dashInPixels`). One SVG px is one reference point on a screen-space Canvas (`stroke-width="2"` is 2 x the Canvas scale factor device pixels; one device pixel on a world-space Canvas) and one device pixel in world space. `StrokeWidthMode.Pixels` on an ordinary shape still counts device pixels. The mesh follows the screen scale (1% rebuilds it). `VectraSvgBuilder.Build` takes the new `pointScale` argument and `ShapeFrame` the field of the same name. `non-scaling-size`, `non-rotation` and `fixed-position` are ignored and named in the import report.
- Tests: 5 EditMode (parsing: attribute, style, class, transforms, dashes, `<use>`, other values) and 5 PlayMode (world at image sizes 50 to 400 px, camera zoom and object scale, dashes, group and `<use>`, Canvas at scale factors 1 and 2) compared with a normal stroke that scales.
- README: the SVG limits list names the main gaps (SMIL animation, `<foreignObject>`, `<script>`, other filter primitives, `mix-blend-mode`, `paint-order`, `@font-face`, `<textPath>` and glyph positions, `letter-spacing`, mask luminance, patterns, external images).

### Fixed
- `vector-effect` was treated as an inherited property, so a `<g vector-effect="non-scaling-stroke">` changed its children. It is not inherited (SVG 2; Chrome agrees): the element itself, its `style` or a CSS rule sets it, and a `<use>` of a marked element is non-scaling.

### Limits
- With a non-uniform fit (`preserveAspectRatio="none"` into a box of another aspect ratio) the stretched mesh makes a non-scaling stroke a little thicker along one axis than the other; a browser keeps it even.

## [0.6.0] - 2026-11-10

### Added
- **World shapes batch under URP and HDRP.** `Vectra/World URP` (with a 2D Renderer pass) and `Vectra/World HDRP` keep every property in `UnityPerMaterial` and the per-shape data in the vertices, so the SRP Batcher takes all `VectraRenderer` shapes (Unity's own check reports the old shader as incompatible because of `_Color`) that share a material in one batch. A shape no longer sets a `MaterialPropertyBlock`: a shape with a texture paint uses a material of its own per texture, shared by all shapes with that texture (`VectraMaterials.GetWorld(mode, texture)`). The Built-in pipeline keeps `Vectra/World`. Shapes (and `VectraSvgRenderer`) switch shader when the pipeline changes.
- **Shader Graph (URP and HDRP).** `VectraShaderGraph.hlsl` (Custom Function code) and five Sub Graphs under *Vectra*: SDF Shape, Coverage, Outline, Pixel Size, Paint; the new *Shader Graph* sample has *Vectra Card*, an unlit transparent graph for both pipelines. `VectraSdf.hlsl` holds the distance functions without any resource declarations; `VectraCore.hlsl` includes it.
- **Hairline strokes.** `StrokeLayer.widthMode = StrokeWidthMode.Pixels` keeps a stroke N device pixels wide at any scale or zoom, anti-aliased, in Canvas and in world space; the mesh follows 1% changes of the pixel size. Inspector row *Unit* on every stroke. Hit tests use the pixel width.
- **Vector sprites.** `VectraSpriteBaker.Bake(...)` turns a shape or an SVG document into a `Sprite`: GPU-drawn texture at a chosen resolution, tight mesh, pivot, 9-slice border (optionally from the corner radii of a rectangle), mipmaps, colour bleed into transparent texels, room for shadows and glows. **Bake to Sprite Asset…** and **Bake to 9-Slice Sprite Asset…** on shapes (replace *Export as Sprite Asset…*), **Assets > Vectra > Bake SVG to Sprite**, and the SVG importer's *Sprite* mode gains 9-slice border, tight mesh and mipmaps.
- **UI Toolkit support.** `VectraElement` and `VectraSvgElement` (new assembly `FirstSymbol.Vectra.UIToolkit`, namespace `FirstSymbol.Vectra.UIToolkit`) draw Vectra in runtime `UIDocument` panels and in editor windows. Both are `[UxmlElement]`s with attributes (`shape`, `sides`, `fill`, `stroke`, `stroke-width`, `corner-radius`, `shape-asset`, `style-asset`; `document`, `preserve-aspect`, `quality`, `generate-text`), so UI Builder lists them under Custom Controls (C#). USS sets the look: `--vectra-fill`, `--vectra-stroke`, `--vectra-stroke-width` and `--vectra-corner-radius` follow `:hover` and the other pseudo states. `VectraElement` takes the same `VectraShapeContent` as `VectraShape`, so primitives, paths, booleans, paints, strokes and every effect work, and `ContainsPoint` picks by outline.
- **How it draws.** UI Toolkit's vertex has no room for Vectra's four float4 and `MeshGenerationContext` takes a texture, not a material, so an element builds the usual mesh, draws it with the usual UI material into a render texture and shows it as a premultiplied quad. Small layers share 2048 px pages (their quads batch into one draw call); layers over 512 px get a texture of their own. An element is redrawn only when its shape, size or screen density (by 25 %) changes; redrawing allocates no managed memory after warm-up, and a redrawn frame allocates no more than a plain UI Toolkit element.
- **SVG in UI Toolkit.** `VectraSvgElement` keeps paint order (shapes after a text are a child element above it) and draws SVG text with UI Toolkit labels, tspan styling and `text-anchor` included.
- **Sample "UI Toolkit"**: a UXML panel, USS with custom properties and hover colours, a gradient redrawn every frame, a star with effects built in code and an SVG element.
- **Platforms page**: UI Toolkit checked in the editor (Built-in/Gamma, URP/Linear, Vectra and Lettera as folders under Assets/Plugins), a Windows IL2CPP player (Direct3D 12), WebGL 2 and WebGPU in Chromium: 12 of 12 player checks each. WebGL 2 has no render texture copy, so elements draw straight into their page.
- New files in `package.json` `samples`: the "UI Toolkit" entry (the lead merges it with other samples).
- Tests: 29 PlayMode (runtime panel into a render texture, pixel checks) and 10 EditMode (page allocator, UXML attributes, an editor window panel).

### Fixed
- On the FirstSymbol Hub's Vectra page in the hub's light theme the object, text and popup fields stayed dark; they follow the theme the hub applies.
- A shape whose distance block changed size by a texel made a new atlas staging texture on every rebuild, so a moving boolean or effect shape allocated on each frame (up to 27 KB). The staging texture is kept and only grows.

### Limits
- No CPU gain that can be measured: 500 shapes cost 0.67 ms in `Camera.Render()` in a Windows IL2CPP URP player (median of 5), the 0.5.3 shader 0.65 ms and 0.78 ms in two sets of 5. The change is that the shapes now qualify for the SRP Batcher: their CPU cost no longer depends on a property block per shape, they can sit in one batch with other SRP Batcher objects of the same shader, and Shader Graph materials built on Vectra's functions batch the same way. Do not expect a frame-time gain at this size.
- Shader Graph functions use screen-space derivatives: fragment stage only. The Sub Graphs and the sample were written as files and imported by Shader Graph 17.4; nobody has edited them in the Shader Graph window yet.
- Unity builds the tight mesh of a baked sprite, so its outline is approximate; a baked sprite has no exact physics shape (`Sprite.OverridePhysicsShape` did not take effect in 6000.4.8), use `VectraRenderer`'s collider option instead.
- HDRP draws world shapes unexposed (like HDRP's Unlit); no fog or volumetric fog on them.
- A Vectra element in UI Toolkit is a bitmap at the density it was drawn for (a scale transform stretches it until the density drifts by 25 %); blend modes other than Normal, `VectraMask`, interaction states, the URP backdrop and Lettera-drawn text do not apply; SVG `<image>` is skipped; SVG text uses UI Toolkit labels in the panel's font (Lettera and TextMesh Pro have no UI Toolkit element) and the panel needs a theme style sheet for any font; a texture is at most 4096 px a side.

## [0.5.3] - 2026-11-09

### Fixed
- **The Lettera detection waited for an idle editor.** It ran from `EditorApplication.delayCall`, which an editor nobody has focused may not run for a long time, so `VECTRA_LETTERA` was not set. It now runs as the scripts load, and once more when the editor is idle. **Tools > FirstSymbol > Vectra > Detect Lettera** runs it by hand and says what it found.

## [0.5.2] - 2026-11-09

### Added
- **Lettera as a folder under `Assets` is detected.** Vectra finds Lettera's assembly wherever it lives and adds `VECTRA_LETTERA` to the scripting define symbols of the build targets, so SVG text through Lettera works without a package. The symbol goes again, before anything is compiled against a missing assembly, when Lettera's folder is deleted or moved. Only a symbol the tool added is ever removed (it keeps a list in `ProjectSettings/FirstSymbolAutoDefines.txt`); a symbol you set yourself stays, and so does one on a target where you set it. One log line says when it changed anything. With Lettera as a package nothing changes: the package manager still sets it.
- **A question after installing: which importer for .svg files?** Once per project, in a floating window in Vectra's look: **Use Vectra's importer** (and offers to switch the .svg files already there), **Keep Unity's importer**, **Ask me later**. When the Vector Graphics package is installed the window says that it imports .svg as well and recommends neither. It does not appear in batch mode, when `VECTRA_NO_PROMPTS` is set, when the importer is already on, or once answered. **Project Settings > FirstSymbol > Vectra > SVG import > Which importer?** asks again.

### Changed
- The README no longer asks you to add `VECTRA_LETTERA` or `FIRSTSYMBOL_HUB` by hand for folders under `Assets`.

### Limits
- Deleting Lettera while the editor is closed leaves `VECTRA_LETTERA` set, so Unity opens with compile errors. Remove it in Project Settings > Player > Scripting Define Symbols, or put the folder back and delete it from inside Unity.

## [0.5.1] - 2026-11-09

### Docs
- The README says what to add when Vectra and Lettera are folders under `Assets` instead of packages: `VECTRA_LETTERA` for SVG text through Lettera, `FIRSTSYMBOL_HUB` for the hub page.

## [0.5.0] - 2026-11-09

### Added
- **FirstSymbol Hub page.** With the FirstSymbol Hub package (`com.firstsymbol.hub`) installed, Vectra appears in its window with a status card (text engine, SVG importer default, distance atlas size) and its settings page. The assembly `FirstSymbol.Vectra.HubPage` compiles only when the hub is there; without it Vectra is unchanged.

### Changed
- **Menus and settings under FirstSymbol.** `Tools > Vectra` is now `Tools > FirstSymbol > Vectra`, `Window > Vectra` is `Window > FirstSymbol > Vectra`, and `Project Settings > Vectra` is `Project Settings > FirstSymbol > Vectra`. The old paths are gone; `EditorApplication.ExecuteMenuItem` calls of your own need the new path.

## [0.4.0] - 2026-11-09

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
- An `.svg` that was imported before Vectra's importer scripts were registered (the first import of a project that already holds the file) stayed a plain asset until reimported by hand; it is reimported once, automatically.
- The URP Backdrop sample broke compilation in a project without URP once imported; it has its own assembly definition now.
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
