# Performance

What Vectra costs, measured with the harness in `Perf~/` (see its README to run it yourself).

## How it was measured

Windows 11, Ryzen 7 4800H, RTX 3060 Laptop, D3D12, 1280x720, vSync off. Unity 6000.4.8f1, URP 17.4, Linear colour space. Release players with IL2CPP, median of 5 launches; allocation and draw counters from development players. The reference is plain uGUI `Image` (sliced sprites, no analytic anti-aliasing, dashes or gradients), `SpriteRenderer` for world space, a UI Toolkit `VisualElement` with border radius, and `com.unity.vectorgraphics` for SVG. The shape mix is a rounded rectangle, an ellipse, a star, a rounded rectangle with a dashed stroke and one with a gradient, 20 px each.

## Figures

| Row | 0.7.5 | 0.8.0 | Reference |
|---|---:|---:|---:|
| Create 1,000 Canvas shapes, first frame | 522 ms | 101 ms | 56 ms |
| Rebuild 1,000 shapes (size changes every frame) | 28.7 ms | 14.4 ms | 12.4 ms |
| Recolour 100 shapes a frame | 3.79 ms | 2.16 ms | 1.81 ms |
| A frame of 1,000 unchanging shapes, main thread | 1.07 ms | 0.73 ms | 0.59 ms |
| 200 tweened shapes, main thread | 1.97 ms | 1.75 ms | 1.02 ms |
| Reactivate 1,000 shapes | 27.4 ms | 21.4 ms | 19.4 ms |
| 5,000 world shapes, a frame, main thread | 7.03 ms | 1.96 ms | 2.54 ms |
| 300 SVG icons (sRGB blending in Linear), first frame | 218 ms | 53 ms | 23 ms |
| Their pictures in memory | 1.83 MB | 0.05 MB | none |
| 100 smooth booleans, rebuild | 124 ms | 34 ms | none |
| Resize 1,000 UI Toolkit elements | 43 ms | 16 ms | 9.1 ms |
| Memory added by 1,000 UI Toolkit elements | 135 MB | 57 MB | 25 MB |
| Memory added by 1,000 Canvas shapes | 56 MB | 40 MB | 15 MB |
| Memory added by 5,000 world shapes | 303 MB | 259 MB | 109 MB |
| Parse 42 Figma SVGs | 6.6 ms | 7.0 ms | 14.6 ms |
| Sprites of 42 Figma SVGs | 300 ms | 275 ms | 764 ms |

Steady frames allocate nothing, including boolean rebuilds and UI Toolkit resizes. The package adds about 3.2 MB to an IL2CPP player.

## Known costs

- A dashed stroke is drawn as one trapezoid per dash segment (128 triangles for a 20 px dashed rectangle). 200 dashed shapes are most of the GPU time of a frame of 1,000 shapes (1.19 ms against 0.18 ms for uGUI); without them Vectra matches uGUI on the GPU.
- Deactivating 1,000 Canvas shapes costs about 1 ms more than in 0.7.5 (5.4 against 4.6 ms; uGUI `Image`: 5.1 ms), because Canvas shapes share one worker mesh, as uGUI's own graphics do. The shared mesh is what saves 16 MB per 1,000 shapes.
- Creating world shapes and SVG icons is still several times slower than creating sprites: most of it is the engine's `AddComponent` and the first build of each mesh.
