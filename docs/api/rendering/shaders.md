# Rendering: Shaders

[API overview](../../api.md) · [document map](../../api.md#document-map)

### 7.3 `Shader` materials and `SetUniform`

```csharp
public sealed class Shader : IDisposable {
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

public sealed class EngineDefaults {
    public Shader CreateMeshShader();
    public Texture White { get; }
    public Texture Error { get; }
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

> **As built.** A `Shader` is a **material instance**, not a loaded program asset. The GPU program is
> an internal engine asset (there is no public shader-program type). `Load<Shader>(path)` returns a new
> independent material on every call, all backed by one cached program for that path; materials are not
> reference-counted engine assets. `Stages`, `Uniforms`, and `Textures` come from the bake sidecar
> metadata, not from runtime SPIR-V reflection. `SetUniform` and `ResetUniforms` write the material's
> own cursor; each draw snapshots it into the frame packet. The cursor writes the per-instance shader
> ABI record described in [§7.4](#74-shader-abi), so `int` and `uint` values round-trip exactly and
> texture bindings become bindless indices at draw time.

A `Shader` is one material over an internal vertex+fragment program. It owns a **mutable uniform
cursor**:

1. `SetUniform` writes the current value for the named uniform on this material.
2. Each `Draw*` call snapshots the cursor into the frame packet.
3. Later `SetUniform` calls affect later draws only.
4. The renderer packs the snapshot into the per-instance parameter block and resolves textures
   to bindless indices.

The snapshot copies the cursor's raw instance data and texture references synchronously on the tick
thread. The recorded frame retains the internal program and every referenced texture, so later cursor
changes, disposing the material, and a later public texture `Dispose` cannot corrupt an already
recorded draw.

This is the central ergonomic rule:

```csharp
shader.SetUniform("mainTexture", textureA);
camera.DrawMesh(mesh, transformA, shader);

shader.SetUniform("mainTexture", textureB);
camera.DrawMesh(mesh, transformB, shader);
```

Both draws use the same mesh, material, and render state, so they become one instanced draw with two
instances and two bindless textures. No separate `Material` object is involved.

Materials over the same internal program also batch together. Creating one material per draw site is
the normal way to vary state without perturbing another system:

```csharp
Shader red = await ctx.Assets.Load<Shader>("/shaders/castle");
Shader blue = await ctx.Assets.Load<Shader>("/shaders/castle");
red.SetUniform("mainTexture", redTexture);
blue.SetUniform("mainTexture", blueTexture);
camera.DrawMesh(mesh, transformA, red);
camera.DrawMesh(mesh, transformB, blue);
```

The two materials share one GPU program and one pipeline, and the batcher merges them into one
instanced draw. Batching is keyed by the internal program, mesh, effective render state, and topology;
uniform bytes and texture bindings are per-instance data.

Rules and errors:

- `name` must match a reflected uniform name from the bake. Unknown names throw `ArgumentException`.
- The value type must match the reflected type. A mismatch throws `ArgumentException`. `Color`
  maps to `vec4`; `Quaternion` maps to `vec4`; `bool` maps to `uint`.
- A `Texture` value must not be disposed at the moment of the call. `null` binds the built-in white
  texture; this is useful for optional texture uniforms and for shaders that branch on a flag.
- Setting a texture records an engine reference in the material cursor. Disposing the public texture
  afterwards does not invalidate the cursor; overwrite the uniform, call `ResetUniforms`, or
  dispose the material to release the cursor's reference. Passing a texture that was already disposed
  to `SetUniform` throws `ObjectDisposedException`.
- The material's default values come from the program's bake sidecar. `ResetUniforms` restores those
  defaults and clears every texture binding to the built-in white fallback; it releases every texture
  reference the cursor acquired. It does not affect any other material over the same program.
- Uniform values are stored as raw little-endian 4-byte components in the per-instance record.
  `float`, `int`, `uint`, and `bool` therefore round-trip exactly, and `Matrix4x4` is stored in
  `M11` through `M44` component order.
- `SetUniform` and `ResetUniforms` are tick-thread only. They are legal in `Setup`, `Tick`, and
  `Shutdown`. Calling them from the render thread, main thread, or a user background thread throws
  `InvalidOperationException`. Cursor mutation is safe against a concurrent `Dispose` from another
  thread.
- The cursor is per material. State is not shared by other materials over the same program; two
  systems that need different values use two materials (or set the state immediately before their own
  draws on the same tick thread). The snapshot rule makes both patterns correct even when the render
  thread runs in parallel.
- `DrawInstanced` uses one snapshot for all instances in the call. To vary a texture or uniform
  per instance, record separate `DrawMesh`/`DrawModel` calls with the automatic batcher.
- `Shader` uniforms are not global frame uniforms. Time, camera matrices, viewport, and similar
  values are supplied by the engine's frame block and are not settable through `SetUniform`.
- `SetUniform` on a disposed material throws `ObjectDisposedException`.
- `Dispose` releases this material's internal program reference and its texture references exactly
  once. The program and textures stay alive while another material or a recorded frame holds them;
  disposing one material never invalidates a sibling over the same program. Dispose each material
  returned by `Load<Shader>` or `EngineDefaults.CreateMeshShader` exactly once.
- A recorded frame keeps the internal program and its textures alive. Disposing the material
  immediately after recording a draw is safe.

`Uniforms` and `Textures` expose bake reflection metadata for diagnostics and tooling. They are
read-only and do not reflect the current values. There is no public `GetUniform` in v1; if it is
added, it will read the cursor, not the GPU.

**Engine defaults.** The app preloads the built-in mesh shader program at `/shaders/mesh` and the
white and error textures after the asset catalog starts and before `IGame.Setup`. `EngineDefaults`
exposes them through `EngineContext.Defaults`:

- `CreateMeshShader()` returns a new caller-owned material over the preloaded program. Dispose every
  material it returns exactly once.
- `White` is the opaque 1x1 white sRGB texture (bindless slot 0) and `Error` is the 4x4 magenta/black
  checkerboard with nearest filtering. Both are engine-owned; do not dispose them.
- Startup fails with an `AssetLoadException` when a built-in default is missing or corrupt.
- The `EngineDefaults` owner references are released during shutdown, after the game callbacks
  return. After that, every `EngineDefaults` member throws `ObjectDisposedException`.

The app also preloads the built-in `/shaders/primitive` program for the engine's primitive meshes and
the `Camera` primitive helpers, but that program is not exposed through `EngineDefaults`; the type
still offers only `CreateMeshShader`, `White`, and `Error`. `/shaders/primitive` is an ordinary
built-in shader entry, so `Load<Shader>("/shaders/primitive")` returns a caller-owned material over
it for a game's own vertex-color meshes. It reflects no uniforms and no textures, so every
`SetUniform` name is rejected and its material cursor stays empty.

### 7.4 Shader ABI

The bake generates a prologue for every shader so user GLSL does not declare the engine plumbing.
The exact generated identifiers are part of the v1 compatibility contract and are stabilized before
1.0. The frame block offsets are shared with the renderer's frame writer and the instance offsets
with the draw recorder, so the tables below are normative.

**Descriptor set 0**

| Binding | Type | Contents |
| --- | --- | --- |
| 0 | uniform buffer, std140, 256 bytes | `RayBorkFrame`: view-projection, view, projection, camera position, viewport, time/delta/frame, screen size/exposure |
| 1 | bindless combined image samplers | `RayBorkTextures[2048]`, a fixed-size array of 2048 slots |
| 2 | storage buffer, std430 | `RayBorkInstances[]`, one record per instance, plus reflected user uniforms |

The texture binding is a fixed-size descriptor array of 2048 combined image samplers, not a runtime
descriptor array: the prologue emits `RayBorkTextures[2048]` for a program that reflects at least
one texture binding, and the descriptor set layout declares exactly 2048 descriptors. The renderer
requires three Vulkan 1.2 descriptor-indexing
features: `descriptorBindingPartiallyBound`, `descriptorBindingSampledImageUpdateAfterBind`, and
`shaderSampledImageArrayNonUniformIndexing`. A physical device that lacks any of them, or whose
`maxPerStageDescriptorUpdateAfterBindSampledImages` or
`maxDescriptorSetUpdateAfterBindSampledImages` limit is below 2048, is rejected during device
selection with a diagnostic naming the missing feature or limit. The capacity is never reduced and
the layout is never created with a count different from the shader array.

The frame block is declared as `layout(set = 0, binding = 0, std140) uniform RayBorkFrameBlock
{ RayBorkFrame frame; } rbFrameBlock;` and `rbFrame` expands to `rbFrameBlock.frame`.

| Offset | Member | Contents |
| --- | --- | --- |
| 0 | `mat4 ViewProjection` | view-projection matrix |
| 64 | `mat4 View` | view matrix |
| 128 | `mat4 Projection` | projection matrix |
| 192 | `vec4 CameraPosition` | camera position xyz, `w = 1` |
| 208 | `vec4 Viewport` | xy = viewport offset in pixels, zw = viewport size in pixels |
| 224 | `vec4 Time` | x = seconds, y = delta seconds, z = frame index, w = 0 |
| 240 | `vec4 ScreenSize` | xy = target size in pixels, z = exposure multiplier, w = 0 |

`CameraPosition`, `Time`, `Viewport`, and `ScreenSize` are written by the engine frame path. They are
not shader uniforms and cannot be changed with `SetUniform`.

**Present pass push constants**

The present pass is a separate built-in program, not a user shader: its descriptor set 0 has one
combined image sampler for the resolved scene color, and it receives its configuration through a
40-byte push-constant block:

| Offset | Type | Member | Contents |
| --- | --- | --- | --- |
| 0 | `float` | `exposureMultiplier` | `2^Exposure` from the frame's tone-mapping snapshot, computed on the CPU |
| 4 | `float` | `hdrWhiteScale` | Paper white in scRGB units: the window's SDR white level on HDR, `1` on SDR |
| 8 | `float` | `outputMaxValue` | `1` on SDR, the reported display headroom (peak / paper white) on scRGB HDR |
| 12 | `float` | `awpContrast` | allenwp curve contrast |
| 16 | `float` | `awpToeA` | allenwp toe intersection parameter |
| 20 | `float` | `awpSlope` | allenwp shoulder slope at the `0.18` crossover |
| 24 | `float` | `awpW` | allenwp shoulder width that places the AgX white at `outputMaxValue` |
| 28 | `int` | `tonemapping` | `0` = AgX, `1` = none |
| 32 | `int` | `encoding` | `0` = linear clamp, `1` = manual sRGB encode, `2` = scRGB HDR |
| 36 | `int` | `reserved` | `0`; keeps the block 40 bytes |

The block is exactly 40 bytes: seven `float` members at offsets 0 through 24, then three 32-bit
integers at offsets 28, 32, and 36. The push-constant range is fragment-stage only. `tonemapping`
selects `AgX` or the direct `None` path; there is no separate HDR highlight stage flag and no AgX
white-point or log-range constant. The `awp*` fields are the allenwp curve parameters derived on the
CPU by `Internal/Graphics/AgxCurveParameters.cs`; the shader (`Shaders/Present.frag.glsl`) and the CPU
reference (`Internal/Graphics/AgxToneMap.cs`) evaluate the same combined-matrix and curve pipeline and
are covered by pinned unit tests. The values come from the frame packet's tone-mapping snapshot, never
from the live `EngineContext.Tonemapping` object. The scene frame block's `ScreenSize.z` exposure
field is written from the same snapshot, so the scene shader's frame data and the present pass agree
on exposure for the frame.

**Instance record layout**

`RayBorkInstance` is the std430 record in the set 0 binding 2 storage buffer. The record starts with a
fixed 96-byte header, then one `uint` texture index slot per sidecar texture binding in declaration
order, then the reflected user uniforms in declaration order:

| Offset | Member |
| --- | --- |
| 0 | `mat4 Transform` (64 bytes) |
| 64 | `vec4 Color` (16 bytes) |
| 80 | `vec4 UvRect` (16 bytes) |
| 96 | `uint <name>Index` for each texture binding in order (4 bytes each) |
| after the texture slots | reflected uniforms in declaration order |

Uniform members follow GLSL std430:

| Declared type | Alignment | Size |
| --- | --- | --- |
| `float`, `int`, `uint`, `bool` | 4 | 4 |
| `vec2` | 8 | 8 |
| `vec3` | 16 | 12 |
| `vec4` | 16 | 16 |
| `mat4` | 16 | 64 |

A uniform's offset is the next aligned position after the previous member. A `vec3` occupies 12
bytes, so a following scalar can pack at offset + 12 even though the `vec3` itself is 16-byte
aligned. The complete record is rounded up to a multiple of 16 bytes. The bake estimates the
reflected record size and warns above 4 KiB per instance; the runtime may split a batch if the
device's storage-buffer range or descriptor limits require it, and correctness does not depend on
the batch not splitting. A record above 16 KiB per instance is rejected by the bake with `RB4008`;
no outputs are emitted.

**Prologue insertion and `#version`**

The prologue is inserted immediately after the first `#version` directive line. A source without a
`#version` directive gets `#version 450` prepended, followed by the prologue. The `#version` line
stays first. The prologue starts with `#extension GL_EXT_nonuniform_qualifier : require`, then the
frame struct and block, the fixed 2048-element bindless texture array when the shader has at least
one texture, the instance struct and storage buffer, the stage-specific instance varying, and the
macros below.

The bake compiles the generated wrapper function named `main` with shaderc, then renames the compiled
`OpEntryPoint` to `main_vertex` or `main_fragment` and validates the module. The runtime never parses
GLSL or SPIR-V.

**Entry points**

User GLSL defines the fixed authoring entry points `main_vertex` and `main_fragment`. The prologue
forward-declares the user function and defines a wrapper `main()` that assigns the instance index in
the vertex stage and calls the user function. A `#define main_vertex rbUserVertexMain` (or
`main_fragment`/`rbUserFragmentMain`) maps the user definition onto that forward declaration. A
shader sidecar may restate the fixed entry-point names for clarity, but it cannot rename them in v1.

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

Only the attributes a shader declares are read. The geometry pipeline declares the full fixed
layout as vertex attribute descriptions, so a custom shader may omit normals, UV sets, color, or
tangents and the unconsumed attributes are ignored; the Vulkan validation layer reports that case
as a performance warning, not an error, because the layout itself remains fixed. The built-in
`/shaders/mesh` program declares only position (location 0), uv (location 2), and color (location 6);
it has no normal input and no lighting. The built-in `/shaders/primitive` program declares only
position (location 0) and color (location 6), and its fragment stage never reads `rbInstance`, so the
compiled fragment interface does not consume the vertex stage's
`layout(location = 15) flat out uint rbInstanceIndexOut`. The validation layer reports that case as a
performance warning too; it is not an error. Custom vertex layouts are post-1.0.

**Generated accessors**

A program with no reflected texture bindings has no bindless texture array in its prologue and needs
no texture index slot, so only the frame block and the fixed 96-byte instance header apply to it.
`/shaders/primitive` is such a program: it has no uniforms and no textures, reads vertex position
and color, and the engine records only the transform, the tint, and the UV rectangle for it. Its
vertex stage multiplies the vertex color by `rbInstance.Color` and its fragment stage writes that
color. `EngineDefaults` does not expose it; the engine loads it as an ordinary built-in shader entry.

The prologue defines the frame block and a stage-specific `rbInstance` expression:

- `rbFrame` expands to `rbFrameBlock.frame`.
- In the vertex stage, `rbInstance` expands to
  `RayBorkInstances.instances[rbInstanceIndexOut]`, where the wrapper assigns
  `rbInstanceIndexOut = uint(gl_InstanceIndex)`.
- In the fragment stage, the prologue emits `layout(location = 15) flat in uint rbInstanceIndex;`
  and `rbInstance` expands to `RayBorkInstances.instances[rbInstanceIndex]`. The vertex stage
  declares the matching `layout(location = 15) flat out uint rbInstanceIndexOut;`. Location 15 is
  reserved for the engine instance index.
- The prologue enables `GL_EXT_nonuniform_qualifier` for the bindless texture lookups.
- Each non-texture uniform name is mapped to the corresponding field of `rbInstance`. For example,
  a reflected `vec4 uTint` becomes a field and `uTint` expands to `rbInstance.uTint`.
- Each texture binding name is mapped to a bindless lookup using the instance's texture index. For
  example, `mainTexture` expands to `RayBorkTextures[nonuniformEXT(rbInstance.mainTextureIndex)]`.
  A `null` texture set with `SetUniform` resolves to the built-in white texture.

**Reserved identifiers and validation**

The user GLSL must not declare `main`, `RayBorkFrame`, `RayBorkFrameBlock`, `rbFrameBlock`,
`RayBorkTextures`, `RayBorkInstance`, `RayBorkInstanceBuffer`, `RayBorkInstances`, `rbFrame`,
`rbInstance`, `rbInstanceIndex`, `rbInstanceIndexOut`, `rbUserVertexMain`, or `rbUserFragmentMain`.
It must not declare any uniform or texture name that the sidecar reflects, and it must not declare a
`layout(set = ...)` binding or a `layout(location = 15)` output/input. `main_vertex` and
`main_fragment` are the user functions and are not reserved. The bake scans the comment-stripped
source with identifier boundaries, reports the offending source line, and fails the stage with
`RB4008` without emitting any output.

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

Supported uniform types are `float`, `int`, `uint`, `bool`, `vec2`, `vec3`, `vec4`, `mat4`, and
`sampler2D`. Arrays, structs, and uniform blocks beyond the generated block are post-1.0. A shader
that uses an unsupported type fails the bake with a diagnostic pointing at the source line.

A shader that fails prologue validation, compilation, entry-point renaming, or SPIR-V validation is a
bake error, not a runtime error.
