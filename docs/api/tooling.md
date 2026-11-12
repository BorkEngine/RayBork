# Asset tooling

[API overview](../api.md) · [document map](../api.md#document-map)

## 13. Asset tooling (`RayBork.Bake`)

`RayBork.Bake` is a JIT build library, not a runtime dependency. A game owns a small assets
executable that configures the bake and calls `RunAsync`. The bake discovers sources, runs
processors, binds TOML data, validates references, and writes one or more packs. The runtime only
sees the packed engine format. No runtime project references `RayBork.Bake`.

The bake uses JIT-only tooling: `Tomlyn` for TOML, `StbImageSharp` for PNG/JPEG/TGA/BMP decoding,
`BCnEncoder.Net` for BC1/BC4/BC5/BC7 encoding, `Silk.NET.Shaderc` plus `Silk.NET.Shaderc.Native`
for GLSL compilation, `SharpGLTF.Core` for glTF 2.0, `PolyType` and `Nerdbank.MessagePack` for
shapes and payloads, and `System.IO.Hashing` for pack hashes. These packages never enter a runtime
project or an AOT publish graph.

### 13.1 Assets executable

```csharp
using RayBork.Bake;

return await new BakeApplicationBuilder()
    .WithProjectRoot(".")
    .WithPack("assets.pck", "Assets")
    .UseDefaultAssets()
    .RegisterAsset<GameSettings>()
    .UseDefaultProcessors()
    .WithTextureDefaults(TextureFilter.Linear, TextureWrap.Clamp)
    .AddProcessor(new CircleProcessor())
    .Build()
    .RunAsync(args);
```

| Method | Contract |
| --- | --- |
| `WithProjectRoot` | Base for relative roots, pack paths, and diagnostics. |
| `WithPack(packPath, root)` | Adds one ordered pack. The first is the main pack; later packs are overlays that shadow earlier paths at runtime. |
| `UseDefaultAssets` | Registers the engine authoring descriptor types and their built-in validators. Idempotent. |
| `RegisterAsset<T>()` | Registers one TOML data type. `T` must carry a compiled `[GenerateShape]`. |
| `RegisterAsset<T>(validator)` | Registers the type if needed and appends an `IAssetValidator<T>`. |
| `UseDefaultProcessors` | Registers the built-in texture, model, shader, font, and raw processors. Audio processors are added in A1. |
| `WithTextureDefaults` | Sampler defaults for textures without a sidecar. |
| `AddProcessor` | Adds a custom `IAssetProcessor`; later registrations override earlier ones for the same extension. |
| `Build` | Validates configuration and returns the bake application. |
| `RunAsync(args)` | Runs `bake`, `clean`, `inspect`, `--help`, or `--version`; returns the process exit code. |

The `watch` command is deferred to M10. M3 ships `bake`, `clean`, `inspect`, `--help`, and
`--version`. `bake`, `clean`, and `inspect` accept `--project-root <dir>` and `--output-root <dir>`;
the project root defaults to the working directory and the output root defaults to
`AppContext.BaseDirectory`, so a build can invoke the assets executable from any working directory.

#### Build-time bake in a game or sample project

A game or sample can run its assets executable as part of its own build instead of a manual step.
`samples/Castle/Castle.csproj` and `samples/HelloInterop/HelloInterop.csproj` use the same
out-of-process pattern as the engine's built-in pack: an `AfterTargets="Build"` target invokes the
assets project with the `MSBuild` task (passing only `Configuration` and removing the sample's
runtime/publish properties), runs the built assets executable as
`<sample>.dll bake --project-root <assets dir> --output-root <intermediate pack dir>`, and copies
`<sample>.pck` beside the sample executable as a recorded build output. A separate target after
`Publish` copies the same pack into the publish directory and fails the publish with a clear error if
it is missing. The pack lives under the sample's intermediate output, so `dotnet clean` removes it
and the next build bakes again.

Because the assets executable is only invoked out of process, the sample project never references
`RayBork.Bake`, and the JIT tooling never enters the sample's Native AOT publish graph. The explicit
`--project-root` and `--output-root` arguments keep the bake independent of the working directory.
The samples are separate solutions (`samples/Castle/Castle.slnx`,
`samples/HelloInterop/HelloInterop.slnx`) and are not part of `src/RayBork/RayBork.slnx`.

M3's public bake surface is `BakeApplicationBuilder`, `BakeApplication`, `IAssetProcessor`,
`AssetProcessor<TSidecar>`, `AssetProcessorContext`, `IAssetOutputSink`,
`IAssetProcessorDiagnostics`, `IAssetValidator<T>`, `SidecarPolicy`, `TextureBaker`,
`BakedTexture`, `TextureChannelType`, and `TextureCompression`. `TextureBaker.Bake` is the one
KTX2 producer: a custom processor that generates pixels calls it instead of writing KTX2 headers or
block data itself. `TextureBaker.AddTo(context, pixels, width, height, ...)` emits the texture
descriptor and payload directly through the processor's output sink for a custom processor that
wants the built-in texture asset shape without touching internal descriptor types.

Built-in assets are not special. The engine's built-in pack is produced by the same processors
through `UseDefaultProcessors`; a game can override a built-in extension with `AddProcessor`.

### 13.2 TOML data assets

A registered data type is selected by a root `[raybork]` table:

```toml
# Assets/Data/level.toml
[raybork]
type = "Castle.GameSettings"

name = "The Castle"
difficulty = 2
```

```csharp
using PolyType;

[GenerateShape]
public sealed partial class GameSettings {
    public string Name { get; init; } = "";
    public int Difficulty { get; init; }
}
```

The bake binds constructor parameters and properties by name, serializes the value as MessagePack,
and emits a typed data entry at `data/level`. The runtime reads it with
`ctx.Assets.Load<GameSettings>("/data/level")`. Member keys are snake_case by default
(`PaddleSpeed` is `paddle_speed`); `[JsonPropertyName]` overrides one member. Enums bind by their
member name, case-insensitively, with snake_case accepted (`greater_or_equal`). Unknown keys,
missing required members, and type mismatches are errors.

A data TOML file is not a sidecar. A sidecar configures one processor source and lives at
`<asset base>.toml` with a table named after the processor (`[texture]`, `[model]`, `[font]`). A
`[shader]` TOML is a shader source, described below.

### 13.3 Processors

```csharp
public interface IAssetProcessor {
    string Name { get; }
    IReadOnlyList<string> Extensions { get; }
    SidecarPolicy Policy { get; }
    ITypeShape? SidecarShape { get; }
    void Process(AssetProcessorContext context);
}
```

- `Name` is stable and participates in diagnostics and incremental hashing. It is also the sidecar
  table name.
- `Extensions` lists the source extensions the processor owns; both `"png"` and `".PNG"` are
  normalized. A compound extension such as `shader.toml` is matched as one extension when registered.
- `Policy` is `None`, `Optional`, or `Required`; `SidecarShape` is the compiled shape for a typed
  sidecar. A missing `Optional` sidecar uses `CreateDefaultSidecar`; a missing `Required` sidecar is
  an error. The bake never creates or rewrites a source sidecar.
- `Process` runs on the bake thread. It must not retain per-source state between calls.
- Outputs go through `AssetProcessorContext.Outputs`: `AddAsset<T>` for shaped data and `AddData`
  for raw payloads. `AddData` takes an alignment of `1`, `16`, `64`, or `256`; GPU payloads use
  `16` or greater. A processor never writes MessagePack or pack bytes directly.
- `AssetProcessorContext` exposes the source paths, the canonical base asset path, the output sink,
  the diagnostic sink, and `AddInput(path)`. `AddInput` records a referenced source file, such as a
  shader's GLSL stages, as a tracked input for the incremental manifest; the path is resolved
  relative to the source file and must stay inside the pack root. It is valid for one source only.
- `AssetProcessor<TSidecar>` is the convenience base for a processor with a required sidecar.
- A processor error discards every output for the source and fails the pack. Warnings do not fail
  the pack unless `--warnings-as-errors` is set.

Built-in processors:

| Processor | Sources | Outputs |
| --- | --- | --- |
| Texture | PNG, JPEG, TGA, BMP | `Texture` descriptor plus its KTX2 payload |
| Model | glTF 2.0 (`.gltf`, `.glb`), OBJ | `Model` descriptor, one `Mesh` descriptor per part, vertex/index payloads |
| Shader | GLSL vertex + fragment pair named by a `[shader]` TOML | `Shader` descriptor plus vertex and fragment SPIR-V payloads |
| Font | TTF, OTF | `Font` descriptor plus the face payload |
| Raw | Any unclaimed source | Opaque bytes for `LoadData` |
| Audio (A1, post-1.0) | WAV, OGG, MP3, FLAC, AIFF | AAC-LC payload via FFmpeg; not registered by `UseDefaultProcessors` in 1.0 |

A texture source `Assets/Textures/castle.png` with an optional sidecar
`Assets/Textures/castle.toml`:

```toml
[texture]
srgb = true
format = "bc7"
filter = "anisotropic"
wrap = "repeat"
anisotropy = 8
```

- `format` is `rgba8`, `bc7`, `bc5`, or `r8`; the default is `rgba8`. `bc5` and `r8` are linear data
  formats and reject `srgb = true`. `srgb` defaults to `true` for `rgba8`/`bc7`.
- `filter` is `nearest`, `linear`, or `anisotropic`; the builder default is `linear`.
- `wrap` is `repeat`, `clamp`, or `mirror`; the builder default is `repeat`.
- `anisotropy` defaults to `1` and is clamped to the device limit at load time.
- The bake generates the full mip chain with a deterministic box filter, encodes every level, and
  writes one complete KTX2 2.0 payload. The descriptor and payload agree on format, mip count, and
  sRGB transfer.
- A texture that decodes to an unsupported dimension, a 16-bit PNG, or a non-finite payload value is
  a `RB4007` error and emits no outputs.

A model source `Assets/Models/castle.glb` with an optional sidecar
`Assets/Models/castle.toml`:

```toml
[model]
scale = 1.0
front = "+z"
shader = "shaders/castle"
```

- glTF 2.0 static meshes and OBJ static meshes are supported. Skins, morph targets, animations,
  cameras, and lights are rejected or ignored with a diagnostic; a source with no mesh is an error.
- The model is emitted at the base path. Each primitive becomes a `Mesh` part at
  `<base>/<part>` with its own vertex/index payload. The `Model` descriptor records the flat part
  list, local transforms, bounds, and the shared sidecar shader path. A part with no `shader` in
  the sidecar is baked with the built-in canonical path `shaders/mesh`, so every part always names
  a shader program.
- `scale` and `front` apply the authoring correction before bounds and payloads are written.
- glTF materials and images are not imported in M4. The bake validates that referenced images exist
  and warns that they are not imported; every part uses the sidecar shader (or the built-in
  `shaders/mesh`), and the game supplies textures and uniforms with `SetUniform`. The model loader
  maps an older pack's `shaders/default` part path to the built-in `/shaders/mesh` program, so packs
  baked before the mesh shader replaced the old default still load.
- UVs use the engine's top-left origin. glTF `TEXCOORD_0`-`TEXCOORD_3` are used as authored. Every
  OBJ `vt` is flipped on V at bake (`v = 1 - v`), so an authored V of 0 maps to the bottom of the
  texture and an authored V of 1 maps to the top, matching raylib's OBJ loader; U is unchanged.
  Negative UV-index resolution is unchanged, and a face corner with no `vt` keeps the zero UV
  default.

A font source `Assets/Fonts/inter.ttf` with an optional sidecar `Assets/Fonts/inter.toml`:

```toml
[font]
ranges = ["U+0020-007E", "U+00A0-00FF"]
msdf_pixel_size = 32
msdf_range = 4
msdf_padding = 2
```

The font processor validates the sfnt container and table directory, stores the settings in the
`Font` descriptor, and emits the original face bytes. The runtime parser and MSDF generation are M6.

A shader is authored as one TOML source at the asset base path:

```toml
# Assets/Shaders/castle.toml
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

- `vertex` and `fragment` are paths relative to the TOML file and must stay inside the pack root.
  They are tracked inputs, not assets. The TOML source itself is not a data asset.
- The entry points default to `main_vertex` and `main_fragment`; a sidecar may restate those exact
  values, but it cannot rename them in v1. The GLSL defines the user functions `main_vertex` and
  `main_fragment`; the bake compiles a generated wrapper `main` and renames the compiled
  `OpEntryPoint` back to the fixed name.
- The bake inserts the generated shader prologue after the first `#version` directive line. When a
  stage has no `#version` directive, the bake prepends `#version 450` before the prologue, so the
  version line always stays first.
- The prologue declares the frame block, the fixed-size 2048-element bindless texture array, the
  per-instance record and storage buffer, the `rbFrame`/`rbInstance` accessors, one macro per
  reflected uniform and texture, and the stage wrapper. User GLSL must not declare the generated
  identifiers (`main`, `RayBorkFrame`, `RayBorkFrameBlock`, `rbFrameBlock`, `RayBorkTextures`,
  `RayBorkInstance`, `RayBorkInstanceBuffer`, `RayBorkInstances`, `rbFrame`, `rbInstance`,
  `rbInstanceIndex`, `rbInstanceIndexOut`, `rbUserVertexMain`, `rbUserFragmentMain`) or any
  uniform/texture name from its sidecar. It must not declare a `layout(set = ...)` binding or use
  varying location 15, which is reserved for the engine instance index. Declarations are detected on
  the comment-stripped source, and a violation is `RB4008` with the source line and no outputs.
- Supported uniform types are `float`, `int`, `uint`, `bool`, `vec2`, `vec3`, `vec4`, `mat4`, and
  `sampler2D`. `default` is a scalar for `float`/`int`/`uint`/`bool`, and an array whose length
  matches the component count for the vector and matrix types. Arrays, structs, and extra uniform
  blocks are errors. An instance record above 4 KiB produces a warning and one above 16 KiB is an
  error.
- `blend` is `opaque`, `alpha`, `additive`, or `multiply`; `cull` is `none`, `front`, or `back`;
  `depth_test` is `disabled`, `greater`, `greater_equal`, `less`, `less_or_equal`, or `always`.
  `depth_write` is a bool.
- The full shader ABI, generated accessors, instance layout, and reserved names are specified in
  [Rendering: Shaders](rendering/shaders.md#74-shader-abi). The descriptor shape is unchanged: the
  bake derives the layout from sidecar declaration order and records the uniform and texture arrays
  in the `Shader` descriptor.

The built-in processors use `RB4007`-`RB4010`; the shared TOML and discovery failures use
`RB4002`/`RB4003`. Unknown keys are errors, not silently ignored.

### 13.4 Pack v1 (as built)

M4 did not change the pack format: pack v1 is unchanged since M3. The pack is little-endian and
starts with a fixed 40-byte header:

| Field | Size | Notes |
| --- | --- | --- |
| Magic | 8 bytes | ASCII `RAYBORKP` |
| Version | `u32` | `1` |
| Flags | `u32` | Reserved; zero |
| Entry count | `u32` | At most 1,000,000 |
| Index offset | `u64` | Absolute file offset, 40 |
| Index length | `u32` | Bytes, at most 64 MiB |
| Index hash | `u32` | XXH32 of the index bytes, seed `0` |
| Reserved | `u32` | Zero |

The index is a sorted array of entries (ordinal canonical-path order). Every field is
little-endian:

| Field | Size | Notes |
| --- | --- | --- |
| Path length | `u16` | UTF-8 byte length, at most 4096 |
| Path | bytes | Canonical asset path without a leading slash |
| Asset type | `u16` | `Raw=0`, `Mesh=1`, `Model=2`, `Texture=3`, `Shader=4`, `Font=5`, `Data=6` |
| Flags | `u16` | Bit 0 compressed, bit 1 aligned; other bits zero |
| Payload offset | `u64` | Absolute file offset |
| Stored length | `u32` | Bytes as stored |
| Uncompressed size | `u32` | Bytes after decompression |
| Alignment | `u32` | `1`, `16`, `64`, or `256` |
| Payload hash | `u64` | XXH3-64 of the stored bytes, seed `0` |

Payloads start after the index. Each payload begins at an offset that is a multiple of its
alignment; the writer inserts zero padding. The reader verifies the alignment, bounds, lengths,
index hash, and payload hash before exposing an entry. A path is rejected if it is not canonical,
if entries are not strictly ordinal-sorted, or if two entries normalize to the same path.

A payload is Zstandard-compressed when its entry flags say so. Debug packs store payloads
uncompressed for fast iteration; release packs compress when the compressed bytes are strictly
smaller. The decompressor is bounded to the declared uncompressed size, and a stream that produces
more or fewer bytes is corruption.

Engine asset entries are MessagePack descriptors that reference sibling raw payload entries
(`<base>-ktx2`, `<base>-mesh`, `<base>-vert`, `<base>-frag`, `<base>-face`). A `Data` entry holds the
MessagePack bytes of one registered custom type; `Load<T>` deserializes it with the compiled shape
and returns a fresh instance.

Pack bytes are **not deterministic**. The entry set and canonical paths are ordered, but the
compressor version, encoder choices, and build can change the bytes. Incremental caching uses
input/output hashes, not byte-for-byte reproducibility.

The pack is not encrypted or signed in v1. Integrity is a hash, not authenticity. A corrupt
header, index, or payload is a hard failure with a diagnostic code; the runtime never guesses. An
unknown version is `RB1002`.

### 13.5 Incremental builds

The bake writes a manifest at `<project root>/obj/raybork/bake/manifest.json`. It records:

- the bake tool version and configuration hash (mode, pack paths, roots, texture defaults,
  processors, data type registrations, and the tool markers for the texture, shader, model, and
  font pipelines);
- every tracked input path and content hash, including sidecars, `AddInput` dependencies, and shader
  GLSL files;
- every output entry path, entry hash, and payload hash;
- the pack logical-layout hash.

A rebuild reuses the previous pack's descriptor and payload bytes for a source whose input hashes,
processor identity, tool version, and configuration are unchanged. It reruns the processor for
changed and new sources, drops deleted sources, and composes a new pack from the reused and fresh
entries. A tool-version or configuration change invalidates the cache and reprocesses every source.
`--force` bypasses the cache. A missing, corrupt, or incompatible previous pack is a full bake.

The manifest is not part of the runtime pack. It never promises byte-identical pack output; it only
avoids redundant processing. Tests assert the processor invocation count for an unchanged source,
not pack byte identity. The samples bake into their own intermediate output at build time and copy
only the pack; the manifest stays under the assets project's `obj/raybork/bake`, so a missing pack
(for example after `dotnet clean`) forces a full bake on the next build.

### 13.6 Validation and diagnostics

The bake fails the build on:

- duplicate or case-normalized canonical paths;
- missing referenced assets or a missing required sidecar;
- unsupported source formats or an unclaimed file with no raw fallback;
- shader prologue validation, compile, entry-point, or sidecar metadata errors;
- texture dimensions, formats, or block-encoding failures;
- model sources with no mesh or invalid accessors;
- font containers that are not valid sfnt files;
- TOML parse, bind, unknown-key, or shape failures;
- a semantic validator error;
- pack size or alignment overflow.

Diagnostics carry a code, severity, source path, and line/column where available. The CLI renders
`path(line,col): error RBxxxx: message` on `stderr` and a summary on `stdout`. The process exits
`0` on success or up to date, `1` on source diagnostics, `2` on usage or configuration errors, and
`3` on an unexpected internal failure. Warnings do not fail the build unless `--warnings-as-errors`
is set.

### 13.7 Built-in pack

`RayBork.Engine` embeds a small built-in pack produced by the same processors:

- a white 1x1 texture and a magenta/black error texture, generated by a tool-only processor that
  calls `TextureBaker.Bake`;
- the present vertex/fragment shader pair, compiled from the engine's committed GLSL sources by a
  tool-only processor through the shared shaderc compiler;
- the built-in mesh draw shader program at `/shaders/mesh`, baked by the ordinary shader processor
  through the generated prologue with one `uTint` uniform, one `mainTexture` binding, and an
  opaque/back/`greater_equal`/depth-write render state. It is the unlit raylib default model shader:
  position, uv, and color only, `texture(mainTexture, vUv) * vColor * uTint * rbInstance.Color`, with
  no normals or lighting.

Primitive meshes and the error glyph are added by later milestones (M5 and M6). Pack v1 is unchanged
by M4: the mesh shader is an ordinary shader entry at a reserved built-in path, not a format change.
The app preloads it into `EngineContext.Defaults` before `IGame.Setup`; there is no longer a built-in
`/shaders/default` entry.

The pack is generated at engine build time by `build/RayBork.BuiltInAssets`, a JIT-only generator
that references `RayBork.Bake`. `build/RayBork.BuiltInAssets.targets` is imported by
`RayBork.Engine.csproj`: before `CoreCompile` it restores and builds the generator out of process,
runs the ordinary `bake` command over `src/RayBork/RayBork.Engine/BuiltInAssets/Assets` into a
unique temporary directory, and emits a generated C# byte array under the intermediate output. The
generated file is compiled into the assembly, never committed, and never published as a loose pack.
The generator and its packages are not referenced by the runtime project and do not enter the AOT
publish graph.

The built-in pack is mounted first, before every game pack. A game pack shadows a built-in entry at
the same canonical path.
