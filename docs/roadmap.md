# RayBork Roadmap

Status: **v0.1 M7 as-built**, 2026-11-16. M0-M7 are shipped. Milestones are ordered by
dependency and exit criteria, not by calendar time. There are no dates or time estimates in this
document.

The API these milestones implement is in [api.md](api.md). The runtime shape is in
[architecture.md](architecture.md). No open design questions remain; resolved decisions are
reflected directly in these documents.

## 1. Definition of 1.0

RayBork 1.0 is done when all of the following are true:

1. A game can create a window, poll keyboard/mouse/gamepad, load a pack, draw a textured 3D model
   with HDR and depth, draw text, draw 2D sprites with an orthographic camera, and render to an
   offscreen target.
2. The public API in [api.md](api.md) is implemented, documented, and covered by XML docs and
   public-API analyzer entries.
3. The Castle, Pong, Text, Instancing, and RenderTarget samples run on Windows x64 and Linux x64
   from a Native AOT publish. WSL is a Linux build environment only; WSLg hardware Vulkan is not a
   target.
4. The renderer is Vulkan 1.2, reverse-Z, linear HDR, AgX tone-mapped, MSAA, bindless, and
   instanced by default. A frame with the same mesh, shader, and render state is one draw call
   regardless of textures or uniform values.
5. The tick thread, render thread, and main/window thread run concurrently with the frame handoff
   in [architecture.md](architecture.md); no data races are found by the stress suite.
6. The pack/bake pipeline is cacheable, validates references, binds custom TOML data through
   compiled shapes, and fails with actionable diagnostics. Pack bytes are not required to be
   deterministic.
7. Validation layers are clean on the gate scenes; the AOT, unit, and GPU integration suites are
   green on the release matrix. There is no golden-image suite.
8. The renderer seam is internal and Vulkan is the only backend, but the public API exposes no
   Vulkan types and a second backend can be added without changing game code.
9. The known-limitations list is honest and matches the out-of-scope table in the
   [API reference](api/versioning.md#15-out-of-scope-for-v1).

Audio is explicitly **not** part of 1.0. It is the first post-1.0 workstream.

## 2. Guiding constraints

- **One runtime assembly.** `RayBork.Engine` contains the app host, SDL3 platform, renderer seam and
  Vulkan backend, asset catalog, pack reader, font/MSDF, input, and logging. There is no
  render/audio/runtime split.
- **The renderer is replaceable.** Vulkan 1.2 is the first backend. The public API stays
  renderer-agnostic for future mobile/web backends.
- **AOT is a gate, not a cleanup task.** Every milestone that touches runtime code must publish a
  Native AOT sample for `win-x64` and `linux-x64`.
- **No public native handles.** Silk.NET, SDL3, Vulkan, and backend types stay internal.
- **No public `Material`.** A public `Shader` is a material instance over an internal program;
  the renderer batches by mesh, program, and render state. Per-instance uniforms and bindless
  textures do the rest.
- **Reverse-Z and linear HDR everywhere.** Projection, clear, compare, tone mapping, MSAA resolve,
  and tests share one convention.
- **The render thread draws the previous completed tick.** No backlog, no stale queue, no hidden
  interpolation in v1.
- **The bake is the only place that compiles shaders or parses source assets.** The runtime is
  data-driven. glTF/OBJ and KTX2 are source formats; packs contain engine formats.
- **No decision log.** Decisions live in the docs they affect: API contracts in `api.md`, runtime
  shape in `architecture.md`, delivery in this roadmap.
- **No golden-image tests.** Renderer tests assert validation, counts, readback values with
  explicit tolerances, and crash/leak behavior.
- **Tests are part of the milestone.** A feature without tests and an AOT publish is not done.

## 3. Milestone map

| ID | Milestone | Outcome | Depends on |
| --- | --- | --- | --- |
| M0 | Foundations and public contract | Repo, build, test, AOT, style, and API guardrails | — |
| M1 | Host, threads, window, input, time | A game loop with a real window, input, and logging | M0 |
| M2 | Renderer foundation | Backend seam, swapchain, HDR/AgX/MSAA, reverse-Z, recovery | M1 |
| M3 | Asset pipeline | Pack v1, assets executable, processors, typed loaders | M1 |
| M4 | Core 3D renderer | Models, shaders, bindless textures, instancing, camera draws | M2, M3 |
| M5 | Primitives and collision | Raylib-style 3D shapes, lines, billboards, wireframe models, mesh generators, collision queries, HDR AgX rework, restored linearized Bork2d color presets | M4 |
| M6 | Text and MSDF | Managed TTF/OTF parsing, deterministic on-demand MSDF, layout, built-in font, world-space text | M4 |
| M7 | 2D and orthographic rendering | Orthographic cameras, screen transform, quad/disc/grid primitives, Pong | M4 |
| M8 | Render targets and multiple cameras | HDR/MSAA targets, readback, split-screen | M4 |
| M9 | Input and window polish | IME, clipboard, fullscreen, gamepad hotplug/rumble | M1 |
| M10 | Performance and hardening | Pacing, caches, stress, validation CI, recovery hardening | M4-M8 |
| M11 | Samples, docs, packaging | Five samples, template, NuGet, docs, MIT license | M10 |
| M12 | 1.0 release gate | API freeze, matrix, AOT, release notes | M11 |

M3 can run in parallel with M2. M5, M6, M7, M8, and M9 can largely run in parallel after their
dependencies. M10 starts only when the feature surface is complete. M11 and M12 are sequential.

## 4. Milestones

### M0 — Foundations and public contract

**Goal.** Make the repository a place where a public API can be added without breaking the build,
the AOT publish, the style policy, or the dependency rules.

**Scope.**

- Keep the `RayBork.Engine`, `RayBork.Bake`, `RayBork.Tests`, and `samples/` layout. Add
  `RayBork.Tests` and `samples/HelloInterop` to the solution.
- `Directory.Build.props`: `net11.0`, nullable, implicit usings, warnings-as-errors,
  deterministic builds, AOT/trim analyzers for runtime projects.
- Pin `Silk.NET.Vulkan`, `SDL3-CS`, `SDL3-CS.Native`, `PolyType`, `Nerdbank.MessagePack`,
  `Microsoft.Extensions.Logging`, and `ZLogger` centrally in `Directory.Packages.props`; record the
  AOT result for every runtime dependency.
- Use the in-box `System.IO.Compression.Zstandard*` APIs from `net11.0` for pack payloads. There is
  no compression package; Zstandard ships with the runtime.
- Add `Microsoft.CodeAnalysis.PublicApiAnalyzers` to `RayBork.Engine` with
  `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt`.
- Add the minimal public API `RayBork.EngineVersion` with `public static Version Current { get; }`.
  `Current` reports `0.1.0.0`, the loaded engine assembly version; the build and package version is
  `0.1.0`. It is immutable for the app lifetime, is safe to read from any thread, and throws no
  RayBork exception. It is the only public member added in M0.
- Add `RayBork.Tests` with xUnit v3 and Moq. Define category traits for unit, GPU integration,
  AOT, and performance tests. No golden-image category.
- Add architecture tests: dependency matrix, no native/backend types in public API, no bake
  references from runtime projects, no `Material` type.
- Add `samples/HelloInterop`, a `net11.0` console host that loads SDL3, opens no window, and exits;
  publish it with `PublishAot` on Windows and Linux.
- Add `.github/workflows/ci.yml`: `build-test` on `windows-latest` and `ubuntu-latest`
  (restore, Debug and Release builds, test, `dotnet format`) and a `docs` job running
  `scripts/check-docs.ps1`.
- Add `.github/workflows/pr-title.yml` to enforce the PR title convention.
- Add `.github/pull_request_template.md` and the PR rules in `AGENTS.md`.
- Add `.editorconfig` K&R braces, `SCREAMING_SNAKE_CASE` constants, and `AGENTS.md`; add the MIT
  `LICENSE`.
- Add `docs/` and the initial API/architecture/roadmap set, plus `THIRD-PARTY-NOTICES.md`. There is
  no decision log.
- Design the internal `IRendererBackend` seam and its lifecycle in [architecture.md](architecture.md);
  it is not a public type, and M0 adds no renderer code.

**Exit criteria.**

- `dotnet build` and `dotnet test` are green on a clean checkout.
- `samples/HelloInterop` loads SDL3, opens no window, and exits; the same host publishes for
  `win-x64` and `linux-x64` with zero AOT/trim warnings.
- The public API analyzer is active, `EngineVersion.Current` is the only public member added in M0,
  and the architecture tests fail on a deliberate violation.
- The dependency matrix documents every runtime, test, and tooling package and its AOT status.
- `dotnet format --verify-no-changes` passes with K&R braces and `SCREAMING_SNAKE_CASE` constants.
- GitHub Actions passes on a PR to `develop`: `build-test` on Windows and Ubuntu, `docs`, and
  `pr-title`.
- The API reference, architecture, and roadmap are linked from the repository README.
- The MIT `LICENSE` and [third-party notices](../THIRD-PARTY-NOTICES.md) are present.

**Risks.** SDL3-CS per-RID packaging is covered by the `samples/HelloInterop` publish gate. If a
later RID or SDL upgrade breaks it, the fallback is a thin hand-written SDL3 P/Invoke layer for the
functions the engine uses, and the binding decision is revisited.

### M1 — Host, threads, window, input, time

**Goal.** A game can open a window, receive input, run `Setup`/`Tick`/`Shutdown` on a dedicated
tick thread, log through `ILoggerFactory`, and exit cleanly.

**Scope.**

- SDL3 initialization, window creation, event pump, close request, resize, minimize, focus,
  DPI, monitor enumeration, and clipboard text.
- Keyboard and mouse: physical scancodes, edge/level state, text input, wheel, cursor modes.
- Gamepad enumeration, buttons/axes/triggers, hotplug polling, SDL gamecontroller mappings.
- `IGame`, `RayBorkAppBuilder`, `RayBorkApp`, `EngineContext`.
- Dedicated tick thread with a `SynchronizationContext`; main/window thread; render-thread
  placeholder; asset-loader placeholder; frame ring skeleton.
- `Stopwatch` clock with `double Time`/`DeltaTime`/`UnscaledDeltaTime`, `MaxDeltaTime`, `FrameCount`,
  `Fps`, `IsFirstTick`.
- 30 FPS tick cap when the window is minimized or unfocused.
- `IWindow` queued setters/snapshot getters; `IInput` tick snapshot.
- Logging: `Microsoft.Extensions.Logging`, default ZLogger console provider, `ILoggerFactory` on
  `EngineContext`, `WithLoggerFactory`, `WithLogLevel`.
- `WinExe` enforcement for release samples; console `Exe` for samples and diagnostics.
- Shutdown ordering, fatal-error paths, and thread joins.

**Exit criteria.**

- The M1 sample window opens, reports the correct DPI/size, receives keyboard/mouse/gamepad input,
  and exits on close or `RequestExit`.
- `Setup` runs on the tick thread; its `await` continuations resume on the tick thread.
- `Tick` runs at a stable cadence with a clamped delta; the first delta is zero.
- Minimize and focus loss cap the tick loop at 30 FPS; restoring returns to the configured cadence.
- Logging appears through `ctx.LoggerFactory`; a caller-supplied factory is used and not disposed.
- The main thread remains responsive while the tick thread runs (no event-pump starvation).
- A window resize and a minimize/restore are observed without crashing.
- Thread names are visible in a debugger; all threads join on exit.
- Unit tests cover input mapping, time clamping, builder validation, and lifecycle errors.
- Native AOT publish runs the M1 sample on Windows and Linux.
- No Vulkan code is required yet; the window may show the platform's default background.

**Notes.** M1 deliberately ships without drawing. That keeps the first milestone small and makes
the threading/lifecycle contract testable before the GPU enters the picture. The window requests
`SDL_WINDOW_HIGH_PIXEL_DENSITY`; `RenderWidth`/`RenderHeight` report the physical drawable size, and
the M2 renderer sizes the swapchain from those values so high-density displays render at native
pixel density.

### M2 — Renderer foundation, HDR, and recovery

**Goal.** A backend-neutral renderer seam with a Vulkan 1.2 backend, a linear HDR swapchain,
AgX presentation, MSAA, reverse-Z depth, and recovery from recoverable failures.

**Scope.**

- Internal `IRendererBackend` contract: initialize, resize, submit frame packet, present, recover,
  report `EngineInfo`, dispose.
- `Vulkan12Backend`: instance 1.2, validation/debug-utils in debug, device selection, queue
  families, surface, swapchain, present modes.
- Swapchain selection: prefer `R16G16B16A16_SFLOAT` with an extended-sRGB-linear (scRGB) color
  space on HDR windows, then `B8G8R8A8_SRGB` or `R8G8B8A8_SRGB`. HDR10/PQ
  (`A2B10G10R10_UNORM_PACK32`) is deferred until the output transform and HDR metadata exist.
- Linear `Rgba16Float` scene target; present pass with AgX and exposure; `TonemappingMode.None`
  escape hatch. M2 shipped an initial AgX curve that did not use the HDR headroom; M5 replaced it
  with Godot 4.7's HDR-aware AgX and removed the separate highlight stage. The rest of the M2 present
  path is unchanged.
- MSAA 4x default (1/2/4/8 supported); HDR color resolves to a single-sample image before present.
  Depth is cleared and compared under MSAA but not resolved until something samples it (M4/M8).
- Present mode `Mailbox` default, `Fifo` fallback, `Immediate` by explicit choice.
- Reverse-Z depth (`D32_SFLOAT`), clear to `0.0`, default compare `GreaterOrEqual`.
- Two/three frames in flight, acquire/present, resize, minimize/restore, swapchain recreation.
- Recoverable failure handling: swapchain out-of-date, resize, focus changes, device loss.
  Recovery pauses the render thread, recreates resources, increments `RenderStats.RecoveryCount`,
  and resumes; failed recovery is fatal.
- Render thread owns the backend/device; main thread owns the surface.
- Minimal camera pass that clears color/depth and presents; no mesh draws yet.
- `RenderStats` skeleton: frame index, CPU frame time, frames in flight, HDR flag, sample count,
  recovery count.
- Validation-layer CI run on the gate scene.

**Exit criteria.**

- The window clears to the requested linear color; AgX output is visible on both HDR and SDR
  fallback paths.
- Depth is cleared to `0.0` every frame and reverse-Z compare is configured.
- MSAA 4x resolves without validation errors; switching to 1x and 8x works or falls back with a
  warning.
- Present mode is Mailbox when available and FIFO otherwise; the selected mode is reported.
- Resize, minimize, restore, and alt-tab do not crash or leak swapchain images.
- The render thread draws the previous completed tick; a slow tick does not block the GPU, and a
  full ring backpressures the tick thread.
- A forced swapchain/device loss recovers at least once and increments `RecoveryCount`; a simulated
  unrecoverable failure produces `GraphicsDeviceLostException` and a clean shutdown.
- Validation is clean for the clear/resize/present/recover cycle.
- Native AOT publish runs the M2 sample on Windows and Linux; Linux uses lavapipe.
- A software-Vulkan (lavapipe) CI job runs the same clear test under Xvfb; it is the Linux GPU path.

**Notes.** The M2 AgX curve was normalized to display white and did not use the reported HDR
headroom, so HDR highlights stopped at paper white. `TonemappingMode.None` could write above
paper white for direct authoring. M5 reworked the tone mapper so the curve's output maximum is the
reported headroom on the scRGB path; see the M5 as-built and
[Rendering: Camera](api/rendering/camera.md#72-projection-clear-hdr-and-tone-mapping).

**Risks.** HDR swapchain support is highly driver-dependent; the SDR fallback is the guaranteed
path. The device policy is tested on Windows and lavapipe before M4 relies on bindless.

### M3 — Asset pipeline and custom data

**Goal.** A game can configure an assets executable, bake sources into `assets.pck`, mount it, load
engine assets and custom data, create procedural meshes and textures, and rely on correct lifetime
and GPU residency.

**Scope.**

- Pack v1 writer/reader: magic, version, binary index, sorted canonical paths, Zstandard payloads,
  alignment, XXH3/XXH32 integrity, memory-mapped reads, clear corruption diagnostics.
- Canonical paths: optional leading `/`, forward slashes, case-insensitive input, lowercase
  canonical storage, extensionless, Bork2d-compatible segment grammar.
- Game-owned assets executable using `BakeApplicationBuilder`: `WithProjectRoot`, `WithPack`,
  `UseDefaultAssets`, `RegisterAsset<T>`, `UseDefaultProcessors`, `AddProcessor`, `RunAsync`.
- `IAssetProcessor` contract (`Name`, `Extensions`, `Policy`, `SidecarShape`, `Process`), output
  and diagnostic sinks, and `AssetProcessor<TSidecar>`.
- Built-in processors are ordinary processors: texture, model, shader, and font. Audio is added in
  A1. Built-in assets are not special; the engine's built-in pack uses the same processors.
- TOML data assets with `[raybork].type`, registered through `RegisterAsset<T>()` with
  `[GenerateShape]`; packed as MessagePack.
- glTF 2.0 and OBJ processing into the engine's mesh/model format; PNG/JPEG/TGA/BMP sources into a
  KTX2 engine texture payload with an optional BC7/BC5/BC1/BC4 block-compressed variant. The
  runtime never parses source formats. KTX2 source passthrough is a follow-up.
- Runtime `IAssetCatalog`: one `Load<T>`, `LoadData`, `Exists`; `WithAssetType<T>()` registers
  custom data types for `Load<T>`. Coalesced async loads, cancellation, reference counting for
  engine assets, caller-owned custom data.
- GPU upload handoff: texture images, mesh buffers, shader modules, frame retention, and re-upload
  after a device recovery.
- Procedural assets: `MeshBuilder` and `TextureBuilder` own CPU data, expose vertices/indices,
  compute normals/tangents/bounds, and upload with `BuildAsync` through the same bridge. Procedural
  textures support uncompressed `Rgba8Unorm`, `Rgba8Srgb`, and `R8Unorm` with optional mips; assets
  are immutable after build and use reserved `/_procedural/...` paths.
- Per-source incremental manifest: unchanged sources reuse the previous pack's entry descriptors and
  payloads; changed, new, and deleted sources are reprocessed. A tool-version or configuration
  change invalidates the cache. Pack bytes are not deterministic; only the entry set and logical
  layout are ordered.
- Reject missing references, duplicate/case-normalized paths, bad shader interfaces, TOML shape
  failures, and unsupported source formats before publishing.

**Exit criteria.**

- The M3 sample bakes a pack, loads a texture, mesh, shader, and custom TOML data, and reads back
  their metadata.
- Loading the same engine path twice returns the same object and creates one GPU resource.
- A procedural mesh and texture can be built from CPU data and are usable only after `BuildAsync`
  completes. Loaded and procedural meshes expose CPU vertices/indices; normal, tangent, and bounds
  helpers produce the documented results.
- The frame packet retains every engine asset recorded into it and releases it after the frame
  completes. M3 tests the retention mechanism directly; the end-to-end "dispose after recording a
  draw" check lands with the M4 camera and draw API.
- `Load<T>` for a registered custom type returns a fresh instance per call; `LoadData` returns
  identical bytes. Engine assets still return the same reference-counted object.
- A corrupt pack (bad magic, truncated index, bad hash, truncated payload) produces the documented
  diagnostic and a clean failure.
- Incremental bake reprocesses only changed assets; a tool-version change invalidates the cache.
- A second bake of unchanged inputs may produce different pack bytes; no test asserts byte identity.
- Built-in processors and a custom processor produce entries through the same `IAssetProcessor`
  path; the engine's built-in pack is generated by the build-time bake tool and mounted first.
- Native AOT publish loads the pack and custom data on Windows and Linux.
- Fuzz tests cover the pack header, index, path normalization, and payload lengths.

**Notes.** M3 owns the pack format. Once a sample pack is published, the format is versioned and
changes require a migration note. M3 does not need to draw the loaded assets; M4 consumes them. The
runtime `Model` and `Font` loaders land in M4 and M6; M3 bakes and validates their entries. The
`watch` bake command is deferred to M10; M3 ships `bake`, `clean`, `inspect`, and `--help`.
GPU uploads are submitted without a fence wait and polled on later render-loop turns, so the loader
and renderer are not blocked by a copy; a dedicated transfer queue is a later optimization, not an
M3 requirement. Procedural assets are M3 creation primitives; drawing them is M4 work.

### M4 — Core 3D renderer, shader state, and instancing

**Goal.** Draw textured 3D models with linear HDR, AgX presentation, MSAA, depth, custom shaders,
automatic instancing, and per-instance textures/uniforms.

**Scope.**

- Vertex/index buffers, staging uploads, bindless texture table, sampler cache, pipeline cache,
  descriptor management, render-state presets.
- Camera recording: `DrawModel`, `DrawMesh`, `DrawInstanced`, `DrawModelInstanced`; shader-state
  snapshots; per-instance parameter block; bindless texture indices; linear tint/UV fields.
- Batch planning by `(mesh, internal shader program, render state)`, frustum culling,
  opaque/alpha sorting, draw call stats.
- `Model` as a caller-owned composition of parts with meshes, per-part materials, local transforms,
  and computed bounds. `Shader` is a material instance over an internal program; `Load<Shader>`
  returns a new independent material.
- Shader ABI: frame UBO, fixed 2048-element bindless texture array, instance SSBO, generated
  prologue, reflection validation, uniform-type checks, `SetUniform` cursor and `ResetUniforms`.
- Fixed vertex layout: position, normal, uv, uv1, uv2, uv3, color, tangent.
- Entry points `main_vertex` and `main_fragment`.
- HDR scene target, AgX present, MSAA resolve, `TonemappingMode`, exposure.
- M4's initial HDR AgX was the M2 curve with a separate paper-white highlight stage driven by the
  reported SDR white level and HDR headroom. `TonemappingMode.None` remained the direct HDR authoring
  path. M5 replaced that entire path with Godot 4.7's HDR-aware AgX; the current contract is in the
  M5 as-built and
  [Rendering: Camera](api/rendering/camera.md#72-projection-clear-hdr-and-tone-mapping).
- Built-in unlit mesh shader (`/shaders/mesh`), white/error textures, preloaded through
  `EngineContext.Defaults`.
- `DrawModel`/`DrawMesh` errors: disposed assets, wrong render state, mismatched topology, missing
  uniforms, unsupported shader interface.
- Reverse-Z depth testing, alpha blending, back-face culling, viewport/scissor basics.
- Unit tests for shader-state snapshot semantics, uniform validation, batching keys, and
  render-state equality. No golden-image tests.

**Exit criteria.**

- The Castle sample renders a textured model with correct depth and AgX output at 800x450 on Windows
  hardware and on Linux with lavapipe.
- Two materials over the same internal program with different textures produce **one** draw call
  and two instances, as reported by `RenderStats.DrawCalls`.
- One thousand cubes with the same mesh/program/state produce one instanced draw (or a documented
  device-limit split), not one thousand draws.
- A custom vertex/fragment pair using `main_vertex`/`main_fragment` and the generated ABI compiles
  at bake, loads, and renders.
- A uniform change between draws does not mutate earlier draws.
- Near geometry is closer in reverse-Z depth; a depth-precision probe passes at near `0.01` with an
  infinite far plane.
- On an HDR window, paper white stayed at the reported SDR white level and a scene value above the
  AgX white point reached the reported peak through the M4 highlight stage; the same content on an
  SDR window produced the M2 image. Numeric probes, no golden images. M5 replaced this path; the
  current behavior is in the M5 as-built and
  [Rendering: Camera](api/rendering/camera.md#72-projection-clear-hdr-and-tone-mapping).
- Validation is clean for the gate scene; resize works with the new pipelines.
- Native AOT publish renders the Castle scene on Windows and Linux.

**As-built notes.** M4 shipped the planned scope with the following as-built shape:

- **Bindless table, sampler cache, and pipeline cache.** The renderer allocates a bindless texture
  table of exactly 2048 slots, matching the shader ABI's fixed `RayBorkTextures[2048]` array; slot 0
  is reserved for the built-in 1x1 white texture. A device whose update-after-bind sampled-image
  limits are below 2048 is rejected during device selection, and the capacity is never reduced.
  The array is fixed-size rather than a runtime array: it is the shader ABI's declared resource
  range. Samplers are cached per device and
  keyed by `(filter, wrap, anisotropy)`. Geometry pipelines are cached by the shader-state snapshot
  `(vertex module, fragment module, effective render state, topology)`; the present pipeline and the
  scene target are owned separately by the backend. A texture allocates its slot when its GPU upload
  completes, not at the first draw, and a full table fails that upload with `AssetLoadException`
  `RB1010`.
- **Batch planner, culling, and sorting.** A pass plan groups instances by
  `(mesh, internal shader program, effective render state)`; material instances over one program
  share a batch. A group splits at `MaxInstancesPerBatch` (16,384) or
  `MaxInstanceBytesPerBatch` (1 MiB, including the 96-byte instance header), and each split chunk is
  a separate draw call. Instance bounds are transformed to world-space AABBs and frustum-culled
  against six normalized planes; an instance is culled only when it is fully outside one plane. An
  opaque batch keeps recording order; alpha, additive, and multiply batches are sorted back-to-front
  by nearest instance depth, and their instances are sorted back-to-front too. Sorting is stable for
  equal depths. Stats count draw calls after splitting and instances after culling.
- **Per-instance uniforms and textures.** Every instance record carries the transform, tint, UV
  rectangle, one bindless index per sidecar texture binding, and the reflected uniform values, so
  uniform and texture changes never split a batch. `Draw*` snapshots the shader cursor once per call;
  `DrawInstanced` applies one snapshot to every transform.
- **HDR AgX present (reworked in M5).** M4's initial HDR AgX was the M2 curve plus a separate
  paper-white highlight stage: on an HDR window the scRGB path normalized AgX's neutral white so it
  sat at display white, scaled it to the reported SDR white level, and mapped scene luminance above
  the AgX white point into the reported HDR headroom. `TonemappingMode.None` remained the direct HDR
  authoring path. M5 replaced that entire path with Godot 4.7's HDR-aware AgX curve; the current
  contract is in the M5 as-built and
  [Rendering: Camera](api/rendering/camera.md#72-projection-clear-hdr-and-tone-mapping).
- **Castle AOT gate.** `samples/Castle` and `samples/Castle/Castle.Assets` were the M4
  per-milestone AOT gate. The sample owns `samples/Castle/Castle.slnx`; it is not part of
  `src/RayBork/RayBork.slnx`. The sample build invokes the JIT assets executable out of process
  without the sample's publish settings, bakes `castle.pck` from the checked-in raylib castle OBJ,
  its diffuse texture, and the model sidecar, and copies the pack beside the sample executable; the
  publish also copies it into the publish directory. The published `win-x64` and `linux-x64` hosts
  load the adjacent pack with no `--assets` argument, draw the unlit castle model with the built-in
  mesh shader, and must exit `0` on the Windows hardware driver and on Linux with lavapipe. The
  sample is a plain user-facing example and contains no test-only draw assertion.
- **Castle sample scope.** The sample is an orbitable castle scene with a model, its diffuse
  texture, an orbital camera, the reference XZ grid, and a mouse-hover outline. It deliberately
  leaves out features that need later milestones: model/texture drag-and-drop (post-1.0 platform
  work) and click selection (the sample tests the pointer ray with `Collision.GetRayBox` and
  `Collision.GetRayMesh` and draws `Camera.DrawBoundingBox` plus the nearest hit point, but it has no
  click action and keeps no selection state). The M4 notes originally deferred the
  `DrawText`/`DrawFPS` overlay because runtime text did not exist; the M6 change adds it, so the
  sample now draws a world-space status line with the presented FPS, the tick rate, the render CPU
  time, `RenderStats.DrawCalls`, and `RenderStats.Instances`. The grid is
  `DrawGrid(20, 10f, Color.Gray)` in the same opaque, depth-tested pass as the castle, and the hover
  box and hit sphere join that pass only while the
  pointer is over the model. The model and its diffuse texture are drawn with the built-in
  `/shaders/mesh` material that the loaded model owns; there is no custom Castle shader. The sample
  draws the castle, its grid, the hover overlays, and the status line; the status overlay is
  world-space text and geometry closer than one unit to the camera can occlude it. The castle model
  and texture are sample-only assets with their own notice under `samples/Castle/`; that notice is
  deliberately not duplicated in the repository-root notices.
- **WSL host policy.** WSL is a Linux build environment only. WSLg's hardware Vulkan path (Mesa
  dzn) is not a target and is not part of the gate; the Linux gate uses the conformant lavapipe ICD.
  The fixed `RayBorkTextures[2048]` array is the shader ABI and table capacity, not a
  driver-specific workaround.
- **M4 limitation: one frame block per frame slot.** The renderer writes one 256-byte `rbFrame`
  block per frame slot. A frame that records more than one camera pass overwrites that block for
  each pass, so every pass in that frame sees the last recorded pass' view-projection, view,
  projection, camera position, and viewport. Single-camera frames are correct; split-screen
  cameras and multiple cameras with different views need per-pass frame data, which lands with the
  M8 multiple-camera work. This is the honest M4 limitation, not an M8 feature already shipped.

**Risks.** This is the largest milestone. The shader ABI and per-instance layout can force changes
in the bake, the renderer, and the sample at the same time. Prototype the ABI on two shaders before
building the full batching path. The fixed-size texture array and the 2048-slot table are the
shader ABI; a device below the required bindless limits is rejected during device selection instead
of clamping silently.

### M5 — Primitives, lines, billboards, and collision

**Goal.** Raylib-style 3D drawing helpers, frame-scoped billboards, a wireframe model path, procedural
mesh generation, and the collision queries a small game expects.

**Scope.**

- Built-in primitive meshes and the built-in `/shaders/primitive` program.
- Camera line, point, and triangle helpers: `DrawLine`, `DrawLines`, `DrawPoint`, and `DrawTriangle`.
- Camera shape helpers with shape/position, tessellation, start/end, and `Transform` overloads:
  `DrawCube`, `DrawSphere`, `DrawPlane`, `DrawCylinder`/`DrawWireCylinder`,
  `DrawCapsule`/`DrawWireCapsule`, `DrawCircle`, `DrawTriangleStrip`, `DrawRay`, `DrawBoundingBox`,
  `DrawWireCube`, `DrawWireSphere`, and `DrawGrid`.
- Billboard and textured-quad helpers: full texture, source rectangle, origin pivot, `Transform`, and
  `faceCamera`.
- Wireframe model drawing: `DrawWireModel`, polygon-mode-line geometry, the `fillModeNonSolid` device
  gate, and the `RB3001` unsupported-device path.
- Default vertex-color/unlit shader; per-instance linear tint; per-instance UV rectangle.
- Collision: sphere/sphere, box/box, box/sphere, ray/sphere, ray/box, ray/triangle, ray/quad,
  ray/plane, ray/mesh.
- CPU mesh positions retained for raycasts; bounds for procedural primitives; the deterministic
  ray-mesh BVH and its baked `RBVC` payload.
- `MeshBuilder` mesh factories: poly, plane, cube, sphere, hemisphere, cylinder, cone, torus, and
  knot. Heightmap and cubicmap generation are deferred until a CPU image type exists.
- Runtime tone mapping, the Godot 4.7 HDR-aware AgX, the sRGB color helpers, and the linearized
  Bork2d/Unity preset set.
- Tests for each shape's bounds, winding, draw counts, and collision result.
- The preview screen-space texture helper is not part of M5; the shipped M7 surface uses world-space
  `DrawQuad`/`DrawBillboard` with `ScreenToWorldTransform` instead of a screen-space texture draw.

**Exit criteria.**

- `samples/PrimitiveGallery` draws the M5 primitive helpers in one frame with the built-in shaders.
- Lines render at 1 px without requiring the `wideLines` device feature.
- Wire shapes are line geometry, not polygon-mode dependent; `DrawWireModel` uses a
  polygon-mode-line pipeline gated on `fillModeNonSolid` and fails with `RB3001` on a device without
  the feature (no CPU fallback).
- Every transformable shape family (cube, sphere, plane, cylinder, capsule, circle, wire cube, and
  wire sphere) has its shape/position and `Transform` forms; the tessellated sphere and wire sphere,
  full and wire cylinder, capsule and wire capsule, and circle forms are covered by unit and GPU
  tests. The line, strip, ray, bounding-box, and grid helpers are world-space forms.
- The billboard source-rectangle, origin-pivot, `Transform`, and `faceCamera` contracts are covered,
  including the world-oriented `faceCamera: false` identity case.
- Collision unit tests cover hit/miss, inside-origin rays, parallel rays, degenerate triangles and
  quads, and `GetRayQuad`'s shared-diagonal rule.
- `GetRayMesh` hits a loaded mesh and returns the expected point/normal/distance; the BVH path and
  the brute-force path agree.
- `MeshBuilder.Generate*` produces the documented bounds, winding, normals, and UVs for every
  factory.
- All helpers batch with the same rules as user draws; stats reflect it, and `RenderStats.Triangles`
  still counts recorded triangle primitives for a wireframe model draw.
- Castle uses `DrawGrid` for the raylib reference grid; all sample solutions build.
- The suite is 1,899 tests, 93 of them GPU integration tests.
- Native AOT publish runs the gallery on Windows and Linux.

**As-built notes.** M5 shipped the planned primitive and collision scope, the approved ray-mesh
acceleration structure with its baked payload, the full shape/Transform/billboard/wireframe surface,
the nine `MeshBuilder` generators, the combined HDR AgX rework with runtime tone mapping and sRGB
color helpers, and the restored Bork2d/Unity color preset set:

- **Primitive helper surface.** M5 ships 46 helper overloads over 17 helper families: the four
  line/point/triangle helpers, the twelve shape families (34 overloads including the wire cylinder and
  wire capsule forms), and the eight `DrawBillboard` overloads. `DrawLine` maps the engine's unit line
  mesh from `(0,0,0)`–`(1,0,0)` to the segment. `DrawLines` records one instance per non-zero
  consecutive point pair, so a polyline of `N` points records `N - 1` segments. `DrawPoint` is a
  camera-facing quad of `size` world units, which avoids Vulkan point-size limits. `DrawTriangle` maps
  the unit reference triangle under the orientation-preserving affine map, so a triangle that is
  counter-clockwise as seen by the camera keeps its front face and a clockwise one is culled.
  `DrawTriangleStrip` decomposes `points.Length - 2` triangles with alternating winding and skips
  zero-area triangles. `DrawRay` draws an effectively infinite ray as one 1,000,000-unit segment from
  `ray.Origin` along the unit `ray.Direction`; `Ray` validates a finite origin and a finite, non-zero
  direction and stores it normalized. `DrawBoundingBox` draws the twelve axis-aligned edges and skips
  the coincident edges of a zero-extent box.
  `DrawCube` and `DrawWireCube` scale the unit cube by `size` around `position`, with explicit
  width/height/length and `Transform` forms. `DrawSphere` instances the unit UV sphere (`32` slices by
  `16` rings, no LOD) or composes a tessellated `rings` by `slices` surface
  (`2 * slices * (rings - 1)` triangles); `DrawWireSphere` is three great circles in the simple form
  and `slices * (2 * rings - 1)` line instances in the tessellated form. `DrawCylinder` records
  `sides` side quads plus `2 * sides` cap triangles with the base cap at `position` and the axis along
  world `+Y`; the full form accepts unequal top/bottom radii (a tapered cylinder is one triangle draw
  with `4 * sides` triangles, and a zero radius at one end is a cone with `2 * sides` triangles and
  outward-facing side winding), and the start/end form treats the endpoints as cap centers.
  `DrawWireCylinder` records one vertical edge per side plus one closed ring per non-zero radius.
  `DrawCapsule` is `slices` side quads plus
  `2 * slices * (2 * rings - 1)` cap triangles in two instanced draws; the start/end form treats
  `start` and `end` as spherical cap centers, and the `Transform` form takes the cap-center distance
  as `height`. `DrawWireCapsule` records `slices * (4 * rings + 1)` line instances. `DrawPlane`
  scales the `XZ` unit plane, whose front faces world `+Y`. `DrawCircle` is a closed 36-segment
  circle in the local `XZ` plane with local `+Y` normal. `DrawGrid` is one instanced line draw with
  `2 * (slices + 1)` instances spanning `slices * spacing` per axis. Every helper works in world
  space; each mesh is a unit reference, and the instance transform carries the shape. The eight M5
  meshes are engine-owned, built once before `IGame.Setup` through the procedural GPU bridge from
  deterministic CPU geometry, and are not public assets. M7 adds the unit disc as a ninth mesh. The
  tessellated sphere and wire sphere,
  cylinder and wire cylinder, capsule and wire capsule, and circle compose their geometry from the
  unit quad, triangle, and line meshes.
- **Overloads and Transform composition.** The primitive families use ordinary C# overloads; the
  engine has no `Ex`, `Rec`, `Pro`, or `V` suffix names. Every transformable solid or wire shape
  (cube, sphere, plane, cylinder, capsule, circle, wire cube, and wire sphere) has a shape/position
  overload and a `Transform` overload; at M5 the line, point, triangle, strip, ray, bounding box, and
  grid helpers were world-space draws without a `Transform` form (M7 adds `DrawGrid(Transform, ...)`).
  The transform supplies position,
  rotation, and scale while the shape dimensions stay explicit. The instance matrix is
  `local * transform.ToMatrix()` under the row-vector convention, so the transform scale multiplies
  the dimensions and an identity rotation with unit scale reproduces the simple overload at
  `Transform.Position`. A zero transform scale component records nothing; a negative scale or a
  non-finite transform component is rejected like any other invalid argument. Anchors: the cube,
  wire cube, sphere, wire sphere, plane, and circle are centered at the origin; the cylinder and wire
  cylinder put the bottom cap/ring at the origin with local `+Y` as the axis; the capsule and wire
  capsule are centered with cap centers at `±height/2`. `DrawCylinder(start, end, ...)` and
  `DrawCapsule(start, end, ...)` orient local `+Y` between the endpoints; the axis-angle
  `DrawCircle` rotates the base circle around `rotationAxis` by `rotationAngle` degrees before
  translating it to `center`. The billboard is the one exception: its non-facing `Transform`
  overloads orient the quad in world space while the non-facing position overloads keep the
  camera-relative vertical billboard.
- **Billboard surface.** `DrawBillboard` records one engine quad through `/shaders/mesh` with
  `RenderState.AlphaBlend`. The four-argument form is camera-facing, full-texture, and centered; the
  other seven overloads add a normalized `source` rectangle, a `Transform`, an explicit normalized
  `origin` pivot, or a combination of them. `source` is recorded as the per-instance
  `rbInstance.UvRect = (X, Y, Width, Height)` and the mesh shader maps the authored UVs through it;
  the default `(0, 0, 1, 1)` reproduces them. The rectangle is per-instance data, so billboards with
  different source rectangles still batch together. `origin` places the normalized pivot at the
  transform position and is not clamped; `(0.5, 0.5)` is centered. `faceCamera: true` uses the camera
  basis and only the transform's in-plane roll; `faceCamera: false` on a `Transform` overload orients
  the quad by `local * transform.ToMatrix()` in world space, so an identity transform gives a quad in
  the local XY plane facing world `+Z`, intentionally different from the position overloads' vertical
  billboard. The engine material's texture binding is set immediately before the recorded call and
  cleared immediately afterwards; the frame retains the texture.
- **Wireframe model drawing.** `DrawWireModel` is the raylib `DrawModelWires`/`DrawModelWiresEx`
  equivalent. It records exactly the parts, material snapshots, render states, tint, and
  `local * transform` matrices of `DrawModel`, and sets an internal wireframe flag on each draw
  record. The renderer creates the geometry pipeline with `VK_POLYGON_MODE_LINE` and
  `LineWidth = 1`; culling, depth test, depth write, blending, tint, textures, and alpha are
  unchanged, so back faces stay culled and the visible edges of front-facing triangles are drawn. A
  triangle split into two triangles also draws its shared diagonal. The flag is part of the batch key
  and the geometry pipeline cache key, so a wireframe record never merges with a filled record over
  the same mesh, program, and state. `Wireframe` is internal; `RenderState` stays the public
  depth/culling/blending contract and there is no public wireframe flag. The polygon-mode-line
  pipeline needs the core `fillModeNonSolid` feature, enabled at device creation when the physical
  device advertises it. A device without it fails the frame with `GraphicsException` `RB3001` naming
  the device and the feature; M5 has no CPU line-geometry fallback. `RenderStats.Triangles` still
  counts the recorded triangle primitives, because the polygon mode changes rasterization, not the
  record's topology or index count.
- **Programs and render states.** Shapes and lines record against the built-in `/shaders/primitive`
  program, which consumes vertex position and linear color only and has no uniforms or textures; its
  vertex stage multiplies the vertex color by the per-instance tint and its fragment stage writes
  that color. Its default state is opaque, back-face culling, `greater_equal`, depth write. The
  billboard uses the built-in `/shaders/mesh` program with the `RenderState.AlphaBlend` state
  override. The engine material's texture binding is set only around the recorded call and cleared
  immediately afterwards, and the frame record retains the texture. At M5, `EngineDefaults` exposed
  only `CreateMeshShader`, `White`, and `Error`; M6 adds `DefaultFont` and `DefaultMonoFont`.
  `/shaders/primitive` is an ordinary
  built-in shader entry, so a game can load a material over it by path for its own vertex-color
  meshes; the primitive meshes have no public accessor. The preview screen-space texture helper is
  not built or shipped in M5; M7 ships no `DrawTexture`, and the screen-space pattern is
  `ScreenToWorldTransform` plus world-space primitives.
- **Validation.** A non-finite numeric argument throws `ArgumentException`; a negative extent,
  radius, height, width, point size, or transform scale component throws
  `ArgumentOutOfRangeException`; an exactly zero extent, radius, height, start/end segment, or
  transform scale component records nothing. `sides < 3`, `rings < 2`, `slices < 3`, `slices < 1`
  for `DrawGrid`, and `spacing <= 0` throw `ArgumentOutOfRangeException`, and no helper substitutes a
  default tessellation. A zero `rotationAxis` is `ArgumentException`. A null texture is
  `ArgumentNullException`; a disposed texture, camera, or primitive asset is
  `ObjectDisposedException`. A billboard source rectangle with a non-finite component is
  `ArgumentException`; a non-positive width or height, or a rectangle outside `[0, 1]`, is
  `ArgumentOutOfRangeException`. A non-finite billboard origin is `ArgumentException`; the origin is
  not clamped. A missing primitive-asset set is `InvalidOperationException`, and every helper is
  tick-thread only. Colors are linear `Color` values; the per-instance tint is `rbInstance.Color`, and
  the shader multiplies it into the vertex color. Validation runs before any retention or record
  mutation, so a rejected call leaves the frame packet untouched.
- **Batching and statistics.** Helpers record ordinary draw records, so they batch under the existing
  `(mesh, program, effective render state, polygon mode)` key and follow the same culling, sorting,
  splitting, and stats. Line topology contributes zero triangles; `DrawPoint` is a camera-facing
  quad, so it contributes the quad's two triangles. `DrawLines`, `DrawGrid`, `DrawCircle`,
  `DrawRay`, `DrawBoundingBox`, and the wire helpers are one instanced line draw each.
  `DrawCylinder` is two instanced draws for equal positive radii (side quads and cap triangles)
  because the two shapes are different meshes; a tapered cylinder is one triangle draw.
  `DrawCapsule` is two instanced draws. Equal records merge: two `DrawCube` calls are one instanced
  draw with two instances. The billboard's render state differs from the primitive program's state,
  so it never merges with shape draws; reusing the same program does not merge different meshes.
- **Collision contract.** `Collision.CheckSpheres`, `CheckBoxes`, and `CheckBoxSphere` are inclusive
  and allocation-free: touching counts, a zero radius is a point, and a negative radius or a NaN
  input reports no collision. Ray queries return the nearest `t >= 0`; an origin inside a sphere or
  box returns the exit point with the outward normal. Triangle queries are double-sided and report
  the normalized geometric normal, which is not flipped toward the ray; a degenerate triangle is a
  miss. `GetRayQuad` splits a quad into the triangles `(a, b, c)` and `(a, c, d)`, reuses the
  double-sided triangle test, returns the nearest non-negative hit, and reports the `(a, b, c)` result
  on the shared diagonal; a degenerate quad is a miss. A ray/plane query normalizes the plane normal
  and misses when the ray is parallel, including when it lies in the plane. `Ray.Direction` is unit
  length, so `RayCollision.Distance` is a world distance, and a miss is `default(RayCollision)`;
  `default(Ray)` has a zero direction and every ray query misses. `GetRayMesh`
  supports triangle lists and strips, throws `NotSupportedException` for line and point topologies,
  `ArgumentException` for a singular transform, and `ArgumentNullException`/`ObjectDisposedException`
  for the mesh; it maps the hit through the row-vector mesh-to-world transform, uses the
  inverse-transpose for the normal, and keeps the nearest hit. The collision helpers never throw for
  numeric input and are safe on any thread.
- **Ray-mesh acceleration (the approved scope addition).** The milestone scope retained CPU triangles
  for raycasts; the shipped implementation also builds a deterministic binary AABB BVH with a binned
  surface-area heuristic (12 bins), leaves of at most four triangles, 32-byte nodes, and a maximum
  depth of 64. The structure is built from the retained CPU triangles only and is immutable and
  thread-safe: a query uses an iterative near-first traversal with a `t` early-out and allocates
  nothing beyond a `stackalloc` traversal stack. `Collision.GetRayMesh` traverses the structure when
  the mesh has one and brute-forces the retained triangles otherwise; both paths select and report
  the same nearest triangle. `Mesh.HasCollisionAcceleration` reports whether a mesh has one and
  throws after disposal; there is no public BVH type and no query over the structure beyond
  `GetRayMesh`.
- **Baked collision payloads.** The model processor writes a sibling raw entry
  `<mesh path>-collision` for every triangle part by default and records it in the optional
  `MeshAssetDescriptor.CollisionPayload` member of that mesh's descriptor. A `[model]` sidecar with
  `collisions = false` opts the whole source out; line and point parts and parts with no
  non-degenerate triangle get no entry. A payload above 4 MiB emits an `RB4009` warning that names
  the part and the opt-out. The payload is the `RBVC` format v1: a 24-byte little-endian header
  (magic `RBVC`, format version, topology, node count, triangle-id count, canonical triangle count),
  then 32-byte node entries (six floats and two 32-bit integers) and 32-bit triangle ids, written
  with 16-byte alignment as a raw entry. The loader decodes the descriptor, decodes and validates the
  payload, and only then queues the GPU upload; a missing entry, a corrupt payload, a topology
  mismatch, or a canonical-count mismatch fails the mesh load with `AssetLoadException`
  `ASSET_LOAD_FAILED` naming the mesh. A descriptor baked before the member existed has no
  `CollisionPayload`, loads with no structure, and brute-forces. The incremental cache is invalidated
  by the built-in processor configuration marker, so an old bake cache is not reused for output that
  gained collision payloads. The payload is CPU-only: it does not affect GPU resources, frame
  retention, device recovery, or the batching path.
- **Procedural opt-in.** `MeshBuilder.GenerateCollisions()` is the procedural opt-in. It is an
  imperative builder step like `ComputeNormals`, does not mutate geometry, is idempotent, and
  persists for every later build from that builder. `BuildAsync` builds the structure from the same
  snapshot it validates and uploads, before the GPU upload, and exposes it through
  `Mesh.HasCollisionAcceleration`. The method and the build accept triangle topologies only and
  throw `NotSupportedException` otherwise. A snapshot with no non-degenerate triangle builds without
  a structure, and a failed or canceled build creates no asset and leaves the builder reusable.
- **MeshBuilder generators.** The nine `Generate*` factories are the procedural counterparts of
  raylib's `GenMesh*`: `GeneratePoly` (XZ disc), `GeneratePlane` (XZ grid), `GenerateCube`,
  `GenerateSphere` (UV sphere), `GenerateHemiSphere` (+Y half), `GenerateCylinder`, `GenerateCone`,
  `GenerateTorus`, and `GenerateKnot` (trefoil tube). Each replaces the builder's vertices and
  indices, sets `Topology` to `TriangleList`, discards explicit bounds, and fills outward normals and
  the first UV set directly; every triangle winds counter-clockwise around its outward normal.
  Tangents are left to `ComputeTangents`, and `BuildAsync` uploads the generated geometry through the
  normal procedural path. The methods are pure CPU and validate their arguments before replacing any
  geometry: `sides`, `rings`, `slices`, and `radSeg` are at least 3, `resX`/`resZ` are at least 1, and
  every dimension, radius, and size is positive and finite. Heightmap and cubicmap generation are
  deferred until the engine has a CPU image type.
- **HDR AgX rework.** M2 shipped the first AgX curve without HDR headroom and M4 added a separate
  paper-white highlight stage; M5 replaces both the M2/M4 curve and that stage with Godot 4.7's
  HDR-aware AgX. The present fragment shader applies the combined Rec.709 to Rec.2020 and AgX inset
  matrix, the allenwp curve (crossover `0.18`; `toe_a`, `slope`, and `w` derived on the CPU), a clamp
  to the output maximum, and the combined inverse outset and Rec.2020 to Rec.709 matrix. The combined
  matrices preserve neutral gray, so a neutral scene color stays neutral. SDR uses an output maximum
  of `1.0`; the scRGB path uses the reported display headroom as the curve's output maximum, so the
  curve writes paper-white units up to the display peak and the pass then multiplies by paper white.
  The old highlight stage, its scene/output white-luminance shader constants, and the old curve are
  gone; `TonemappingMode.None` remains the direct path. The shader's curve is mirrored by the CPU
  reference `AgxToneMap` with `AgxCurveParameters`, covered by pinned unit tests; the renderer never
  calls the reference per frame.
- **Runtime tone mapping.** `EngineContext.Tonemapping` exposes the app-lifetime
  `TonemappingSettings`: `Exposure` in stops, `AgxWhite` (default `16.29`), and `AgxContrast`
  (default `1.25`). `WithExposure`, `WithAgxWhite`, and `WithAgxContrast` set the startup defaults;
  `WithTonemapping` still fixes the mode at startup. Every member is tick-thread only; the tick
  thread snapshots the values into the frame packet at `CameraService.EndFrame`, the values in effect
  when the frame is sealed apply to that frame's present, and the render thread never reads the live
  settings object. The scene frame block's exposure field is written from the same snapshot.
- **sRGB color helpers and the restored preset set.** `Color.FromSrgb(float r, float g, float b,
  float a = 1f)`, `Color.FromSrgbBytes(byte r, byte g, byte b, byte a = 255)`, and `Color.ToSrgb()`
  convert between display-encoded sRGB and linear `Color` values. The full 158-color Bork2d/Unity
  preset set is restored with its Bork2d names, and every preset is defined from its authored sRGB
  source through the same piecewise EOTF as `FromSrgb`, so the type stays linear. The corrected
  display values are `Color.Gray`/`Color.Grey` at linear `0.214041`, `Color.WhiteSmoke` at
  `0.913099`, `Color.Yellow` at `(1, 0.8307699, 0.0012141)`, and `Color.DarkCyan` at
  `(0, 0.2581829, 0.2581829)`; the primaries and `Clear`/`Transparent` are unchanged, `Grey` aliases
  `Gray`, `Transparent` aliases `Clear`, and `YellowNice` follows the new `Yellow`.
  `Color.WhiteSmoke` is the Castle background. The Castle and `PrimitiveGallery` samples draw named
  presets instead of hand-written color constants. The `Gray`/`Grey`, `Yellow`, and `DarkCyan` values
  are breaking 0.x preset changes relative to the earlier linear constants; the migration note is in
  [Versioning](api/versioning.md#14-versioning-and-compatibility).
- **Primitive gallery AOT gate.** `samples/PrimitiveGallery` is a single-project self-contained
  solution, `samples/PrimitiveGallery/PrimitiveGallery.slnx`, outside `src/RayBork/RayBork.slnx`. It
  has no assets executable and no pack; `Setup` builds its one `128x128` `Rgba8Srgb` checker texture
  with `CreateTextureBuilder`. It draws the M5 primitive helpers in one perspective pass at 800x450,
  uses orbit controls, and accepts `--frames` and `--help`. The published `win-x64` and `linux-x64`
  hosts run from the publish directory with zero AOT/trim warnings, and the publish directory contains
  no bake tooling. The sample is a plain user-facing example and contains no test-only draw
  assertion. The M5 suite is 1,899 tests, 93 of them GPU integration tests, and every sample solution
  builds.
- **Preview screen-space helper.** The preview `DrawTexture` overlay helper was removed in M5 and
  did not return in M7. M7 ships no `DrawTexture` and no screen-space draw overload; a sprite is a
  textured `DrawQuad` or `DrawBillboard(faceCamera: false)` on a `ScreenToWorldTransform` plane. The
  migration note is in [Versioning](api/versioning.md#14-versioning-and-compatibility).
- **Expected validation warnings.** The validation layer reports two performance warnings on the
  gate path; neither is an error and neither fails the gate. The fixed vertex layout declares
  attributes a shader does not consume, and the primitive program's vertex stage writes the engine
  instance-index varying that its fragment stage never reads, because that stage needs no
  per-instance data. The draw tests assert that no validation *errors* are recorded.
- **Castle and the M4 forward references.** `samples/Castle` keeps its orbitable scene and adds
  `DrawGrid(20, 10f, Color.Gray)` under the model in the same opaque, depth-tested pass, so the
  reference XZ grid is no longer deferred. Two M4 forward references are corrected: the grid ships in
  M5, and `DrawBoundingBox` now ships as a public M5 helper. The Castle sample uses the helper for its
  hover overlay: it tests the pointer ray against the model bounds with `Collision.GetRayBox`, draws
  the bounds on a hit, and marks the nearest `Collision.GetRayMesh` hit. It still does not implement
  click selection. M6 adds the presented-FPS/render-statistics status overlay the sample deferred in
  M4, and M7 draws it through `ScreenToWorldTransform` so it stays screen-aligned. Castle remains a
  buildable and publishable sample; the M6 AOT gate was `samples/Text` and the M7 gate is
  `samples/Pong`. The M4
  limitation of one `rbFrame` block per frame slot is unchanged by M5-M7.

**Risks.**

- **Wireframe device support.** `DrawWireModel` is the only M5 path that needs the core
  `fillModeNonSolid` feature. A device without it fails the frame with the documented `RB3001`
  unsupported-device error instead of drawing nothing, and M5 has no CPU line-geometry fallback.
  Filled model draws and the primitive wire shapes are unaffected; the GPU suite covers the pipeline
  split, and a host that cannot exercise the line pipeline reports the test as a skip with its
  device name.
- **HDR10/PQ is deferred.** HDR presentation is scRGB float with the reported display headroom;
  `A2B10G10R10_UNORM_PACK32` with PQ output is never selected in M5. A display that only accepts PQ
  HDR falls back to the SDR order and reports `HdrOutput = false`.
- **SDR AgX differs from M4.** The HDR-aware curve changes the SDR image at the same exposure, so
  content tuned against the M4 curve may need a new exposure or AgX white. The behavior is measured
  by the pinned CPU-reference and presented-pixel tests, not by golden images.
- **Preset values change.** The sRGB-authored palette moves `Gray`/`Grey` to linear `0.214041`,
  `Yellow` to `(1, 0.8307699, 0.0012141)`, and `DarkCyan` to `(0, 0.2581829, 0.2581829)`. Code that
  used those presets as raw linear constants must switch to an explicit `Color` constructor or accept
  the display-authored value; see the migration note in
  [Versioning](api/versioning.md#14-versioning-and-compatibility).
- **Collision memory.** The collision structure is CPU memory retained for the mesh lifetime, about
  20 bytes per non-degenerate triangle at a leaf size of four (32-byte nodes plus the canonical
  triangle ids). The `collisions = false` sidecar opt-out avoids that bake cost, and a mesh without a
  structure still queries correctly by brute-forcing the retained triangles.

### M6 — Text and runtime MSDF

**Goal.** World-space text that looks correct at arbitrary sizes, with no pre-baked atlas and no
blocking glyph generation.

**Scope.**

- Managed sfnt face parsing: table directory, `head`/`maxp`/`hhea`/`hmtx` metrics, cmap formats 0, 4,
  6, and 12, TrueType `glyf` simple and composite outlines, CFF 1.0 Type 2 charstrings including
  CID FDArray/FDSelect and `seac`, `kern` format 0 and GPOS PairPos 1/2 with extension lookups,
  `.notdef`, defensive limits, and fuzzing against hostile input.
- Deterministic runtime MSDF: one padded texture per glyph with a 32-pixel longest side, an authored
  distance range and padding, msdfgen-style two-channel edge colouring, a normalized field where
  `0.5` is the edge, derivative-based screen-space antialiasing in the shader, and analytic
  validation of the generated field.
- Glyph pipeline: an app-lifetime `RayBork.Font` thread that generates and uploads glyphs
  asynchronously, a tick thread that never waits for glyph generation, a per-font hard cache with LRU
  eviction, and frame holds so an eviction cannot destroy a texture a recorded frame still
  references.
- The CPU-only `Font : IAsset` and `Glyph` contract: `Load<Font>` parses the face and generates no
  glyph texture, `TryGetGlyph`, `Measure`, unmapped-rune fallback to `.notdef`, and `RB1011` for an
  invalid or unsupported face. There is no `Preload` and no font preload range.
- Built-in Fira Sans Regular at `/fonts/fira-sans`, exposed as `EngineDefaults.DefaultFont`, and the
  built-in JetBrains Mono variable face at `/fonts/jetbrains-mono` (default instance Regular), exposed
  as `EngineDefaults.DefaultMonoFont`; the `[font]` sidecar carries only the MSDF settings.
- Layout: advance, kerning, line breaks, line height, `TextAlignment`, `TextWrapping`, `maxWidth`,
  and `Measure`; the `DrawText` origin is the top-left of the first line's ascent box.
- Drawing: `Camera.DrawText` in a transform's local XY plane and `Camera.DrawTextBillboard` in a
  camera-facing plane, through the engine quad and the built-in `/shaders/msdf` program, with one
  draw record per call, one instance per ready glyph, and a per-instance bindless glyph texture.
- `samples/Text` as the M6 sample and Native AOT gate, with `Text.Assets` baking `text.pck` from the
  checked-in Lora face; `samples/Castle` and `samples/PrimitiveGallery` gain the presented-FPS text
  status overlays they had deferred, drawn at 18 px in the built-in mono face.
- Tests for parsing, layout, MSDF, the glyph pipeline, the font asset, text draws, and fuzzing.
- **Out of scope for v1:** complex shaping, ligatures, bidi ordering, font fallback, color fonts,
  emoji, variable font axes, and CFF2 outlines.

**Exit criteria.**

- `samples/Text` renders the same paragraph at multiple sizes, rotations, alignments, and wrapping
  modes, with Latin and Cyrillic paragraphs in the built-in Fira Sans and custom Lora faces, a
  camera-facing billboard, lazily appearing Greek glyphs, a mono status line, an orbit camera, and
  `--frames`/`--help`.
- Glyphs remain sharp at large sizes and readable at small sizes with one padded texture per glyph
  and no atlas seams; the generated MSDF field matches the analytic distance within the documented
  tolerance.
- A glyph generated lazily during a tick appears on a later frame without corrupting the frame; the
  tick thread never blocks on glyph generation, and the per-font texture cache never exceeds its
  budget.
- Kerning from `kern`/GPOS matches the face metrics within tolerance, and a face with thousands of
  mapped glyphs loads without generating a glyph texture; only the glyphs a draw requests are
  generated, up to the per-font cache cap.
- `Load<Font>` is CPU-only and generates no glyph texture; an unmapped rune falls back to `.notdef`;
  an invalid face is `RB1011`; a pre-M6 pack still loads, while an old TOML `ranges` key is
  `RB4002`.
- Both AOT publishes (`win-x64` and `linux-x64`) finish with zero warnings and both published hosts
  run `--frames 30` to exit `0` (Windows hardware and Linux lavapipe).
- Fuzz tests cover malformed sfnt/cmap/glyf/CFF/kern/GPOS tables without crashes or unbounded
  allocation.
- The M6 suite was 2,625 tests, 106 of them GPU integration tests, and every sample solution built.

**As-built notes.** M6 shipped the planned text surface with the following as-built shape:

- **Managed font parser.** `SfntFace` parses the sfnt container and table directory, requires
  `head`, `maxp`, `hhea`, `hmtx`, and `cmap`, and resolves TrueType `loca`/`glyf` or CFF 1.0. Cmap
  selection is deterministic over formats 0, 4, 6, and 12; a Unicode subtable wins over a symbol or
  legacy one, and `GlyphCount` counts mapped codepoints rather than distinct glyph ids. TrueType
  simple and composite glyphs are supported, with point-matched composite arguments rejected as
  unsupported and a recursive-component budget. CFF Type 2 charstrings resolve through the CID
  FDArray/FDSelect path and the deprecated `seac` composition; CFF2 is parsed structurally and then
  rejected at load. Kerning comes from `kern` format 0 or GPOS PairPos format 1/2 including
  extension lookups. The parser enforces defensive caps (16 composite levels, 20,000 points per
  glyph, 4,096 component resolutions, 1,000,000 `kern` pairs, bounded GPOS lookup/pair-set records,
  and CFF/interpreter budgets) and fuzzing asserts that malformed input raises `FontFormatException`
  (malformed) or `FontUnsupportedException` (valid but outside a documented limit), never a
  framework exception, within a 16 MiB allocation bound per parse. Ligatures, contextual shaping,
  bidi, color fonts, and emoji are not implemented. A variable face loads and renders its default
  instance; named axes are not exposed.
- **Deterministic runtime MSDF.** One glyph is one padded texture, not an atlas entry. The authored
  `msdf_pixel_size` is the complete padded texture side, so the default `32` is the longest side of
  the final texture; `msdf_range` defaults to `4` and `msdf_padding` to `2`, and the layout fits the
  glyph box into `pixelSize - 2 * padding` so the outline keeps the empty border. Edge colouring
  follows the msdfgen-style two-channel scheme; the field is normalized with `0.5` as the edge and
  values above `0.5` inside, and the alpha channel is opaque. Generation is deterministic:
  repeated calls on the same outline produce byte-identical buffers. The fragment shader
  reconstructs the edge from the median of the three sampled distance channels and derives its
  antialiasing band from `fwidth`, so there is no range, pixel-size, or per-font uniform. The field
  is the exact nearest-edge distance per channel and deliberately does **not** extend edges past
  their endpoints (no pseudo-distance corner extension), so bilinear filtering can round a sharp
  corner at very large sizes; this is the documented quality limitation. The generator caps one
  glyph at 4,194,304 texels and 8,388,608 edge-texel pairs and validates its layout arguments.
  Analytic and generated-field tests pin the distance values, edge colouring, determinism, empty and
  degenerate layouts, and the limits.
- **Glyph pipeline and cache.** The asset catalog starts one app-lifetime `RayBork.Font` thread with
  its loader thread, before the tick thread starts, and stops it during `AssetCatalog.Stop` while
  the render thread still services the GPU upload bridge. `Load<Font>` parses the face and creates a
  `FontGlyphPipeline`; it never generates a glyph. The tick thread calls `BeginFrame` once per
  camera and `Acquire` per laid-out glyph with a positive size. A miss marks the glyph pending,
  records a waiter, enqueues exactly one generation request, and returns no texture; the draw skips
  that glyph for the call while the pen still advances, and the glyph appears on a later frame. A
  hit adds the texture's frame hold to the recorder's retention set while the cache lock is held, so
  eviction cannot release the owner reference between the lookup and the hold. The per-font cache is
  hard-capped at 512 textures and evicts the least-recently-used entries in one batch on insert; a
  glyph texture is an ordinary procedural `Rgba8Unorm`, linear, clamp-sampled texture, so frame
  retention and device-recovery re-upload use the same path as any other texture. Generation and
  upload failures are terminal for the glyph, logged once, and never retried; an empty glyph such as
  a space becomes ready without a texture; a font-data failure falls back to the face advance with a
  zero quad and is logged once.
- **Font asset and pack contract.** `Font` is a shared, reference-counted `IAsset` and the engine's
  first CPU-only engine asset: it has no GPU resource of its own, so a load queues no upload and the
  last release disposes the glyph pipeline and its cached glyph textures. `Load<Font>` parses the
  whole packed face once; a repeated load returns the same object and its live glyph cache. A font
  descriptor carries the MSDF settings and the sibling `<base>-face` raw payload. The pre-M6
  descriptor member `Ranges` is gone: a pre-M6 pack still loads because MessagePack ignores the
  unknown member, while an old TOML sidecar that still declares `ranges` fails the bake with
  `RB4002`. An unavailable or wrong-length face, an invalid face, CFF2, an unsupported parser
  feature, or invalid MSDF settings is `AssetLoadException` `RB1011`. `Glyph` values are in em
  units; `Advance` is the horizontal pen advance, `Bearing` is the padded quad's bottom-left corner
  on the baseline, `Size` is the padded quad size, `Uv0`/`Uv1` are `(0, 0)`/`(1, 1)` because one
  glyph is one whole texture, and `Rune` echoes the requested rune. `TryGetGlyph` returns only
  glyphs whose texture is ready and does not generate one; `Measure` reads face metrics and kerning
  without generating a texture. An unmapped rune falls back to glyph id 0 (`.notdef`), so layout
  still advances.
- **Built-in fonts.** The built-in pack carries Fira Sans Regular version 4.203 at
  `/fonts/fira-sans` with its `/fonts/fira-sans-face` payload and the JetBrains Mono variable face
  version 2.211 at `/fonts/jetbrains-mono` with its `/fonts/jetbrains-mono-face` payload, produced by
  the ordinary font processor with the default 32/4/2 MSDF settings, so the pack has 20 entries. The
  bundled JetBrains Mono face's default named instance is Regular (weight 400); that is the instance
  the runtime renders. Fira Sans and JetBrains Mono are licensed under the SIL Open Font License 1.1
  and cover Latin, Cyrillic, and Greek, which is the coverage the samples exercise.
  The app preloads the faces before `IGame.Setup` and exposes them as
  `EngineContext.Defaults.DefaultFont` and `EngineContext.Defaults.DefaultMonoFont`; the fonts are
  engine-owned, the preload is CPU-only and queues no GPU upload, and the owner references are
  released during shutdown. Both are recorded in the bundled-assets section of
  [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md). The sample's
  Lora face is sample-only and keeps its own notice under `samples/Text/`.
- **Layout.** The layout engine is pure CPU and reads `ITextMetrics`; it allocates nothing once the
  caller's buffer has grown. `'\n'` starts a new line and `'\r'` is ignored. Advances and kerning
  come from the face, line height is `(ascender - descender + lineGap) / unitsPerEm`, and
  `TextAlignment` shifts each line inside the layout box. `TextWrapping.Word` breaks only before a
  whitespace run that follows a word and drops that run; a word wider than the box overflows on its
  own line. `TextWrapping.Character` breaks between any two glyphs and never drops a rune. `Word`
  and `Character` require a positive `maxWidth`; `None` with `maxWidth == 0` sizes the box to the
  widest line. `Measure` ignores wrapping and a width limit and returns the widest line advance plus
  `lineCount * LineHeight * size`. The layout origin is the top-left of the box; the first baseline
  is at `-Ascent * size` and the block bounds include every line box and glyph quad.
- **Text drawing.** `Camera.DrawText` records world-space text through the engine quad and the
  built-in `/shaders/msdf` program. The block lies in the transform's local XY plane, with `+X` to
  the right, `+Y` up, the readable face toward local `+Z`, and the origin at the top-left of the
  first line's ascent box. `Camera.DrawTextBillboard` uses the same layout with `TextWrapping.None`
  and no width limit, centers the union of the layout box and every glyph quad on `position` in a
  camera-facing plane, and aligns each line inside the block width. One call records at most one
  draw record; every ready glyph is one instance in layout order with its own bindless glyph texture,
  and the per-instance UV rectangle stays `(0, 0, 1, 1)`. The text render state is alpha blend, no
  culling, `greater_equal`, no depth write (M10 changes the default to an alpha-to-coverage cutout).
  Instances of the same quad and program batch across
  calls, fonts, sizes, and strings; `Instances` counts submitted glyph instances and `Triangles` is
  two per instance. A call whose glyphs are all pending or empty records nothing. A glyph texture
  the draw acquired is retained by its frame hold rather than again by the draw record. Validation
  runs before any retention or record mutation: a null font is `ArgumentNullException`, a disposed
  camera or font is `ObjectDisposedException`, non-finite or out-of-range layout arguments are
  `ArgumentException`/`ArgumentOutOfRangeException`, and a zero transform scale or empty text records
  nothing.
- **Text sample and AOT gate.** `samples/Text` was the M6 sample and Native AOT gate. Its solution
  `samples/Text/Text.slnx` contains the sample and the JIT `Text.Assets` executable, which bakes
  `text.pck` from the checked-in Lora Regular 3.008 face and copies it beside the sample
  executable; the sample has no `--assets` argument. It draws text in the built-in Fira Sans and
  JetBrains Mono faces and the baked Lora face: a title in each proportional face, one paragraph at
  three sizes, three alignments, both wrapping modes, and two rotations, Latin and Cyrillic paragraphs
  in both proportional faces, a camera-facing billboard, and a Greek line that gains one lazily
  generated glyph every 30 ticks. A fixed right-aligned status block in the built-in mono face shows
  presented FPS, the tick rate, the render CPU time, and draw calls and instances from `RenderStats`;
  the sample has orbit/zoom controls and the `--frames`/`--help` options. `samples/Castle` and
  `samples/PrimitiveGallery` gained the `DrawText` status overlays they had deferred, so they draw
  the presented FPS, the tick rate, the render CPU time, and draw calls and instances from
  `RenderStats`; M7 anchors those lines with `ScreenToWorldTransform`. The published `win-x64` and
  `linux-x64` hosts finish with zero AOT/trim warnings, contain `text.pck` but no bake tooling, and
  both run `--frames 30` to exit `0`; the Linux run used the conformant lavapipe software driver.
- **Presentation pacing and truthful diagnostics.** The samples set no tick target and request FIFO.
  In FIFO mode the tick thread waits until the render loop finishes with the frame it published
  (normally its presentation), so one tick maps to one presented image and the tick rate follows the
  display refresh; a dropped or failed frame also finishes the wait. MAILBOX and IMMEDIATE keep the
  tick decoupled and the renderer presents the newest completed frame. `RenderStats.PresentedFrames`
  counts successful presentations since startup, and the samples label `EngineContext.Fps` as
  `tick` and show the presented rate separately from a one-second delta of that counter. The ring's
  completion mark is a pacing signal, not a successful-presentation count.
  `RenderStats.CpuFrameMilliseconds` now measures only planning, recording, and submission: it
  starts after the frame fence wait and the swapchain acquire, so a vsync or acquire stall is no
  longer reported as CPU frame time.
- **Measured text-path cost.** The Text sample records 1,196 glyph instances in one draw call.
  Measured on the reference machine, its tick-thread recording work (layout, metrics lookup, cache
  acquire, and frame retention) is about **10-13 ms per tick in Debug** (~80 ticks/s) and about
  **2.7-3 ms per tick in Release** (~300 ticks/s); the display refresh was 144 Hz, so the Debug tick
  rate, not vsync, was the observed limit. The render thread's plan, record, and submit is about
  1-2 ms, and the GPU is not the bottleneck. These are measured observations, not targets. The
  per-glyph text recording path is the cost, and optimizing it is deferred to the M10 optimization
  milestone.
- **Tests.** At the M6 milestone the suite was 2,625 tests, 106 of them GPU integration tests; the
  current suite, including the M7 screen-space suites, is 2,695 tests, 114 GPU integration tests.
  M6 adds the font parser,
  cmap, metrics, CFF, kerning, and fuzz suites; the MSDF generator, layout, analytic-field, and edge
  colouring suites; the glyph pipeline cache, generation service, settings, and font asset suites;
  the text layout, measurement, wrapping, buffer, and validation suites; the camera text draw
  records and the `TextDrawGpuTests` presented-pixel and statistics suite; and the frame-ring
  presentation-wait, app FIFO/Mailbox pacing and mode-change, render-loop failure/drop marking,
  presented-frame counter and recovery/suboptimal result, and acquire-excluded CPU timing suites.
  The two AOT publishes cover the Text sample on both RIDs. There is no golden-image suite.
- **Limitations.** There is no complex shaping, ligature substitution, bidi ordering, font fallback,
  color font, or emoji support. Coverage is per face: Fira Sans and JetBrains Mono cover Latin,
  Cyrillic, and Greek.
  Variable fonts load their default instance. CFF2 is rejected. One texture per glyph consumes
  bindless slots: 512 cached per font, within the fixed 2048-slot table. There is no preload and no
  synchronous glyph generation. Text is world-space geometry; M7 adds the screen-space pattern
  through `ScreenToWorldTransform` and the world-space `DrawText` overload, not a screen-space draw
  API. MSDF sharp
  corners are not extended, so a small artifact can appear at very large sizes. A status overlay on
  a screen-aligned plane can still be occluded by geometry closer than that plane, and the separate
  overlay pass is M8. M6 ships text as
  alpha-blended (derivative antialiasing, transparent batch, no depth writes); M10 makes the
  alpha-to-coverage cutout the default so text needs no sorting and resolves overlapping glyphs by
  depth, keeping the alpha path for translucent text.

**Risks.**

- **The managed parser and MSDF generator shipped without a native fallback.** The highest-risk
  runtime libraries are now the shipped implementation, not a prototype: quality, hostile-input
  behavior, and performance are the runtime's own. The mitigation is the defensive caps, the
  format/unsupported split, the deterministic generator, and the fuzz and analytic suites; a native
  `msdfgen`/FreeType path stays a possible later change, not a hidden dependency.
- **MSDF corner quality.** The generator does not extend edges past their endpoints, so a sharp
  corner can round slightly under bilinear filtering at large sizes. The field is otherwise exact at
  texel centers; a future generator change would need the same analytic coverage.
- **Bindless slot pressure.** One texture per glyph means a glyph cache is also a descriptor budget:
  512 textures per font against the fixed 2048-slot table. A scene that keeps many fonts alive
  concurrently can exhaust the table, which fails the upload with `RB1010`; the per-font LRU bound
  and the existing bindless table contract are the mitigation.
- **Coverage and feature gaps.** There is no fallback, shaping, bidi, color font, emoji, or variable
  axis support, and coverage is per face. A game that needs those features must pre-process its text
  or wait for post-1.0 work; the samples state the per-face boundary honestly.
- **World-space text.** M7 adds `ScreenToWorldTransform` so a text block can be anchored to a screen
  pixel on a camera-facing plane, but the draw is still world-space geometry. The sample overlays
  are alpha text on that plane, so geometry closer than it can occlude them; a separate overlay pass
  is M8 work, not an M7 feature already shipped.

### M7 — Orthographic rendering and the screen-space surface

**Goal.** Raylib-style 2D games are practical without a separate 2D renderer.

**Scope.**

- Unified camera creation: `EngineContext.CreateCamera` takes a `CameraProjection` and either a
  `fieldOfView` (perspective) or an `orthographicHeight` (orthographic); `CreateOrthographicCamera`
  is removed. The perspective default `far` becomes `1000`, and a caller that needs an infinite far
  plane passes `float.PositiveInfinity` explicitly; an orthographic `far` stays finite.
- `Camera.ScreenToWorldTransform(Vector2 point, float distance)`: viewport-relative render pixels
  with a top-left origin, a finite positive view-depth `distance`, and a returned `Transform` whose
  position is the world point on that plane, whose rotation is the camera rotation, and whose
  uniform scale is world units per render pixel. It works for perspective and orthographic cameras,
  and the camera transform must have unit scale.
- 2D primitives as world-space draws: a solid quad, a full-texture quad, and a normalized
  source-rectangle quad; a disc; and a transformable grid. The effective render state is part of the
  batch key, and `RenderState.AlphaBlend` is the sprite/alpha case.
- Screen-space text through `ScreenToWorldTransform` and the world-space `DrawText` `Transform`
  overload. There is no `DrawText(Vector2)` overload and no separate screen-space draw API.
- Sprite batching with bindless textures and per-instance UV/color through the existing batch path;
  no new renderer path.
- A ninth internal primitive mesh, the unit disc.
- A small plane tolerance in `BoundingFrustum.Intersects` so a full-viewport or boundary-aligned
  quad is not culled by plane-extraction drift.
- HDR scene target and AgX present apply to the screen-space pass as well; LDR sprites are still
  authored linearly.
- DPI and resize behavior; letterboxing is a game choice through the camera's clear color and a
  design-space fit, not an engine mode.
- Pong sample: a human left paddle and a deterministic AI right paddle, ball speed-up, score text,
  keyboard/gamepad input, pause, restart; a two-project solution with a build-time bake, and the M7
  Native AOT gate.
- Remove the M1-M3 `samples/HelloInterop` diagnostic host.
- **Deferred to M8, not shipped in M7:** a separate multi-camera overlay pass and per-pass frame
  data. The M7 HUD is same-pass text on a screen-aligned plane.

**Exit criteria.**

- Pong runs at 1280x720 on Windows and on WSL with lavapipe, resizes correctly, and keeps a stable
  aspect.
- A texture drawn at integer pixel coordinates is pixel-aligned at 100% DPI.
- A sprite sheet or multiple textures batch into one draw when mesh/shader/state match.
- Text renders correctly in the same frame as sprites.
- `ScreenToWorldTransform` places a pixel-authored quad at the requested render pixels, and its
  world position agrees with `WorldToScreen` on the same plane.
- A full-viewport quad survives frustum culling.
- The screen-space pass does not disturb 3D depth: it is one same-pass text/quad draw with the
  normal render state.
- Native AOT publish runs Pong on Windows and Linux.

**As-built notes.** M7 shipped the planned orthographic and screen-space scope with these as-built
details:

- **Unified camera creation.** `EngineContext.CreateCamera(Transform, CameraProjection, float
  fieldOfView = 0f, float orthographicHeight = 0f, ClearMode clearMode = ClearMode.ColorAndDepth,
  Color? clearColor = null, float near = 0.01f, float far = 1000f, Rect? viewport = null)` is the
  only camera entry point. A perspective camera uses `fieldOfView` and rejects a nonzero
  `orthographicHeight`; an orthographic camera uses `orthographicHeight` and rejects a nonzero
  `fieldOfView`; both reject an undefined projection and an out-of-range selected parameter. A
  perspective `far` may be `float.PositiveInfinity`; an orthographic `far` must be finite. The
  perspective default changed from infinity to `1000`, so callers that relied on the old implicit
  infinity pass it explicitly. `CreateOrthographicCamera` is removed; `orthographicHeight` is its
  replacement. The camera properties, clamping, clearing, frame scoping, and disposal contract are
  unchanged. The unified signature has no `target` parameter; `RenderTexture` targets arrive in M8.
- **Screen transform.** `Camera.ScreenToWorldTransform(Vector2 point, float distance)` takes a
  viewport-relative render-pixel point with a top-left origin (`(0, 0)` is the viewport's top-left
  corner, not target space) and a view-depth plane distance that must be finite and greater than
  zero. It builds the view-space point from the normalized device position and the plane half-extents,
  transforms it to world space by the camera transform, and returns
  `new Transform(worldPosition, cameraRotation, new Vector3(unitsPerPixel))`. The scale is
  `2 * distance * tan(fieldOfView / 2) / viewport.Height` for perspective and
  `orthographicHeight / viewport.Height` for orthographic, so a quad authored in render pixels maps
  to those exact pixel extents at that plane. The returned transform's local `XY` is screen-aligned
  and local `+Z` faces the camera. The camera transform must have unit scale: a scaled camera
  transform would scale the world position while `distance` and the pixel scale stay unscaled, so
  the member documents that requirement instead of guessing. There is no `ScreenToWorldPoint`.
- **New world-space primitives.** `Camera.DrawQuad(Transform, Vector2, Color, RenderState?)` draws
  one instance of the engine quad in the transform's local `XY` plane, centered, local `+Z` front,
  with the instance matrix `CreateScale(size.X, size.Y, 1) * transform.ToMatrix()`, over the built-in
  `/shaders/primitive` material. `Camera.DrawQuad(Transform, Vector2, Texture, Color?, RenderState?)`
  samples the full texture through the built-in `/shaders/mesh` material and records `(0, 0, 1, 1)`
  as the per-instance UV rectangle.
  `Camera.DrawQuad(Transform, Vector2, Texture, Rect source, Color?, RenderState?)` takes a
  normalized `[0, 1]` source rectangle (top-left corner plus positive extents) and records it as
  `rbInstance.UvRect`. `Camera.DrawDisc(Transform, float radius, Color)` instances the unit disc
  mesh (36 segments) in the same local `XY` plane and is opaque.
  `Camera.DrawGrid(Transform, int slices, float spacing, Color)` composes every line as
  `CreateLineMatrix(localStart, localEnd) * transform.ToMatrix()`; the base grid is the local `XZ`
  plane with local `+Y` as the normal, the transform position is the grid center, and the scale
  multiplies both `spacing` and the line extents. `DrawGrid(int, float, Color)` is exactly the
  `Transform.Identity` form, so its behavior is unchanged. A zero transform scale component records
  nothing. `DrawDisc` and `DrawGrid` use the opaque state; `DrawQuad` with a null `renderState` is
  opaque, and `RenderState.AlphaBlend` is the alpha/sprite case. The effective state is written to
  the draw record and is part of the batch key, so opaque and alpha quads never merge. The text and
  billboard paths keep their alpha/no-depth-write states.
- **Validation.** The new helpers validate before any retention or record mutation. A non-finite
  size, radius, spacing, transform component, color, or source rectangle component is
  `ArgumentException`; a negative size, radius, spacing, or transform scale component is
  `ArgumentOutOfRangeException`; an exactly zero size, radius, or transform scale component records
  nothing; a null texture is `ArgumentNullException`; a disposed texture, camera, or primitive asset
  is `ObjectDisposedException`. A source rectangle with a non-positive width or height, or one
  outside the normalized `[0, 1]` texture rectangle, is `ArgumentOutOfRangeException`. A camera draw
  off the tick thread is `InvalidOperationException`.
- **No `DrawTexture`, no 2D overloads.** The M5 preview screen-space texture helper did not return:
  M7 ships no `DrawTexture`, no destination-rectangle screen-space draw, and no 2D overload family.
  A textured `DrawQuad` or `DrawBillboard(faceCamera: false)` covers a sprite, and
  `ScreenToWorldTransform` plus the world-space `DrawText` `Transform` overload covers screen-space
  text with pixel-sized text. There is no `DrawText(Vector2)` overload; the screen pattern passes
  the transform from `ScreenToWorldTransform`. Sprite batching uses the existing bindless +
  per-instance UV/color path, not a second renderer.
- **Orthographic 3D.** The same orthographic camera renders 3D models and primitives with an
  orthographic projection; it is not a separate 2D renderer. `samples/Castle` has an `O` toggle
  between perspective and orthographic, records one camera per tick, and derives the orthographic
  height from the orbit distance (`2 * orbitDistance * tan(fieldOfView / 2)`) so the framing matches
  the perspective view at that distance. Its orthographic far plane is derived from
  `MAX_DISTANCE * 2`, and the status line shows the current projection. The same `Camera` batching,
  depth test, culling, and statistics apply to both projections.
- **HUD text.** Castle and PrimitiveGallery draw their HUD with
  `ScreenToWorldTransform(new Vector2(margin, margin), 1f)` and
  `DrawText(..., 18f, ...)`. The returned scale keeps 18 local units at 18 render pixels in either
  projection. This is same-pass, screen-aligned text: it is alpha-blended with no depth write, so
  geometry closer than the overlay plane can still occlude it. It is not a separate overlay pass,
  and per-pass frame data is still absent, so the M4 one-frame-block limitation is unchanged. A
  dedicated multi-camera overlay pass and per-pass frame data are deferred to M8.
- **Nine primitive meshes.** The engine builds one more internal primitive mesh, the unit disc,
  alongside cube, sphere, plane, quad, triangle, line, wire cube, and wire sphere. The disc is one
  center vertex plus 36 rim vertices and 36 triangles, built once before `IGame.Setup` through the
  procedural GPU bridge. It has no public accessor; the primitive meshes and the three engine
  materials remain internal.
- **Frustum tolerance.** `BoundingFrustum.Intersects` accepts a positive corner within `1e-4` of a
  plane so a full-viewport quad or boundary-aligned geometry is not culled by floating-point drift
  in the extracted planes. `Contains` keeps its exact non-negative test, and the tolerance applies
  only to the `Intersects` positive-vertex test.
- **Pong sample and AOT gate.** `samples/Pong` is the M7 sample and Native AOT gate. It is a
  two-project solution, `samples/Pong/Pong.slnx`, outside `src/RayBork/RayBork.slnx`, containing
  the sample and the JIT `Pong.Assets` executable. The solution builds `Pong` once and then
  `Pong.Assets`; the assets project runs the bake executable out of process over the checked-in
  sources, bakes `pong.pck`, and copies it beside the sample executable. `Pong` does not reference
  or build `Pong.Assets`, so the graph has no cycle. The publish target copies the pack produced by
  the Release solution build into the publish directory and fails with a clear error when it is
  missing, so neither path needs a manual bake or an `--assets` argument. The pack contains
  `/data/game`, a `Pong.PongSettings` TOML data asset (paddle texture path, ball radius, max score,
  paddle width/height/speed, ball speed, ball speed-up per hit, maximum ball speed, and AI paddle
  speed), and `/textures/paddle`, a 32x128 original opaque paddle PNG with its KTX2 raw payload.
  `PongSettings` lives only in the sample project; `Pong.Assets` references `Pong.csproj` directly
  instead of linking the settings file, and the assets project also references `RayBork.Bake`.
  Every tick records one orthographic camera at `(0, 0, 1)` looking down `-Z` with
  `orthographicHeight = Window.RenderHeight`, so the plane at distance `1` has a
  world-units-per-pixel scale of exactly one. The `1280x720` 16:9 design playfield is fitted into
  the current render pixels, letterboxed with the camera clear color, and every draw uses
  `ScreenToWorldTransform`. `DrawQuad` draws the playfield, border, net, score panels, shaded
  overlay, and textured paddles; `DrawDisc` draws the ball; `DrawText` draws the scores, status
  line, and pause/winner overlay. The left paddle is the human and the right paddle is a
  deterministic AI that tracks the ball while it approaches and returns to the playfield center
  otherwise, moving at `AiPaddleSpeed` (`400` design pixels per second) with a `5` design-pixel
  deadzone inside the fixed substeps. The simulation uses a fixed opening serve, fixed `1/120`
  second substeps capped at a `0.25` second tick delta, top and bottom bounces,
  `Collision.CheckBoxSphere` paddle collisions, scoring with the serve going to the loser, a
  max-score win, and pause/restart. Every paddle hit adds `BallSpeedupPerHit` (`24` design pixels
  per second) to the ball speed up to `MaxBallSpeed` (`900`), and a serve or reset restores
  `BallSpeed` (`420`). The controls are W/S, Up/Down, and gamepad 0 left stick or d-pad for the left
  paddle, plus pause/restart/exit; the sample accepts `--frames` and `--help`. The Windows `win-x64`
  publish finished with zero AOT/trim warnings: its publish directory contains `pong.pck` beside
  `Pong.exe` with no bake tooling, and the published host ran `--frames 30` to exit `0` on the
  hardware driver. The `linux-x64` publish also finished with zero warnings, contains `pong.pck`
  beside `Pong` and no bake tooling, and the published host ran `--frames 30` to exit `0` on the
  conformant lavapipe/llvmpipe software driver with an SDR `B8G8R8A8_SRGB` swapchain, FIFO
  presentation, 4x MSAA, and a 1280x720 window.
- **Tests.** M7 adds `Graphics/ScreenDrawGpuTests`, which submits the production screen-space draws
  through the Vulkan backend and asserts validation-layer cleanliness, statistics, and single-pixel
  linear scene-target probes. It covers pixel-aligned textured quads, the normalized source
  rectangle, opaque and alpha quads (including the mesh-program textured alpha path), batching
  across textures and split only by render state, the disc interior, text and sprites in one frame,
  full-viewport frustum tolerance, and 3D orthographic cube rendering. `Rendering/PrimitiveDrawTests`
  gains the `DrawQuad`, `DrawDisc`, and `DrawGrid(Transform)` record, validation, zero-extent,
  disposed-asset, and batching coverage; `Rendering/CameraTests` covers `ScreenToWorldTransform`.
  The Windows hardware run is 2,695
  tests, 114 of them GPU integration tests, with no skips. The lavapipe Linux run is 2,688 passed,
  0 failed, and 7 skipped: the seven existing HDR/scRGB-present tests skip because lavapipe exposes
  only SDR, and the GPU-filtered run is 107 passed with the same 7 skips. Every sample solution
  builds. There is no golden-image suite.
- **HelloInterop removed.** The M1-M3 `samples/HelloInterop` diagnostic host is gone; the Pong,
  Text, Castle, and PrimitiveGallery samples and the test suite cover the host, input, time,
  logging, renderer, and asset-loading surface it exercised.
- **Overlay pass deferred.** The M7 scope listed a screen-space overlay pass; the shipped HUD is
  same-pass text, and the separate multi-camera overlay pass with per-pass frame data is M8 work.
  M7 does not claim it as shipped.

**Risks.**

- **Screen transform and camera scale.** `ScreenToWorldTransform` requires a unit-scale camera
  transform. A scaled camera would produce a plane position scaled by the transform while the
  pixel scale stays unscaled, so the member documents the requirement and does not support a scaled
  camera; the samples keep the camera transform at unit scale.
- **Same-pass HUD occlusion.** The M7 HUD is depth-tested alpha text on a screen-aligned plane, so
  geometry closer than the overlay plane can occlude it, and the one-frame-block limitation still
  prevents different per-pass views in one frame. A dedicated overlay pass with per-pass frame data
  is M8; the current behavior is documented rather than hidden.
- **No 2D draw API.** Games porting a raylib-style 2D renderer must draw through
  `ScreenToWorldTransform` and the world-space primitives instead of screen-coordinate overloads.
  This is a deliberate design choice: one draw path, one batch key, and no duplicate 2D renderer.
  The Pong sample is the reference pattern.
- **Frustum tolerance.** `Intersects` keeps boundary geometry that is up to `1e-4` behind a plane.
  The slack is small and applies only to the conservative overlap test; `Contains` stays exact, and
  the renderer still rejects geometry fully outside a plane.
- **Pixel mapping and DPI.** Pixel alignment depends on the render-pixel viewport and the DPI
  scaling of the window; the screen transform is exact for the clamped viewport, while a game that
  wants a fixed design space must fit it itself, as Pong does.
- **AOT gate moved to Pong.** `samples/Text`, `samples/Castle`, and `samples/PrimitiveGallery` remain
  buildable and publishable but are no longer the gate. The M7 gate is Pong on both RIDs, and a
  failure there is a milestone failure.

### M8 — Render targets, multiple cameras, and readback

**Goal.** Offscreen rendering, post-processing building blocks, split-screen, and screenshots.

**Scope.**

- `RenderTexture` creation with `Rgba16Float` default, color/depth attachments, MSAA resolve,
  resize, sampling, and lifetime.
- Camera target routing and viewport rectangles.
- Image and resolve barriers between passes; write-after-read validation.
- Multiple cameras to the HDR scene target and to offscreen targets.
- Per-pass frame data: one frame uniform block per camera pass, so multiple cameras with different
  views are correct instead of sharing the last pass' values.
- A separate multi-camera overlay pass for HUD text and sprites that must not be occluded by scene
  depth; the M7 HUD is same-pass and is the deferred input to this work.
- `ReadPixelsAsync` display-referred RGBA8 after AgX for screenshots; raw HDR readback is post-1.0.
- RenderTarget sample: render the castle to a target, sample it in a fullscreen triangle with a
  simple post-process shader, and present.
- Split-screen sample pass: two cameras, two viewports.
- Target format/sRGB rules; depth texture sampling.

**Exit criteria.**

- Render-to-texture produces the same visible image as the direct path (within tolerance).
- A post-process shader samples the color target without validation errors.
- Split-screen renders two independent views in one frame.
- `ReadPixelsAsync` returns the expected top-left display-referred RGBA8 image and completes
  without stalling the next tick indefinitely.
- Resizing a target while it is not in use works; resizing while in use fails with a clear error.
- MSAA 4x offscreen color/depth resolves correctly; 1x and 8x are supported or fall back.
- Native AOT publish runs the RenderTarget sample on Windows and Linux.

### M9 — Input and window polish

**Goal.** Close the raylib input/window gap and make the platform layer comfortable for real games.

**Scope.**

- Text input, including committed IME text, delivered through `TextInput`.
- Clipboard text (read/write); clipboard image is later.
- Cursor: normal/hidden/disabled, relative motion, cursor-on-screen state.
- Fullscreen, borderless, windowed transitions; monitor selection; monitor hotplug.
- Per-monitor DPI changes and resize handling.
- Gamepad hotplug, stable instance IDs, rumble with duration, SDL mapping database.
- Minimum/maximum size, focus state, and window close behavior.
- 30 FPS minimized/unfocused cap remains correct through focus and DPI changes.
- Input matrix tests: keyboard layout, text input, gamepad mapping, hotplug, rumble, DPI.
- Touch, window icon, opacity, and drag-and-drop stay post-1.0.

**Exit criteria.**

- A text field sample accepts typed text and committed IME text on Windows and on WSL with lavapipe.
- Alt+Enter toggles fullscreen; borderless mode works on both platforms.
- A gamepad can be connected/disconnected while running and the game sees the change at the next
  tick with stable instance IDs.
- Rumble is best-effort and does not throw on unsupported devices.
- DPI change does not corrupt the swapchain or input coordinates.
- Focus loss caps the tick loop at 30 FPS; focus restore returns to the configured cadence.
- Native AOT publish runs the input sample on Windows and Linux.

### M10 — Performance, validation, and hardening

**Goal.** Prove the engine is stable, allocation-conscious, recoverable, and fast enough for its
target games.

**Scope.**

- Frame pacing: latency measurement and further pacing refinements beyond the M6 FIFO coupling and
  the target cap.
- Text recording-path optimization: the measured per-glyph layout, metrics lookup, cache acquire,
  and frame-retention cost, deferred from M6.
- **Text cutout and alpha-to-coverage.** Text becomes a depth-writing cutout by default: add an
  `AlphaToCoverage` pipeline and render-state capability, draw MSDF text with an opaque blend and
  depth writes so MSAA resolves the glyph edges through alpha-to-coverage, and remove text from the
  transparent sort. The alpha-blended MSDF variant stays available for translucent text. MSAA
  antialiases triangle edges, not texture-defined glyph shapes, so a plain discard cutout would
  alias; alpha-to-coverage (currently hard-coded off in the geometry pipeline) is the required
  plumbing. Covered by edge and occlusion pixel probes plus a validation-clean pipeline test, and
  the API and versioning docs record the changed default.
- Pipeline cache persisted to disk; cache invalidation on driver/version change.
- Buffer/descriptor/staging pools; no steady-state per-frame allocations in the renderer.
- Bindless slot reuse and retirement; memory budget counters.
- Expanded `RenderStats` overlay/profiler; GPU timestamps where supported.
- Validation CI: debug layer on the gate scenes, clean output requirement.
- Stress suite: 10k instances, 1k draw groups, resize storm, asset churn, tick/render race,
  repeated device-loss recovery.
- Fuzzing: pack reader, font parser, shader metadata, path normalization, TOML shape binding.
- OOM and unrecoverable device-loss paths; no hangs, no partial state.
- AOT matrix: win-x64 and linux-x64, Debug and Release; publish size and startup measurements.
- Benchmark suite for draw submission, batching, text layout, pack load, and asset load.
- No golden-image suite.

**Exit criteria.**

- Gate scenes hit the provisional targets: 1080p60 on the reference GPU, under 1,000 draw calls
  for the stress scene, 10k instances in the instancing scene, no engine allocations in steady
  state (measured with `GC.GetAllocatedBytesForCurrentThread` and native counters).
- Validation is clean for every gate scene, including resize, MSAA resolve, HDR/SDR fallback, and
  target recreation.
- Stress suite runs for a defined iteration count without crashes, leaks, or validation errors.
- A forced device loss recovers repeatedly; unrecoverable loss produces a clean fatal error within
  a bounded time.
- AOT publish size and startup time are recorded and do not regress without review.
- Fuzz corpora run in CI with no crashes or unbounded allocations.

**Notes.** Performance targets are provisional until the reference GPU and CI hardware are fixed.
The engine records the numbers and the hardware in the release notes; it does not claim a target
it cannot reproduce.

### M11 — Samples, documentation, and packaging

**Goal.** A new user can install RayBork, create a project, bake assets, run a sample, and publish
with Native AOT without reading the engine source.

**Scope.**

- Samples: Castle (3D textured/custom shader/HDR plus the perspective/orthographic toggle; the
  sample and its assets executable ship in M4, and M11 polishes them), Pong
  (orthographic/screen-space match, input, and text; the sample and its AOT gate ship in M7, and M11
  polishes them), Text (MSDF/layout; the sample and its AOT gate ship in
  M6, and M11 polishes them), Instancing (batching/stats), RenderTarget
  (offscreen/MSAA/post-process).
- `dotnet new raybork` template with a game project, assets executable, and AOT publish profile.
- NuGet packages: `RayBork.Engine` and the bake tool; package validation and symbol packages.
- XML docs for every public member; the API reference synchronized with the implementation.
- Migration notes for every breaking change since the last preview.
- README, getting-started guide, bake guide, shader ABI guide, threading guide, troubleshooting.
- Public API analyzer files updated; obsolete members documented.
- MIT license with copyright holder Norne, and third-party notices finalized.
- Samples use `Exe` for console logging; release games use `WinExe`.
- Add the AOT publish job to GitHub Actions once the first sample exists.

**Exit criteria.**

- All five samples run on Windows and on WSL with lavapipe from a clean checkout and from the
  packaged NuGet.
- The template creates, builds, bakes, runs, and AOT-publishes without manual edits.
- A new user can follow the getting-started guide in under an hour without asking for help.
- The API reference matches the public API analyzer output.
- Packages install into an empty project and the sample runs.
- Docs build has no broken internal links.

### M12 — 1.0 release gate

**Goal.** Freeze the public surface and ship a reproducible release.

**Scope.**

- API freeze review: naming, nullable annotations, thread contracts, exceptions, diagnostics.
- Final AOT publishes for `win-x64` and `linux-x64`, Debug and Release.
- CI matrix: Windows hardware Vulkan, lavapipe software Vulkan, AOT smoke, added to GitHub Actions
  after the first sample.
- Full test run: unit, GPU integration, AOT, stress, fuzz, benchmarks. No golden-image suite.
- Release notes: features, known limitations, migration notes, hardware baseline, HDR/AgX/MSAA
  behavior, performance numbers.
- Version stamping, symbols, source link, package signing policy, checksums.
- MIT license, contributing guide, security policy, issue templates.
- A final pass over [api.md](api.md), [architecture.md](architecture.md), and this roadmap so the
  shipped reality matches the documents.

**Exit criteria.**

- Every 1.0 definition item in §1 is satisfied.
- The release artifacts are reproducible from a tagged commit.
- No open P0/P1 bugs; known limitations are documented.
- The post-1.0 backlog is ordered and linked from the release notes.

## 5. Dependency graph

```text
M0
 └─> M1
      ├─> M2 ─┐
      ├─> M3 ─┴─> M4
      │            ├─> M5
      │            ├─> M6
      │            ├─> M7
      │            └─> M8
      └─> M9 (parallel after M1)

M4 + M5 + M6 + M7 + M8 + M9
 └─> M10
      └─> M11
           └─> M12
```

M3's pack format, assets executable, and processors can be developed while M2 brings up the
renderer. M4 is the integration point: it needs both the renderer foundation and the asset
pipeline. M5-M8 can be split across parallel workstreams once M4 is stable. M9 is independent of
the renderer and can run alongside M2-M8.

## 6. raylib parity matrix

RayBork targets "most of raylib" for the drawing/input/model/text surface, not every module.
The matrix is the scope contract.

| raylib module | RayBork status | Milestone | Notes |
| --- | --- | --- | --- |
| Window | Covered | M1, M9 | One window; fullscreen/borderless/DPI in M9; 30 FPS cap when minimized/unfocused. |
| Cursor | Covered | M9 | Normal/hidden/disabled; cursor-on-screen. |
| Drawing (2D/3D begin/end) | Covered | M4, M7 | Cameras are the begin/end scope; no global draw state. 2D is drawn as world-space quads, discs, and text on a `ScreenToWorldTransform` plane, not through a second 2D path. |
| Shader load/uniforms | Covered | M4 | SPIR-V at bake; `SetUniform` snapshot; entry points `main_vertex`/`main_fragment`. |
| Screen-space helpers | Covered | M4, M7 | `ScreenToWorldRay`, `WorldToScreen`, `ScreenToWorldTransform`, orthographic cameras, and screen-aligned HUD text; no 2D draw overloads. |
| Timing | Covered | M1 | `Stopwatch`; double seconds; no fixed step in v1. |
| Random | Not engine | — | Use `System.Random`; no global RNG. |
| Misc (screenshot, OpenURL) | Partial | M8/M9 | Display-referred readback screenshot; `OpenURL` out. |
| Logging | Covered | M1 | `Microsoft.Extensions.Logging` + ZLogger; custom providers supported. |
| Memory allocators | Not public | — | Engine-internal pools; no `MemAlloc` API. |
| File system | Partial | M3 | Pack reads; user-data file API post-1.0. |
| Compression/encoding | Not public | — | Pack-internal Zstandard; no public codec API. |
| Automation events | Out | — | No input replay/automation in v1. |
| Keyboard | Covered | M1 | Physical scancodes + text input. |
| Mouse | Covered | M1 | Buttons, position, delta, wheel. |
| Gamepad | Covered | M1, M9 | Basic in M1; hotplug/rumble in M9. |
| Touch | Post | — | Post-1.0. |
| Text/fonts | Covered | M6, M7 | Managed TTF/OTF parsing (cmap 0/4/6/12, TrueType `glyf`, CFF Type 2, `kern`/GPOS pair kerning), deterministic on-demand MSDF, layout/alignment/wrapping, and world-space text; M7 anchors a block to a screen pixel with `ScreenToWorldTransform`. No complex shaping, ligatures, bidi, fallback, color fonts, emoji, variable axes, or CFF2. |
| Textures | Covered | M3, M5, M7, M8 | Loaded textures, billboard texture sampling, textured quads and screen-space sprites through `DrawQuad`/`DrawBillboard`, and HDR/MSAA render targets. |
| Images | Partial | M3 | Bake decodes; no runtime image manipulation API. |
| Models | Covered | M4, M5 | glTF 2.0/OBJ are source formats; packs contain the engine format; static meshes; wireframe model drawing. |
| Custom data | Covered | M3 | TOML data with `[GenerateShape]`; `WithAssetType<T>` then `Load<T>`. |
| Meshes | Covered | M3, M4, M5 | Loaded meshes, procedural `MeshBuilder` creation including the nine `Generate*` factories, and primitive meshes; fixed vertex layout. |
| Materials | Rethought | M4 | No `Material`; shader state + per-instance uniforms. |
| Model animations | Post | — | Skeletal/morph post-1.0. |
| 3D shapes | Covered | M5, M7 | Lines, points, triangles, strips, rays, bounding boxes, cube, sphere, plane, cylinder/wire cylinder, capsule/wire capsule, circle, grid, and wire shapes, with shape/position and `Transform` forms; M7 adds the quad, disc, and transformable grid. |
| Collision | Covered | M5 | Sphere/box checks and ray/sphere/box/triangle/quad/plane/mesh queries. |
| HDR / AgX / MSAA | Covered | M2, M4, M5 | Linear scene target, Godot 4.7's HDR-aware AgX present, 4x default MSAA. |
| Renderer backend | Covered | M2 | Vulkan 1.2 first; internal seam for future mobile/web backends. |
| Device-loss recovery | Covered | M2, M10 | Recoverable failures retry; unrecoverable loss is fatal. |
| Audio | Post | A1-A3 | AAC in engine, FFmpeg bake, mixer, 3D sources, HRTF, Doppler. |
| VR | Post | — | Not in 1.0. |
| Math | Covered | M1 | `System.Numerics` + `Transform` + degree-based Unity-style extensions. |
| Colors | Covered | M1, M5 | Linear HDR `Color`, the restored 158-color Bork2d/Unity sRGB-authored preset palette, OkLab, sRGB conversion helpers, AgX output. |

"Covered" means the API exists and has a sample/test gate. "Partial" means the common case is
covered and the missing part is documented. "Post" means deliberately after 1.0.

## 7. Cross-cutting workstreams

### 7.1 Native AOT

- Every runtime project enables `IsAotCompatible`, `EnableTrimAnalyzer`, and `EnableAotAnalyzer`.
- Every milestone publishes a sample for `win-x64` and `linux-x64` with `PublishAot=true`.
- No reflection serialization, no runtime shader compilation, no `Assembly.Load` for assets.
- Engine asset loading is a registry: the built-in engine formats plus loaders registered with
  `WithAssetLoader<T>()`. Custom data is registered with `WithAssetType<T>()` and loaded through the
  same `Load<T>` using PolyType `[GenerateShape]` and Nerdbank.MessagePack; `LoadData` covers bytes.
- `Microsoft.Extensions.Logging` and ZLogger are AOT-gated runtime dependencies; M1 is their first
  feature-level gate.
- Native dependencies (SDL3, Vulkan loader) are deployed per RID and validated on a clean machine.
- The bake is JIT and may use reflection; it must never appear in a runtime publish graph.

### 7.2 Testing and CI

| Suite | Runs | Gate |
| --- | --- | --- |
| Unit | Every PR, Windows and Linux | Must pass |
| Architecture | Every PR | Must pass |
| GPU integration | Every PR on a GPU runner; lavapipe fallback | Must pass or be explicitly skipped with a tracked issue |
| AOT publish | Every PR | Must start and render a gate frame |
| Stress | Nightly and release | Must pass |
| Fuzz | Nightly | No crashes, no unbounded allocation |
| Benchmarks | Nightly and release | Report; regression threshold reviewed |

- xUnit v3 and Moq are the only test frameworks.
- GPU tests render offscreen; they do not require a visible window.
- **No golden-image suite.** Renderer tests assert validation-layer cleanliness, draw/instance
  counts, readback values with explicit tolerances, and crash/leak behavior. Stable numeric probes
  may read a small region, but there is no committed reference-image corpus.
- M5 adds the primitive draw GPU suite (`PrimitiveDrawGpuTests`), which records the production
  `Camera` helpers over the real built-in primitive assets and submits them through the Vulkan
  backend. It asserts validation-layer cleanliness, draw/instance/triangle statistics and the plan's
  topology, and single-pixel linear scene-target probes for a one-pixel line row, solid-shape tint,
  plane back-face culling, triangle winding versus culling, billboard texture sampling, and billboard
  occlusion behind opaque geometry. The model wireframe suite (`ModelWireframeGpuTests`) covers the
  polygon-mode-line path: front-facing triangle edges without a filled interior, texture sampling on
  a wireframe fragment, and distinct filled/wireframe pipelines. The collision queries and the BVH
  are covered by deterministic unit tests, including accelerated-versus-brute-force equivalence, and
  the baked payload by codec and loader tests. The M5 suite was 1,899 tests, 93 of them GPU
  integration tests; every sample solution built.
- M6 adds the font and text suites. `SfntFaceContainerTests`, `CmapTests`, `FontMetricsTests`,
  `KerningTests`, `GposKerningTests`, `CffReaderTests`, and `CffType2Tests` cover the parser and
  outlines; `FontParserFuzzTests` and `CffFuzzTests` cover malformed and hostile input against the
  `FontFormatException`/`FontUnsupportedException` split and the allocation bound. The MSDF suites
  (`MsdfGeneratorTests`, `MsdfLayoutTests`, `MsdfAnalyticFieldTests`, `MsdfEdgeColoringTests`) pin
  determinism, the normalized field, edge colouring, the padded layout, and the work limits. The
  glyph pipeline suites (`FontGlyphPipelineCacheTests`, `FontGlyphPipelineMetricsTests`,
  `FontGlyphSettingsTests`, `GlyphGenerationServiceTests`) cover async generation, the 512-texture
  LRU bound, frame holds, failure/empty handling, and shutdown. `FontAssetTests`,
  `FontTests`, the layout suites (`TextLayoutEngineTests`, `TextLayoutMeasurementTests`,
  `TextLayoutWrappingTests`, `TextLayoutBufferTests`, `TextLayoutValidationTests`), and
  `TextDrawTests` cover the CPU-only asset, `.notdef`, layout, and the camera draw records;
  `TextDrawGpuTests` submits the production text draws through the Vulkan backend and asserts
  validation-layer cleanliness, draw/instance/triangle statistics, and single-pixel linear
  scene-target probes. At the M6 milestone the suite was 2,625 tests, 106 of them GPU integration
  tests.
- M7 adds the screen-space GPU suite (`ScreenDrawGpuTests`) and extends the primitive draw unit
  suite (`PrimitiveDrawTests`). `ScreenDrawGpuTests` submits the production screen-space draws
  through the Vulkan backend and asserts validation-layer cleanliness, statistics, and single-pixel
  linear scene-target probes for pixel-aligned textured quads, the normalized source rectangle,
  opaque and alpha quads (including the mesh-program textured alpha path), batching across textures
  and split only by render state, the disc interior, text and sprites in one frame, full-viewport
  frustum tolerance, and 3D orthographic cube rendering. `PrimitiveDrawTests` covers the
  `DrawQuad`, `DrawDisc`, and `DrawGrid(Transform)` records, zero-extent behavior, validation,
  disposed assets, retention, and batching, and `CameraTests` covers `ScreenToWorldTransform`. The
  suite is 2,695 tests, 114 of
  them GPU integration tests, and every sample solution builds.
- The HDR AgX rework is covered by unit tests that pin the combined matrices, the Godot curve
  parameters, neutral-gray preservation, the crossover continuity, the SDR and scRGB responses, and
  the present shader source, plus the `HdrPresentGpuTests` presented-pixel probes for the SDR image,
  scRGB paper white and peak at two headrooms, the frame tone-mapping snapshot, and the direct
  `None` path. A host without the scRGB swapchain path reports the test as a skip with its actual
  selected format.
- The architecture tests enforce the dependency rules, the no-native/backend-types-in-public-API
  rule, and the absence of a public `Material`.
- CI runs on Windows and Ubuntu. Linux GPU tests use lavapipe under Xvfb; WSLg hardware Vulkan is
  not a target.
- HDR/SDR fallback, MSAA sample counts, and device-loss recovery each have an explicit test path;
  a host that cannot exercise them reports a skip with a reason.

### 7.3 Documentation

- XML docs on every public member; warnings-as-errors for missing docs on public API.
- The API reference is updated in the same PR as a public API change.
- Each sample has a README with build/bake/run/publish steps.
- The shader ABI has a standalone guide in [Rendering: Shaders](api/rendering/shaders.md#74-shader-abi);
  the generated identifiers are stable for v1.
- Release notes include a migration section for breaking changes.

### 7.4 Performance

Provisional targets, to be confirmed against the reference GPU:

| Metric | Target |
| --- | --- |
| Frame rate | 1080p60 on the reference mid-range GPU for the Castle scene, HDR/AgX/4x MSAA |
| Draw calls | Under 1,000 for the stress scene; one per mesh/shader/state group |
| Instances | 10,000 in the instancing scene |
| Steady-state allocations | Zero in the engine's tick/render hot paths |
| Startup | Under 1 second to first frame without pack loading |
| Pack load | Bounded by asset size; no full-pack decompression |
| Input latency | One tick + present; documented, not hidden |

The numbers are recorded with hardware and driver versions. A target that cannot be reproduced is
removed from the release notes rather than weakened silently.

### 7.5 Security and robustness

- Pack parser: length checks before allocation, integer-overflow checks, path validation, hash
  verification, and a fuzz corpus.
- Font parser: table bounds, glyph count limits, recursion limits for composite glyphs, and a fuzz
  corpus.
- Shader metadata: bake-time validation; runtime rejects unknown/oversized interfaces.
- TOML/MessagePack custom data: compiled shapes only; no reflection binding at runtime.
- No dynamic code loading from packs. Packs contain data, not assemblies.
- Paths are canonicalized case-insensitively and cannot escape the pack namespace.
- OOM and unrecoverable device loss fail fast with a diagnostic; no partial-frame presentation.
- Recoverable device loss retries with bounded attempts; a recovery loop is treated as fatal.
- Packs are not signed in v1. Integrity is corruption detection, not authenticity.

## 8. Risks

| Risk | Impact | Mitigation | Milestone |
| --- | --- | --- | --- |
| HDR swapchain variability | HDR path unavailable or inconsistent | Prefer HDR, fall back to sRGB; report `EngineInfo.HdrOutput`; test both | M2/M10 |
| HDR tone mapping is display-dependent | HDR highlights are capped at the reported display headroom; HDR10/PQ output is deferred | The AgX curve's output maximum is the reported headroom on scRGB, so the curve itself reaches the display peak; HDR10/PQ remains deferred and is never selected; probe paper white and peak on an HDR display | M2/M4/M5 |
| Wireframe needs `fillModeNonSolid` | `DrawWireModel` unavailable on a device without the feature | Feature-probe at device creation; `RB3001` names the device and feature; no silent fallback; GPU tests cover the pipeline split | M5 |
| SDR AgX differs from the M4 image | Exposure or AgX white tuned against M4 changes the image | Pinned CPU-reference and presented-pixel tests; runtime exposure/white/contrast; migration note | M5 |
| sRGB-authored preset values | `Gray`/`Grey`, `Yellow`, and `DarkCyan` change from their earlier linear constants | 0.x migration note; aliases; use `new Color` or `FromSrgb` for an intentional raw value | M5 |
| MSAA + HDR resolve | Extra memory/bandwidth or resolve bugs | 4x default, 1/2/4/8 support or fallback; resolve tests; validation | M2/M8 |
| Shader ABI complexity | Bake/runtime/sample churn; custom shaders hard to author | Prototype two shaders before M4 batching; explicit sidecar; publish ABI guide | M4 |
| Per-instance uniform limits | Batches split unexpectedly; performance regressions | Size estimator, device-limit checks, documented split behavior, per-draw fallback | M4 |
| Managed font/MSDF library | Quality, hostile-input, or performance miss; no native fallback ships | Shipped with defensive caps, the format/unsupported split, a deterministic generator, and fuzz/analytic suites; a native `msdfgen`/FreeType path stays a possible later change, not a hidden dependency | M6 |
| SDL3-CS maturity | Missing binding, packaging gap, AOT issue | Pin and test per RID in M0; thin hand-written fallback for used functions | M0/M1 |
| Renderer seam abstraction cost | Over-abstraction slows the Vulkan path | Keep the seam at frame/resource granularity; no per-draw virtual calls | M2/M10 |
| Threading/lifetime bugs | Races, use-after-free, intermittent crashes | Frame retention, tick-affinity rules, stress suite, validation, deterministic shutdown order | M1-M5 |
| Reverse-Z custom projections | Depth bugs in user code | One engine projection path, tests, documentation; no public OpenGL-style helper | M2/M4 |
| Asset refcount/double dispose | Leaks or premature destruction | Exactly-once contract, debug checks, frame retention | M3 |
| GPU CI availability | Renderer changes untested | Offscreen tests, lavapipe fallback, numeric probes, explicit skip policy | M0/M2 |
| ZLogger/PolyType AOT | Runtime trim/AOT regressions | AOT publish gate from M0; pin versions; no reflection fallback | M0-M3 |
| Public API churn | Samples/docs rework | Public API analyzer, review, 0.x preview policy | M0-M12 |
| License/dependency ambiguity | Cannot publish | MIT selected in M0; third-party notice policy; no vendoring before review | M0 |

## 9. Post-1.0

These are ordered by expected value, not by date.

### Audio (A1-A3)

- **A1 — Audio bake.** Decode WAV/OGG/MP3/FLAC/AIFF with FFmpeg, trim/loudness metadata, encode
  AAC-LC, pack entries, incremental hashing, CLI diagnostics.
- **A2 — Audio runtime.** AAC decode in the engine, SDL3 audio device, one mixer thread, voice
  pool, master volume, play/pause/stop/seek, streaming for music, device hotplug.
- **A3 — Spatial audio.** Listener from a camera, 2D/3D sources, distance attenuation, panning,
  Doppler, HRTF, occlusion/reverb later.

Audio defines `ctx.Audio`, `AudioClip`, `AudioSource`, `AudioListener`, and the post-1.0 version of
`Load<AudioClip>`. It must not change the v1 rendering API.

### Other post-1.0 work

- Skeletal and morph animation; animation asset pipeline; model animation playback.
- Built-in forward lighting, PBR, shadow maps, skybox, fog, and a post-processing stack.
- Rigid-body physics and a collision world; v1 only has queries.
- UI toolkit (retained or immediate mode) built on orthographic cameras and text.
- Custom vertex/instance layouts; compute shaders; GPU-driven culling.
- Fixed timestep with render interpolation.
- Hot reload for shaders/assets in development.
- macOS via MoltenVK; Windows/Linux ARM64; mobile and web backends through the renderer seam.
- VR/OpenXR.
- Multiple windows; editor tooling; scripting/mods.
