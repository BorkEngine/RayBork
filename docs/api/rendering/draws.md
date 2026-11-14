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
| `DrawTriangleStrip` | Engine triangle | One instance per non-degenerate triangle from the `points.Length - 2` triangle strip; every odd triangle swaps its first two corners, so the strip keeps the `DrawTriangle` winding and back-face behavior. Fewer than three points records nothing. |
| `DrawRay` | Engine line mesh | One segment from `ray.Origin` to `ray.Origin + ray.Direction`. The direction is not normalized, so the segment has the direction's length. A zero direction records nothing. |
| `DrawBoundingBox` | Engine line mesh | The twelve axis-aligned edges of the box, one line instance each; coincident edges of a zero-extent box are skipped. |
| `DrawCube` | Engine cube | Box centered at `position` with `size` extents, or with explicit width/height/length, or under a `Transform`. A zero size component records nothing. |
| `DrawWireCube` | Engine wire cube | The twelve edges as line geometry; no polygon mode. Same size, scalar, and `Transform` forms as `DrawCube`. |
| `DrawSphere` | Engine UV sphere or engine triangle | The simple forms instance the unit UV sphere scaled by `radius` with 32 slices by 16 rings and no LOD. The `rings`/`slices` forms instead compose `2 * slices * (rings - 1)` triangles with pole fans and a closed seam; `rings >= 2` and `slices >= 3`. A `Transform` overload exists for both. Zero radius records nothing. |
| `DrawWireSphere` | Engine wire sphere or engine line | The simple forms instance three great circles. The `rings`/`slices` forms compose `slices` meridians of `rings` segments plus `rings - 1` latitude circles, for `slices * (2 * rings - 1)` line instances. A `Transform` overload exists for both. Zero radius records nothing. |
| `DrawCylinder` | Engine quad and triangle | The simple form is a capped equal-radius cylinder with its base cap at `position`, its axis along world `+Y`, and its height upward: `sides` side quads plus `2 * sides` cap triangles in two instanced draws. The four-argument convenience overload uses 16 sides; the five-argument form takes `sides` explicitly. The full form takes unequal `radiusTop`/`radiusBottom`; equal positive radii use the same two draws, while a tapered cylinder uses one triangle draw with `4 * sides` triangles (two per side segment and one per cap). A zero radius at one end is a cone: one triangle draw with `2 * sides` triangles and no cap at the apex. The start/end form treats `start` and `end` as cap centers with `startRadius`/`endRadius` and orients the local `+Y` axis between them. The `Transform` form puts the bottom cap at the transform origin along local `+Y`. Zero height or two zero radii records nothing. |
| `DrawWireCylinder` | Engine line mesh | One vertical edge per side plus one closed ring per non-zero radius, so `sides * (1 + nonZeroRadiusCount)` line instances. The position, start/end, and `Transform` forms follow `DrawCylinder`. Zero height or two zero radii records nothing. |
| `DrawCapsule` | Engine quad and triangle | The start/end form treats `start` and `end` as the spherical cap centers; the `Transform` form takes the cap-center distance as `height` and centers the capsule at the transform origin along local `+Y`. It is `slices` side quads plus `2 * slices * (2 * rings - 1)` cap triangles in two instanced draws; the total height is `height + 2 * radius`. `rings >= 2` and `slices >= 3`. Zero radius, zero height, or a zero-length start/end segment records nothing. |
| `DrawWireCapsule` | Engine line mesh | One body edge per slice, one equator ring per end, and per hemispherical cap `slices` meridians of `rings` segments plus `rings - 1` interior latitude rings, for `slices * (4 * rings + 1)` line instances. The start/end and `Transform` anchors match `DrawCapsule`. Zero radius, zero height, or a zero-length start/end segment records nothing. |
| `DrawPlane` | Engine plane | `XZ` plane centered at `center`, front face toward world `+Y`, `size` is width along `X` and depth along `Z`; the `Transform` form centers it at the transform origin and local `+Y` is its normal. |
| `DrawCircle` | Engine line mesh | A closed 36-segment circle. The axis-angle form starts from the local `XZ` plane with local `+Y` as its normal, rotates it by `rotationAngle` degrees around `rotationAxis`, and translates it to `center`. The `Transform` form uses the local `XZ` plane and the transform rotation and scale. A zero rotation axis is `ArgumentException`; zero radius records nothing. |
| `DrawGrid` | Engine line mesh | `XZ` grid centered at the world origin with `slices` cells per axis, one uniform color, and `spacing` world units between lines. It has `slices + 1` lines per axis, so it is one instanced draw with `2 * (slices + 1)` instances spanning `slices * spacing` per axis. |
| `DrawBillboard` | Engine quad | Centered textured quad. `faceCamera` true faces along the camera's view direction; false keeps world up and applies only the camera's yaw. |

**Programs and render states.** Shapes and lines record against the built-in `/shaders/primitive`
program: vertex position at location 0, linear vertex color at location 6, no uniforms and no
textures, opaque blending, back-face culling, `greater_equal` depth test, depth write. Its vertex
stage multiplies the vertex color by the per-instance tint and its fragment stage writes that color.
The billboard uses the built-in `/shaders/mesh` program with the `RenderState.AlphaBlend` state
override. The batch key includes the effective render state, so a billboard record never merges with
a shape draw.

**Colors and tint.** `color` is a linear `Color`; no conversion is applied at draw time. A
display-encoded authoring color converts once with `Color.FromSrgb` or `Color.FromSrgbBytes`. The
engine meshes carry opaque white vertex colors, so the helper color is the output color, recorded as
the per-instance tint `rbInstance.Color`. `DrawBillboard` takes it as its `color` argument. A custom
shader sees the same field and must apply it itself.

**Instance composition.** Each helper records one world matrix per instance that maps the unit mesh
into world space: scale and translation for the cube, sphere, plane, wire cube, and wire sphere; a
line basis built from the segment endpoints for `DrawLine`, every `DrawLines` pair, and every circle
or wire segment; a camera-facing basis for `DrawPoint` and `DrawBillboard`; the affine
reference-triangle map for `DrawTriangle`, `DrawTriangleStrip`, the tessellated sphere, and the
cylinder and capsule caps; a side-quad basis for equal-radius cylinder sides and capsule sides; two
reference-triangle maps per tapered cylinder side segment; and one line matrix per grid, wire, ray,
or bounding-box segment. A transformed overload applies the same `local * transform.ToMatrix()`
composition to every one of those matrices. The mesh bounds are transformed by the same matrix for
frustum culling.

**No suffix names.** The primitive families use ordinary C# overloads. The engine has no `Ex`, `Rec`,
`Pro`, or `V` suffix variants: `DrawCube`, `DrawWireCube`, `DrawSphere`, `DrawWireSphere`,
`DrawCylinder`, `DrawWireCylinder`, `DrawCapsule`, `DrawWireCapsule`, `DrawPlane`, and `DrawCircle`
each cover their scalar, tessellation, start/end, and `Transform` forms by overload.

**Transform composition.** Every world shape has a `Transform` overload in addition to the simple
and shape/position forms. The transform supplies the world position, rotation, and scale, and the
shape dimensions stay explicit. The instance matrix is `local * transform.ToMatrix()` under the
row-vector convention, where `local` builds the shape dimensions in local space:
`CreateScale(size)`, `CreateScale(radius)`, or `CreateScale(size.X, 1, size.Y)` for the box, sphere,
or plane; the local cylinder or capsule basis for those shapes. The transform scale therefore
multiplies the shape dimensions, and an identity rotation with unit scale reproduces the matching
simple overload at `Transform.Position`. A transformed cube, wire cube, sphere, wire sphere, plane,
or circle is centered at the transform origin; a transformed cylinder or wire cylinder puts its
bottom cap/ring at the transform origin along local `+Y`; a transformed capsule or wire capsule is
centered at the transform origin along local `+Y` with its cap centers at `±height/2`.

**Validation.** All helpers are tick-thread only, and a disposed camera or primitive asset is
`ObjectDisposedException`.

- A non-finite numeric argument throws `ArgumentException`. A `Transform` overload requires finite
  position, rotation, and scale; a non-finite component is `ArgumentException`.
- A negative extent, radius, height, width, or point size throws `ArgumentOutOfRangeException`. A
  negative transform scale component is also `ArgumentOutOfRangeException`.
- An exactly zero extent, radius, height, start/end segment, or transform scale component records
  nothing.
- `sides < 3`, `rings < 2`, `slices < 3`, `slices < 1` for `DrawGrid`, and `spacing <= 0` throw
  `ArgumentOutOfRangeException`; no helper substitutes a default tessellation.
- A zero `rotationAxis` on `DrawCircle` throws `ArgumentException`; the axis-angle form has no plane
  without it.
- A `null` texture throws `ArgumentNullException`; a disposed texture is `ObjectDisposedException`.
- A camera whose app has no primitive assets attached throws `InvalidOperationException`. Game code
  does not hit this case: the app attaches the primitive assets before `IGame.Setup`.

Validation runs before anything is retained or recorded, so a rejected call leaves the frame packet
and its retention set untouched.

**Batching and statistics.** Every helper records the same kind of draw record as `DrawMesh`, with
the engine mesh, internal program, and effective render state, so it batches by
`(mesh, program, effective render state)` and follows the normal culling, sorting, splitting, and
statistics rules. Line-topology draws contribute zero triangles; `DrawPoint` draws the quad and
contributes two. `DrawLines`, `DrawGrid`, `DrawCircle`, `DrawRay`, `DrawBoundingBox`, and the wire
helpers are one instanced line draw each. `DrawCylinder` is two instanced draws for equal positive
radii because its sides and caps use different meshes; a tapered cylinder is one triangle draw.
`DrawCapsule` is two instanced draws: side quads and cap triangles. Equal records merge: two
`DrawCube` calls are one instanced draw with two instances, and every triangle- or line-based helper
merges into the shared engine mesh batch for its program and render state.

**Lines and wire shapes.** Lines are one pixel wide. The engine draws them as `LineList` geometry
and never requests the `wideLines` device feature, so v1 has no thick-line option and no per-line
width argument. Wire shapes are line geometry, not polygon-mode rasterization; the engine never
changes the polygon mode.

**Textured quads.** `DrawBillboard` samples the texture through the engine material's `mainTexture`
binding. That material is shared by every billboard, so the binding is set immediately before the
draw is recorded and cleared immediately afterwards. The recorded frame retains the texture until
the render thread releases the slot, so the shared material never keeps a game texture and a
texture disposed after the call stays alive for the recorded frame. To shade a quad with a custom
material, draw a quad mesh instead.

`DrawText` and `DrawTextBillboard` are M6 and are not part of this surface.
