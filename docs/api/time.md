# Time and frames

[API overview](../api.md) · [document map](../api.md#document-map)

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
