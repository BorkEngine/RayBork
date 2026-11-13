# RayBork API Reference

Status: **v0.1 M5**. M0 ships `EngineVersion`; M1 ships the app host, dedicated tick thread, SDL3
window/input/gamepads, time/pacing, and logging surface listed in §1.6. M2 ships the renderer
foundation: `EngineInfo`, `PresentMode`, `RenderStats`, `TonemappingMode`, the renderer builder
options, the graphics exceptions, and `EngineContext.Info`/`Stats`, backed by the Vulkan 1.2
backend. M3 ships the asset pipeline: `IAsset`, `IAssetCatalog`, `Mesh`, `Texture`, `Shader`, the
procedural `MeshBuilder`/`TextureBuilder` API, the asset and shader metadata value types, the asset
exceptions, pack mounting and lookup, custom TOML data through `WithAssetType<T>()`, `RayBork.Bake`,
and the build-time built-in pack. M4 ships the core 3D renderer: `Camera`, `CameraProjection`,
`ClearMode`, `Model`, `ModelPart`, `Ray`, `BoundingFrustum`, the camera draw methods,
`Shader.SetUniform`/`ResetUniforms`, and the shader ABI, backed by the bindless texture table,
instanced batches, pipeline and sampler caches, and the AgX HDR highlight extension. M5 ships the
primitive surface and the collision queries: the 13 `Camera` helpers, the engine-owned primitive
meshes and the built-in `/shaders/primitive` program, `Collision`, `RayCollision`,
`MeshBuilder.GenerateCollisions`, `Mesh.HasCollisionAcceleration`, and the baked `RBVC` collision
payload. The rest of the API reference remains the 1.0 contract, not a description of shipped
behavior.

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
| Platform | Windows x64, Linux x64 (including WSL) in v1; mobile and web are future targets |
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
| Screen space | Top-left origin, pixels; `Window.Width/Height` are window coordinates, `RenderWidth/RenderHeight` are physical pixels |
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

Unless marked otherwise, a mutable object (`Shader`, `Camera`, `Font` preloading) is tick-only.

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
reverse-Z depth, bounded recovery, and the GPU tests. The remaining rows name the milestone that
owns the deferred surface; the full code blocks in the section pages stay the 1.0 contract.

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
| `Camera` primitive, line, billboard, and screen-space texture helpers (`DrawLine`, `DrawLines`, `DrawPoint`, `DrawTriangle`, `DrawCube`, `DrawSphere`, `DrawCylinder`, `DrawPlane`, `DrawGrid`, `DrawWireCube`, `DrawWireSphere`, `DrawBillboard`, `DrawTexture`) | M5 |
| `Collision` and `RayCollision` | M5 |
| `MeshBuilder.GenerateCollisions` and `Mesh.HasCollisionAcceleration` | M5 |
| `Font`, `Glyph`, `TextAlignment`, and `TextWrapping` | M6 |
| `RenderTexture` and `RenderTextureDescription` | M8 |

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
member; those arrive in M4, M6, and M8. The M3 sample loads a texture, a mesh part, a shader, and
registered custom data, and reads back their metadata without drawing.

**M4 subset.** `EngineContext` additionally exposes `CreateCamera` and `CreateOrthographicCamera`,
both legal only on the tick thread during `Tick`, and `Defaults`, which exposes the preloaded mesh
shader program and white/error textures. `Camera` ships as a frame-scoped recorder with the
`DrawModel`, `DrawMesh`, `DrawInstanced`, and `DrawModelInstanced` draw methods, `Viewport`,
`ViewMatrix`, `ProjectionMatrix`, `ViewProjectionMatrix`, `Frustum`, `ScreenToWorldRay`, and
`WorldToScreen`. `CameraProjection`, `ClearMode`, `Model`, `ModelPart`, `Ray`, and
`BoundingFrustum` ship in M4. `Model` is a caller-owned composition of parts and meshes, not an
engine asset; each part owns a `Shader` material instance, and `Shader.SetUniform`/`ResetUniforms`
write that material's cursor. `Load<Shader>` returns a new independent material over a cached
internal program. M4 has no `Camera.Target` or `target` parameter (`RenderTexture` arrives in M8),
no `Font`, and no text draw helpers (`DrawText` arrives in M6); the primitive, line, billboard, and
screen-space texture helpers ship in M5. Clear control is per camera:
`RayBorkAppBuilder.WithClearColor` is removed, and each pass clears its clamped viewport with its
`ClearMode` and `ClearColor`. A camera left undisposed when `Tick` returns is an
`InvalidOperationException` naming it, and the frame is not published.

**M5 subset.** `Camera` ships the 13 primitive helpers listed in §7.9: the world-space shape, line,
and billboard draws plus the screen-space `DrawTexture`. They draw engine-owned primitive meshes
through the built-in `/shaders/primitive` program (vertex position and color, no uniforms or
textures) and through the built-in `/shaders/mesh` program with alpha/overlay render states for
billboards and textured quads. The meshes and the primitive program are internal engine assets, and
`EngineDefaults` still exposes only `CreateMeshShader`, `White`, and `Error`. `Collision` and
`RayCollision` ship as allocation-free, thread-safe queries over `Ray`, `BoundingBox`, and `Plane`;
`GetRayMesh` accepts triangle-list and triangle-strip meshes, maps the hit through a world transform,
and traverses an immutable structure when the mesh has one. `MeshBuilder.GenerateCollisions()` opts a
procedural mesh into that structure before `BuildAsync`, and `Mesh.HasCollisionAcceleration` reports
whether a mesh has one. A mesh loaded from a pack gets one when the bake wrote the `RBVC` v1
collision payload that its descriptor names; a pack baked before the descriptor member existed loads
with no structure and brute-forces the retained triangles. `DrawTexture` takes render pixels relative
to the camera viewport and is an alpha-blended overlay with no depth write. `Camera.Target` and
`DrawText` remain later milestones (M8 and M6).

**Current asset surface.** The generic loader registration is a current-surface addition after M4,
not part of the M3 subset: `IAssetLoader<T>`, `AssetLoadContext`, `AssetEntryType`, and
`RayBorkAppBuilder.WithAssetLoader<T>`. See [Assets §6.7](api/assets.md#67-custom-asset-loaders).

`RenderStats` describes the draw path. `DrawCalls` counts scene submissions after batch splitting
and excludes the present triangle; `Instances` counts instances that survived culling;
`Triangles` counts submitted triangles after instancing; `CulledInstances` counts frustum
rejections; `PipelineCount` counts distinct scene pipelines; `TextureCount` counts live bindless
texture slots, including the reserved white slot. A frame with no camera passes and no draws reports
zero for the draw counters. Textures allocate a bindless slot when their GPU upload completes; slot
0 is always the built-in white fallback, and a full table fails the upload with
`AssetLoadException` `RB1010`. The bindless table has exactly 2048 slots matching the shader ABI's
`RayBorkTextures[2048]` array; a device whose update-after-bind sampled-image limits are below 2048
is rejected during device selection, and the capacity is never reduced.

`samples/PrimitiveGallery` is the M5 AOT gate. It is one project in its own solution,
`samples/PrimitiveGallery/PrimitiveGallery.slnx`, outside `src/RayBork/RayBork.slnx`. It has no
assets executable and no pack: `Setup` builds its only texture with `CreateTextureBuilder`, and the
sample draws every M5 helper in one perspective pass at 800x450 with orbit controls and the
`--frames` and `--help` options. The published `win-x64` and `linux-x64` hosts run from the publish
directory with zero AOT/trim warnings, and the publish directory contains no bake tooling.

`samples/Castle` remains a buildable and publishable sample: its solution builds the `Castle.Assets`
executable out of process, bakes `/models/obj/castle` and `/models/obj/castle_diffuse` into
`castle.pck`, and copies the pack beside the sample executable, so the published hosts load it
without a manual bake or an `--assets` argument. The model parts use the built-in mesh material, the
pass draws the castle and `DrawGrid(20, 10f, Color.Gray)`, and the hosts exit `0`. It is not the M5
gate. The Windows hardware path, the conformant lavapipe Linux path, and the WSLg dzn hardware path
all pass. `samples/HelloInterop` remains the M1-M3 diagnostic host; it is not the AOT gate.

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
