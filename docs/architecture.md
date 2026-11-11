# RayBork Architecture

Status: M2 as-built for the renderer, host, platform, threads, frame handoff, HDR presentation,
recovery, and time. M0 established the build, dependency, and AOT guardrails. M1 ships the app
host, dedicated tick thread, SDL3 window/input/platform seam, the render-thread frame handoff,
time/pacing, and logging. M2 adds the Vulkan 1.2 backend behind the internal seam, the linear HDR
scene target, the AgX/scRGB present pass, MSAA with color resolve, reverse-Z depth, bounded
recovery, the main-thread surface handshake, committed present shaders, and GPU tests. The asset
pipeline remains the M3 design and is not shipped.

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
samples/HelloInterop -> RayBork.Engine (M1 diagnostic host; M2 AOT gate host, Native AOT)
Samples (M11) ──────> RayBork.Engine
```

| Project | Role |
| --- | --- |
| `RayBork.Engine` | Public API and all runtime internals: app host, SDL3 platform, renderer seam and Vulkan backend, built-in present shaders, asset catalog, pack reader, font/MSDF, input, logging. Native AOT compatible. |
| `RayBork.Bake` | JIT build library and assets-executable host: source discovery, TOML binding, processors, MessagePack shapes, pack writer, incremental manifest. Not referenced by a runtime project. |
| `RayBork.Tests` | xUnit v3 + Moq. Unit tests, architecture/dependency tests, GPU integration tests, AOT publish smoke tests. No golden-image suite. |
| `samples/HelloInterop` | M1 diagnostic host and the M2 AOT gate host: opens an SDL3 Vulkan window, runs `Setup`/`Tick`/`Shutdown` on the tick thread, presents the cleared linear scene through the renderer, logs window/DPI/monitor/input/gamepad/time activity, and exits on the close request, Escape, or `--frames`. It exercises the AOT publish path on `win-x64` and `linux-x64`. |
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
| `SDL3-CS` | 3.4.20 | Managed SDL3 bindings for windowing, input, events, window HDR state, and Vulkan surfaces | Exercised end to end by the M1 and M2 `samples/HelloInterop` AOT hosts on `win-x64` and `linux-x64`. |
| `SDL3-CS.Native` | 3.4.2 | Per-RID native SDL3 binaries | Loaded and run by the same hosts on both RIDs. |
| `Silk.NET.Vulkan` | 2.23.0 | Vulkan 1.2 bindings for the M2 backend | Exercised end to end by the M2 AOT gate: the published host creates the instance, device, swapchain, scene target, and present pass. |
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

SDL3 and logging are exercised end to end by the M1 `samples/HelloInterop` AOT host. The M2
milestone gate republishes that host with the renderer and exercises Silk.NET.Vulkan end to end on
`win-x64` and `linux-x64`; both publishes must finish with zero AOT/trim warnings. The Vulkan
loader itself is a system/GPU-driver component, not a NuGet dependency. PolyType and
Nerdbank.MessagePack are pinned and remain in the AOT publish graph without AOT/trim warnings, but
their feature-level validation lands in M3. The bake's source-format tooling (shaderc and the
image/font/model libraries) is added in M3; it is JIT only and never enters the runtime publish
graph, so this matrix gains its pinned versions when M3 selects them.

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
| `Submit` | Render | Consumes one sealed `RenderFramePacket`, waits for a frame slot, acquires a swapchain image, clears the multisampled scene target (with color resolve), records the present pass, submits, and tracks the fence. It does not present. A minimized or zero-extent surface skips the frame without acquiring an image. |
| `Present` | Render | Presents the newest submitted frame. It idles without presenting when the surface is minimized, has no extent, or no frame was submitted. |
| `Recover` | Render | Handles one recoverable failure: swaps in a recreated presentation path, surface, or device, drops the failed frame, increments `RecoveryCount`, refreshes `Info`, and returns recovered or fatal. The call is bounded to three consecutive recoveries without a completed frame. |
| `Dispose` | Render | Waits for the device to idle, drops the pending present, destroys the present pipeline, scene target, frame resources, swapchain, and device on the render thread, and destroys the native surface through the provider. It hands the instance and loader teardown to the app's main-thread teardown queue, which runs that teardown after the render thread has joined. It is idempotent and safe after a failed recovery. |

`Submit` takes a **sealed frame packet**, not a live game object or a draw command. The packet is a
value type that carries only the frame index in M2; the backend never sees `Camera`, `Shader`,
`Mesh`, or any other game-facing type. `Resize` and the frame call are the only inputs. There is no
`Draw`, `SetUniform`, or per-object method on the seam, so a backend cannot add a virtual call per
draw. Batching, pipeline lookup, descriptor updates, and draw recording stay inside the backend
behind the frame boundary.

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

## Threads

| Thread | Owns | Notes |
| --- | --- | --- |
| Main / window thread | SDL3 initialization, window, event pump, monitor and clipboard snapshots, queued window commands, gamepad reconciliation, native Vulkan surface | Runs `RayBorkApp.Run`. The process main thread. Services `MainThreadSurfaceChannel` during startup, the main loop, and shutdown. No game callbacks. |
| Tick thread | `IGame.Setup`, `Tick`, `Shutdown`; input snapshot; frame publication | Dedicated thread. Starts only after the renderer published `Info`, so `Setup` can read it. The game-facing engine state is tick-thread affine. |
| Render thread | Backend, instance, device, queues, swapchain, scene target, present pipeline, the newest published frame lease | Creates the Vulkan objects, consumes the newest frame, submits and presents, recovers, and disposes. Never calls user code and never touches the window directly. |
| Asset loader | Pack I/O, decompression, deserialization, CPU decode (M3) | No loader thread exists in M2; M3 adds it and hands GPU upload requests to the render thread. |
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
4. The tick thread starts and `IGame.Setup` runs. Because the tick thread starts only after the
   renderer is ready, `Setup` observes a valid `EngineContext.Info`.
5. The main loop services the surface channel, pumps events, and checks for exit, tick completion,
   or a render-thread failure before it stops.

Shutdown unwinds in the opposite direction:

1. The main thread stops accepting ticks and waits for the current tick to finish; the tick thread
   publishes the final frame and runs `IGame.Shutdown` on that thread, then exits.
2. The main thread joins the tick thread (pumping the platform while it waits) and completes the
   frame ring. The complete ring still drains its last ready frame.
3. The main thread joins the render thread while it services the surface channel and pumps. The
   render thread drains and presents the final frame, disposes the backend (including destroying
   the native surface through the channel), and exits; the join is bounded to 30 seconds.
4. The main thread closes the surface channel and disposes the platform, which destroys the window
   and unloads SDL3 and the Vulkan loader. GPU resources are not destroyed before `Shutdown`
   returns.
5. `Run` returns.

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
- A tick that requests exit still publishes its final frame before `IGame.Shutdown` runs. In M2 the
  render thread drains and presents it during teardown before the backend is disposed.
- M2 frames carry only the frame index: no cameras, draw commands, or texture state. The scene pass
  clears the linear HDR target and the present pass draws one fullscreen triangle. M4 adds the
  camera packet and mesh draws.
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
deliberately deferred and are never selected in M2.

**HDR state and output.** The platform caches `SDL.window.HDR_enabled`,
`SDL.window.SDR_white_level`, and `SDL.window.HDR_headroom`, and refreshes the cache on
`SDL_EVENT_WINDOW_HDR_STATE_CHANGED` and display changes. A change to the HDR flag, or to the SDR
white level while HDR is enabled, recreates the presentation path. The scRGB present path scales
display-linear color by the SDR white level and does not clamp, so values above the white point use
the display headroom. `EngineInfo.HdrOutput` is true only when the presented path is scRGB HDR.

**Present pass.** A fullscreen triangle generated from `gl_VertexIndex` writes the swapchain image
and samples the resolved scene color through one combined image sampler. Its push-constant block is
16 bytes: `{ float exposureMultiplier; float hdrWhiteScale; int tonemapping; int encoding; }`.
`exposureMultiplier` is `2^exposureEV` computed on the CPU; `tonemapping` is `0` for AgX and `1`
for none; `encoding` is `0` for linear clamp, `1` for manual sRGB encode, and `2` for scRGB HDR
(scaled by the SDR white level, not clamped). AgX applies the inset matrix, log2 range clamp,
contrast polynomial, and outset matrix, with no look transform. The scene clear color comes from
`WithClearColor`; until cameras arrive in M4 it is the only clear control.

**MSAA and depth.** The requested sample count comes from `WithMSAA` (default 4); the effective
count is the highest device-supported count that does not exceed the request, or `1` when none is
supported, and a mismatch logs a warning. The scene target color and depth attachments use the
effective count; the color attachment is resolved to a single-sample sampled image. Depth is
cleared to `0.0` and the reverse-Z comparison state is defined, but multisampled depth is not
resolved in M2 because nothing samples depth before M4/M8. The M2 present triangle is not
depth-tested; the state is defined for the geometry pipelines that arrive in M4.

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
- M1 validates SDL3 and logging end to end with the `samples/HelloInterop` AOT host. M2 extends the
  same gate to Silk.NET.Vulkan: the published host creates the instance, device, swapchain, scene
  target, and present pass and presents frames. PolyType and Nerdbank.MessagePack stay pinned and
  in the publish graph; their feature-level validation lands in M3.
- The Vulkan loader is loaded through `NativeLibrary` with a custom `INativeContext` instead of
  Silk.NET's path resolution, so the published host does not depend on a per-RID Silk.NET loader
  asset. The loader itself is a system/GPU-driver component and is not redistributed with the
  engine.
- Built-in shader blobs are embedded assembly resources. The publish graph contains no shader
  compiler, GLSL parser, or source shader file.
- P/Invoke is direct; native SDL3 and Vulkan libraries are deployed per RID.
- The generic asset loader maps a closed set of built-in types; custom data goes through the
  `WithAssetType<T>()` registry or `LoadData`.
- The bake tool is JIT and may use reflection, `MetadataLoadContext`, and native executables. It is
  never referenced by a runtime project and never enters the publish graph.
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
- There are no golden-image tests. `VulkanReadback` copies one pixel through a staging buffer; it is
  a diagnostic that stalls the GPU and is never used by the frame path.
- A GPU test that cannot run on the host calls `Assert.Skip` with the reason: no Vulkan loader, no
  physical device, or SDL cannot create a Vulkan window or surface. Programming errors and
  capability failures throw and fail the test; they are never silently skipped.
- CI has a `gpu` job on `ubuntu-latest`. It installs `mesa-vulkan-drivers` and `xvfb`, resolves the
  lavapipe ICD (`/usr/share/vulkan/icd.d/lvp_icd*.json`), and runs the GPU-filtered tests under
  `xvfb-run` with `VK_ICD_FILENAMES` pointing at that ICD. The `build-test` jobs continue to run
  the full suite; GPU tests skip there when the runner has no display or Vulkan device.
- CI consumes the committed shader blobs and never needs the Vulkan SDK, `glslc`, or a GPU to build.

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

Invariants 2-4, 7, and 10 arrive with the camera, shader, and asset milestones. M1 enforces 1, 5,
6, and 11; M2 adds 8.
