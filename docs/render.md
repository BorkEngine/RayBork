# Renderer

RayBork renders with Vulkan 1.2 on a dedicated render thread. The tick thread only records frame
packets; the render thread owns the device, swapchain, uploads, command buffers, and present.

## Device selection

At startup the backend creates a Vulkan 1.2 instance and picks the best physical device that supports:

- a graphics and present queue, and `VK_KHR_swapchain`;
- descriptor indexing with partially bound, update-after-bind, and non-uniform sampled-image arrays;
- at least 2048 update-after-bind sampled images;
- the Vulkan 1.2 sample-zero depth/stencil resolve mode.

Devices are ranked discrete, integrated, virtual, then CPU, with ties broken by API version and
index. There is no reduced-feature fallback: a device that misses a requirement is rejected with a
reason in the log. Optional features (anisotropy, BC compression, non-solid fill) are enabled only
when advertised.

The swapchain prefers `R16G16B16A16_SFLOAT` in an extended-linear HDR color space when HDR output is
available, and `B8G8R8A8_SRGB` or `R8G8B8A8_SRGB` otherwise. A missing swapchain-colorspace
extension falls back to SDR. The requested MSAA count (4 by default) becomes the highest supported
count that does not exceed it.

## One frame

Three frames may be in flight. Each has a fence, an image-available semaphore, and its own instance
and frame uniform buffers. A render-finished semaphore is owned per swapchain image.

1. The tick thread records cameras into a frame packet and publishes it to the frame ring.
2. The render thread services pending GPU uploads and render-texture requests, then takes the newest
   ready packet.
3. It waits for that frame slot's fence, acquires a swapchain image, and plans draw batches.
4. One command buffer records the scene passes and the present pass, then submits; present happens in
   a separate call.
5. Render statistics are published and the frame slot is released.

Each camera pass is a separate render-pass instance limited to its viewport. Attachments always load
and store, so clearing is a `vkCmdClearAttachments` confined to the viewport. Multisampled color and
depth resolve in the same subpass to single-sample images, which later passes can sample.

The app renders into a linear HDR scene target. A camera may instead target a `RenderTexture`. After
the geometry passes, the camera's post-process stack runs as a target-sized `R16G16B16A16_SFLOAT`
ping-pong chain, and the result is written back into the camera target for the viewport.

## Geometry

Meshes use one fixed 88-byte vertex layout: position, normal, four UV sets, a linear color, and a
tangent. A mesh has one vertex buffer and one 32-bit index buffer.

Draws are batched by mesh, shader program, render state, topology, and wireframe flag. Uniform values
and texture bindings are per-instance data and never split a batch. A batch is capped at 16,384
instances or 1 MiB of instance records. Opaque batches keep recording order; transparent batches are
sorted back to front.

Each batch is one instanced indexed draw. The per-instance record starts with a 96-byte header (model
matrix, color, UV rectangle) followed by texture slot indices and the shader's uniform template.
Instance records live in a host-visible storage buffer. Frustum culling is done on the CPU against
the world-space mesh bounds; there is no GPU or occlusion culling.

Pipelines are cached by shader, render state, topology, and wireframe. Depth is reverse-Z: the
projection maps near to 1 and far to 0, the depth test is greater-or-equal, and the depth buffer
clears to 0. Wireframe drawing requires the non-solid fill feature; without it, the draw is rejected
with `GraphicsException`.

## Descriptors, textures, and samplers

One descriptor set layout serves all geometry:

| Binding | Contents |
| --- | --- |
| 0 | Frame storage buffer, one 256-byte block per camera pass. |
| 1 | `RayBorkTextures[2048]`: bindless combined image samplers, partially bound and update-after-bind. |
| 2 | Per-instance storage buffer. |

A 4-byte push constant selects the camera pass. Texture slot 0 is a 1×1 white fallback. The bindless
allocator takes the lowest free slot and writes each new texture into all frame sets. A draw does not
write a descriptor: its instance record carries the resolved texture slots, so each material's
sampler is already part of its bindless descriptor. Samplers are cached by filter, wrap, and
anisotropy.

Packed textures are BC7, BC5, R8, or RGBA8 with an sRGB flag. BC uploads require BC texture
compression support.

## Render textures

A `RenderTexture` has a color image and an optional `D32Sfloat` depth image, plus multisampled and
resolved variants. Supported color formats are `Rgba16Float` (default), `Rgba8Unorm`, and
`Rgba8Srgb`; MSAA may be 1, 2, 4, or 8, subject to device support. The maximum dimension is 16384 or
the device limit, whichever is smaller.

`Resize` recreates the GPU images in place but keeps the `RenderTexture`, its color and depth
`Texture` views, format, depth flag, and effective sample count. Contents are undefined after a
resize. Readback returns the resolved color image as tightly packed rows in top-left texel order; no
Y-flip is applied.

## Post-processing

A `PostProcessStack` is an ordered list. When a camera is created it snapshots the stack, each
effect's enabled flag, and custom shader cursors and textures. Later changes affect later cameras.

- `SsaoEffect` reconstructs normals from reverse-Z depth, samples a golden-angle kernel rotated by a
  128×128 blue-noise texture, denoises in two bilateral passes, and composites a multiplied
  occlusion color. It needs a depth attachment.
- `BloomEffect` prefilters HDR color and builds up to five half-resolution pyramid levels with tent
  upsampling, then composites the result.
- `ShaderPostProcessEffect` runs a custom fragment shader with the reserved `sourceColor` binding and,
  optionally, `sourceDepth`. The shader must output scene-linear HDR; the chain applies no tone
  mapping or gamma.

Tone mapping happens only in the present pass. Exposure is `2^EV`; AgX maps the configured white
point to the display maximum with an adjustable contrast. SDR output is in `[0, 1]` with optional
manual sRGB encoding; HDR output is scRGB and keeps values above 1. `TonemappingMode` is fixed at
startup, while exposure, AgX white, and AgX contrast are per-frame settings.

## Recovery

A lost surface or out-of-date swapchain recreates the presentation resources while keeping the
device, bindless table, and GPU assets. A lost device rebuilds the device and all GPU state; live
assets are replayed from the asset bridge, and render textures keep their public objects and stable
ids. The frame in which a device was rebuilt is dropped.

Recovery is attempted at most three times without a completed frame. If it still fails, the app
stops with `GraphicsDeviceLostException`. Validation-layer errors are fatal and become
`GraphicsException`, not warnings.

## Limits

| Limit | Value |
| --- | --- |
| Frames in flight | 3 |
| Swapchain images | At least 3 |
| Bindless sampled textures | 2048 |
| Instance header | 96 bytes |
| Instance record | At most 16 KiB |
| Batch | 16,384 instances or 1 MiB |
| Render texture size | 16384 px |
| Consecutive recoveries | 3 |

See [Architecture](architecture.md) for the thread and frame model and [Assets](assets.md) for how
shaders, meshes, and textures are produced.
