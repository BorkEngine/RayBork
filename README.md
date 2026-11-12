# RayBork

[![CI](https://github.com/BorkEngine/RayBork/actions/workflows/ci.yml/badge.svg)](https://github.com/BorkEngine/RayBork/actions/workflows/ci.yml)

RayBork is a C# 3D game framework built on Vulkan 1.2, SDL3, linear HDR rendering, and Native AOT.
It aims for the ergonomics of raylib with a modern bindless, instanced renderer: a window, input, a
frame-scoped draw API, assets from a baked pack, text, and enough 3D/2D primitives to ship a small
game without adopting a scene graph or an ECS.

**Status: M4 core 3D renderer shipped.** The engine opens an SDL3 window, runs
`Setup`/`Tick`/`Shutdown` on a dedicated tick thread, and presents through a Vulkan 1.2 backend: a
linear HDR scene target, AgX tone mapping, scRGB HDR output when the window reports HDR, MSAA with
color resolve, reverse-Z depth, bounded recovery, and a Native AOT gate. M3 added the asset pipeline:
the RAYBORKP v1 pack, canonical asset paths, a dedicated asset loader, reference-counted
`Mesh`/`Texture`/`Shader` assets, procedural `MeshBuilder`/`TextureBuilder` creation, custom TOML
data through compiled shapes, a non-blocking GPU upload bridge with frame retention and
device-recovery re-upload, the JIT `RayBork.Bake` tooling, and a build-time embedded built-in pack.
M4 adds the core 3D renderer: the frame-scoped camera and draw API (`DrawModel`, `DrawMesh`,
`DrawInstanced`, `DrawModelInstanced`), `Model` loading with per-part local transforms and baked
shader state, `Shader.SetUniform`/`ResetUniforms` through the generated shader ABI, a bindless
texture table with sampler and pipeline caches, automatic batch planning with frustum culling and
opaque/transparency sorting, per-instance uniforms and textures, and the AgX highlight extension
that maps scene luminance above the AgX white point into the reported HDR headroom.
`samples/Castle` is the M4 gate: a recreation of raylib's `models_loading` example that draws the
raylib castle model, its diffuse texture, and the reference grid from a Native AOT publish on
Windows and Linux. The Windows hardware path and the conformant lavapipe Linux path both
pass; [samples/Castle/README.md](samples/Castle/README.md) records the current WSLg dzn driver
limitation for the hardware WSL run. The host, window, input, time, and logging surface shipped in
M1; the renderer foundation shipped in M2. The rest of the API reference remains the 1.0 contract
and is delivered by the milestones listed in
[API reference §1.6](docs/api.md#16-milestone-availability).

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
- Linear HDR rendering with AgX tone mapping, MSAA (4x default), and reverse-Z depth.
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
- No public `Material`. A `Shader` owns a uniform cursor; every draw captures an immutable snapshot;
  per-instance uniforms and bindless textures let different draws batch together. A `Camera` is a
  frame-scoped recorder: create it in `Tick`, record `DrawModel`/`DrawMesh`/`DrawInstanced`/
  `DrawModelInstanced` calls, and dispose it before `Tick` returns. Draws batch by
  `(mesh, shader, effective render state)`.
- Engine assets are reference-counted and loaded from one or more `RAYBORKP` packs produced by the
  bake. Custom TOML data uses `[GenerateShape]`, `WithAssetType<T>()`, and the same `Load<T>` API.
- A `Model` is a flat list of parts. Each part has a mesh, a baked shader, a local transform, and
  bounds; a part with no authored shader uses the built-in `/shaders/default` draw shader.
- Games can create procedural meshes and textures with `ctx.CreateMeshBuilder()` and
  `ctx.CreateTextureBuilder(...)`. Builders expose CPU vertices/indices, compute normals, tangents,
  and bounds, and `BuildAsync` returns an immutable asset only after the GPU upload finishes.
- Built-in assets are ordinary `IAssetProcessor` outputs; there is no special engine-only bake path.
  The engine's built-in pack is generated at engine build time by a JIT-only tool and compiled into
  the assembly. It contains the present shader pair, the default draw shader, and white/error
  textures.
- Shaders are GLSL compiled to SPIR-V ahead of runtime by the bake; the runtime never compiles
  shaders. The M4 shader ABI generates the prologue and reflection for a frame uniform block, a
  bindless texture array, and a per-instance storage buffer.
- Fonts are TTF/OTF parsed at runtime; MSDF is generated per glyph at 32 px and drawn bindlessly.
  M3 packages the face bytes; M6 adds the runtime parser.
- The renderer recovers from swapchain/device loss where possible; failed recovery is fatal. Live
  GPU assets are re-uploaded from their retained CPU descriptors after a device recovery.
- M4 keeps one 256-byte frame uniform block per frame slot, so multiple camera passes recorded in
  one tick share the last pass' `rbFrame` values. Single-camera frames are correct; per-pass frame
  data arrives with the multiple-camera work in M8.

## Repository layout

```text
src/RayBork/RayBork.Engine/   runtime engine (public API + internals)
  Shaders/                    present-pass GLSL sources and committed SPIR-V blobs
  BuiltInAssets/Assets/       built-in pack sources (default draw shader, present shader, textures)
src/RayBork/RayBork.Bake/     JIT asset tooling (pack writer, processors, TOML, CLI)
src/RayBork/RayBork.Tests/    unit, architecture, GPU, and AOT tests
build/RayBork.BuiltInAssets/  JIT build-time built-in pack generator
samples/Castle/               M4 sample and Native AOT gate (raylib models_loading recreation)
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

The samples are outside `src/RayBork/RayBork.slnx`: each sample owns its own solution and assets
executable, and the engine solution builds only the engine, bake, and test projects.

Run the Castle sample from the repository root with a Vulkan 1.2 device and a real window system (on
WSL, use the WSLg display). The sample build bakes `castle.pck` beside the executable, and the sample
loads it automatically; `--assets` is only an optional override:

```powershell
dotnet build samples/Castle/Castle.slnx -c Debug
dotnet run --project samples/Castle/Castle.csproj -c Debug -- --frames 30
```

Use `--help` for the available options. [samples/Castle/README.md](samples/Castle/README.md) covers
the Native AOT publish and run commands, the controls, the omitted raylib features, the sample-only
asset license, and the gate assertion. `samples/HelloInterop` remains the M1-M3 diagnostic host for
window, input, time, logging, renderer, and asset-loading metadata; its build also bakes `hello.pck`
beside the host and mounts it automatically. Built-in shader SPIR-V is regenerated with
`pwsh scripts/compile-shaders.ps1` and the Vulkan SDK; a normal build and CI use the committed blobs.
The bake, the built-in pack generator, and the sample builds compile their GLSL with the pinned
native shaderc package; they do not need the Vulkan SDK.

## Next step

M5 adds the raylib-style primitive, line, and billboard helpers plus the collision queries: cube,
sphere, cylinder, plane, grid, wire shapes, `DrawLine`/`DrawPoint`/`DrawTriangle`, and ray/sphere,
ray/box, ray/triangle, ray/plane, and ray/mesh collision. It builds on the M4 camera and draw API;
the shipped surface is listed in [API reference §1.6](docs/api.md#16-milestone-availability).
