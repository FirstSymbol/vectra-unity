# Vectra — vector graphics for Unity

*Русская версия: [README.ru.md](README.ru.md).*

Vector shapes for the **uGUI Canvas**, **world space** and **UI Toolkit**. Includes:
- analytic SDF primitives and arbitrary paths;
- boolean operations, including smooth ones;
- gradients, patterns, strokes with dashes and trim;
- shadows, glows, bevels and other effects;
- masks, palettes with themes, animation and interaction states;
- shapes traced from sprites and a shared shape library;
- SVG import and export, with text, markers and paint order;
- SRP Batcher world shaders for URP and HDRP, Shader Graph nodes, hairline strokes and baking to sprites.

Everything is drawn with one shared material, so shapes batch. Geometry is computed by Burst and rebuilding does not allocate managed memory.

- **Unity:** 6000.3+ (6.3 LTS).
- **Dependencies:** Burst, Collections, Mathematics, uGUI. SVG text is drawn by Lettera or TextMesh Pro when they are installed, otherwise by the built-in uGUI Text (chosen in Project Settings → Vectra).
- **Pipelines:** Built-in, URP, HDRP. Backdrop effects work in URP only.
- **Platforms:** Windows, macOS, Linux, WebGL, WebGPU, iOS, Android — all with Shader Model 3.5. No compute shaders. On WebGL the Burst code runs as ordinary IL2CPP. What has actually been built and run where is on the [Platforms](Documentation~/Platforms.md) page.

Full guide: [Documentation~/Vectra.md](Documentation~/Vectra.md); in Russian: [Documentation~/Vectra.ru.md](Documentation~/Vectra.ru.md).

## Installation

Package Manager → **+** → **Add package from git URL...**

```
https://github.com/FirstSymbol/vectra-unity.git
```

A specific version is installed through its tag: `https://github.com/FirstSymbol/vectra-unity.git#0.6.2`.

Vectra and Lettera work as packages or as plain folders under `Assets`: when Lettera is a folder, Vectra notices it and adds `VECTRA_LETTERA` to the scripting define symbols by itself (and takes it away when the folder goes). A symbol you set yourself is left alone. If you delete Lettera while the editor is closed, Unity opens with compile errors (or Safe Mode): remove `VECTRA_LETTERA` in **Project Settings > Player > Scripting Define Symbols**, or put the folder back and delete it from inside Unity.

## Quick start

- **GameObject → UI → Vectra → …** — a shape on a canvas (the canvas is created for you).
- **GameObject → 2D Object → Vectra → …** — a shape in the world (`VectraRenderer`).
- Drop an `.svg` into the project. To have Vectra import it, pick the **Vectra Svg Importer** in the asset's inspector or turn on **Project Settings → Vectra → Use Vectra SVG importer**. Then create **GameObject → UI → Vectra → SVG Image**.
- **Samples** (Package Manager → Vectra → Samples):
  - Shapes Gallery;
  - SVG Showcase;
  - Effects & Masks;
  - Boolean Operations;
  - Animation;
  - World Space;
  - UI Toolkit;
  - URP Backdrop;
  - Shader Graph.

## Components

| Component | Purpose |
|---|---|
| `VectraShape` | A uGUI shape (`MaskableGraphic`). Works with Mask, RectMask2D (with softness), layout and exact raycast. |
| `VectraRenderer` | A shape in the world: MeshRenderer, sorting layer and order, an optional `PolygonCollider2D`. |
| `VectraElement` / `VectraSvgElement` | The same shapes and SVG documents in UI Toolkit (UXML, USS, runtime and editor panels), drawn into a shared render texture so every effect is kept. |
| `VectraSvgImage` / `VectraSvgRenderer` | An SVG document as one mesh per run of shapes. Text is drawn by the chosen text engine (Lettera, TextMesh Pro or Legacy; in world space Lettera or TextMesh Pro), embedded images through RawImage. Colour overrides by `#id`, `.class` and tag, and expanding into a hierarchy of shapes. |
| `VectraBooleanGroup` (+ `VectraBooleanOperand`) | Child shapes become the operands of a boolean operation. The hierarchy sets the order. |
| `VectraMask` | A mask that follows a shape's outline. Stencil works for any UI. Soft has feather and invert, for Vectra children. |
| `VectraTheme` (+ `VectraPalette` asset) | Named colours by theme. Switching the theme recolours every shape that uses palette tokens. |
| `VectraAnimatedProperties` | Fields for Animator and Timeline keys: trim, colours, width, rotation, fill amount, dissolve, morph. |
| `VectraInteractionStates` | Hover, pressed, selected (focus) and disabled: tint, scale, colours, effect strength, smooth transitions. |
| `VectraShapeAsset` (asset) | Shared geometry for many objects: a shape library. |
| `VectraBackdrop` (URP) | Frosted glass and 15 advanced blend modes. Needs `VectraBackdropFeature` in the URP Renderer. |
| `VectraStyle` (asset) | A shared set of fills, strokes and effects, like a CSS class. |

## Shapes

**Analytic:** an exact distance function, perfect anti-aliasing at any scale, nearly free effects.
- **Rectangle:** its own radius per corner; corner style Round, Chamfer, Inverted, Notch or Squircle.
- **Basic:** ellipse, ring, arc, pie, polygon, star, triangle, line.
- **Other:** cross, heart, diamond, trapezoid, parallelogram, crescent, egg, vesica, superellipse, flower, arrow, chevron.

**Path shapes:**
- **Outlines:** a Bezier path (edited in the Scene view; points Corner, Smooth, Symmetric), a polyline with rounded corners, spiral, gear, wave, an SVG path (`d`), a speech bubble with a tail, a checkmark.
- **Compound:** a boolean group (exact or smooth), a morph between any two shapes.
- **From sprites:** a sprite's silhouette as an editable outline (alpha threshold, resolution, simplification, smoothing).

Any shape, SVG path or traced sprite can be turned into an editable Bezier path (**Convert to editable path**).

Every shape has `rotation`. An analytic shape can always be drawn as a path too: the `Force Path` flag, or automatically when a dashed stroke needs it.

## Fills, strokes, effects

- **Paint:**
  - solid (HDR);
  - gradients linear, radial (with a focal point), angular, diamond: spread pad/repeat/reflect, unlimited stops, midpoint, interpolation sRGB/Linear/OKLab/Step, dithering against banding;
  - texture (stretch, fit, fill, tile);
  - patterns stripes, checker, dots, grid;
  - noise;
  - a freeform gradient from colour points;
  - contour gradient: a gradient and stripes that follow the shape (fade to the edge, rings).

  A colour can be taken from a palette by name.
- **Strokes:** any number; align center/inside/outside; caps; joins with miter limit; dashes (absolute or relative lengths, offset); trim path; a width profile `AnimationCurve`; a "behind the fill" mode.
- **Effects:**
  - drop shadow, inner shadow;
  - outer glow, inner glow;
  - bevel & emboss, layer blur, noise/grain;
  - color adjust (HSBC);
  - fill amount (horizontal/vertical/radial), dissolve, pixelate;
  - outline (from the distance field);
  - color overlay, gradient overlay;
  - extrude (silhouette layers);
  - deform (animated deformation on the GPU).

  Write your own effects by inheriting `VectraCustomEffect`.

  Every layer has blend Normal or Additive. The whole shape can use Normal, Additive, Multiply or Screen.

## Code

```csharp
var shape = gameObject.AddComponent<VectraShape>();
var star = shape.SetGeometry<StarShape>();
star.points = 6;
shape.SetFill(VectraPaint.Linear(Color.yellow, Color.red, 90f))
     .SetStroke(Color.white, 3f, StrokeAlignment.Outside);
shape.AddEffect<DropShadowEffect>().blur = 12f;

// animation with tweens
shape.TweenTrim(0f, 1f, 0.6f).SetEase(VectraEase.OutCubic).SetLoops(-1, true);

// or by hand: change the parameters and mark the shape dirty
shape.Strokes[0].trimEnd = Mathf.PingPong(Time.time, 1f);
shape.SetShapeDirty();

// palette: switching the theme recolours shapes that use tokens
shape.Fills[0].paint.colorToken = "Accent";
theme.SetTheme("Dark");

// SVG at runtime
svgImage.Document = VectraSvg.Parse(svgText);
VectraSvg.Expand(svgImage);   // one VectraShape per element
```

Utilities:
- `VectraRasterizer.Render(...)` — a shape or SVG into a `Texture2D`.
- `SvgWriter.Write(...)` — export to SVG.
- `VectraOutline.GetOutline(...)` — outlines for colliders.
- `VectraGeometry` — the low-level Burst API: flatten, tessellate (fill rules and N-operand booleans), stroke, SDF, contours.

## Editor

- **Inspector:**
  - a grid of shape types (green are analytic, orange are path);
  - cards for fills, strokes and effects: visibility, drag by "⋯", delete;
  - a gradient editor (click the bar to add a stop, with presets);
  - a palette colour picker;
  - styles (Save/Detach) and mesh statistics.
- **Scene view:** the **Edit Vectra Shape** tool edits:
  - path points and tangents: "+" in the middle of a segment adds a point, Ctrl+click removes it;
  - polyline points;
  - corner radii (Shift — one corner);
  - arc angles;
  - gradient handles and the shadow offset.

  The tool's overlay switches the point mode, turns on grid snapping and the handles.
- **Context menu:** Copy/Paste Vectra Style, Export as PNG / SVG, Bake to Sprite Asset / 9-Slice Sprite Asset, Expand to Hierarchy (SVG Image).
- **Windows:** Window → Vectra → Atlas Debugger and SVG Import Report.
- **CLI for scripts and AI agents** (with `com.unity.pipeline` installed): `unity command vectra help | types | list | get | create | set | add | remove | mask | states | palette | theme | svg_parse | export_png …`.
  - Input and output are JSON, edits are undoable.
  - **Tools → Vectra → Install Claude Code Skill** puts a skill with instructions for Claude Code into the project.

## URP backdrop

Add the **Vectra Backdrop Feature** to the URP Renderer asset. It captures before transparent objects. That is why glass over other UI is built with a camera stack: the base camera draws the background and an overlay camera draws the glass. The URP Backdrop sample is built this way.

## SVG limits

The main gaps are below; every SVG element, property and CSS feature is listed as Supported, Partial or Not supported in [SVG support](Documentation~/Vectra.md#svg-support).

- `<mask>` is applied as a geometric clip; the mask's luminance is ignored.
- Text inside a `<pattern>` is not drawn; other pattern content (images, shapes, gradients, nested patterns) is.
- Of the filters, blur, drop shadow (including the offset + blur + merge chain), saturate and hueRotate are supported; the other primitives (`feTurbulence`, `feComposite`, `feBlend`, ...) are not.
- No animation: SMIL (`<animate>`, `<animateTransform>`, `<set>`) and CSS `@keyframes` are ignored; the first frame is drawn. `<foreignObject>` and `<script>` are ignored.
- No `mix-blend-mode`, `paint-order` or `@font-face`.
- Text is one run per `<text>`: no per-glyph positions (`dx`, `dy`, `rotate`, `x` lists), `<textPath>`, `textLength` or `letter-spacing`.
- `vector-effect` works for `non-scaling-stroke` only (Canvas, world space, UI Toolkit, baked sprites and PNG, and the SVG export; see the guide). `non-scaling-size`, `non-rotation` and `fixed-position` are ignored.
- A group's `opacity` is applied to each child separately.
- External images by URL are not loaded; only embedded data URIs work.
- Anything unsupported is written to the import report (the asset's inspector and Window → Vectra → SVG Import Report); every element that is not supported is named there (a test keeps the list and the guide equal).

## Performance

- A rebuild happens only on change. Tessellation, strokes and SDFs are cached by hash.
- The gradient atlas (RGBAHalf) and the SDF atlas (RHalf) are shared by all shapes, which is why everything has one material. Gradient rows are shared by content and the least recently used are dropped when the atlas is full.
- Baked SDFs exist only for path shapes with effects and for soft masks. Analytic shapes need no textures.
- The SDF atlas size is set in Project Settings → Vectra; how full it is shows in the Atlas Debugger.

## License

MIT, see [LICENSE.md](LICENSE.md).
