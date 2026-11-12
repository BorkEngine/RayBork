# Threading and lifetime rules

[API overview](../api.md) · [document map](../api.md#document-map)

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
| `MeshBuilder`/`TextureBuilder` | Any thread | The factory and build may run on any thread; one builder instance is not thread-safe |
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

Procedural builders are also thread-safe as a factory/build operation: `EngineContext.CreateMeshBuilder`,
`EngineContext.CreateTextureBuilder`, and `BuildAsync` may be called on any thread, including `Setup`
and `Tick`. A single builder instance is not thread-safe; create one per thread. The GPU copy still
runs through the render thread and `BuildAsync` completes only after it is ready.

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
