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
    public void Dispose();
}

public readonly record struct RenderTextureDescription(
    int Width,
    int Height,
    TextureFormat ColorFormat = TextureFormat.Rgba16Float,
    bool Depth = true,
    int SampleCount = 4);

public Task<RenderTexture> CreateRenderTextureAsync(
    RenderTextureDescription description,
    CancellationToken cancellationToken = default);
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

The code above is the `Setup` pattern: `CreateRenderTextureAsync` is awaited while `Setup` runs, the
target is stored in a field, and the cameras are created in `Tick`. `IGame.Tick` is `void`, so a
target created during `Tick` cannot be awaited there; the working pattern in `Tick` is the blocking
form `ctx.CreateRenderTextureAsync(description).GetAwaiter().GetResult()`, which returns after the
render thread allocated the images. Creating targets in `Setup` is preferred because a target created
in `Tick` can stall that tick for the allocation. The screen pass uses `ScreenToWorldTransform` and a
textured world-space `DrawQuad`; there is no `DrawTexture`, and the full-screen target is a normal quad
on the plane.

**Creation and validation.** `CreateRenderTextureAsync` returns after the render thread allocated the
images, so the result is usable for the next camera recorded in the same tick. It is legal in `Setup`
and `Tick`; the request is serviced by the render thread independently of the tick thread, so the tick
thread never deadlocks. Cancellation before completion completes the call as
`OperationCanceledException` and a target created for a canceled request is destroyed instead of
leaking. After shutdown every call fails with `ObjectDisposedException`, even when the description is
invalid. The creation queue holds at most 256 pending requests; a full queue fails the call with
`InvalidOperationException` rather than waiting for the render thread.

- `Width` and `Height` must be positive and at most `16384`. The renderer also rejects a target that
  exceeds the device's maximum 2D image dimension.
- The color format is `Rgba16Float` (the default), `Rgba8Unorm`, or `Rgba8Srgb`. The packed
  block-compressed formats and `Depth32Float` are rejected. The default is linear HDR; the SDR formats
  are for LDR intermediate work. Tone mapping is not applied when the target is written: a runtime
  target is never presented directly, and AgX is applied only by the present pass when the content
  reaches the screen through the scene target.
- `SampleCount` is `1`, `2`, `4`, or `8`; the default is `4`. The effective count is the highest
  device-supported count that does not exceed the request, or `1` when none is supported, and a
  fallback logs a warning. `RenderTexture.SampleCount` reports the effective count. The multisampled
  color and depth attachments are resolved into single-sample sampled images; `ColorTexture` and
  `DepthTexture` are those resolved images. A device recovery re-selects the count from the recreated
  device and updates `RenderTexture.SampleCount`.
- `Depth` controls the target's depth attachment and its public `DepthTexture` view. With `Depth: true`
  the target owns a `D32_SFLOAT` depth attachment and a single-sample resolved depth image; a
  multisampled target resolves the depth attachment with the Vulkan 1.2 depth/stencil resolve path.
  `DepthTexture` is a `Texture` view over that resolved image. With `Depth: false` there is no depth
  attachment and `DepthTexture` is `null`: depth-tested and depth-writing render states are not rejected,
  but the depth test and depth write are no-ops for that target, and `ClearMode.Depth` is a no-op. Such a
  target still records color-only draws, while a draw that must be occluded by 3D depth needs
  `Depth: true`.
- `IsHdr` is `true` for `Rgba16Float`.
- The target's contents are undefined after creation until a camera renders into them.

**Resize.** `RenderTexture.Resize(int width, int height)` recreates the target's color attachment, optional
depth attachment, resolved images, render pass, framebuffer, per-target geometry pipeline cache, and
bindless color and depth views at the new size. It is tick-thread only, like `CreateRenderTextureAsync`,
and blocks until the render thread finished the recreation; the render thread services the request
independently of the tick thread, so the call never deadlocks with a frame. It keeps the target id, the
color format, the `Depth` flag, the effective `SampleCount`, and both `Texture` view objects. `Width`,
`Height`, `ColorTexture.Width`/`Height`, and `DepthTexture.Width`/`Height` report the new size after the
call; the view's format, sampler, empty `Path`, and owner do not change, and directly disposing a view
still throws. The target's contents are undefined after a resize.

- An unchanged size is a no-op: it recreates nothing and does not throw, even while a frame holds the
  target.
- A dimension that is not positive or exceeds `16384` throws `ArgumentOutOfRangeException` before the
  request is queued. The render thread re-checks the new size against the current device's maximum 2D
  image dimension and fails the call with `ArgumentOutOfRangeException` when it is too large; the target
  keeps its previous images.
- `InvalidOperationException` means the target is in use: a camera targets it, a draw samples one of its
  views, or a recorded or in-flight frame still references it. The early check runs on the tick thread
  and the render thread re-checks before it recreates anything, so a resize never silently blocks on a
  current-frame hold and never replaces images an in-flight frame reads. Resize after the frame that
  used the target completed; a frame that was released but is still executing on the GPU is waited for.
- `ObjectDisposedException` means the target was disposed, or the app's render texture service stopped
  during shutdown. A resize failure (invalid size, device limit, or a failed image/sampler creation)
  finishes the call with the exception and leaves the previous target usable.
- The target keeps its bindless slots; the renderer rewrites their descriptors to the recreated views
  before any later frame can sample them, so a resize neither exhausts the bindless table nor leaves a
  stale view or slot. A later device recovery recreates the target from the updated description, so the
  resized dimensions survive.

**Camera target and viewport.** `EngineContext.CreateCamera` takes `target:` as its last parameter.
A camera targeting a `RenderTexture` records its pass into that target instead of the app's linear HDR
scene target; `Camera.Target` returns the target object. The target's viewport defaults to the full
target and clamps to the target size, not the window, and the projection aspect ratio uses the clamped
viewport. `ScreenToWorldRay`, `WorldToScreen`, and `ScreenToWorldTransform` keep their documented
camera-viewport behavior. A camera targeting a disposed target throws `ObjectDisposedException`.

**Same-tick sampling and barriers.** Passes run in camera creation order. A later camera in the same
tick can sample `ColorTexture` or `DepthTexture` with `Shader.SetUniform`, `DrawQuad`, or `DrawMesh`: the
renderer transitions the target's single-sample resolved color and resolved depth from their
attachment layouts to `ShaderReadOnlyOptimal` after the writing pass, and transitions them back if a
later pass writes the target again. Sampling either view of the target in the same camera pass that
writes it is invalid and throws `InvalidOperationException` from the draw call, before the frame is
published; it is not only a validation-layer error. A render target is reused only after the frame that
last used it completed, so a frame cannot overwrite or sample content an in-flight frame still reads.
The app scene target keeps its pre-existing cross-frame overwrite behavior; that hazard is unchanged and
is a separate follow-up.

Multiple targets work in one frame: each camera selects its target, each target has its own render
pass and geometry pipeline cache, and cameras on the scene target and on runtime targets can interleave
in one tick. There is no render graph and no automatic pass dependency analysis in v1; creation order
is the pass order. Write→sample and sample→write between two different passes are both synchronized by
the per-target barriers: a later write transitions the resolved color and depth back from
`ShaderReadOnlyOptimal` to their attachment layouts, and a later sample sees the content the earlier
write stored. Creation order decides which content a sample observes. The only rejected ordering is
sampling either view of the target inside the same camera pass that writes it.

**The `ColorTexture` and `DepthTexture` views.** `ColorTexture` is a `Texture` view over the target's
single-sample resolved color image, and `DepthTexture` is a `Texture` view over its single-sample
resolved depth image when the target has depth. Both are owned by the `RenderTexture`. They report an
empty `Path` because they are not pack assets, they sample and batch like any texture, and the recorded
frame retains the target until the renderer released the frame slot. A resize keeps both view objects and
updates their `Width` and `Height` in place; only the GPU image and bindless slot behind them are
replaced. Disposing a view directly throws `InvalidOperationException`; dispose the `RenderTexture`
instead. Disposing the render texture retires both views and releases the target; `Dispose` is idempotent,
and a target that is the destination or a sample source of a recorded in-flight frame is not destroyed
until that frame completes. A target destroyed by the renderer at shutdown also reports
`IsDisposed = true`, and its later `Dispose` is a no-op.

`DepthTexture` reports `Format == TextureFormat.Depth32Float`, `IsSrgb == false`, `MipLevels == 1`, the
target's `Width`/`Height`, and a nearest-filter clamp sampler. It samples the stored reverse-Z value as
a float. The view is single-sample even when the target's depth attachment is multisampled: the
renderer resolves the attachment into the view's image for every camera pass' render area. The depth
resolve uses Vulkan 1.2 `VkSubpassDescriptionDepthStencilResolve` with
`VK_RESOLVE_MODE_SAMPLE_ZERO_BIT`, which every conformant Vulkan 1.2 device advertises and which leaves
the stored reverse-Z value unchanged. Device selection rejects a physical device that does not advertise
sample-zero instead of creating a depth view that can never be sampled or substituting a min/max resolve.
A single-sample target has no resolve attachment: its depth attachment is the resolved depth image. After
a writing pass the renderer transitions the resolved depth image to `ShaderReadOnlyOptimal`, and a later
pass that writes the target transitions it back to `DepthStencilAttachmentOptimal`. After a device
recovery the same public `Texture` object remains valid; only its GPU image and bindless slot are
replaced. `DepthTexture` is `null` for a `Depth: false` target.

The app scene target also owns a resolved depth image, but it is internal: there is no public scene-target
depth view. It is the input a later internal consumer such as T5 SSAO samples. The scene target discards
its attachments, including the resolved depth image, at the start of every frame, and
`VulkanReadback.ReadSceneDepthPixel` reads the resolved depth image for both single-sample and
multisampled scene targets. Resolving scene depth costs bandwidth on every camera pass; T5 SSAO is the
consumer, and until it lands the only reader is the diagnostic probe. That cost is a known property of
the depth path, not a hidden regression; T5 can decide whether a pass needs the resolve.

**Remaining M8 work.** `ReadPixelsAsync`/`ReadColorsAsync` (T4) and post-processing/effects (T5)
remain later M8 tasks. This section covers the color path, the depth view, and resize: creation, camera
routing, MSAA color and depth resolve, same-tick sampling of both views, resize, and lifetime.

- Multiple cameras can target the HDR scene target for split-screen. Set `viewport` on each camera
  and clear only the first; later cameras use `ClearMode.None`, `ClearMode.Color`, or
  `ClearMode.Depth` as appropriate. Each camera pass is its own render pass with the clamped viewport
  as the render area, so a clear only touches that viewport and the pass resolves only its own
  rectangle. Each camera pass reads its own frame block from shader ABI v2, so passes with different
  views are correct. A HUD is an ordinary later camera: `ClearMode.Depth` loads the scene color and
  clears depth when the HUD must not be occluded, and `ClearMode.None` keeps both when the HUD should
  remain depth-tested. There is no separate overlay or layer pass.
- The present pass samples the app scene target only. A runtime target's content reaches the screen
  through a camera on the scene target that samples `ColorTexture`.
- There is no render graph and no automatic pass dependency analysis in v1. Creation order is the
  pass order. Write→read and read→write between different passes are synchronized by the per-target
  barriers; a cycle, or sampling a target in the pass that writes it, is invalid.

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
