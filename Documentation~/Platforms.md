# Platforms

What has run where, said plainly. Vectra has no native code, so a platform is about shaders, Burst-compiled code, the graphics API and the render pipeline. A player that builds is not a feature that works, and this page keeps the two apart.

[Русская версия](Platforms.ru.md) · Other guides: [Vectra guide](Vectra.md)

In the tables:

- **Built**: a player for that platform was made with no errors.
- **Run**: a player ran there under a script that checks what a script can see: that every sample scene loads and draws, that markers, paint order and world-space text come out right in a render texture, that the gradient atlas takes the load. Run does not mean anyone looked.
- **Seen by a person**: someone looked at it running there, or used it by hand.

Where a cell says *no*, nobody knows yet. Everything here was done on Unity 6000.4.8f1 with the 0.4 code by one developer agent on one Windows laptop (NVIDIA RTX 3060 and an AMD integrated GPU). Pictures were saved and read by the agent; **no person has looked at any of them yet**.

## How a player is checked

Every platform below is the same project: Vectra, the URP package, TextMesh Pro with its Essential Resources, and the seven samples as scenes. A small script inside the player writes one line per result:

1. **Samples.** Each sample scene is loaded and given ten frames; the player's frame is copied into a render texture, read back and its colours counted. A scene passes when the frame holds more than a dozen colours and the console shows no error.
2. **Markers.** An SVG whose path has an arrow at each end (`marker-start` with `orient="auto-start-reverse"`, and `marker-end`) is drawn in a Canvas into a render texture. Green arrows must be there at both ends.
3. **Paint order.** White text, then a red block over it, in a Canvas. No white may show inside the block.
4. **World text.** The same text in a `VectraSvgRenderer` in world space, drawn by the project's engine (TextMesh Pro 3D, or Lettera where it is installed). White pixels must show.
5. **Gradient atlas.** 4000 different gradients are taken and released, then 4000 more, and the times are logged.

Readback is asynchronous wherever the platform has it, because WebGPU cannot read a texture back at once. Colours are compared by hue, not by value, because HDRP applies exposure and tone mapping.

## Windows

| | Built | Run | Seen by a person |
|---|---|---|---|
| The editor | — | Yes: EditMode and PlayMode in four projects (plain, Lettera, TextMesh Pro, both), see the changelog for the counts | Not in this release |
| Player, x64, Direct3D 12, IL2CPP, URP | Yes | Yes: 7 of 7 samples; markers, paint order, world text and the atlas pass | Pictures read by the agent; no person |
| Player, Mono, Built-in pipeline | Yes | Yes: 6 of 6 samples (the backdrop sample needs URP); the three feature checks and the atlas pass | No |
| Player with Lettera 1.0 installed, IL2CPP, URP | Yes | Yes: 7 of 7 samples; Lettera registers itself in the player and draws the paint-order and world-text checks | No |
| Player, IL2CPP, HDRP | Yes | Yes: 6 of 6 samples; the three feature checks and the atlas pass | No |

## The web

| | Built | Run | Seen by a person |
|---|---|---|---|
| WebGL 2 in Chromium (headless, the full browser in new headless mode, real GPU) | Yes, IL2CPP | Yes: 7 of 7 samples; markers, paint order, world text and the atlas pass | No |
| WebGPU in Chromium (WebGPU as the only graphics API) | Yes | Yes, on an AMD GCN 5 adapter: 7 of 7 samples; markers, paint order, world text and the atlas pass | No |
| Safari, Firefox, a phone browser | — | No | No |

On the web there are no threads, so the Burst jobs run as IL2CPP code on the main thread; the figures below include that.

## Android, iOS, macOS, Linux

| | Built | Run | Seen by a person |
|---|---|---|---|
| Android, ARM64 and ARMv7, IL2CPP | Yes: an APK, built by Gradle | No | No |
| iOS | An Xcode project was exported; it was never compiled by Xcode (no Mac here) | No | No |
| macOS, universal, Mono | Yes | No | No |
| macOS, IL2CPP | No: the editor has no macOS IL2CPP module | No | No |
| Linux x64, IL2CPP | Yes | No | No |

## UI Toolkit

`VectraElement` and `VectraSvgElement` (new in 0.6) were checked on 6000.4.8f1 in the editor and in players. A player script puts a runtime panel into a render texture, reads it back asynchronously and counts pixels:

1. **Orientation**: a plain UI Toolkit element tells which way the readback is turned, then a triangle must point up (a symmetric shape would hide a flipped picture).
2. **Fill, translucency, gradient, anti-aliased edge, drop shadow outside the layout box**: translucency is compared with a plain element of the same colour, because "half transparent" is 188, not 128, in a Linear project.
3. **SVG**: shapes, and white text drawn by UI Toolkit labels.
4. **Twelve small shapes**: they share one page, every one shows.
5. **One large element**: it gets a texture of its own.

| | Built | Run | Seen by a person |
|---|---|---|---|
| The editor, Built-in, Gamma: PlayMode panel into a render texture, EditMode (allocator, UXML, an editor window captured with the editor's own capture) | — | Yes: 29 PlayMode and 10 EditMode tests | No |
| The editor, URP, Linear | — | Yes: the same 29 and 10 | No |
| The editor with Vectra and Lettera as folders under `Assets/Plugins` | — | Yes: the same 29 and 10 | No |
| Player, x64, Direct3D 12, IL2CPP, URP, Linear | Yes | Yes: 12 of 12 checks | No |
| WebGL 2 in Chromium (headless, real GPU), URP, Linear | Yes | Yes: 12 of 12 checks | No |
| WebGPU in Chromium, URP, Linear | Yes | Yes: 12 of 12 checks | No |

Not run: Android, iOS, macOS, Linux, the Built-in and HDRP players, world-space UI Toolkit panels, a real mouse hover. WebGL 2 cannot copy between render textures (`SystemInfo.copyTextureSupport` is `None`); the element draws straight into its page for that reason, and the first web round, which copied, drew nothing there. One rebuild and redraw of a 64-point ellipse took 0.021 ms on Windows, 0.16 ms in WebGL 2 and 0.48 ms in WebGPU (200 calls, one run).

## Render pipelines

| | What Vectra gets |
|---|---|
| Built-in | Everything except the backdrop effects. The six other samples passed the checks in a Windows Mono player. |
| URP | Everything. The backdrop effects (frosted glass and the 15 blend modes) need the **Vectra Backdrop Feature** in the URP renderer; with it the URP Backdrop sample drew correctly on Windows, WebGL 2 and WebGPU. |
| HDRP | Shapes, SVG, masks, effects and world-space shapes draw: the six samples and the feature checks pass in a Windows IL2CPP player, and TextMesh Pro 3D text shows. **No backdrop effects** (they are URP only; the sample is left out). HDRP's exposure and tone mapping apply to world-space shapes as to any lit object in the scene, so a picture there is darker than in URP; Canvas shapes in an Overlay canvas are not touched by them. |
| World shapes, URP (0.6) | `Vectra/World URP` is SRP Batcher compatible: Unity's own check reports the 0.5.3 shader as not (`_Color` outside the buffer) and the new one as compatible. 500 `VectraRenderer` shapes cost 0.67 ms of CPU in `Camera.Render()` in a Windows IL2CPP player (median of 5 runs); the 0.5.3 shader took 0.65 ms and 0.78 ms in two sets of 5 runs. **No gain you can measure at this size**: the 0.5.3 path was already cheap, and the difference is below the noise between sets. Hairline strokes, baked sprites and the Shader Graph sample (a rounded card) pass their tests in the editor. |
| World shapes, HDRP (0.6) | `Vectra/World HDRP` is SRP Batcher compatible by Unity's check and draws in the editor tests (hairline strokes, the Shader Graph sample). Not timed, and no HDRP player was built for 0.6. |

## Numbers

The gradient atlas: 4000 different gradients taken and released (fill), then 4000 more taken and released, each one evicting an older one (churn). One laptop, one run each; not a promise.

| Where | Fill | Churn |
|---|---|---|
| Windows player, IL2CPP, URP | 120 ms (30 µs a row) | 153 ms |
| Windows player, IL2CPP, URP, with Lettera | 120 ms | 142 ms |
| Windows player, IL2CPP, HDRP | 189 ms | 199 ms |
| Windows player, Mono, Built-in | 352 ms | 285 ms |
| WebGL 2, Chromium | 143 ms | 145 ms |
| WebGPU, Chromium | 154 ms | 151 ms |
| Editor, Mono (the test; 4095 rows) | 330 ms (80 µs a row) | 309 ms |

Taking a row that is already there (a cache hit) took 457 ns in the editor on Mono.

## Known risks

- **Nobody has used any of this by hand**, on any platform, in this release.
- **Android, macOS, Linux and iOS were built, not run.** Shaders that compile for a platform can still draw wrongly on its drivers; mobile GPUs in particular were not tried.
- **Web:** only Chromium was run, and WebGPU needs a browser that has it.
- **TextMesh Pro 3D text in HDRP** showed in the check with TextMesh Pro's own shader; other TextMesh Pro materials were not tried.
- The saved sample pictures of the player runs are upside down: the asynchronous readback returns the rows the other way up. The checks count by area and are not affected.
