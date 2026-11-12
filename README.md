# RayBork

[![CI](https://github.com/BorkEngine/RayBork/actions/workflows/ci.yml/badge.svg)](https://github.com/BorkEngine/RayBork/actions/workflows/ci.yml)

RayBork is a C# 3D game framework built on Vulkan 1.2, SDL3, linear HDR rendering, and Native AOT.
It aims for the ergonomics of raylib with a modern bindless, instanced renderer: a window, input, a
frame-scoped draw API, assets from a baked pack, text, and enough 3D/2D primitives to ship a small
game without adopting a scene graph or an ECS.

**Status: M3 asset pipeline shipped.** The engine opens an SDL3 window, runs
`Setup`/`Tick`/`Shutdown` on a dedicated tick thread, and presents through a Vulkan 1.2 backend: a
linear HDR scene target, AgX tone mapping, scRGB HDR output when the window reports HDR, MSAA with
color resolve, reverse-Z depth, bounded recovery, and a Native AOT gate. M3 adds the asset pipeline:
the RAYBORKP v1 pack, canonical asset paths, a dedicated asset loader, reference-counted
`Mesh`/`Texture`/`Shader` assets, procedural `MeshBuilder`/`TextureBuilder` creation, custom TOML
data through compiled shapes, a non-blocking GPU upload bridge with frame retention and
device-recovery re-upload, the JIT `RayBork.Bake` tooling, and a build-time embedded built-in pack.
The host, window, input, time, and logging surface shipped in M1; the renderer foundation shipped in
M2. The rest of the API reference remains the 1.0 contract and is delivered by the milestones listed
in [API reference §1.6](docs/api.md#16-milestone-availability).

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
  per-instance uniforms and bindless textures let different draws batch together. (The M3 runtime
  loads shader metadata; the cursor and draw API land in M4.)
- Engine assets are reference-counted and loaded from one or more `RAYBORKP` packs produced by the
  bake. Custom TOML data uses `[GenerateShape]`, `WithAssetType<T>()`, and the same `Load<T>` API.
- Games can create procedural meshes and textures with `ctx.CreateMeshBuilder()` and
  `ctx.CreateTextureBuilder(...)`. Builders expose CPU vertices/indices, compute normals, tangents,
  and bounds, and `BuildAsync` returns an immutable asset only after the GPU upload finishes.
- Built-in assets are ordinary `IAssetProcessor` outputs; there is no special engine-only bake path.
  The engine's built-in pack is generated at engine build time by a JIT-only tool and compiled into
  the assembly.
- Shaders are GLSL compiled to SPIR-V ahead of runtime by the bake; the runtime never compiles
  shaders. The M3 bake emits SPIR-V and sidecar metadata; the M4 shader ABI adds the generated
  prologue and full reflection.
- Fonts are TTF/OTF parsed at runtime; MSDF is generated per glyph at 32 px and drawn bindlessly.
  M3 packages the face bytes; M6 adds the runtime parser.
- The renderer recovers from swapchain/device loss where possible; failed recovery is fatal. Live
  GPU assets are re-uploaded from their retained CPU descriptors after a device recovery.

## Repository layout

```text
src/RayBork/RayBork.Engine/   runtime engine (public API + internals)
  Shaders/                    present-pass GLSL sources and committed SPIR-V blobs
  BuiltInAssets/Assets/       built-in pack sources (textures and present shader marker)
src/RayBork/RayBork.Bake/     JIT asset tooling (pack writer, processors, TOML, CLI)
src/RayBork/RayBork.Tests/    unit, architecture, GPU, and AOT tests
build/RayBork.BuiltInAssets/  JIT build-time built-in pack generator
samples/HelloInterop/         M1 diagnostic host, M2 renderer/AOT gate, M3 assets gate host
samples/HelloInterop.Assets/  M3 assets executable (bakes hello.pck)
samples/                      Castle, Pong, Text, Instancing, RenderTarget (M11)
docs/                         API reference (overview + section pages), roadmap, architecture
.github/                      GitHub Actions CI and PR template
scripts/                      docs checks and shader compilation
AGENTS.md                     agent workflow and code style
THIRD-PARTY-NOTICES.md        dependency licenses and runtime/test/tooling policy
LICENSE                       MIT
```

Run the HelloInterop sample from the repository root with a Vulkan 1.2 device and a real window
system (on WSL, use the WSLg display). It runs without a pack by default; the M3 gate bakes one and
loads it:

```powershell
dotnet run --project samples/HelloInterop.Assets/HelloInterop.Assets.csproj -c Release -- bake
dotnet run --project samples/HelloInterop/HelloInterop.csproj -c Release -- --assets samples/HelloInterop.Assets/bin/Release/net11.0/hello.pck --frames 30
```

Use `--help` on either host for the available options. The sample README covers the Native AOT
publish and run commands. Built-in shader SPIR-V is regenerated with
`pwsh scripts/compile-shaders.ps1` and the Vulkan SDK; a normal build and CI use the committed
blobs. The bake and built-in pack generator compile their GLSL with the pinned native shaderc
package; they do not need the Vulkan SDK.

## Next step

M4 builds the core 3D renderer on top of the M2 renderer foundation and this asset pipeline:
`Camera`, `Model` loading, shader-state snapshots, bindless textures, instancing, batching, and the
AgX HDR highlight extension.
