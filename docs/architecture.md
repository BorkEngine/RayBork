# Architecture

RayBork has three moving parts: a game running on a tick thread, a renderer running on its own render
thread, and build-time tools that bake assets before the game starts. The runtime never compiles
shaders, scans assemblies, or reads source assets.

## Projects

| Project | Role |
| --- | --- |
| `src/RayBork/RayBork.Engine` | The runtime engine. Targets Native AOT and ships no tooling. |
| `src/RayBork/RayBork.Bake` | The JIT asset baker used at build time. Never shipped with a game. |
| `src/RayBork/RayBork.Tests` | Unit, contract, and GPU tests. |
| `build/RayBork.BuiltInAssets` | A build-only generator that bakes the engine's built-in pack and embeds it in the engine assembly. |
| `samples/*` | Standalone sample solutions, each with its own README. |

The engine targets `net11.0` and is marked `IsAotCompatible`. It has no reflection-based asset
discovery, no runtime code generation, and no dependency on the bake tool. `RayBork.Bake` may use
JIT-only libraries because it runs on the developer machine, not in the game.

## Threads

| Thread | Owns |
| --- | --- |
| `RayBork.Main` | SDL initialization, window events, the window surface, and the app lifecycle. |
| `RayBork.Tick` | `IGame.Setup`, `Tick`, and `Shutdown`; input snapshots, cameras, shader uniforms, and game state. |
| `RayBork.Render` | The Vulkan instance/device/swapchain, GPU uploads, render-texture allocation, frame submission, and present. |
| `RayBork.Asset` | Asset pack reads, deserialization, and the synchronous part of custom asset loaders. |
| `RayBork.Font` | MSDF glyph generation for the built-in font pipeline. |

Cameras, shader cursors, and effect settings are tick-thread affine and throw from other threads.
Window setters and exit requests are queued and safe from any thread. The asset catalog is
thread-safe.

The tick thread and render thread communicate through a fixed frame ring. The tick thread records a
frame packet, seals it, and publishes it; the render thread always takes the newest ready packet and
releases the slot after present. The render slot currently being drawn is never written by the tick
thread, so the two threads do not lock each other.

## Frame flow

1. The tick thread captures the clock and the input snapshot.
2. `IGame.Tick` records zero or more camera passes. A camera records value snapshots of meshes,
   shaders, render state, tints, and transforms, plus retention references to GPU assets.
3. When the tick ends, the frame is sealed with the current tone-mapping settings and published to
   the frame ring.
4. The render thread drains pending GPU uploads and render-texture requests, then records the camera
   passes, the post-process chain, and the present pass.
5. The frame is submitted and presented. Render statistics are published, and the frame slot is
   released.

Present pacing depends on the present mode. `Fifo` is vsync-paced; `Mailbox` and `Immediate` let the
tick loop run independently, optionally capped by `WithTargetFps`.

## Startup and shutdown

`new RayBorkAppBuilder()` only stores configuration. `Build()` validates it and creates the app
without touching SDL or Vulkan. `RayBorkApp.Run()` then:

1. Creates the logger factory.
2. Initializes SDL, the window, monitors, and HDR state.
3. Starts the render thread and creates the Vulkan device and swapchain.
4. Mounts the built-in pack, then each user pack in order, starts the asset loader and glyph service,
   and preloads engine defaults, primitives, SSAO, and bloom.
5. Runs `Setup`, then the tick loop, then `Shutdown`.
6. Tears down in reverse: stops the tick loop, disposes engine defaults and the asset catalog, joins
   the render thread, drains GPU teardown, and closes SDL.

A `Setup` exception skips `Shutdown`. A `Tick` exception still runs `Shutdown`, and the exceptions
are aggregated. A user window close is a graceful exit request, not a veto.

## Device loss

If Vulkan reports a lost device or an out-of-date swapchain, the backend rebuilds the device,
swapchain, pipelines, and frame resources. It retries at most three times. Live GPU assets are
replayed onto the new device, and render textures keep their public objects and ids across recovery.
If recovery fails, the app stops with `GraphicsDeviceLostException`.

## Module map

| Folder | Responsibility |
| --- | --- |
| `Internal/App` | Lifecycle host, tick and render loops, surface channel, teardown queue. |
| `Internal/Concurrency` | Frame ring, leases, tick synchronization context. |
| `Internal/Graphics` | Frame packet, camera service, backend seam, render textures, tick thread, post-process contracts. |
| `Internal/Graphics/Vulkan` | Instance, device, swapchain, pipelines, descriptors, passes, present, readback. |
| `Internal/Assets` | Pack format, asset catalog, loaders, GPU upload bridge, procedural builders. |
| `Internal/Fonts` | SFNT/TTF/CFF parsing and MSDF glyph generation. |
| `Internal/Platform` | Platform seam and the SDL3 implementation. |
| `Internal/Input`, `Internal/Time` | Input state, engine clock, tick pacing. |

## Invariants

- Cameras are sequential: create one, record it, dispose it before creating the next or ending the
  tick.
- Engine defaults are engine-owned. Assets loaded by the game are caller-owned and must be disposed.
- Asset packs are fixed when the app is built; there is no runtime mount or unmount.
- Shader programs are baked. The runtime only loads SPIR-V and sidecar metadata.
- Custom data assets use compiled PolyType shapes, so loading works under Native AOT.
- Every engine asset is reference-counted by the frame that uses it, so the game can dispose its
  handle after recording a draw and the GPU resources survive until the frame completes.

See [Assets](assets.md) for the bake pipeline and [Renderer](render.md) for the Vulkan details.
