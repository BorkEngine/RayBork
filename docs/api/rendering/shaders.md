# Rendering: Shaders

[API overview](../../api.md) · [document map](../../api.md#document-map)

### 7.3 `Shader` and `SetUniform`

```csharp
public sealed class Shader : IAsset {
    public string Path { get; }
    public ShaderStageMask Stages { get; }
    public RenderState DefaultRenderState { get; }
    public IReadOnlyList<ShaderUniform> Uniforms { get; }
    public IReadOnlyList<ShaderTextureBinding> Textures { get; }
    public bool IsDisposed { get; }

    public void SetUniform(string name, float value);
    public void SetUniform(string name, int value);
    public void SetUniform(string name, uint value);
    public void SetUniform(string name, bool value);
    public void SetUniform(string name, Vector2 value);
    public void SetUniform(string name, Vector3 value);
    public void SetUniform(string name, Vector4 value);
    public void SetUniform(string name, Quaternion value);
    public void SetUniform(string name, Matrix4x4 value);
    public void SetUniform(string name, Color value);
    public void SetUniform(string name, Texture? texture);

    public void ResetUniforms();
    public void Dispose();
}

[Flags]
public enum ShaderStageMask {
    None = 0,
    Vertex = 1 << 0,
    Fragment = 1 << 1,
}

public enum ShaderUniformType {
    Float,
    Int,
    UInt,
    Bool,
    Vector2,
    Vector3,
    Vector4,
    Matrix4x4,
    Texture2D,
}

public readonly record struct ShaderUniform(string Name, ShaderUniformType Type);

public readonly record struct ShaderTextureBinding(string Name, int Index);
```

> **M3 subset.** M3 loads and exposes the metadata members only: `Path`, `Stages`,
> `DefaultRenderState`, `Uniforms`, `Textures`, `IsDisposed`, and `Dispose`. `SetUniform` and
> `ResetUniforms` are M4. `Uniforms` and `Textures` come from the bake sidecar metadata, not from
> runtime SPIR-V reflection.

A `Shader` is a complete vertex+fragment program. It owns a **mutable uniform cursor**:

1. `SetUniform` writes the current value for the named uniform.
2. Each `Draw*` call snapshots the cursor into the frame packet.
3. Later `SetUniform` calls affect later draws only.
4. The renderer packs the snapshot into the per-instance parameter block and resolves textures
   to bindless indices.

This is the central ergonomic rule:

```csharp
shader.SetUniform("mainTexture", textureA);
camera.DrawMesh(mesh, transformA, shader);

shader.SetUniform("mainTexture", textureB);
camera.DrawMesh(mesh, transformB, shader);
```

Both draws use the same mesh, shader, and render state, so they become one instanced draw with two
instances and two bindless textures. No `Material` object is involved.

Rules and errors:

- `name` must match a reflected uniform name from the bake. Unknown names throw `ArgumentException`.
- The value type must match the reflected type. A mismatch throws `ArgumentException`. `Color`
  maps to `vec4`; `Quaternion` maps to `vec4`; `bool` maps to `uint`.
- A `Texture` value must not be disposed at the moment of the call. `null` binds the built-in white
  texture; this is useful for optional texture uniforms and for shaders that branch on a flag.
- Setting a texture records an engine reference in the shader cursor. Disposing the public texture
  afterwards does not invalidate the cursor; overwrite the uniform, call `ResetUniforms`, or
  dispose the shader to release the cursor's reference. Passing a texture that was already disposed
  to `SetUniform` throws `ObjectDisposedException`.
- The shader's default values come from the bake sidecar. `ResetUniforms` restores those defaults.
- `SetUniform` is tick-thread only. It is legal in `Setup`, `Tick`, and `Shutdown`. Calling it from
  the render thread, main thread, or a user background thread throws `InvalidOperationException`.
- Shader state is shared by all draws that use the shader. There is no per-camera or per-thread
  state. Interleaved systems must set the state immediately before the draws that need it; the
  snapshot rule makes this correct even when the render thread runs in parallel.
- `DrawInstanced` uses one snapshot for all instances in the call. To vary a texture or uniform
  per instance, record separate `DrawMesh`/`DrawModel` calls with the automatic batcher.
- `Shader` uniforms are not global frame uniforms. Time, camera matrices, viewport, and similar
  values are supplied by the engine's frame block and are not settable through `SetUniform`.
- `SetUniform` on a disposed shader throws `ObjectDisposedException`.
- `Dispose` follows the asset reference-count rules. A recorded frame keeps the shader and its
  textures alive.

`Uniforms` and `Textures` expose bake reflection metadata for diagnostics and tooling. They are
read-only and do not reflect the current values. There is no public `GetUniform` in v1; if it is
added, it will read the cursor, not the GPU.

### 7.4 Shader ABI

The bake generates a prologue for every shader so user GLSL does not declare the engine plumbing.
The exact generated identifiers are part of the v1 compatibility contract and are stabilized
before 1.0. The current target is:

**Descriptor set 0**

| Binding | Type | Contents |
| --- | --- | --- |
| 0 | uniform buffer | `RayBorkFrame`: view-projection, view, projection, camera position, viewport, time/delta/frame, screen size, exposure |
| 1 | bindless combined image samplers | `RayBorkTextures[]`, capacity queried from the device (4096 target) |
| 2 | storage buffer | `RayBorkInstances[]`, one record per instance, plus reflected user uniforms |

**Fixed mesh vertex layout**

| Location | Type | Semantic |
| --- | --- | --- |
| 0 | `vec3` | position |
| 1 | `vec3` | normal |
| 2 | `vec2` | uv |
| 3 | `vec2` | uv1 |
| 4 | `vec2` | uv2 |
| 5 | `vec2` | uv3 |
| 6 | `vec4` | color (linear) |
| 7 | `vec4` | tangent |

Only the attributes a shader declares are bound. A custom shader may omit normals, UV sets, color,
or tangents, but the mesh layout remains fixed. Custom vertex layouts are post-1.0.

**Entry points**

Vertex and fragment stages use the fixed entry points `main_vertex` and `main_fragment`. The bake
compiles both stages with those entry points and validates that they exist. A shader sidecar may
restate them for clarity, but it cannot rename them in v1.

**Generated accessors**

The prologue defines the frame block and a stage-specific `rbInstance` expression:

- `rbFrame` expands to `RayBorkFrame`.
- In the vertex stage, `rbInstance` expands to `RayBorkInstances[gl_InstanceIndex]`.
- In the fragment stage, the prologue emits a `flat` instance-index varying and `rbInstance`
  expands to `RayBorkInstances[rbInstanceIndex]`.
- The prologue enables `GL_EXT_nonuniform_qualifier` for the bindless texture lookups.
- Each non-texture uniform name is mapped to the corresponding field of `rbInstance`. For example,
  a reflected `vec4 uTint` becomes a field and `uTint` expands to `rbInstance.uTint`.
- Each texture uniform name is mapped to a bindless lookup using the instance's texture index. For
  example, `mainTexture` expands to `RayBorkTextures[nonuniformEXT(rbInstance.mainTextureIndex)]`.

Do not declare `RayBorkFrame`, `RayBorkTextures`, `RayBorkInstances`, `rbInstance`, the generated
varyings, or any uniform/texture that the sidecar reflects. The bake rejects redeclarations with a
diagnostic instead of emitting GLSL that fails later.

The sidecar is the source of truth for user uniforms and textures in v1. The bake generates the
prologue fields and macros from this list, so the GLSL uses the names without declaring them. If a
name is declared in both the sidecar and the GLSL, the bake reports a redeclaration diagnostic.
Declaring reflected uniforms in GLSL instead is a post-1.0 authoring option.

```toml
# /shaders/castle.toml
[shader]
vertex = "castle.vert"
fragment = "castle.frag"
vertex_entry = "main_vertex"
fragment_entry = "main_fragment"

[[shader.uniforms]]
name = "uTint"
type = "vec4"
default = [1.0, 1.0, 1.0, 1.0]

[[shader.textures]]
name = "mainTexture"

[shader.render_state]
blend = "opaque"
cull = "back"
depth_test = "greater_equal"
depth_write = true
```

A complete custom pair:

```glsl
// castle.vert
#version 450

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aUv;

layout(location = 0) out vec2 vUv;
layout(location = 1) out vec3 vNormal;

void main_vertex() {
    vec4 world = rbInstance.Transform * vec4(aPosition, 1.0);
    gl_Position = rbFrame.ViewProjection * world;
    vUv = aUv;
    vNormal = normalize(mat3(rbInstance.Transform) * aNormal);
}
```

```glsl
// castle.frag
#version 450

layout(location = 0) in vec2 vUv;
layout(location = 1) in vec3 vNormal;

layout(location = 0) out vec4 oColor;

void main_fragment() {
    vec4 albedo = texture(mainTexture, vUv);
    float light = max(dot(normalize(vNormal), normalize(vec3(0.4, 0.8, 0.2))), 0.0);
    oColor = vec4(albedo.rgb * (0.25 + 0.75 * light), albedo.a) * uTint;
}
```

The runtime never parses GLSL or SPIR-V. The bake emits SPIR-V, reflection metadata, the generated
prologue, and validation results. Loading a shader that fails validation is a bake error, not a
runtime error.

Supported uniform types are `float`, `int`, `uint`, `bool`, `vec2`, `vec3`, `vec4`, `mat4`, and
`sampler2D`. Arrays, structs, and uniform blocks beyond the generated block are post-1.0. A shader
that uses an unsupported type fails the bake with a diagnostic pointing at the source line.

Per-instance parameters have a practical size limit. The bake estimates the reflected record size
and warns above 4 KiB per instance; the runtime may split a batch if the device's storage-buffer
range or descriptor limits require it. Correctness does not depend on the batch not splitting.
