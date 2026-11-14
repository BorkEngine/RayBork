# RayBork

[![CI](https://github.com/BorkEngine/RayBork/actions/workflows/ci.yml/badge.svg)](https://github.com/BorkEngine/RayBork/actions/workflows/ci.yml)

RayBork is a C# 3D game framework built on Vulkan 1.2, SDL3, linear HDR rendering, and Native AOT.
It aims for the ergonomics of raylib with a modern bindless, instanced renderer: a window, input, a
frame-scoped draw API, assets from a baked pack, text, and enough 3D/2D primitives to ship a small
game without adopting a scene graph or an ECS.

**Status: M5 shipped.** The engine opens an SDL3 window, runs
`Setup`/`Tick`/`Shutdown` on a dedicated tick thread, and presents through a Vulkan 1.2 backend: a
linear HDR scene target, Godot 4.7's HDR-aware AgX tone mapping, scRGB HDR output when the window
reports HDR, MSAA with color resolve, reverse-Z depth, bounded recovery, and a Native AOT gate. M3
added the asset pipeline: the RAYBORKP v1 pack, canonical asset paths, a dedicated asset loader,
reference-counted `Mesh`/`Texture` assets, procedural `MeshBuilder`/`TextureBuilder` creation, custom
TOML data through compiled shapes, a non-blocking GPU upload bridge with frame retention and
device-recovery re-upload, the JIT `RayBork.Bake` tooling, and a build-time embedded built-in pack.
M4 adds the core 3D renderer: the frame-scoped camera and draw API (`DrawModel`, `DrawMesh`,
`DrawInstanced`, `DrawModelInstanced`), `Model` as a user-owned composition of parts, `Shader` as a
material instance over an internal program with `SetUniform`/`ResetUniforms`, a fixed 2048-slot
bindless texture table with sampler and pipeline caches, automatic batch planning with frustum
culling and opaque/transparency sorting, per-instance uniforms and textures, and the HDR AgX present
path.
M5 adds the raylib-style primitive surface and the collision queries: the 13 `Camera` helpers
(`DrawLine`, `DrawLines`, `DrawPoint`, `DrawTriangle`, `DrawCube`, `DrawSphere`, `DrawCylinder`,
`DrawPlane`, `DrawGrid`, `DrawWireCube`, `DrawWireSphere`, `DrawBillboard`, and the screen-space
`DrawTexture`), engine-owned primitive meshes drawn by the built-in `/shaders/primitive` program,
`Collision`/`RayCollision` with a deterministic ray-mesh BVH that is either generated procedurally or
baked into the pack, and the reference XZ grid in the castle scene. M5 also reworks the tone mapper
to Godot 4.7's HDR-aware AgX: the combined Rec.709 to Rec.2020 and AgX inset matrix, the `allenwp`
curve with CPU-derived parameters, a clamp to the output maximum, and the combined inverse outset and
Rec.2020 to Rec.709 matrix. SDR uses an output maximum of `1`; the scRGB path uses the reported
display headroom as the curve's output maximum and then scales by paper white, so the curve itself
reaches the reported peak. M5 additionally ships runtime tone mapping through `ctx.Tonemapping`
(`Exposure`, `AgxWhite`, `AgxContrast`, snapshotted into the sealed frame) and the
`Color.FromSrgb`/`Color.FromSrgbBytes`/`Color.ToSrgb` helpers. M5 restores the full Bork2d/Unity
color preset set converted from sRGB to linear, and the samples use those named presets instead of
hand-written color constants.
`samples/PrimitiveGallery` is the M5 gate: it draws every primitive helper in one perspective pass
with a runtime procedural texture, needs no asset pack, and publishes with Native AOT.
`samples/Castle` keeps its orbitable castle scene and now draws `DrawGrid` under the model.
The Windows hardware path, the conformant lavapipe Linux path, and the WSLg Mesa dzn hardware path
all pass. The host, window, input, time, and logging surface shipped in M1; the renderer foundation
shipped in M2. The rest of the API reference remains the 1.0 contract and is delivered by the
milestones listed in [API reference §1.6](docs/api.md#16-milestone-availability).

## Documents

| Document | Contents |
| --- | --- |
| [API reference](docs/api.md) | Overview and document map for public types, lifecycle, assets, rendering, shader ABI, input, threading, AOT, and tooling. |
| [Roadmap](docs/roadmap.md) | Milestones M0-M12, raylib parity matrix, cross-cutting gates, risks, post-1.0 work. |
| [Architecture](docs/architecture.md) | Threads, frame handoff, renderer seam, HDR/AgX, shader state, asset pipeline, invariants. |
| [Third-party notices](THIRD-PARTY-NOTICES.md) | Dependency inventory, licenses, and the runtime/test/tooling notice policy. |
| [AGENTS.md](AGENTS.md) | Agent workflow, code style, gates, PR rules, and review checklist. |

## Targets

- Windows x64 and Linux x64 (including WSL), Native AOT.
- Vulkan 1.2 with descriptor indexing and bindless textures; the renderer is replaceable behind an
  internal seam for future mobile/web backends.
- Linear HDR rendering with Godot 4.7's HDR-aware AgX tone mapping, MSAA (4x default), and reverse-Z
  depth.
- SDL3 for windowing, input, and (post-1.0) audio.
- `Microsoft.Extensions.Logging` with ZLogger; `EngineContext` exposes `ILoggerFactory`.
- One runtime assembly, `RayBork.Engine`; JIT-only build tooling in `RayBork.Bake`.
- xUnit v3 and Moq for tests; no golden-image suite.
- MIT license; see [LICENSE](LICENSE).

## Design snapshot

- Right-handed, Y-up, `-Z` forward; reverse-Z depth; user-facing angles are degrees.
- Dedicated tick thread; main thread pumps the window; a render thread draws the previous completed
  tick at full rate, or 30 FPS when minimized/unfocused.
- No scene graph and no ECS. The game owns its objects and transforms.
- No public `Material`. A `Shader` is a material instance with its own uniform cursor and texture
  bindings, backed by one internal cached GPU program. `Load<Shader>` returns a new independent
  material every call; every draw captures an immutable snapshot, so later cursor changes affect
  later draws only. Per-instance uniforms and textures let different materials over one program
  batch together. A `Camera` is a frame-scoped recorder: create it in `Tick`, record
  `DrawModel`/`DrawMesh`/`DrawInstanced`/`DrawModelInstanced` and the primitive, line, billboard, and
  texture helpers, and dispose it before `Tick` returns. Draws batch by
  `(mesh, program, effective render state)`.
- Rendering is linear HDR with MSAA and reverse-Z. The present pass applies Godot 4.7's HDR-aware
  AgX; `TonemappingMode.None` is the direct path. Tone mapping is frame-global: `ctx.Tonemapping`
  carries the runtime exposure, AgX white, and AgX contrast, and the tick thread snapshots it when
  the frame is sealed, so the render thread never reads the live object. `Color` is linear; use
  `Color.FromSrgb`/`Color.FromSrgbBytes`/`Color.ToSrgb` for display-encoded values.
- The primitive helpers draw engine-owned unit meshes through two built-in programs. Shapes and lines
  use `/shaders/primitive`, which reads vertex position and color and has no uniforms or textures;
  billboards and textured quads use `/shaders/mesh` with alpha render-state overrides. The eight
  primitive meshes (cube, sphere, plane, quad, triangle, line, wire cube, wire sphere) are built once
  before `IGame.Setup` through the procedural GPU bridge, are internal, and are not exposed as public
  assets. `EngineDefaults` still exposes only `CreateMeshShader`, `White`, and `Error`; a game can
  load a material over `/shaders/primitive` for its own vertex-color meshes.
- `Collision` and `RayCollision` are allocation-free, thread-safe queries: sphere/sphere, box/box,
  box/sphere, and ray/sphere, ray/box, ray/triangle, ray/plane, and ray/mesh. `GetRayMesh` traverses
  an immutable, deterministic AABB BVH when the mesh has one and tests the retained CPU triangles
  otherwise; both paths return the same nearest hit. `MeshBuilder.GenerateCollisions()` opts a
  procedural mesh in, and the bake writes an `RBVC` v1 collision payload for every triangle mesh by
  default (`[model] collisions = false` opts out).
- `Mesh` and `Texture` are reference-counted engine assets loaded from one or more `RAYBORKP` packs
  produced by the bake. Custom TOML data uses `[GenerateShape]`, `WithAssetType<T>()`, and the same
  `Load<T>` API; a custom format registers an `IAssetLoader<T>` with `WithAssetLoader<T>()` and
  decodes its own payload. `Shader` materials and `Model` objects are caller-owned, not engine
  assets.
- A `Model` is a user-owned flat list of parts. Each part has a mesh, a material, a render state,
  a local transform, and computed bounds; constructing a model acquires its meshes and transfers
  its part materials to the model. A part with no authored shader uses the built-in `/shaders/mesh`
  material program, which the engine preloads with the white/error textures and exposes through
  `EngineContext.Defaults`.
- Games can create procedural meshes and textures with `ctx.CreateMeshBuilder()` and
  `ctx.CreateTextureBuilder(...)`. Builders expose CPU vertices/indices, compute normals, tangents,
  and bounds, and `BuildAsync` returns an immutable asset only after the GPU upload finishes.
- Built-in assets are ordinary `IAssetProcessor` outputs; there is no special engine-only bake path.
  The engine's built-in pack is generated at engine build time by a JIT-only tool and compiled into
  the assembly. It contains the present shader pair, the mesh draw shader, the primitive shader, and
  the white/error textures.
- Shaders are GLSL compiled to SPIR-V ahead of runtime by the bake; the runtime never compiles
  shaders. The shader ABI generates the prologue and reflection for a frame uniform block, a fixed
  2048-element bindless texture array, and a per-instance storage buffer. `/shaders/primitive` has no
  reflected uniforms or textures, so its prologue omits the texture array and uses only the 96-byte
  instance header.
- Fonts are TTF/OTF parsed at runtime; MSDF is generated per glyph at 32 px and drawn bindlessly.
  M3 packages the face bytes; M6 adds the runtime parser.
- The renderer recovers from swapchain/device loss where possible; failed recovery is fatal. Live
  GPU assets are re-uploaded from their retained CPU descriptors after a device recovery.
- The engine keeps one 256-byte frame uniform block per frame slot, so multiple camera passes
  recorded in one tick share the last pass' `rbFrame` values. Single-camera frames are correct;
  per-pass frame data arrives with the multiple-camera work in M8.

## Repository layout

```text
src/RayBork/RayBork.Engine/   runtime engine (public API + internals)
  Shaders/                    present-pass GLSL sources and committed SPIR-V blobs
  BuiltInAssets/Assets/       built-in pack sources (mesh, primitive, and present shaders, textures)
src/RayBork/RayBork.Bake/     JIT asset tooling (pack writer, processors, TOML, CLI)
src/RayBork/RayBork.Tests/    unit, architecture, GPU, and AOT tests
build/RayBork.BuiltInAssets/  JIT build-time built-in pack generator
samples/PrimitiveGallery/     M5 sample and Native AOT gate (every primitive helper, no pack)
  PrimitiveGallery.slnx       sample-owned solution (single project)
samples/Castle/               castle scene sample (orbitable model, texture, and reference grid)
  Castle.slnx                 sample-owned solution (sample + assets executable)
  Castle.Assets/              JIT assets executable (bakes castle.pck at sample build time)
samples/HelloInterop/         M1-M3 diagnostic host (window/input/time/logging/assets; no draws)
  HelloInterop.slnx           sample-owned solution (host + assets executable)
  HelloInterop.Assets/        JIT assets executable (bakes hello.pck at sample build time)
samples/                      Pong, Text, Instancing, RenderTarget (M11)
docs/                         API reference (overview + section pages), roadmap, architecture
.github/                      GitHub Actions CI and PR template
scripts/                      docs checks and shader compilation
AGENTS.md                     agent workflow and code style
THIRD-PARTY-NOTICES.md        dependency licenses and runtime/test/tooling policy
LICENSE                       MIT
```

The samples are outside `src/RayBork/RayBork.slnx`: each sample owns its own solution, and a sample
that needs assets owns an assets executable too. The engine solution builds only the engine, bake,
and test projects.

Run the samples from the repository root with a Vulkan 1.2 device and a real window system (on WSL,
use the WSLg display). The Castle build bakes `castle.pck` beside the executable, and the sample loads
it automatically; `--assets` is only an optional override. The gallery has no assets executable and
no pack: it builds its only texture at runtime.

```powershell
dotnet build samples/Castle/Castle.slnx -c Debug
dotnet run --project samples/Castle/Castle.csproj -c Debug -- --frames 30
dotnet build samples/PrimitiveGallery/PrimitiveGallery.slnx -c Debug
dotnet run --project samples/PrimitiveGallery/PrimitiveGallery.csproj -c Debug -- --frames 30
```

Use `--help` for the available options. [samples/PrimitiveGallery/README.md](samples/PrimitiveGallery/README.md)
covers the gallery scene, its Native AOT publish and run commands, and its controls, and
[samples/Castle/README.md](samples/Castle/README.md) covers the castle scene's publish commands,
controls, and omitted features. `samples/HelloInterop` remains the M1-M3 diagnostic host for
window, input, time, logging, renderer, and asset-loading metadata; its build also bakes `hello.pck`
beside the host and mounts it automatically. Built-in shader SPIR-V is regenerated with
`pwsh scripts/compile-shaders.ps1` and the Vulkan SDK; a normal build and CI use the committed blobs.
The bake, the built-in pack generator, and the sample builds compile their GLSL with the pinned
native shaderc package; they do not need the Vulkan SDK.

## Next step

M6 adds world-space text: TTF/OTF face parsing, runtime MSDF generation, layout, and
`DrawText`/`DrawTextBillboard`. It builds on the M5 primitive draw path and the M4 camera; the shipped
surface is listed in [API reference §1.6](docs/api.md#16-milestone-availability).
