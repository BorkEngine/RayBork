# RayBork Architecture

Status: design draft, 2026-11-11. This document describes the runtime shape the
[API reference](api.md) and [roadmap](roadmap.md) assume. It is not an as-built document yet.

## Goals

RayBork is a small, raylib-shaped 3D game framework for C#:

- a window, input, time, assets, and enough drawing to make a game without a scene framework;
- linear HDR rendering with AgX tone mapping, reverse-Z, MSAA, bindless textures, and automatic
  instancing;
- SDL3 for windowing, input, and (later) sound;
- Windows x64 and Linux x64 first, Native AOT for game assemblies;
- a renderer seam that keeps the public API independent of Vulkan so future mobile/web backends can
  replace it;
- a bake step for assets, but a runtime that never compiles shaders or parses source formats;
- one runtime assembly, `RayBork.Engine`, with renderer and platform internals inside it. There is
  no `RayBork.Render` or `RayBork.Runtime` split like Bork2d.

The engine is deliberately not an editor, ECS, physics engine, animation system, or UI toolkit.
Those can be built on top of the draw API or added in later milestones.

## Projects and dependency rules

```text
RayBork.Tests ──────> RayBork.Engine, RayBork.Bake

RayBork.Engine ─────> Silk.NET.Vulkan, SDL3-CS, Zstd, PolyType, Nerdbank.MessagePack,
                      Microsoft.Extensions.Logging, ZLogger
RayBork.Bake  ──────> RayBork.Engine (pack writer/types), shaderc, image/font/model tooling (JIT)
Samples       ──────> RayBork.Engine
```

| Project | Role |
| --- | --- |
| `RayBork.Engine` | Public API and all runtime internals: app host, SDL3 platform, renderer seam and Vulkan backend, asset catalog, pack reader, font/MSDF, input, logging. Native AOT compatible. |
| `RayBork.Bake` | JIT build library and assets-executable host: source discovery, TOML binding, processors, MessagePack shapes, pack writer, incremental manifest. Not referenced by a runtime project. |
| `RayBork.Tests` | xUnit v3 + Moq. Unit tests, architecture/dependency tests, GPU integration tests, AOT publish smoke tests. No golden-image suite. |
| `samples/` | Castle, Pong, Text, Instancing, RenderTarget. Each has assets, a bake step, and a Native AOT publish target. |

Rules:

- No runtime project references `RayBork.Bake`.
- No public type exposes a Silk.NET, SDL3, Vulkan, or other backend-native handle.
- The engine may reference only pinned, AOT-tested packages. Every package added to
  `RayBork.Engine` must pass a trim/AOT publish before it is accepted.
- `RayBork.Bake` may use reflection-heavy libraries because it is never published as AOT.
- Tests may reference everything.
- The renderer backend is selected internally. The public API does not expose Vulkan types or
  device handles.

These rules are enforced by an architecture test and by the AOT publish gate, not only by review.

## Renderer backend seam

The public API is renderer-agnostic. Internally, the render thread talks to an `IRendererBackend`
contract that owns:

- instance/device/surface/swapchain lifetime and recovery;
- pipelines, descriptor sets, buffers, and texture tables;
- command recording, submission, presentation, and GPU timing;
- capability reporting (`EngineInfo`: backend name, HDR output, sample count, swapchain format).

`Vulkan12Backend` is the first implementation. A future WebGPU or mobile backend implements the
same seam without changing `Camera`, `Shader`, `Mesh`, or `Model`. The seam is internal; games never
implement a backend in v1.

Recoverable backend failures (swapchain out-of-date, resize, focus changes, device loss) are
handled by the backend. It pauses, recreates the affected resources, and resumes. If recreation
fails, the app stops with `GraphicsDeviceLostException` or the underlying `GraphicsException`.

## Threads

| Thread | Owns | Notes |
| --- | --- | --- |
| Main / window thread | SDL3 initialization, window, event pump, monitor and clipboard state, native surface creation | Runs `RayBorkApp.Run`. The process main thread. No game callbacks. |
| Tick thread | `IGame.Setup`, `Tick`, `Shutdown`; cameras; shader state; per-frame input snapshot | Dedicated thread. The game-facing engine state is tick-thread affine. |
| Render thread | Backend device/swapchain, pipelines, descriptor sets, buffers, command buffers, GPU uploads, present, recovery | Consumes sealed frame packets. Never calls user code. |
| Asset loader | Pack I/O, decompression, deserialization, CPU decode | Completes loads; hands GPU upload requests to the render thread. |
| Sound thread | Audio mixer (post-1.0) | Not present in v1. |

Thread names are set for debuggers: `RayBork.Main`, `RayBork.Tick`, `RayBork.Render`,
`RayBork.Asset`, `RayBork.Audio`.

### Thread-affine state

- `IGame` callbacks and every object created by them (`Camera`, `RenderTexture`, `Font`
  preloading, `Shader.SetUniform`) are tick-thread affine.
- `IAssetCatalog.Load`, `LoadData`, and `Exists` are thread-safe. Concurrent loads are allowed.
  Engine assets may be used on the tick thread after completion; custom data returned by the same
  `Load<T>` method is caller-owned. Custom types must be registered with `WithAssetType<T>()`
  before `Build`.
- `EngineContext.Time`, `DeltaTime`, `FrameCount`, `Fps`, and `Stats` are snapshots for the
  current tick. They are not synchronization primitives.
- `IInput` and `IWindow` getters return the last confirmed snapshot and are safe to read from
  the tick thread. Window setters are queued to the main thread.
- Renderer backend objects are internal. No public type is a Vulkan handle.
- `ILoggerFactory` is safe to use from any thread; the app owns the default factory.
- If user code starts its own threads, it must join them before `Tick` returns. RayBork does not
  support background mutation of cameras, shader state, or engine assets that are in use.

### Setup and shutdown continuations

`Setup` and `Shutdown` are asynchronous, but they run on the tick thread. The engine installs a
`SynchronizationContext` on that thread, so `await` continuations resume on the tick thread while
the setup/shutdown task is pending. This keeps this legal:

```csharp
public async Task Setup(EngineContext ctx) {
    _model = await ctx.Assets.Load<Model>("/models/castle");
    _texture = await ctx.Assets.Load<Texture>("/textures/castle");
    _shader = await ctx.Assets.Load<Shader>("/shaders/castle");
    _shader.SetUniform("mainTexture", _texture);
}
```

`ConfigureAwait(false)` opts out of the context and may resume on a pool thread. Asset calls are
still safe there, but tick-affine objects must not be touched from that continuation.

## Frame handoff

The tick and render threads run concurrently. The render thread draws the previous completed tick.

```text
tick thread                                  render thread
-----------                                  -------------
wait for a free slot
snapshot input / advance clock
IGame.Tick(ctx)
  camera 1: record draws -> slot
  camera 1: Dispose -> seal pass
  camera 2: ...
seal slot, publish as newest ready
                                              wait for newest ready
                                              plan passes, build batches
                                              record command buffer
                                              submit, present
                                              release slot on fence signal
```

- The ring has three slots. A tick blocks only when all three are occupied; this bounds run-ahead.
- A newer publish supersedes an older `Ready` slot, never a slot that is being rendered.
- The renderer always draws the newest completed tick. It does not queue a backlog of stale frames.
- A tick that requests exit still publishes its final frame.
- A frame with no camera still presents a cleared image, so the swapchain never starves.
- The engine does not interpolate between ticks in v1. Movement at a tick rate below the display
  rate will show discrete steps. Fixed timestep with interpolation is a later milestone.
- When the window is minimized or unfocused, the tick loop continues at a **30 FPS cap**;
  the renderer may idle while the swapchain has no extent.

## Time

- `EngineContext.Time` is absolute monotonic seconds since `Run` started, measured with
  `Stopwatch`. It is not the sum of `DeltaTime`.
- `EngineContext.DeltaTime` is the elapsed seconds since the previous tick, clamped to
  `MaxDeltaTime`.
- `EngineContext.UnscaledDeltaTime` is the unclamped value.
- The first tick reports `DeltaTime = 0`.
- `MaxDeltaTime` is configured on the builder and defaults to `0.25` seconds.
- `Time` continues across a long stall; `DeltaTime` does not. Games that need a stall-free clock
  should accumulate `DeltaTime` themselves.
- Minimized/unfocused windows cap the tick cadence at 30 FPS; the clock keeps advancing.

## Space, color, and depth

- Right-handed, Y-up, forward `-Z`.
- `Transform` is a world-space TRS value. `WithParent` treats the receiver as parent-local and
  returns the composed world transform: `world = local * parent` under System.Numerics'
  row-vector convention.
- User-facing angles are degrees. `System.Numerics` math stays radians; RayBork extension methods
  convert at the boundary.
- Color is linear float RGBA with HDR values allowed. There is no 8-bit `Color` in the public API.
- Cameras render into a linear `Rgba16Float` scene target. The present pass applies AgX and the
  swapchain transfer function. The swapchain prefers an HDR format and falls back to
  `B8G8R8A8_SRGB` or `R8G8B8A8_SRGB`.
- MSAA defaults to 4x and is resolved before presentation; HDR and SDR paths share the resolve.
- Projection is built for Vulkan's `[0,1]` depth range and reverse-Z:
  - near plane maps to depth `1.0`;
  - infinity maps to `0.0`;
  - clear depth is `0.0`;
  - default compare is `GreaterOrEqual`.
- Perspective cameras may use `far = +infinity`; orthographic cameras require a finite `far`.
- The renderer owns the Vulkan Y-flip and the front-face rule. Public screen coordinates are
  top-left origin; world space stays Y-up.

The reverse-Z perspective projection used by the engine is, for row vectors:

```text
[ f/aspect   0        0        0 ]
[ 0          f        0        0 ]
[ 0          0        0       -1 ]
[ 0          0        near     0 ]
```

where `f = 1 / tan(fieldOfView / 2)`. This gives `z_ndc = near / distance` and `w = distance`
for an infinite far plane. A finite far plane uses the usual reverse-Z variant. The Y-flip is
applied separately by the viewport/projection path.

## Shader state, batching, and instancing

RayBork has no public `Material`. A `Shader` is a complete program plus a mutable uniform cursor:

1. `Shader.SetUniform(name, value)` writes the current value.
2. `Camera.Draw*` copies the current values into the frame packet.
3. Later `SetUniform` calls affect later draws only.
4. The renderer packs the captured values into a per-instance parameter block.
5. Texture references become bindless indices in the instance record.

Batching key: `(mesh, shader, render state, topology)`. Uniform values and texture bindings do not
split a batch. A model with several parts is several mesh groups; each group is one instanced
draw.

This makes the following one draw call when `mesh`, `shader`, and render state match:

```csharp
shader.SetUniform("mainTexture", textureA);
camera.DrawMesh(mesh, transformA, shader);
shader.SetUniform("mainTexture", textureB);
camera.DrawMesh(mesh, transformB, shader);
```

The shader ABI is generated by the bake so custom GLSL does not have to declare the plumbing:
a frame uniform buffer, a bindless `sampler2D[]`, and a per-instance storage buffer containing the
transform, tint, UV rectangle, texture indices, and the reflected user uniforms. v1 has one fixed
vertex input layout: position, normal, uv, uv1, uv2, uv3, color, tangent. Vertex and fragment entry
points are `main_vertex` and `main_fragment`. The full ABI is in the
[API reference](api.md#74-shader-abi).

Vulkan instancing consumes one vertex/index buffer, so different meshes with the same shader are
still separate draws in v1. Multi-draw indirect is post-1.0.

## Assets

The runtime never loads loose source assets in normal operation.

```text
source files + TOML sidecars
        |
        v
RayBork.Bake assets executable (JIT)
  texture/model/shader/font processors, TOML data binding, validation, hashing
        |
        v
assets.pck (RAYBORKP v1, Zstd payloads, binary index, aligned GPU blobs)
        |
        v
RayBork.Engine asset loader (I/O, decompress, deserialize, GPU upload)
        |
        v
game code: await ctx.Assets.Load<Model>("/models/castle")
           await ctx.Assets.Load<GameSettings>("/data/settings")
```

- The pack is the unit of deployment. Multiple packs can be mounted in order; later packs shadow
  earlier paths. The engine's built-in pack is mounted first.
- `Load<T>` is one typed loader. It handles built-in engine assets (`Mesh`, `Model`, `Texture`,
  `Shader`, `Font`, later `AudioClip`) and custom data types registered with
  `WithAssetType<T>()`.
- A custom data load reads a MessagePack entry through a compiled `[GenerateShape]`; the result is
  a fresh caller-owned instance per call, matching Bork2d. The type is registered at build time with
  `WithAssetType<T>()`; an unregistered type is a `NotSupportedException`.
- `LoadData` returns raw bytes.
- glTF and OBJ are source formats. The bake converts them to the engine's packed mesh/model format;
  the runtime never parses them.
- A load for an engine asset completes after GPU upload, so the first draw does not trigger a
  hidden upload hitch.
- Engine assets are reference-counted. A completed frame keeps its referenced assets alive until
  the GPU fence signals. Disposing an asset after recording a draw is safe; using the public object
  after `Dispose` is not.
- The loader is bounded and cancellation-aware. Shutdown cancels pending loads and drains the
  renderer before destroying GPU resources.
- Built-in assets are not special: the engine's built-in pack is produced by the same
  `IAssetProcessor` implementations through `UseDefaultProcessors`.

The pack format, bake application builder, processors, sidecars, and incremental manifest are
specified in the API reference's tooling section.

## Native AOT

- `RayBork.Engine` sets `IsAotCompatible`, `EnableTrimAnalyzer`, and `EnableAotAnalyzer`.
- No runtime reflection serialization. Pack metadata is read explicitly; custom data uses
  PolyType `[GenerateShape]` and Nerdbank.MessagePack.
- No runtime shader compilation. Shader reflection is bake-generated metadata.
- `Load<T>` maps built-in engine asset types with a closed type switch; custom data uses a
  builder-registered compiled shape delegate. `LoadData` covers opaque bytes.
- `Microsoft.Extensions.Logging` and ZLogger are AOT-tested runtime dependencies.
- P/Invoke is direct; native SDL3 and Vulkan libraries are deployed per RID.
- The generic asset loader maps a closed set of built-in types; custom data goes through the
  `WithAssetType<T>()` registry or `LoadData`.
- The bake tool is JIT and may use reflection, `MetadataLoadContext`, and native executables. It is
  never referenced by a runtime project and never enters the publish graph.
- Every milestone's exit gate includes at least one `win-x64` and one `linux-x64` AOT publish of
  a sample.

## Invariants

1. The tick thread never calls the renderer backend. The render thread never calls user code.
2. A `Camera` is valid only between `CreateCamera` and `Dispose`, inside one `Tick`.
3. A frame packet owns a reference to every engine asset it references.
4. Shader state is captured at draw time; later mutation cannot change a recorded draw.
5. The renderer draws the newest completed tick, never a stale queue.
6. Public API never exposes a native handle or a backend type.
7. A game assembly published with `PublishAot=true` must start, load a pack, draw, and shut down.
8. Depth is reverse-Z everywhere: projection, clear, compare, and tests.
9. The public surface is renderer-agnostic; swapping the backend does not change game code.
10. The engine's built-in assets are always mounted, so an empty game can clear the screen.
11. Only one RayBork app runs per process in v1.
