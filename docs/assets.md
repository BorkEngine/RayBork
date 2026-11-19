# Assets

RayBork reads assets from baked packs, never from source files. Textures become KTX2, shaders become
SPIR-V, models become mesh descriptors, fonts keep their source face bytes, and typed data becomes
MessagePack. Baking runs at build time in `RayBork.Bake`, which is JIT tooling and never ships with a
game.

## Baking a project

An assets project is a normal .NET executable that references `RayBork.Bake`, registers the asset
types it uses, and runs the baker. Samples wire this into their build: the sample solution builds the
assets executable, runs it, and copies the resulting `.pck` beside the game.

```powershell
dotnet build samples/Pong/Pong.Assets/Pong.Assets.csproj -c Debug
dotnet run --project samples/Pong/Pong.csproj -c Debug -- --frames 30
```

The first command bakes `pong.pck` and copies it beside the game. The game finds it next to its own
executable. A sample that has no asset project is fully procedural and needs no pack.

The baker is also a direct CLI:

```text
bake    [--project-root DIR] [--output-root DIR] [--mode debug|release]
        [--check] [--force] [--warnings-as-errors] [--verbose|--quiet]
clean   [--project-root DIR] [--output-root DIR]
inspect [--pack FILE]
```

- `--project-root` defaults to the current directory.
- `--mode release` compresses pack payloads with Zstandard when that makes them smaller.
- `--check` validates and processes everything without writing.
- `--force` ignores the incremental cache.
- Exit codes: `0` success, `1` diagnostics, `2` usage or configuration error, `3` internal error.

## Sources and sidecars

Each pack has a root directory. The baker walks it in ordinal order and assigns each file to the
processor with the longest matching extension.

- A TOML file whose root table is `[raybork]` is typed data.
- A TOML file named `<source>.toml` next to a processor source is that source's sidecar.
- A TOML file whose single root table names a processor is a source in its own right; shaders use
  this form.
- A raw file with no processor is copied as-is.

A sidecar must contain exactly one root table named after its processor. It is optional for
textures, models, and fonts, required for shaders, and not used for raw files. The baker never writes
sidecars; a missing optional sidecar uses the defaults.

### Texture

Source: PNG, JPEG, TGA, or BMP. Sidecar: `<name>.toml` with a `[texture]` table.

```toml
[texture]
format = "bc7"        # rgba8, bc7, bc5, r8
srgb = true
filter = "linear"     # nearest, linear, anisotropic
wrap = "repeat"       # repeat, clamp, mirror
anisotropy = 8
```

Defaults are `rgba8`, sRGB enabled for RGBA8 and BC7, `linear`, `repeat`, and anisotropy `1`. BC5 and
R8 are single/dual-channel and cannot be sRGB. The baker decodes to straight-alpha RGBA8, generates a
full box-filtered mip chain, block-compresses every level, and writes KTX2. Sources must be at most
16384 pixels per side.

### Model

Source: OBJ, glTF, or GLB. Sidecar: `<name>.toml` with a `[model]` table.

```toml
[model]
scale = 1.0
front = "+z"          # +x, -x, +y, -y, +z, -z
shader = "shaders/mesh"
collisions = true
```

The defaults are scale `1`, front `+z`, the built-in mesh shader, and collision geometry enabled.
`front` is the authored forward direction; `+z` rotates 180 degrees around Y, so sources already
authored facing `-z` should set `front = "-z"`.

Every glTF primitive becomes a model part. OBJ polygons are fan-triangulated, missing normals become
flat normals, and UVs are flipped vertically. Each part gets a canonical slug path under the model
path, its own mesh descriptor, and an optional BVH collision payload. Skins, animations, cameras, and
lights are not supported.

### Shader

A shader source is a TOML file such as `mesh.shader.toml`. The `[shader]` table is both the source
and its configuration, so a shader never has a separate sidecar.

```toml
[shader]
vertex = "mesh.vert.glsl"
fragment = "mesh.frag.glsl"

[[shader.uniforms]]
name = "tint"
type = "vec4"
default = [1, 1, 1, 1]

[[shader.textures]]
name = "mainTexture"

[shader.render_state]
blend = "alpha"
cull = "back"
depth_test = "greater_equal"
depth_write = true
```

`vertex` and `fragment` are required relative paths that must stay inside the TOML's directory.
Entry points are fixed to `main_vertex` and `main_fragment`. Uniform types are `float`, `int`,
`uint`, `bool`, `vec2`, `vec3`, `vec4`, and `mat4`; at most 16 textures are allowed. The render
state defaults to opaque, back-face culling, greater-or-equal depth testing, and depth writes.

The baker prepends the engine shader prologue, compiles with shaderc for Vulkan 1.2, and validates
the resulting SPIR-V. Programs use set 0, binding 0 for the frame block, binding 1 for the bindless
`RayBorkTextures[2048]` array, and binding 2 for the per-instance record. `rbFrame` and `rbInstance`
expose those blocks, and the reserved `sourceColor` and `sourceDepth` names are used by post-process
shaders.

### Font

Source: a single TTF or OTF face. Sidecar: `<name>.toml` with a `[font]` table.

```toml
[font]
msdf_pixel_size = 32
msdf_range = 4
msdf_padding = 2
```

The defaults are `32`, `4`, and `2`; the pixel size must exceed twice the padding. The baker
validates the face and packs the original face bytes. MSDF glyph pages are generated at runtime, not
baked. TTC collections and CFF2 fonts are rejected.

### Raw

Any file with no processor is copied to its canonical path unchanged. It is read with
`IAssetCatalog.LoadData`.

### Typed data

A TOML file with a `[raybork]` root table is deserialized into a registered .NET type:

```toml
[raybork]
type = "Pong.PongSettings"

ball_speed = 420.0
max_score = 11
```

The type name is the CLR full name. Keys are snake_case and bind to the type's members. The same type
must be registered with `WithAssetType<T>()` on the app and with the baker's `RegisterAsset<T>()`;
both sides need a compiled `[GenerateShape]`. Validators can reject bad values during the bake.

## Pack format

A pack is a `RAYBORKP` version 1 file:

- a 40-byte little-endian header with the magic, version, flags, entry count, index offset and
  length, and an index CRC32C;
- a sorted index of entries;
- alignment-padded payloads.

Each entry stores a canonical path, an `AssetEntryType`, compression and alignment flags, offsets,
stored and original lengths, alignment, and a payload hash. Canonical paths are lowercase, use `/`
separators, and omit the extension. Path segments allow `a-z`, `0-9`, `_`, and `-`. Payloads may be
Zstandard-compressed in release mode, and are aligned to 1, 16, 64, or 256 bytes. Descriptors are
MessagePack; references between entries are plain canonical paths. Identical payloads are not
deduplicated: each entry stores its own copy.

At runtime the built-in pack mounts first, then each user pack in order. A later pack shadows an
earlier one for the same path. Packs are fixed when the app is built; there is no runtime mount or
unmount. Relative pack paths resolve against `AppContext.BaseDirectory`.

## Incremental builds

The baker stores state in `obj/raybork/bake/manifest.json`. It records the tool version, the
configuration hash, the bake mode, and for every source its input hashes and output descriptor.
Content is compared by SHA-256, not timestamps.

A cache hit requires the same tool version, configuration, mode, pack set, pack layout, and previous
pack file hash. If any source changed, only the changed sources are reprocessed. If everything
matches, the run reports `up to date` and writes nothing. A missing or corrupt pack, or a changed
pack, triggers a full rebake. `--force` ignores the cache; `--check` processes without writing.

## The built-in pack

The engine ships with a built-in pack containing the default mesh, text, and primitive shader
programs, the default font and mono font, the white and error textures, and the SSAO, bloom, and
present resources. Its sources live in `src/RayBork/RayBork.Engine/BuiltInAssets/Assets`.

The pack is baked when the engine builds and embedded in `RayBork.Engine.dll`; no pack file is
shipped or loaded at startup. Changing a built-in asset means changing its source and rebuilding the
engine.

## Rules of thumb

- Keep asset paths lowercase and extensionless in code: `Assets.Load<Texture>("/textures/paddle")`.
- Bake before publishing. When a sample bakes in a separate assets project, `dotnet publish` alone
  does not build that project.
- Rebake after upgrading RayBork: shader programs carry an ABI version and older packs are rejected.
- Do not ship `RayBork.Bake`, shaderc, or model/texture decoders with a game.
