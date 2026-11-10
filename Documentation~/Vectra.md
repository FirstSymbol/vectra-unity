# Vectra — Vector Graphics for Unity

Vectra draws resolution-independent vector shapes in **uGUI Canvas**, in **world space** and in **UI Toolkit**: analytic primitives,
arbitrary paths, boolean operations, gradients, strokes, effects, masks and SVG documents. All shapes share one
material, so a whole UI made of Vectra shapes batches into a handful of draw calls, and rebuilding analytic shapes,
rectangle and ellipse paths and boolean groups of them does not allocate managed memory (path mode of star, polygon and
a few other shapes still builds a small point array).

*Русская версия: [Vectra.ru.md](Vectra.ru.md).*

- **Unity** 6000.3 (6.3 LTS) or newer · **Render pipelines** Built-in, URP, HDRP (backdrop effects: URP only)
- **Dependencies** Burst, Collections, Mathematics, uGUI. SVG text uses Lettera or TextMesh Pro when present, else the legacy uGUI Text
- **Platforms** Shader Model 3.5 and up: Windows, macOS, Linux, WebGL 2, WebGPU, iOS, Android.
  No compute shaders. On WebGL the Burst jobs run as IL2CPP code.

## Contents

1. [Installation](#installation)
2. [How it works](#how-it-works)
3. [Components](#components)
4. [UI Toolkit](#ui-toolkit)
5. [Shapes](#shapes)
6. [Paint, strokes and palettes](#paint-strokes-and-palettes)
7. [Effects](#effects)
8. [Masks](#masks)
9. [Boolean operations](#boolean-operations)
10. [SVG](#svg)
11. [Animation](#animation)
12. [URP backdrop](#urp-backdrop)
13. [Editor tools](#editor-tools)
14. [Command line](#command-line)
15. [Performance](#performance)
16. [Troubleshooting](#troubleshooting)

## Installation

Package Manager → **+** → **Add package from git URL…** →
`https://github.com/FirstSymbol/vectra-unity.git` (a specific version: append `#0.5.3`).

Samples: **Package Manager → Vectra Vector Graphics → Samples**.

| Sample | Shows |
|---|---|
| Shapes Gallery | Every primitive and path generator, paints, animated trim, dashes, fill amount, morphing |
| SVG Showcase | SVG documents in a Canvas and in world space, per-element overrides |
| Effects & Masks | Every effect, soft / inverted / stencil masks, a palette with a theme switch |
| Boolean Operations | Groups with moving operands, smooth union (metaballs), a BooleanShape built in code |
| Animation | Fluent tweens, an AnimationClip keying Animated Properties, morphing, marching dashes |
| World Space | VectraRenderer cards in 3D, generated colliders, click handling |
| UI Toolkit | A UXML panel with `VectraElement`s styled from USS, an SVG element and a code-built star (see [UI Toolkit](#ui-toolkit)) |
| URP Backdrop | Frosted glass and 15 blend modes over moving UI (see [URP backdrop](#urp-backdrop)) |

## How it works

**Analytic shapes** (rectangle, ellipse, star, …) are drawn as one quad per layer; the fragment shader evaluates the
exact signed distance to the outline. Edges are anti-aliased at any scale, corner radii never stretch, and shadows,
glows, bevels and strokes cost almost nothing.

**Path shapes** (Bezier paths, SVG, booleans, gears, …) are flattened and tessellated on the CPU by Burst jobs into
trapezoids whose sides are anti-aliased analytically. Effects that need a distance field (shadows, glows, blur,
pixelate, soft masks) bake one into a shared atlas.

**One material.** Layer parameters travel in the vertex data; gradients live in a shared gradient atlas and baked
distance fields in a shared SDF atlas. Every Vectra shape therefore uses the same material and textures. Texture
paints, backdrop shapes and soft-masked children are the exceptions (their own texture or material).

**Rebuilds** happen only when something changes; tessellation, strokes and distance fields are cached by hash.

## Components

| Component | Purpose |
|---|---|
| `VectraShape` | uGUI shape (`MaskableGraphic`): Mask, RectMask2D (with softness), layout, precise raycasts |
| `VectraRenderer` | World-space shape (MeshRenderer): sorting layer/order, optional `PolygonCollider2D` |
| `VectraSvgImage` / `VectraSvgRenderer` | An SVG document as one mesh (UI / world) with per-element overrides |
| `VectraElement` / `VectraSvgElement` | The same shapes and SVG documents for UI Toolkit (UXML, USS, runtime and editor panels): [UI Toolkit](#ui-toolkit) |
| `VectraBooleanGroup` + `VectraBooleanOperand` | Child shapes become operands of a boolean operation |
| `VectraMask` | Masks children by the outline of the shape: stencil or soft (feathered, invertible) |
| `VectraTheme` | Activates a `VectraPalette` and selects its theme |
| `VectraAnimatedProperties` | Keyable fields for Animator / Timeline (trim, colours, fill amount, …) |
| `VectraInteractionStates` | Hover, pressed, selected (focus) and disabled looks with eased transitions |
| `VectraBackdrop` (URP) | Frosted glass and advanced blend modes |

Assets: `VectraStyle` (shared fills/strokes/effects, like a CSS class), `VectraShapeAsset` (shared geometry: a shape library), `VectraPalette` (named colours per theme),
`VectraSvgDocument` (imported SVG), `VectraFontMap` (SVG font-family → font of each text engine).

Create objects from **GameObject → UI → Vectra** (Canvas) or **GameObject → 2D Object → Vectra** (world).

```csharp
var shape = gameObject.AddComponent<VectraShape>();
var star = shape.SetGeometry<StarShape>();
star.points = 6;
shape.SetFill(VectraPaint.Linear(Color.yellow, Color.red, 90f))
     .SetStroke(Color.white, 3f, StrokeAlignment.Outside);
shape.AddEffect<DropShadowEffect>().blur = 12f;

// After changing parameters from code:
shape.Strokes[0].trimEnd = 0.5f;
shape.SetShapeDirty();
```

## UI Toolkit

`VectraElement` and `VectraSvgElement` (namespace `FirstSymbol.Vectra.UIToolkit`, assembly `FirstSymbol.Vectra.UIToolkit`) draw Vectra in UI Toolkit panels: runtime `UIDocument`s and editor windows. Both are `[UxmlElement]`s, so UI Builder lists them under **Library → Project → Custom Controls (C#)**.

| Element | Purpose |
|---|---|
| `VectraElement` | Any primitive or path with fills, strokes, effects, booleans, palettes and styles. Takes the same `VectraShapeContent` as `VectraShape`, so code written for uGUI moves over unchanged. |
| `VectraSvgElement` | A `VectraSvgDocument`: shapes, colour overrides by selector, paint order, SVG text (see the limits below). |

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements" xmlns:vf="FirstSymbol.Vectra.UIToolkit">
    <vf:VectraElement shape="Star" sides="6" fill="#ffcc00" stroke="#222222" stroke-width="3" class="badge" />
    <vf:VectraSvgElement document="project://database/Assets/Icons/logo.svg" />
</ui:UXML>
```

Attributes of `VectraElement`: `shape` (Rectangle, Ellipse, Triangle, Polygon, Star, Heart, Squircle, Custom), `sides`, `fill`, `stroke`, `stroke-width`, `corner-radius`, `quality`, `shape-asset` and `style-asset` (shared geometry and appearance). Attributes of `VectraSvgElement`: `document`, `preserve-aspect`, `quality`, `generate-text`. Size comes from the usual `width` and `height`; with none, the element is 100 by 100 points.

USS properties, which win over the attributes and follow `:hover`, `:active` and the other pseudo states:

```css
.badge { --vectra-fill: #ffcc00; --vectra-stroke: #222222; --vectra-stroke-width: 3; --vectra-corner-radius: 8; }
.badge:hover { --vectra-fill: #ffe680; }
```

From code, the shape is described like a `VectraShape`:

```csharp
using FirstSymbol.Vectra.UIToolkit;
using UnityEngine.UIElements;

var root = new VisualElement();
var star = new VectraElement { Shape = VectraElementShape.Star, Fill = Color.yellow, StrokeWidth = 3f };
star.style.width = 120;
star.style.height = 120;
star.SetGeometry<StarShape>().points = 7;
star.AddEffect<DropShadowEffect>().blur = 10f;
star.Fills[0].paint = VectraPaint.Linear(Color.yellow, Color.red, 90f);
star.SetShapeDirty();   // after changing geometry, fills, strokes or effects
root.Add(star);
```

**How it works.** UI Toolkit meshes carry only a position, a colour and one uv, and `MeshGenerationContext` takes a texture, not a material, while a Vectra vertex carries four float4. So the element builds the same mesh as `VectraShape`, draws it with the same material and shader into a render texture, and UI Toolkit shows that texture as a quad. Anti-aliased SDF edges, gradients, dashes, shadows, glows, bevels and every other effect therefore look the same as in a Canvas. Small elements share 2048-pixel pages, so UI Toolkit batches their quads into one draw call; an element over 512 pixels gets a texture of its own. The picture is redrawn only when the shape, the element's size or the screen density (by 25 %) changes; an idle element costs nothing, and redrawing allocates no managed memory after warm-up.

**Limits.**
- The element is a bitmap at the density it was drawn for. A scale transform stretches it until the density has drifted by 25 %, then it is redrawn. A rotated element is resampled and softer.
- Blend modes other than Normal, `VectraMask`, interaction states, the URP backdrop and Lettera-drawn text are uGUI and world features and do not apply here. A shape cannot blend with what is behind the element.
- SVG text is drawn by UI Toolkit labels in the panel's font (Lettera and TextMesh Pro have no UI Toolkit element). The panel needs a theme style sheet for any font to show. Baseline and anchor are placed from the font size, so a font with unusual metrics sits a point or two off.
- SVG `<image>` elements are skipped.
- A texture is at most 4096 pixels on a side; a larger element is drawn at a lower density.
- Elements pick by outline (`ContainsPoint`), including holes and strokes.

## Shapes

- **Analytic:** rectangle (per-corner radius; round, chamfer, inverted, notch or squircle corners), ellipse, arc / ring /
  pie, polygon, star, triangle, line, cross, heart, rhombus, trapezoid, parallelogram, crescent, egg, vesica,
  squircle, flower, arrow, chevron.
- **Paths:** Bezier path (edited in the Scene view), polyline with rounded corners, spiral, gear, wave, SVG path data,
  speech bubble, checkmark.
- **Composite:** boolean shape (exact or smooth), morph between any two shapes.

- **From sprites:** `TracedSprite` turns a sprite's alpha silhouette into contours (alpha threshold, sampling resolution, simplification, smoothing; holes included; works with non-readable textures).

Every shape has `rotation`. Any analytic shape can be rendered as a path (**Force Path**), which dashes need anyway.
**Convert to editable path** (Shape tab, or `VectraPathConverter.ToPathShape`) turns any shape, SVG path data or traced sprite into Bezier points you can edit in the Scene view.

**Shared shapes.** Save the geometry as a `VectraShapeAsset` (Shape tab → Save, or Assets → Create → Vectra → Shape). Every object that references it draws the same geometry and updates when the asset changes; **Detach** copies it back for local edits. Together with styles and palettes this forms a shape library: place an asset with Assets → Create → Vectra → Shape in Scene.

## Paint, strokes and palettes

**Paint types:** solid (HDR), linear / radial (with focal point) / angular / diamond gradients (pad, repeat, reflect;
any number of stops, midpoints, sRGB / linear / OKLab / step interpolation), texture (stretch, fit, fill, tile),
stripes, checker, dots, grid, noise, freeform gradient, and **contour gradient**: a gradient along the distance from the outline (edge fades; with Repeat/Reflect, rings and bands that follow the shape). Colour and alpha share one stop list. Gradients are dithered against banding.

**Strokes:** any number per shape; centre / inside / outside; caps and joins with miter limit; dashes (absolute or
relative lengths, offset); trim path; width profile (`AnimationCurve`); drawn behind or above the fill.

**Palettes.** A `VectraPalette` (Assets → Create → Vectra → Palette) holds named colours with one value per theme.
Pick a name in the **Palette** row of a paint (fills, strokes, pattern colours). Add a **Vectra Theme** component to
make a palette active and choose the theme; switching the theme recolours every shape that uses tokens:

```csharp
theme.SetTheme("Dark");            // VectraTheme component
VectraPalette.Active = palette;    // or from code
palette.SetTheme("Light");
```

Tokens apply to paint colours (solid, texture tint, pattern colours). Effect colours are plain colours.

## Effects

Drop shadow, inner shadow, outer glow, inner glow, bevel & emboss, layer blur, noise / grain, colour adjust (hue,
saturation, brightness, contrast), fill amount (horizontal, vertical, radial), dissolve, **pixelate**, **outline**
(a band of the distance field, rounded at every corner), **colour overlay**, **gradient overlay**, **extrude** (layered silhouette slices from a near to a far colour) and **deform** (wavy outline animated on the GPU, no rebuilds).

**Custom effects.** Derive from `VectraCustomEffect` and emit layers in `Populate`; they use the same change tracking, batching and shader as the built-in effects and appear in the Add effect menu:

```csharp
[Serializable] public class NeonRings : VectraCustomEffect
{
    public Color color = Color.cyan; public int rings = 3; public float gap = 6f;
    public override string DisplayName => "Neon Rings";
    public override float Reach => rings * gap + 4f;
    public override void Populate(VectraEffectBuilder b)
    {
        for (int i = 1; i <= rings; i++) b.Band(color, 2f, i * gap);   // also Fill, Shadow, InnerShadow
    }
}
```

Each layer and effect blends Normal or Additive; the whole shape blends Normal, Additive, Multiply or Screen. Only
one of fill amount, pixelate and dissolve is applied at a time (in that priority). Pixelate and the distance-based
effects of path shapes use the baked distance field; geometric strokes of path shapes are not pixelated.

## Masks

Add **Vectra Mask** to a `VectraShape`:

- **Stencil** — like uGUI `Mask`, but the stencil follows the exact outline instead of the rect. Masks every UI graphic
  below it (Images, Text, other shapes). Hard edge.
- **Soft** — evaluated per pixel by the Vectra shader from the distance function: `Softness` feathers the edge inwards,
  `Inset` moves it, `Invert` shows the children outside the shape. Applies to `VectraShape` and `VectraSvgImage`
  children; other graphics are not masked. Soft-masked children share one material per mask (they batch together).

## Boolean operations

**Vectra Boolean Group** turns its child shapes into operands (union, subtract, intersect, exclude, evaluated in
hierarchy order); the children are hidden and the result is drawn by the group's own shape with all its fills,
strokes and effects. Operands may move, rotate and resize at runtime. **Smoothness** blends them (metaballs) through
a distance field. From code use `BooleanShape` directly:

```csharp
var b = shape.SetGeometry<BooleanShape>();
b.operands.Clear();
b.operands.Add(new BooleanOperand(new EllipseShape(), BooleanOp.Union, new Rect(0, 0, 1, 1)));
b.operands.Add(new BooleanOperand(new EllipseShape(), BooleanOp.Subtract, new Rect(0.25f, 0.25f, 0.5f, 0.5f)));
shape.SetShapeDirty();
```

## SVG

**Import.** Select an `.svg` file and choose **Vectra Svg Importer** in its importer dropdown, or turn on
**Project Settings → Vectra → Use Vectra importer** (a button switches every existing file). Modes: Vectra Document,
Sprite or Texture. **Window → Vectra → SVG Import Report** lists what each file could not reproduce.

**Supported:** paths (all commands), basic shapes, groups, nested svg, use/symbol/defs, transforms, CSS (`<style>`,
selectors, specificity, inheritance), presentation attributes, linear and radial gradients (with transforms and
spread), strokes with dashes, opacity, clip paths, blur and drop-shadow filters, saturate / hue-rotate, `<text>` with
`<tspan>` styling (see Text engines below), embedded data-URI images, and markers (see Markers below).

**Paint order.** Text and images are separate objects, and the shapes that follow them in the file are drawn above
them: a `VectraSvgImage` draws the shapes before the first text as its own mesh and puts each later run of shapes in a
child (`SVG Layer`) after the text it follows. A red block that comes after a text in the file covers the text, as in a
browser. `VectraSvgRenderer` does the same with sorting orders. The children are generated, not saved, and stay put when
the image is resized (only their positions change).

**Markers.** `marker-start`, `marker-mid`, `marker-end` and the `marker` shorthand work on `path`, `line`, `polyline` and
`polygon`: `orient` (`auto`, `auto-start-reverse` or an angle), `markerUnits` (`strokeWidth` by default, or
`userSpaceOnUse`), `refX` / `refY`, `viewBox`, `markerWidth` / `markerHeight` and `overflow` (hidden by default: the
marker is clipped to its viewport). Markers are drawn as ordinary elements after the shape, so they take its transform,
clip path and filter. Markers on a marker's own content are not drawn, and every marker counts against the `<use>`
budget.

**Runtime API:**

```csharp
var doc = VectraSvg.Parse(svgText);                 // or VectraSvg.Load(textAsset)
svgImage.Document = doc;
VectraSvg.Download(url, (d, error) => svgImage.Document = d);
```

**Expand to hierarchy** (button in the SVG Image inspector, context menu, or `VectraSvg.Expand(image)`) converts the
document into one `VectraShape` per element so each can be edited or animated. Clip paths become boolean operands
(filled elements) or nested stencil masks (stroked elements).

**Text engines.** SVG `<text>` is drawn by a text engine, chosen in **Project Settings → Vectra → Text engine**:

| Engine | Drawn with | Needs |
|---|---|---|
| Auto (default in new projects) | Lettera if installed, else TextMesh Pro if it has a font, else Legacy | |
| Lettera | `LetteraText` | the Lettera package 1.0.0 or newer |
| TextMesh Pro | `TextMeshProUGUI` | TMP Essential Resources, or a TMP font in the font map or on the component |
| Legacy | uGUI `Text` with the built-in font | nothing; font families are ignored |
| None | the text is not drawn | |

The page shows which engines this project has and what Auto picks. A named engine that is missing or has no font falls
back along the Auto order and the console says so once per document. Each SVG Image can override the project choice
(**Text engine** in its inspector, or `image.TextEngine`). The engine is also carried into player builds.

Text keeps its styling: a `<tspan>` with its own bold, italic, fill or size stays inside one text as markup (`<b>`,
`<i>`, `<color>`, `<size>`) in every engine. Not drawn: per-`<tspan>` positions (`x`, `y`, `dx`, `dy`), `textPath`,
`letter-spacing` and text decoration; a `<tspan>` with another `font-family` uses the font of the whole text.

**Text in world space.** `VectraSvgRenderer` draws text through the same backends, with the engines' Canvas-free text:
Lettera **World Text** (`LetteraWorldText`) or TextMesh Pro **3D** (`TextMeshPro`). The legacy uGUI `Text` only draws inside
a Canvas, so with only that engine the text of a world SVG is skipped and the console says so once. The renderer has the
same **Text engine**, **Font map** and fallback fonts as the UI image. Text and the shapes after it keep their order
through sorting orders: this component's `SortingOrder` plus one per step of the document. An engine written for 0.3
must add `SupportsWorld` and `BuildWorld` (return `false` and do nothing if it cannot draw without a Canvas).

**Upgrading from 0.2.** Before 0.3 all SVG text used TextMesh Pro. A project that already used Vectra (it has a Vectra
settings file, a font map, or an SVG with text) gets **TextMesh Pro** written into its settings once, with a line in the
console, so its text looks as before. Choose **Auto** or **Lettera** to move to Lettera. New projects start on Auto.
`VectraFontMap` keeps its TextMesh Pro fields as they were (they are plain object references now, the saved data is the
same) and has Lettera fields next to them: `lettera`, `letteraBold`, `letteraFallback` (a Lettera font asset or font
stack).

**Fonts.** A `VectraFontMap` (Assets → Create → Vectra → SVG Font Map) maps `font-family` names to the fonts of each
engine; assign it on the SVG Image (new images get the default from Project Settings). Without a match the component's
fallback font (one per engine), then the engine's default font, is used. Lettera finds fonts only through the map in
this version.

**Limitations:** `<mask>` is applied as a geometric clip (luminance is ignored); pattern fills fall back to the colour
of the first pattern shape; only blur, drop shadow, saturate and hue-rotate filters; external image URLs are not
loaded; a group's `opacity` is applied to each child separately, so overlapping children show through each other (the
import report says so); text and images are not clipped by `clip-path`; images are axis-aligned rectangles and
`preserveAspectRatio` is ignored for them; of a `<switch>` only the first child whose `systemLanguage` /
`requiredExtensions` conditions pass is drawn (the language list is `SvgParseOptions.languages`, default `en`). A `<use>`
restyles its target for that copy only; the target keeps its own style where it stands.

**Untrusted files.** The parser stops at budgets instead of crashing or hanging: nesting depth 256, 200 000 elements,
200 000 elements drawn (every `<use>` copy counts), 10 000 `<use>` copies, 1 000 000 characters of path data, 32 million
characters per file. A file that hits one keeps what was read so far and gets a warning starting with "SVG limit
reached". `SvgParser.Parse(text, new SvgParseOptions { ... })` changes the budgets.

## Animation

**Tweens** (no dependencies; stop automatically when the target is destroyed):

```csharp
shape.TweenTrim(0f, 1f, 0.6f).SetEase(VectraEase.OutCubic).OnComplete(Next);
shape.TweenFillColor(Color.red, 0.3f);
shape.TweenRotation(360f, 1f).SetEase(VectraEase.Linear).SetLoops(-1);
shape.TweenFillAmount(0.75f, 0.5f);        // adds a FillAmountEffect when missing
shape.TweenMorph(1f, 1f).SetLoops(-1, true);
shape.TweenFloat(0f, 1f, 1f, v => shape.Strokes[0].dashOffset = v * 40f);
shape.KillTweens();
```

Works for `VectraShape` and `VectraRenderer` (`IVectraShape`). Eases: Linear, In/Out/InOut Quad and Cubic, InOutSine,
OutBack, OutElastic, OutBounce. Options: `SetDelay`, `SetLoops(count, yoyo)`, `SetUnscaledTime`, `Kill`.

**Animator and Timeline.** Layer lists and polymorphic effects cannot be keyed directly. Add **Vectra Animated
Properties**, turn on the parameters to drive (fill colour/opacity, stroke colour/width, trim, dash offset, rotation,
fill amount, dissolve, morph) and key its fields in the Animation window or a Timeline animation track.

## Interaction states

**Vectra Interaction States** gives a shape Normal, Hover, Pressed, Selected (keyboard/controller focus) and Disabled looks: tint, scale, fill and stroke colour, and shadow/glow strength, blended with an eased transition. It reacts to pointer and selection events; a Selectable on the same object (Button, Toggle…) drives Disabled. `SetState` forces a state from code; `StateChanged` reports changes. It runs in play mode only.

## URP backdrop

1. Add **Vectra Backdrop Feature** to the Universal Renderer asset (Add Renderer Feature).
2. Add **Vectra Backdrop (URP)** to a `VectraShape` and pick Frosted Glass or a blend mode (Overlay, Soft Light, Hard
   Light, Color Dodge/Burn, Difference, Exclusion, Hue, Saturation, Color, Luminosity, Darken, Lighten, Linear
   Burn/Dodge).

The feature captures the camera image **before transparent objects** (once per camera) and blurs it. Opaque scene
content is therefore part of the backdrop. To blur other UI, draw that UI with a base camera and the glass UI with an
overlay camera of its stack (two Screen Space - Camera canvases): the overlay camera's capture contains the finished
background. The *URP Backdrop* sample is set up this way. Backdrop shapes use their own material.

## Editor tools

- **Inspector:** shape grid, fill/stroke/effect cards (visibility, drag to reorder by the grip, duplicate, remove),
  gradient editor with presets, palette picker, styles (Save / Detach), build statistics (vertices, distance-field
  size, build time).
- **Scene view:** the **Edit Vectra Shape** tool edits path points and tangents (+ on a segment inserts a point,
  Ctrl/Cmd+click deletes), polyline points, corner radii (Shift: one corner), arc angles, gradient handles and the drop
  shadow offset. Its overlay switches the selected point between Corner, Smooth and Symmetric, snaps to a grid and
  toggles handle groups.
- **Context menu:** Copy / Paste Vectra Style (also between VectraStyle assets), Export as PNG / SVG / Sprite Asset,
  Expand to Hierarchy (SVG Image).
- **Windows:** Window → Vectra → Atlas Debugger (atlas textures, usage, regions, reset) and SVG Import Report.
- **Project Settings → Vectra:** SVG importer, default quality, distance atlas size, default font map.

## Command line

With the `com.unity.pipeline` package installed, the optional assembly `FirstSymbol.Vectra.Editor.Cli` registers `unity command vectra …` commands for scripts, CI and AI agents. What the commands do lives in `FirstSymbol.Vectra.Editor.CliCore`, which does not need that package (the tests call it directly); the Cli assembly only holds names and arguments. Every command answers JSON and edits are undoable:

- discovery: `help`, `types` (all shapes, effects and paints with defaults), `list`, `get --target` (`--full true` for every field);
- authoring: `create`, `set --data <json patch>`, `add`, `remove`, `mask`, `states`, `convert_to_path`, `save_style`, `save_shape`, `palette`, `theme`, `svg_parse` and `svg_create` (both take `--text_engine`), `select`;
- output: `export_png` (check results visually), `export_svg`.

```bash
unity command vectra create --shape Rectangle --name Card --size 320,180 --data "{effects:[{$type:DropShadow,blur:24}]}"
unity command vectra set --target Card --data "{fills:[{paint:{type:LinearGradient,gradient:'#6366F1,#EC4899'}}]}"
unity command vectra export_png --target Card --output card.png
```

**Claude Code:** **Tools → Vectra → Install Claude Code Skill** copies `Documentation~/AgentGuide.md` to `.claude/skills/vectra/SKILL.md` so the agent knows these commands, the JSON conventions and recipes.

More examples:

```
unity command vectra stats
unity command vectra reset_atlases
unity command vectra use_importer --path Assets/Icons
unity command vectra export_png --target "Canvas/Logo" --output Logo.png --scale 2
unity command vectra export_svg --target Logo --output Logo.svg
unity command vectra expand_svg --target "SVG Image"
```

## Performance

- Keep shapes analytic when possible; path shapes with effects bake a distance field (Quality controls its size).
- Shapes rebuild only when changed. Animated parameters rebuild every frame without managed allocations.
- Texture paints, backdrop shapes and soft-masked children use separate materials (they batch among themselves).
- In UI Toolkit a shape is rasterized into a shared page only when it changes; small elements share pages so their quads batch (see [UI Toolkit](#ui-toolkit)).
- The SDF atlas size limit is in Project Settings and reaches player builds (the build adds a small settings asset to the Preloaded Assets and removes it afterwards); the Atlas Debugger shows usage. A change uploads only the changed blocks of the atlas, not the whole texture.
- The gradient atlas (256 texels wide, up to 4096 rows, 8 MiB of half floats at most) shares rows between paints with the same colours and stops. Rows are compared by content, so two paints can never end up on one row. A row nobody uses any more stays baked; the least recently used ones are dropped when the atlas is full. If 4095 different gradients are alive at once, the rest draw white and the console warns once.
- What has been built and run on which platform: [Platforms](Platforms.md).

## Troubleshooting

| Symptom | Fix |
|---|---|
| A shape is invisible in a Canvas | The canvas needs TexCoord1–3 shader channels (added automatically when the shape is enabled under it) |
| Backdrop shapes render plain | Add the Vectra Backdrop Feature to the active URP renderer |
| Glass does not blur other UI | Draw the background UI with a base camera and the glass with an overlay camera |
| SVG text is missing | Check **Project Settings → Vectra → Text engine**. For TextMesh Pro import TMP Essential Resources or assign a font / font map; Lettera needs the package; the console warns once when an engine had to be replaced |
| A Vectra element in UI Toolkit shows nothing | It needs a size (width and height) and to be in a panel; SVG text also needs a theme style sheet on the panel for a font |
| `.svg` imported by another importer | Choose Vectra Svg Importer on the file or enable it in Project Settings |
