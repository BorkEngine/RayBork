# RayBork Architecture

Status: M4 as-built for the core 3D renderer, on top of the M3 asset pipeline, the M2 renderer
foundation, and the M1 host, platform, threads, frame handoff, HDR presentation, recovery, and time.
M0 established the build, dependency, and AOT guardrails. M1 ships the app host, dedicated tick
thread, SDL3 window/input/platform seam, the render-thread frame handoff, time/pacing, and logging.
M2 adds the Vulkan 1.2 backend behind the internal seam, the linear HDR scene target, the
AgX/scRGB present pass, MSAA with color resolve, reverse-Z depth, bounded recovery, the main-thread
surface handshake, committed present shaders, and GPU tests. M3 adds the RAYBORKP v1 pack, canonical
asset paths, the dedicated asset loader, the runtime catalog and reference counting, the
render-thread GPU upload bridge, the custom-data pipeline, the bake tooling, and the build-time
built-in pack. M4 adds the frame-scoped camera and draw API, `Model` loading and parts, the shader
ABI and uniform cursor, the bindless texture table, the geometry pipeline, sampler and batch caches,
the batch planner with frustum culling and sorting, the geometry draw stats, the AgX highlight
extension, and the Castle AOT gate.

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
                      System.IO.Hashing, Microsoft.Extensions.Logging, ZLogger
                      System.IO.Compression.Zstandard* (in-box, net11.0)
RayBork.Bake  ──────> RayBork.Engine (pack writer/types), Tomlyn, StbImageSharp,
                      BCnEncoder.Net, Silk.NET.Shaderc(+.Native), SharpGLTF.Core,
                      System.IO.Hashing (JIT)
build/RayBork.BuiltInAssets -> RayBork.Bake, RayBork.Engine (JIT build-time generator)
samples/HelloInterop -> RayBork.Engine (M1-M3 diagnostic host, Native AOT)
samples/HelloInterop/HelloInterop.Assets -> RayBork.Bake (M3 assets executable, JIT)
samples/Castle -> RayBork.Engine (M4 sample and Native AOT gate)
samples/Castle/Castle.Assets -> RayBork.Bake (M4 assets executable, JIT)
Samples (M11) ──────> RayBork.Engine
```

| Project | Role |
| --- | --- |
| `RayBork.Engine` | Public API and all runtime internals: app host, SDL3 platform, renderer seam and Vulkan backend, built-in present shaders, asset catalog, pack reader, loader thread, GPU upload bridge, camera/frame recorder, batch planner, input, logging. Native AOT compatible. |
| `RayBork.Bake` | JIT build library and assets-executable host: source discovery, TOML binding, processors, MessagePack shapes, KTX2/BC encoding, GLSL compilation, pack writer, incremental manifest. Not referenced by a runtime project. |
| `build/RayBork.BuiltInAssets` | JIT build-only generator: runs the ordinary bake over the checked-in built-in sources and emits a C# byte array compiled into `RayBork.Engine`. Not referenced by a runtime project. |
| `RayBork.Tests` | xUnit v3 + Moq. Unit tests, architecture/dependency tests, GPU integration tests, AOT publish smoke tests. No golden-image suite. |
| `samples/HelloInterop` | M1-M3 diagnostic host: opens an SDL3 Vulkan window, runs `Setup`/`Tick`/`Shutdown` on the tick thread, presents the cleared linear scene, auto-mounts the adjacent `hello.pck` when the sample build produced it (or a pack passed with `--assets`) and loads its custom data, logs window/DPI/monitor/input/gamepad/time activity, and exits on the close request, Escape, or `--frames`. It is not the M4 AOT gate. |
| `samples/HelloInterop/HelloInterop.Assets` | M3 assets executable: configures `BakeApplicationBuilder`, bakes `hello.pck` at sample build time, registers a custom type, and demonstrates a custom processor. JIT only. |
| `samples/Castle` | M4 sample and Native AOT gate, an orbitable castle scene: mounts the adjacent `castle.pck`, loads `/models/obj/castle` and `/models/obj/castle_diffuse`, binds the texture to the loaded parts' built-in mesh materials, records an orbital castle pass every tick, and exits on Escape or `--frames`. The reference grid moves to M5 with `DrawGrid`. |
| `samples/Castle/Castle.Assets` | M4 assets executable: bakes `castle.pck` from the checked-in OBJ, TOML, GLSL, and PNG sources at sample build time. JIT only. |
| `samples/` | Pong, Text, Instancing, RenderTarget (M11). Each sample owns a solution, an assets executable with a build-time bake, and a Native AOT publish target. |

Rules:

- No runtime project references `RayBork.Bake` or `build/RayBork.BuiltInAssets`.
- The built-in pack generator is invoked by an MSBuild target out of process; `RayBork.Engine` only
  compiles the generated byte array. The generator's packages never enter the engine output or AOT
  publish graph.
- No public type exposes a Silk.NET, SDL3, Vulkan, or other backend-native handle.
- The engine may reference only pinned, license-reviewed packages. Every package added to
  `RayBork.Engine` must stay in a trim/AOT publish graph without AOT/trim warnings; the milestone
  that first uses its features owns feature-level AOT validation.
- `RayBork.Bake` may use reflection-heavy and native tooling because it is never published as AOT.
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
| `SDL3-CS` | 3.4.20 | Managed SDL3 bindings for windowing, input, events, window HDR state, and Vulkan surfaces | Exercised end to end by the M4 `samples/Castle` AOT gate on `win-x64` and `linux-x64`; earlier M1-M3 hosts use the same bindings. |
| `SDL3-CS.Native` | 3.4.2 | Per-RID native SDL3 binaries | Loaded and run by the same hosts on both RIDs. |
| `Silk.NET.Vulkan` | 2.23.0 | Vulkan 1.2 bindings for the backend | Exercised end to end by the M4 AOT gate: the published host creates the instance, device, swapchain, scene target, present pass, and geometry draw path. |
| `PolyType` | 1.4.1 | Compiled shapes for custom TOML data and pack descriptors | Exercised end to end by the M4 AOT gate, which loads the packed model, mesh, shader, and texture descriptors through compiled shapes. |
| `Nerdbank.MessagePack` | 1.4.104 | MessagePack serialization for custom data and pack descriptors | Exercised end to end by the M4 AOT gate. |
| `System.IO.Hashing` | 11.0.0 | XXH32/XXH3-64 pack integrity hashes | Exercised end to end by the M4 AOT gate; no reflection or dynamic code. |
| `Microsoft.Extensions.Logging` | 11.0.0 | Logging abstraction exposed by `EngineContext` | Exercised end to end by the M4 AOT gate on both RIDs. |
| `ZLogger` | 2.6.1 | Default console logging provider | Exercised end to end by the M4 AOT gate on both RIDs. |
| `System.IO.Compression.Zstandard*` | in-box `net11.0` | Zstandard pack payload compression | Part of the .NET 11 shared framework; no package and no separate AOT gate. |

**Test and tooling dependencies** (JIT only; outside the runtime publish graph):

| Package | Version | Class | Purpose | AOT status |
| --- | --- | --- | --- | --- |
| `Microsoft.CodeAnalysis.PublicApiAnalyzers` | 5.6.0 | Tooling | Public API tracking for `RayBork.Engine` | Compile-time analyzer; no runtime or publish-graph impact. |
| `xunit.v3` | 3.2.2 | Test | Test framework | JIT only; not in the runtime publish graph. |
| `xunit.runner.visualstudio` | 3.1.5 | Test | VSTest adapter for xUnit v3 | JIT only; not in the runtime publish graph. |
| `Microsoft.NET.Test.Sdk` | 18.11.1 | Test | Test host and MSBuild integration | JIT only; not in the runtime publish graph. |
| `Moq` | 4.21.0 | Test | Mocking for unit tests | JIT only; not in the runtime publish graph. |
| `Tomlyn` | 2.11.1 | Tooling | TOML parse and DOM binding for the bake | JIT only; not in the runtime publish graph. |
| `StbImageSharp` | 2.30.16 | Tooling | PNG/JPEG/TGA/BMP decoding for the texture processor | JIT only; not in the runtime publish graph. |
| `BCnEncoder.Net` | 2.3.0 | Tooling | BC1/BC4/BC5/BC7 block encoding for KTX2 payloads | JIT only; not in the runtime publish graph. |
| `Silk.NET.Shaderc` | 2.23.0 | Tooling | Managed shaderc bindings used by the GLSL compiler | JIT only; not in the runtime publish graph. |
| `Silk.NET.Shaderc.Native` | 2.23.0 | Tooling | Per-RID native libshaderc binaries for the bake | JIT only; not in the runtime publish graph. |
| `SharpGLTF.Core` | 1.0.7 | Tooling | glTF 2.0/GLB reading for the model processor | JIT only; not in the runtime publish graph. |

SDL3 and logging are exercised end to end by every sample AOT gate. The M2 milestone gate exercised
Silk.NET.Vulkan with the presenter; the M3 gate extended the host with a baked pack, a KTX2 texture
upload, a mesh upload, a shader-module upload, and a MessagePack custom-data load; the M4 gate
replaces it with `samples/Castle`, which draws the castle model with the built-in mesh material on
both RIDs. Both publishes must finish with zero AOT/trim warnings. The sample solutions are outside
the engine
solution, and each sample build bakes its pack beside the sample executable, so the JIT bake tool and
its packages stay out of the sample's AOT publish graph. The Vulkan loader is a system/GPU-driver
component, not a NuGet dependency. The bake's source-format tooling is JIT only and never enters the
runtime publish graph; `build/RayBork.BuiltInAssets` invokes it at engine build time but its output is
only the generated byte array.

## Platform seam (M1, Vulkan surface in M2)

The host talks to SDL3 only through two internal interfaces:

| Interface | Thread | Contract |
| --- | --- | --- |
| `IEnginePlatform` | Main for `Initialize`, `Pump`, `Dispose`, and the Vulkan surface operations; Tick for `CaptureInput` and the vibration queue | Owns SDL initialization, the window, the event pump, monitor snapshots, gamepad reconciliation, queued rumble, and the cached window HDR state. Exposes `IWindow` and returns a new `IInput` snapshot per capture. `GetVulkanInstanceExtensions`, `CreateVulkanSurface`, and `DestroyVulkanSurface` are main-thread only. |
| `IPlatformWindow` | Main for commands and snapshots; Tick for `BeginTick` | Extends `IWindow` with `ApplyPendingCommands` and `BeginTick` so the host can promote pending resize state at a tick boundary. |

The implementation:

- `SdlPlatform` initializes SDL with the video, events, and gamepad subsystems, creates the window,
  starts text input, refreshes monitors, applies the startup window state, and opens connected
  gamepads. It runs those operations on the process main thread.
- When `PlatformOptions.EnableVulkan` is set, the window is created with SDL's Vulkan flag.
  `GetVulkanInstanceExtensions` returns the de-duplicated, ordinal-sorted extension names SDL
  requires and fails with `PlatformException` `RB2003` when SDL reports none or omits
  `VK_KHR_surface`. `CreateVulkanSurface` loads the Vulkan loader on first use and fails with
  `RB2003` when the loader or the surface is unavailable. The renderer owns the returned handle and
  destroys it before the instance.
- The platform keeps a cached `WindowHdrState` under a `Lock`, built from the SDL3 window properties
  `SDL.window.HDR_enabled`, `SDL.window.SDR_white_level`, and `SDL.window.HDR_headroom`.
  `WindowHdrState.Normalize` forces the exact SDR state when HDR is disabled and substitutes
  neutral values for a non-finite or non-positive white level or headroom. The cache is refreshed
  after initialization, on `SDL_EVENT_WINDOW_HDR_STATE_CHANGED`, and on display changes; it is safe
  to read from any thread and never throws. The render thread reads the snapshot instead of calling
  SDL.
- `Pump` applies queued window commands and rumble requests, waits at most one short pump interval
  for an event, drains the rest, and reconciles gamepads. Window, display, close, focus, HDR state,
  and gamepad device events are handled by the platform; input events go to `SdlInput`.
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
- SDL's `dummy` video driver cannot create a Vulkan window or surface. GPU tests and the sample
  therefore use a real video driver; on WSL that means the WSLg display.
- No SDL type or native handle appears in the public API.

## Renderer backend seam

The public API is renderer-agnostic. Internally, the render thread talks to the `IRendererBackend`
contract; `Vulkan12Backend` is the M2 implementation.

The backend owns:

- instance/device/surface/swapchain lifetime and recovery;
- pipelines, descriptor sets, buffers, and texture tables;
- command recording, submission, presentation, and GPU timing;
- capability projection into the public, immutable `EngineInfo` value.

### Lifecycle

`IRendererBackend` has one operation per frame or resource decision, never one per draw:

| Operation | Thread | As-built contract |
| --- | --- | --- |
| `Initialize` | Render | Creates the instance, requests the native surface through `RendererBackendOptions.SurfaceProvider`, then creates the device, queues, swapchain, scene target, present pipeline, and frame resources. Failure is fatal (`RB3001`; a platform surface failure surfaces as `RB2003` on the main thread and is wrapped as `RB3001` on the render thread). |
| `Resize` | Render | Recreates the swapchain, scene target, present pipeline framebuffers, and per-image semaphores when the selected extent changes. A zero width or height marks the surface as having no drawable extent and keeps the presentation path. It is idempotent for an unchanged extent. |
| `Submit` | Render | Consumes one sealed `RenderFramePacket` with its camera passes, plans and draws the geometry, waits for a frame slot, acquires a swapchain image, clears the multisampled scene target per camera pass, resolves color, records the present pass, submits, and tracks the fence. It does not present. A minimized or zero-extent surface skips the frame without acquiring an image. |
| `Present` | Render | Presents the newest submitted frame. It idles without presenting when the surface is minimized, has no extent, or no frame was submitted. |
| `Recover` | Render | Handles one recoverable failure: swaps in a recreated presentation path, surface, or device, drops the failed frame, increments `RecoveryCount`, refreshes `Info`, and returns recovered or fatal. The call is bounded to three consecutive recoveries without a completed frame. |
| `Dispose` | Render | Waits for the device to idle, drops the pending present, destroys the present pipeline, scene target, frame resources, swapchain, and device on the render thread, and destroys the native surface through the provider. It hands the instance and loader teardown to the app's main-thread teardown queue, which runs that teardown after the render thread has joined. It is idempotent and safe after a failed recovery. |

`Submit` takes a **sealed frame packet**, not a live game object or a draw command. In M4 the packet
carries the frame index, the camera passes, and the recorded draws as value snapshots; the backend
never sees `Camera`, `Shader`, `Mesh`, or any other game-facing type. `Resize` and the frame call are
the only inputs. There is no `Draw`, `SetUniform`, or per-object method on the seam, so a backend
cannot add a virtual call per draw. The backend plans the batches, culls and sorts the instances,
looks up pipelines and descriptors, writes the frame block and instance storage buffer, and records
the draw commands behind the frame boundary. The plan itself is produced inside `Submit`; the tick
thread only records immutable draw records.

### Ownership and threading

- The render thread owns the backend object, device, queues, command buffers, swapchain, scene
  target, present pipeline, descriptors, and images. It creates the instance, but the final instance
  and loader teardown is handed to the app's main-thread teardown queue and runs after the render
  thread has joined. Backend calls are render-thread only.
- The main thread owns the SDL3 window and the native surface. The backend does not pump window
  events, create windows, or read input; it reads a `WindowSurfaceState` snapshot and creates or
  destroys the surface through `IRendererSurfaceProvider`.
- The app's `MainThreadSurfaceChannel` implements that provider. The render thread enqueues one
  request, signals the main thread, and blocks until the main thread serviced it or a 30-second
  bound expired. The main thread calls `TryService` from its startup wait, main loop, and shutdown
  join, so surface creation and destruction always run on the thread that owns the window. A
  platform failure is captured and rethrown on the render thread as `GraphicsException`.
- The tick thread records into the frame ring and never calls the backend. The render thread never
  calls user code: there are no callbacks or virtual methods from the backend into the game.
- `EngineInfo` is projected from backend capabilities at startup and after a recovery that changes
  the device or presentation path. It is an immutable public value; no backend type, native handle,
  or live capability query crosses the seam.
- The backend is selected internally. `Vulkan12Backend` is the first implementation; a future
  WebGPU or mobile backend implements the same seam without changing `Camera`, `Shader`, `Mesh`, or
  `Model`. Games never implement a backend in v1.

`EngineInfo` reports the engine name and version, the renderer name (`Vulkan <major>.<minor>` for
the selected device, for example `Vulkan 1.4` on a 1.4 device even though the backend requires 1.2),
the GPU name, the packed Vulkan API version, the swapchain format token, whether the presented path
is scRGB HDR, the effective sample count, the effective present mode, and whether the validation
layer is active. `RenderStatus` publishes `Info` and `Stats` as independent value copies under a
short lock; the tick thread never touches the backend.

### Failure handling

Recoverable failures are handled on the render thread without calling user code:

| Reason | Trigger | As-built response |
| --- | --- | --- |
| Swapchain out of date | Resize, restore, or display change | Wait for the device to idle, drop the pending present, recreate the swapchain, scene target, present pipeline framebuffers, and per-image semaphores at a frame boundary. |
| Swapchain suboptimal | Driver hint on acquire or present | Finish the present, then recreate the presentation path at the next frame boundary; presentation continues meanwhile. |
| Surface lost | Window or display topology change | Destroy the old native surface through the provider, have the main thread create a replacement inside the recovery, then recreate the presentation path. A failure is fatal. |
| Recoverable device loss | Driver reset or timeout where a new device succeeds | Recreate the logical device, re-probe capabilities, rebuild the swapchain, scene target, present pipeline, and frame resources from the retained settings. |
| Minimized / zero extent | Window state | Do not acquire or present; report a zero-draw frame and idle the renderer while the 30 FPS tick cap continues. |

A recovery waits for the device to idle, drops the frame that was waiting for presentation,
recreates the affected resources, refreshes `Info`, and increments `RenderStats.RecoveryCount`. An
out-of-date swapchain, a lost surface, or a device recreation logs at Warning; a suboptimal
swapchain logs at Information and recreates at the next frame boundary. An in-flight frame that
cannot complete is dropped, never partially presented.

Recovery is bounded to three consecutive recoveries without a completed frame
(`Vulkan12Backend.MAX_CONSECUTIVE_RECOVERIES`); a present that reaches the presentation engine
resets the count. Reaching the bound or failing to recreate the surface is fatal: the renderer
throws `GraphicsDeviceLostException` with `RB3002`. A Vulkan acquire or present result that is not
a recoverable presentation status throws `GraphicsException` with `RB3003`, and a validation error
is fatal with `RB3001`. The tick loop stops, `Shutdown` runs, and `Run` rethrows; no partial frame
is presented, and disposal remains safe after a failed recovery.

The backend also exposes internal GPU-test accessors (`Instance`, `Device`, `SceneTarget`), fault
injection for the recovery tests, and the `VulkanReadback` single-pixel probes. These are
diagnostics, not seam members: a readback creates its own command pool, staging buffer, and fence
and stalls the GPU, so it is never used by the frame path. The scene color is resolved to a
single-sample image the present pass samples; multisampled depth is not resolved.

M3 adds the `GpuAssetBridge` as an internal companion to the backend, not a new seam operation. The
render loop drains upload and destroy requests before frame waits and recreates live asset resources
after a device recovery. `IRendererBackend.DeviceGeneration` increments only when the logical device
is recreated, so presentation-only recoveries (out-of-date or suboptimal swapchain, lost surface) do
not re-upload assets. The backend never sees a game-facing asset type: the bridge carries descriptor
values and payload bytes only. M4 adds the render-thread companions the draw path needs: the
bindless texture table and slot allocator, the per-device sampler cache, the geometry pipeline
cache, and the frame-instance storage-buffer allocator. They are backend internals behind the same
seam; the tick thread never touches them.

## Threads

| Thread | Owns | Notes |
| --- | --- | --- |
| Main / window thread | SDL3 initialization, window, event pump, monitor and clipboard snapshots, queued window commands, gamepad reconciliation, native Vulkan surface | Runs `RayBorkApp.Run`. The process main thread. Services `MainThreadSurfaceChannel` during startup, the main loop, and shutdown. No game callbacks. |
| Tick thread | `IGame.Setup`, `Tick`, `Shutdown`; input snapshot; frame publication | Dedicated thread. Starts only after the renderer published `Info`, so `Setup` can read it. The game-facing engine state is tick-thread affine. |
| Render thread | Backend, instance, device, queues, swapchain, scene target, present pipeline, the newest published frame lease | Creates the Vulkan objects, consumes the newest frame, submits and presents, recovers, and disposes. Never calls user code and never touches the window directly. |
| Asset loader | Pack I/O, decompression, descriptor decode, registered-load dispatch, MessagePack custom data | Dedicated `RayBork.Asset` thread added in M3. Runs the synchronous part of every registered `IAssetLoader<T>`; it never calls SDL, Vulkan, or tick-thread APIs. It queues engine-asset GPU uploads and continues serving the next request instead of waiting for the copy. |
| Sound thread | Audio mixer (post-1.0) | Not present in v1. |

Thread names are set for debuggers: `RayBork.Main`, `RayBork.Tick`, `RayBork.Render`, and
`RayBork.Asset`. The post-1.0 audio milestone adds `RayBork.Audio`.

### Thread-affine state

- `IGame` callbacks run on the tick thread. Every object created by them (`Camera` in M4,
  `RenderTexture` in M8, `Font` preloading in M6, `Shader.SetUniform` in M4) is tick-thread affine.
- M3 adds `IAssetCatalog.Load`, `LoadData`, and `Exists` as thread-safe calls with concurrent loads
  allowed. Engine assets may be used on the tick thread after completion; custom data returned by
  the same `Load<T>` method is caller-owned. Custom types must be registered before `Build` with
  `WithAssetType<T>()` or `WithAssetLoader<T>()`. Engine asset metadata reads and `Dispose` are
  thread-safe; the M4 draw path requires the tick thread.
- `EngineContext.Time`, `DeltaTime`, `FrameCount`, and `Fps` are snapshots for the current tick.
  They are not synchronization primitives. `Stats` and `Info` join them in M2.
- `IInput` getters read the tick snapshot and are tick-thread only. `IWindow` getters return the
  last confirmed snapshot and are safe to read from any thread; window setters are queued to the
  main thread.
- Renderer backend objects are internal. No public type is a Vulkan handle.
- `ILoggerFactory` is safe to use from any thread; the app owns the default factory.
- If user code starts its own threads, it must join them before `Tick` returns. RayBork does not
  support background mutation of cameras, shader state, or engine assets that are in use.

### Startup and shutdown ordering

Startup runs on the main thread and interleaves with the render thread:

1. SDL3 initializes and creates the window with the Vulkan flag. The event pump starts.
2. The main thread queries the Vulkan instance extensions from the platform and creates the
   `MainThreadSurfaceChannel`. This is necessary because the window and its surface are
   main-thread objects.
3. The render thread starts and creates the instance. Its request for the native surface is
   marshalled through the channel; the render thread blocks on it while the main thread services
   the channel and pumps. The main thread also waits for the backend to publish `Info`, bounded to
   30 seconds; the surface request has the same 30-second bound.
4. The main thread mounts the built-in pack and every configured user pack, then starts the asset
   loader thread. A pack failure is a startup failure and no game callback runs. The loader is ready
   before `Setup`, so `Setup` can await engine-asset loads.
5. The tick thread starts and `IGame.Setup` runs. Because the tick thread starts only after the
   renderer is ready, `Setup` observes a valid `EngineContext.Info`.
6. The main loop services the surface channel, pumps events, and checks for exit, tick completion,
   or a render-thread failure before it stops.

Shutdown unwinds in the opposite direction:

1. The main thread stops accepting ticks and waits for the current tick to finish; the tick thread
   publishes the final frame and runs `IGame.Shutdown` on that thread, then exits.
2. The main thread joins the tick thread (pumping the platform while it waits), then cancels and
   joins the asset loader. The catalog releases its public assets and queues their GPU destruction
   on the render thread through the upload bridge.
3. The main thread completes the frame ring. The complete ring still drains its last ready frame.
   The render thread services the pending GPU destructions before it disposes the backend.
4. The main thread joins the render thread while it services the surface channel and pumps. The
   render thread drains and presents the final frame, disposes the backend (including destroying
   the native surface through the channel), and exits; the join is bounded to 30 seconds.
5. The main thread closes the surface channel and disposes the platform, which destroys the window
   and unloads SDL3 and the Vulkan loader. GPU resources are not destroyed before `Shutdown`
   returns.
6. `Run` returns.

The render thread never blocks on the platform directly and the main thread never blocks
unboundedly on the render thread. A renderer initialization failure before `Setup` is a startup
failure; a failure after it is a run-time failure, and both stop the tick loop and run the same
shutdown path.

### Setup and shutdown continuations

`Setup` and `Shutdown` are asynchronous, but they run on the tick thread. The engine installs a
`SynchronizationContext` on that thread, so `await` continuations resume on the tick thread while
the setup/shutdown task is pending. This keeps this legal:

```csharp
public async Task Setup(EngineContext ctx) {
    _mesh = await ctx.Assets.Load<Mesh>("/models/castle/wall");
    _texture = await ctx.Assets.Load<Texture>("/textures/castle");
    _shader = await ctx.Assets.Load<Shader>("/shaders/castle");
    _settings = await ctx.Assets.Load<GameSettings>("/data/settings");
}
```

The asset calls are M3; the continuation rule is M1. The M4 `Model` and `SetUniform` calls extend the
same pattern. `ConfigureAwait(false)` opts out of the context and may resume on a pool thread. Asset
calls are still safe there, but tick-affine objects must not be touched from that continuation.

## Frame handoff

The tick and render threads run concurrently. The tick thread publishes a sealed frame; the render
thread consumes the newest frame and runs it through the backend's submit/present path. The
handoff is unchanged from M1 except that the consumer now owns a GPU backend.

```text
tick thread                                  render thread
-----------                                  -------------
wait for a free slot
snapshot input / advance clock
IGame.Tick(ctx)
seal slot, publish as newest ready
                                              wait for newest ready
                                              take the newest ready frame
                                              submit and present it
                                              release the slot
```

- The ring has three slots. A tick blocks only when all three are occupied; this bounds run-ahead.
- A newer publish supersedes an older `Ready` slot, never a slot that is being consumed.
- The consumer always takes the newest completed tick. It does not queue a backlog of stale frames.
- A tick that requests exit still publishes its final frame before `IGame.Shutdown` runs. The render
  thread drains and presents it during teardown before the backend is disposed.
- M4 frames carry the camera passes and the recorded draw records: mesh, internal shader program,
  effective render state, instance matrices, per-instance uniform bytes, and bindless texture
  references. A tick with no camera pass still clears the linear HDR target; the present pass always
  draws one fullscreen triangle, which is not counted in `RenderStats.DrawCalls`.
- The frame slot retains every engine asset recorded into it and releases the hold when the render
  thread releases the slot after present. The camera draw recorder calls the retention set for every
  mesh, internal shader program, and bound texture; an asset disposed after recording a draw stays
  alive until the consuming frame completes. A model and a material are not engine assets and add no
  hold; materials are value snapshots in the record, and a model draw retains the internal program of
  each part material.
- The render thread services the GPU upload/destroy bridge at each loop turn and while it waits for
  a frame. Uploads are submitted without a fence wait and polled on later turns, so a load that
  starts in `Setup` completes without waiting for the first tick and does not stall frame
  presentation.
- The engine does not interpolate between ticks in v1. Movement at a tick rate below the display
  rate will show discrete steps. Fixed timestep with interpolation is a later milestone.
- When the window is minimized or unfocused, the tick loop continues at a **30 FPS cap**. The M2
  renderer skips image acquisition and presentation while the surface has no extent and keeps the
  last presentation path for the restore.

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
- The scene target uses `R16G16B16A16_SFLOAT` color and `D32_SFLOAT` depth. The present pass
  samples the resolved color and applies exposure, `TonemappingMode`, and the output encoding.
- MSAA defaults to 4x; scene color is resolved to a single-sample image before presentation, and
  the swapchain images are single-sample. The HDR and SDR paths share the resolve.
- Projection is built for Vulkan's `[0,1]` depth range and reverse-Z:
  - near plane maps to depth `1.0`;
  - infinity maps to `0.0`;
  - clear depth is `0.0`;
  - default compare is `GreaterOrEqual`;
  - depth test and depth write are enabled, and the depth range is `[0, 1]`.
- Perspective cameras may use `far = +infinity`; orthographic cameras require a finite `far`.
- The renderer owns the Vulkan Y-flip and the front-face rule. Public screen coordinates are
  top-left origin; world space stays Y-up.

**HDR selection.** The cached `WindowHdrState` drives the swapchain choice. When the window has HDR
enabled, the renderer selects `R16G16B16A16_SFLOAT` with
`VK_COLOR_SPACE_EXTENDED_SRGB_LINEAR_EXT` (scRGB) when the surface advertises that pair. If the
surface advertises the float format only with `VK_COLOR_SPACE_SRGB_NONLINEAR_KHR`, it uses that pair
and logs a warning. If the surface advertises no scRGB float pair, it falls back to the SDR order,
reports `EngineInfo.HdrOutput = false`, and logs a warning. The SDR order is `B8G8R8A8_SRGB`,
`R8G8B8A8_SRGB`, `B8G8R8A8_UNORM`, `R8G8B8A8_UNORM` (manual sRGB encode in the present shader),
then `R16G16B16A16_SFLOAT` (linear clamp). HDR10, `A2B10G10R10_UNORM_PACK32`, and PQ output are
deliberately deferred and are never selected in M4.

**HDR state and output.** The platform caches `SDL.window.HDR_enabled`,
`SDL.window.SDR_white_level`, and `SDL.window.HDR_headroom`, and refreshes the cache on
`SDL_EVENT_WINDOW_HDR_STATE_CHANGED` and display changes. A change to the HDR flag, or to the SDR
white level while HDR is enabled, recreates the presentation path. The scRGB present path scales
display-linear color by the SDR white level and does not clamp.

AgX remains the SDR-referred curve. On an HDR window the scRGB path first normalizes AgX's neutral
white by `luma(AgX(white)) = 0.9829795037669735` so its white sits at display luminance `1.0`, then
scales that to the reported paper white (the window's SDR white level, for example `2.0`). The
normalization makes the AgX white about 1.7% brighter than the raw M2 response; it is the same curve
presented at the reported paper white. Above the AgX white point
(`AGX_WHITE_SCENE_LUMINANCE = 2^4.026069 = 16.291742...` scene-linear luminance after exposure) the
highlight extension maps the remaining scene luminance into the reported HDR headroom at constant
chromaticity. With `r = sceneLuminance / AGX_WHITE_SCENE_LUMINANCE`,
`t = clamp((r - 1) / (headroom - 1), 0, 1)`, and
`yOut = paperWhite + (peak - paperWhite) * t`, where `peak = paperWhite * HDR_headroom` and
`paperWhite` is the reported SDR white level, the output is the normalized AgX color scaled by
`yOut / luma(normalized)`. Displayed luminance is exactly `yOut` and the channel ratios are
preserved. The mapping is continuous at the knee at paper white, reaches the reported peak at
`r = headroom`, and clamps beyond it. It is disabled when the headroom is at most one or when
`TonemappingMode.None` is selected; `None` remains the direct HDR authoring path and writes
`scene * paperWhite` without the extension. An SDR window uses a paper white and peak of `1` and
disables the extension, so its image is unchanged from M2. HDR10/PQ output is still deferred.
`EngineInfo.HdrOutput` is true only when the presented path is scRGB HDR.

**Present pass.** A fullscreen triangle generated from `gl_VertexIndex` writes the swapchain image
and samples the resolved scene color through one combined image sampler. Its push-constant block is
16 bytes: `{ float exposureMultiplier; float hdrWhiteScale; int tonemapping; int encoding; }`.
`exposureMultiplier` is `2^exposureEV` computed on the CPU; `tonemapping` is `0` for AgX and `1`
for none; `encoding` is `0` for linear clamp, `1` for manual sRGB encode, and `2` for scRGB HDR
(scaled by the SDR white level, not clamped). AgX applies the inset matrix, log2 range clamp,
contrast polynomial, and outset matrix, with no look transform. Scene clear control belongs to the
camera pass: each camera clears its clamped viewport with its own `ClearMode` and `ClearColor`, and
there is no builder-level clear color in M4. The present triangle is not a scene draw and is not
counted in `RenderStats.DrawCalls`.

**MSAA and depth.** The requested sample count comes from `WithMSAA` (default 4); the effective
count is the highest device-supported count that does not exceed the request, or `1` when none is
supported, and a mismatch logs a warning. The scene target color and depth attachments use the
effective count; the color attachment is resolved to a single-sample sampled image. Depth is
cleared to `0.0` per camera pass and the reverse-Z comparison state is defined, but multisampled
depth is not resolved in M4 because nothing samples depth until M8. The present triangle is not
depth-tested; the geometry pipelines consume the reverse-Z state.

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

## Built-in shader assets

The M2 present pass is the engine's first built-in shader:

```text
src/RayBork/RayBork.Engine/Shaders/Present.vert.glsl   committed source
src/RayBork/RayBork.Engine/Shaders/Present.vert.spv    committed SPIR-V build input
src/RayBork/RayBork.Engine/Shaders/Present.frag.glsl   committed source
src/RayBork/RayBork.Engine/Shaders/Present.frag.spv    committed SPIR-V build input
```

- `RayBork.Engine.csproj` embeds the `.spv` files as `EmbeddedResource` items under the logical
  names `RayBork.Shaders.Present.vert.spv` and `RayBork.Shaders.Present.frag.spv`.
- `ShaderAssets` reads each module once into an immutable byte array and exposes it as a
  `ReadOnlySpan<byte>` valid for the process lifetime. `VulkanPresentPipeline` passes those spans
  to `vkCreateShaderModule`; the entry points are `main` for both stages.
- The runtime never compiles shaders and never invokes `glslc`. CI and normal builds consume the
  committed blobs; the AOT publish graph contains no shader compiler or GLSL parser.
- Regenerate the blobs with `pwsh scripts/compile-shaders.ps1`, which requires the Vulkan SDK
  (`VULKAN_SDK` or `glslc` on `PATH`) and compiles every `*.glsl` in `Shaders/` with
  `--target-env=vulkan1.2 -O -Werror`. The script writes the sibling `.spv` file and fails the run
  when any source fails to compile. A shader change and its regenerated blob belong in the same
  commit.
- The fragment shader owns the encoding branch and the AgX transform. The numeric `encoding`
  values are the shader ABI: changing one requires recompiling the committed blob in the same
  change.

M3 bakes the present vertex/fragment pair into the built-in pack through the shared shaderc compiler,
so the pack contains the same program as ordinary content. The runtime present pipeline continues to
consume the committed embedded blobs directly. M4 adds the built-in mesh draw shader program at
`/shaders/mesh` to the built-in pack; it is baked by the ordinary shader processor with the generated
prologue, one `uTint` uniform, one `mainTexture` binding, and an opaque/back/`greater_equal`/depth-write
render state. It matches raylib's actual default model shader: unlit, vertex attributes position, uv,
and color only, `texture(mainTexture, vUv) * vColor * uTint * rbInstance.Color`, with no normals or
lighting. It replaces the old `/shaders/default` program. A model part with no authored
`[model] shader` resolves to `/shaders/mesh`, and an older pack that baked `shaders/default` is mapped
to the same program at load time.

The built-in pack also contains the white and error textures generated by the built-in texture
processor, and the white texture is the reserved bindless slot 0. After the catalog starts and before
`IGame.Setup`, the app preloads the `/shaders/mesh` program and both textures into
`EngineContext.Defaults` and holds their owner references until shutdown. `CreateMeshShader()` returns
a new caller-owned material over the preloaded program; `White` and `Error` are engine-owned and must
not be disposed by the game.

## Shader state, batching, and instancing

RayBork has no public `Material` type: a public `Shader` **is** a material instance. The GPU program
is an internal reference-counted engine asset with no public type. `Load<Shader>` caches the internal
program by `(canonical path, internal program type)` and returns a new independent material on every
call. A material owns a mutable uniform cursor:

1. `Shader.SetUniform(name, value)` writes the current value on that material.
2. `Camera.Draw*` copies the current values into the frame packet.
3. Later `SetUniform` calls affect later draws only.
4. The renderer packs the captured values into a per-instance parameter block.
5. Texture references become bindless indices in the instance record.

The program and the bake defaults are shared by every material over it; the cursor and the texture
bindings are not. Disposing one material releases only that material's program reference and texture
references, so another material over the same program stays usable.

Batching key: `(camera pass, mesh, internal program, effective render state)`; the mesh determines the
topology. Uniform values and texture bindings are per-instance material data and do not split a batch,
so materials over the same program batch together. A model with several parts is several mesh groups;
each group is one instanced draw. The planner splits a group when it would exceed
`MaxInstancesPerBatch` (16,384) or `MaxInstanceBytesPerBatch` (1 MiB, including the 96-byte instance
header); each split chunk is a separate draw call, and a single instance larger than the byte limit is
still a batch of one. Opaque batches keep recording order; alpha, additive, and multiply batches sort
back-to-front by nearest instance depth, with their instances also sorted back-to-front and a stable
order for equal depths. Instances are frustum-culled against the pass view-projection by transforming
the mesh bounds to a world-space AABB and rejecting only when the AABB is fully outside a plane. There
is no occlusion culling, LOD, or GPU-driven culling in v1.

This makes the following one draw call when the mesh and render state match, whether the calls use one
material or two:

```csharp
shader.SetUniform("mainTexture", textureA);
camera.DrawMesh(mesh, transformA, shader);
shader.SetUniform("mainTexture", textureB);
camera.DrawMesh(mesh, transformB, shader);
```

```csharp
Shader red = await ctx.Assets.Load<Shader>("/shaders/castle");
Shader blue = await ctx.Assets.Load<Shader>("/shaders/castle");
red.SetUniform("mainTexture", textureA);
blue.SetUniform("mainTexture", textureB);
camera.DrawMesh(mesh, transformA, red);
camera.DrawMesh(mesh, transformB, blue);
```

Both patterns record two instances over the same internal program, so the batcher emits one instanced
draw. The second pattern keeps two independent cursors, which is how separate systems vary state
without coordinating.

The shader ABI is generated by the bake so custom GLSL does not have to declare the plumbing: a
256-byte frame uniform buffer in descriptor set 0 binding 0, a fixed-size bindless
combined-image-sampler array in binding 1, and a per-instance storage buffer in binding 2 containing
the transform, tint, UV rectangle, one bindless index per texture binding, and the reflected user
uniforms. The array binding is `RayBorkTextures[2048]` in the shader and 2048 descriptors in the
layout. v1 has one fixed
vertex input layout: position, normal, uv, uv1, uv2, uv3, color, tangent. The geometry pipeline
declares the full layout, so unconsumed attributes are ignored by the shader (the validation layer
reports them as performance warnings). Vertex and fragment entry points are `main_vertex` and
`main_fragment`. The full ABI is in the
[API reference](api/rendering/shaders.md#74-shader-abi).

The bindless texture table has exactly 2048 slots, matching the `RayBorkTextures[2048]` array the
shader ABI declares and the descriptor set layout creates; the descriptor-indexing subset is
partially bound arrays, sampled-image update-after-bind, and non-uniform indexing of sampled-image
arrays. A device whose update-after-bind sampled-image limits are below 2048 is rejected during
device selection. Slot 0 is always the built-in 1x1 white fallback. Samplers are cached per device by
`(filter, wrap, anisotropy)`. Geometry pipelines are cached by the shader-state snapshot
`(vertex module, fragment module, effective render state, topology)`, and the present pipeline is
owned separately by the backend. `TextureCount` counts the live bindless slots including the white
slot, and `PipelineCount` counts the distinct scene pipelines the frame used.

M4 writes **one 256-byte frame uniform block per frame slot**. All camera passes recorded in the
same tick write that block, so a frame with more than one camera pass submits the last recorded
pass' view-projection, view, projection, camera position, and viewport to every pass. Single-camera
frames are correct. Split-screen and multiple cameras with different views need per-pass frame data,
which is M8 work with the multiple-camera gate; the limitation is documented in the M4 as-built
notes in the roadmap.

Vulkan instancing consumes one vertex/index buffer, so different meshes with the same shader are
still separate draws in v1. Multi-draw indirect is post-1.0.

## Assets

The runtime never loads loose source assets in normal operation.

```text
source files + TOML data/sidecars
        |
        v
RayBork.Bake assets executable (JIT)
  texture/model/shader/font processors, TOML binding, validation, hashing,
  KTX2/BC encoding, shaderc GLSL compilation
        |
        v
assets.pck (RAYBORKP v1, binary index, Zstandard payloads, aligned GPU blobs)
        |
        v
RayBork.Engine asset catalog (I/O, decompress, registry dispatch,
                             descriptor decode or loader, GPU upload)
        |
        v
game code: await ctx.Assets.Load<Texture>("/textures/castle")
           await ctx.Assets.Load<Model>("/models/castle")
           await ctx.Assets.Load<GameSettings>("/data/settings")
```

### Pack format and mounting

The pack is the unit of deployment. `RAYBORKP` v1 is a 40-byte little-endian prefix followed by a
sorted binary index and alignment-padded payloads. The index stores the canonical path, asset type,
flags, absolute payload offset, stored and uncompressed lengths, alignment, and an XXH3-64 hash of
the stored bytes; the prefix carries the XXH32 index hash. Every offset, length, alignment, path,
order, and hash is validated before an entry becomes visible. Payloads are read through a memory
map and decompressed with the in-box Zstandard APIs, bounded to the declared length.

Multiple packs mount in order; later packs shadow earlier paths. The engine built-in pack is
mounted first. Mounting is atomic: a failed mount leaves the previous stack unchanged and startup
fails with `AssetPackException`. v1 has no runtime mount or unmount API.

Asset paths are canonical lowercase ASCII with `/` separators and the `a-z0-9_-` segment grammar.
Public input may carry one optional leading `/`, is lowercased invariantly, and rejects
backslashes. The public `IAsset.Path` is the canonical path with a leading `/`; the pack index
stores it without one.

### Loader, coalescing, and reference counting

The app owns an `AssetCatalog` with one dedicated `RayBork.Asset` thread. `Load<T>`, `LoadData`, and
`Exists` are thread-safe. A request normalizes the path, resolves the winning entry at enqueue time,
and resolves the registration for `T` from the registry built at `Build`. The registry merges the
built-in registrations with the user registrations and holds two strategies: an engine asset format,
which decodes a descriptor and queues a reference-counted GPU upload, and an `IAssetLoader<T>`, which
runs arbitrary decode logic and returns a fresh caller-owned value. The loader thread reads and
decompresses the payload, decodes the descriptor or invokes the registered loader, and never touches
SDL or Vulkan. It runs only the synchronous part of a loader; a continuation after an `await` may
resume on the thread pool, so a loader must not block and must not call SDL, Vulkan, or tick-thread
APIs. An engine-asset request is deferred: the loader queues its GPU upload and immediately serves the
next request, so custom data and `LoadData` do not wait behind a GPU copy.

Engine assets (`Mesh`, `Texture`, and the internal shader program) are cached by
`(canonical path, engine asset type)` and reference-counted:

- the first successful load creates the object and returns it with one reference;
- a concurrent load of the same key joins the in-flight operation and acquires one reference on
  success;
- a later load increments the count and returns the same object;
- `Dispose` releases one reference; at zero the public object is marked disposed and its GPU
  resource destruction is queued on the render thread;
- a caller that cancels its wait acquires no reference and does not cancel the shared load for
  other callers;
- a failed load is not cached, so a retry is allowed.

A load for an engine asset completes only after the GPU upload succeeds, so the first draw never
triggers a hidden upload. The completion is a deferred loader item: the loader starts the upload and
keeps serving queued requests, and the asset is constructed and committed when the upload finishes.
A `WithAssetType<T>` value is a MessagePack custom-data payload decoded through the compiled shape; a
`WithAssetLoader<T>` value runs the caller's loader over the entry payload. Neither is an `IAsset`,
neither is cached, and both return a fresh caller-owned instance per call. A loader's nested loads
are queued on the same loader thread, and the load chain rejects a loader that requests the asset
type and path it is currently loading. `LoadData` returns a fresh copy of the decompressed entry
payload.

The public `Shader` is not in the engine-asset cache. `Load<Shader>` resolves the internal
`ShaderProgram` through the engine-asset path, then creates a material and transfers one program
reference to it. Every call returns a distinct material; program loads are coalesced, and the
material releases its program reference on `Dispose`. A canceled `Load<Shader>` caller acquires no
material and releases its load's program reference. A frame that recorded a draw retains the program
and the bound textures, not the material object.

### Models

`Load<Model>` decodes the model descriptor, resolves every part's mesh and internal program through
the catalog, and creates one material per part from the part's shader program path. The model is
constructed only after every part upload has completed. Shared part meshes are the same
reference-counted objects as a direct load; each model acquires one reference to every distinct part
mesh and owns the per-part materials. Every part gets its own material even when two parts name the
same program path. A part with no `[model] shader`, an older descriptor with a `null` shader path, or
an older pack that baked `shaders/default` resolves to the built-in `/shaders/mesh` program.
`Model.Parts` is a flat list in descriptor order, `Model.Bounds` is the union of the computed part
bounds, and each part keeps its baked local transform and its material's default render state. A
failed or canceled model load disposes every material and releases every mesh reference it already
acquired. There is no model cache entry: every `Load<Model>` returns a new wrapper with its own
materials over the shared cached meshes and programs, so a retry starts fresh and repeated loads
never share the wrapper. Disposing the model disposes its part materials and releases its mesh
references; a frame that recorded the model keeps the programs, meshes, and bound textures alive
through its retention set. `Model` is not an `IAsset` and has no path or lifetime of its own. `Model`
is static: no skeletal or morph animation in v1.

### GPU upload bridge and frame retention

The render thread is the only thread that calls Vulkan. The catalog hands each engine-asset upload
to the `GpuAssetBridge`, a thread-safe queue with a signal and one completion task per request. The
render thread drains the bridge before it waits for a frame, creates the image, buffer, or shader
module, records a short-lived command buffer, and submits it **without waiting for the fence**. It
polls completion on later loop turns, frees staging memory and the submission objects only after the
fence signals, and completes the request. Frames continue to submit and present while uploads are in
flight. While upload work is queued or in flight the render loop's wait is bounded to 2 ms so it can
poll even when no frame is being published; with no upload work it waits indefinitely.

At most eight uploads and 256 MiB of estimated upload payload are in flight. A single upload larger
than the byte budget may start when nothing is in flight, so a load always makes progress. A
canceled requester destroys the just-created resource instead of leaking it; cancellation of an
already-submitted copy waits briefly for its fence before cleanup. Destructions use the same queue
and run at a frame boundary.

A texture upload allocates its bindless slot when the GPU copy completes, not lazily at the first
draw. Slot 0 is the renderer's built-in 1x1 white texture, so a `null` binding and a bake default
both sample white. A full table fails the upload with `AssetLoadException` `RB1010` and names the
failing path and capacity. The slot is released only when the texture's GPU resource is destroyed,
which already waits for every submitted frame that can reference it, so a texture disposed while a
frame is in flight keeps its image and slot until that frame completes. Samplers are cached per
device and keyed by `(filter, wrap, anisotropy)`; every texture with the same sampler state shares
one sampler, and the cache is destroyed with the logical device. Mesh uploads create the
vertex/index buffers; shader uploads create both modules.

The bridge retains the CPU upload descriptor for every live GPU resource. After the backend recovers
a lost device, the render loop asks the bridge to recreate the live resources from those
descriptors before frame submission resumes; the public asset objects stay valid. Pending uploads
are canceled before the old device is destroyed and their `Load` calls fail with `RB1010`; they can
be retried after recovery.

A frame slot owns an engine-asset retention set. The camera draw recorder retains every mesh,
internal shader program, and bound texture it records; the frame ring releases the set after the
render thread releases the slot following present. An asset disposed while a recorded frame still
references it becomes pending, and its GPU destruction is queued only when the last frame hold is
gone.

### Procedural assets

`MeshBuilder` and `TextureBuilder` create assets without a pack. `EngineContext.CreateMeshBuilder`
and `CreateTextureBuilder` resolve an app-lifetime `ProceduralAssetService` owned by the asset
catalog. The service only queues uploads on the same `GpuAssetBridge` and destruction on the same
render thread; it never caches an asset or resolves a path.

- A builder owns mutable CPU data and the optional normal/tangent/bounds helpers.
  `BuildAsync` snapshots that data, validates the topology/index ranges or pixel layout, queues the
  upload, and returns an immutable `Mesh`/`Texture` only after the GPU copy finishes. One-shot
  semantics keep frame retention simple; changing the builder does not mutate a built asset.
- A procedural asset gets an `EngineAsset` only after a successful upload, with a reserved synthetic
  canonical path under `_procedural/mesh/` or `_procedural/texture/`. The catalog never resolves
  that prefix. The caller owns one reference; frame holds and deferred destruction use the same
  engine-asset lifetime as loaded assets.
- Loaded meshes decode and retain CPU vertices/indices in `Mesh.Vertices`/`Indices`; procedural
  meshes expose the builder snapshot. Textures never expose CPU pixels; procedural texture pixels
  stay in the builder. Procedural textures use uncompressed `Rgba8Unorm`, `Rgba8Srgb`, or
  `R8Unorm` levels and optional box-filter mips; BC payloads stay a bake product.
- The builders and `BuildAsync` are safe to call from any thread, including `Setup` and `Tick`.
  A builder instance is not thread-safe. The GPU copy still runs on the render thread through the
  bridge, and a build after app shutdown throws `ObjectDisposedException`.

### Custom data, Model, and Font

A custom data source is a TOML file with a root `[raybork]` table naming a registered CLR type. The
bake binds constructor parameters and properties with the compiled PolyType shape, runs semantic
validators, and writes one MessagePack `Data` entry. The runtime `Load<T>` deserializes it with the
registered `[GenerateShape]` shape; there is no reflection resolver and no runtime TOML parser.

M4 exposes the runtime `Model` loader. The model processor emits a `Model` descriptor plus one
`Mesh` part per primitive; the font processor emits a `Font` descriptor plus the face payload.
Font parsing and MSDF generation arrive in M6. M3 baked and validated both entry types before the
runtime types existed; M4 adds the `Model` load path and the model/part ownership described above.

### Bake tooling and the built-in pack

`RayBork.Bake` is a JIT library and assets-executable host. It discovers sources, parses TOML,
binds registered data types, runs processors, validates references and duplicate paths, writes the
pack, and maintains the per-source incremental manifest. It may use Tomlyn, StbImageSharp,
BCnEncoder.Net, Silk.NET.Shaderc, and SharpGLTF.Core; no runtime project references it.

Built-in assets are not special: the engine's built-in pack is produced by the same processors
through the same bake pipeline. A JIT-only generator under `build/RayBork.BuiltInAssets` runs the
ordinary `bake` command over `src/RayBork/RayBork.Engine/BuiltInAssets/Assets` into a temporary
directory and emits a C# byte array. An MSBuild target imported by `RayBork.Engine.csproj` builds
and runs the generator before `CoreCompile` and adds the generated file to the compilation. The
generated file lives under the intermediate output, is never committed, and is not published as a
loose pack; the generator and its packages do not enter the engine's output or AOT publish graph.

The pack format, bake application builder, processors, sidecars, validation, and incremental
manifest are specified in the [API reference](api/tooling.md#13-asset-tooling-rayborkbake).

## Native AOT

- `RayBork.Engine` sets `IsAotCompatible`, `EnableTrimAnalyzer`, and `EnableAotAnalyzer`.
- No runtime reflection serialization. Pack metadata is read explicitly; custom data uses
  PolyType `[GenerateShape]` and Nerdbank.MessagePack.
- No runtime shader compilation. Shader reflection is bake-generated metadata.
- `Load<T>` resolves a registration from the registry built at `Build`: engine asset formats are
  built-in registrations, `WithAssetType<T>()` stores a compiled shape delegate, and
  `WithAssetLoader<T>()` stores the caller's loader. `LoadData` covers opaque bytes.
- `Microsoft.Extensions.Logging` and ZLogger are AOT-gated runtime dependencies; M1 is their first
  feature-level gate and exercises them on both RIDs.
- M1 validated SDL3 and logging end to end with the `samples/HelloInterop` AOT host. M2 extended the
  same gate to Silk.NET.Vulkan: the published host created the instance, device, swapchain, scene
  target, and present pass and presented frames. M3 extended it again with a baked pack, a KTX2
  texture, a mesh, a shader, and a MessagePack custom-data value. M4 moves the gate to
  `samples/Castle`: the sample build bakes `castle.pck` and copies it beside the executable (the
  publish copies it into the publish directory), the published host mounts the adjacent pack, loads
  `/models/obj/castle` and `/models/obj/castle_diffuse`, binds the texture to the loaded parts'
  built-in `/shaders/mesh` materials, draws the castle model through the geometry pipeline,
  and shuts down. `samples/HelloInterop` remains the M1-M3 diagnostic host, auto-mounts its own
  build-baked `hello.pck`, and is not the AOT gate.
- PolyType, Nerdbank.MessagePack, and System.IO.Hashing are exercised as runtime dependencies by the
  M4 gate; the pack reader and descriptor decoding contain no reflection resolver.
- The Vulkan loader is loaded through `NativeLibrary` with a custom `INativeContext` instead of
  Silk.NET's path resolution, so the published host does not depend on a per-RID Silk.NET loader
  asset. The loader itself is a system/GPU-driver component and is not redistributed with the
  engine.
- Built-in shader blobs are embedded assembly resources. The built-in pack is a generated byte array
  compiled into the assembly. The publish graph contains no shader compiler, GLSL parser, TOML
  parser, image decoder, or block encoder.
- P/Invoke is direct; native SDL3 and Vulkan libraries are deployed per RID.
- The generic asset loader resolves the registry built at `Build`; engine formats are built-in
  registrations, custom data goes through `WithAssetType<T>()`, and custom decoding goes through
  `WithAssetLoader<T>()` or `LoadData`.
- The bake tool and the built-in pack generator are JIT and may use reflection and native tooling.
  Neither is referenced by a runtime project, and neither enters the publish graph.
- Every milestone's exit gate includes at least one `win-x64` and one `linux-x64` AOT publish of
  a sample.

## GPU tests and CI

- GPU tests are xUnit v3 tests marked `Category=Gpu`. `BackendTestHost` creates a hidden SDL Vulkan
  window, a Vulkan instance with validation requested, a device, and a `Vulkan12Backend`, and
  exposes the backend's instance, device, and scene target for diagnostics. `GpuTestHost` creates
  the same window, instance, surface, and device for the device-level tests without a backend.
- The tests assert numeric readback values with explicit tolerances: the configured linear clear
  color, depth cleared to `0.0`, the MSAA resolve producing the clear color at 1, 4, and 8
  samples, resize recreation, minimized idle, SDR-to-HDR presentation recreation, stats fields, and
  validation-layer cleanliness. The recovery suite covers swapchain out-of-date, surface loss,
  device loss, the three-recovery bound, and the fatal path.
- M3 adds GPU asset tests: a KTX2 texture creates one image/view/sampler and the expected format;
  a mesh creates the expected vertex/index buffers; a shader creates both modules; loading the same
  path twice creates one GPU resource; disposing after a simulated frame hold defers destruction
  until the hold is released; a device recovery re-creates the live resources; and the validation
  layer reports no errors for the upload path.
- M4 adds the geometry draw suite (`VulkanDrawTests`): a model and mesh draw through a camera and
  reach the readback target; repeated draws of one mesh/shader/state with different textures and
  uniforms become one instanced draw with the expected instance count; `RenderStats` reports draw
  calls after batch splitting, instances after culling, triangles, culled instances, and pipeline
  and texture counts; a frustum-culled instance is not submitted; an alpha batch is ordered
  back-to-front; an instanced draw with one cursor snapshot applies it to every instance; a
  disposed asset recorded into a frame stays alive until the frame completes; and the validation
  layer reports no errors for the draw path. There are no golden-image tests; the suite asserts
  counts, validation cleanliness, and explicit numeric readbacks with tolerances.
- There are no golden-image tests. `VulkanReadback` copies one pixel through a staging buffer; it is
  a diagnostic that stalls the GPU and is never used by the frame path.
- A GPU test that cannot run on the host calls `Assert.Skip` with the reason: no Vulkan loader, no
  physical device, or SDL cannot create a Vulkan window or surface. Programming errors and
  capability failures throw and fail the test; they are never silently skipped.
- CI has a `gpu` job on `ubuntu-latest`. It installs `libvulkan1`, `mesa-vulkan-drivers`,
  `vulkan-validationlayers`, and `xvfb`, resolves the lavapipe ICD
  (`/usr/share/vulkan/icd.d/lvp_icd*.json`), and runs the GPU-filtered tests under `xvfb-run` with
  `VK_ICD_FILENAMES` pointing at that ICD, so the validation layer is active in CI. The
  `build-test` jobs continue to run the full suite; GPU tests skip there when the runner has no
  display or Vulkan device.
- CI consumes the committed present shader blobs and runs the shader compiler only inside the JIT
  bake and built-in pack generation. It never needs the Vulkan SDK, `glslc`, or a GPU to build; the
  shaderc native binaries come from the pinned `Silk.NET.Shaderc.Native` package.

## Invariants

1. The tick thread never calls the renderer backend. The render thread never calls user code.
2. A `Camera` is valid only between `CreateCamera` and `Dispose`, inside one `Tick`.
3. A frame slot retains every engine asset a recorded frame references until the frame completes.
4. Shader state is captured at draw time; later mutation cannot change a recorded draw.
5. The render thread consumes the newest completed tick, never a stale queue.
6. Public API never exposes a native handle or a backend type.
7. A game assembly published with `PublishAot=true` must start, load a pack and custom data, draw
   where the milestone has a draw path, and shut down.
8. Depth is reverse-Z everywhere: projection, clear, compare, and tests.
9. The public surface is renderer-agnostic; swapping the backend does not change game code.
10. The engine's built-in pack is always mounted first; it supplies the present shader pair, the
    built-in mesh draw shader program at `/shaders/mesh`, and the white/error textures. The app
    preloads the mesh program and both textures into `EngineContext.Defaults` before `IGame.Setup`
    and releases them during shutdown.
11. Only one RayBork app runs per process in v1.
12. A GPU resource is created, re-created after a device recovery, and destroyed only on the render
    thread, and only after every frame that references it has completed.

Invariants 1, 5, 6, and 11 are M1; 8 and 12 are M2/M3; 2-4, 7, and 10 are exercised end to end by
the M4 camera and draw path and the Castle AOT gate. M3 enforced the asset and frame-retention parts
of 3, 7, and 10 before the draw recorder existed.
