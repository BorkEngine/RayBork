# Rendering: Render state

[API overview](../../api.md) · [document map](../../api.md#document-map)

### 7.7 `RenderState`

```csharp
public readonly record struct RenderState {
    public BlendMode Blend { get; init; }
    public CullMode Cull { get; init; }
    public DepthTest DepthTest { get; init; }
    public bool DepthWrite { get; init; }

    public static RenderState Opaque { get; }
    public static RenderState AlphaBlend { get; }
    public static RenderState Additive { get; }
    public static RenderState Multiply { get; }
}

public enum BlendMode {
    Opaque,
    Alpha,
    Additive,
    Multiply,
}

public enum CullMode {
    None,
    Front,
    Back,
}

public enum DepthTest {
    Disabled,
    Greater,
    GreaterOrEqual,
    Less,
    LessOrEqual,
    Always,
}
```

`RenderState.Opaque` is the default:

| Field | Opaque | AlphaBlend | Additive | Multiply |
| --- | --- | --- | --- | --- |
| `Blend` | `Opaque` | `Alpha` | `Additive` | `Multiply` |
| `Cull` | `Back` | `Back` | `Back` | `Back` |
| `DepthTest` | `GreaterOrEqual` | `GreaterOrEqual` | `GreaterOrEqual` | `GreaterOrEqual` |
| `DepthWrite` | `true` | `false` | `false` | `false` |

- `DepthTest` uses reverse-Z. `Greater` and `GreaterOrEqual` mean "closer wins". `Less` and
  `LessOrEqual` are escape hatches for custom depth tricks and are rarely correct with the
  engine's projection.
- `DepthTest.Disabled` skips the test; `DepthWrite` is ignored when the test is disabled.
- `BlendMode` has fixed presets in v1. Custom blend factors are post-1.0.
- Culling uses counter-clockwise front faces in the right-handed world. `CullMode.None` is for
  two-sided geometry and billboards.
- Topology is a property of `Mesh`, not `RenderState`. The batching key includes the mesh topology
  implicitly.
