# RayBork

[![CI](https://github.com/BorkEngine/RayBork/actions/workflows/ci.yml/badge.svg)](https://github.com/BorkEngine/RayBork/actions/workflows/ci.yml)

RayBork is a C# 3D game framework built on Vulkan 1.2, SDL3, linear HDR rendering, and Native AOT.
It aims for the ergonomics of raylib with a modern bindless, instanced renderer: a window, input, a
frame-scoped draw API, assets from a baked pack, text, and enough 3D/2D primitives to ship a small
game without adopting a scene graph or an ECS.

**Status: design phase.** There is no engine implementation yet. The repository contains the
project scaffold and the design documents below. The API reference is a contract sketch, not a
description of shipped behavior.

## Documents

| Document | Contents |
| --- | --- |
| [API reference](docs/api.md) | Public types, lifecycle, assets, rendering, shader ABI, input, threading, AOT, tooling. |
| [Roadmap](docs/roadmap.md) | Milestones M0-M12, raylib parity matrix, cross-cutting gates, risks, post-1.0 work. |
| [Architecture](docs/architecture.md) | Threads, frame handoff, renderer seam, HDR/AgX, shader state, asset pipeline, invariants. |
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
- Shaders are GLSL compiled to SPIR-V at bake time with `main_vertex`/`main_fragment`; the runtime
  never compiles shaders.
- Fonts are TTF/OTF parsed at runtime; MSDF is generated per glyph at 32 px and drawn bindlessly.
- The renderer recovers from swapchain/device loss where possible; failed recovery is fatal.

## Repository layout

```text
src/RayBork/RayBork.Engine/   runtime engine (public API + internals)
src/RayBork/RayBork.Bake/     JIT asset tooling
src/RayBork/RayBork.Tests/    unit, GPU, AOT, and architecture tests (to be added)
samples/                      Castle, Pong, Text, Instancing, RenderTarget (to be added)
docs/                         API, roadmap, architecture
.github/                      GitHub Actions CI and PR template
scripts/                      repository checks (docs links/fences)
AGENTS.md                     agent workflow and code style
LICENSE                       MIT
```

## Next step

The design set is complete; M0 can start with the build, test, AOT, CI, and public-API guardrails.
