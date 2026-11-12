# Rendering: Draws

[API overview](../../api.md) · [document map](../../api.md#document-map)

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
