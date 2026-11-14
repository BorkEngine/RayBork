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
  `1 / DeltaTime`. It is `0` before two ticks have been sampled. It is the tick rate, not the
  presented frame rate: difference `Stats.PresentedFrames` over a window for presented frames per
  second, as the samples do.
- `FrameCount` increments once per `Tick`, after `Setup`.
- `MaxDeltaTime` is fixed at build time; it prevents a debugger pause or a window drag from
  teleporting the simulation.
- Presentation pacing follows the effective present mode reported by `Info.PresentMode`. With
  `PresentMode.Fifo`, a tick waits until the render loop finishes with the frame it published
  (normally its presentation), so one tick maps to one presented image and the tick rate follows the
  display refresh. A frame dropped by minimization or recovery and a failed render attempt also
  release the wait; `Stats.PresentedFrames` is the successful-presentation counter. With `Mailbox`
  or `Immediate`, the tick stays decoupled and the render thread presents the newest completed
  frame; the presented rate can then differ from the tick rate.
- A configured tick target caps the tick loop after the render-finished wait, so a target below the
  display refresh sleeps longer and a target above it is naturally limited by FIFO presentation.
- There is no interpolation between ticks in v1. In M2 the render thread presents the newest
  completed tick's frame, which currently clears the linear scene target and runs the present pass.
  A game that needs smoother motion at low tick rates must interpolate its own render transforms or
  wait for the fixed-timestep milestone.
- `Tick` must not block. If it does, it delays the next input snapshot and the frame handoff.

### 4.1 Minimized and unfocused windows

When `Window.IsMinimized` or `Window.IsFocused` is false, the tick loop continues but is capped at
**30 FPS**. The cap applies on top of `WithTargetFps` and presentation pacing: the effective cap is
the lower of the configured cap, 30 FPS, and the FIFO display cadence when FIFO is active. With
`Mailbox` or `Immediate` presentation does not pace the tick loop: the configured cap and the 30 FPS
low-power cap set its cadence, with the frame ring bounding run-ahead. Input is still snapshotted
and delivered, so a game can pause, save, or keep a background simulation alive.

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
