# RayBork

[![CI](https://github.com/BorkEngine/RayBork/actions/workflows/ci.yml/badge.svg)](https://github.com/BorkEngine/RayBork/actions/workflows/ci.yml)

RayBork is a C# 3D game framework built on Vulkan 1.2, SDL3, linear HDR rendering, and Native AOT.
It aims for the ergonomics of raylib with a modern bindless, instanced renderer: a window, input, a
frame-scoped draw API, assets from a baked pack, text, and enough 3D/2D primitives to ship a small
game without adopting a scene graph or an ECS.

**Status: M2 renderer foundation shipped.** The engine opens an SDL3 window, runs
`Setup`/`Tick`/`Shutdown` on a dedicated tick thread, and presents through a Vulkan 1.2 backend: a
linear HDR scene target, AgX tone mapping, scRGB HDR output when the window reports HDR, MSAA with
color resolve, reverse-Z depth, bounded recovery, and a Native AOT gate. The host, window, input,
time, and logging surface shipped in M1. The rest of the API reference remains the 1.0 contract and
is delivered by the milestones listed in
[API reference §1.6](docs/api.md#16-milestone-availability).

## Documents

| Document | Contents |
| --- | --- |
| [API reference](docs/api.md) | Public types, lifecycle, assets, rendering, shader ABI, input, threading, AOT, tooling. |
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
  per-instance uniforms and bindless textures let different draws batch together.
- Engine assets are reference-counted and loaded from one or more `RAYBORKP` packs produced by the
  bake. Custom TOML data uses `[GenerateShape]`, `WithAssetType<T>()`, and the same `Load<T>` API.
- Built-in assets are ordinary `IAssetProcessor` outputs; there is no special engine-only bake path.
- Shaders are GLSL compiled to SPIR-V ahead of runtime; the runtime never compiles shaders. Game
  shaders will be baked into packs, and the built-in present pass uses committed blobs.
- Fonts are TTF/OTF parsed at runtime; MSDF is generated per glyph at 32 px and drawn bindlessly.
- The renderer recovers from swapchain/device loss where possible; failed recovery is fatal.

## Repository layout

```text
src/RayBork/RayBork.Engine/   runtime engine (public API + internals)
  Shaders/                    present-pass GLSL sources and committed SPIR-V blobs
src/RayBork/RayBork.Bake/     JIT asset tooling
src/RayBork/RayBork.Tests/    unit, architecture, GPU, and AOT tests
samples/HelloInterop/         M1 diagnostic host and M2 renderer/AOT gate host
samples/                      Castle, Pong, Text, Instancing, RenderTarget (M11)
docs/                         API, roadmap, architecture
.github/                      GitHub Actions CI and PR template
scripts/                      docs checks and shader compilation
AGENTS.md                     agent workflow and code style
THIRD-PARTY-NOTICES.md        dependency licenses and runtime/test/tooling policy
LICENSE                       MIT
```

Run the HelloInterop sample from the repository root with a Vulkan 1.2 device and a real window
system (on WSL, use the WSLg display):

```powershell
dotnet run --project samples/HelloInterop/HelloInterop.csproj -- --frames 30
```

Use `--help` for the frame limit, title, size, and log-level options. The sample README covers the
Native AOT publish and run commands. Built-in shader SPIR-V is regenerated with
`pwsh scripts/compile-shaders.ps1` and the Vulkan SDK; a normal build and CI use the committed
blobs.

## Next step

M3 begins the asset pipeline: the pack format, the assets executable, TOML data, and the asset
loader. The renderer foundation, host, threads, window, input, time, and logging surface are in
place.
