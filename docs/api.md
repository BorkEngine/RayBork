# RayBork API Reference

Status: **v0.1 M3**. M0 ships `EngineVersion`; M1 ships the app host, dedicated tick thread, SDL3
window/input/gamepads, time/pacing, and logging surface listed in §1.6. M2 ships the renderer
foundation: `EngineInfo`, `PresentMode`, `RenderStats`, `TonemappingMode`, the renderer builder
options, the graphics exceptions, and `EngineContext.Info`/`Stats`, backed by the Vulkan 1.2
backend. M3 ships the asset pipeline: `IAsset`, `IAssetCatalog`, `Mesh`, `Texture`, `Shader`, the
asset and shader metadata value types, the asset exceptions, pack mounting and lookup, custom TOML
data through `WithAssetType<T>()`, `RayBork.Bake`, and the build-time built-in pack. The rest of
this document remains the 1.0 contract, not a description of shipped behavior.

The API is intentionally raylib-shaped: a small set of concrete types, a frame-scoped draw API,
and no mandatory scene framework. The runtime shape and renderer seam are described in
[architecture.md](architecture.md); the delivery plan is in [roadmap.md](roadmap.md).

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
owns the deferred surface; the full code blocks below stay the 1.0 contract.

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
| `EngineInfo`, `PresentMode`, `RenderStats`, `TonemappingMode`, and the renderer builder options (`WithPresentMode`, `WithMSAA`, `WithTonemapping`, `WithExposure`, `WithValidation`, `WithClearColor`) | M2 |
| `IAssetCatalog`, `IAsset`, `Mesh`, `Texture`, `Shader`, the texture/mesh enums, `RenderState` and the shader metadata types, and the asset exceptions | M3 |
| `WithAssetsPack`, `WithAssetsPacks`, `WithAssetType<T>`, and `EngineContext.Assets` | M3 |
| `Camera`, `CameraProjection`, `ClearMode`, `Model`, `ModelPart`, `Shader.SetUniform`, `ResetUniforms`, and the draw API | M4 |
| `Collision` and `RayCollision` | M5 |
| `Font`, `Glyph`, `TextAlignment`, and `TextWrapping` | M6 |
| `RenderTexture` and `RenderTextureDescription` | M8 |

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
`WithPresentMode`, `WithMSAA`, `WithTonemapping`, `WithExposure`, `WithValidation`, and
`WithClearColor`. M2 has no camera or draw API: a frame clears the linear scene target and presents
it. `Assets`, cameras, render textures, and custom asset types remain later milestones.

**M3 subset.** `EngineContext` additionally exposes `Assets`, which is valid from `Setup` and safe
to call from any thread. `RayBorkAppBuilder` additionally exposes `WithAssetsPack`,
`WithAssetsPacks`, and `WithAssetType<T>`. M3 ships the loading and metadata surface of `Mesh`,
`Texture`, and `Shader`: `Path`, `IsDisposed`, `Dispose`, and the metadata getters documented in
§7.3, §7.5, and §7.6. It does not ship `Model`, `Font`, cameras, render textures, `Shader.SetUniform`,
`ResetUniforms`, or any `Draw*` member; those arrive in M4, M6, and M8. The M3 sample loads a texture,
a mesh part, a shader, and registered custom data, and reads back their metadata without drawing.

## 2. Quick start

This is the seed program corrected for the API in this document. The changes are listed in
[Appendix A](#appendix-a-seed-to-api-changes).

```csharp
using RayBork;
using System.Numerics;

public sealed class CastleGame : IGame {
    private Model? _castle;
    private Texture? _texture;
    private Shader? _shader;

    public async Task Setup(EngineContext ctx) {
        _castle = await ctx.Assets.Load<Model>("/models/castle");
        _texture = await ctx.Assets.Load<Texture>("/textures/castle");
        _shader = await ctx.Assets.Load<Shader>("/shaders/castle");

        _shader.SetUniform("mainTexture", _texture);
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

        camera.DrawModel(_castle!, modelTransform, _shader!);
    }

    public Task Shutdown() {
        _castle?.Dispose();
        _shader?.Dispose();
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
- Two shader loads became one `Shader` program load.
- `Shader.SetUniform` replaced the fragment-shader call; there is no `Material`.
- The camera moved from `z = -10` to `z = +10` for a right-handed, `-Z`-forward world.
- `ClearMode.Color` became `ClearMode.ColorAndDepth`.
- `ctx.Time` is already seconds, so the `* 0.001` disappeared.
- Rotation uses the degree-based `Quaternion.Euler` extension, not
  `Quaternion.CreateFromAxisAngle` radians.

## 3. Lifecycle

### 3.1 `IGame`

```csharp
public interface IGame {
    Task Setup(EngineContext context);
    void Tick(EngineContext context);
    Task Shutdown();
}
```

| Member | Thread | Contract |
| --- | --- | --- |
| `Setup` | Tick | Runs once before the first `Tick`. Awaited to completion. Continuations resume on the tick thread unless `ConfigureAwait(false)` is used. Exceptions are fatal and `Shutdown` is not called. |
| `Tick` | Tick | Runs once per simulated frame. Must be synchronous and must not block. Exceptions are fatal; `Shutdown` is still called. |
| `Shutdown` | Tick | Runs once after the loop stops, including after a `Tick` exception. Awaited. Exceptions are aggregated with the original failure. |

`Setup` is where assets are loaded, shaders are configured, and render targets are created. It is
legal to call `Shader.SetUniform` in `Setup`; those values become the defaults for the first draw.
Camera creation is not legal in `Setup` because there is no frame to record into.

`Tick` must not call `.GetAwaiter().GetResult()` on an asset load unless it is a deliberate,
documented hitch. Prefer loading in `Setup`.

### 3.2 `RayBorkAppBuilder`

```csharp
public sealed class RayBorkAppBuilder {
    public RayBorkAppBuilder WithGame(IGame game);
    public RayBorkAppBuilder WithTitle(string title);
    public RayBorkAppBuilder WithSize(int width, int height);
    public RayBorkAppBuilder WithResizable(bool resizable = true);
    public RayBorkAppBuilder WithMaxDeltaTime(double seconds);
    public RayBorkAppBuilder WithAssetsPack(string path);
    public RayBorkAppBuilder WithAssetsPacks(params string[] paths);
    public RayBorkAppBuilder WithAssetType<T>() where T : IShapeable<T>;
    public RayBorkAppBuilder WithPresentMode(PresentMode mode = PresentMode.Mailbox);
    public RayBorkAppBuilder WithMSAA(int sampleCount = 4);
    public RayBorkAppBuilder WithTonemapping(TonemappingMode mode = TonemappingMode.AgX);
    public RayBorkAppBuilder WithExposure(float ev = 0f);
    public RayBorkAppBuilder WithClearColor(Color color);
    public RayBorkAppBuilder WithTargetFps(int framesPerSecond);
    public RayBorkAppBuilder WithValidation(bool enabled = true);
    public RayBorkAppBuilder WithLoggerFactory(ILoggerFactory loggerFactory);
    public RayBorkAppBuilder WithLogLevel(LogLevel level);
    public RayBorkAppBuilder WithWindowState(WindowState state);
    public RayBorkApp Build();
}
```

| Method | Default | Notes |
| --- | --- | --- |
| `WithGame` | required | Missing game is an `InvalidOperationException` from `Build`. |
| `WithTitle` | `"RayBork"` | Empty or whitespace is invalid. |
| `WithSize` | `1280 x 720` | Both dimensions must be positive. |
| `WithResizable` | `false` | |
| `WithMaxDeltaTime` | `0.25` seconds | Finite, positive. Values above `1.0` are allowed but discouraged. |
| `WithAssetsPack` | none | Pack path. Relative paths resolve against `AppContext.BaseDirectory`. May be called more than once; later packs shadow earlier ones. |
| `WithAssetsPacks` | none | Same, in order. |
| `WithAssetType<T>` | none | Registers a custom TOML data type for `Load<T>`. `T` must carry a compiled `[GenerateShape]`. Idempotent for the same type; a conflicting registration is an `InvalidOperationException`. |
| `WithPresentMode` | `Mailbox` | `Mailbox` is preferred; if unavailable, the engine falls back to `Fifo`. `Immediate` is available when the user explicitly chooses it. |
| `WithMSAA` | `4` | Valid sample counts are 1, 2, 4, and 8, subject to device support. The renderer selects the highest supported sample count that does not exceed the request, and 1 if none is supported, logging a warning when the effective count differs. |
| `WithTonemapping` | `AgX` | `AgX` maps linear HDR to the output; `None` is for custom output/presentation paths. |
| `WithExposure` | `0` EV | Exposure in stops applied before AgX. `0` is neutral; `+1` doubles linear exposure. |
| `WithClearColor` | linear opaque black | Sets the scene-pass clear color. Until cameras arrive in M4 it is the only clear color; from M4 a camera's own clear color takes precedence. |
| `WithTargetFps` | `0` (uncapped) | Caps the tick loop when vsync/mailbox is not throttling. `0` means no cap. |
| `WithValidation` | `true` in Debug, `false` in Release | If the validation layer is unavailable, the app logs a warning and continues without it. |
| `WithLoggerFactory` | default ZLogger console factory | A caller-supplied factory is not disposed by the app. `EngineContext.LoggerFactory` always returns a usable factory. |
| `WithLogLevel` | `Information` | Minimum level for the default ZLogger provider. Ignored when a caller-supplied factory already filters. |
| `WithWindowState` | `WindowState.Normal` | Fullscreen/borderless/maximized are applied at startup. |

The builder is single-use. `Build` validates the configuration and creates a `RayBorkApp`.
Configuration is not thread-safe; configure before `Build` and do not mutate after.

> **M1 subset.** M1 ships only `WithGame`, `WithTitle`, `WithSize`, `WithResizable`,
> `WithMaxDeltaTime`, `WithTargetFps`, `WithWindowState`, `WithLoggerFactory`, `WithLogLevel`, and
> `Build`. The remaining methods in the block above are the 1.0 contract; their milestones are in
> §1.6.

> **M3 subset.** M3 additionally ships `WithAssetsPack`, `WithAssetsPacks`, and `WithAssetType<T>`.
> Pack paths are not opened by `Build`; a missing or corrupt pack is an `AssetPackException` from
> `Run`, because the bake may produce the pack as part of the build. `WithAssetType<T>()` requires
> `T : IShapeable<T>` and a compiled `[GenerateShape]`; it registers a non-reflective loader delegate
> and is idempotent for the same type. A conflicting registration is an `InvalidOperationException`
> from the method call.

`Build` does not create a window, initialize SDL3, or touch Vulkan. Those happen in `Run`, so a
missing pack or an unavailable GPU is reported from `Run` with a clear startup diagnostic.

The renderer backend is selected internally. The public API is renderer-agnostic; Vulkan 1.2 is
the first backend, and future mobile/web backends do not change game code.

### 3.3 `RayBorkApp`

```csharp
public sealed class RayBorkApp : IDisposable {
    public void Run();
    public void RequestExit();
    public bool IsRunning { get; }
    public bool IsDisposed { get; }
    public void Dispose();
}
```

| Member | Thread | Contract |
| --- | --- | --- |
| `Run` | Main | Starts SDL3, the window, the tick thread, the render thread, and the loader; runs `Setup`, the loop, and `Shutdown`; joins everything before returning. Called once. |
| `RequestExit` | Any | Requests a graceful exit after the current tick. Idempotent. The final frame is rendered. |
| `IsRunning` | Any | `true` between the start and end of `Run`. |
| `IsDisposed` | Any | `true` after `Dispose`. |
| `Dispose` | Any except Tick | Stops a running app and joins its threads, then releases native resources. Idempotent. Calling it on the tick thread throws `InvalidOperationException` instead of self-joining. |

`Run` throws `InvalidOperationException` if it has already run, `ObjectDisposedException` if the
app was disposed, and `RayBorkException` for startup or runtime failures. It is not legal to call
`Run` from a thread other than the process main thread; SDL3 and some Vulkan surface paths require
the main thread.

If `Dispose` is called while `Run` is blocking on another thread, `Dispose` calls `RequestExit` and
waits for `Run` to finish. This is supported but not the normal path; prefer a single owner.
Calling `Dispose` from the tick thread is rejected because waiting for the tick thread from inside
a game callback would deadlock.

Recoverable renderer failures do not terminate the app. The render thread pauses, recreates the
swapchain or device, logs the recovery, and resumes. A failed recovery stops the app with
`GraphicsDeviceLostException` or the underlying `GraphicsException`.

### 3.4 `EngineContext`

```csharp
public sealed class EngineContext {
    public IAssetCatalog Assets { get; }
    public IInput Input { get; }
    public IWindow Window { get; }
    public ILoggerFactory LoggerFactory { get; }
    public EngineInfo Info { get; }

    public double Time { get; }
    public double DeltaTime { get; }
    public double UnscaledDeltaTime { get; }
    public long FrameCount { get; }
    public double Fps { get; }
    public bool IsFirstTick { get; }
    public bool IsExiting { get; }
    public double MaxDeltaTime { get; }

    public RenderStats Stats { get; }

    public Camera CreateCamera(
        Transform transform,
        CameraProjection projection,
        float fieldOfView,
        ClearMode clearMode = ClearMode.ColorAndDepth,
        Color? clearColor = null,
        float near = 0.01f,
        float far = float.PositiveInfinity,
        RenderTexture? target = null,
        Rect? viewport = null);

    public Camera CreateOrthographicCamera(
        Transform transform,
        float height,
        ClearMode clearMode = ClearMode.ColorAndDepth,
        Color? clearColor = null,
        float near = 0.01f,
        float far = 1000f,
        RenderTexture? target = null,
        Rect? viewport = null);

    public Task<RenderTexture> CreateRenderTextureAsync(RenderTextureDescription description);
    public void RequestExit();
}
```

> **M1 subset.** M1 exposes only `Input`, `Window`, `LoggerFactory`, `Time`, `DeltaTime`,
> `UnscaledDeltaTime`, `FrameCount`, `Fps`, `IsFirstTick`, `IsExiting`, `MaxDeltaTime`, and
> `RequestExit`. `Assets`, camera creation, and render-texture creation are the later contract;
> their milestones are in §1.6. M2 adds `Info` and `Stats`. M3 adds `Assets`; camera creation and
> render-texture creation remain later contract.

| Member | Thread | Notes |
| --- | --- | --- |
| `Assets` | Any | Shared catalog; see §6. M3. |
| `Input` | Tick | Input snapshot for the current tick. |
| `Window` | Any | Snapshot getters; setters are queued. |
| `LoggerFactory` | Any | App-owned factory; create category loggers from it. Never `null`. |
| `Info` | Any | Engine version, renderer/backend name, GPU name, Vulkan API version, HDR output flag, sample count, present mode, and validation state. Valid from `Setup`; a read may be stale by one tick. |
| `Time` | Tick | Absolute seconds from `Run` start. It is live during `Setup` and is not reset at the first tick. |
| `DeltaTime` | Tick | Clamped seconds since the previous tick; `0` in `Setup` and on the first tick. |
| `UnscaledDeltaTime` | Tick | Unclamped seconds since the previous tick; `0` in `Setup` and on the first tick. |
| `FrameCount` | Tick | `0` in `Setup`; `1` during the first `Tick`. |
| `Fps` | Tick | Ticks per second averaged over a 1-second sliding window; `0` before two ticks. |
| `IsFirstTick` | Tick | `true` only during the first `Tick`. |
| `IsExiting` | Tick | `true` after `RequestExit`, a window close, or an engine stop request. |
| `MaxDeltaTime` | Tick | The configured clamp, in seconds. |
| `Stats` | Any | Last completed frame's render statistics. Valid from `Setup`; a read may be stale by one tick. |
| `CreateCamera` | Tick | Perspective camera. Legal only during `Tick`; creating one while another is active is `InvalidOperationException`. |
| `CreateOrthographicCamera` | Tick | Orthographic camera. `height` is the visible world-space height. |
| `CreateRenderTextureAsync` | Tick | Creates an offscreen target and completes when the render thread has allocated it. Legal in `Setup` and `Tick`. |
| `RequestExit` | Tick | Requests a graceful stop. The current tick completes and the final frame is presented. |

`EngineContext` is stable for the app lifetime, but its per-frame properties are only meaningful
for the callback that observes them. Re-read `Time`, `DeltaTime`, and `Stats` each tick instead of
caching them; `Setup` sees a changing `Time` but `DeltaTime` is `0`.

`clearColor` is `null` by default, which means linear opaque black. `near` must be positive and
finite. `far` must be greater than `near`; `float.PositiveInfinity` is valid only for perspective
cameras. For an orthographic camera, `height` is the visible vertical extent; `far` must be finite.

`CreateCamera` with `target: null` records into the app's HDR scene target. The present pass applies
AgX and writes to the swapchain after the cameras finish. A camera with a `RenderTexture` target
writes to that target and can be sampled by a later camera.

### 3.5 Startup and shutdown sequence

```text
1. Build validates configuration. No OS or GPU work.
2. Run (main thread):
   a. SDL3 init, Vulkan-capable window creation, event pump start.
   b. Query the Vulkan instance extensions on the main thread and create the surface channel.
   c. Render thread starts: create the instance, request the native surface through the main-thread
      channel, create the device and swapchain, and publish Info.
   d. Tick thread starts with a SynchronizationContext. Setup therefore sees Info.
   e. IGame.Setup(ctx) is awaited on the tick thread.
   f. Loop: input snapshot, clock, IGame.Tick, frame publish; render consumes previous frame.
   g. Exit: stop accepting ticks. The tick thread publishes the final frame and runs
      IGame.Shutdown, then exits.
   h. Join the tick thread and complete the frame ring; the render thread drains and presents the
      final frame, disposes the GPU objects, and destroys the native surface through the channel
      while the main thread services it.
   i. Close the surface channel, dispose the platform (window, SDL3, Vulkan loader), and release
      the remaining engine state.
3. Run returns; Dispose releases the app object.
```

The surface handshake is bounded: a render-thread surface request and the main-thread wait for the
renderer each time out after 30 seconds. The tick thread does not start before the renderer is
ready, so `Setup` can read `Info`. M3 inserts the asset loader between (a) and (d).

Failure behavior:

- If startup fails before `Setup`, `Run` throws and nothing user-owned is called.
- A platform failure to create the Vulkan surface is a `PlatformException` with `RB2003`; when it
  arrives through the render-thread surface request it is rethrown from `Run` as a
  `GraphicsException` with `RB3001` and the platform failure as its `InnerException`.
- If `Setup` throws, `Shutdown` is **not** called; the engine releases what it owns and rethrows.
- If `Tick` throws, the loop stops, `Shutdown` is called, the renderer drains, and `Run` rethrows.
- If the renderer fails recoverably, it recreates the swapchain, surface, or device, logs, and
  continues. Recovery is bounded to three consecutive recoveries without a completed frame.
- If recovery fails or the bound is reached, the tick loop is stopped, `Shutdown` is called, and
  `Run` rethrows `GraphicsDeviceLostException` (`RB3002`) or the underlying `GraphicsException`.
  The window may close before the exception surfaces.
- If `Shutdown` throws, the exception is aggregated with any earlier failure.

## 4. Time and frames

`EngineContext` exposes time directly; there is no separate `EngineTime` object. The time members
are M1; the camera line in the example is later contract.

```csharp
using var camera = ctx.CreateCamera(...);

float spin = (float)ctx.Time;          // absolute seconds
float step = (float)ctx.DeltaTime;     // clamped seconds
double fps = ctx.Fps;
long frame = ctx.FrameCount;
```

Semantics:

- `Time` starts at `0` when `Run` begins and advances with `Stopwatch`. It is **live during
  `Setup`**; during `Tick` it is the value captured at the start of the tick. It is absolute, so a
  long stall (or a slow setup) makes it larger. It is not the sum of `DeltaTime`.
- `DeltaTime` is `min(now - lastTick, MaxDeltaTime)`. It is `0` in `Setup` and on the first tick.
- `UnscaledDeltaTime` is the raw interval, useful for diagnostics. It is also `0` on the first tick.
- `Fps` is a **1-second sliding-window average** of the tick rate, not an instantaneous
  `1 / DeltaTime`. It is `0` before two ticks have been sampled.
- `FrameCount` increments once per `Tick`, after `Setup`.
- `MaxDeltaTime` is fixed at build time; it prevents a debugger pause or a window drag from
  teleporting the simulation.
- There is no interpolation between ticks in v1. In M2 the render thread presents the newest
  completed tick's frame, which currently clears the linear scene target and runs the present pass.
  A game that needs smoother motion at low tick rates must interpolate its own render transforms or
  wait for the fixed-timestep milestone.
- `Tick` must not block. If it does, it delays the next input snapshot and the frame handoff.

### 4.1 Minimized and unfocused windows

When `Window.IsMinimized` or `Window.IsFocused` is false, the tick loop continues but is capped at
**30 FPS**. The cap applies on top of `WithTargetFps` and vsync/mailbox behavior: the effective cap
is the lower of the configured cap and 30 FPS. Input is still snapshotted and delivered, so a game
can pause, save, or keep a background simulation alive.

The renderer may idle when the window is minimized or its swapchain has no extent. The 30 FPS cap
exists to avoid burning CPU/GPU while the user is away, not to stop the simulation.

### 4.2 Frame lifetime

| Object | Created | Valid until |
| --- | --- | --- |
| `Camera` | `CreateCamera` during `Tick` | Its `Dispose`; must be before `Tick` returns |
| Render commands | `Camera.Draw*` | Camera `Dispose`; the sealed frame is consumed by the render thread |
| Shader snapshot | Each `Draw*` | Frame completion; later `SetUniform` calls do not affect it |
| `RenderTexture` | `CreateRenderTextureAsync` | `Dispose`; GPU destruction is deferred until frames release it |
| Asset reference | `Load`/`LoadData` | `Dispose` for engine assets; custom data has no GPU ownership |

A tick that creates no camera still produces a frame. The renderer presents a cleared image so the
swapchain does not starve.

## 5. Space, math, and color

### 5.1 Coordinate system

| Direction | Vector |
| --- | --- |
| Right | `+X` |
| Up | `+Y` |
| Forward | `-Z` |
| Camera at origin, identity rotation | Looks toward `-Z` |

This is the raylib/OpenGL/System.Numerics convention. A camera that should see the origin from
"behind" the scene sits at positive `Z` and keeps `Quaternion.Identity`. A camera at negative `Z`
must rotate 180 degrees around `Y`.

`Transform.Forward`, `Right`, and `Up` are derived from the rotation and follow this convention.
`Vector3.UnitZ` is **backward**, not forward.

glTF import is handled by the bake. glTF is right-handed and Y-up, so vertex positions and
normals pass through without an axis swap. A glTF asset whose authored "front" faces `+Z` may
need a 180-degree `Y` rotation at the model root; the bake can apply that per asset when the
sidecar requests it. Z-up sources such as Blender exports are converted to Y-up at bake time.

### 5.2 `Transform`

```csharp
public struct Transform {
    public Vector3 Position { get; set; }
    public Quaternion Rotation { get; set; }
    public Vector3 Scale { get; set; }

    public static Transform Identity { get; }

    public Transform(Vector3 position, Quaternion rotation, Vector3 scale);
    public Transform(Vector3 position);

    public readonly Matrix4x4 ToMatrix();
    public readonly Transform WithParent(Transform parent);
    public readonly Transform WithPosition(Vector3 position);
    public readonly Transform WithRotation(Quaternion rotation);
    public readonly Transform WithScale(Vector3 scale);

    public readonly Vector3 TransformPoint(Vector3 point);
    public readonly Vector3 TransformDirection(Vector3 direction);
    public readonly Transform Inverse();
    public static Transform Lerp(Transform a, Transform b, float t);

    public readonly Vector3 Forward => Vector3.Transform(-Vector3.UnitZ, Rotation);
    public readonly Vector3 Right => Vector3.Transform(Vector3.UnitX, Rotation);
    public readonly Vector3 Up => Vector3.Transform(Vector3.UnitY, Rotation);
}
```

Semantics:

- `Transform` is **world space** by default. It is not a node in a retained hierarchy.
- `WithParent(parent)` treats the receiver as parent-local and returns the world transform:
  `world = local * parent` under the row-vector convention. This matches
  `Matrix4x4.Multiply(localMatrix, parentMatrix)`.
- `ToMatrix()` is `Scale * Rotation * Translation`, the System.Numerics row-vector order.
- `WithParent` composes matrices and decomposes the result back to TRS. A parent with non-uniform
  scale plus a rotated child produces shear that TRS cannot represent exactly; the method throws
  `InvalidOperationException` rather than silently returning a wrong transform. Decompose-friendly
  hierarchies work as expected.
- `Rotation` is expected to be normalized. The constructor and `WithRotation` normalize it; direct
  property assignment is the caller's responsibility.
- `Transform` is a mutable value type. Copy semantics apply. Use the `With*` methods when a new
  value is intended.
- `Inverse()` throws `InvalidOperationException` when the scale has a zero component or the matrix
  is singular. It also throws when the matrix inverse contains shear and cannot be represented as a
  translation, rotation, and scale, for example a non-uniform scale combined with a rotation.
- Rotation helpers in §5.6 use degrees. The raw `Quaternion.CreateFromAxisAngle` and other
  `System.Numerics` methods use radians.

Example:

```csharp
var parent = new Transform {
    Position = new Vector3(0f, 1f, 0f),
    Rotation = Quaternion.Identity,
    Scale = Vector3.One,
};

var local = new Transform {
    Position = new Vector3(1f, 0f, 0f),
    Rotation = Quaternion.Identity,
    Scale = Vector3.One,
};

Transform world = local.WithParent(parent); // Position == (1, 1, 0)
```

### 5.3 `Color`

`Color` is RayBork's port of the linear `Color` type from
`Bork2d.Engine/Rendering/Color.cs`. It is an immutable linear RGBA value stored as a `Vector4`.
Components are normally in `[0, 1]`, but HDR rendering deliberately allows values above `1`.
There is no 8-bit sRGB `Color` and no implicit gamma conversion; the present pass applies AgX and
the output transfer function.

```csharp
public readonly struct Color : IEquatable<Color>, IFormattable {
    public Color(float r, float g, float b, float a);
    public Color(float r, float g, float b);

    public float R { get; }
    public float G { get; }
    public float B { get; }
    public float A { get; }

    public float this[int index] { get; } // 0..3; out of range throws
    public float Grayscale { get; }
    public float MaxColorComponent { get; }

    public static Color Lerp(Color a, Color b, float t);
    public static Color LerpUnclamped(Color a, Color b, float t);

    public void ToOkLab(out float l, out float a, out float b);
    public static Color FromOkLab(float l, float a, float b, float alpha = 1f);
    public static Color LerpOkLab(Color a, Color b, float t);
    public static Color LerpOkLabUnclamped(Color a, Color b, float t);

    public static Color HSVToRGB(float h, float s, float v);
    public static Color HSVToRGB(float h, float s, float v, bool hdr = true);
    public static void RGBToHSV(Color rgb, out float h, out float s, out float v);

    public static Color operator +(Color a, Color b);
    public static Color operator -(Color a, Color b);
    public static Color operator *(Color a, Color b);
    public static Color operator *(Color a, float b);
    public static Color operator *(float a, Color b);
    public static Color operator /(Color a, float b);

    public static implicit operator Vector4(Color color);
    public static implicit operator Color(Vector4 value);

    public static readonly Color Clear;
    public static readonly Color Black;
    public static readonly Color White;
    public static readonly Color Red;
    public static readonly Color Green;
    public static readonly Color Blue;
    public static readonly Color Yellow;
    public static readonly Color DarkCyan;
    public static readonly Color Transparent;
    // The full Unity palette is provided as static readonly fields.
}
```

- Equality is exact per-component `float` equality, matching the Bork2d type.
- `Color` serializes as four linear floats. The Bork2d `ColorMarshaler` is reused.
- `HSVToRGB` uses Unity-style HSV components with `h`, `s`, and `v` in `[0, 1]`. Hue wraps around
  the color wheel, saturation is clamped to `[0, 1]`, and `v` is clamped to `[0, 1]` only when
  `hdr` is false. With `hdr: true`, values above `1` are preserved for HDR colors.
- `Color` values are **authored in linear space**; every preset field is a linear value. For example
  `Color.DarkCyan` is `(0, 0.5, 0.5, 1)`, not an sRGB byte triple.
- The final output path applies AgX tone mapping and then the swapchain transfer function. Sampling
  an sRGB texture decodes to linear before lighting. Sampling a linear texture stays linear.

### 5.4 `Rect`

```csharp
public readonly record struct Rect(float X, float Y, float Width, float Height) {
    public float Right { get; }
    public float Bottom { get; }
    public Vector2 Position { get; }
    public Vector2 Size { get; }

    public bool Contains(Vector2 point);
    public static Rect FromSize(float width, float height);
}
```

`Rect` is used for viewports, destination rectangles, monitor bounds, and scissor-like regions.
Screen rectangles use a top-left origin.

### 5.5 Rays, bounds, and collision

RayBork uses `System.Numerics.Plane` and the engine's own `RayBork.BoundingBox` value type directly,
and adds a small `Collision` helper plus a `RayCollision` result. The M5 collision milestone adds the
ray and sphere helpers and any additional bounds types it needs.

```csharp
public readonly record struct BoundingBox(Vector3 Min, Vector3 Max);

public readonly struct RayCollision {
    public bool Hit { get; }
    public float Distance { get; }
    public Vector3 Point { get; }
    public Vector3 Normal { get; }
}

public static class Collision {
    public static bool CheckSpheres(Vector3 centerA, float radiusA, Vector3 centerB, float radiusB);
    public static bool CheckBoxes(BoundingBox a, BoundingBox b);
    public static bool CheckBoxSphere(BoundingBox box, Vector3 center, float radius);

    public static RayCollision GetRaySphere(Ray ray, Vector3 center, float radius);
    public static RayCollision GetRayBox(Ray ray, BoundingBox box);
    public static RayCollision GetRayTriangle(Ray ray, Vector3 a, Vector3 b, Vector3 c);
    public static RayCollision GetRayPlane(Ray ray, Plane plane);
    public static RayCollision GetRayMesh(Ray ray, Mesh mesh, Matrix4x4 transform);
}
```

`GetRayMesh` requires the mesh's CPU positions. v1 retains CPU positions for loaded meshes;
procedural primitives may expose bounds only until their CPU data is wired in. If a mesh has no CPU
positions, `GetRayMesh` throws `NotSupportedException` rather than returning a false miss.

Collision helpers are pure math and thread-safe. They do not query the renderer or the scene.

### 5.6 Vector and quaternion extensions

RayBork extends `System.Numerics.Vector3` and `Quaternion` with Unity-style members using the C#
`extension` syntax. The extensions live in the flat `RayBork` namespace and are available through
the normal `using RayBork;`.

User-facing angle arguments are **degrees**. The extension methods convert to radians internally;
raw `System.Numerics` methods remain radians.

```csharp
public static class Vector3Extensions {
    extension(Vector3 value) {
        public float Magnitude => value.Length();
        public float SqrMagnitude => value.LengthSquared();
        public Vector3 Normalized => Vector3.Normalize(value);

        public Vector3 WithX(float x) => new(x, value.Y, value.Z);
        public Vector3 WithY(float y) => new(value.X, y, value.Z);
        public Vector3 WithZ(float z) => new(value.X, value.Y, z);
        public Vector3 Xz => new(value.X, 0f, value.Z);

        public float Angle(Vector3 other);                 // degrees
        public float SignedAngle(Vector3 other, Vector3 axis); // degrees
        public Vector3 Project(Vector3 normal);
        public Vector3 ProjectOnPlane(Vector3 normal);
        public Vector3 Reflect(Vector3 normal);
        public Vector3 ClampMagnitude(float maxLength);
        public Vector3 Rotated(Quaternion rotation);
    }
}

public static class QuaternionExtensions {
    private const float DEGREES_TO_RADIANS = MathF.PI / 180f;
    private const float RADIANS_TO_DEGREES = 180f / MathF.PI;

    extension(Quaternion value) {
        public static Quaternion Euler(float x, float y, float z);
        public static Quaternion AngleAxis(float degrees, Vector3 axis);
        public static Quaternion LookRotation(Vector3 forward, Vector3 upwards);
        public static Quaternion FromToRotation(Vector3 from, Vector3 to);

        public Quaternion Normalized => Quaternion.Normalize(value);
    }
}
```

Additional members follow the same pattern: `EulerAngles` returns degrees, `Angle(Quaternion)`
returns degrees, `ToAngleAxis` returns a degree angle and axis, and `RotateTowards`/`Slerp`
interpolate with degree-based angular limits. There is no `Inverse` extension property; use the
static `Quaternion.Inverse(q)` for quaternion inversion.

Unity-style static `Vector3` members are also provided through extension properties: `Up`, `Down`,
`Left`, `Right`, `Forward`, and `Back`. `Forward` is `-Z` to match the world convention; `Back` is
`+Z`. `Vector3.Zero` and `Vector3.One` come from `System.Numerics` and are not shadowed.

Example:

```csharp
Quaternion turn = Quaternion.Euler(0f, 90f, 0f);
Vector3 forward = Vector3.Transform(-Vector3.UnitZ, turn);
Vector3 euler = turn.EulerAngles;    // degrees
float angle = a.Angle(b);            // degrees
```

## 6. Assets

### 6.1 `IAsset` and `IAssetCatalog`

```csharp
public interface IAsset : IDisposable {
    string Path { get; }
    bool IsDisposed { get; }
}

public interface IAssetCatalog {
    Task<T> Load<T>(string path, CancellationToken cancellationToken = default);
    Task<byte[]> LoadData(string path, CancellationToken cancellationToken = default);
    bool Exists(string path);
}
```

There is one typed loader. `Load<T>` handles both engine assets and registered custom data:

| `T` | Behavior |
| --- | --- |
| `Mesh`, `Texture`, `Shader` (M3) | Loads a shared, reference-counted engine asset. Completes after GPU upload. `T` is an `IAsset` and must be disposed once per successful load. |
| `Model` (M4), `Font` (M6), later `AudioClip` | The same shared, reference-counted contract; the types arrive with their milestones. |
| A custom type registered with `WithAssetType<T>()` | Deserializes a MessagePack data entry through the compiled `[GenerateShape]`. Returns a fresh caller-owned instance per call; no reference counting and no `Dispose`. |
| Any other type | `NotSupportedException` naming the type and asking for `WithAssetType<T>()`. |

`IAsset.Path` is the canonical path with a leading `/`; `Load` accepts the same path with or without
it. A disposed asset keeps `Path` and `IsDisposed` readable for diagnostics; every other member
throws `ObjectDisposedException`.

A custom type registration is explicit on the builder:

```csharp
using PolyType;

[GenerateShape]
public sealed partial class LevelConfig {
    public string Name { get; init; } = "";
    public int Difficulty { get; init; }
}
```

```csharp
using var app = new RayBorkAppBuilder()
    .WithGame(game)
    .WithAssetType<LevelConfig>()
    .WithAssetsPack(packPath)
    .Build();

LevelConfig level = await ctx.Assets.Load<LevelConfig>("/data/level");
ReadOnlySpan<byte> raw = await ctx.Assets.LoadData("/data/level-raw");
```

`WithAssetType<T>()` requires `T : IShapeable<T>` and registers a non-reflective loader delegate. The
same type must be registered in the bake with `RegisterAsset<T>()`; the two registrations are
independent because the runtime never scans assemblies.

`Load<T>` is not cached for custom data; every call returns a new instance. Engine assets remain
cached and reference-counted by `(canonical path, engine asset type)`.

`LoadData` returns a copy of a raw or decompressed payload for custom formats and tooling. There is
no `TryLoad` in v1: catch `AssetNotFoundException` when absence is expected.

### 6.2 Asset paths

| Rule | Example |
| --- | --- |
| Optional leading `/` | `/models/castle` and `models/castle` are the same |
| Forward slashes only | `/textures/castle/albedo` |
| Case-insensitive input | `/Textures/Castle` resolves `/textures/castle` |
| Canonical storage is lowercase | The bake and pack index store lowercase |
| No extension | `/shaders/castle`, not `/shaders/castle.glsl` |
| No `.` or `..` segments | |
| No trailing slash | |
| Segments use ASCII lowercase letters, digits, `_`, `-` | Bork2d-compatible canonical grammar |
| Maximum length is 4096 bytes | |

The runtime normalizes input by stripping one optional leading slash and lowercasing with invariant
rules. Backslashes are rejected; use forward slashes. The canonical path is lowercase ASCII with `/`
separators. A path that normalizes to the same key as another entry is a collision; the bake reports
it and does not publish the pack.

A canonical path is an asset identity. Two different source files cannot produce the same path in
one pack. Two packs may contain the same path; the later-mounted pack wins. The public `IAsset.Path`
is the canonical path with a leading `/`; the pack index stores it without one.

### 6.3 Asset types

| Type | Baked from | Runtime form | Milestone |
| --- | --- | --- | --- |
| `Mesh` | One primitive from glTF 2.0 or OBJ | GPU vertex/index buffers; CPU positions retained for collision | M3 |
| `Texture` | PNG, JPEG, TGA, BMP | GPU-ready KTX2 payload, mips, sRGB flag, sampler state | M3 |
| `Shader` | GLSL vertex + fragment pair | SPIR-V plus sidecar metadata, entry points, and a default render state | M3 |
| `Model` | glTF 2.0, OBJ | Multi-part model with meshes, local transforms, shader references | M4 (baked in M3) |
| `Font` | TTF, OTF | Face metrics plus runtime-generated MSDF glyphs | M6 (baked in M3) |
| Custom data | TOML source | MessagePack payload deserialized through `Load<T>` after `WithAssetType<T>()` | M3 |
| Raw data | Any processor output or raw source | Opaque bytes through `LoadData` | M3 |

glTF and OBJ are **source formats only**. The bake converts them to the engine's packed mesh/model
format; the runtime never parses glTF or OBJ. The engine texture payload is a complete KTX2 2.0
file; PNG/JPEG/TGA/BMP are the M3 source formats and the runtime only reads the packed KTX2
payload. KTX2 source passthrough is a later addition, not an M3 source format.

`RenderTexture` is created at runtime and is not a pack asset. `AudioClip` is post-1.0.

`Model` is the normal entry point for a glTF/OBJ file from M4. A model's parts are separate `Mesh`
assets at `<model-path>/<part>`; M3 emits and validates both the model descriptor and the part mesh
entries. `Load<Model>` will load the model's referenced meshes and shaders and hold its own
references to them; disposing the model releases those references. If the game also loaded one of
those parts directly, it holds a separate reference and must dispose it separately.

`Load<Mesh>` is for a standalone mesh-part asset. Loading a multi-part model path as `Mesh` throws
`AssetTypeMismatchException`.

Texture formats in M3 are `Rgba8Unorm`, `Rgba8Srgb`, `Bc7Unorm`, `Bc7Srgb`, `Bc5Unorm`, and
`R8Unorm`. `Rgba16Float` and `Depth32Float` are render-target formats and are not accepted in a
packed texture in M3. The bake writes an sRGB transfer function into the KTX2 data format
descriptor for the sRGB formats; the runtime creates the matching Vulkan format.

### 6.4 Identity, reference counting, and lifetime

`Load<T>` for engine assets is cached by `(canonical path, engine asset type)`:

- The first successful load creates the asset and returns it.
- A second load of the same path and type increments the reference count and returns the **same
  object**.
- A load of the same path with a different engine asset type throws `AssetTypeMismatchException`.
- `Dispose` releases one reference. Once the count reaches zero, the public object becomes
  disposed and GPU destruction is queued on the render thread.
- A frame packet that uses an engine asset holds its own reference for the lifetime of that frame.
  Recording a draw calls an internal retain on each referenced asset; the frame ring releases the
  hold after the frame is presented. Disposing an asset immediately after recording a draw is
  therefore safe; the frame keeps the GPU resource alive until its fence signals. M3 implements and
  tests the retain/release mechanism directly; M4 wires `Camera.Draw*` into it.
- A device recovery re-uploads the live GPU resources from the retained CPU descriptors before
  frame submission resumes. A public asset object stays valid across the recovery.
- Using a disposed engine asset (reading a property or passing it to a draw) throws
  `ObjectDisposedException`.

Because `Load<T>` returns the shared object for engine assets, `Dispose` must be called **exactly
once for each successful engine-asset load**. The engine cannot distinguish a double-dispose from a
second owner's release; a second `Dispose` after the count reaches zero throws. A debug build also
checks the count under the catalog lock.

A custom data type loaded with `Load<T>` is not an `IAsset`. It is a caller-owned value, has no
cache, and has no `Dispose`. It never keeps a GPU resource alive.

Loading the same engine path concurrently is coalesced: one load runs, all callers receive the same
result and each caller owns one reference. If the load fails, every caller observes the failure
and no reference is created. A caller that cancels its wait does not acquire a reference, even if
the shared load later succeeds. Custom data loads are independent; each call produces a new instance.

`LoadData` returns the decompressed bytes of the winning entry. For a typed engine asset entry that
is the serialized descriptor, not the GPU payload; tooling that needs a raw texture or mesh payload
uses the payload entry path emitted by the bake. For a `[raybork]` data entry it is the MessagePack
bytes.

### 6.5 Load pipeline and errors

```text
Load<T>(path)
  -> resolve winning entry in the mounted pack stack
  -> engine asset?  -> read/decompress -> descriptor -> GPU upload on the render thread
                    -> wait -> refcount -> return
  -> registered?    -> read/decompress -> MessagePack shape -> fresh instance -> return
  -> otherwise      -> NotSupportedException
```

Consequences:

- `Load<T>` for engine assets is asynchronous because pack I/O, decompression, and GPU upload are
  asynchronous. The read runs on the dedicated `RayBork.Asset` thread; the upload runs on the render
  thread through an internal queue, and the load task completes only after the upload succeeds.
- Engine assets do not return half-uploaded, so the first draw never triggers a hidden upload.
- Custom data is CPU-only and can complete without the renderer; it is still a `Task` because pack
  I/O runs on the loader.
- `Load` may be called from any thread, but awaiting it from the tick thread blocks that tick's
  continuation, not the render thread. Load in `Setup` unless a hitch is acceptable.
- Cancellation before completion releases all partial resources and throws
  `OperationCanceledException`. A canceled caller does not acquire a reference. Cancellation after
  the asset is returned has no effect. A canceled GPU upload destroys the just-created resource on
  the render thread instead of leaking it.
- A failed load is not cached as a permanently failed entry; retrying is allowed.
- The loader is bounded: pack payload reads validate every length and offset before allocation, and
  a decompressed payload larger than its declared size is an `AssetLoadException`.

| Exception | When |
| --- | --- |
| `AssetException` | Base type for every asset pipeline failure with a diagnostic code. |
| `AssetPackException` | The pack is missing, truncated, has a bad magic/version, fails a hash, or has an invalid index. |
| `AssetNotFoundException` | The path is not in any mounted pack. |
| `AssetTypeMismatchException` | The path exists but was baked as a different engine asset type. |
| `AssetLoadException` | The payload is corrupt, decode fails, a custom-data shape fails, or GPU upload fails. |
| `OperationCanceledException` | The caller's token was canceled before completion. |
| `ObjectDisposedException` | The catalog or a required dependency is already disposed. |
| `NotSupportedException` | `T` is neither an engine asset nor registered with `WithAssetType<T>()`. |

`AssetPackException` is a startup failure for a pack passed to `WithAssetsPack`. A corrupt entry
inside an otherwise valid pack is an `AssetLoadException` for that path.

### 6.6 Pack mounting and built-in assets

- The engine's built-in pack is mounted first, below every user pack. In M3 it contains the
  placeholder white and error textures and the present shader pair, produced by the same
  `IAssetProcessor` pipeline as game content. The default draw shader, primitive meshes, and error
  glyph arrive with M4 and M6.
- `WithAssetsPack` mounts user packs in call order after the built-in pack. Later packs shadow
  earlier packs for the same canonical path.
- The winning entry is the last mounted pack that contains the path. A shadowed entry is never
  partially used.
- v1 has no runtime pack mount/unmount API. Packs are fixed when the app is built.
- A missing pack file is an `AssetPackException` from `Run`, not from `Build`, because the bake may
  produce the pack as part of the build.
- Relative pack paths resolve against `AppContext.BaseDirectory`, so a published game finds its
  pack next to the executable.
- The built-in pack is generated at engine build time by a JIT-only generator that references
  `RayBork.Bake`, runs the ordinary `bake` command over the checked-in built-in sources into a
  temporary directory, and emits a C# byte array that `RayBork.Engine` compiles into the assembly.
  The generator never enters a runtime project or an AOT publish graph, and no generated pack is
  committed.
- Mounting validates every pack before it becomes visible. A failed mount leaves the previous pack
  stack unchanged, and startup fails with `AssetPackException`.

## 7. Rendering

### 7.1 `Camera`

```csharp
public sealed class Camera : IDisposable {
    public Transform Transform { get; }
    public CameraProjection Projection { get; }
    public float FieldOfView { get; }
    public float OrthographicHeight { get; }
    public float Near { get; }
    public float Far { get; }
    public ClearMode ClearMode { get; }
    public Color ClearColor { get; }
    public RenderTexture? Target { get; }
    public Rect Viewport { get; }
    public Matrix4x4 ViewMatrix { get; }
    public Matrix4x4 ProjectionMatrix { get; }
    public Matrix4x4 ViewProjectionMatrix { get; }
    public BoundingFrustum Frustum { get; }
    public bool IsDisposed { get; }

    public void DrawModel(Model model, Transform transform);
    public void DrawModel(
        Model model,
        Transform transform,
        Shader shader,
        RenderState? renderState = null,
        Color? tint = null);

    public void DrawMesh(
        Mesh mesh,
        Transform transform,
        Shader shader,
        RenderState? renderState = null,
        Color? tint = null);

    public void DrawInstanced(
        Mesh mesh,
        Shader shader,
        ReadOnlySpan<Transform> transforms,
        RenderState? renderState = null,
        Color? tint = null);

    public void DrawModelInstanced(
        Model model,
        Shader shader,
        ReadOnlySpan<Transform> transforms,
        RenderState? renderState = null,
        Color? tint = null);

    public void DrawLine(Vector3 start, Vector3 end, Color color);
    public void DrawLines(ReadOnlySpan<Vector3> points, Color color);
    public void DrawPoint(Vector3 position, Color color, float size = 1f);
    public void DrawTriangle(Vector3 a, Vector3 b, Vector3 c, Color color);
    public void DrawCube(Vector3 position, Vector3 size, Color color);
    public void DrawSphere(Vector3 center, float radius, Color color);
    public void DrawCylinder(Vector3 position, float radius, float height, Color color, int sides = 16);
    public void DrawPlane(Vector3 center, Vector2 size, Color color);
    public void DrawGrid(int slices, float spacing, Color color);
    public void DrawWireCube(Vector3 position, Vector3 size, Color color);
    public void DrawWireSphere(Vector3 center, float radius, Color color);
    public void DrawBillboard(
        Texture texture,
        Vector3 position,
        Vector2 size,
        Color color,
        bool faceCamera = true);
    public void DrawTexture(Texture texture, Rect destination, Color? tint = null);
    public void DrawText(
        Font font,
        ReadOnlySpan<char> text,
        Transform transform,
        float size,
        Color color,
        TextAlignment alignment = TextAlignment.Left,
        TextWrapping wrapping = TextWrapping.None,
        float maxWidth = 0f);
    public void DrawTextBillboard(
        Font font,
        ReadOnlySpan<char> text,
        Vector3 position,
        float size,
        Color color,
        TextAlignment alignment = TextAlignment.Left);

    public Ray ScreenToWorldRay(Vector2 screenPosition);
    public Vector2 WorldToScreen(Vector3 worldPosition);

    public void Dispose();
}
```

A camera is a **frame-scoped recorder**, not a GPU object and not a reusable scene camera.

- Created on the tick thread during `Tick`; must be disposed before `Tick` returns.
- `Dispose` seals the pass and submits it to the render thread. It is idempotent.
- After `Dispose`, any draw or property access throws `ObjectDisposedException`.
- Creating a camera while another camera is active throws `InvalidOperationException`; cameras are
  sequential, not nested. Multiple cameras in one tick are created and disposed one after another.
- If a camera is not disposed when `Tick` returns, the engine throws `InvalidOperationException`
  naming the leak. In a debug build this happens before the frame is published; in release the
  frame is failed rather than silently rendered without the pass.
- The camera does not copy the whole scene. It records value snapshots of the draws and shader
  state at each call. The renderer never reads live game objects.
- `Target == null` means the app's linear HDR scene target. The present pass applies AgX and writes
  to the swapchain after all cameras finish. A non-null target writes to that offscreen target.
- MSAA is a property of the target. The swapchain uses the builder's `WithMSAA` sample count; a
  `RenderTexture` uses its own `SampleCount`. The camera records into whichever target it owns.

Properties:

| Property | Meaning |
| --- | --- |
| `Transform` | World-space camera transform. The camera looks down local `-Z`. |
| `FieldOfView` | Vertical field of view in degrees; perspective only. |
| `OrthographicHeight` | Visible world-space height; orthographic only. |
| `Near`, `Far` | Clip distances. `Far` may be `+infinity` for perspective. |
| `ClearMode` | Which buffers this pass clears. |
| `ClearColor` | Linear color used when `ClearMode.Color` is set. |
| `Target` | Offscreen target, or `null` for the HDR scene target. |
| `Viewport` | Pixel rectangle in the target. Defaults to the full target. |
| `Frustum` | World-space frustum derived from the view-projection matrix; useful for custom culling. |

`CreateCamera` sets the aspect ratio from the viewport (or target/window size). The projection is
built with reverse-Z; the `ProjectionMatrix` already contains Vulkan's Y-flip. Custom code that
builds its own projection must preserve both conventions if it wants to match the engine's depth
and screen-space helpers.

`ScreenToWorldRay` takes **render pixels** with a top-left origin. `Input.MousePosition` is in
window coordinates, so convert it with `ctx.Window.RenderWidth / (float)ctx.Window.Width` when
`Width` is nonzero. `DpiScale` is the OS display scale, not the pixel ratio, and is not the
conversion factor for this call. `WorldToScreen` returns render pixels.

`DrawModel(model, transform)` uses each model part's baked shader and shader state. This is the
normal path for an imported model with authored per-part shading.

`DrawModel(model, transform, shader)` overrides the shader for **every** part. The current shader
state at the time of the call is captured for every part. Baked per-part textures and uniforms are
ignored; set the uniforms you need before the call. This overload is for simple models and custom
shading, not for preserving a multi-material glTF's authored look.

`tint` writes the per-instance linear color field. Built-in shaders multiply it into the output. A
custom shader sees it as `rbInstance.Color` and must use it explicitly.

`renderState` overrides the shader's default render state for this draw only. `null` uses
`Shader.DefaultRenderState`. The override is part of the batching key.

### 7.2 Projection, clear, HDR, and tone mapping

```csharp
public enum CameraProjection {
    Perspective,
    Orthographic,
}

[Flags]
public enum ClearMode {
    None = 0,
    Color = 1 << 0,
    Depth = 1 << 1,
    Stencil = 1 << 2,
    ColorAndDepth = Color | Depth,
    All = Color | Depth | Stencil,
}

public enum TonemappingMode {
    AgX,
    None,
}
```

- Default camera clear is `ClearMode.ColorAndDepth`. A 3D frame that uses only `ClearMode.Color`
  will reuse the previous frame's depth buffer and must be treated as an overlay pass.
- Depth clears to `0.0`. Color clears to `ClearColor`. Stencil clears to `0`.
- The depth test defaults to `GreaterOrEqual`; depth writes are on for opaque draws.
- Perspective supports an infinite far plane. The projection maps the near plane to `1.0` and
  infinity to `0.0`.
- Orthographic supports reverse-Z too, but requires a finite far plane.
- The viewport is in pixels, top-left origin, and is clamped to the target. An invalid or empty
  viewport throws `ArgumentException`.
- Multiple cameras can render to the same target. The first camera usually clears; later cameras
  choose `ClearMode.None` or `ClearMode.Depth` for overlays.
- A camera that targets a `RenderTexture` and a later camera that samples that texture in the same
  tick are ordered by creation order; the renderer inserts the required image barrier. Sampling a
  target inside the same pass that writes it is invalid and is reported as a validation error.

In M2, before `Camera` exists, `WithClearColor` is the scene clear color. Cameras add per-camera
clear control in M4; a camera's own clear color then takes precedence.

**HDR chain.** The scene target is linear `R16G16B16A16_SFLOAT` color with a `D32_SFLOAT` depth
attachment. The present pass applies exposure, `TonemappingMode`, and the output encoding. The
default is `AgX` at `0` EV. `TonemappingMode.None` skips the tone curve and still writes through the
selected output encoding. Exposure is applied as `2^EV` before tone mapping.

**Swapchain selection.** The cached SDL3 window HDR state drives the choice. When the window has HDR
enabled, the renderer selects `R16G16B16A16_SFLOAT` with
`VK_COLOR_SPACE_EXTENDED_SRGB_LINEAR_EXT` (scRGB) when the surface advertises that pair. If the
surface advertises the float format only with `VK_COLOR_SPACE_SRGB_NONLINEAR_KHR`, that pair is used
with a warning. If the surface advertises no scRGB float pair, the renderer falls back to the SDR
order and reports `HdrOutput = false`.

The SDR order is `B8G8R8A8_SRGB`, `R8G8B8A8_SRGB`, `B8G8R8A8_UNORM`, `R8G8B8A8_UNORM` (manual sRGB
encode in the present shader), then `R16G16B16A16_SFLOAT` (linear clamp). HDR10
(`A2B10G10R10_UNORM_PACK32`, PQ) is deliberately deferred and is never selected in M2. The selected
format token is reported by `EngineInfo.SwapchainFormat`; `HdrOutput` is true only when the window
reports HDR and the selected pair presents through the scRGB HDR path.

**HDR output.** The scRGB present path scales display-linear color by the window's SDL SDR white
level and does not clamp, so values above the SDR white point use the display headroom. The cached
HDR state also carries the headroom for diagnostics. A window HDR state change, or a change to the
SDR white level while HDR is active, recreates the presentation path.

**MSAA.** The requested sample count comes from `WithMSAA` (default 4). The effective count is the
highest device-supported count that does not exceed the request, or `1` when none is supported, and
a fallback logs a warning. The scene target color and depth attachments use the effective count;
the swapchain images are single-sample. Scene color is resolved into a single-sample sampled image
before the present pass. Depth is cleared to `0.0` and the reverse-Z state is defined; multisampled
depth is not resolved in M2 because nothing samples depth before M4/M8. A `RenderTexture` uses its
own `SampleCount` (M8).

**Present mode.** The default is `Mailbox`, with automatic fallback to `Fifo` when the surface does
not advertise it; a requested `Fifo` falls back to `Mailbox` when FIFO is unavailable. `Immediate` is
used only when explicitly selected, and an unavailable `Immediate` mode fails startup with `RB3001`
instead of falling back. Mailbox is not a guarantee of tearing-free presentation on every driver;
the selected mode is reported by `EngineInfo.PresentMode`.

**Reverse-Z.** The scene target clears depth to `0.0`. Depth-tested pipelines use `GreaterOrEqual`,
enable depth test and depth write, and use the `[0, 1]` depth range. The M2 present triangle is not
depth-tested; M4's geometry pipelines consume the state.

Reverse-Z is a whole-pipeline convention. Custom projection code must not mix in a
`[-1,1]`-style OpenGL projection or the depth test will be wrong. The engine exposes no
`CreatePerspective` helper in v1; custom projections are the caller's responsibility.

### 7.3 `Shader` and `SetUniform`

```csharp
public sealed class Shader : IAsset {
    public string Path { get; }
    public ShaderStageMask Stages { get; }
    public RenderState DefaultRenderState { get; }
    public IReadOnlyList<ShaderUniform> Uniforms { get; }
    public IReadOnlyList<ShaderTextureBinding> Textures { get; }
    public bool IsDisposed { get; }

    public void SetUniform(string name, float value);
    public void SetUniform(string name, int value);
    public void SetUniform(string name, uint value);
    public void SetUniform(string name, bool value);
    public void SetUniform(string name, Vector2 value);
    public void SetUniform(string name, Vector3 value);
    public void SetUniform(string name, Vector4 value);
    public void SetUniform(string name, Quaternion value);
    public void SetUniform(string name, Matrix4x4 value);
    public void SetUniform(string name, Color value);
    public void SetUniform(string name, Texture? texture);

    public void ResetUniforms();
    public void Dispose();
}

[Flags]
public enum ShaderStageMask {
    None = 0,
    Vertex = 1 << 0,
    Fragment = 1 << 1,
}

public enum ShaderUniformType {
    Float,
    Int,
    UInt,
    Bool,
    Vector2,
    Vector3,
    Vector4,
    Matrix4x4,
    Texture2D,
}

public readonly record struct ShaderUniform(string Name, ShaderUniformType Type);

public readonly record struct ShaderTextureBinding(string Name, int Index);
```

> **M3 subset.** M3 loads and exposes the metadata members only: `Path`, `Stages`,
> `DefaultRenderState`, `Uniforms`, `Textures`, `IsDisposed`, and `Dispose`. `SetUniform` and
> `ResetUniforms` are M4. `Uniforms` and `Textures` come from the bake sidecar metadata, not from
> runtime SPIR-V reflection.

A `Shader` is a complete vertex+fragment program. It owns a **mutable uniform cursor**:

1. `SetUniform` writes the current value for the named uniform.
2. Each `Draw*` call snapshots the cursor into the frame packet.
3. Later `SetUniform` calls affect later draws only.
4. The renderer packs the snapshot into the per-instance parameter block and resolves textures
   to bindless indices.

This is the central ergonomic rule:

```csharp
shader.SetUniform("mainTexture", textureA);
camera.DrawMesh(mesh, transformA, shader);

shader.SetUniform("mainTexture", textureB);
camera.DrawMesh(mesh, transformB, shader);
```

Both draws use the same mesh, shader, and render state, so they become one instanced draw with two
instances and two bindless textures. No `Material` object is involved.

Rules and errors:

- `name` must match a reflected uniform name from the bake. Unknown names throw `ArgumentException`.
- The value type must match the reflected type. A mismatch throws `ArgumentException`. `Color`
  maps to `vec4`; `Quaternion` maps to `vec4`; `bool` maps to `uint`.
- A `Texture` value must not be disposed at the moment of the call. `null` binds the built-in white
  texture; this is useful for optional texture uniforms and for shaders that branch on a flag.
- Setting a texture records an engine reference in the shader cursor. Disposing the public texture
  afterwards does not invalidate the cursor; overwrite the uniform, call `ResetUniforms`, or
  dispose the shader to release the cursor's reference. Passing a texture that was already disposed
  to `SetUniform` throws `ObjectDisposedException`.
- The shader's default values come from the bake sidecar. `ResetUniforms` restores those defaults.
- `SetUniform` is tick-thread only. It is legal in `Setup`, `Tick`, and `Shutdown`. Calling it from
  the render thread, main thread, or a user background thread throws `InvalidOperationException`.
- Shader state is shared by all draws that use the shader. There is no per-camera or per-thread
  state. Interleaved systems must set the state immediately before the draws that need it; the
  snapshot rule makes this correct even when the render thread runs in parallel.
- `DrawInstanced` uses one snapshot for all instances in the call. To vary a texture or uniform
  per instance, record separate `DrawMesh`/`DrawModel` calls with the automatic batcher.
- `Shader` uniforms are not global frame uniforms. Time, camera matrices, viewport, and similar
  values are supplied by the engine's frame block and are not settable through `SetUniform`.
- `SetUniform` on a disposed shader throws `ObjectDisposedException`.
- `Dispose` follows the asset reference-count rules. A recorded frame keeps the shader and its
  textures alive.

`Uniforms` and `Textures` expose bake reflection metadata for diagnostics and tooling. They are
read-only and do not reflect the current values. There is no public `GetUniform` in v1; if it is
added, it will read the cursor, not the GPU.

### 7.4 Shader ABI

The bake generates a prologue for every shader so user GLSL does not declare the engine plumbing.
The exact generated identifiers are part of the v1 compatibility contract and are stabilized
before 1.0. The current target is:

**Descriptor set 0**

| Binding | Type | Contents |
| --- | --- | --- |
| 0 | uniform buffer | `RayBorkFrame`: view-projection, view, projection, camera position, viewport, time/delta/frame, screen size, exposure |
| 1 | bindless combined image samplers | `RayBorkTextures[]`, capacity queried from the device (4096 target) |
| 2 | storage buffer | `RayBorkInstances[]`, one record per instance, plus reflected user uniforms |

**Fixed mesh vertex layout**

| Location | Type | Semantic |
| --- | --- | --- |
| 0 | `vec3` | position |
| 1 | `vec3` | normal |
| 2 | `vec2` | uv |
| 3 | `vec2` | uv1 |
| 4 | `vec2` | uv2 |
| 5 | `vec2` | uv3 |
| 6 | `vec4` | color (linear) |
| 7 | `vec4` | tangent |

Only the attributes a shader declares are bound. A custom shader may omit normals, UV sets, color,
or tangents, but the mesh layout remains fixed. Custom vertex layouts are post-1.0.

**Entry points**

Vertex and fragment stages use the fixed entry points `main_vertex` and `main_fragment`. The bake
compiles both stages with those entry points and validates that they exist. A shader sidecar may
restate them for clarity, but it cannot rename them in v1.

**Generated accessors**

The prologue defines the frame block and a stage-specific `rbInstance` expression:

- `rbFrame` expands to `RayBorkFrame`.
- In the vertex stage, `rbInstance` expands to `RayBorkInstances[gl_InstanceIndex]`.
- In the fragment stage, the prologue emits a `flat` instance-index varying and `rbInstance`
  expands to `RayBorkInstances[rbInstanceIndex]`.
- The prologue enables `GL_EXT_nonuniform_qualifier` for the bindless texture lookups.
- Each non-texture uniform name is mapped to the corresponding field of `rbInstance`. For example,
  a reflected `vec4 uTint` becomes a field and `uTint` expands to `rbInstance.uTint`.
- Each texture uniform name is mapped to a bindless lookup using the instance's texture index. For
  example, `mainTexture` expands to `RayBorkTextures[nonuniformEXT(rbInstance.mainTextureIndex)]`.

Do not declare `RayBorkFrame`, `RayBorkTextures`, `RayBorkInstances`, `rbInstance`, the generated
varyings, or any uniform/texture that the sidecar reflects. The bake rejects redeclarations with a
diagnostic instead of emitting GLSL that fails later.

The sidecar is the source of truth for user uniforms and textures in v1. The bake generates the
prologue fields and macros from this list, so the GLSL uses the names without declaring them. If a
name is declared in both the sidecar and the GLSL, the bake reports a redeclaration diagnostic.
Declaring reflected uniforms in GLSL instead is a post-1.0 authoring option.

```toml
# /shaders/castle.toml
[shader]
vertex = "castle.vert"
fragment = "castle.frag"
vertex_entry = "main_vertex"
fragment_entry = "main_fragment"

[[shader.uniforms]]
name = "uTint"
type = "vec4"
default = [1.0, 1.0, 1.0, 1.0]

[[shader.textures]]
name = "mainTexture"

[shader.render_state]
blend = "opaque"
cull = "back"
depth_test = "greater_equal"
depth_write = true
```

A complete custom pair:

```glsl
// castle.vert
#version 450

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aUv;

layout(location = 0) out vec2 vUv;
layout(location = 1) out vec3 vNormal;

void main_vertex() {
    vec4 world = rbInstance.Transform * vec4(aPosition, 1.0);
    gl_Position = rbFrame.ViewProjection * world;
    vUv = aUv;
    vNormal = normalize(mat3(rbInstance.Transform) * aNormal);
}
```

```glsl
// castle.frag
#version 450

layout(location = 0) in vec2 vUv;
layout(location = 1) in vec3 vNormal;

layout(location = 0) out vec4 oColor;

void main_fragment() {
    vec4 albedo = texture(mainTexture, vUv);
    float light = max(dot(normalize(vNormal), normalize(vec3(0.4, 0.8, 0.2))), 0.0);
    oColor = vec4(albedo.rgb * (0.25 + 0.75 * light), albedo.a) * uTint;
}
```

The runtime never parses GLSL or SPIR-V. The bake emits SPIR-V, reflection metadata, the generated
prologue, and validation results. Loading a shader that fails validation is a bake error, not a
runtime error.

Supported uniform types are `float`, `int`, `uint`, `bool`, `vec2`, `vec3`, `vec4`, `mat4`, and
`sampler2D`. Arrays, structs, and uniform blocks beyond the generated block are post-1.0. A shader
that uses an unsupported type fails the bake with a diagnostic pointing at the source line.

Per-instance parameters have a practical size limit. The bake estimates the reflected record size
and warns above 4 KiB per instance; the runtime may split a batch if the device's storage-buffer
range or descriptor limits require it. Correctness does not depend on the batch not splitting.

### 7.5 `Mesh` and `Model`

```csharp
public sealed class Mesh : IAsset {
    public string Path { get; }
    public int VertexCount { get; }
    public int IndexCount { get; }
    public BoundingBox Bounds { get; }
    public PrimitiveTopology Topology { get; }
    public bool IsDisposed { get; }

    public void Dispose();
}

public sealed class Model : IAsset {
    public string Path { get; }
    public IReadOnlyList<ModelPart> Parts { get; }
    public BoundingBox Bounds { get; }
    public bool IsDisposed { get; }

    public void Dispose();
}

public readonly struct ModelPart {
    public string Name { get; }
    public Mesh Mesh { get; }
    public Shader Shader { get; }
    public Transform LocalTransform { get; }
    public BoundingBox Bounds { get; }
}

public enum PrimitiveTopology {
    TriangleList,
    TriangleStrip,
    LineList,
    LineStrip,
    PointList,
}
```

> **M3 subset.** M3 ships `Mesh` and loads standalone mesh-part assets. `Model` and `ModelPart` are
> M4; M3 bakes model descriptors and their part mesh entries. `Mesh.Bounds` is a
> `System.Numerics.BoundingBox`.

- `Mesh` is one primitive: one vertex/index buffer pair and one topology. It is immutable after
  load. `Bounds` is in mesh space. The fixed vertex layout is listed in §7.4: position, normal,
  uv, uv1, uv2, uv3, color, and tangent.
- `Model` is a flat list of parts. It has no node hierarchy in v1; glTF node transforms are baked
  into each part's `LocalTransform`. `Bounds` is the union in model space. glTF 2.0 and OBJ are
  converted to this packed engine format by the bake; the runtime never parses them.
- `ModelPart.LocalTransform` is applied before the draw's instance transform:
  `world = part.LocalTransform.WithParent(instanceTransform)`.
- `ModelPart.Shader` is the baked shader for the part. The part also carries an internal baked
  shader-state snapshot that is not public. `DrawModel(model, transform)` uses that snapshot;
  mutating `part.Shader` with `SetUniform` does not change it. To override, pass a shader to
  `DrawModel`.
- `ModelPart.Bounds` is in model space after the part's local transform.
- `Model` is not an animation asset. Skeletal and morph animation are post-1.0.
- A model's parts may share meshes and shaders. The model holds references to all of them; dispose
  the model rather than individual parts unless those parts were loaded independently.
- Meshes keep their CPU positions for collision and bounds in v1. Textures do not keep CPU pixels.

### 7.6 `Texture`, samplers, and formats

```csharp
public sealed class Texture : IAsset {
    public string Path { get; }
    public int Width { get; }
    public int Height { get; }
    public int MipLevels { get; }
    public TextureFormat Format { get; }
    public bool IsSrgb { get; }
    public SamplerState Sampler { get; }
    public bool IsDisposed { get; }

    public void Dispose();
}

public readonly record struct SamplerState(
    TextureFilter Filter,
    TextureWrap Wrap,
    float MaxAnisotropy = 1f);

public enum TextureFilter {
    Nearest,
    Linear,
    Anisotropic,
}

public enum TextureWrap {
    Repeat,
    Clamp,
    Mirror,
}

public enum TextureFormat {
    Rgba8Unorm,
    Rgba8Srgb,
    Bc7Unorm,
    Bc7Srgb,
    Bc5Unorm,
    R8Unorm,
    Rgba16Float,
    Depth32Float,
}
```

> **M3 subset.** M3 loads and exposes `Path`, `Width`, `Height`, `MipLevels`, `Format`, `IsSrgb`,
> `Sampler`, `IsDisposed`, and `Dispose`. The GPU image is created from the packed KTX2 payload.
> Mip generation, format selection, sRGB, and sampler settings are fixed at bake time.

- Textures are immutable GPU resources. The bake chooses format, mip count, sRGB, and sampler;
  v1 has no runtime sampler override or texture upload API.
- `IsSrgb` is true for color textures baked as sRGB. Data textures (normal, mask, MSDF) are linear.
  The renderer decodes sRGB texture samples to linear before shading.
- `Sampler` is baked per texture. The default is `Linear` + `Repeat`, with anisotropy `1`.
- `Rgba16Float` is the default HDR render-target format. Ordinary packed color textures are usually
  `Bc7Srgb`; linear data textures use `Bc7Unorm`, `Bc5Unorm`, or `R8Unorm`.
- Bindless slots are allocated lazily on first use. The renderer queries the device's
  `maxPerStageDescriptorUpdateAfterBindSampledImages` and targets a 4096-slot table. Exceeding the
  table throws `GraphicsException`; the game must reduce live textures or wait for frame
  retirement to free slots.
- `Depth32Float` is used by render targets, not by ordinary packed textures.
- A texture participating in a frame keeps its slot and GPU image alive until the frame fence
  signals, even if the game disposes its public reference earlier.

### 7.7 `RenderState`

```csharp
public readonly record struct RenderState {
    public BlendMode Blend { get; init; }
    public CullMode Cull { get; init; }
    public DepthTest DepthTest { get; init; }
    public bool DepthWrite { get; init; }

    public static RenderState Opaque { get; }
    public static RenderState AlphaBlend { get; }
    public static RenderState Additive { get; }
    public static RenderState Multiply { get; }
}

public enum BlendMode {
    Opaque,
    Alpha,
    Additive,
    Multiply,
}

public enum CullMode {
    None,
    Front,
    Back,
}

public enum DepthTest {
    Disabled,
    Greater,
    GreaterOrEqual,
    Less,
    LessOrEqual,
    Always,
}
```

`RenderState.Opaque` is the default:

| Field | Opaque | AlphaBlend | Additive | Multiply |
| --- | --- | --- | --- | --- |
| `Blend` | `Opaque` | `Alpha` | `Additive` | `Multiply` |
| `Cull` | `Back` | `Back` | `Back` | `Back` |
| `DepthTest` | `GreaterOrEqual` | `GreaterOrEqual` | `GreaterOrEqual` | `GreaterOrEqual` |
| `DepthWrite` | `true` | `false` | `false` | `false` |

- `DepthTest` uses reverse-Z. `Greater` and `GreaterOrEqual` mean "closer wins". `Less` and
  `LessOrEqual` are escape hatches for custom depth tricks and are rarely correct with the
  engine's projection.
- `DepthTest.Disabled` skips the test; `DepthWrite` is ignored when the test is disabled.
- `BlendMode` has fixed presets in v1. Custom blend factors are post-1.0.
- Culling uses counter-clockwise front faces in the right-handed world. `CullMode.None` is for
  two-sided geometry and billboards.
- Topology is a property of `Mesh`, not `RenderState`. The batching key includes the mesh topology
  implicitly.

### 7.8 Draw calls and batching

Every `Camera.Draw*` call records a value-only command. The render thread builds batches from the
sealed frame.

**Batch key.** A batch is keyed by:

1. the camera pass and render target;
2. the mesh (vertex/index buffers and topology);
3. the shader program;
4. the effective `RenderState`.

Uniform values and texture bindings are **not** part of the key. They are written per instance.
Consequently:

- Repeated `DrawMesh` calls with the same mesh/shader/state become one instanced draw even when
  every texture or uniform differs.
- A different mesh, shader, or render state starts a new batch.
- Different meshes with the same shader are separate draws in v1. A Vulkan instanced draw uses one
  vertex/index buffer, so "same shader" alone cannot merge different geometry. Multi-draw indirect
  and GPU-driven batching are post-1.0.
- Different cameras do not batch together.
- Model parts are separate meshes; a model with three parts produces at most three batches per
  shader/state, each with one instance per model draw.
- The renderer may split a batch if a device limit is reached (storage-buffer range, descriptor
  count, or instance-data size). Splitting is an implementation detail and does not change
  correctness.

**Per-instance data.** Each instance carries the world transform, the tint, a UV rectangle for
sprite/text-style shaders, and the reflected uniform/texture data. Built-in shaders consume the
fields they need; custom shaders see them through the generated ABI.

**Culling.** Instances are frustum-culled against their world bounds. A model part uses its part
bounds transformed by the instance transform; a mesh uses its mesh bounds. `RenderStats` reports
the number of culled instances. There is no occlusion culling, LOD, or GPU-driven culling in v1.
Custom vertex displacement can invalidate bounds; v1 has no per-draw bounds override, so such a
shader should disable culling by drawing a larger mesh or accepting incorrect culling.

**Sorting.**

- Opaque batches are submitted in a stable order. Depth testing with reverse-Z resolves visibility;
  there is no front-to-back sort guarantee.
- Alpha and multiply batches are sorted back-to-front by instance depth within a batch. Batches
  themselves are ordered by their nearest instance. Additive batches are sorted back-to-front for
  determinism.
- Sorting is stable for equal depths. The renderer does not reorder draws across cameras.

**Explicit instancing.** `DrawInstanced` records one snapshot of the current shader state and
applies it to every transform in the span. It is one batch by construction, subject to device
limits. To vary a texture per instance, use separate `DrawMesh` calls; the automatic batcher will
merge them.

`DrawInstanced` and `DrawModelInstanced` take `ReadOnlySpan<Transform>`, so the span must remain
valid for the duration of the call; the engine copies the transforms into the frame packet.

**Threading.** All `Draw*` calls and `Camera.Dispose` are tick-thread only. Recording a draw does
not block on the GPU and does not submit a command buffer.

### 7.9 Built-in primitives, lines, and billboards

The primitive helpers use engine-owned meshes and the built-in unlit/vertex-color shader. They are
convenience APIs, not a second rendering path; they participate in the same batching and stats.

| Method | Geometry | Notes |
| --- | --- | --- |
| `DrawLine` | One line segment | 1-pixel line. Thick lines are post-1.0. |
| `DrawLines` | Line strip through the points | Consecutive pairs; an odd trailing point is ignored. |
| `DrawPoint` | Camera-facing quad of `size` world units | Avoids Vulkan point-size limits. |
| `DrawTriangle` | One triangle | Counter-clockwise front face. |
| `DrawCube` | Box centered at `position` with `size` extents | |
| `DrawSphere` | UV sphere | Default tessellation; no LOD. |
| `DrawCylinder` | Capped cylinder along local Y | `sides` controls tessellation. |
| `DrawPlane` | XZ plane centered at `center` | `size` is width and depth. |
| `DrawGrid` | XZ grid centered at the origin | `slices` lines per axis, `spacing` world units. |
| `DrawWireCube` | Line geometry | No polygon-mode dependency. |
| `DrawWireSphere` | Line geometry | Three great circles. |
| `DrawBillboard` | Camera-facing quad | `faceCamera` false uses world up and the camera's yaw. |
| `DrawTexture` | Axis-aligned textured quad | Intended for orthographic/2D cameras; stretches to `destination`. |

`DrawText` is covered in §7.10. `DrawBillboard` and `DrawTexture` use the built-in texture shader;
for custom shading, draw a quad mesh with a custom shader instead.

Colors are sRGB values converted to linear in the shader. The primitive shader multiplies vertex
color by the per-instance tint.

### 7.10 Fonts and text

```csharp
public sealed class Font : IAsset {
    public string Path { get; }
    public float LineHeight { get; }
    public float Ascent { get; }
    public float Descent { get; }
    public int GlyphCount { get; }
    public int CachedGlyphCount { get; }
    public bool IsDisposed { get; }

    public bool TryGetGlyph(Rune rune, out Glyph glyph);
    public Vector2 Measure(ReadOnlySpan<char> text, float size);
    public void Preload(ReadOnlySpan<char> characters);
    public void Dispose();
}

public readonly struct Glyph {
    public Rune Rune { get; }
    public Vector2 Advance { get; }
    public Vector2 Bearing { get; }
    public Vector2 Size { get; }
    public Vector2 Uv0 { get; }
    public Vector2 Uv1 { get; }
}

public enum TextAlignment {
    Left,
    Center,
    Right,
}

public enum TextWrapping {
    None,
    Word,
    Character,
}
```

Fonts are TTF/OTF assets. The bake copies the font file and its face metadata; the runtime parses
the face and generates MSDF glyph textures on demand.

- One glyph is one small texture, 32 px on its longest side, with a baked distance range and
  padding. There is no atlas. Glyph textures are bindless and are referenced per text instance.
- `Load<Font>` parses the face and preloads ASCII (`U+0020..U+007E`) plus the replacement glyph.
  `Preload` generates any additional characters. `DrawText` generates missing glyphs on the tick
  thread before recording; the first draw of a new script may hitch. Preload known text in
  `Setup`.
- `GlyphCount` is the number of mapped glyphs in the face. `CachedGlyphCount` is the number with a
  generated MSDF texture.
- `TryGetGlyph` returns only cached glyphs. `Measure` works from face metrics and kerning without
  generating textures.
- Metrics (`LineHeight`, `Ascent`, `Descent`) are in em units; `Measure` and `DrawText` multiply by
  the requested `size`.
- Kerning is applied. Ligatures, contextual shaping (Arabic, Indic), bidi, font fallback, color
  fonts, and emoji are post-1.0. Latin/Cyrillic/Greek text maps codepoint-to-glyph directly.
- There is no hinting. MSDF antialiasing uses screen-space derivatives.
- `size` is the em height in world units. A `size` of `1` means one world unit per em.
- Text is world space. `DrawText` lays the text in the transform's local XY plane, with +X to the
  right and +Y up; the readable face points toward local +Z.
- Alignment and wrapping are applied within `maxWidth` when `wrapping` is not `None`. `maxWidth`
  is in world units.
- `DrawTextBillboard` lays out the text in a camera-facing plane. It is the same layout and
  batching path with a billboard transform.
- Text batching uses the built-in MSDF shader. All text with the same font and render state can be
  one instanced draw; each glyph instance carries its bindless texture index and UV rectangle.
- Fonts are shared assets. Disposing a font while a frame references it is safe; using the public
  object after `Dispose` is not.

`Camera` also exposes:

```csharp
public void DrawTextBillboard(
    Font font,
    ReadOnlySpan<char> text,
    Vector3 position,
    float size,
    Color color,
    TextAlignment alignment = TextAlignment.Left);
```

### 7.11 Render targets and multiple cameras

```csharp
public sealed class RenderTexture : IDisposable {
    public int Width { get; }
    public int Height { get; }
    public int SampleCount { get; }
    public Texture ColorTexture { get; }
    public Texture? DepthTexture { get; }
    public bool IsHdr { get; }
    public bool IsDisposed { get; }

    public void Resize(int width, int height);
    public Task<byte[]> ReadPixelsAsync(CancellationToken cancellationToken = default);
    public void Dispose();
}

public readonly record struct RenderTextureDescription(
    int Width,
    int Height,
    TextureFormat ColorFormat = TextureFormat.Rgba16Float,
    bool Depth = true,
    int SampleCount = 4);
```

```csharp
using var target = await ctx.CreateRenderTextureAsync(
    new RenderTextureDescription(512, 512, TextureFormat.Rgba16Float, Depth: true));

using (var offscreen = ctx.CreateCamera(
    offscreenTransform,
    CameraProjection.Perspective,
    fieldOfView: 60f,
    clearMode: ClearMode.ColorAndDepth,
    target: target)) {
    offscreen.DrawModel(model, Transform.Identity, shader);
}

using (var screen = ctx.CreateOrthographicCamera(
    new Transform { Position = new Vector3(0f, 0f, 1f) },
    height: ctx.Window.RenderHeight,
    clearMode: ClearMode.ColorAndDepth)) {
    screen.DrawTexture(
        target.ColorTexture,
        new Rect(0, 0, ctx.Window.RenderWidth, ctx.Window.RenderHeight));
}
```

The snippet is inside `Tick`; `CreateRenderTextureAsync` would normally be awaited in `Setup`
and the target stored in a field.

- `CreateRenderTextureAsync` returns after the render thread has allocated the image. It is legal
  in `Setup` and `Tick`.
- The default color format is `Rgba16Float` because the renderer works in linear HDR. An SDR
  `Rgba8Unorm` or `Rgba8Srgb` target is allowed for LDR intermediate work; the present pass still
  applies the configured tone mapping when the target is presented.
- `ColorTexture` and `DepthTexture` are views owned by the `RenderTexture`. Do not dispose them
  separately; dispose the render texture. Sampling them is legal. A runtime-created texture view
  reports an empty `Path` because it is not a pack asset.
- `SampleCount` is 1, 2, 4, or 8; the default is 4. HDR color and depth are resolved to a
  single-sample texture before sampling. Unsupported counts fall back to the highest supported
  count and log a warning.
- `Resize` recreates the images. It must not be called while a camera targets the texture or while
  a frame that samples it is in flight; the method blocks until it is safe and then recreates.
- `ReadPixelsAsync` returns display-referred RGBA8, top-left origin, **after** AgX and the output
  transfer function, matching what the user would see. It stalls the pipeline and is intended for
  screenshots and tests, not per-frame work. Raw linear HDR readback is post-1.0.
- A camera targeting a `RenderTexture` is a normal pass. A later pass in the same tick can sample
  the texture; the renderer inserts the image and resolve barriers. Sampling a target inside the
  pass that writes it is invalid.
- Multiple cameras can target the HDR scene target for split-screen. Set `viewport` on each camera
  and clear only the first; later cameras use `ClearMode.None` or `ClearMode.Depth` as appropriate.
- There is no render graph and no automatic pass dependency analysis in v1. Creation order is the
  pass order. Cycles and write-after-read hazards are validation errors.

### 7.12 Render statistics

```csharp
public readonly record struct RenderStats(
    long FrameIndex,
    int DrawCalls,
    int Instances,
    int Triangles,
    int CulledInstances,
    int PipelineCount,
    int TextureCount,
    int SampleCount,
    bool HdrOutput,
    double CpuFrameMilliseconds,
    double GpuFrameMilliseconds,
    int FramesInFlight,
    int RecoveryCount);
```

| Field | Meaning |
| --- | --- |
| `FrameIndex` | The tick frame this snapshot describes. |
| `DrawCalls` | `vkCmdDrawIndexed`/`vkCmdDraw` calls submitted for the frame. In M2 the present triangle is one draw call. |
| `Instances` | Instances that survived culling and were submitted. M2 reports one instance for the present triangle. |
| `Triangles` | Submitted triangle count after instancing. M2 reports one triangle. |
| `CulledInstances` | Instances rejected by frustum culling. M2 has no culling and reports `0`. |
| `PipelineCount` | Distinct pipelines used by the frame. M2 reports the present pipeline. |
| `TextureCount` | Bindless texture slots live after the frame. M2 reports the resolved scene color. |
| `SampleCount` | Effective MSAA sample count for the frame. |
| `HdrOutput` | Whether the frame presented through the scRGB HDR path. |
| `CpuFrameMilliseconds` | Render-thread CPU time for planning and recording. |
| `GpuFrameMilliseconds` | GPU timestamp delta, or `0` when timestamps are unavailable. M2 does not collect timestamps and always reports `0`. |
| `FramesInFlight` | Frames submitted but not yet retired at snapshot time; `0` to `3` in M2. |
| `RecoveryCount` | Device, surface, and swapchain recoveries completed since startup. |

`ctx.Stats` is a snapshot of the last completed frame, not the frame currently being recorded. It
is updated by the render thread and read by the tick thread; individual fields are not a
consistent transactional snapshot. Use it for diagnostics and overlays, not for synchronization.
A frame that idles on a minimized or zero-extent surface reports zero draw calls, instances,
triangles, pipelines, and textures, with zero CPU and GPU time.

`RecoveryCount` is incremented when a recovery completes; the public snapshot publishes the new
value with the next completed frame. A device, surface, and swapchain recovery all count. A
recovery that fails does not increment the count and is fatal.

## 8. Window

```csharp
public interface IWindow {
    string Title { get; }
    int Width { get; }
    int Height { get; }
    int RenderWidth { get; }
    int RenderHeight { get; }
    float DpiScale { get; }
    Rect Bounds { get; }

    bool IsResizable { get; }
    bool IsFullscreen { get; }
    bool IsMinimized { get; }
    bool IsFocused { get; }
    bool WasResized { get; }
    bool CloseRequested { get; }
    WindowState State { get; }

    IReadOnlyList<Monitor> Monitors { get; }
    Monitor CurrentMonitor { get; }

    void SetTitle(string title);
    void SetSize(int width, int height);
    void SetFullscreen(bool fullscreen);
    void SetBorderless(bool borderless);
    void SetPosition(int x, int y);
    void SetMonitor(int monitorIndex);
    void SetMinSize(int width, int height);
    void SetMaxSize(int width, int height);
    void Minimize();
    void Maximize();
    void Restore();
    void SetCursor(CursorMode mode);
    void SetClipboardText(string text);
    string GetClipboardText();
    void Close();
}

public enum WindowState {
    Normal,
    Minimized,
    Maximized,
    Fullscreen,
    BorderlessFullscreen,
    Hidden,
}

public enum CursorMode {
    Normal,
    Hidden,
    Disabled,
}

public enum PresentMode {
    Mailbox,
    Fifo,
    Immediate,
}

public readonly record struct Monitor(
    int Index,
    string Name,
    Rect Bounds,
    Rect WorkArea,
    float DpiScale,
    int RefreshRate);
```

Semantics:

- `Width`/`Height` are **window coordinates**. On Windows they equal render pixels; on platforms
  that distinguish logical points they may differ from the physical framebuffer.
  `RenderWidth`/`RenderHeight` are **physical pixels**. `DpiScale` is the OS display/content scale,
  for example `1.5` at 150%, **not** the pixel ratio; the pixel ratio is `RenderWidth / Width`.
  Mouse positions are window coordinates; cameras and viewports are render pixels.
- `Bounds` is in desktop coordinates. It is a snapshot, not a live handle.
- `WasResized` is true for exactly the first tick after a resize; it is reset by the engine.
- `CloseRequested` is true after the window close request. The engine treats it as a graceful exit
  request and stops after the current tick. There is no close veto in v1; save in `Shutdown`.
- `SetFullscreen(true)` prefers borderless fullscreen for predictable WSL/desktop behavior. If the
  platform cannot provide it, the app falls back to windowed fullscreen and logs a warning.
- `PresentMode` defaults to `Mailbox`; if the surface does not support it, the engine falls back to
  `Fifo`. `Immediate` is only used when explicitly requested.
- `SetBorderless(true)` toggles a borderless window; it does not itself enter fullscreen.
- `SetSize` is ignored while fullscreen; it is remembered for the next windowed restore.
- `SetMonitor` moves a windowed window to the monitor at the given stable index. An out-of-range
  index throws `ArgumentOutOfRangeException`; the call is queued.
- `SetMinSize`/`SetMaxSize` use window units. A zero maximum means "no maximum".
- `SetCursor(CursorMode.Disabled)` locks and hides the cursor and enables relative mouse motion;
  use `Input.MouseDelta` for look. `Hidden` hides the cursor without locking.
- Window setters are **Any**-thread safe. They are queued to the main thread and coalesced; a
  getter returns the last confirmed value, which may lag the setter by one event-pump cycle.
  `SetClipboardText` follows the same queued, coalesced setter path.
- `GetClipboardText` returns the last cached clipboard snapshot immediately and queues a
  main-thread read that a later event-pump cycle refreshes.
- `Monitors` and `CurrentMonitor` are snapshots. Monitor indices are stable for the process
  lifetime; a monitor hotplug may change the list between ticks.
- `RayBork.Monitor` collides with `System.Threading.Monitor` when `System.Threading` is imported.
  Qualify `RayBork.Monitor` or use `var` for monitor locals; the public type name is unchanged.
- When `IsMinimized` or `IsFocused` is false, the tick loop is capped at 30 FPS as described in
  §4.1. The renderer may idle while the swapchain has no extent.
- v1 has one window. Multiple windows are post-1.0.
- Window icon, opacity, clipboard image, drag-and-drop, and accessibility are post-1.0. IME input
  is delivered through `TextInput` in M9.

---

## 9. Input

```csharp
public interface IInput {
    bool IsKeyDown(Key key);
    bool IsKeyPressed(Key key);
    bool IsKeyPressedRepeat(Key key);
    bool IsKeyReleased(Key key);
    bool IsKeyUp(Key key);

    string TextInput { get; }

    Vector2 MousePosition { get; }
    Vector2 MouseDelta { get; }
    float MouseWheel { get; }

    bool IsMouseButtonDown(MouseButton button);
    bool IsMouseButtonPressed(MouseButton button);
    bool IsMouseButtonReleased(MouseButton button);
    bool IsMouseButtonUp(MouseButton button);

    IReadOnlyList<IGamepad> Gamepads { get; }
    IGamepad? GetGamepad(int index);
}

public enum MouseButton {
    Left,
    Middle,
    Right,
    X1,
    X2,
}
```

### 9.1 Keyboard and text

```csharp
public enum Key {
    A, B, C, D, E, F, G, H, I, J, K, L, M,
    N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    Escape, Tab, CapsLock, Space, Enter, Backspace, Delete, Insert,
    Home, End, PageUp, PageDown,
    Left, Right, Up, Down,
    LeftShift, RightShift, LeftControl, RightControl, LeftAlt, RightAlt, LeftSuper, RightSuper,
    NumLock, ScrollLock, PrintScreen, Pause, Menu,
    Minus, Equal, LeftBracket, RightBracket, Backslash, Semicolon, Apostrophe,
    Comma, Period, Slash, Grave,
    Keypad0, Keypad1, Keypad2, Keypad3, Keypad4,
    Keypad5, Keypad6, Keypad7, Keypad8, Keypad9,
    KeypadDecimal, KeypadDivide, KeypadMultiply, KeypadSubtract, KeypadAdd, KeypadEnter,
    // The full enum mirrors SDL3 scancodes; aliases are stable names, not layout characters.
}
```

- `Key` is a **physical** scancode. `Key.W` is the key in the W position on a US layout, regardless
  of the user's keyboard layout. This is what games want for movement.
- `TextInput` is the UTF-16 text entered during the current tick, in layout order. It is the right
  API for names, chat, and console input. It is empty when no text was entered.
- `IsKeyPressed` is true for the first press of a key only; OS-generated repeat events do not set
  it. `IsKeyPressedRepeat` is true only for an OS-generated repeat event, never for the first
  press. `IsKeyDown`/`IsKeyUp` are level state.
- Input is snapshotted at the start of `Tick`; events that arrive while `Tick` runs are applied to
  the next tick. A slow `Tick` therefore coalesces input.
- IME composition, text editing, and clipboard image are M9. v1 handles direct text input.

### 9.2 Mouse

- `MousePosition` is in window coordinates, top-left origin.
- `MouseDelta` is the movement since the previous tick. It is especially useful with
  `CursorMode.Disabled`.
- `MouseWheel` is the vertical wheel delta for the tick. Horizontal wheel and high-resolution
  scroll are post-1.0.
- Mouse buttons use the five-button `MouseButton` enum.

### 9.3 Gamepads

```csharp
public interface IGamepad {
    int Index { get; }
    int InstanceId { get; }
    string Name { get; }
    bool IsConnected { get; }

    bool IsButtonDown(GamepadButton button);
    bool IsButtonPressed(GamepadButton button);
    bool IsButtonReleased(GamepadButton button);

    float GetAxis(GamepadAxis axis);
    float GetTrigger(GamepadTrigger trigger);

    void SetVibration(float lowFrequency, float highFrequency, double seconds);
    void StopVibration();
}

public enum GamepadButton {
    South,
    East,
    West,
    North,
    LeftShoulder,
    RightShoulder,
    LeftStick,
    RightStick,
    Back,
    Start,
    Guide,
    DpadUp,
    DpadDown,
    DpadLeft,
    DpadRight,
    Misc1,
    Misc2,
    Misc3,
    Touchpad,
}

public enum GamepadAxis {
    LeftX,
    LeftY,
    RightX,
    RightY,
}

public enum GamepadTrigger {
    Left,
    Right,
}
```

- `Index` is the position in `Input.Gamepads` for the current tick. It can change across hotplug.
  `InstanceId` is stable for the lifetime of the connected device; use it for per-device state.
- `Input.Gamepads` contains connected pads. `GetGamepad(index)` returns the pad at that index or
  `null` when disconnected. Handles are stable objects; `IsConnected` becomes false on removal.
- Axes are raw `[-1, 1]`; triggers are `[0, 1]`. RayBork applies **no deadzone**; games choose
  their own. SDL's gamecontroller mapping database supplies the button/axis mapping.
- Hotplug is polled at tick boundaries. Rumble is best-effort; an unsupported device logs at Debug
  and ignores the call.
- Touch, motion sensors, and haptics beyond simple rumble are post-1.0.

---

## 10. Diagnostics

### 10.1 Logging

RayBork uses `Microsoft.Extensions.Logging` for its public logging contract. `EngineContext` exposes
an `ILoggerFactory`; the default implementation is configured with ZLogger's console provider.

```csharp
using Microsoft.Extensions.Logging;

public async Task Setup(EngineContext ctx) {
    ILogger logger = ctx.LoggerFactory.CreateLogger("Castle");
    logger.LogInformation("Setup started");
}
```

- `RayBorkAppBuilder.WithLoggerFactory(ILoggerFactory)` supplies a caller-owned factory. The app
  does not dispose it.
- If no factory is supplied, the engine creates a ZLogger console factory filtered at
  `WithLogLevel` (default `Information`). The app disposes that factory after `Shutdown`.
- `EngineContext.LoggerFactory` is never `null` and is safe to read from any thread.
- Engine categories are `RayBork.App`, `RayBork.Assets`, `RayBork.Render`, `RayBork.Window`,
  `RayBork.Input`, `RayBork.Font`, and `RayBork.Bake`.
- Recoverable renderer failures are logged at Warning with the recovery action; failed recovery is
  logged at Error before the exception surfaces.
- `ZLogger` is the default provider, not a public abstraction. Games may use `ILoggerFactory`,
  `ILogger`, and any MEL-compatible provider.

### 10.2 `EngineVersion`

```csharp
namespace RayBork;

public static class EngineVersion {
    public static Version Current { get; }
}
```

| Member | Thread | Contract |
| --- | --- | --- |
| `Current` | **Any** | The version of the loaded `RayBork.Engine` assembly. Immutable for the app lifetime. Safe to read from any thread. Throws no `RayBorkException`. |

`Current` is the loaded `RayBork.Engine` assembly's `Version`; it reports `0.1.0.0` in the 0.1
release, including M1 and M2. The build and package version is `0.1.0`. It describes the engine
assembly that is actually loaded, not the game assembly, the pack format, or the shader ABI. The
value cannot change while the process runs, and no RayBork API mutates it. `EngineVersion` is the
only public type added in M0. M1 adds the host, window, input, time, and logging surface listed in
§1.6; M2 adds the renderer diagnostics (`EngineInfo`, `RenderStats`, `PresentMode`,
`TonemappingMode`, `GraphicsException`, and `GraphicsDeviceLostException`). Every other type in this
reference remains a contract for later milestones.

The getter has no dependency on a pack, window, GPU, renderer, or logger. It does not block, does not
require synchronization, and throws no `RayBorkException`; the value is available to loggers,
diagnostics, crash reports, and game-side compatibility checks. RayBork does not compare
`EngineVersion.Current` with a game's own version or enforce compatibility; that policy belongs to
the game.

### 10.3 `EngineInfo`

```csharp
public readonly record struct EngineInfo(
    string Name,
    Version Version,
    string RendererName,
    string GpuName,
    uint VulkanApiVersion,
    string SwapchainFormat,
    bool HdrOutput,
    int SampleCount,
    PresentMode PresentMode,
    bool ValidationEnabled);
```

`RendererName` identifies the active backend together with the selected device's Vulkan API version,
for example `"Vulkan 1.4"` on a 1.4 device; the backend itself requests and requires Vulkan 1.2.
The public API is renderer-agnostic; a future mobile/web backend changes this string, not game
code. `GpuName` is the selected device. `VulkanApiVersion` is the packed Vulkan version the device
reports (for example `1.4.0` encoded as `(1 << 22) | (4 << 12)`). `SwapchainFormat` is the selected
surface format token without the `VK_FORMAT_` prefix (for example `B8G8R8A8_SRGB` or
`R16G16B16A16_SFLOAT`), `HdrOutput` is true only when the window reports HDR and the selected path
presents through scRGB, `SampleCount` is the effective MSAA count, `PresentMode` is the effective
present mode after fallback, and `ValidationEnabled` reports whether the validation layer is
active.

### 10.4 Exceptions

```csharp
public class RayBorkException : Exception {
    public string? DiagnosticCode { get; }
}

public sealed class GameException : RayBorkException { }
public class AssetException : RayBorkException { }
public sealed class AssetNotFoundException : AssetException { }
public sealed class AssetTypeMismatchException : AssetException { }
public sealed class AssetPackException : AssetException { }
public sealed class AssetLoadException : AssetException { }
public class GraphicsException : RayBorkException { }
public sealed class GraphicsDeviceLostException : GraphicsException { }
public sealed class ShaderException : GraphicsException { }
public sealed class WindowException : RayBorkException { }
public sealed class PlatformException : RayBorkException { }
```

M1 ships `RayBorkException`, `GameException`, `PlatformException`, and `WindowException`. M2 ships
`GraphicsException` and `GraphicsDeviceLostException`; `GraphicsException` is not sealed, and
`GraphicsDeviceLostException` derives from it. M3 ships `AssetException` and its four sealed
subtypes. `ShaderException` remains the M4 shader-ABI contract.

Game and platform diagnostic codes (M1, M2):

| Code | Exception | Meaning |
| --- | --- | --- |
| `RB0001` | `GameException` | A `Setup`, `Tick`, or `Shutdown` callback failed. |
| `RB2001` | `PlatformException` | SDL3/platform initialization failed. |
| `RB2002` | `WindowException` | Window creation failed. |
| `RB2003` | `PlatformException` | SDL3 Vulkan surface creation or loader failure. |

Graphics diagnostic codes (M2):

| Code | Exception | Meaning |
| --- | --- | --- |
| `RB3001` | `GraphicsException` | Graphics initialization or device creation failed, or the Vulkan validation layer reported an error. Fatal. |
| `RB3002` | `GraphicsDeviceLostException` | A bounded recovery failed or the consecutive-recovery limit was reached. Fatal. |
| `RB3003` | `GraphicsException` | An unrecoverable Vulkan acquire or present failure stopped the app. Fatal. |

Asset diagnostic codes (M3):

| Code | Exception | Meaning |
| --- | --- | --- |
| `RB1001` | `AssetPackException` | A pack could not be opened or is truncated. |
| `RB1002` | `AssetPackException` | Unsupported pack magic or version. |
| `RB1003` | `AssetPackException` | The pack index is structurally invalid, unsorted, or contains a duplicate path. |
| `RB1004` | `AssetPackException` | The index hash does not match the index bytes. |
| `RB1005` | `AssetLoadException` | A payload hash does not match the stored bytes. |
| `RB1006` | `AssetNotFoundException` | The path is absent from every mounted pack. |
| `RB1007` | `AssetTypeMismatchException` | The path exists with a different asset type. |
| `RB1008` | `AssetLoadException` | A descriptor, KTX2 payload, mesh payload, or custom-data shape could not be decoded. |
| `RB1009` | `AssetException` | An asset path is not canonical. |
| `RB1010` | `AssetLoadException` | A GPU upload or device-recovery re-upload failed. |

Bake diagnostic codes (M3, `RayBork.Bake`):

| Range | Meaning |
| --- | --- |
| `RB4001` | Builder configuration, pack/root paths, or registration errors. |
| `RB4002` | TOML syntax, binding, sidecar, or unknown-key errors. |
| `RB4003` | Source discovery, canonical path, duplicate path, or unsupported source errors. |
| `RB4004` | Pack layout, size overflow, or write errors. |
| `RB4005` | Incremental manifest or cache errors. |
| `RB4006` | A custom processor failed. |
| `RB4007` | Texture decode, KTX2, mip, or block-encoding errors. |
| `RB4008` | Shader source, shaderc compilation, or metadata errors. |
| `RB4009` | glTF/OBJ parse or model/mesh conversion errors. |
| `RB4010` | Font container, face validation, or packaging errors. |
| `RB4011` | Missing or invalid baked references. |
| `RB4012` | A semantic asset validator reported an error. |

| Exception | Typical cause |
| --- | --- |
| `GameException` | `Setup`, `Tick`, or `Shutdown` threw. `InnerException` is the game's exception. |
| `AssetNotFoundException` | `Load<T>` path is absent from every mounted pack. |
| `AssetTypeMismatchException` | Path exists, but was baked as a different type. |
| `AssetPackException` | Pack missing, bad magic/version, invalid index, hash failure. |
| `AssetLoadException` | Corrupt payload, decode failure, GPU upload failure, or custom-data shape failure. |
| `GraphicsException` | Unrecoverable device/swapchain/pipeline/resource failure, a validation error, or an unsupported required mode. |
| `GraphicsDeviceLostException` | Device loss, a failed surface or swapchain recovery, or the recovery bound. Fatal. |
| `ShaderException` | A runtime shader interface mismatch that escaped bake validation. |
| `WindowException` | SDL3 window creation or platform window failure. |
| `PlatformException` | SDL3 initialization, Vulkan surface or loader failure (`RB2003`), audio device (later), or native library failure. |

`DiagnosticCode` is a stable `RB`-prefixed code such as `RB1002` (unsupported pack version),
`RB2003` (SDL3 Vulkan surface or loader failure), or `RB3002` (bounded recovery failed). Codes are
documented in the runtime and bake diagnostics tables; tests assert them. They are stable within a
major version.

Lifecycle and argument errors use the standard BCL exceptions, not `RayBorkException`:
`ArgumentNullException`, `ArgumentException`, `InvalidOperationException`,
`ObjectDisposedException`, `NotSupportedException`.

### 10.5 Assertions and validation

- Debug builds enable Vulkan validation when available. Validation errors are fatal by default;
  warnings are logged. `WithValidation(false)` disables the layer explicitly.
- Debug builds use `Debug.Assert` for internal invariants. Public API misuse throws; it is never
  silently ignored.
- Release builds still validate public arguments and lifecycle rules; they skip the expensive
  internal assertions.
- GPU object names and debug labels are set in debug builds for validation messages and
  RenderDoc/Nsight captures.

## 11. Threading and lifetime rules

### 11.1 Thread ownership

| State | Owner | Access |
| --- | --- | --- |
| `IGame` callbacks | Tick thread | Only the callback runs; no concurrent callback |
| `Camera` | Tick thread | Create, draw, dispose inside one `Tick` |
| `Shader` uniform cursor | Tick thread | `SetUniform`/`ResetUniforms` |
| `Font` glyph cache | Tick thread | `Preload`, lazy glyph generation |
| `RenderTexture` handle | Tick thread | Create/dispose/resize; GPU work is deferred |
| `IInput` | Tick thread | Read the current snapshot |
| `IWindow` setters | Any thread | Queued to the main thread |
| `IWindow` getters | Any thread | Last confirmed snapshot |
| `IAssetCatalog` | Any thread | Thread-safe load/cache; custom data is caller-owned |
| `ILoggerFactory` | Any thread | App-owned; create category loggers |
| `RenderStats` | Render thread writes | Any thread reads a snapshot |
| Renderer backend objects | Render thread | Never exposed; the backend is replaceable internally |

### 11.2 The "join before Tick returns" rule

If user code starts a thread that touches game-owned objects, it must join that thread before
`Tick` returns. This includes `Camera`, `Shader.SetUniform`, `Font.Preload`, `RenderTexture`, and
any object read by a recorded draw. The engine's frame handoff assumes that all game-owned state
is quiescent at the end of `Tick`.

`IAssetCatalog` is the exception: background loads are safe, but the completed asset must not be
used by a tick-affine object until the tick that observes completion. `Load<T>` returns a
caller-owned value for custom data; it can be used on any thread after completion.

Recoverable renderer failures are handled on the render thread. The render thread pauses, recreates
the swapchain or device, recreates pipelines and GPU resources, increments `RenderStats.RecoveryCount`,
and resumes. It does not call game code. If recovery fails, the app stops as described in §3.5.

### 11.3 Disposal

- `Camera.Dispose` is required and must happen before `Tick` returns.
- `RenderTexture.Dispose` is deferred; frames that sample it keep it alive.
- Engine asset `Dispose` releases a reference; frames and pending GPU work retain their own
  references.
- `Load<T>` and `LoadData` results for custom data are caller-owned and are not disposed by the
  engine.
- `RayBorkApp.Dispose` stops a running app, joins threads, and releases native resources. It is
  idempotent. Calling it from the tick thread throws `InvalidOperationException` instead of
  deadlocking on a self-join; call it from another thread.
- The engine never calls `Dispose` on game-owned assets. `Shutdown` is the game's responsibility;
  the engine logs leaked references in debug builds but does not guess ownership.
- A double asset `Dispose` is a usage error (see §6.4).

### 11.4 Shutdown order

```text
1. Stop accepting new ticks; finish the current tick if one is running.
2. The tick thread publishes the final frame and runs IGame.Shutdown on the tick thread, then exits.
3. Join the tick thread and complete the frame ring.
4. The render thread drains and presents the final frame, disposes the backend, and destroys the
   native surface through the surface channel.
5. Join the render thread while the main thread services the surface channel and pumps, then close
   the channel.
6. Dispose the platform: window, SDL3, Vulkan loader. Join every thread.
```

The order is deliberate: `Shutdown` runs while the renderer is still alive, but it cannot draw.
GPU resources are not destroyed until after `Shutdown` returns, so a final readback or save is
possible.

M2 has a real renderer: the main thread services surface requests until the render thread exits,
then disposes the platform. The shutdown join is bounded to 30 seconds for the render thread; the
tick thread is joined after it publishes the final frame. M3 inserts the asset loader drain before
the renderer is destroyed; the numbered order above remains the 1.0 contract.

### 11.5 Multiple apps and restarts

v1 supports one `RayBorkApp` per process. `Run` twice on the same app throws. To restart a game,
exit the process and start it again. This keeps SDL3/Vulkan global state and native library
lifetime simple, and matches the Native AOT deployment model.

---

## 12. Native AOT

RayBork's runtime is designed for Native AOT:

- `RayBork.Engine` sets `IsAotCompatible`, `EnableTrimAnalyzer`, and `EnableAotAnalyzer`.
- No runtime reflection-based serialization. Pack metadata and custom data use compiled
  `PolyType` shapes with `Nerdbank.MessagePack`; pack metadata is read with explicit binary readers.
- No runtime shader compilation. Shader reflection is bake-generated metadata.
- `Load<T>` maps built-in engine asset types with a closed type switch. Custom data types are
  registered at build time with `WithAssetType<T>()`, which stores a compiled `IShapeable<T>`
  loader delegate; `LoadData` covers opaque bytes without reflection.
- Logging uses `Microsoft.Extensions.Logging`; ZLogger is the default provider and is AOT-tested.
- P/Invoke is direct. SDL3 native libraries are deployed per RID; the Vulkan loader is resolved from
  the system/GPU driver with `NativeLibrary` and a custom `INativeContext`.
- The engine may use `unsafe` internally; game projects do not need `AllowUnsafeBlocks`.
- The bake tool is JIT and may use reflection, native tooling, and shader compilation. It is never
  referenced by a runtime project and never enters the publish graph.
- The built-in pack is generated at engine build time by a JIT-only generator that runs the ordinary
  bake tool. Its generated C# byte array is compiled into `RayBork.Engine`; the generator, its
  packages, and shaderc are not part of the runtime publish graph.
- Pack descriptors and custom data use `[GenerateShape]` and `Nerdbank.MessagePack`; the runtime
  never uses reflection serialization. `Load<T>` maps engine asset types with a closed type switch
  and custom data through the registered shape delegate.

A game project uses `WinExe` for release. Samples and diagnostic hosts use `Exe` so console logging
is visible.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net11.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <PublishAot>true</PublishAot>
    <InvariantGlobalization>false</InvariantGlobalization>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\RayBork\RayBork.Engine\RayBork.Engine.csproj" />
  </ItemGroup>
</Project>
```

Publish commands used by the gate:

```text
dotnet publish -c Release -r win-x64   -p:PublishAot=true
dotnet publish -c Release -r linux-x64 -p:PublishAot=true
```

Both publishes must start, load a pack and any registered custom data, draw where the milestone has
a draw path, and shut down. AOT publish is a per-milestone gate, not a release-only check. The M3
gate bakes a sample pack with the assets executable, publishes `samples/HelloInterop`, runs it with
`--assets <pack> --frames 30` on `win-x64` and `linux-x64`, and requires exit code `0` with zero
AOT/trim warnings.

Single-file publishing is supported when the native SDL3/Vulkan libraries are deployed alongside
the executable or extracted by the host. A fully self-extracting single binary is not a v1 gate.

---

## 13. Asset tooling (`RayBork.Bake`)

`RayBork.Bake` is a JIT build library, not a runtime dependency. A game owns a small assets
executable that configures the bake and calls `RunAsync`. The bake discovers sources, runs
processors, binds TOML data, validates references, and writes one or more packs. The runtime only
sees the packed engine format. No runtime project references `RayBork.Bake`.

The bake uses JIT-only tooling: `Tomlyn` for TOML, `StbImageSharp` for PNG/JPEG/TGA/BMP decoding,
`BCnEncoder.Net` for BC1/BC4/BC5/BC7 encoding, `Silk.NET.Shaderc` plus `Silk.NET.Shaderc.Native`
for GLSL compilation, `SharpGLTF.Core` for glTF 2.0, `PolyType` and `Nerdbank.MessagePack` for
shapes and payloads, and `System.IO.Hashing` for pack hashes. These packages never enter a runtime
project or an AOT publish graph.

### 13.1 Assets executable

```csharp
using RayBork.Bake;

return await new BakeApplicationBuilder()
    .WithProjectRoot(".")
    .WithPack("assets.pck", "Assets")
    .UseDefaultAssets()
    .RegisterAsset<GameSettings>()
    .UseDefaultProcessors()
    .WithTextureDefaults(TextureFilter.Linear, TextureWrap.Clamp)
    .AddProcessor(new CircleProcessor())
    .Build()
    .RunAsync(args);
```

| Method | Contract |
| --- | --- |
| `WithProjectRoot` | Base for relative roots, pack paths, and diagnostics. |
| `WithPack(packPath, root)` | Adds one ordered pack. The first is the main pack; later packs are overlays that shadow earlier paths at runtime. |
| `UseDefaultAssets` | Registers the engine authoring descriptor types and their built-in validators. Idempotent. |
| `RegisterAsset<T>()` | Registers one TOML data type. `T` must carry a compiled `[GenerateShape]`. |
| `RegisterAsset<T>(validator)` | Registers the type if needed and appends an `IAssetValidator<T>`. |
| `UseDefaultProcessors` | Registers the built-in texture, model, shader, font, and raw processors. Audio processors are added in A1. |
| `WithTextureDefaults` | Sampler defaults for textures without a sidecar. |
| `AddProcessor` | Adds a custom `IAssetProcessor`; later registrations override earlier ones for the same extension. |
| `Build` | Validates configuration and returns the bake application. |
| `RunAsync(args)` | Runs `bake`, `clean`, `inspect`, `--help`, or `--version`; returns the process exit code. |

The `watch` command is deferred to M10. M3 ships `bake`, `clean`, `inspect`, `--help`, and
`--version`.

M3's public bake surface is `BakeApplicationBuilder`, `BakeApplication`, `IAssetProcessor`,
`AssetProcessor<TSidecar>`, `AssetProcessorContext`, `IAssetOutputSink`,
`IAssetProcessorDiagnostics`, `IAssetValidator<T>`, `SidecarPolicy`, `TextureBaker`,
`BakedTexture`, `TextureChannelType`, and `TextureCompression`. `TextureBaker.Bake` is the one
KTX2 producer: a custom processor that generates pixels calls it instead of writing KTX2 headers or
block data itself.

Built-in assets are not special. The engine's built-in pack is produced by the same processors
through `UseDefaultProcessors`; a game can override a built-in extension with `AddProcessor`.

### 13.2 TOML data assets

A registered data type is selected by a root `[raybork]` table:

```toml
# Assets/Data/level.toml
[raybork]
type = "Castle.GameSettings"

name = "The Castle"
difficulty = 2
```

```csharp
using PolyType;

[GenerateShape]
public sealed partial class GameSettings {
    public string Name { get; init; } = "";
    public int Difficulty { get; init; }
}
```

The bake binds constructor parameters and properties by name, serializes the value as MessagePack,
and emits a typed data entry at `data/level`. The runtime reads it with
`ctx.Assets.Load<GameSettings>("/data/level")`. Member keys are snake_case by default
(`PaddleSpeed` is `paddle_speed`); `[JsonPropertyName]` overrides one member. Enums bind by their
member name, case-insensitively, with snake_case accepted (`greater_or_equal`). Unknown keys,
missing required members, and type mismatches are errors.

A data TOML file is not a sidecar. A sidecar configures one processor source and lives at
`<asset base>.toml` with a table named after the processor (`[texture]`, `[model]`, `[font]`). A
`[shader]` TOML is a shader source, described below.

### 13.3 Processors

```csharp
public interface IAssetProcessor {
    string Name { get; }
    IReadOnlyList<string> Extensions { get; }
    SidecarPolicy Policy { get; }
    ITypeShape? SidecarShape { get; }
    void Process(AssetProcessorContext context);
}
```

- `Name` is stable and participates in diagnostics and incremental hashing. It is also the sidecar
  table name.
- `Extensions` lists the source extensions the processor owns; both `"png"` and `".PNG"` are
  normalized. A compound extension such as `shader.toml` is matched as one extension when registered.
- `Policy` is `None`, `Optional`, or `Required`; `SidecarShape` is the compiled shape for a typed
  sidecar. A missing `Optional` sidecar uses `CreateDefaultSidecar`; a missing `Required` sidecar is
  an error. The bake never creates or rewrites a source sidecar.
- `Process` runs on the bake thread. It must not retain per-source state between calls.
- Outputs go through `AssetProcessorContext.Outputs`: `AddAsset<T>` for shaped data and `AddData`
  for raw payloads. `AddData` takes an alignment of `1`, `16`, `64`, or `256`; GPU payloads use
  `16` or greater. A processor never writes MessagePack or pack bytes directly.
- `AssetProcessorContext` exposes the source paths, the canonical base asset path, the output sink,
  the diagnostic sink, and `AddInput(path)`. `AddInput` records a referenced source file, such as a
  shader's GLSL stages, as a tracked input for the incremental manifest; the path is resolved
  relative to the source file and must stay inside the pack root. It is valid for one source only.
- `AssetProcessor<TSidecar>` is the convenience base for a processor with a required sidecar.
- A processor error discards every output for the source and fails the pack. Warnings do not fail
  the pack unless `--warnings-as-errors` is set.

Built-in processors:

| Processor | Sources | Outputs |
| --- | --- | --- |
| Texture | PNG, JPEG, TGA, BMP | `Texture` descriptor plus its KTX2 payload |
| Model | glTF 2.0 (`.gltf`, `.glb`), OBJ | `Model` descriptor, one `Mesh` descriptor per part, vertex/index payloads |
| Shader | GLSL vertex + fragment pair named by a `[shader]` TOML | `Shader` descriptor plus vertex and fragment SPIR-V payloads |
| Font | TTF, OTF | `Font` descriptor plus the face payload |
| Raw | Any unclaimed source | Opaque bytes for `LoadData` |
| Audio (A1, post-1.0) | WAV, OGG, MP3, FLAC, AIFF | AAC-LC payload via FFmpeg; not registered by `UseDefaultProcessors` in 1.0 |

A texture source `Assets/Textures/castle.png` with an optional sidecar
`Assets/Textures/castle.toml`:

```toml
[texture]
srgb = true
format = "bc7"
filter = "anisotropic"
wrap = "repeat"
anisotropy = 8
```

- `format` is `rgba8`, `bc7`, `bc5`, or `r8`; the default is `rgba8`. `bc5` and `r8` are linear data
  formats and reject `srgb = true`. `srgb` defaults to `true` for `rgba8`/`bc7`.
- `filter` is `nearest`, `linear`, or `anisotropic`; the builder default is `linear`.
- `wrap` is `repeat`, `clamp`, or `mirror`; the builder default is `repeat`.
- `anisotropy` defaults to `1` and is clamped to the device limit at load time.
- The bake generates the full mip chain with a deterministic box filter, encodes every level, and
  writes one complete KTX2 2.0 payload. The descriptor and payload agree on format, mip count, and
  sRGB transfer.
- A texture that decodes to an unsupported dimension, a 16-bit PNG, or a non-finite payload value is
  a `RB4007` error and emits no outputs.

A model source `Assets/Models/castle.glb` with an optional sidecar
`Assets/Models/castle.toml`:

```toml
[model]
scale = 1.0
front = "+z"
shader = "shaders/castle"
```

- glTF 2.0 static meshes and OBJ static meshes are supported. Skins, morph targets, animations,
  cameras, and lights are rejected or ignored with a diagnostic; a source with no mesh is an error.
- The model is emitted at the base path. Each primitive becomes a `Mesh` part at
  `<base>/<part>` with its own vertex/index payload. The `Model` descriptor records the flat part
  list, local transforms, bounds, and the shared sidecar shader path.
- `scale` and `front` apply the authoring correction before bounds and payloads are written.
- glTF material and image import is M4 work. M3 validates that referenced images exist and warns
  that they are not imported.

A font source `Assets/Fonts/inter.ttf` with an optional sidecar `Assets/Fonts/inter.toml`:

```toml
[font]
ranges = ["U+0020-007E", "U+00A0-00FF"]
msdf_pixel_size = 32
msdf_range = 4
msdf_padding = 2
```

The font processor validates the sfnt container and table directory, stores the settings in the
`Font` descriptor, and emits the original face bytes. The runtime parser and MSDF generation are M6.

A shader is authored as one TOML source at the asset base path:

```toml
# Assets/Shaders/castle.toml
[shader]
vertex = "castle.vert"
fragment = "castle.frag"
vertex_entry = "main_vertex"
fragment_entry = "main_fragment"

[[shader.uniforms]]
name = "uTint"
type = "vec4"
default = [1.0, 1.0, 1.0, 1.0]

[[shader.textures]]
name = "mainTexture"

[shader.render_state]
blend = "opaque"
cull = "back"
depth_test = "greater_equal"
depth_write = true
```

- `vertex` and `fragment` are paths relative to the TOML file and must stay inside the pack root.
  They are tracked inputs, not assets. The TOML source itself is not a data asset.
- The entry points default to `main_vertex` and `main_fragment`; M3 accepts an explicit value only
  for built-in shaders. The bake validates that each entry point exists in its SPIR-V module.
- Supported uniform types are `float`, `int`, `uint`, `bool`, `vec2`, `vec3`, `vec4`, `mat4`, and
  `sampler2D`. `default` is a scalar for `float`/`int`/`uint`/`bool`, and an array whose length
  matches the component count for the vector and matrix types. Arrays, structs, and extra uniform
  blocks are errors.
- `blend` is `opaque`, `alpha`, `additive`, or `multiply`; `cull` is `none`, `front`, or `back`;
  `depth_test` is `disabled`, `greater`, `greater_equal`, `less`, `less_or_equal`, or `always`.
  `depth_write` is a bool.
- The generated prologue, shader ABI bindings, and full SPIR-V reflection are M4. M3 compiles the
  two-stage program with shaderc, validates the entry points, and records the sidecar metadata and
  render state in the `Shader` descriptor.

The built-in processors use `RB4007`-`RB4010`; the shared TOML and discovery failures use
`RB4002`/`RB4003`. Unknown keys are errors, not silently ignored.

### 13.4 Pack v1 (as built)

The pack is little-endian and starts with a fixed 40-byte header:

| Field | Size | Notes |
| --- | --- | --- |
| Magic | 8 bytes | ASCII `RAYBORKP` |
| Version | `u32` | `1` |
| Flags | `u32` | Reserved; zero |
| Entry count | `u32` | At most 1,000,000 |
| Index offset | `u64` | Absolute file offset, 40 |
| Index length | `u32` | Bytes, at most 64 MiB |
| Index hash | `u32` | XXH32 of the index bytes, seed `0` |
| Reserved | `u32` | Zero |

The index is a sorted array of entries (ordinal canonical-path order). Every field is
little-endian:

| Field | Size | Notes |
| --- | --- | --- |
| Path length | `u16` | UTF-8 byte length, at most 4096 |
| Path | bytes | Canonical asset path without a leading slash |
| Asset type | `u16` | `Raw=0`, `Mesh=1`, `Model=2`, `Texture=3`, `Shader=4`, `Font=5`, `Data=6` |
| Flags | `u16` | Bit 0 compressed, bit 1 aligned; other bits zero |
| Payload offset | `u64` | Absolute file offset |
| Stored length | `u32` | Bytes as stored |
| Uncompressed size | `u32` | Bytes after decompression |
| Alignment | `u32` | `1`, `16`, `64`, or `256` |
| Payload hash | `u64` | XXH3-64 of the stored bytes, seed `0` |

Payloads start after the index. Each payload begins at an offset that is a multiple of its
alignment; the writer inserts zero padding. The reader verifies the alignment, bounds, lengths,
index hash, and payload hash before exposing an entry. A path is rejected if it is not canonical,
if entries are not strictly ordinal-sorted, or if two entries normalize to the same path.

A payload is Zstandard-compressed when its entry flags say so. Debug packs store payloads
uncompressed for fast iteration; release packs compress when the compressed bytes are strictly
smaller. The decompressor is bounded to the declared uncompressed size, and a stream that produces
more or fewer bytes is corruption.

Engine asset entries are MessagePack descriptors that reference sibling raw payload entries
(`<base>-ktx2`, `<base>-mesh`, `<base>-vert`, `<base>-frag`, `<base>-face`). A `Data` entry holds the
MessagePack bytes of one registered custom type; `Load<T>` deserializes it with the compiled shape
and returns a fresh instance.

Pack bytes are **not deterministic**. The entry set and canonical paths are ordered, but the
compressor version, encoder choices, and build can change the bytes. Incremental caching uses
input/output hashes, not byte-for-byte reproducibility.

The pack is not encrypted or signed in v1. Integrity is a hash, not authenticity. A corrupt
header, index, or payload is a hard failure with a diagnostic code; the runtime never guesses. An
unknown version is `RB1002`.

### 13.5 Incremental builds

The bake writes a manifest at `<project root>/obj/raybork/bake/manifest.json`. It records:

- the bake tool version and configuration hash (mode, pack paths, roots, texture defaults,
  processors, data type registrations, and the tool markers for the texture, shader, model, and
  font pipelines);
- every tracked input path and content hash, including sidecars, `AddInput` dependencies, and shader
  GLSL files;
- every output entry path, entry hash, and payload hash;
- the pack logical-layout hash.

A rebuild reuses the previous pack's descriptor and payload bytes for a source whose input hashes,
processor identity, tool version, and configuration are unchanged. It reruns the processor for
changed and new sources, drops deleted sources, and composes a new pack from the reused and fresh
entries. A tool-version or configuration change invalidates the cache and reprocesses every source.
`--force` bypasses the cache. A missing, corrupt, or incompatible previous pack is a full bake.

The manifest is not part of the runtime pack. It never promises byte-identical pack output; it only
avoids redundant processing. Tests assert the processor invocation count for an unchanged source,
not pack byte identity.

### 13.6 Validation and diagnostics

The bake fails the build on:

- duplicate or case-normalized canonical paths;
- missing referenced assets or a missing required sidecar;
- unsupported source formats or an unclaimed file with no raw fallback;
- shader compile, entry-point, or sidecar metadata errors;
- texture dimensions, formats, or block-encoding failures;
- model sources with no mesh or invalid accessors;
- font containers that are not valid sfnt files;
- TOML parse, bind, unknown-key, or shape failures;
- a semantic validator error;
- pack size or alignment overflow.

Diagnostics carry a code, severity, source path, and line/column where available. The CLI renders
`path(line,col): error RBxxxx: message` on `stderr` and a summary on `stdout`. The process exits
`0` on success or up to date, `1` on source diagnostics, `2` on usage or configuration errors, and
`3` on an unexpected internal failure. Warnings do not fail the build unless `--warnings-as-errors`
is set.

### 13.7 Built-in pack

`RayBork.Engine` embeds a small built-in pack produced by the same processors:

- a white 1x1 texture and a magenta/black error texture, generated by a tool-only processor that
  calls `TextureBaker.Bake`;
- the present vertex/fragment shader pair, compiled from the engine's committed GLSL sources by a
  tool-only processor through the shared shaderc compiler.

The default draw shader, primitive meshes, and error glyph are added when M4 and M6 introduce them.

The pack is generated at engine build time by `build/RayBork.BuiltInAssets`, a JIT-only generator
that references `RayBork.Bake`. `build/RayBork.BuiltInAssets.targets` is imported by
`RayBork.Engine.csproj`: before `CoreCompile` it restores and builds the generator out of process,
runs the ordinary `bake` command over `src/RayBork/RayBork.Engine/BuiltInAssets/Assets` into a
unique temporary directory, and emits a generated C# byte array under the intermediate output. The
generated file is compiled into the assembly, never committed, and never published as a loose pack.
The generator and its packages are not referenced by the runtime project and do not enter the AOT
publish graph.

The built-in pack is mounted first, before every game pack. A game pack shadows a built-in entry at
the same canonical path.

## 14. Versioning and compatibility

| Surface | Policy |
| --- | --- |
| Public C# API | 0.x is preview; 1.0 follows semantic versioning. |
| License | MIT; see [LICENSE](../LICENSE). |
| Pack format | `RAYBORKP` v1 is the only accepted format in 1.x; an unknown magic or version is rejected with `RB1002`. The 40-byte header, sorted binary index, alignment, and XXH32/XXH3-64 hashes are part of the format. Pack bytes are not deterministic; the logical entry layout is. |
| Shader ABI | The generated prologue identifiers, binding layout, and entry points (`main_vertex`, `main_fragment`) are stable within a major version. |
| Custom data shapes | The `[GenerateShape]` contract and MessagePack wire shape are stable within a major version. |
| Diagnostic codes | Stable within a major version. |
| Native AOT | Supported RIDs are part of the release gate, not a best-effort extra. |
| Renderer backend | The public API is renderer-agnostic; the backend name and capability flags are reported through `EngineInfo`. |
| Public API analysis | `Microsoft.CodeAnalysis.PublicApiAnalyzers` tracks every public member; accidental changes fail CI. |

Before 1.0, breaking changes are allowed when they improve the API. Each breaking change gets a
migration note in the release notes and an update to this reference. After 1.0, obsolete members
are marked `[Obsolete]` for at least one minor release before removal.

The pack format and the runtime are versioned independently. A 1.x runtime reads a 1.x pack. A
future pack v2 requires a runtime that advertises support; v1 rejects it rather than attempting a
best-effort read.

---

## 15. Out of scope for v1

The following are deliberate exclusions, not forgotten features. The roadmap places them after
1.0 unless noted.

| Area | v1 status | Notes |
| --- | --- | --- |
| Audio | Deferred | SDL3 audio, mixer, 2D/3D sources, streaming, AAC decode in engine, FFmpeg bake, HRTF and Doppler. |
| Skeletal/morph animation | Deferred | Models are static. |
| Built-in lighting/PBR/shadows | Deferred | Custom shaders can implement them. |
| Physics | Deferred | Collision queries only. |
| UI toolkit | Deferred | Text, sprites, and orthographic cameras are the building blocks. |
| Networking | Out | |
| VR/OpenXR | Deferred | Not in 1.0; later. |
| Custom vertex/instance layouts | Deferred | One fixed vertex layout in v1. |
| Compute/geometry/tessellation shaders | Deferred | Vertex+fragment only. |
| Hot reload | Deferred | Bake and relaunch first. |
| Editor | Out | RayBork is a framework. |
| Multiple windows | Deferred | One window per process. |
| macOS / ARM64 / mobile / web | Deferred | Windows x64 and Linux x64 first; the renderer seam exists for future mobile/web backends. |
| Scripting / mods | Out | Native AOT game assemblies only. |
| IME / touch / accessibility | Partial | Committed IME text in M9; touch and accessibility post-1.0. |
| Debug overlay / profiler UI | Deferred | `RenderStats` is the data source. |

---

## Appendix A: seed-to-API changes

| Seed | This reference | Why |
| --- | --- | --- |
| `Mesh? _model` | `Model? _model` | A castle is a multi-part model; `Mesh` is one primitive. |
| `Load<Mesh>("/models/castle")` | `Load<Model>("/models/castle")` | Multi-part glTF/OBJ support. |
| `Load<Mesh>("/textures/castle")` | `Load<Texture>("/textures/castle")` | The seed's type is a typo. |
| `_vertShader`, `_fragShader` | one `Shader` | A shader is a complete program; the bake pairs the stages. |
| `_fragShader.SetUniform(...)` | `_shader.SetUniform(...)` | No `Material`; the shader owns the uniform cursor. |
| `DrawModel(model, transform, vert, frag)` | `DrawModel(model, transform, shader)` | One program, one batching key. |
| Camera at `z = -10` | `z = +10` | Right-handed, `-Z`-forward world. |
| `ClearMode.Color` | `ClearMode.ColorAndDepth` | A 3D pass must clear depth too. |
| `(float)(ctx.Time * 0.001)` | `(float)ctx.Time` | `Time` is seconds. |
| `Mesh?`/`Texture?`/`Shader?` disposal | same `Dispose` calls, once per load | Shared, reference-counted assets. |
| `using var app = ...Build()` | unchanged | `RayBorkApp` is `IDisposable`. |
| `WithAssetsPack(packPath)` | unchanged | Relative paths resolve next to the executable. |
| `MaxDeltaTime = 0.25` | unchanged | Seconds. |
| `Quaternion.CreateFromAxisAngle` | `Quaternion.Euler` / `Quaternion.AngleAxis` | User-facing rotations use degrees. |
| `Color` from sRGB bytes | linear `Color` | HDR rendering and AgX output; `Color` components are linear floats. |
| Custom data via ad-hoc files | `Load<T>` / `LoadData` | Compiled `[GenerateShape]` data and raw bytes; register with `WithAssetType<T>()`. |
| Logger injected as a custom interface | `ctx.LoggerFactory` | `Microsoft.Extensions.Logging` + ZLogger. |

The seed's `Shutdown` order remains correct: the model first, then the shader, then the texture.
The model holds references to its parts. The shader cursor holds an internal reference to the
texture set in `Setup`; disposing the shader first releases that cursor. The order is still the
clearest one, even though the cursor would keep the GPU resource alive if the texture were
disposed first.

---

## Appendix B: public type index

| Type | Section |
| --- | --- |
| `IGame` | §3.1 |
| `RayBorkAppBuilder` | §3.2 |
| `RayBorkApp` | §3.3 |
| `EngineContext` | §3.4 |
| `Transform` | §5.2 |
| `BoundingBox` | §5.5 |
| `Color` | §5.3 |
| `Rect` | §5.4 |
| `RayCollision`, `Collision` | §5.5 |
| `Vector3Extensions`, `QuaternionExtensions` | §5.6 |
| `IAsset`, `IAssetCatalog` | §6.1 |
| `Mesh`, `Model`, `ModelPart`, `PrimitiveTopology` | §7.5 |
| `Texture`, `TextureFormat`, `TextureFilter`, `TextureWrap`, `SamplerState` | §7.6 |
| `Shader`, `ShaderStageMask`, `ShaderUniform`, `ShaderUniformType`, `ShaderTextureBinding` | §7.3 |
| `RenderState`, `BlendMode`, `CullMode`, `DepthTest` | §7.7 |
| `Camera`, `CameraProjection`, `ClearMode`, `TonemappingMode` | §7.1, §7.2 |
| `Shader`, `ShaderUniform`, `ShaderTextureBinding` | §7.3 |
| `Font`, `Glyph`, `TextAlignment`, `TextWrapping` | §7.10 |
| `RenderTexture`, `RenderTextureDescription` | §7.11 |
| `RenderStats` | §7.12 |
| `IWindow`, `WindowState`, `CursorMode`, `Monitor`, `PresentMode` | §8 |
| `IInput`, `Key`, `MouseButton`, `IGamepad`, `GamepadButton`, `GamepadAxis`, `GamepadTrigger` | §9 |
| `ILoggerFactory` | §10.1 |
| `EngineVersion` | §10.2 |
| `EngineInfo` | §10.3 |
| `RayBorkException` and derived exceptions | §10.4 |
| `BakeApplicationBuilder`, `BakeApplication`, `IAssetProcessor`, `AssetProcessor<TSidecar>`, `AssetProcessorContext`, `IAssetOutputSink`, `IAssetProcessorDiagnostics`, `IAssetValidator<T>`, `TextureBaker`, `BakedTexture`, `TextureChannelType`, `TextureCompression`, `SidecarPolicy` | §13 |
