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
11. [World space and Shader Graph](#world-space-and-shader-graph)
12. [Vector sprites](#vector-sprites)
13. [Animation](#animation)
14. [URP backdrop](#urp-backdrop)
15. [Editor tools](#editor-tools)
16. [Command line](#command-line)
17. [Performance](#performance)
18. [Troubleshooting](#troubleshooting)

## Installation

Package Manager → **+** → **Add package from git URL…** →
`https://github.com/FirstSymbol/vectra-unity.git` (a specific version: append `#0.7.4`).

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

**Hairlines.** A stroke's **Unit** is Local (the width follows the scale of the shape) or **Pixels**: the stroke stays N
device pixels wide at any scale or zoom, anti-aliased, in a Canvas and in world space. SVG's `vector-effect="non-scaling-stroke"` becomes such a stroke (see [SVG support](#svg-support)). The mesh follows the pixel size
(a change of 1% rebuilds it), so a camera zoom or a Canvas Scaler change keeps the line crisp.

```csharp
shape.SetStroke(Color.white, 1f, StrokeAlignment.Center);
shape.Strokes[0].widthMode = StrokeWidthMode.Pixels;   // 1 pixel at any zoom
shape.SetShapeDirty();
```

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

**Text layout.** Vectra places the text, the engine draws it. The parser cuts a `<text>` into pieces that start at an
absolute `x` or `y` (a `<tspan x=".." y="..">` per line, as Figma exports text when "Outline text" is off; per-glyph lists
`x="10 20 30"` make a piece per glyph, up to 512 pieces per `<text>`). Each piece is one text object at its own baseline
position and `text-anchor`, so positions work with every engine. A relative `dy` / `dx` on the start of a piece is added to
its position; `1.2em` follows the font size. White space follows `xml:space="preserve"` and `white-space: pre`
(otherwise it collapses); a line break at the end of a piece draws nothing and moves nothing.

Styling goes through the engine's markup. A `<tspan>` with its own bold, italic, fill or size stays inside one text
(`<b>`, `<i>`, `<color>`, `<size>`); a `<tspan>` with another `font-family` uses the font of the whole text.

| Style | Lettera | TextMesh Pro | UI Toolkit | Legacy uGUI Text |
|---|---|---|---|---|
| Position (`x`, `y`, lists, `dx`, `dy` at the start of a piece), `text-anchor` | Yes | Yes | Yes | Yes |
| bold, italic, fill, size per `<tspan>` | Yes | Yes | Yes | Yes (size in whole pixels) |
| `letter-spacing` (`em`, `px`, number) | Yes (`<tracking>`) | Yes (`<cspace>`) | Yes (`<cspace>`) | No |
| `text-decoration` underline and line-through | Yes (`<u>`, `<s>`) | Yes | Yes | No |
| `dx` / `dy` inside a line | No | Yes (`<space>`, `<voffset>`) | Yes | No |

The underline sits where the engine puts it (the font's own metrics), a pixel or two from a browser's. Not drawn:
`rotate`, `textLength`, `word-spacing`, `textPath` (drawn straight), `overline`. The sprite and PNG paths draw no text.
A `y` without an `x` on a later piece continues from the start of the previous piece: Vectra does not measure glyphs.

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

**Limitations** (the full list is the SVG support table below): a `<mask>` is composited offscreen unless it is a plain opaque shape (then it is a geometric clip); text inside a `<pattern>` is not drawn;
only blur, drop shadow, saturate and hue-rotate filters; external image URLs are not
loaded; text and images are not clipped by `clip-path`, and inside a group with a mask they ignore the mask; images are axis-aligned rectangles and
`preserveAspectRatio` is ignored for them; of a `<switch>` only the first child whose `systemLanguage` /
`requiredExtensions` conditions pass is drawn (the language list is `SvgParseOptions.languages`, default `en`). A `<use>`
restyles its target for that copy only; the target keeps its own style where it stands.

### Layers (opacity, masks, blend modes, background blur)

Some SVG features need a picture of a part of the document, not just a mesh. Vectra draws such a part (a *layer*) offscreen at the pixel density of the output and composites it:

- `opacity` on a `<g>`, `<svg>` or `<use>` with more than one shape, and on a shape that has both a fill and a stroke: the part is composited as one, as SVG says, so overlapping children do not show through each other. A single shape with only a fill or a stroke just gets the opacity in its colour.
- `mask`: luminance (the default) or alpha (`mask-type`), with its `maskUnits` region. A mask that is only plain opaque white shapes (any opaque colour in an alpha mask) stays a geometric clip: exact and free. Figma's inside and outside stroke masks are of that kind.
- `mix-blend-mode`, all CSS modes (the separable ones and hue, saturation, color, luminosity).
- Figma's background blur (`data-figma-bg-blur-radius`): blurs the content below, only inside the shape (what lies outside it does not mix in).
- `filter` on an element whose result depends on more than one painted thing: a `<g>`, `<svg>` or `<use>` with several shapes, or a shape with both fill and stroke. The shadows (drop shadow with spread, inner shadow with choke) and the layer blur are computed from the picture of the whole subtree, in the order of the filter chain, so one soft shadow falls from the combined silhouette and a shape never shadows its sibling. A single shape with only a fill or a stroke keeps native, resolution-free effects.
- The blend mode of a Figma shadow (`feBlend mode="multiply"` and the others, all CSS modes). Figma writes `in2="BackgroundImageFix"`, an empty flood, which in strict SVG makes any mode a no-op (browsers draw a normal shadow); Figma itself blends the shadow with what is below, and so does Vectra: a drop shadow with a mode blends with the backdrop through the layer compositor, an inner shadow with the picture of the shape. A shape with such a shadow is a layer.

Only the parts that need it become layers; everything else stays in the mesh. The layers of a Canvas, UI Toolkit or world component are drawn into render textures that the component keeps, and drawn again only when the size, the pixel density or the overrides change; the sprite importer and `VectraRasterizer` draw the same layers into the PNG.

**What blends with what.** The backdrop of a blend mode or of the background blur is the content of the same SVG drawn before it (back to the last text or image), never the scene behind the component: such a layer draws everything it needs into one texture, so a blend mode does not mix the picture with the UI behind it. Texts and images are separate objects above the layer; they do not take part in blending, and inside a group with a mask they ignore the mask.

**Blur like Figma.** Figma draws every blur (shadows, inner shadows, layer blur, background blur) with a Gaussian of 5/6 of the standard deviation its SVG export writes (`stdDeviation` = radius / 2): measured against Figma's own renders, 0.83 gives the smallest error every time. Chrome follows the SVG and blurs about 20 % wider. Vectra follows Figma for the chains Figma writes; a filter from another generator (`feDropShadow`, a plain blur) is drawn as written. Shadows of a layer are computed in the resolution of the picture and end at the edge of the component (leave room for them in the `viewBox`, as Figma's export does); noise (`feTurbulence`) and colour adjust stay per shape. Texts are separate objects and do not cast a shadow.

**Cost and limits.** A layer texture is as big as the component in device pixels (at most 4096 px a side); nested layers use temporary textures of that size (a document may have 256 layers, nested 8 deep; the import report names what is beyond that, which is then drawn without the group effect), and a background blur wider than about 12 px is drawn on a smaller copy. The colours are blended as stored (the sRGB values, like browsers do); in a project with Linear colour space the result may differ slightly. Not supported: `VectraMask` soft masks on a composited part of a Canvas image, composited parts in world space under HDRP, masks inside masks, `maskContentUnits="objectBoundingBox"` as a luminance mask. *Expand into objects* draws the shapes without layers (the shadows and blurs of a filtered group become effects of each of its shapes).

**SVG colour blending (Linear colour space projects).** Browsers and Figma blend translucency, anti-aliased edges and gradients on sRGB values; Unity in a Linear project blends in linear light, which makes soft edges, semi-transparent overlaps and group opacity look paler. Project Settings > FirstSymbol > Vectra > Rendering > **SVG colour blending** chooses: **Browsers and Figma (sRGB)**, the default, or **Linear (Unity)**. With the first, an SVG document is drawn into a texture in sRGB space (colours, gradient stops and blending are the same as in a Gamma project) and the finished picture is shown once on the Canvas, in UI Toolkit or in world space, and written to sprites and PNGs, so every SVG looks as in Figma. `VectraSvgImage`, `VectraSvgRenderer` and `VectraSvgElement` have a **Color blending** override (`ColorBlending`). A Gamma project is not affected and pays nothing; native Vectra shapes (`VectraShape` and the others) are not changed.

The picture is drawn again only when the size, the pixel density, the overrides or the document change, like layers. It costs a render texture the size of the component. What is not in the picture: texts and embedded `<image>` elements are separate objects drawn above it, so a text engine blends its glyph edges in linear light, and an image does not blend with the shapes below it in sRGB space; the picture itself is blended with the scene behind the component in linear light, like all UI (the edges of a translucent document over a scene are slightly paler than in a browser; an opaque document has no such edge).

### SVG support

Every element of SVG 1.1 and SVG 2 that Vectra knows, and what happens to it. **Supported** means drawn or used as the specification says; **Partial** names the difference; **Not supported** means ignored, and the import report (the asset's inspector, **Window → Vectra → SVG Import Report**, `VectraSvgDocument.warnings`) names it. An element that is not in the table is reported as unknown; editor data with a namespace prefix (`sodipodi:`, `inkscape:`) is skipped quietly. A test keeps this table equal to the parser's list (`SvgSupport`).

| Element | Support | Notes |
|---|---|---|
| `<svg>` | Partial | The root's `viewBox`, `width`, `height` and `preserveAspectRatio` set the document. A nested `<svg>` takes `x`, `y`, `width`, `height`, `viewBox`; it does not clip its content (`overflow` is ignored, silently). |
| `<g>` | Partial | A group. `opacity`, `mask` and `mix-blend-mode` on a group (or on a shape that has both a fill and a stroke) make it a compositing layer: drawn offscreen and composited as one (see [Layers](#layers-opacity-masks-blend-modes-background-blur)). Text and images inside get the opacity but not the mask or the blend mode. |
| `<defs>` | Supported | Content is kept for `<use>` and references, never drawn directly. |
| `<symbol>` | Supported | Drawn through `<use>` with `viewBox`, `width`, `height`. |
| `<use>` | Partial | `href` / `xlink:href` to an id in the same document, `x`, `y`, and `width` / `height` for a symbol; the target is restyled for that copy only. A reference to another file is not loaded and a missing id draws nothing (both reported). At most 10 000 copies. |
| `<switch>` | Partial | Draws the first child whose `systemLanguage` / `requiredExtensions` conditions pass (languages: `SvgParseOptions.languages`, default `en`); the other children are skipped (reported). |
| `<a>` | Supported | Drawn as a group; links are not followed. |
| `<title>` | Supported | Read and ignored (nothing to draw). |
| `<desc>` | Supported | Read and ignored. |
| `<metadata>` | Supported | Read and ignored; editor data (`sodipodi:`, `inkscape:`, `rdf:` ...) inside or beside it is not reported. |
| `<style>` | Supported | CSS, see the CSS rows below. |
| `<script>` | Not supported | Never run. Reported. |
| `<view>` | Not supported | Named views are not used. Reported. |
| `<path>` | Supported | All commands, including arcs; data up to 1 000 000 characters. |
| `<rect>` | Supported | `x`, `y`, `width`, `height`, `rx`, `ry`. |
| `<circle>` | Supported |  |
| `<ellipse>` | Supported |  |
| `<line>` | Supported | Strokes only (no fill, as in browsers). |
| `<polyline>` | Supported |  |
| `<polygon>` | Supported |  |
| `<linearGradient>` | Supported | `x1`..`y2`, `gradientUnits`, `gradientTransform`, `spreadMethod`, `href` inheritance. |
| `<radialGradient>` | Partial | As the linear one plus `cx`, `cy`, `r`, `fx`, `fy`; the focal radius `fr` is ignored (reported). |
| `<stop>` | Supported | `offset`, `stop-color`, `stop-opacity`. |
| `<pattern>` | Partial | Fills and strokes: `patternUnits`, `patternContentUnits`, `x`/`y`/`width`/`height`, `viewBox` with `preserveAspectRatio`, `patternTransform`, `href` inheritance, nested patterns, and an `<image>` inside (also through `<use>`; cropped, fitted or rotated pictures included). A pattern that is one picture filling its tile (Figma image fill and tile) uses the picture itself; other content is drawn into a tile texture (4 px per unit, at most 1024 px a side) the first time the document is drawn, and freed with the document. A mesh has one texture, so a pattern with another texture starts a new mesh (as text does). Text inside a pattern is not drawn (reported). |
| `<solidcolor>` | Not supported | SVG 2 draft element that no browser ships. Reported. |
| `<hatch>` | Not supported | SVG 2 draft, as above. Reported. |
| `<hatchpath>` | Not supported | SVG 2 draft. Reported. |
| `<meshgradient>` | Not supported | SVG 2 draft. Reported. |
| `<meshrow>` | Not supported | SVG 2 draft. Reported. |
| `<meshpatch>` | Not supported | SVG 2 draft. Reported. |
| `<clipPath>` | Partial | The union of its shapes, groups and `<use>` (`clip-rule`, `clipPathUnits`, `transform`). A `<text>` inside is ignored (reported); text and images are not clipped by `clip-path`. |
| `<mask>` | Partial | `mask-type` (luminance by default, or alpha, also as `style="mask-type:alpha"`), the `maskUnits="userSpaceOnUse"` region and any content (gradients, opacity) are drawn offscreen at the pixel density of the output. A mask of plain opaque white shapes (opaque shapes in an alpha mask), like Figma's inside-stroke masks, stays a geometric clip. `maskContentUnits="objectBoundingBox"` is applied as a geometric clip (reported); the default bounding-box region of `maskUnits` is not applied; a mask inside a mask is a geometric clip (reported); `<text>` in a mask is ignored (reported). |
| `<marker>` | Supported | `marker-start`, `marker-mid`, `marker-end`, `marker`; `orient`, `markerUnits`, `refX`, `refY`, `viewBox`, `markerWidth`, `markerHeight`, `overflow`. Markers inside a marker are not drawn. |
| `<text>` | Partial | Pieces at their own `x` / `y` (lists too), `dx`, `dy`, `text-anchor` per piece, `font-family`, `font-size`, `font-weight`, `font-style`, `letter-spacing`, `text-decoration` (underline, line-through), `xml:space="preserve"`, `white-space: pre`, a solid `fill` and rotation from the transform (see Text layout). `rotate`, `textLength`, `lengthAdjust`, `word-spacing`, `dominant-baseline`, `baseline-shift`, `writing-mode`, `direction` are ignored (reported); stroked text is not drawn and a gradient fill is drawn black. |
| `<tspan>` | Partial | Own `x` / `y` / `dx` / `dy` (a new piece when `x` or `y` is set), fill, font, weight, style, `letter-spacing` and `text-decoration`; what an engine cannot style is in the table under Text layout. |
| `<textPath>` | Partial | Drawn as ordinary straight text (reported). |
| `<tref>` | Not supported | SVG 1.1, removed from SVG 2. Reported. |
| `<altGlyph>` | Not supported | SVG 1.1. Reported. |
| `<altGlyphDef>` | Not supported | SVG 1.1. Reported. |
| `<altGlyphItem>` | Not supported | SVG 1.1. Reported. |
| `<glyphRef>` | Not supported | SVG 1.1. Reported. |
| `<textArea>` | Not supported | SVG Tiny 1.2 flowed text. Reported. |
| `<image>` | Partial | Embedded `data:` URIs (PNG, JPG); external URLs are not loaded (reported). On its own: an axis-aligned rectangle with opacity, `preserveAspectRatio` is ignored (reported). Inside a `<pattern>`: `preserveAspectRatio` (`meet`, `slice`) and any transform are honoured. |
| `<foreignObject>` | Partial | The HTML and other XML inside is not drawn (reported). Skipped silently, because they are read from elsewhere: Figma's gradient fallbacks (`data-figma-skip-parse="true"` groups; the angular and diamond gradients are built from `data-figma-gradient-fill` / `-stroke` instead) and Figma's background blur (a `<foreignObject>` with `backdrop-filter`, then a shape with `data-figma-bg-blur-radius`; see [Layers](#layers-opacity-masks-blend-modes-background-blur)). |
| `<video>` | Not supported | Reported. |
| `<audio>` | Not supported | Reported. |
| `<canvas>` | Not supported | Reported. |
| `<iframe>` | Not supported | Reported. |
| `<filter>` | Partial | Only through `filter="url(#id)"` (CSS filter functions such as `blur()` are reported). Filter regions, `in`, `in2` and `result` are not followed: the primitives of a filter are read as one effect, as listed below. The chains Figma exports are recognised as a whole (drop shadows with spread, inner shadows with choke, layer blur, noise; several shadows stack in order); a primitive that cannot be placed is reported and left out, the element itself is always drawn. |
| `<feGaussianBlur>` | Partial | The first `stdDeviation` value; becomes a layer blur, or the blur of a shadow (Figma's chains, drawn at 5/6 of it like Figma) or of a drop shadow when an offset or a merge is present. On a group, a shape with fill and stroke or a `<use>` it blurs the picture of the whole subtree. |
| `<feOffset>` | Partial | `dx`, `dy` become the offset of a drop shadow. |
| `<feFlood>` | Partial | `flood-color` and `flood-opacity` become the colour of the drop shadow. |
| `<feMerge>` | Partial | Marks the offset / blur chain as a drop shadow; the merge order is not followed. |
| `<feMergeNode>` | Partial | Not read on its own; `feMerge` is taken as a whole. |
| `<feDropShadow>` | Partial | `dx`, `dy`, `stdDeviation`, `flood-color`, `flood-opacity`. |
| `<feColorMatrix>` | Partial | `type="saturate"` and `type="hueRotate"`; in a Figma shadow chain `type="matrix"` gives the shadow colour and opacity (last row) and `luminanceToAlpha` belongs to the noise; any other matrix is reported. |
| `<feBlend>` | Partial | Closes a Figma shadow; the mode of the shadow is any CSS mode (see [Layers](#layers-opacity-masks-blend-modes-background-blur)): normal draws the shadow natively on a single shape, any other mode composites it through the layer compositor. Inside a mask or a pattern tile (and past the layer limits) a shadow blend mode other than normal is reported and drawn as normal. |
| `<feComponentTransfer>` | Partial | Only inside Figma's noise chain: the `feFuncA` table sets the noise density. |
| `<feComposite>` | Partial | `operator="arithmetic"` with `k2=-1 k3=1` marks a Figma inner shadow, `operator="in"` is part of the noise chain; other operators are reported. |
| `<feConvolveMatrix>` | Not supported | Reported. |
| `<feDiffuseLighting>` | Not supported | Reported. |
| `<feDisplacementMap>` | Not supported | Reported. |
| `<feImage>` | Not supported | Reported. |
| `<feMorphology>` | Partial | In a Figma shadow chain: `dilate` is the spread of a drop shadow, `erode` the choke of an inner shadow; elsewhere reported. |
| `<feSpecularLighting>` | Not supported | Reported. |
| `<feTile>` | Not supported | Reported. |
| `<feTurbulence>` | Partial | Only as Figma's noise effect: becomes a monotone grain (colour, density and size from the chain; an approximation, not the same pattern). |
| `<feFuncR>` | Not supported | Reported. |
| `<feFuncG>` | Not supported | Reported. |
| `<feFuncB>` | Not supported | Reported. |
| `<feFuncA>` | Partial | `type="discrete"` table of Figma's noise chain (density); not read elsewhere. |
| `<feDistantLight>` | Not supported | Reported. |
| `<fePointLight>` | Not supported | Reported. |
| `<feSpotLight>` | Not supported | Reported. |
| `<animate>` | Not supported | No animation: the document is drawn as written, in its first frame. Reported. |
| `<animateColor>` | Not supported | As `animate`. |
| `<animateMotion>` | Not supported | As `animate`. |
| `<animateTransform>` | Not supported | As `animate`. |
| `<set>` | Not supported | As `animate`. |
| `<mpath>` | Not supported | As `animate`. |
| `<discard>` | Not supported | As `animate`. |
| `<handler>` | Not supported | SVG Tiny scripting. Reported. |
| `<listener>` | Not supported | SVG Tiny events. Reported. |
| `<prefetch>` | Not supported | SVG Tiny. Reported. |
| `<font>` | Not supported | SVG 1.1 font or cursor machinery. Reported. |
| `<font-face>` | Not supported | SVG 1.1 font or cursor machinery. Reported. |
| `<font-face-src>` | Not supported | SVG 1.1 font or cursor machinery. Reported. |
| `<font-face-uri>` | Not supported | SVG 1.1 font or cursor machinery. Reported. |
| `<font-face-format>` | Not supported | SVG 1.1 font or cursor machinery. Reported. |
| `<font-face-name>` | Not supported | SVG 1.1 font or cursor machinery. Reported. |
| `<glyph>` | Not supported | SVG 1.1 font or cursor machinery. Reported. |
| `<missing-glyph>` | Not supported | SVG 1.1 font or cursor machinery. Reported. |
| `<hkern>` | Not supported | SVG 1.1 font or cursor machinery. Reported. |
| `<vkern>` | Not supported | SVG 1.1 font or cursor machinery. Reported. |
| `<color-profile>` | Not supported | SVG 1.1 font or cursor machinery. Reported. |
| `<cursor>` | Not supported | SVG 1.1 font or cursor machinery. Reported. |

Properties, CSS and units (a row marked "silent" is ignored without a report):

| Property or feature | Support | Notes |
|---|---|---|
| transform | Supported | `matrix`, `translate`, `scale`, `rotate` (with a centre), `skewX`, `skewY`, in any order. |
| CSS `transform`, `transform-origin`, `transform-box` | Not supported | Only the `transform` attribute works; these are reported. |
| `fill`, `stroke` | Supported | Named colours, `#rgb`, `#rrggbb`, `#rgba`, `#rrggbbaa`, `rgb()`, `rgba()`, `hsl()`, `hsla()`, `currentColor`, `transparent`, `url(#id)` with a fallback colour. |
| `context-fill`, `context-stroke`, `color-mix()`, `lab()`, `oklch()` | Not supported | Black is drawn instead; `context-fill` and `context-stroke` are reported. |
| `fill-rule`, `clip-rule`, `opacity`, `fill-opacity`, `stroke-opacity`, `visibility`, `display` | Supported |  |
| `stroke-width`, `stroke-linecap`, `stroke-linejoin`, `stroke-miterlimit`, `stroke-dasharray`, `stroke-dashoffset` | Supported | `miter-clip` and `arcs` joins are drawn as miter. |
| `vector-effect` | Partial | `non-scaling-stroke` works (see below); `non-scaling-size`, `non-rotation` and `fixed-position` ("at risk" in SVG 2) are ignored and reported. Not inherited, as in browsers. |
| `paint-order`, `isolation` | Not supported | Ignored; reported. Strokes are always drawn over the fill. |
| `mix-blend-mode` | Supported | All 16 modes of CSS Compositing (normal, multiply, screen, overlay, darken, lighten, color-dodge, color-burn, hard-light, soft-light, difference, exclusion, hue, saturation, color, luminosity) and `plus-lighter`, on a group or a shape. It blends with the SVG's own content below, not with the scene behind the component (see Layers). Not inherited. |
| `backdrop-filter` | Partial | Only as Figma writes it: the `data-figma-bg-blur-radius` of the shape that follows the `<foreignObject>` blurs the SVG's own content below, limited to that shape (sigma = radius / 2 * 5/6, like Figma) and only the content inside the shape counts. Anywhere else it is ignored silently. |
| `shape-rendering`, `text-rendering`, `color-rendering`, `image-rendering`, `color-interpolation`, `color-interpolation-filters` | Not supported | Rendering hints; ignored silently (everything is anti-aliased). |
| `font-family`, `font-size`, `font-weight`, `font-style`, `text-anchor` | Supported | See the text engines and the font map. |
| `font-variant`, `font-stretch`, `font` shorthand, `@font-face` | Not supported | Ignored; `font-variant`, `font-stretch` and `@font-face` are reported. |
| `letter-spacing`, `text-decoration`, `xml:space`, `white-space` | Supported | On text; see Text layout for what each engine draws. |
| External style sheets (`<?xml-stylesheet?>`) | Not supported | External style sheets are not loaded. Silent. |
| Units | Partial | `px`, `%`, `pt`, `pc`, `mm`, `cm`, `in`, `em` / `rem` (16 px), `ex` (8 px). Other units (`vw`, `vh`, `vmin`, `ch`) are read as plain numbers. Silent. |
| CSS selectors | Partial | Type, `*`, `.class`, `#id`, `[attr]`, `[attr=value]`, descendant and `>` child combinators, comma lists, specificity, `!important`, the `style` attribute. Pseudo-classes, pseudo-elements, `+`, `~` and `[a^=b]`-style operators never match; the rule is skipped and reported. |
| CSS at-rules and functions | Partial | `@media` rules apply always, whatever the condition (reported); `@import`, `@font-face`, `@keyframes`, `@supports`, `@namespace`, `@charset` are skipped (reported). `var()` and `calc()` are not evaluated (reported); `inherit` works, `initial`, `unset` and `revert` do not (silent). |
| Untrusted files | Supported | Budgets for depth, elements, `<use>` copies, path data and file size; a file that hits one keeps what was read and is reported (see Untrusted files). |

**Non-scaling strokes.** `vector-effect="non-scaling-stroke"` keeps a stroke the same width on screen whatever transforms apply, as in browsers: the `stroke-width` is measured in screen units and not scaled by the element's `transform`, the `viewBox` fit, the Canvas, the object's scale or the camera. Dashes (`stroke-dasharray`, `stroke-dashoffset`) of such a stroke are in screen units too. The parser turns the stroke into a pixel-mode stroke (`StrokeWidthMode.Pixels`, with `dashInPixels`), and one SVG px of it is:

- on a screen-space Canvas, one reference point: `stroke-width="2"` is 2 × the Canvas scale factor device pixels (it matches what a browser does at a device pixel ratio of the same value, and it stays the same size when a Canvas Scaler changes the scale factor); on a world-space Canvas, one device pixel;
- in world space (`VectraSvgRenderer`), one device pixel of the camera that sees it;
- in UI Toolkit (`VectraSvgElement`), one pixel of the panel (a point; at a density of 2, two device pixels);
- in a baked sprite or a PNG, one pixel of the baked texture.

`StrokeWidthMode.Pixels` on a shape (without SVG) still counts device pixels, so a Vectra shape on a Canvas scaled by 2 gets a hairline twice as thin in UI points as the same number in an SVG. The property is not inherited (SVG 2; Chrome draws it the same way): put it on the element itself, in its `style`, or in a CSS rule; a `<use>` takes it from the referenced element, not from the `<use>` or the `<g>` around it. The mesh follows the screen scale (a change of 1% rebuilds it). Exporting a shape with a pixel stroke as SVG writes `vector-effect="non-scaling-stroke"`; the width is written as given.

**Untrusted files.** The parser stops at budgets instead of crashing or hanging: nesting depth 256, 200 000 elements,
200 000 elements drawn (every `<use>` copy counts), 10 000 `<use>` copies, 1 000 000 characters of path data, 32 million
characters per file. A file that hits one keeps what was read so far and gets a warning starting with "SVG limit
reached". `SvgParser.Parse(text, new SvgParseOptions { ... })` changes the budgets.

## World space and Shader Graph

`VectraRenderer` draws a shape with a `MeshRenderer`. The shader follows the render pipeline: **Vectra/World URP** (URP, with
a pass for the 2D Renderer too), **Vectra/World HDRP**, and **Vectra/World** for the Built-in pipeline (and for any other
pipeline). The URP and HDRP shaders keep every property in `UnityPerMaterial` and the per-shape data in the vertices, so the
**SRP Batcher** takes all shapes that share a material in one batch; a shape sets no `MaterialPropertyBlock`. A shape with a
texture paint gets a material of its own per texture, shared by every shape that uses that texture. Check a shader in the
inspector (SRP Batcher: compatible) or with `Window → Analysis → Frame Debugger`.

**Shader Graph (URP and HDRP).** Build materials of your own with Vectra's distance fields, anti-aliasing and paints. The
package ships Custom Function code (`Runtime/ShaderGraph/VectraShaderGraph.hlsl`) and five Sub Graphs under
*Create Node → Vectra*:

| Sub Graph | What it does |
|---|---|
| Vectra SDF Shape | Signed distance of a rectangle, ellipse, star, polygon, cross, rhombus, squircle or capsule |
| Vectra Coverage | Anti-aliased opacity of a distance (Vectra's own edge filter), with a softness |
| Vectra Outline | Outline opacity: centre, inside or outside; width in units or in **screen pixels** (hairline) |
| Vectra Pixel Size | Size of a screen pixel in the units of a distance |
| Vectra Paint | Solid, linear, radial, angular, diamond, stripes, checker, dots, grid and noise paints from a UV |

They use screen-space derivatives, so they work in the fragment stage only. Import the **Shader Graph** sample for *Vectra Card*, an
unlit transparent graph (URP and HDRP targets) that makes a rounded card with a gradient and a 2-pixel hairline outline
from these functions.

## Vector sprites

Bake a shape or an SVG into a `Sprite` for `SpriteRenderer` or a uGUI `Image`: the texture is drawn by the GPU with the same
shaders as on screen, the mesh hugs the opaque pixels (tight), the pivot is set, and a frame can get a 9-slice border taken
from its corner radii. Transparent texels keep the colour of their neighbours, so filtering shows no dark fringe.

```csharp
var options = VectraSpriteOptions.Default;
options.resolution = 256;          // longest side in pixels
options.pixelsPerUnit = 100f;      // 0 = as large in units as the shape
options.autoBorder = true;         // 9-slice border from the corner radii of a rectangle
Sprite sprite = VectraSpriteBaker.Bake(shape, options);   // VectraShape, VectraRenderer, VectraSvgDocument or content + size
```

Edit mode: right-click a Vectra shape → **Bake to Sprite Asset…** or **Bake to 9-Slice Sprite Asset…** (a PNG with the sprite
import settings), or select `.svg` files → **Assets → Vectra → Bake SVG to Sprite**. An SVG can also be imported as a Sprite
directly: the **Vectra Svg Importer** set to *Sprite* has the pivot, 9-slice border, tight mesh and mipmap options. The
sprite grows to include shadows and glows (`includeOverflow`). Unity builds the tight mesh, so its outline is approximate; for an exact collider use the
**Generate Collider** option of `VectraRenderer` or `VectraOutline.GetOutline`.

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
- **Context menu:** Copy / Paste Vectra Style (also between VectraStyle assets), Export as PNG / SVG, Bake to Sprite Asset / 9-Slice
  Sprite Asset, Expand to Hierarchy (SVG Image).
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
- World shapes under URP and HDRP use SRP Batcher compatible shaders (the 0.5.3 one was not). Measured: 500 shapes cost the same CPU time as before in a Windows IL2CPP player (see [Platforms](Platforms.md)); the change is that they now qualify for the batcher (no property block per shape, shared batches with other batcher-compatible objects), not a faster frame at this size.
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
