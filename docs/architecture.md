# RayBork Architecture

Status: M1 as-built for the host, platform, threads, frame handoff, and time, 2026-11-11. M0
established the build, dependency, and AOT guardrails. M1 ships the app host, dedicated tick
thread, SDL3 window/input/platform seam, a no-GPU render-thread frame consumer, time/pacing, and
logging. The renderer-backend sections remain the M2 design and are not shipped yet.

## Goals

RayBork is a small, raylib-shaped 3D game framework for C#:

- a window, input, time, assets, and enough drawing to make a game without a scene framework;
- linear HDR rendering with AgX tone mapping, reverse-Z, MSAA, bindless textures, and automatic
  instancing;
- SDL3 for windowing, input, and (later) sound;
- Windows x64 and Linux x64 first, Native AOT for game assemblies;
- a renderer seam that keeps the public API independent of Vulkan so future mobile/web backends can
  replace it;
- a bake step for assets, but a runtime that never compiles shaders or parses source formats;
- one runtime assembly, `RayBork.Engine`, with renderer and platform internals inside it. There is
  no `RayBork.Render` or `RayBork.Runtime` split like Bork2d.

The engine is deliberately not an editor, ECS, physics engine, animation system, or UI toolkit.
Those can be built on top of the draw API or added in later milestones.

## Projects and dependency rules

```text
RayBork.Tests ──────> RayBork.Engine, RayBork.Bake

RayBork.Engine ─────> Silk.NET.Vulkan, SDL3-CS, PolyType, Nerdbank.MessagePack,
                      Microsoft.Extensions.Logging, ZLogger
                      System.IO.Compression.Zstandard* (in-box, net11.0)
RayBork.Bake  ──────> RayBork.Engine (pack writer/types), shaderc, image/font/model tooling (JIT)
samples/HelloInterop -> RayBork.Engine (M1: window/input/logging diagnostic host, Native AOT)
Samples (M11) ──────> RayBork.Engine
```

| Project | Role |
| --- | --- |
| `RayBork.Engine` | Public API and all runtime internals: app host, SDL3 platform, renderer seam and Vulkan backend, asset catalog, pack reader, font/MSDF, input, logging. Native AOT compatible. |
| `RayBork.Bake` | JIT build library and assets-executable host: source discovery, TOML binding, processors, MessagePack shapes, pack writer, incremental manifest. Not referenced by a runtime project. |
| `RayBork.Tests` | xUnit v3 + Moq. Unit tests, architecture/dependency tests, GPU integration tests, AOT publish smoke tests. No golden-image suite. |
| `samples/HelloInterop` | M1 diagnostic host: opens an SDL3 window, runs `Setup`/`Tick`/`Shutdown` on the tick thread, logs window/DPI/monitor/input/gamepad/time activity, and exits on the close request, Escape, or `--frames`. It exercises the AOT publish path on `win-x64` and `linux-x64` before the renderer exists. |
| `samples/` | Castle, Pong, Text, Instancing, RenderTarget (M11). Each has assets, a bake step, and a Native AOT publish target. |

Rules:

- No runtime project references `RayBork.Bake`.
- No public type exposes a Silk.NET, SDL3, Vulkan, or other backend-native handle.
- The engine may reference only pinned, license-reviewed packages. Every package added to
  `RayBork.Engine` must stay in a trim/AOT publish graph without AOT/trim warnings; the milestone
  that first uses its features owns feature-level AOT validation.
- `RayBork.Bake` may use reflection-heavy libraries because it is never published as AOT.
- Tests may reference everything.
- The renderer backend is selected internally. The public API does not expose Vulkan types or
  device handles.

These rules are enforced by an architecture test and by the AOT publish gate, not only by review.

### Dependency matrix

Versions are pinned centrally in `Directory.Packages.props`; no project uses a floating range. The
[third-party notices](../THIRD-PARTY-NOTICES.md) mirror this matrix with licenses and the notice
policy.

**Runtime dependencies** (in the `RayBork.Engine` publish graph):

| Package | Version | Purpose | AOT status |
| --- | --- | --- | --- |
| `SDL3-CS` | 3.4.20 | Managed SDL3 bindings for windowing, input, and events | Exercised end to end by the M1 `samples/HelloInterop` AOT host on `win-x64` and `linux-x64`. |
| `SDL3-CS.Native` | 3.4.2 | Per-RID native SDL3 binaries | Loaded and run by the same M1 host on both RIDs. |
| `Silk.NET.Vulkan` | 2.23.0 | Vulkan 1.2 bindings for the M2 backend | Pinned and in the AOT publish graph; feature-level AOT validation lands with the M2 backend. |
| `PolyType` | 1.4.1 | Compiled shapes for custom TOML data | Pinned and in the AOT publish graph; feature-level AOT validation lands in M3. |
| `Nerdbank.MessagePack` | 1.4.104 | MessagePack serialization for custom data and pack entries | Pinned and in the AOT publish graph; feature-level AOT validation lands in M3. |
| `Microsoft.Extensions.Logging` | 11.0.0 | Logging abstraction exposed by `EngineContext` | Exercised end to end by the M1 AOT host on both RIDs. |
| `ZLogger` | 2.6.1 | Default console logging provider | Exercised end to end by the M1 AOT host on both RIDs. |
| `System.IO.Compression.Zstandard*` | in-box `net11.0` | Zstandard pack payload compression | Part of the .NET 11 shared framework; no package and no separate AOT gate. |

**Test and tooling dependencies** (JIT only; outside the runtime publish graph):

| Package | Version | Class | Purpose | AOT status |
| --- | --- | --- | --- | --- |
| `Microsoft.CodeAnalysis.PublicApiAnalyzers` | 5.6.0 | Tooling | Public API tracking for `RayBork.Engine` | Compile-time analyzer; no runtime or publish-graph impact. |
| `xunit.v3` | 3.2.2 | Test | Test framework | JIT only; not in the runtime publish graph. |
| `xunit.runner.visualstudio` | 3.1.5 | Test | VSTest adapter for xUnit v3 | JIT only; not in the runtime publish graph. |
| `Microsoft.NET.Test.Sdk` | 18.11.1 | Test | Test host and MSBuild integration | JIT only; not in the runtime publish graph. |
| `Moq` | 4.21.0 | Test | Mocking for unit tests | JIT only; not in the runtime publish graph. |

SDL3 and logging are exercised end to end by the M1 `samples/HelloInterop` AOT host. The other
runtime packages are pinned and remain in the AOT publish graph without AOT/trim warnings, but the
features that use them are first validated by the milestone that calls them: Vulkan in M2 and
PolyType/MessagePack in M3. M1 adds no renderer code, no Vulkan initialization, and no asset
pipeline. The bake's source-format tooling (shaderc and the image/font/model libraries) is added in
M3; it is JIT only and never enters the runtime publish graph, so this matrix gains its pinned
versions when M3 selects them.

## Platform seam (M1)

The host talks to SDL3 only through two internal interfaces:

| Interface | Thread | Contract |
| --- | --- | --- |
| `IEnginePlatform` | Main for `Initialize`, `Pump`, and `Dispose`; Tick for `CaptureInput` and the vibration queue | Owns SDL initialization, the window, the event pump, monitor snapshots, gamepad reconciliation, and queued rumble. Exposes `IWindow` and returns a new `IInput` snapshot per capture. |
| `IPlatformWindow` | Main for commands and snapshots; Tick for `BeginTick` | Extends `IWindow` with `ApplyPendingCommands` and `BeginTick` so the host can promote pending resize state at a tick boundary. |

The M1 implementation:

- `SdlPlatform` initializes SDL with the video, events, and gamepad subsystems, creates the window,
  starts text input, refreshes monitors, applies the startup window state, and opens connected
  gamepads. It runs those operations on the process main thread.
- `Pump` applies queued window commands and rumble requests, waits at most one short pump interval
  for an event, drains the rest, and reconciles gamepads. Window, display, close, focus, and
  gamepad device events are handled by the platform; input events go to `SdlInput`.
- `SdlWindow` keeps getter snapshots under a `Lock`. Every setter queues a command instead of
  calling SDL. `WindowCommandQueue` coalesces setter commands of the same kind in place, keeps
  action commands in order, and validates arguments on the caller. `BeginTick` promotes a pending
  resize to `WasResized` for exactly one tick. Clipboard reads return the cached value and queue a
  main-thread refresh.
- `SdlInput` writes keyboard, mouse, text, and gamepad events into `InputState` under a `Lock`.
  `CaptureInput` runs on the tick thread, clones the level and edge arrays, and clears the per-tick
  text, mouse delta, wheel, and edge sets in the same critical section. A gamepad handle is stable
  per SDL instance id; `Input.Gamepads` contains the pads connected for the current tick.
- Gamepad hotplug is reconciled on SDL gamepad-added/removed events and polled every 0.25 seconds.
  Buttons, sticks, and triggers are seeded from the initial device poll; vibration requests are
  queued to the main thread, so the tick thread never calls SDL directly.
- The test project substitutes `FakeEnginePlatform`, `FakePlatformWindow`, and `FakeInput` (see
  `src/RayBork/RayBork.Tests/TestInfrastructure`) to run lifecycle, timer, and logging tests without
  a display or a wall-clock wait.
- No SDL type or native handle appears in the public API.

## Renderer backend seam

M1 adds no renderer backend. The seam below is the design M2 implements; `IRendererBackend` is
internal and does not exist in the M1 assembly.

The public API is renderer-agnostic. Internally, the render thread talks to the `IRendererBackend`
contract, which owns:

- instance/device/surface/swapchain lifetime and recovery;
- pipelines, descriptor sets, buffers, and texture tables;
- command recording, submission, presentation, and GPU timing;
- capability projection into the public, immutable `EngineInfo` value.

### Lifecycle

`IRendererBackend` has one operation per frame or resource decision, never one per draw:

| Operation | Thread | Contract |
| --- | --- | --- |
| Initialize | Render | Creates the instance, device, queues, swapchain, and fixed GPU state after the main thread has created the window and native surface. Failure is fatal. |
| Resize | Render | Recreates the swapchain and dependent images after a window resize; retired images are released after their fences signal. |
| Submit | Render | Consumes one sealed frame packet, plans passes, builds batches, records and submits command buffers, and tracks frame fences. This is the only per-frame submission entry point. |
| Present | Render | Presents the newest submitted frame. Idles without presenting while the window is minimized or the swapchain has no extent. |
| Recover | Render | Handles one recoverable failure and returns recovered or fatal. |
| Dispose | Render | Destroys GPU objects, swapchain, device, and instance after the tick thread has stopped and in-flight frames have retired. |

`Submit` takes a **sealed frame packet**, not a live game object or a draw command. The tick thread
seals the packet before publishing it; the backend never sees `Camera`, `Shader`, `Mesh`, or any
other game-facing type. `Resize` and resource create/destroy requests are the other inputs. There is
no `Draw`, `SetUniform`, or per-object method on the seam, so a backend cannot add a virtual call
per draw. Batching, pipeline lookup, descriptor updates, and draw recording stay inside the backend
behind the frame boundary.

### Ownership and threading

- The render thread owns the backend object, device, queues, command buffers, pipelines,
  descriptors, buffers, images, and GPU uploads. Backend calls are render-thread only.
- The main thread owns the SDL3 window and creates the native surface. The backend does not pump
  window events, create windows, or read input.
- The tick thread records into the frame ring and never calls the backend. The render thread never
  calls user code: there are no callbacks or virtual methods from the backend into the game.
- `EngineInfo` is projected from backend capabilities at startup and after recovery. It is an
  immutable public value; no backend type, native handle, or live capability query crosses the
  seam.
- The backend is selected internally. `Vulkan12Backend` is the first implementation; a future
  WebGPU or mobile backend implements the same seam without changing `Camera`, `Shader`, `Mesh`, or
  `Model`. Games never implement a backend in v1.

### Failure handling

Recoverable failures are handled on the render thread without calling user code:

| Reason | Trigger | Response |
| --- | --- | --- |
| Swapchain out of date | Resize, restore, or display change | Recreate the swapchain and dependent images at a frame boundary. |
| Swapchain suboptimal | Driver hint | Recreate at the next frame boundary; presentation continues meanwhile. |
| Surface lost | Window or display topology change | Signal the main thread to recreate the native surface; recreate the swapchain at a frame boundary. |
| Recoverable device loss | Driver reset or timeout where a new device succeeds | Recreate the device and rebuild pipelines and GPU resources from retained descriptions. |
| Minimized / zero extent | Window state | Do not acquire or present; idle the renderer and keep the 30 FPS tick cap. |

Recovery pauses submission, recreates the affected resources, increments
`RenderStats.RecoveryCount`, logs at Warning, and resumes with the newest completed tick. An
in-flight frame that cannot complete is dropped, never partially presented.

Recovery is bounded. A failed recovery, an unavailable required device or feature, an out-of-memory
condition, or a repeated recovery loop is fatal: the tick loop stops, `Shutdown` runs, and `Run`
rethrows `GraphicsDeviceLostException` or the underlying `GraphicsException`. No partial frame is
presented.

## Threads

| Thread | Owns | Notes |
| --- | --- | --- |
| Main / window thread | SDL3 initialization, window, event pump, monitor and clipboard snapshots, queued window commands, gamepad reconciliation | Runs `RayBorkApp.Run`. The process main thread. No game callbacks. |
| Tick thread | `IGame.Setup`, `Tick`, `Shutdown`; input snapshot; frame publication | Dedicated thread. The game-facing engine state is tick-thread affine. |
| Render thread | The newest published frame lease; releases it after consuming | M1 consumes and releases frames without GPU work. M2 adds the backend, device, swapchain, present, and recovery. Never calls user code. |
| Asset loader | Pack I/O, decompression, deserialization, CPU decode (M3) | No loader thread exists in M1; M3 adds it and hands GPU upload requests to the render thread. |
| Sound thread | Audio mixer (post-1.0) | Not present in v1. |

Thread names are set for debuggers: `RayBork.Main`, `RayBork.Tick`, and `RayBork.Render`. M3 adds
`RayBork.Asset`; the post-1.0 audio milestone adds `RayBork.Audio`.

### Thread-affine state

- `IGame` callbacks run on the tick thread. Every object created by them (`Camera` in M4,
  `RenderTexture` in M8, `Font` preloading in M6, `Shader.SetUniform` in M4) is tick-thread affine.
- M3 adds `IAssetCatalog.Load`, `LoadData`, and `Exists` as thread-safe calls with concurrent loads
  allowed. Engine assets may be used on the tick thread after completion; custom data returned by
  the same `Load<T>` method is caller-owned. Custom types must be registered with
  `WithAssetType<T>()` before `Build`.
- `EngineContext.Time`, `DeltaTime`, `FrameCount`, and `Fps` are snapshots for the current tick.
  They are not synchronization primitives. `Stats` joins them in M2.
- `IInput` getters read the tick snapshot and are tick-thread only. `IWindow` getters return the
  last confirmed snapshot and are safe to read from any thread; window setters are queued to the
  main thread.
- Renderer backend objects are internal. No public type is a Vulkan handle.
- `ILoggerFactory` is safe to use from any thread; the app owns the default factory.
- If user code starts its own threads, it must join them before `Tick` returns. RayBork does not
  support background mutation of cameras, shader state, or engine assets that are in use.

### Setup and shutdown continuations

`Setup` and `Shutdown` are asynchronous, but they run on the tick thread. The engine installs a
`SynchronizationContext` on that thread, so `await` continuations resume on the tick thread while
the setup/shutdown task is pending. This keeps this legal:

```csharp
public async Task Setup(EngineContext ctx) {
    _model = await ctx.Assets.Load<Model>("/models/castle");
    _texture = await ctx.Assets.Load<Texture>("/textures/castle");
    _shader = await ctx.Assets.Load<Shader>("/shaders/castle");
    _shader.SetUniform("mainTexture", _texture);
}
```

The asset calls in this example arrive in M3; the continuation rule is already M1.
`ConfigureAwait(false)` opts out of the context and may resume on a pool thread. Asset calls are
still safe there, but tick-affine objects must not be touched from that continuation.

## Frame handoff

The tick and render threads run concurrently. M1 ships the handoff skeleton: the tick thread
publishes a sealed frame, and the render thread consumes the newest frame without GPU work. M2
replaces the consumer body with the renderer backend's record/submit/present path.

```text
tick thread                                  render thread
-----------                                  -------------
wait for a free slot
snapshot input / advance clock
IGame.Tick(ctx)
seal slot, publish as newest ready
                                              wait for newest ready
                                              take the newest ready frame
                                              consume and release the slot
```

- The ring has three slots. A tick blocks only when all three are occupied; this bounds run-ahead.
- A newer publish supersedes an older `Ready` slot, never a slot that is being consumed.
- The consumer always takes the newest completed tick. It does not queue a backlog of stale frames.
- A tick that requests exit still publishes its final frame before `IGame.Shutdown` runs. In M1 the
  render thread consumes and releases it during teardown; there is no GPU work.
- M1 frames contain no cameras, draw commands, or GPU work. M2 adds the camera packet and the
  cleared-background present path; M4 adds mesh draws.
- The engine does not interpolate between ticks in v1. Movement at a tick rate below the display
  rate will show discrete steps. Fixed timestep with interpolation is a later milestone.
- When the window is minimized or unfocused, the tick loop continues at a **30 FPS cap**. The M1
  consumer has no swapchain to idle; M2 may idle the renderer while the swapchain has no extent.

## Time

- `EngineContext.Time` is absolute monotonic seconds since `Run` started, measured with
  `Stopwatch`. It is live during `Setup`; a tick sees the value captured at the start of the tick.
  It is not the sum of `DeltaTime`.
- `EngineContext.DeltaTime` is the elapsed seconds since the previous tick, clamped to
  `MaxDeltaTime`.
- `EngineContext.UnscaledDeltaTime` is the unclamped value.
- The first tick reports `DeltaTime = 0` and `UnscaledDeltaTime = 0`.
- `EngineContext.Fps` is the tick rate averaged over a 1-second sliding window. It is `0` before
  two ticks have been sampled.
- `MaxDeltaTime` is configured on the builder and defaults to `0.25` seconds.
- `Time` continues across a long stall; `DeltaTime` does not. Games that need a stall-free clock
  should accumulate `DeltaTime` themselves.
- Minimized/unfocused windows cap the tick cadence at 30 FPS; the clock keeps advancing.

## Space, color, and depth

- Right-handed, Y-up, forward `-Z`.
- `Transform` is a world-space TRS value. `WithParent` treats the receiver as parent-local and
  returns the composed world transform: `world = local * parent` under System.Numerics'
  row-vector convention.
- User-facing angles are degrees. `System.Numerics` math stays radians; RayBork extension methods
  convert at the boundary.
- Color is linear float RGBA with HDR values allowed. There is no 8-bit `Color` in the public API.
- Cameras render into a linear `Rgba16Float` scene target. The present pass applies AgX and the
  swapchain transfer function. The swapchain prefers an HDR format and falls back to
  `B8G8R8A8_SRGB` or `R8G8B8A8_SRGB`.
- MSAA defaults to 4x and is resolved before presentation; HDR and SDR paths share the resolve.
- Projection is built for Vulkan's `[0,1]` depth range and reverse-Z:
  - near plane maps to depth `1.0`;
  - infinity maps to `0.0`;
  - clear depth is `0.0`;
  - default compare is `GreaterOrEqual`.
- Perspective cameras may use `far = +infinity`; orthographic cameras require a finite `far`.
- The renderer owns the Vulkan Y-flip and the front-face rule. Public screen coordinates are
  top-left origin; world space stays Y-up.

The reverse-Z perspective projection used by the engine is, for row vectors:

```text
[ f/aspect   0        0        0 ]
[ 0          f        0        0 ]
[ 0          0        0       -1 ]
[ 0          0        near     0 ]
```

where `f = 1 / tan(fieldOfView / 2)`. This gives `z_ndc = near / distance` and `w = distance`
for an infinite far plane. A finite far plane uses the usual reverse-Z variant. The Y-flip is
applied separately by the viewport/projection path.

## Shader state, batching, and instancing

RayBork has no public `Material`. A `Shader` is a complete program plus a mutable uniform cursor:

1. `Shader.SetUniform(name, value)` writes the current value.
2. `Camera.Draw*` copies the current values into the frame packet.
3. Later `SetUniform` calls affect later draws only.
4. The renderer packs the captured values into a per-instance parameter block.
5. Texture references become bindless indices in the instance record.

Batching key: `(mesh, shader, render state, topology)`. Uniform values and texture bindings do not
split a batch. A model with several parts is several mesh groups; each group is one instanced
draw.

This makes the following one draw call when `mesh`, `shader`, and render state match:

```csharp
shader.SetUniform("mainTexture", textureA);
camera.DrawMesh(mesh, transformA, shader);
shader.SetUniform("mainTexture", textureB);
camera.DrawMesh(mesh, transformB, shader);
```

The shader ABI is generated by the bake so custom GLSL does not have to declare the plumbing:
a frame uniform buffer, a bindless `sampler2D[]`, and a per-instance storage buffer containing the
transform, tint, UV rectangle, texture indices, and the reflected user uniforms. v1 has one fixed
vertex input layout: position, normal, uv, uv1, uv2, uv3, color, tangent. Vertex and fragment entry
points are `main_vertex` and `main_fragment`. The full ABI is in the
[API reference](api.md#74-shader-abi).

Vulkan instancing consumes one vertex/index buffer, so different meshes with the same shader are
still separate draws in v1. Multi-draw indirect is post-1.0.

## Assets

The runtime never loads loose source assets in normal operation.

```text
source files + TOML sidecars
        |
        v
RayBork.Bake assets executable (JIT)
  texture/model/shader/font processors, TOML data binding, validation, hashing
        |
        v
assets.pck (RAYBORKP v1, Zstandard payloads, binary index, aligned GPU blobs)
        |
        v
RayBork.Engine asset loader (I/O, decompress, deserialize, GPU upload)
        |
        v
game code: await ctx.Assets.Load<Model>("/models/castle")
           await ctx.Assets.Load<GameSettings>("/data/settings")
```

- The pack is the unit of deployment. Multiple packs can be mounted in order; later packs shadow
  earlier paths. The engine's built-in pack is mounted first.
- `Load<T>` is one typed loader. It handles built-in engine assets (`Mesh`, `Model`, `Texture`,
  `Shader`, `Font`, later `AudioClip`) and custom data types registered with
  `WithAssetType<T>()`.
- A custom data load reads a MessagePack entry through a compiled `[GenerateShape]`; the result is
  a fresh caller-owned instance per call, matching Bork2d. The type is registered at build time with
  `WithAssetType<T>()`; an unregistered type is a `NotSupportedException`.
- `LoadData` returns raw bytes.
- glTF and OBJ are source formats. The bake converts them to the engine's packed mesh/model format;
  the runtime never parses them.
- A load for an engine asset completes after GPU upload, so the first draw does not trigger a
  hidden upload hitch.
- Engine assets are reference-counted. A completed frame keeps its referenced assets alive until
  the GPU fence signals. Disposing an asset after recording a draw is safe; using the public object
  after `Dispose` is not.
- The loader is bounded and cancellation-aware. Shutdown cancels pending loads and drains the
  renderer before destroying GPU resources.
- Built-in assets are not special: the engine's built-in pack is produced by the same
  `IAssetProcessor` implementations through `UseDefaultProcessors`.

The pack format, bake application builder, processors, sidecars, and incremental manifest are
specified in the API reference's tooling section.

## Native AOT

- `RayBork.Engine` sets `IsAotCompatible`, `EnableTrimAnalyzer`, and `EnableAotAnalyzer`.
- No runtime reflection serialization. Pack metadata is read explicitly; custom data uses
  PolyType `[GenerateShape]` and Nerdbank.MessagePack.
- No runtime shader compilation. Shader reflection is bake-generated metadata.
- `Load<T>` maps built-in engine asset types with a closed type switch; custom data uses a
  builder-registered compiled shape delegate. `LoadData` covers opaque bytes.
- `Microsoft.Extensions.Logging` and ZLogger are AOT-gated runtime dependencies; M1 is their first
  feature-level gate and exercises them on both RIDs.
- M1 validates SDL3 and logging end to end with the `samples/HelloInterop` AOT host. The other
  runtime packages are in the M1 publish graph and are feature-validated in M2 (Vulkan) and M3
  (PolyType/MessagePack).
- P/Invoke is direct; native SDL3 and Vulkan libraries are deployed per RID.
- The generic asset loader maps a closed set of built-in types; custom data goes through the
  `WithAssetType<T>()` registry or `LoadData`.
- The bake tool is JIT and may use reflection, `MetadataLoadContext`, and native executables. It is
  never referenced by a runtime project and never enters the publish graph.
- Every milestone's exit gate includes at least one `win-x64` and one `linux-x64` AOT publish of
  a sample.

## Invariants

1. The tick thread never calls the renderer backend. The render thread never calls user code.
2. A `Camera` is valid only between `CreateCamera` and `Dispose`, inside one `Tick`.
3. A frame packet owns a reference to every engine asset it references.
4. Shader state is captured at draw time; later mutation cannot change a recorded draw.
5. The render thread consumes the newest completed tick, never a stale queue.
6. Public API never exposes a native handle or a backend type.
7. A game assembly published with `PublishAot=true` must start, load a pack, draw, and shut down.
8. Depth is reverse-Z everywhere: projection, clear, compare, and tests.
9. The public surface is renderer-agnostic; swapping the backend does not change game code.
10. The engine's built-in assets are always mounted, so an empty game can clear the screen.
11. Only one RayBork app runs per process in v1.

Invariants 2-4, 7, 8, and 10 describe the renderer and asset milestones; M1 enforces 1, 5, 6, and
11.
