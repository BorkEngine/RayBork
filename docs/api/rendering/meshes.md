# Rendering: Meshes

[API overview](../../api.md) · [document map](../../api.md#document-map)

### 7.5 `Mesh` and `Model`

```csharp
public sealed class Mesh : IAsset {
    public string Path { get; }
    public bool IsProcedural { get; }
    public PrimitiveTopology Topology { get; }
    public ReadOnlyMemory<Vertex> Vertices { get; }
    public ReadOnlyMemory<uint> Indices { get; }
    public int VertexCount { get; }
    public int IndexCount { get; }
    public BoundingBox Bounds { get; }
    public bool HasCollisionAcceleration { get; }
    public bool IsDisposed { get; }

    public void Dispose();
}

public struct Vertex {
    public Vector3 Position;
    public Vector3 Normal;
    public Vector2 Uv;
    public Vector2 Uv1;
    public Vector2 Uv2;
    public Vector2 Uv3;
    public Color Color;
    public Vector4 Tangent;

    public Vertex(
        Vector3 position,
        Vector3? normal = null,
        Vector2? uv = null,
        Vector2? uv1 = null,
        Vector2? uv2 = null,
        Vector2? uv3 = null,
        Color? color = null,
        Vector4? tangent = null);
}

public enum MeshNormalMode {
    Smooth,
    Flat,
}

public sealed class MeshBuilder {
    public PrimitiveTopology Topology { get; set; }
    public List<Vertex> Vertices { get; }
    public List<uint> Indices { get; }

    public void SetVertices(ReadOnlySpan<Vertex> vertices);
    public void SetIndices(ReadOnlySpan<uint> indices);

    public void ComputeNormals(MeshNormalMode mode = MeshNormalMode.Smooth);
    public void ComputeTangents();
    public BoundingBox ComputeBounds();
    public void GenerateCollisions();

    public void GeneratePoly(int sides, float radius);
    public void GeneratePlane(float width, float length, int resX, int resZ);
    public void GenerateCube(float width, float height, float length);
    public void GenerateSphere(float radius, int rings, int slices);
    public void GenerateHemiSphere(float radius, int rings, int slices);
    public void GenerateCylinder(float radius, float height, int slices);
    public void GenerateCone(float radius, float height, int slices);
    public void GenerateTorus(float radius, float size, int radSeg, int sides);
    public void GenerateKnot(float radius, float size, int radSeg, int sides);

    public Task<Mesh> BuildAsync(CancellationToken cancellationToken = default);
}

public sealed class Model : IDisposable {
    public Model(params ModelPart[] parts);
    public Model(IEnumerable<ModelPart> parts);

    public IReadOnlyList<ModelPart> Parts { get; }
    public BoundingBox Bounds { get; }
    public bool IsDisposed { get; }

    public void Dispose();
}

public readonly struct ModelPart {
    public ModelPart(
        string name,
        Mesh mesh,
        Shader shader,
        RenderState? renderState = null,
        Transform? localTransform = null);

    public string Name { get; }
    public Mesh Mesh { get; }
    public Shader Shader { get; }
    public RenderState RenderState { get; }
    public Transform LocalTransform { get; }
    public BoundingBox Bounds { get; }
}

public enum PrimitiveTopology {
    TriangleList,
    TriangleStrip,
    LineList,
    LineStrip,
    PointList,
}
```

> **As built.** `Model` and `ModelPart` are a composition API: `IAssetCatalog.Load<Model>`
> loads a packed model descriptor and every part's mesh, creates one material per part over the cached
> internal program, and returns a **new `Model` wrapper per call**. `Model` is not an `IAsset`: it has
> no path, no catalog cache entry, and no shared identity. A part with no `[model] shader` in its
> sidecar is baked with the built-in shader path `shaders/mesh`; the model loader requires every part
> descriptor to carry a canonical shader path and has no runtime default, so a descriptor with a
> missing or stale path fails to load. A model draw uses each part material's current uniform cursor
> and texture bindings; there is no shader-override draw overload.
>
> A mesh's descriptor may also name a baked ray-mesh collision payload. The loader decodes and
> validates it before the GPU upload and fails the load when it is missing or corrupt; a mesh whose
> descriptor has no such member loads without an acceleration structure and brute-forces its
> retained triangles.

- `Mesh` is one primitive: one vertex/index buffer pair and one topology. It is immutable after load
  or build. `Bounds` is in mesh space. The fixed 88-byte vertex layout is position, normal, uv,
  uv1, uv2, uv3, linear color, and tangent; the shader ABI is in §7.4.
- A loaded mesh has the canonical pack path. A procedural mesh has a reserved synthetic path under
  `/_procedural/mesh/`, `IsProcedural` is `true`, and the path cannot be loaded from a pack.
- `Vertices` and `Indices` expose a read-only CPU copy for both loaded and procedural meshes. A
  loaded mesh decodes the arrays from the packed payload; a procedural mesh returns the builder's
  snapshot. The arrays stay valid until the mesh is disposed. Textures do not keep CPU pixels.
- `HasCollisionAcceleration` reports whether the mesh carries an immutable ray-mesh acceleration
  structure. A procedural mesh has one only when `GenerateCollisions` was called before the build
  that produced it. A loaded mesh has one when its descriptor names a baked `RBVC` v1 collision
  payload; the loader decodes and validates that payload before the GPU upload, and a missing,
  corrupt, or mismatched payload fails the load with `AssetLoadException` `ASSET_LOAD_FAILED` naming
  the mesh. A descriptor baked before the member existed loads with no structure and brute-forces.
  The structure is immutable, is not disposed with the mesh, and is safe to query from any thread.
  The property throws `ObjectDisposedException` after disposal.
- `Mesh` is one-shot. Changing a `MeshBuilder` after `BuildAsync` does not change any mesh already
  built from it; the builder can be reused to build another mesh.

Build a procedural mesh with the engine factory:

```csharp
MeshBuilder builder = ctx.CreateMeshBuilder(PrimitiveTopology.TriangleList);
builder.Vertices.Add(new Vertex(new Vector3(0, 0, 0), uv: new Vector2(0, 1)));
builder.Vertices.Add(new Vertex(new Vector3(1, 0, 0), uv: new Vector2(1, 1)));
builder.Vertices.Add(new Vertex(new Vector3(0, 1, 0), uv: new Vector2(0, 0)));
builder.Indices.AddRange([0u, 1u, 2u]);

builder.ComputeNormals();
builder.ComputeTangents();
Mesh mesh = await builder.BuildAsync();
```

A named generator fills the geometry, normals, and first UV set in one call:

```csharp
MeshBuilder builder = ctx.CreateMeshBuilder();
builder.GenerateCube(2f, 2f, 2f);
Mesh mesh = await builder.BuildAsync();
```

- `BuildAsync` snapshots the lists, validates the topology/index counts/ranges and finite
  attributes, computes bounds when `ComputeBounds` was not called, uploads through the
  non-blocking GPU bridge, and returns only after the GPU resource is ready. A failed or canceled
  build destroys any partial resource and leaves the builder reusable. The caller owns one
  reference and must dispose the returned mesh exactly once.
- `BuildAsync` may be called from `Setup`, `Tick`, or any thread, and awaited on any thread. The
  builder instance itself is not thread-safe; use one builder per thread. Awaiting a build on the
  tick thread suspends that tick's continuation like an asset load. After app shutdown every
  builder member throws `ObjectDisposedException`.
- `ComputeNormals(Smooth)` accumulates the unnormalized area-weighted face normal per shared
  position and normalizes the sum. `ComputeNormals(Flat)` splits every triangle into three vertices
  with its face normal and rebuilds the indices; a triangle strip keeps its topology with one
  degenerate connecting index. A vertex that belongs only to degenerate triangles keeps a zero
  normal.
- `ComputeTangents` requires normals and the first UV set. It accumulates per-triangle tangent and
  bitangent vectors, orthonormalizes against the vertex normal, and stores the bitangent handedness
  in `Tangent.W`. Degenerate triangles produce a zero tangent. Call `ComputeNormals` first.
- `ComputeBounds` is optional; `BuildAsync` computes bounds from the current positions when it was
  not called. The stored bounds are not recomputed after the exposed vertex list is mutated; call it
  again after editing positions.
- Normal and tangent generation support triangle topologies only. Line and point meshes are valid
  for `BuildAsync` but have no generated normals or tangents.
- `GenerateCollisions()` opts a procedural build into a ray-mesh acceleration structure. It is an
  imperative builder step like `ComputeNormals`: it changes no geometry, is idempotent, and the
  selection persists for every later `BuildAsync` from that builder. `BuildAsync` builds the
  structure from the same snapshot it validates and uploads, before the GPU upload, and returns a
  mesh whose `HasCollisionAcceleration` is `true`. A snapshot with no non-degenerate triangle builds
  without a structure. A failed or canceled build creates no asset, keeps the builder reusable, and
  never lets a later geometry change affect a structure already attached to a mesh. Only
  triangle-list and triangle-strip builders accept it; any other topology throws
  `NotSupportedException`.
- The nine `Generate*` methods are the procedural counterparts of raylib's `GenMesh*` factories. Each
  replaces the builder's vertices and indices with one named primitive, sets `Topology` to
  `TriangleList`, discards explicit bounds, and fills outward normals and the first UV set directly.
  They are pure CPU and need no GPU, tick thread, or async path: `BuildAsync` uploads the generated
  geometry like any other builder content, and `ComputeBounds` recomputes bounds from it. Every
  generated triangle winds counter-clockwise around its outward normal. The methods generate normals
  but not tangents; call `ComputeTangents` after generation when a tangent basis is needed.
- `GeneratePoly(sides, radius)` is a regular polygon disc centered at the origin in the XZ plane with
  its front face toward +Y: a center vertex plus one perimeter vertex per side, and `sides * 3`
  indices. All normals are +Y. UVs map the disc into the unit square with the center at
  `(0.5, 0.5)` and the first corner, on +Z, at `(0.5, 0)`.
- `GeneratePlane(width, length, resX, resZ)` is an XZ grid centered at the origin with `resX` by
  `resZ` quads and +Y normals. UV `u` runs 0..1 from -X to +X and `v` runs 0..1 from -Z to +Z.
- `GenerateCube(width, height, length)` is a box centered at the origin with 24 vertices and 36
  indices: four independent vertices per face carry the face's outward normal. Each face's UVs cover
  0..1 with the origin at the face's top-left corner and V increasing downward, matching the engine's
  top-left UV convention.
- `GenerateSphere(radius, rings, slices)` is a UV sphere with its poles on +Y and -Y. `rings` counts
  the latitude bands from pole to pole and `slices` the longitude segments. Each pole is one vertex
  and the seam duplicates the first meridian at `u = 1`; normals equal the position divided by
  `radius`. UV `u` runs around +Y from +Z toward +X, and `v` runs 0 at the +Y pole to 1 at the -Y
  pole.
- `GenerateHemiSphere(radius, rings, slices)` is the +Y half of that sphere, from the pole down to the
  equator at `y = 0`. `rings` counts the bands from pole to equator. The equator is open: there is no
  bottom cap, so the underside is visible from below. Normals and UVs match the full sphere, with the
  equator at `v = 1`.
- `GenerateCylinder(radius, height, slices)` has its base at `y = 0`, its top at `y = height`, and its
  axis on +Y. It has side quads and a cap at each end; side normals are horizontal and the caps are
  ±Y. Side UV `u` runs around the axis from +Z toward +X and `v` runs 0 at the top to 1 at the base.
  Each cap uses a planar disc mapping centered at `(0.5, 0.5)`.
- `GenerateCone(radius, height, slices)` has its base circle at `y = 0` and its apex at
  `(0, height, 0)`. The side is smooth and its normals slant outward and up; the base cap normal is
  -Y. Side UV `u` runs around the axis from +Z toward +X and `v` runs 0 at the apex to 1 at the base.
- `GenerateTorus(radius, size, radSeg, sides)` is a torus centered at the origin in the XZ plane with
  its hole along +Y. `radius` is the major radius to the tube center and `size` the tube radius, so
  the outer radius is `radius + size`. `radSeg` segments the major ring and `sides` the tube. UV `u`
  runs around the major ring from +Z toward +X and `v` runs around the tube from the outer equator
  toward +Y.
- `GenerateKnot(radius, size, radSeg, sides)` is a trefoil-knot tube centered at the origin: the loop
  lies in the XZ plane, the lobes rise along ±Y, and every vertex is within `radius + size` of the
  origin. `radius` is the winding radius of the center line and `size` the tube radius. `radSeg`
  segments the closed curve and `sides` the tube. UV `u` runs along the curve and `v` runs around the
  tube.
- The generators validate their arguments before replacing any geometry: `sides`, `rings`, `slices`,
  and `radSeg` are at least 3, `resX` and `resZ` are at least 1, and every dimension, radius, and
  size is positive and finite. An invalid value throws `ArgumentOutOfRangeException` with the
  offending parameter name; there is no silent clamping or default tessellation. After app shutdown
  every generator throws `ObjectDisposedException`.
- `Model` is a flat list of parts. It has no node hierarchy in v1; glTF node transforms are baked
  into each part's `LocalTransform`. `Bounds` is the union of the part bounds. glTF 2.0 and OBJ are
  converted to this packed engine format by the bake; the runtime never parses them.
- UVs use a top-left origin. glTF texture coordinates are used as authored; Wavefront OBJ authors V
  from the bottom of the texture, so the bake flips it (`v = 1 - v`) into the engine's top-left
  convention, matching raylib's OBJ loader. U is unchanged, and a face corner with no `vt` keeps the
  zero UV default.
- `ModelPart.LocalTransform` is applied before the draw's instance transform:
  `world = part.LocalTransform.ToMatrix() * instanceTransform.ToMatrix()` under the row-vector
  convention. The engine deliberately does not compose it through `Transform.WithParent`, which can
  throw when the composed matrix contains shear. It defaults to `Transform.Identity`.
- `ModelPart.Bounds` is in model space after the part's local transform. It is computed from
  `Mesh.Bounds` as the axis-aligned box of the eight transformed corners; it is not passed in.
- `ModelPart.Shader` is the part material and is never `null`. It starts from the program's bake
  defaults, and `DrawModel`/`DrawModelInstanced` snapshot the material's **current** uniform cursor and
  texture bindings at the call, so `SetUniform` before a draw changes it. `ModelPart.RenderState` is
  the effective state: a `null` constructor argument uses `shader.DefaultRenderState`.
- Each part gets its own material. Two parts that name the same program path share the internal
  program but never a material, and two materials over one program batch together.
- `Model` is not an animation asset. Skeletal and morph animation are post-1.0.
- Constructing a model over caller parts acquires one engine reference to every **distinct** part mesh
  and transfers every **distinct** part material to the model; callers create one material per part.
  `Model.Dispose` is idempotent, disposes each distinct material once, and releases each distinct mesh
  reference once. Two models that share parts each release their own references. Dispose the model
  rather than the transferred materials; a mesh the game also loaded directly keeps its own reference
  and is disposed separately.
- `IsDisposed` stays readable after disposal; `Parts` and `Bounds` throw `ObjectDisposedException`.
- `Load<Model>` twice returns two distinct wrappers with distinct materials over the same cached part
  meshes and programs. There is no model cache and no model lifetime: a failed or canceled load
  releases every part mesh reference and material it already acquired, and a retry starts a fresh
  wrapper.
- A part's mesh becomes disposed when its last owner releases it, including a model that was the last
  owner. A frame that recorded the model keeps the part meshes, internal programs, and bound textures
  alive through its frame retention set; the model and material objects are not engine assets and add
  no frame hold.
- Meshes keep their CPU vertices and indices for collision and bounds in v1;
  `Collision.GetRayMesh` reads them directly when the mesh has no acceleration structure. The
  collision structure and its baked payload are CPU-only: they add no GPU resource, frame hold, or
  device-recovery work. Textures do not keep CPU pixels.
