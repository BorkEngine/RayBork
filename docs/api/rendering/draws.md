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

The primitive helpers draw engine-owned unit meshes through two built-in programs. They are
convenience APIs over the same draw records, batching, culling, sorting, and statistics as
`DrawMesh`; there is no second rendering path. The meshes and the primitive program are internal
engine assets with no public accessor, built once before `IGame.Setup`.

| Method | Geometry | Semantics |
| --- | --- | --- |
| `DrawLine` | Engine line mesh | One segment, one instance. A zero-length segment records nothing. |
| `DrawLines` | Engine line mesh | One instance per non-zero consecutive point pair, so a polyline of `N` points is `N - 1` segments. Fewer than two points, or no non-zero pair, records nothing. |
| `DrawPoint` | Engine quad | Camera-facing square whose `size` is its world-space width and height. Zero size records nothing. |
| `DrawTriangle` | Engine triangle | One triangle under the orientation-preserving affine map from the unit reference triangle. Counter-clockwise as seen by the camera is front; clockwise is culled. A degenerate or collinear triangle records nothing. |
| `DrawCube` | Engine cube | Box centered at `position` with `size` extents. A zero size component records nothing. |
| `DrawSphere` | Engine UV sphere | Unit sphere scaled by `radius`; 32 slices by 16 rings, no LOD. Zero radius records nothing. |
| `DrawCylinder` | Engine quad and triangle | Capped cylinder with its base cap at `position`, its axis along world `+Y`, and its height upward: `sides` side quads plus `2 * sides` cap triangles, recorded as two instanced draws. The side quads use the exact ring positions, so the silhouette is exact for any `sides`. Zero radius or height records nothing. |
| `DrawPlane` | Engine plane | `XZ` plane centered at `center`, front face toward world `+Y`, `size` is width along `X` and depth along `Z`. |
| `DrawGrid` | Engine line mesh | `XZ` grid centered at the world origin with `slices` cells per axis, one uniform color, and `spacing` world units between lines. It has `slices + 1` lines per axis, so it is one instanced draw with `2 * (slices + 1)` instances spanning `slices * spacing` per axis. |
| `DrawWireCube` | Engine wire cube | The twelve edges as line geometry; no polygon mode. |
| `DrawWireSphere` | Engine wire sphere | Three great circles as line geometry. |
| `DrawBillboard` | Engine quad | Centered textured quad. `faceCamera` true faces along the camera's view direction; false keeps world up and applies only the camera's yaw. |
| `DrawTexture` | Engine quad | Screen-space quad over the camera viewport, in render pixels. |

**Programs and render states.** Shapes and lines record against the built-in `/shaders/primitive`
program: vertex position at location 0, linear vertex color at location 6, no uniforms and no
textures, opaque blending, back-face culling, `greater_equal` depth test, depth write. Its vertex
stage multiplies the vertex color by the per-instance tint and its fragment stage writes that color.
Billboards and textured quads use the built-in `/shaders/mesh` program with a state override:
billboards record `RenderState.AlphaBlend`, and `DrawTexture` records alpha blending with
`Cull.None`, `DepthTest.Always`, and no depth write. The batch key includes the effective render
state, so a billboard record and an overlay record never merge with a shape draw or with each other.

**Colors and tint.** `color` is a linear `Color`; no conversion is applied at draw time. The engine
meshes carry opaque white vertex colors, so the helper color is the output color, recorded as the
per-instance tint `rbInstance.Color`. `DrawBillboard` takes it as its `color` argument and
`DrawTexture` takes it as the optional `tint`, which defaults to opaque white. A custom shader sees
the same field and must apply it itself.

**Instance composition.** Each helper records one world matrix per instance that maps the unit mesh
into world space: scale and translation for the cube, sphere, plane, wire cube, and wire sphere; a
line basis built from the segment endpoints for `DrawLine` and for every `DrawLines` pair; a
camera-facing basis for `DrawPoint` and `DrawBillboard`; the affine reference-triangle map for
`DrawTriangle` and the cylinder caps; a side-quad basis for the cylinder sides; and one line matrix
per grid line for `DrawGrid`. The mesh bounds are transformed by the same matrix for frustum
culling.

**Validation.** All helpers are tick-thread only, and a disposed camera or primitive asset is
`ObjectDisposedException`.

- A non-finite numeric argument throws `ArgumentException`.
- A negative extent, radius, height, width, or point size throws `ArgumentOutOfRangeException`.
- An exactly zero extent, radius, height, segment length, or destination width/height records
  nothing.
- `sides < 3`, `slices < 1`, and `spacing <= 0` throw `ArgumentOutOfRangeException`; no helper
  substitutes a default tessellation.
- A `null` texture throws `ArgumentNullException`; a disposed texture is `ObjectDisposedException`.
- A camera whose app has no primitive assets attached throws `InvalidOperationException`. Game code
  does not hit this case: the app attaches the primitive assets before `IGame.Setup`.

Validation runs before anything is retained or recorded, so a rejected call leaves the frame packet
and its retention set untouched.

**Batching and statistics.** Every helper records the same kind of draw record as `DrawMesh`, with
the engine mesh, internal program, and effective render state, so it batches by
`(mesh, program, effective render state)` and follows the normal culling, sorting, splitting, and
statistics rules. Line-topology draws contribute zero triangles; `DrawPoint` draws the quad and
contributes two. `DrawLines` and `DrawGrid` are one instanced line draw each. `DrawCylinder` is two
instanced draws because its sides and caps use different meshes. Equal records merge: two
`DrawCube` calls are one instanced draw with two instances.

**Lines and wire shapes.** Lines are one pixel wide. The engine draws them as `LineList` geometry
and never requests the `wideLines` device feature, so v1 has no thick-line option and no per-line
width argument. Wire shapes are line geometry, not polygon-mode rasterization; the engine never
changes the polygon mode.

**Textured quads.** `DrawBillboard` and `DrawTexture` sample the texture through the engine
material's `mainTexture` binding. That material is shared by every billboard and textured quad, so
the binding is set immediately before the draw is recorded and cleared immediately afterwards. The
recorded frame retains the texture until the render thread releases the slot, so the shared
material never keeps a game texture and a texture disposed after the call stays alive for the
recorded frame. To shade a quad with a custom material, draw a quad mesh instead.

**`DrawTexture` coordinate space.** `destination` is in render pixels relative to the camera
viewport, with a top-left origin and Y increasing downward: the same space as
`Camera.WorldToScreen` and `ScreenToWorldRay`, not window coordinates. The quad's top-left UV lands
on the destination top-left corner, the destination size maps to the quad size, and the result is
pixel-exact for perspective and orthographic cameras. The overlay matrix is built from an NDC
rectangle at depth `0.5` multiplied by the inverse view-projection matrix. Its homogeneous `w` is
constant, so it is affine in world space, and the quad's corners are the destination rectangle's
clip positions at that depth: an on-screen overlay lies inside the camera frustum and is not culled,
including for a translated or rotated perspective camera. The recorded state is an overlay:
`DepthTest.Always`, no depth write, and `Cull.None`, so it draws over scene geometry regardless of
depth. A destination width or height of zero records nothing.

`DrawText` and `DrawTextBillboard` are M6 and are not part of this surface.
