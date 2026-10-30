---
name: vectra
description: Create, inspect and edit Vectra vector shapes (UI and world), SVG images, styles, palettes, masks and interaction states in the open Unity Editor through "unity command vectra …". Use when the user asks to build or change vector UI, icons, buttons, badges, gradients, shadows or SVG in a Unity project that has the Vectra package.
---

# Driving Vectra from the command line

Vectra registers CLI commands in the `vectra` group (needs the `com.unity.pipeline` package and an open editor).
Every command answers JSON. Edits are undoable in the editor and mark the scene dirty; save with
`unity command scene save` when the user wants the result kept.

## Workflow

1. `unity command vectra help` — command list, JSON conventions and examples.
2. `unity command vectra types` — every shape, effect and paint type with its fields and defaults.
3. `unity command vectra list` — what already exists (paths, ids, shape types).
4. Create or edit, then **check the result visually**:
   `unity command vectra export_png --target <id> --output preview.png` and look at the image.
5. Keep using the `id` (`"#1234"`) returned by `create`/`list` as `--target`; names can be ambiguous.

## Commands

| Command | Purpose |
|---|---|
| `create --shape <type> [--name] [--parent] [--world true] [--size w,h] [--position x,y] [--fill color] [--data patch]` | New UI shape (canvas created when missing) or world `VectraRenderer` |
| `get --target <t> [--full true]` | Description; by default only non-default values |
| `set --target <t> --data <patch>` | Patch geometry, fills, strokes, effects, size, position, rotation, color, style, shapeAsset, blendMode, quality, name, active |
| `add --target <t> --list fills\|strokes\|effects [--type <effect>] [--data <element>]` | Append a layer |
| `remove --target <t> --list … --index <i>` | Remove a layer |
| `mask --target <t> --mode stencil\|soft [--softness] [--inset] [--invert] [--show_graphic] [--remove true]` | Mask children by the outline |
| `states --target <t> [--data {...}] [--remove true]` | Hover / pressed / selected / disabled looks |
| `convert_to_path --target <t>` | Editable Bezier path with the same outline |
| `save_style --target <t> --path Assets/…asset`, `save_shape …` | Shared style / shared geometry assets |
| `palette --path Assets/…asset [--themes Light,Dark] [--data {"Accent":["#3B82F6","#93C5FD"]}]` | Named colours per theme |
| `theme --palette <asset> --theme Dark` | Switch theme (adds a VectraTheme to the scene) |
| `svg_parse --svg "<svg…>" --save Assets/…asset [--create true]` | SVG markup → document asset (+ image) |
| `svg_create --asset <svg or document path>`, `expand_svg --target <t>`, `use_importer --path <file or folder>` | SVG images |
| `export_png --target <t> --output <file> [--scale 2]`, `export_svg …` | Previews and exports |
| `select --target <t>` | Select and frame the object for the user |
| `stats`, `reset_atlases` | Diagnostics |

## JSON patches (`--data`)

Relaxed JSON: unquoted keys and single quotes are accepted, e.g. `{geometry:{$type:Star,points:6}}`.

- Colours: `"#RRGGBB"`, `"#RRGGBBAA"`, names (`"white"`) or `[r,g,b,a]` (HDR above 1).
- Vectors `[x,y]`, rects `[x,y,w,h]`, enums by name (`"Outside"`, `"LinearGradient"`), assets by path.
- Geometry and effects are polymorphic: give `"$type"` (`"Star"`, `"DropShadow"`); a bare string switches type with defaults.
- An array replaces a list; an object with indices patches elements: `{fills:{"0":{paint:{color:"#fff"}}}}`.
- Gradients: `{stops:[{position:0,color:"#f00"},{position:1,color:"#00f"}]}` or the shorthand `"#f00,#00f"`.
- Shapes linked to a shared style or shape asset edit that asset (every user changes) — detach first when only one object should change (`set --data {style:null}`).

## Recipes

```bash
# Card with gradient, inner stroke and shadow
unity command vectra create --shape Rectangle --name Card --size 320,180 \
  --data "{geometry:{cornerRadii:[24,24,24,24]},fills:[{paint:{type:LinearGradient,gradient:'#6366F1,#EC4899',gradientStart:[0,1],gradientEnd:[1,0]}}],strokes:[{paint:{color:'#FFFFFF60'},width:2,alignment:Inside}],effects:[{$type:DropShadow,blur:24,offset:[0,-8]}]}"

# Icon button with hover/press states
unity command vectra create --shape Ellipse --name PlayButton --size 96,96 --fill #22C55E
unity command vectra add --target PlayButton --list effects --type Outline --data "{color:'#22C55E80',width:2,offset:6}"
unity command vectra states --target PlayButton --data "{hover:{scale:1.06},pressed:{scale:0.94}}"

# Progress ring at 70 %
unity command vectra create --shape Arc --name Progress --data "{geometry:{innerRadius:0.82,sweepAngle:360},effects:[{$type:FillAmount,amount:0.7}]}"

# Soft-masked image area
unity command vectra mask --target Card --mode soft --softness 24
```

Useful types: shapes `Rectangle Ellipse Arc Polygon Star Triangle Line Cross Heart Arrow Chevron Squircle Gear Path
Polyline SvgPath TracedSprite Boolean Morph`; effects `DropShadow InnerShadow OuterGlow InnerGlow Bevel LayerBlur
Grain ColorAdjust FillAmount Dissolve Pixelate Outline ColorOverlay GradientOverlay Extrude Deform`; paints
`Solid LinearGradient RadialGradient AngularGradient DiamondGradient ContourGradient Texture Stripes Checker Dots Grid
Noise Freeform`. Run `types` for their fields.
