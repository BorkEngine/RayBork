# Rendering: Draws

[API overview](../../api.md) · [document map](../../api.md#document-map)

### 7.8 Draw calls and batching

Every `Camera.Draw*` call records a value-only command. The render thread builds batches from the
sealed frame.

**Batch key.** A batch is keyed by:

1. the camera pass and render target;
2. the mesh (vertex/index buffers and topology);
3. the internal shader program;
4. the effective `RenderState`.

A material instance is not part of the key: two different `Shader` materials over the same internal
program batch together. Uniform values and texture bindings are **not** part of the key either. They
are written per instance. Consequently:

- Repeated `DrawMesh` calls with the same mesh/program/state become one instanced draw even when
  every texture or uniform differs and each call uses a different material over that program.
- A different mesh, internal program, or render state starts a new batch.
- Different meshes with the same program are separate draws in v1. A Vulkan instanced draw uses one
  vertex/index buffer, so "same program" alone cannot merge different geometry. Multi-draw indirect
  and GPU-driven batching are post-1.0.
- Different cameras do not batch together.
- Model parts are separate meshes and each part draws with its own material snapshot. A model with
  three parts produces at most three batches per mesh/program/state, each with one instance per model
  draw. Two parts whose materials share a program and whose mesh and state match still batch together.
- The planner splits a batch when it would exceed `MaxInstancesPerBatch` (default `16384`) or
  `MaxInstanceBytesPerBatch` (default 1 MiB, including the 96-byte instance header). Splitting
  preserves instance order and counts as separate draw calls. A single instance larger than the byte
  limit is still a batch of one.

**Per-instance data.** Each instance carries the world transform, the tint, a UV rectangle for
sprite/text-style shaders, and the reflected uniform/texture data. Built-in shaders consume the
fields they need; custom shaders see them through the generated ABI. Uniform values and texture
bindings never split a batch.

**Culling.** Instances are frustum-culled against the pass view-projection. The planner transforms the
mesh-space bounds to a world-space AABB and tests it against the six normalized frustum planes; an
instance is culled only when its bounds are fully outside a plane. A model part's world matrix
already includes its local transform. `RenderStats` reports the number of culled instances. There is
no occlusion culling, LOD, or GPU-driven culling in v1. Custom vertex displacement can invalidate
bounds; v1 has no per-draw bounds override, so such a shader should disable culling by drawing a
larger mesh or accepting incorrect culling.

**Sorting.** The planner emits one ordered plan per camera pass; it never reorders across passes.

- Opaque batches keep the order in which their first instance was recorded.
- Alpha, additive, and multiply batches are ordered back-to-front by their nearest instance depth,
  and the instances inside each transparent batch are sorted back-to-front by depth.
- Sorting is stable for equal depths: equal-depth items keep their recording order.
- A split batch's chunks preserve the sorted instance order and stay adjacent in submission order.

The plan reports draw calls after splitting, instances submitted after culling, triangles, and
culled instances. Triangles count `indexCount / 3` per triangle-list instance, `indexCount - 2` per
triangle-strip instance, and `0` for line and point topologies. The renderer derives pipeline counts,
not the planner.

**Explicit instancing.** `DrawInstanced` records one snapshot of the material state and applies it to
every transform in the span. `DrawModelInstanced` records one snapshot of each part material and
applies it to every transform in the span. Each is one batch per part/mesh by construction, subject to
the split limits. To vary a texture per instance, use separate `DrawMesh` calls; the automatic
batcher will merge them.

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
