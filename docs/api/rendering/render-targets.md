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
