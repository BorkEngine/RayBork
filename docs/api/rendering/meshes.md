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

> **M4 realized.** M4 ships `Model` and `ModelPart` as a composition API: `IAssetCatalog.Load<Model>`
> loads a packed model descriptor and every part's mesh, creates one material per part over the cached
> internal program, and returns a **new `Model` wrapper per call**. `Model` is not an `IAsset`: it has
> no path, no catalog cache entry, and no shared identity. A part with no `[model] shader` in its
> sidecar is baked with the built-in shader path `shaders/mesh`; the model loader requires every part
> descriptor to carry a canonical shader path and has no runtime default, so a descriptor with a
> missing or stale path fails to load. A model draw uses each part material's current uniform cursor
> and texture bindings; there is no shader-override draw overload.

- `Mesh` is one primitive: one vertex/index buffer pair and one topology. It is immutable after load
  or build. `Bounds` is in mesh space. The fixed 88-byte vertex layout is position, normal, uv,
  uv1, uv2, uv3, linear color, and tangent; the shader ABI is in §7.4.
- A loaded mesh has the canonical pack path. A procedural mesh has a reserved synthetic path under
  `/_procedural/mesh/`, `IsProcedural` is `true`, and the path cannot be loaded from a pack.
- `Vertices` and `Indices` expose a read-only CPU copy for both loaded and procedural meshes. A
  loaded mesh decodes the arrays from the packed payload; a procedural mesh returns the builder's
  snapshot. The arrays stay valid until the mesh is disposed. Textures do not keep CPU pixels.
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
- Meshes keep their CPU vertices for collision and bounds in v1. Textures do not keep CPU pixels.
