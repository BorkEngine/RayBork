# RayBork API Reference

Status: **v0.1 M7**. M0 ships `EngineVersion`; M1 ships the app host, dedicated tick thread, SDL3
window/input/gamepads, time/pacing, and logging surface listed in §1.6. M2 ships the renderer
foundation: `EngineInfo`, `PresentMode`, `RenderStats`, `TonemappingMode`, the renderer builder
options, the graphics exceptions, and `EngineContext.Info`/`Stats`, backed by the Vulkan 1.2
backend. M3 ships the asset pipeline: `IAsset`, `IAssetCatalog`, `Mesh`, `Texture`, `Shader`, the
procedural `MeshBuilder`/`TextureBuilder` API, the asset and shader metadata value types, the asset
exceptions, pack mounting and lookup, custom TOML data through `WithAssetType<T>()`, `RayBork.Bake`,
and the build-time built-in pack. M4 ships the core 3D renderer: `Camera`, `CameraProjection`,
`ClearMode`, `Model`, `ModelPart`, `Ray`, `BoundingFrustum`, the camera draw methods,
`Shader.SetUniform`/`ResetUniforms`, and the shader ABI, backed by the bindless texture table,
instanced batches, pipeline and sampler caches, and the HDR AgX present path. M5 ships the primitive surface and the collision queries: the line,
point, and triangle helpers, the twelve shape helpers with their shape/position/tessellation,
start/end, and `Transform` overloads, the eight `DrawBillboard` overloads, `DrawWireModel`, the
engine-owned primitive meshes and the built-in `/shaders/primitive` program, `Collision`,
`RayCollision`, `GetRayQuad`, `MeshBuilder.GenerateCollisions`, `Mesh.HasCollisionAcceleration`, the
baked `RBVC` collision payload, and the nine `MeshBuilder.Generate*` factories. M5 also ships the
runtime tone-mapping surface: `TonemappingSettings` behind `EngineContext.Tonemapping`, with
`WithAgxWhite` and `WithAgxContrast` as startup defaults, and the `Color.FromSrgb`,
`Color.FromSrgbBytes`, and `Color.ToSrgb` helpers. The M5 present pass replaces the M4 AgX curve and
its separate HDR highlight path with Godot 4.7's HDR-aware AgX curve: SDR uses an output maximum of
`1`, scRGB HDR uses the reported display headroom, and the curve output is scaled by paper white.
M6 ships the
font and text surface: `Font` and `Glyph`, `Load<Font>`, `TryGetGlyph`, `Measure`, on-demand
asynchronous MSDF glyph generation, the built-in Fira Sans Regular default font and the JetBrains Mono
variable monospaced font (default instance Regular) exposed as `EngineDefaults.DefaultFont` and
`EngineDefaults.DefaultMonoFont`, and the world-space `Camera.DrawText` and
`Camera.DrawTextBillboard` draws. The text alignment and wrapping enums and the text draw contract are
described in §7.10. The M6 Native AOT gate was `samples/Text`, which loads the built-in faces and a
baked custom face and draws the world-space text scene. M7 ships the orthographic and screen-space
surface: the unified `EngineContext.CreateCamera(Transform, CameraProjection, fieldOfView,
orthographicHeight, ...)` entry point, `Camera.ScreenToWorldTransform`, the world-space
`Camera.DrawQuad` (solid color, full texture, and normalized source rectangle), `Camera.DrawDisc`,
and `Camera.DrawGrid(Transform, ...)` primitives, and the ninth primitive mesh, the unit disc.
`DrawTexture` is not shipped, there is no 2D or screen-space draw overload, and `DrawText` remains
world-space. The M7 Native AOT gate is `samples/Pong`, an orthographic player-vs-AI match drawn
through `ScreenToWorldTransform` with `DrawQuad`, `DrawDisc`, and `DrawText`. The rest of the API
reference remains the 1.0 contract, not a description of shipped behavior.

The API is intentionally raylib-shaped: a small set of concrete types, a frame-scoped draw API,
and no mandatory scene framework. The runtime shape and renderer seam are described in
[architecture.md](architecture.md); the delivery plan is in [roadmap.md](roadmap.md).

## Document map

This page keeps [§1 Scope and conventions](#1-scope-and-conventions) and
[§2 Quick start](#2-quick-start). The rest of the reference is split into the focused pages
below. Section numbers are stable, so a `§7.3` reference names the same section in any page.

| Page | Contents |
| --- | --- |
| [Lifecycle](api/lifecycle.md) | §3 `IGame`, `RayBorkAppBuilder`, `RayBorkApp`, `EngineContext`, and the startup/shutdown sequence. |
| [Time and frames](api/time.md) | §4 time members, minimized/unfocused behavior, and frame lifetime. |
| [Space, math, and color](api/math.md) | §5 coordinate system, `Transform`, `Color`, `Rect`, rays/bounds/collision, and vector/quaternion extensions. |
| [Assets](api/assets.md) | §6 `IAsset`, `IAssetCatalog`, asset paths and types, lifetime, the load pipeline, and pack mounting. |
| [Rendering: camera](api/rendering/camera.md) | §7, §7.1-7.2 `Camera`, projection, clear, HDR, and tone mapping. |
| [Rendering: shaders](api/rendering/shaders.md) | §7.3-7.4 `Shader`/`SetUniform` and the shader ABI. |
| [Rendering: meshes](api/rendering/meshes.md) | §7.5 `Mesh`, `Vertex`, `MeshBuilder`, and `Model`. |
| [Rendering: textures](api/rendering/textures.md) | §7.6 `Texture`, `TextureBuilder`, samplers, and formats. |
| [Rendering: render state](api/rendering/render-state.md) | §7.7 `RenderState`. |
| [Rendering: draws](api/rendering/draws.md) | §7.8-7.9 draw calls, batching, primitives, lines, and billboards. |
| [Rendering: text](api/rendering/text.md) | §7.10 fonts and text. |
| [Rendering: render targets](api/rendering/render-targets.md) | §7.11-7.12 render targets, multiple cameras, and render statistics. |
| [Rendering: post-processing](api/rendering/post-processing.md) | §7.13 camera post-process stacks, custom post shaders, target-sized chains, and viewport store-back. |
| [Window](api/window.md) | §8 `IWindow`, `WindowState`, `CursorMode`, `Monitor`, and `PresentMode`. |
| [Input](api/input.md) | §9 keyboard and text, mouse, and gamepads. |
| [Diagnostics](api/diagnostics.md) | §10 logging, `EngineVersion`, `EngineInfo`, exceptions, and assertions. |
| [Threading and lifetime](api/threading.md) | §11 thread ownership, join rules, disposal, shutdown order, and multiple apps. |
| [Native AOT](api/aot.md) | §12 Native AOT and trimming requirements. |
| [Asset tooling](api/tooling.md) | §13 `RayBork.Bake`, TOML data, processors, pack v1, incremental builds, and validation. |
| [Versioning and appendices](api/versioning.md) | §14-15 versioning, compatibility, the out-of-scope list, and Appendix A/B. |

## 1. Scope and conventions

### 1.1 Targets

| Item | Value |
| --- | --- |
| Runtime | .NET 11 (`net11.0`), Native AOT for games |
| GPU API | Vulkan 1.2 backend; the renderer is replaceable behind an internal seam for future mobile/web backends |
| Platform | Windows x64 and Linux x64 in v1; WSL is a Linux build environment only and its hardware Vulkan path is not a target. Mobile and web are future targets. |
| Windowing / input | SDL3 |
| Sound | SDL3, deferred to a post-1.0 milestone |
| Logging | `Microsoft.Extensions.Logging` with ZLogger; `EngineContext` exposes `ILoggerFactory` |
| Color / output | Linear float color, HDR rendering, AgX tone mapping, sRGB output fallback |
| Public assembly | `RayBork.Engine` |
| Tooling assembly | `RayBork.Bake` (JIT, build-time only) |
| Test stack | xUnit v3, Moq; no golden-image suite |
| License | [MIT](../LICENSE) |

### 1.2 Namespace and naming

Everything game-facing is in the flat `RayBork` namespace. Internals are in `RayBork.Internal`.
The bake tool is in `RayBork.Bake`.

```csharp
using RayBork;
using System.Numerics;
```

Types are PascalCase, methods are PascalCase, parameters and locals are camelCase. Constants are
`SCREAMING_SNAKE_CASE`. Async methods that are not event-like end in `Async`; `IGame.Setup` and
`IGame.Shutdown` are the deliberate exception because they are lifecycle hooks, not general-purpose
async APIs.

Source style uses K&R braces: the opening brace stays on the declaration line, and `else`, `catch`,
and `finally` stay on the closing-brace line.

### 1.3 Units and space

| Quantity | Convention |
| --- | --- |
| World unit | 1 unit = 1 meter |
| World axes | Right-handed, +Y up, **-Z forward**, +X right |
| Transform | World-space TRS; `WithParent` composes parent-local into world |
| Matrix | `System.Numerics.Matrix4x4`, row-vector convention |
| Time | `double` seconds; monotonic `Stopwatch` |
| Angle | User-facing RayBork APIs use **degrees**; raw `System.Numerics` rotation methods use radians |
| Color | Linear float RGBA, HDR values allowed; AgX maps to the output |
| Screen space | Top-left origin, pixels; `Input.MousePosition`/`Input.MouseDelta`, cameras, and viewports use physical render pixels; `Window.Width/Height` and `SetSize` are window coordinates |
| Depth | Reverse-Z, `[0,1]`, clear `0.0`, default compare `GreaterOrEqual` |

The engine flips Vulkan's Y-down NDC internally. World space stays Y-up. Public `gl_Position`
examples in the shader ABI section already include the frame matrix, so custom shaders do not
need to know about the flip.

### 1.4 Threading notation

RayBork documents thread affinity per member because most bugs in a threaded renderer are lifetime
bugs, not rendering bugs.

| Marker | Meaning |
| --- | --- |
| **Tick** | Must be called on the tick thread, from `Setup`, `Tick`, or `Shutdown`. |
| **Any** | Thread-safe; may be called from any thread. |
| **Main** | Must be called on the process main thread; only `RayBorkApp.Run` normally does this. |
| **Render** | Internal; never called by game code. |

Unless marked otherwise, a mutable object (`Shader`, `Camera`) is tick-only.

### 1.5 Error model

- Invalid arguments and lifecycle violations throw standard exceptions:
  `ArgumentNullException`, `ArgumentException`, `InvalidOperationException`,
  `ObjectDisposedException`.
- Asset, graphics, and platform failures throw `RayBorkException` subclasses with a stable
  `DiagnosticCode`.
- `Load<T>` throws on missing assets and type mismatches. There is no `TryLoad` in v1; catch
  `AssetNotFoundException` when absence is expected. `LoadData` returns bytes.
- Engine callbacks (`Setup`, `Tick`, `Shutdown`) may throw. `Run` stops the app, runs the shutdown
  path, and rethrows a `RayBorkException` whose `InnerException` is the callback exception.
- Recoverable renderer failures are retried without stopping the app: swapchain out-of-date, resize,
  focus changes, and device loss where the backend can recreate the device and swapchain. The
  recovery is logged at Warning, or at Information for a suboptimal-swapchain hint; the app stops
  and rethrows only when recovery fails. Recovery is bounded to three consecutive recoveries
  without a completed frame. `GraphicsDeviceLostException` means recovery failed or the bound was
  reached.
- Logging is structured through `Microsoft.Extensions.Logging`; the default provider is ZLogger.

### 1.6 Milestone availability

M0 ships `EngineVersion`. M1 ships the host, thread, window, input, time, and logging surface. M2
ships the renderer foundation: the Vulkan 1.2 backend behind the internal seam, swapchain selection
with HDR/SDR output, the scRGB HDR present path, AgX tone mapping, MSAA with color resolve,
reverse-Z depth, bounded recovery, and the GPU tests. M3 ships the asset pipeline and pack; M4 ships
the core 3D renderer, camera draws, and shader ABI; M5 ships the primitive, billboard, wireframe, and
collision surface plus the HDR AgX rework; M6 ships the managed font parser, on-demand MSDF glyph
pipeline, layout, built-in default and monospaced fonts, and world-space text draws; M7 ships the
unified camera entry point, `Camera.ScreenToWorldTransform`, the world-space quad, disc, and
transformable-grid primitives, the ninth primitive mesh, and the Pong sample. M8 ships shader ABI v2
(one frame block per camera pass), one render pass per camera pass on the scene target, and the color
and depth paths of public render textures: `RenderTexture` creation, `Camera.Target` routing, MSAA color
and depth resolve, the sampled `ColorTexture` and `DepthTexture` views, same-tick sampling, lifetime,
`RenderTexture.Resize`, and render-target color readback through `RenderTexture.ReadPixelsAsync`
(display-referred RGBA8 for screenshots) and `RenderTexture.ReadColorsAsync` (raw scene-linear HDR
colors). The scene target's resolved depth image is internal. M8 also ships the extensible camera
post-process core: `PostProcessStack`, `PostProcessEffect`, `ShaderPostProcessEffect`, the
`CreateCamera` `postProcess` parameter, target-sized chains over the engine fullscreen quad, the
reserved `sourceColor`/`sourceDepth` custom-shader contract, and viewport store-back on the scene
target and on a `RenderTexture` target; the post-processed output is what a later sample and both
readback methods observe. Built-in SSAO and bloom effects and the render-target samples remain later
M8 tasks. The remaining rows name the milestone that owns the deferred surface; the
full code blocks in the section pages stay the 1.0 contract.

**M8 readback contract.** `RenderTexture.ReadColorsAsync` returns raw scene-linear `Color[y, x]` values
(top-left origin, first dimension height, alpha included, HDR preserved) and never applies AgX or sRGB
encoding. `RenderTexture.ReadPixelsAsync` returns display-referred RGBA8 in row-major top-left order
with length `width * height * 4`, after the exposure, AgX white, and AgX contrast of the last completed
frame that wrote the target, the app's startup `TonemappingMode`, AgX, and the sRGB encode; alpha is
`255` and is not premultiplied. Both read the resolved single-sample color image, not depth, after the
last completed frame that wrote or sampled the target. A target that no frame wrote has undefined GPU
content; rather than reading undefined memory, both methods return the defined opaque-black default
(raw `Color.Black` and RGBA8 `(0, 0, 0, 255)`) with the default tone settings on the pixels path. This
includes a target that a frame only sampled, because sampling moves its image out of the undefined
layout without writing it. Both are legal from `Setup` and `Tick` and
stall the render thread while they wait for the target's last-use fence and copy the image, so they are
for screenshots and tests, not per-frame work; a `Tick` caller blocks on the returned task. Cancellation
before the render thread completes the call is `OperationCanceledException`; cancellation after the
request is queued is best effort and never corrupts the target. A disposed target, a stopped app, or a
stopped render texture service throws `ObjectDisposedException`. Raw linear HDR color readback is part
of the public API; it is not deferred to post-1.0.

| Surface | Milestone |
| --- | --- |
| `EngineVersion` | M0 |
| `RayBorkAppBuilder` with `WithGame`, `WithTitle`, `WithSize`, `WithResizable`, `WithMaxDeltaTime`, `WithTargetFps`, `WithWindowState`, `WithLoggerFactory`, `WithLogLevel`, and `Build` | M1 |
| `RayBorkApp` with `Run`, `RequestExit`, `IsRunning`, `IsDisposed`, and `Dispose` | M1 |
| `EngineContext` with `Input`, `Window`, `LoggerFactory`, `Time`, `DeltaTime`, `UnscaledDeltaTime`, `FrameCount`, `Fps`, `IsFirstTick`, `IsExiting`, `MaxDeltaTime`, and `RequestExit` | M1 |
| `IGame`; `IInput`, `Key`, `MouseButton`; `IGamepad`, `GamepadButton`, `GamepadAxis`, `GamepadTrigger` | M1 |
| `IWindow`, `WindowState`, `CursorMode`, `Monitor`; logging through `Microsoft.Extensions.Logging` | M1 |
| `Transform`, `Color`, `Rect`, `Vector3Extensions`, `QuaternionExtensions` | M1 |
| `BoundingBox` | M3 |
| `RayBorkException`, `GameException`, `PlatformException`, `WindowException` | M1 |
| `EngineInfo`, `PresentMode`, `RenderStats`, `TonemappingMode`, and the renderer builder options (`WithPresentMode`, `WithMSAA`, `WithTonemapping`, `WithExposure`, `WithValidation`) | M2 |
| `IAssetCatalog`, `IAsset`, `Mesh`, `Texture`, `Shader`, `Vertex`, `MeshBuilder`, `MeshNormalMode`, `TextureBuilder`, the texture/mesh enums, `RenderState` and the shader metadata types, and the asset exceptions | M3 |
| `WithAssetsPack`, `WithAssetsPacks`, `WithAssetType<T>`, `EngineContext.Assets`, `EngineContext.CreateMeshBuilder`, and `EngineContext.CreateTextureBuilder` | M3 |
| `Camera`, `CameraProjection`, `ClearMode`, `Model`, `ModelPart`, `Ray`, `BoundingFrustum`, `Shader.SetUniform`, `ResetUniforms`, and the draw API | M4 |
| `Camera` line, point, and triangle helpers (`DrawLine`, `DrawLines`, `DrawPoint`, `DrawTriangle`) and the twelve shape helpers with their overloads (`DrawCube`, `DrawSphere`, `DrawPlane`, `DrawCylinder`/`DrawWireCylinder`, `DrawCapsule`/`DrawWireCapsule`, `DrawCircle`, `DrawTriangleStrip`, `DrawRay`, `DrawBoundingBox`, `DrawWireCube`, `DrawWireSphere`, and `DrawGrid`) | M5 |
| `DrawBillboard` (source rectangle, `Transform`, origin, and `faceCamera` overloads) and `DrawWireModel` | M5 |
| `Collision` and `RayCollision`, including `GetRayQuad` | M5 |
| `MeshBuilder.GenerateCollisions`, `MeshBuilder.Generate*` (poly, plane, cube, sphere, hemisphere, cylinder, cone, torus, and knot), and `Mesh.HasCollisionAcceleration` | M5 |
| `TonemappingSettings`, `EngineContext.Tonemapping`, `WithAgxWhite`, and `WithAgxContrast` | M5 |
| `Color.FromSrgb`, `Color.FromSrgbBytes`, `Color.ToSrgb`, and the 158 restored Bork2d/Unity presets | M5 |
| `Font`, `Glyph`, `TextAlignment`, and `TextWrapping` | M6 |
| `EngineDefaults.DefaultFont`, `EngineDefaults.DefaultMonoFont`, the built-in Fira Sans Regular font, and the built-in JetBrains Mono variable font (default instance Regular) | M6 |
| Unified `EngineContext.CreateCamera(Transform, CameraProjection, fieldOfView, orthographicHeight, ...)` with `CameraProjection.Perspective`/`CameraProjection.Orthographic` | M7 |
| `Camera.ScreenToWorldTransform(Vector2, float)` | M7 |
| `Camera.DrawQuad` (solid color, full texture, and normalized source rectangle), `Camera.DrawDisc`, and `Camera.DrawGrid(Transform, int, float, Color)` | M7 |
| `RenderTexture`, `RenderTextureDescription`, `EngineContext.CreateRenderTextureAsync`, `RenderTexture.Resize`, `RenderTexture.ReadPixelsAsync`, `RenderTexture.ReadColorsAsync`, `Camera.Target`, the `CreateCamera` `target` parameter, and the sampled `ColorTexture`/`DepthTexture` views (color, depth, resize, and readback paths) | M8 |
| `PostProcessStack`, `PostProcessEffect`, `ShaderPostProcessEffect`, the `CreateCamera` `postProcess` parameter, the reserved `sourceColor`/`sourceDepth` custom post shader contract, target-sized chains, and viewport store-back (SSAO and bloom are later M8 tasks) | M8 |

`IAssetLoader<T>`, `AssetLoadContext`, `AssetEntryType`, and `WithAssetLoader<T>` extend the
current asset surface after M4; see §6.

In M1, `EngineContext` exposes **only** `Input`, `Window`, `LoggerFactory`, `Time`, `DeltaTime`,
`UnscaledDeltaTime`, `FrameCount`, `Fps`, `IsFirstTick`, `IsExiting`, `MaxDeltaTime`, and
`RequestExit`. It has no `Assets`, `Info`, `Stats`, camera, or render-texture members.

In M1, `RayBorkAppBuilder` exposes **only** `WithGame`, `WithTitle`, `WithSize`, `WithResizable`,
`WithMaxDeltaTime`, `WithTargetFps`, `WithWindowState`, `WithLoggerFactory`, `WithLogLevel`, and
`Build`. The other builder methods in §3.2 are the later contract.

**M2 subset.** `EngineContext` additionally exposes `Info` and `Stats`, both valid from `Setup`.
`Info` reports the selected GPU, the Vulkan API version, the swapchain format, the effective sample
count and present mode, whether the scRGB HDR path is active, and whether validation is active;
`Stats` describes the last completed frame. `RayBorkAppBuilder` additionally exposes
`WithPresentMode`, `WithMSAA`, `WithTonemapping`, `WithExposure`, and `WithValidation`. M2 has no
camera or draw API: a frame clears the linear scene target and presents it. `Assets`, cameras, render
textures, and custom asset types remain later milestones.

**M3 subset.** `EngineContext` additionally exposes `Assets`, `CreateMeshBuilder`, and
`CreateTextureBuilder`. `RayBorkAppBuilder` additionally exposes `WithAssetsPack`, `WithAssetsPacks`,
and `WithAssetType<T>`. M3 ships the loading, creation, and metadata surface of `Mesh`, `Texture`,
and `Shader`: `Path`, `IsDisposed`, `Dispose`, the metadata getters in §7.3, §7.5, and §7.6, the
procedural `MeshBuilder`/`TextureBuilder` API, and CPU mesh vertices/indices. It does not ship
`Model`, `Font`, cameras, render textures, `Shader.SetUniform`, `ResetUniforms`, or any `Draw*`
member; those arrive in M4, M6, and M8. The M3 sample loaded a texture, a mesh part, a shader, and
registered custom data, and read back their metadata without drawing.

**M4 subset.** `EngineContext` additionally exposes `CreateCamera`, legal only on the tick thread
during `Tick`, and `Defaults`, which exposes the preloaded mesh
shader program and white/error textures. `Camera` ships as a frame-scoped recorder with the
`DrawModel`, `DrawMesh`, `DrawInstanced`, and `DrawModelInstanced` draw methods, `Viewport`,
`ViewMatrix`, `ProjectionMatrix`, `ViewProjectionMatrix`, `Frustum`, `ScreenToWorldRay`, and
`WorldToScreen`. `CameraProjection`, `ClearMode`, `Model`, `ModelPart`, `Ray`, and
`BoundingFrustum` ship in M4. The M4 entry point creates a perspective camera; M7 unifies the
orthographic projection into it with the `orthographicHeight` parameter described in the M7 subset.
`Model` is a caller-owned composition of parts and meshes, not an
engine asset; each part owns a `Shader` material instance, and `Shader.SetUniform`/`ResetUniforms`
write that material's cursor. `Load<Shader>` returns a new independent material over a cached
internal program. M4 has no `Camera.Target` or `target` parameter (`RenderTexture` ships in M8),
no `Font`, and no text draw helpers; the font asset and `DrawText`/`DrawTextBillboard` ship in M6. The
primitive, line, billboard, and wireframe-model helpers ship in M5. Clear
control is per camera:
`RayBorkAppBuilder.WithClearColor` is removed, and each pass clears its clamped viewport with its
`ClearMode` and `ClearColor`. A camera left undisposed when `Tick` returns is an
`InvalidOperationException` naming it, and the frame is not published.

**M5 subset.** `Camera` ships the helper surface described in §7.9: the line, point, and triangle
helpers; the twelve shape helpers with their shape, position, tessellation, start/end, and
`Transform` overloads; and the eight `DrawBillboard` overloads. Shapes and lines draw engine-owned
primitive meshes through the built-in `/shaders/primitive` program (vertex position and color, no
uniforms or textures), the billboard draws through `/shaders/mesh` with an alpha render state, and
`DrawWireModel` draws model parts through a polygon-mode-line pipeline gated on the device's
`fillModeNonSolid` feature (`GraphicsException` `RB3001` on a device without it; the engine has no CPU
line-geometry fallback). The meshes and the primitive program are internal engine assets; at M5,
`EngineDefaults` exposed only `CreateMeshShader`, `White`, and `Error`, and M6 adds `DefaultFont` and
`DefaultMonoFont`. `Collision` and `RayCollision` ship as allocation-free, thread-safe queries over
`Ray`, `BoundingBox`, and `Plane`;
`GetRayQuad` splits a quad into two double-sided triangles and returns the nearest non-negative hit,
and `GetRayMesh` accepts triangle-list and triangle-strip meshes, maps the hit through a world
transform, and traverses an immutable structure when the mesh has one.
`MeshBuilder.GenerateCollisions()` opts a procedural mesh into that structure before `BuildAsync`,
and `Mesh.HasCollisionAcceleration` reports whether a mesh has one. A mesh loaded from a pack gets one when the bake wrote the `RBVC` v1
collision payload that its descriptor names; a pack baked before the descriptor member existed loads
with no structure and brute-forces the retained triangles. `MeshBuilder` also ships the nine
`Generate*` factories (poly, plane, cube, sphere, hemisphere, cylinder, cone, torus, and knot);
heightmap and cubicmap generation are deferred until the engine has a CPU image type.
`EngineContext.Tonemapping` exposes the app-lifetime `TonemappingSettings`: `Exposure` in stops,
`AgxWhite` (default `16.29`), and `AgxContrast` (default `1.25`), all initialized from the builder
and mutable from `Setup`, `Tick`, and `Shutdown` on the tick thread. The present pass is frame-global,
and the tick thread snapshots the values into the frame packet when it seals the frame, so a write
during `Tick` applies to that frame's present. The M5 present pass reworks the tone mapper to Godot
4.7's HDR-aware AgX curve with the `allenwp` curve and CPU-derived curve parameters; SDR uses an
output maximum of `1`, and scRGB HDR uses the reported display headroom and then scales by paper
white. `Color` remains a linear value; its full 158-color Bork2d/Unity preset set is authored as sRGB
and stored linear through the same explicit conversion, so `Color.Gray` is the display mid-gray
(linear `0.214041`) and `Color.WhiteSmoke` is `0.913099`. `Color.FromSrgb`, `Color.FromSrgbBytes`,
and `Color.ToSrgb` add the explicit display-encoded conversions. `Camera.Target` remains a later
milestone (M8); the screen-space surface is described in the M7 subset.

The M5 limitations are deliberate: lines are one pixel wide and never request `wideLines`; wire
shapes are line geometry rather than polygon-mode rasterization; `DrawWireModel` needs
`fillModeNonSolid` and has no fallback; HDR10/PQ output is deferred and never selected; the SDR AgX
image differs from the M4 curve at the same exposure; and the `Gray`/`Grey`, `Yellow`, and
`DarkCyan` preset values changed with the sRGB-authored palette.

**M6 subset.** `EngineContext.Defaults` additionally exposes `DefaultFont`, the built-in Fira Sans
Regular face, and `DefaultMonoFont`, the built-in JetBrains Mono variable face (default instance
Regular). `Font` and `Glyph` ship as described in §7.10: `Load<Font>` returns the shared,
reference-counted font asset; the load parses
the packed face and is CPU-only, so it queues no GPU upload and the font object that load creates
reports `CachedGlyphCount` `0` (a repeated load returns
the shared object, whose cache reflects earlier draws). `TryGetGlyph` returns only glyphs whose MSDF
texture is already generated, and `Measure` reads face metrics and kerning without generating a
texture. An unmapped rune falls back to glyph id zero (`.notdef`). Glyph generation runs
asynchronously on the app-lifetime `RayBork.Font` thread that the asset catalog starts before
`Setup`; a draw never blocks on it. `Camera.DrawText` lays a block out in a transform's local XY plane
and `Camera.DrawTextBillboard` centers a block in a camera-facing plane; both record world-space text
through the built-in `/shaders/msdf` program with one instance per ready glyph and skip a glyph whose
texture is not ready yet, as described in §7.1 and §7.10. The font descriptor no longer carries a
preload range: the bake packs the whole face and any mapped glyph can be generated at runtime. A font
pack baked before M6 still loads because its removed `Ranges` member is just an unknown descriptor
member; only an old TOML sidecar that still declares `ranges` fails the bake (`RB4002`).

**M7 subset.** `EngineContext.CreateCamera` is the one camera entry point:

```csharp
public Camera CreateCamera(
    Transform transform,
    CameraProjection projection,
    float fieldOfView = 0f,
    float orthographicHeight = 0f,
    ClearMode clearMode = ClearMode.ColorAndDepth,
    Color? clearColor = null,
    float near = 0.01f,
    float far = 1000f,
    Rect? viewport = null);
```

A perspective camera uses `fieldOfView` and rejects a nonzero `orthographicHeight`; an orthographic
camera uses `orthographicHeight` and rejects a nonzero `fieldOfView`. A perspective `far` may be
`float.PositiveInfinity`; an orthographic `far` must be finite. The default `far` for a perspective
camera is `1000` (M4's default was infinity), so a caller that needs an infinite far plane passes
`float.PositiveInfinity` explicitly. `CreateOrthographicCamera` is removed; its `height` argument is
the unified `orthographicHeight`. The rest of the camera contract, including the `Camera` properties,
is unchanged, and every camera still records into the app's linear HDR scene target.

`Camera.ScreenToWorldTransform(Vector2 point, float distance)` returns a `Transform` for a
viewport-relative render-pixel point (`(0, 0)` is the viewport's top-left corner) on the view-depth
plane at `distance`: the position is the world point at that screen pixel, the rotation is the
camera transform's rotation, and the uniform scale is world units per render pixel at that plane
(`2 * distance * tan(fieldOfView / 2) / viewport.Height` for perspective,
`orthographicHeight / viewport.Height` for orthographic). Local `XY` is screen-aligned and local `+Z`
faces the camera. `distance` must be finite and greater than zero; the camera transform must have
unit scale. There is no `ScreenToWorldPoint` and no 2D draw overload.

`Camera` adds the world-space primitive helpers: `DrawQuad(Transform, Vector2 size, Color color,
RenderState? renderState = null)` draws one solid quad in the transform's local `XY` plane, centered,
with local `+Z` front, through the built-in `/shaders/primitive` material;
`DrawQuad(Transform, Vector2 size, Texture texture, Color? tint = null, RenderState? renderState =
null)` draws a textured quad through the built-in `/shaders/mesh` material, sampling the full texture;
`DrawQuad(Transform, Vector2 size, Texture texture, Rect source, Color? tint = null, RenderState?
renderState = null)` takes a normalized `[0, 1]` source rectangle, recorded as the per-instance UV
rectangle; `DrawDisc(Transform, float radius, Color color)` draws the unit disc mesh in the local `XY`
plane, centered, local `+Z` front, opaque; and `DrawGrid(Transform, int slices, float spacing, Color
color)` draws a uniform grid in the transform's local `XZ` plane, with the transform's position as
the grid center and local `+Y` as the normal. `DrawGrid(int, float, Color)` keeps its exact identity
behavior and is the transform form with `Transform.Identity`.

A `null` `renderState` means `RenderState.Opaque`; pass `RenderState.AlphaBlend` for a sprite or
alpha quad. The effective state is written to the draw record and is part of the batch key, so an
opaque and an alpha quad never merge. `DrawDisc` and `DrawGrid` are opaque. The text and billboard
paths keep their own states: text is alpha/no-depth-write and the billboard is alpha-blended.

The new primitives validate before any retention or record mutation. For `DrawQuad` and `DrawDisc`,
a non-finite size, radius, transform component, color, or source-rectangle component is
`ArgumentException`; a negative size, radius, or transform scale component is
`ArgumentOutOfRangeException`; an exactly zero size, radius, or transform scale component records
nothing. `DrawGrid` validates a non-finite `spacing` or transform component as `ArgumentException`,
`slices < 1` or `spacing <= 0` as `ArgumentOutOfRangeException`, records nothing for a zero transform
scale component, and keeps the M5 grid's color behavior (it does not validate its color). A null
texture is `ArgumentNullException` and a disposed texture, camera, or primitive asset is
`ObjectDisposedException`; a source rectangle with a non-finite component is `ArgumentException`, and
a non-positive width or height or a rectangle outside the normalized `[0, 1]` texture rectangle is
`ArgumentOutOfRangeException`.

`DrawTexture` is not shipped. A textured `DrawQuad` or `DrawBillboard(faceCamera: false)` covers a
sprite, and `ScreenToWorldTransform` plus `DrawText` covers screen-space text with pixel-sized text;
there is no `DrawText(Vector2)` screen overload and the `Transform` overload stays world-space. The
engine builds a ninth internal primitive mesh, the unit disc (36 segments), alongside cube, sphere,
plane, quad, triangle, line, wire cube, and wire sphere; there is still no public accessor for the
primitive meshes or their materials. `BoundingFrustum.Intersects` accepts a small plane tolerance
(`1e-4`) so a full-viewport or boundary-aligned quad is not culled by plane-extraction drift;
`Contains` keeps its exact non-negative test.

The screen-space HUD is same-pass text: `ScreenToWorldTransform(point, 1f)` plus
`DrawText(..., 18f, ...)` draws screen-aligned text that is alpha-blended with no depth write, so
geometry closer than the overlay plane can still occlude it. A HUD that must not be occluded is an
ordinary later camera that loads the scene color and clears depth (`ClearMode.Depth`), or keeps both
buffers (`ClearMode.None`) when the HUD should remain depth-tested; there is no separate overlay or
layer pass. Multiple cameras and viewport
rectangles are supported: each camera pass is its own render pass with the clamped viewport as the
render area, so split-screen views, viewport-scoped clears, and layered HUD cameras are correct in
one frame. The shader ABI v2 provides per-pass frame data: every camera pass in a frame writes its own
256-byte frame block and a push-constant pass index selects it, so multiple cameras with different
views read their own matrices, camera position, and viewport. `samples/Pong` is the M7 sample and
Native AOT gate, and `samples/Castle` and `samples/PrimitiveGallery` draw their status lines through
the same screen transform.

**Current asset surface.** The generic loader registration is a current-surface addition after M4,
not part of the M3 subset: `IAssetLoader<T>`, `AssetLoadContext`, `AssetEntryType`, and
`RayBorkAppBuilder.WithAssetLoader<T>`. See [Assets §6.7](api/assets.md#67-custom-asset-loaders).

`RenderStats` describes the draw path. `DrawCalls` counts scene submissions after batch splitting
and excludes the present triangle; `Instances` counts instances that survived culling;
`Triangles` counts submitted triangles after instancing; `CulledInstances` counts frustum
rejections; `PipelineCount` counts distinct scene pipelines; `TextureCount` counts live bindless
texture slots, including the reserved white slot. `PresentedFrames` counts successful presentations
since startup and is cumulative, so a presented frame-rate overlay differences it over a window;
`CpuFrameMilliseconds` covers only planning, recording, and submission and excludes the frame fence
wait, the swapchain acquire, and presentation. A frame with no camera passes and no draws reports
zero for the draw counters. Textures allocate a bindless slot when their GPU upload completes; slot
0 is always the built-in white fallback, and a full table fails the upload with
`AssetLoadException` `RB1010`. The bindless table has exactly 2048 slots matching the shader ABI's
`RayBorkTextures[2048]` array; a device whose update-after-bind sampled-image limits are below 2048
is rejected during device selection, and the capacity is never reduced. A depth-bearing render target
also consumes a second bindless slot for its resolved depth view. Device selection additionally requires
the Vulkan 1.2 `VK_RESOLVE_MODE_SAMPLE_ZERO_BIT` depth resolve mode, because every multisampled
depth-bearing target and the scene target resolve depth with that mode; a device without it is rejected
instead of falling back to a min/max resolve or an unsampleable depth view.

`samples/PrimitiveGallery` was the M5 AOT gate. It is one project in its own solution,
`samples/PrimitiveGallery/PrimitiveGallery.slnx`, outside `src/RayBork/RayBork.slnx`. It has no
assets executable and no pack: `Setup` builds its only texture with `CreateTextureBuilder`, and the
sample draws the M5 primitive helpers in one perspective pass at 800x450 with orbit controls and the
`--frames` and `--help` options, and draws its status line through `ScreenToWorldTransform`. Its
published `win-x64` and `linux-x64` hosts ran with zero AOT/trim
warnings, and the publish directory contains no bake tooling. The M5 suite was 1,899 tests, 93 of
them GPU integration tests, and every sample solution built.

`samples/Text` was the M6 AOT gate. It is a two-project solution, `samples/Text/Text.slnx`, outside
`src/RayBork/RayBork.slnx`: the sample and a JIT `Text.Assets` executable that bakes `text.pck` from
the checked-in Lora face and copies it beside the sample executable, so the sample has no `--assets`
argument. It draws the built-in Fira Sans and custom Lora faces at several sizes, rotations,
alignments, and wrapping modes, with Latin and Cyrillic paragraphs, a camera-facing billboard, and a
lazily generated Greek line. A fixed right-aligned status block in the built-in JetBrains Mono face
shows presented FPS, the tick rate, render CPU time, draw calls, and instances; the sample also has
orbit/zoom controls and the `--frames` and `--help` options.
The samples set no tick target and request FIFO presentation, so the display refresh paces the tick
thread; the engine default remains Mailbox for games that want the decoupled path. The published
`win-x64` and `linux-x64` M6 hosts ran `--frames 30` to exit `0` with zero AOT/trim warnings, on the
Windows hardware driver and on the conformant lavapipe software driver. The current
suite is 2,695 tests, 114 of them GPU integration tests, and every sample solution builds. The Text
sample is a plain user-facing example and contains no test-only draw assertion.

`samples/Pong` is the M7 AOT gate. It is a two-project solution, `samples/Pong/Pong.slnx`, outside
`src/RayBork/RayBork.slnx`: the sample and a JIT `Pong.Assets` executable that bakes `pong.pck`
during the solution build and copies it beside the sample executable and into the publish directory,
so the sample has no `--assets` argument. `PongSettings` lives only in the sample project;
`Pong.Assets` references `Pong.csproj` directly instead of linking the settings source file. The
solution builds `Pong` once and then `Pong.Assets`, which runs the assets executable out of process,
bakes `pong.pck`, and copies it beside the sample executable; `Pong` does not reference the assets
project, so the graph has no cycle. The publish target copies the pack produced by the Release
solution build into the publish directory and fails with a clear error if it is missing. The pack
contains `/data/game`, a `Pong.PongSettings` TOML data asset (paddle texture path, ball radius, max
score, paddle width/height/speed, ball speed, ball speed-up per hit, maximum ball speed, and AI
paddle speed), and `/textures/paddle`, a 32x128 original opaque paddle PNG baked by the ordinary
texture processor with its KTX2 raw payload. The match is player vs AI: the human moves the left
paddle and a deterministic AI moves the right paddle at `AiPaddleSpeed` (`400` design pixels per
second), tracking the ball while it approaches and returning to center otherwise with a `5`
design-pixel deadzone. The match records one orthographic camera per tick, fits a `1280x720` 16:9
design space into the current render pixels, and anchors every draw with `ScreenToWorldTransform`.
`DrawQuad` draws the playfield, border, net, score panels, and textured paddles; `DrawDisc` draws
the ball; `DrawText` draws the scores, status line, and pause/winner overlay. The simulation uses a
fixed opening serve, fixed `1/120` second substeps, top and bottom bounces,
`Collision.CheckBoxSphere` paddle collisions, a paddle-hit speed-up of `BallSpeedupPerHit` (`24`) up
to `MaxBallSpeed` (`900`) with the serve restoring `BallSpeed` (`420`), scoring with the serve going
to the loser, a max-score win, and pause/restart. The left-paddle controls are W/S, Up/Down, and
gamepad 0 left stick or d-pad; the sample accepts `--frames` and `--help`. The build bakes the pack
without manual steps. The Windows `win-x64` publish finished with zero AOT/trim warnings and
contains `pong.pck` beside `Pong.exe` with no bake tooling; the published Windows host ran
`--frames 30` to exit `0` on the hardware driver. The `linux-x64` publish also finished with zero
warnings, contains `pong.pck` beside `Pong` and no bake tooling, and the published host ran
`--frames 30` to exit `0` on the conformant lavapipe/llvmpipe software driver with an SDR
`B8G8R8A8_SRGB` swapchain, FIFO presentation, 4x MSAA, and a 1280x720 window. The Windows suite is
2,695 tests with no skips (114 GPU); the lavapipe run is 2,688 passed with 7 skipped HDR/
scRGB-present tests, because lavapipe exposes only SDR. The sample is a plain user-facing example
and contains no test-only draw assertion.

`samples/Castle` remains a buildable and publishable sample: its solution builds the `Castle.Assets`
executable out of process, bakes `/models/obj/castle` and `/models/obj/castle_diffuse` into
`castle.pck`, and copies the pack beside the sample executable, so the published hosts load it
without a manual bake or an `--assets` argument. The model parts use the built-in mesh material, the
pass draws the castle and `DrawGrid(20, 10f, Color.Gray)`, and `O` toggles the one camera between
perspective and orthographic. The orthographic height reproduces the perspective framing at the
current orbit distance (`2 * distance * tan(fieldOfView / 2)`), and its finite far plane is derived
from the maximum orbit distance. Castle draws its presented-FPS and render-statistics status line
through `ScreenToWorldTransform` at an 18-pixel em height, and appends the current projection to the
status block. The sample ray-casts the pointer against the model bounds and mesh
with `Collision.GetRayBox` and `Collision.GetRayMesh`, drawing `Camera.DrawBoundingBox` and a small hit
sphere while hovered. The hosts exit `0`. It is not the AOT gate. The Windows hardware path and the
conformant lavapipe Linux path pass; WSLg hardware Vulkan is not a target.

## 2. Quick start

This is the seed program corrected for the API in this document. The changes are listed in
[Appendix A](api/versioning.md#appendix-a-seed-to-api-changes).

```csharp
using RayBork;
using System.Numerics;

public sealed class CastleGame : IGame {
    private Model? _castle;
    private Texture? _texture;

    public async Task Setup(EngineContext ctx) {
        _castle = await ctx.Assets.Load<Model>("/models/castle");
        _texture = await ctx.Assets.Load<Texture>("/textures/castle");

        foreach (ModelPart part in _castle.Parts) {
            part.Shader.SetUniform("mainTexture", _texture);
        }
    }

    public void Tick(EngineContext ctx) {
        var cameraTransform = new Transform {
            Position = new Vector3(0f, 1f, 10f),
            Rotation = Quaternion.Identity,
            Scale = Vector3.One,
        };

        using var camera = ctx.CreateCamera(
            cameraTransform,
            projection: CameraProjection.Perspective,
            fieldOfView: 45f,
            clearMode: ClearMode.ColorAndDepth,
            clearColor: Color.DarkCyan,
            near: 0.01f,
            far: float.PositiveInfinity);

        var modelTransform = new Transform {
            Position = Vector3.Zero,
            Rotation = Quaternion.Euler(0f, (float)ctx.Time * 60f, 0f),
            Scale = Vector3.One,
        };

        camera.DrawModel(_castle!, modelTransform);
    }

    public Task Shutdown() {
        _castle?.Dispose();
        _texture?.Dispose();
        return Task.CompletedTask;
    }
}

internal static class Program {
    private const string WINDOW_TITLE = "3D Castle";
    private const int WINDOW_WIDTH = 1280;
    private const int WINDOW_HEIGHT = 720;
    private const double MAX_DELTA_TIME = 0.25;
    private const string PACK_FILE_NAME = "assets.pck";

    internal static int Main(string[] args) {
        string packPath = Path.Combine(AppContext.BaseDirectory, PACK_FILE_NAME);

        try {
            var game = new CastleGame();
            using var app = new RayBorkAppBuilder()
                .WithGame(game)
                .WithTitle(WINDOW_TITLE)
                .WithSize(WINDOW_WIDTH, WINDOW_HEIGHT)
                .WithResizable(true)
                .WithMaxDeltaTime(MAX_DELTA_TIME)
                .WithAssetsPack(packPath)
                .Build();

            app.Run();
            return 0;
        } catch (Exception exception) {
            Console.Error.WriteLine($"Windowed run failed: {exception}");
            return 1;
        }
    }
}
```

What changed:

- `Mesh` became `Model`; a castle is a multi-part model, not one primitive.
- Two shader loads create two independent `Shader` materials over one cached internal program.
- `Shader` is the material instance; `SetUniform` writes its cursor and there is no `Material`.
  The model's parts own their materials; set the texture on the part material, not on a separate
  shader loaded for the model.
- The camera moved from `z = -10` to `z = +10` for a right-handed, `-Z`-forward world.
- `ClearMode.Color` became `ClearMode.ColorAndDepth`.
- `ctx.Time` is already seconds, so the `* 0.001` disappeared.
- Rotation uses the degree-based `Quaternion.Euler` extension, not
  `Quaternion.CreateFromAxisAngle` radians.
