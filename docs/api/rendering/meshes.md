# Rendering: Meshes

[API overview](../../api.md) · [document map](../../api.md#document-map)

### 7.5 `Mesh` and `Model`

```csharp
public sealed class Mesh : IAsset {
    public string Path { get; }
    public int VertexCount { get; }
    public int IndexCount { get; }
    public BoundingBox Bounds { get; }
    public PrimitiveTopology Topology { get; }
    public bool IsDisposed { get; }

    public void Dispose();
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

> **M3 subset.** M3 ships `Mesh` and loads standalone mesh-part assets. `Model` and `ModelPart` are
> M4; M3 bakes model descriptors and their part mesh entries. `Mesh.Bounds` is a
> `System.Numerics.BoundingBox`.

- `Mesh` is one primitive: one vertex/index buffer pair and one topology. It is immutable after
  load. `Bounds` is in mesh space. The fixed vertex layout is listed in §7.4: position, normal,
  uv, uv1, uv2, uv3, color, and tangent.
- `Model` is a flat list of parts. It has no node hierarchy in v1; glTF node transforms are baked
  into each part's `LocalTransform`. `Bounds` is the union in model space. glTF 2.0 and OBJ are
  converted to this packed engine format by the bake; the runtime never parses them.
- `ModelPart.LocalTransform` is applied before the draw's instance transform:
  `world = part.LocalTransform.WithParent(instanceTransform)`.
- `ModelPart.Shader` is the baked shader for the part. The part also carries an internal baked
  shader-state snapshot that is not public. `DrawModel(model, transform)` uses that snapshot;
  mutating `part.Shader` with `SetUniform` does not change it. To override, pass a shader to
  `DrawModel`.
- `ModelPart.Bounds` is in model space after the part's local transform.
- `Model` is not an animation asset. Skeletal and morph animation are post-1.0.
- A model's parts may share meshes and shaders. The model holds references to all of them; dispose
  the model rather than individual parts unless those parts were loaded independently.
- Meshes keep their CPU positions for collision and bounds in v1. Textures do not keep CPU pixels.
