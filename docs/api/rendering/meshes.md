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

public sealed class Model : IAsset {
    public string Path { get; }
    public IReadOnlyList<ModelPart> Parts { get; }
    public BoundingBox Bounds { get; }
    public bool IsDisposed { get; }

    public void Dispose();
}

public readonly struct ModelPart {
    public string Name { get; }
    public Mesh Mesh { get; }
    public Shader Shader { get; }
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

> **M4 realized.** M4 ships `Model` and `ModelPart`: `IAssetCatalog.Load<Model>` loads a packed model
> descriptor and every part's mesh and shader, and `Model` is a reference-counted engine asset. A part
> with no `[model] shader` in its sidecar is baked with the built-in canonical shader path
> `shaders/default`; an older descriptor with a `null` shader path falls back to `/shaders/default`
> at load time. Each part also carries an internal bake-default shader-state snapshot:
> `DrawModel(model, transform)` uses that snapshot, while `DrawModel(model, transform, shader, ...)`
> overrides the shader for every part and snapshots the current cursor.

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
  into each part's `LocalTransform`. `Bounds` is the union in model space. glTF 2.0 and OBJ are
  converted to this packed engine format by the bake; the runtime never parses them.
- UVs use a top-left origin. glTF texture coordinates are used as authored; Wavefront OBJ authors V
  from the bottom of the texture, so the bake flips it (`v = 1 - v`) into the engine's top-left
  convention, matching raylib's OBJ loader. U is unchanged, and a face corner with no `vt` keeps the
  zero UV default.
- `ModelPart.LocalTransform` is applied before the draw's instance transform:
  `world = part.LocalTransform.ToMatrix() * instanceTransform.ToMatrix()` under the row-vector
  convention. The engine deliberately does not compose it through `Transform.WithParent`, which can
  throw when the composed matrix contains shear.
- `ModelPart.Shader` is the baked shader for the part, or the built-in default shader when the bake
  had none. `DrawModel(model, transform)` uses the shader's bake-default render state, uniform
  template, and white texture fallbacks; mutating `part.Shader` with `SetUniform` does not change
  the draw. To override, pass a shader to `DrawModel`.
- `ModelPart.Bounds` is in model space after the part's local transform.
- `Model` is not an animation asset. Skeletal and morph animation are post-1.0.
- A model's parts may share meshes and shaders. The model owns one reference to every **distinct**
  part mesh and shader object. `Model.Dispose` releases one owner reference for the model; the
  distinct part references are released once, with the model's last owner. Two models that share a
  part each release their own reference. Dispose the model rather than individual parts unless those
  parts were loaded independently.
- `Load<Model>` twice returns the same instance and acquires one owner reference per load; disposing
  one reference keeps the model and its parts usable for the other owner. A failed model load releases
  every part reference it already acquired and leaves no cache entry, so a retry starts a fresh load.
- A part's mesh and shader become disposed when their last owner releases them, including a model
  that was the last owner. A frame that recorded the model keeps the part assets alive through its
  frame retention set.
- Meshes keep their CPU vertices for collision and bounds in v1. Textures do not keep CPU pixels.
