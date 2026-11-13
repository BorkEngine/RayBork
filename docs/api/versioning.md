# Versioning and appendices

[API overview](../api.md) · [document map](../api.md#document-map)

## 14. Versioning and compatibility

| Surface | Policy |
| --- | --- |
| Public C# API | 0.x is preview; 1.0 follows semantic versioning. |
| License | MIT; see [LICENSE](../../LICENSE). |
| Pack format | `RAYBORKP` v1 is the only accepted format in 1.x; an unknown magic or version is rejected with `RB1002`. The 40-byte header, sorted binary index, alignment, and XXH32/XXH3-64 hashes are part of the format. Pack bytes are not deterministic; the logical entry layout is. |
| Shader ABI | The generated prologue identifiers, binding layout, and entry points (`main_vertex`, `main_fragment`) are stable within a major version. |
| Custom data shapes | The `[GenerateShape]` contract and MessagePack wire shape are stable within a major version. |
| Diagnostic codes | Stable within a major version. |
| Native AOT | Supported RIDs are part of the release gate, not a best-effort extra. |
| Renderer backend | The public API is renderer-agnostic; the backend name and capability flags are reported through `EngineInfo`. |
| Public API analysis | `Microsoft.CodeAnalysis.PublicApiAnalyzers` tracks every public member; accidental changes fail CI. |

Before 1.0, breaking changes are allowed when they improve the API. Each breaking change gets a
migration note in the release notes and an update to this reference. After 1.0, obsolete members
are marked `[Obsolete]` for at least one minor release before removal.

A model descriptor requires a canonical shader path on every part. The pre-release alias for the
retired default model shader and the runtime null-path default are removed, so repack older model
packs; the bake writes `shaders/mesh` for a part with no authored `[model] shader`.

The ray-mesh collision payload is additive to pack v1. A mesh descriptor may carry the optional
`CollisionPayload` member naming a sibling `<mesh>-collision` raw entry in the `RBVC` v1 format;
both the member and the entry are optional, and the entry is an ordinary raw payload, so v1 remains
the only pack format. A pack baked without the member loads normally: its meshes have no collision
acceleration structure, and `Collision.GetRayMesh` brute-forces the retained CPU triangles.

The custom loader registration API is additive: `IAssetLoader<T>`, `AssetLoadContext`,
`AssetEntryType`, and `RayBorkAppBuilder.WithAssetLoader<T>` extend the asset surface without
changing `Load<T>`, and `WithAssetType<T>()` remains the MessagePack custom-data sugar.

The M5 collision surface is additive as well: `MeshBuilder.GenerateCollisions` adds the procedural
opt-in, and `Mesh.HasCollisionAcceleration` exposes whether a mesh carries the structure, without
changing `Mesh`, `MeshBuilder.BuildAsync`, or `Load<T>` semantics. A mesh without a structure keeps
working through the brute-force ray/mesh path.

The runtime tone-mapping surface is additive too: `TonemappingSettings` and
`EngineContext.Tonemapping` expose the exposure, AgX white, and AgX contrast for the app lifetime, and
`WithAgxWhite` and `WithAgxContrast` add their startup defaults. `WithExposure` and `WithTonemapping`
keep their meaning, so a game that configures tone mapping only at startup continues to work
unchanged.

The sRGB color conversion helpers are additive as well: `Color.FromSrgb`, `Color.FromSrgbBytes`, and
`Color.ToSrgb` convert between linear `Color` values and display-encoded components without changing
`Color` or its named constants. `Color` remains linear, `Color.Gray` is still linear `0.5`, and the
palette is unchanged.

The M5 AgX rework is a behavior change, not a new option. At the same exposure, SDR output differs
from the M4 curve, the separate HDR highlight path is gone, and the scRGB path uses
the reported display headroom as the AgX curve's output maximum. `TonemappingMode` and
`WithExposure` keep their names and meanings, so a game that configures tone mapping at startup
still builds and runs; re-check exposure and AgX white if it tuned against the M4 image. This is a
0.x preview change and this paragraph is its migration note; after 1.0 the same change would need the
obsolete/migration cycle above.

The pack format and the runtime are versioned independently. A 1.x runtime reads a 1.x pack. A
future pack v2 requires a runtime that advertises support; v1 rejects it rather than attempting a
best-effort read.

---

## 15. Out of scope for v1

The following are deliberate exclusions, not forgotten features. The roadmap places them after
1.0 unless noted.

| Area | v1 status | Notes |
| --- | --- | --- |
| Audio | Deferred | SDL3 audio, mixer, 2D/3D sources, streaming, AAC decode in engine, FFmpeg bake, HRTF and Doppler. |
| Skeletal/morph animation | Deferred | Models are static. |
| Built-in lighting/PBR/shadows | Deferred | Custom shaders can implement them. |
| Physics | Deferred | Collision queries only. |
| UI toolkit | Deferred | Text, sprites, and orthographic cameras are the building blocks. |
| Networking | Out | |
| VR/OpenXR | Deferred | Not in 1.0; later. |
| Custom vertex/instance layouts | Deferred | One fixed vertex layout in v1. |
| Compute/geometry/tessellation shaders | Deferred | Vertex+fragment only. |
| Hot reload | Deferred | Bake and relaunch first. |
| Editor | Out | RayBork is a framework. |
| Multiple windows | Deferred | One window per process. |
| macOS / ARM64 / mobile / web | Deferred | Windows x64 and Linux x64 first; the renderer seam exists for future mobile/web backends. |
| Scripting / mods | Out | Native AOT game assemblies only. |
| IME / touch / accessibility | Partial | Committed IME text in M9; touch and accessibility post-1.0. |
| Debug overlay / profiler UI | Deferred | `RenderStats` is the data source. |

---

## Appendix A: seed-to-API changes

| Seed | This reference | Why |
| --- | --- | --- |
| `Mesh? _model` | `Model? _model` | A castle is a multi-part model; `Mesh` is one primitive. |
| `Load<Mesh>("/models/castle")` | `Load<Model>("/models/castle")` | Multi-part glTF/OBJ support. |
| `Load<Mesh>("/textures/castle")` | `Load<Texture>("/textures/castle")` | The seed's type is a typo. |
| `_vertShader`, `_fragShader` | one `Shader` material over one internal program | The bake pairs the stages; a material owns the uniform cursor. |
| `_fragShader.SetUniform(...)` | `_model.Parts[i].Shader.SetUniform(...)`, or a `ctx.Defaults.CreateMeshShader()` material | No `Material`; a `Shader` is a material instance over an internal program. |
| `DrawModel(model, transform, vert, frag)` | `DrawModel(model, transform)` | Each model part supplies its own material and render state; there is no model-level shader override. |
| Camera at `z = -10` | `z = +10` | Right-handed, `-Z`-forward world. |
| `ClearMode.Color` | `ClearMode.ColorAndDepth` | A 3D pass must clear depth too. |
| `(float)(ctx.Time * 0.001)` | `(float)ctx.Time` | `Time` is seconds. |
| `Mesh?`/`Texture?` disposal | same `Dispose` calls, once per load | Shared, reference-counted engine assets. |
| `Shader?` disposal | dispose each `Load<Shader>` material and each `CreateMeshShader()` material once | Materials are caller-owned; they are not reference-counted. |
| `Model?` disposal | dispose the model once | `Model` owns its part materials and mesh references; it is not an engine asset. |
| `using var app = ...Build()` | unchanged | `RayBorkApp` is `IDisposable`. |
| `WithAssetsPack(packPath)` | unchanged | Relative paths resolve next to the executable. |
| `MaxDeltaTime = 0.25` | unchanged | Seconds. |
| `Quaternion.CreateFromAxisAngle` | `Quaternion.Euler` / `Quaternion.AngleAxis` | User-facing rotations use degrees. |
| `Color` from sRGB bytes | linear `Color`, with `Color.FromSrgbBytes` for encoded byte input | HDR rendering and AgX output; `Color` components are linear floats. |
| Custom data via ad-hoc files | `Load<T>` / `LoadData` | Compiled `[GenerateShape]` data, custom loaders, and raw bytes; register with `WithAssetType<T>()` or `WithAssetLoader<T>()`. |
| Logger injected as a custom interface | `ctx.LoggerFactory` | `Microsoft.Extensions.Logging` + ZLogger. |

The seed's `Shutdown` order remains correct: the model first, then the shader, then the texture.
The model holds references to its parts. The shader cursor holds an internal reference to the
texture set in `Setup`; disposing the shader first releases that cursor. The order is still the
clearest one, even though the cursor would keep the GPU resource alive if the texture were
disposed first.

---

## Appendix B: public type index

| Type | Section |
| --- | --- |
| `IGame` | §3.1 |
| `RayBorkAppBuilder` | §3.2 |
| `RayBorkApp` | §3.3 |
| `EngineContext` | §3.4 |
| `Transform` | §5.2 |
| `BoundingBox` | §5.5 |
| `Color` | §5.3 |
| `Rect` | §5.4 |
| `RayCollision`, `Collision` | §5.5 |
| `Vector3Extensions`, `QuaternionExtensions` | §5.6 |
| `IAsset`, `IAssetCatalog` | §6.1 |
| `IAssetLoader<T>`, `AssetLoadContext`, `AssetEntryType`, `WithAssetLoader<T>` | §6.1, §6.7 |
| `Mesh`, `Model`, `ModelPart`, `PrimitiveTopology`, `Vertex`, `MeshBuilder`, `MeshNormalMode` | §7.5 |
| `Texture`, `TextureBuilder`, `TextureFormat`, `TextureFilter`, `TextureWrap`, `SamplerState` | §7.6 |
| `Shader`, `ShaderStageMask`, `ShaderUniform`, `ShaderUniformType`, `ShaderTextureBinding` | §7.3 |
| `RenderState`, `BlendMode`, `CullMode`, `DepthTest` | §7.7 |
| `Camera`, `CameraProjection`, `ClearMode`, `TonemappingMode` | §7.1, §7.2 |
| `TonemappingSettings` | §3.4, §7.2 |
| `Shader`, `ShaderUniform`, `ShaderTextureBinding` | §7.3 |
| `Font`, `Glyph`, `TextAlignment`, `TextWrapping` | §7.10 |
| `RenderTexture`, `RenderTextureDescription` | §7.11 |
| `RenderStats` | §7.12 |
| `IWindow`, `WindowState`, `CursorMode`, `Monitor`, `PresentMode` | §8 |
| `IInput`, `Key`, `MouseButton`, `IGamepad`, `GamepadButton`, `GamepadAxis`, `GamepadTrigger` | §9 |
| `ILoggerFactory` | §10.1 |
| `EngineVersion` | §10.2 |
| `EngineInfo` | §10.3 |
| `RayBorkException` and derived exceptions | §10.4 |
| `BakeApplicationBuilder`, `BakeApplication`, `IAssetProcessor`, `AssetProcessor<TSidecar>`, `AssetProcessorContext`, `IAssetOutputSink`, `IAssetProcessorDiagnostics`, `IAssetValidator<T>`, `TextureBaker`, `BakedTexture`, `TextureChannelType`, `TextureCompression`, `SidecarPolicy` | §13 |
