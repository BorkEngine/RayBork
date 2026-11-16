# Rendering: Render targets

[API overview](../../api.md) · [document map](../../api.md#document-map)

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
    offscreen.DrawModel(model, Transform.Identity);
}

using (var screen = ctx.CreateCamera(
    new Transform { Position = new Vector3(0f, 0f, 1f) },
    CameraProjection.Orthographic,
    orthographicHeight: ctx.Window.RenderHeight,
    clearMode: ClearMode.ColorAndDepth)) {
    Transform full = screen.ScreenToWorldTransform(
        new Vector2(ctx.Window.RenderWidth * 0.5f, ctx.Window.RenderHeight * 0.5f),
        1f);
    screen.DrawQuad(
        full,
        new Vector2(ctx.Window.RenderWidth, ctx.Window.RenderHeight),
        target.ColorTexture);
}
```

The snippet is inside `Tick`; `CreateRenderTextureAsync` would normally be awaited in `Setup`
and the target stored in a field. The screen pass uses `ScreenToWorldTransform` and a textured
world-space `DrawQuad`; there is no `DrawTexture`, and the full-screen target is a normal quad on
the plane. The `target` parameter and `RenderTexture` are M8, not part of the M7 surface; M7 authors
screen-space content on the app's scene target instead.

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
  and clear only the first; later cameras use `ClearMode.None`, `ClearMode.Color`, or
  `ClearMode.Depth` as appropriate. Each camera pass is its own render pass with the clamped viewport
  as the render area, so a clear only touches that viewport and the pass resolves only its own
  rectangle. Each camera pass reads its own frame block from shader ABI v2, so passes with different
  views are correct. A HUD is an ordinary later camera: `ClearMode.Depth` loads the scene color and
  clears depth when the HUD must not be occluded, and `ClearMode.None` keeps both when the HUD should
  remain depth-tested. There is no separate overlay or layer pass. The render-target surface described
  in this section is a later M8 task; split-screen to the HDR scene target uses per-camera render
  passes and needs no render target.
- There is no render graph and no automatic pass dependency analysis in v1. Creation order is the
  pass order. Cycles and write-after-read hazards are validation errors.

### 7.12 Render statistics

```csharp
public readonly record struct RenderStats(
    long FrameIndex,
    long PresentedFrames,
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
| `PresentedFrames` | Successful presentations since startup. It is cumulative across frames and recoveries and advances only after the image reached the presentation engine. A skipped frame, a dropped recovery frame, and a present with no pending frame do not increment it. |
| `DrawCalls` | Scene draw commands submitted for the frame: the camera-pass draws after batch splitting. The present pass' fullscreen triangle is not counted. A frame with no camera passes reports `0`. |
| `Instances` | Instances that survived culling and were submitted in the scene draws. The present triangle is not an instance. |
| `Triangles` | Submitted triangle count after instancing: `indexCount / 3` per triangle-list instance, `indexCount - 2` per triangle-strip instance, and `0` for line and point topologies. |
| `CulledInstances` | Instances rejected by frustum culling in this frame. |
| `PipelineCount` | Distinct scene (geometry) pipelines used by the frame. The present pipeline is not counted. |
| `TextureCount` | Bindless texture slots live after the frame, including the reserved white slot at slot 0. A texture allocates its slot when its GPU upload completes and releases it only when its GPU resource is destroyed. |
| `SampleCount` | Effective MSAA sample count for the frame. |
| `HdrOutput` | Whether the frame presented through the scRGB HDR path. |
| `CpuFrameMilliseconds` | Render-thread CPU time for planning, recording, and submitting the frame. The measurement starts after the frame fence wait and the swapchain acquire, so it excludes both blocking waits and presentation. |
| `GpuFrameMilliseconds` | GPU timestamp delta, or `0` when timestamps are unavailable. GPU timestamps are not collected in v1, so this always reports `0`. |
| `FramesInFlight` | Frames submitted but not yet retired at snapshot time; `0` to `3` (the frame ring has three slots). |
| `RecoveryCount` | Device, surface, and swapchain recoveries completed since startup. |

`ctx.Stats` is a snapshot of the last completed frame, not the frame currently being recorded. It
is updated by the render thread and read by the tick thread; individual fields are not a
consistent transactional snapshot. Use it for diagnostics and overlays, not for synchronization.
`PresentedFrames` is monotonic and is the truthful source for a presented-frame-rate overlay:
difference it over a one-second window. `EngineContext.Fps` is the tick rate and is not a presented
rate. A frame that idles on a minimized or zero-extent surface reports zero draw calls, instances,
triangles, culled instances, pipelines, and textures, with zero CPU and GPU time, and leaves
`PresentedFrames` unchanged. A frame that records camera passes but draws nothing (for example,
everything was culled) reports zero for the draw counters; `SampleCount` and `TextureCount` still
report the frame's effective sample count and live bindless slots.

`RecoveryCount` is incremented when a recovery completes; the public snapshot publishes the new
value with the next completed frame. A device, surface, and swapchain recovery all count. A
recovery that fails does not increment the count and is fatal.
