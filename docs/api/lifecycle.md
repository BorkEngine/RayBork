# Lifecycle

[API overview](../api.md) · [document map](../api.md#document-map)

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
Camera creation is not legal in `Setup` because there is no frame to record into. The engine has
already preloaded `EngineContext.Defaults` (the built-in mesh shader program and the white/error
textures) before `Setup` runs; a missing or corrupt built-in default is a startup failure and
`Setup` is not called.

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
    public RayBorkAppBuilder WithAssetLoader<T>(IAssetLoader<T> loader);
    public RayBorkAppBuilder WithPresentMode(PresentMode mode = PresentMode.Mailbox);
    public RayBorkAppBuilder WithMSAA(int sampleCount = 4);
    public RayBorkAppBuilder WithTonemapping(TonemappingMode mode = TonemappingMode.AgX);
    public RayBorkAppBuilder WithExposure(float ev = 0f);
    public RayBorkAppBuilder WithAgxWhite(float white = 16.29f);
    public RayBorkAppBuilder WithAgxContrast(float contrast = 1.25f);
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
| `WithAssetType<T>` | none | Registers a custom TOML data type for `Load<T>` as a MessagePack `Data` entry through the built-in data loader. `T` must carry a compiled `[GenerateShape]`. Idempotent for the same type; a registration that conflicts with `WithAssetLoader<T>` is an `InvalidOperationException`. |
| `WithAssetLoader<T>` | none | Registers an `IAssetLoader<T>` for `Load<T>`. `T` needs no `[GenerateShape]`. The loader's synchronous part runs on the `RayBork.Asset` loader thread, returns a fresh caller-owned value per load, and must not block or call SDL, Vulkan, or tick-thread APIs. One registration per type; a built-in engine type fails at `Build`. See §6.7. |
| `WithPresentMode` | `Mailbox` | `Mailbox` is preferred; if unavailable, the engine falls back to `Fifo`. `Immediate` is available when the user explicitly chooses it. |
| `WithMSAA` | `4` | Valid sample counts are 1, 2, 4, and 8, subject to device support. The renderer selects the highest supported sample count that does not exceed the request, and 1 if none is supported, logging a warning when the effective count differs. |
| `WithTonemapping` | `AgX` | The startup tone-mapping mode. `AgX` applies Godot 4.7's HDR-aware AgX curve to the linear HDR scene; `None` is for custom output/presentation paths. The mode is fixed for the app lifetime. |
| `WithExposure` | `0` EV | The startup exposure in stops applied before AgX. `0` is neutral; `+1` doubles linear exposure. The game can change it at runtime through `ctx.Tonemapping.Exposure`. |
| `WithAgxWhite` | `16.29` | The startup scene-linear AgX white, before the display headroom scaling. Finite and at least `2`. The game can change it at runtime through `ctx.Tonemapping.AgxWhite`. |
| `WithAgxContrast` | `1.25` | The startup AgX curve contrast. Finite and at least `1`. The game can change it at runtime through `ctx.Tonemapping.AgxContrast`. |
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

> **Current asset surface.** The generic loader registration (`WithAssetLoader<T>`,
> `IAssetLoader<T>`, `AssetLoadContext`, `AssetEntryType`) is a current-surface addition after M4,
> not part of the M3 subset.

> **M4 subset.** M4 removes `WithClearColor`; clear control belongs to each camera pass. The
> renderer builder keeps only the presentation options (`WithPresentMode`, `WithMSAA`,
> `WithTonemapping`, `WithExposure`, `WithValidation`). `EngineContext` additionally exposes
> `CreateCamera`, `CreateOrthographicCamera`, and `Defaults`; the `target` parameter and
> `RenderTexture` targets arrive with M8, so every M4 camera records into the app's linear HDR scene
> target. Camera creation, draw recording, and camera disposal are legal only on the tick thread
> during `Tick`. A camera left undisposed when `Tick` returns is an `InvalidOperationException`
> naming it, and the frame is not published.

> **M5 subset.** M5 adds the primitive, billboard, and wireframe-model helpers and the collision
> queries described in §7. The app preloads the built-in `/shaders/primitive` and `/shaders/mesh`
> programs, their two engine materials, and the eight primitive meshes before `IGame.Setup`, so the
> first tick can record a primitive draw. `DrawWireModel` records on the tick thread like
> `DrawModel`; its polygon-mode-line pipeline needs the device's `fillModeNonSolid` feature, and a
> device without it fails the frame with `GraphicsException` `RB3001` rather than falling back.
> `Collision` and `RayCollision` are pure CPU queries, and `MeshBuilder.GenerateCollisions()` plus
> the nine `Generate*` factories are ordinary builder steps available in `Setup` and `Tick`.
> `DrawTexture` is not part of M5; it moves to M7 with the orthographic sprite path. M5 also adds
> the runtime tone-mapping surface: `WithAgxWhite` and `WithAgxContrast` set the startup AgX
> defaults, and `EngineContext.Tonemapping` exposes the app-lifetime settings that a game changes
> during `Setup`, `Tick`, or `Shutdown`. The values are frame-global and are snapshotted into the
> frame packet when the frame is sealed. M5 replaces the M4 AgX and its separate HDR highlight path
> with Godot 4.7's HDR-aware AgX curve; `WithTonemapping` still fixes the mode at startup, and
> `WithExposure` keeps its meaning.

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
    public EngineDefaults Defaults { get; }
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

    public TonemappingSettings Tonemapping { get; }

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

    public MeshBuilder CreateMeshBuilder(
        PrimitiveTopology topology = PrimitiveTopology.TriangleList);

    public TextureBuilder CreateTextureBuilder(
        int width,
        int height,
        TextureFormat format = TextureFormat.Rgba8Srgb,
        bool generateMips = true,
        SamplerState? sampler = null);

    public void RequestExit();
}
```

> **M1 subset.** M1 exposes only `Input`, `Window`, `LoggerFactory`, `Time`, `DeltaTime`,
> `UnscaledDeltaTime`, `FrameCount`, `Fps`, `IsFirstTick`, `IsExiting`, `MaxDeltaTime`, and
> `RequestExit`. `Assets`, camera creation, and render-texture creation are the later contract;
> their milestones are in §1.6. M2 adds `Info` and `Stats`. M3 adds `Assets`,
> `CreateMeshBuilder`, and `CreateTextureBuilder`; camera creation and render-texture creation
> remain later contract.

| Member | Thread | Notes |
| --- | --- | --- |
| `Assets` | Any | Shared catalog; see §6. M3. |
| `Defaults` | Any | Engine-owned default assets preloaded before `Setup`: the built-in mesh shader program plus the white and error textures. `CreateMeshShader` returns a new caller-owned material; the textures are engine-owned and must not be disposed. Released during shutdown. M4. |
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
| `Tonemapping` | Tick | App-lifetime runtime tone-mapping settings, initialized from the builder defaults. Read or write them on the tick thread. A write during `Tick` applies to the frame that tick records. |
| `CreateCamera` | Tick | Perspective camera. Legal only during `Tick`; creating one while another is active is `InvalidOperationException`. |
| `CreateOrthographicCamera` | Tick | Orthographic camera. `height` is the visible world-space height. |
| `CreateRenderTextureAsync` | Tick | Creates an offscreen target and completes when the render thread has allocated it. Legal in `Setup` and `Tick`. |
| `CreateMeshBuilder` | Any | Creates a procedural mesh builder. The builder is not thread-safe; use one per thread. After shutdown the factory and its builders throw `ObjectDisposedException`. |
| `CreateTextureBuilder` | Any | Creates a procedural texture builder for an uncompressed format. The builder is not thread-safe; use one per thread. After shutdown the factory and its builders throw `ObjectDisposedException`. |
| `RequestExit` | Tick | Requests a graceful stop. The current tick completes and the final frame is presented. |

`EngineContext` is stable for the app lifetime, but its per-frame properties are only meaningful
for the callback that observes them. Re-read `Time`, `DeltaTime`, and `Stats` each tick instead of
caching them; `Setup` sees a changing `Time` but `DeltaTime` is `0`.

`Tonemapping` is the exception: it is one app-lifetime settings object, not a per-frame snapshot.
It starts from `WithExposure`, `WithAgxWhite`, and `WithAgxContrast` and can be changed from
`Setup`, `Tick`, or `Shutdown`. `Exposure` is in stops, `AgxWhite` is the scene-linear AgX white
before the display-headroom scaling, and `AgxContrast` is the curve contrast. Each write validates
first: a non-finite exposure is `ArgumentOutOfRangeException`, and so is an AgX white below `2` or a
contrast below `1`. A valid write from a thread other than the tick thread is
`InvalidOperationException`. The values are read when the camera service seals the frame after
`Tick` returns, so a write during `Tick` applies to that frame's present pass; a frame that was
already published keeps the snapshot it was sealed with.

`clearColor` is `null` by default, which means linear opaque black (`Color.Black`); HDR components
above `1` are allowed and a non-finite component is `ArgumentException`. `near` must be positive and
finite. `far` must be greater than `near`; `float.PositiveInfinity` is valid only for perspective
cameras. For an orthographic camera, `height` is the visible vertical extent; `far` must be finite.
`CreateCamera` is the perspective entry point and rejects `CameraProjection.Orthographic` because it
has no height parameter; use `CreateOrthographicCamera` for an orthographic camera.

A `viewport` of `null` means the full window render size. A non-null viewport is clamped to the
target; an invalid, non-finite, or empty result is `ArgumentException`. The camera's `Viewport`
reports the clamped rectangle and the projection aspect uses it.

`CreateCamera` and `CreateOrthographicCamera` have no `target` parameter and record into the app's
HDR scene target. The present pass applies the frame's exposure and AgX settings and writes to the
swapchain after the cameras finish; `ctx.Tonemapping` is frame-global, not per camera. The
`RenderTexture` target parameter arrives in M8; a camera with a `RenderTexture` target writes to
that target and can be sampled by a later camera.

### 3.5 Startup and shutdown sequence

```text
1. Build validates configuration. No OS or GPU work.
2. Run (main thread):
   a. SDL3 init, Vulkan-capable window creation, event pump start.
   b. Query the Vulkan instance extensions on the main thread and create the surface channel.
   c. Render thread starts: create the instance, request the native surface through the main-thread
      channel, create the device and swapchain, and publish Info.
   d. The asset catalog mounts the built-in pack (and any user packs) and starts the loader; the app
      preloads `EngineContext.Defaults` (the `/shaders/mesh` program and the white/error textures) and
      then the engine's primitive assets (the `/shaders/primitive` program, its material, and the
      eight primitive meshes).
   e. Tick thread starts with a SynchronizationContext. Setup therefore sees Info, Defaults, and a
      ready primitive draw path.
   f. IGame.Setup(ctx) is awaited on the tick thread.
   g. Loop: input snapshot, clock, IGame.Tick, frame publish; render consumes previous frame.
   h. Exit: stop accepting ticks. The tick thread publishes the final frame and runs
      IGame.Shutdown, then exits.
   i. Join the tick thread and complete the frame ring; the render thread drains and presents the
      final frame, disposes the GPU objects, and destroys the native surface through the channel
      while the main thread services it.
   j. The app releases the engine defaults and the primitive assets, stops the asset catalog, closes
      the surface channel, disposes the platform (window, SDL3, Vulkan loader), and releases the
      remaining engine state.
3. Run returns; Dispose releases the app object.
```

The surface handshake is bounded: a render-thread surface request and the main-thread wait for the
renderer each time out after 30 seconds. The tick thread does not start before the renderer is
ready, so `Setup` can read `Info` and `Defaults`. M3 inserts the asset loader after (a); M4 inserts
the engine-defaults preload between the catalog start and the tick thread start; M5 adds the
primitive-asset preload after the defaults.

Failure behavior:

- If startup fails before `Setup`, `Run` throws and nothing user-owned is called. A missing or corrupt
  built-in default is an `AssetLoadException` from `Run` before the tick thread starts.
- A platform failure to create the Vulkan surface is a `PlatformException` with `RB2003`; when it
  arrives through the render-thread surface request it is rethrown from `Run` as a
  `GraphicsException` with `RB3001` and the platform failure as its `InnerException`.
- If `Setup` throws, `Shutdown` is **not** called; the engine releases what it owns and rethrows.
- If `Tick` throws, the loop stops, `Shutdown` is called, the renderer drains, and `Run` rethrows.
- If a camera is created during `Tick` and not disposed before the callback returns, the loop stops
  with `InvalidOperationException` naming the camera; the frame is not published, and `Shutdown` is
  still called.
- If the renderer fails recoverably, it recreates the swapchain, surface, or device, logs, and
  continues. Recovery is bounded to three consecutive recoveries without a completed frame.
- If recovery fails or the bound is reached, the tick loop is stopped, `Shutdown` is called, and
  `Run` rethrows `GraphicsDeviceLostException` (`RB3002`) or the underlying `GraphicsException`.
  The window may close before the exception surfaces.
- If `Shutdown` throws, the exception is aggregated with any earlier failure.
