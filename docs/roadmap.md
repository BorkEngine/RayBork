# RayBork Roadmap

Status: **v0.1 M5 as-built**, 2026-11-14. M0-M5 are shipped. Milestones are ordered by
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
   (WSL) from a Native AOT publish.
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
| M6 | Text and MSDF | TTF/OTF, runtime MSDF, layout, world-space text | M4 |
| M7 | 2D and orthographic rendering | Sprites, 2D shapes, pixel mapping, Pong | M4 |
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
the threading/lifecycle contract testable before the GPU enters the picture.

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
- Native AOT publish runs the M2 sample on Windows and WSL with hardware Vulkan.
- A software-Vulkan (lavapipe) CI job runs the same clear test under Xvfb, if the CI host has no
  GPU.

**Notes.** The M2 AgX curve was normalized to display white and did not use the reported HDR
headroom, so HDR highlights stopped at paper white. `TonemappingMode.None` could write above
paper white for direct authoring. M5 reworked the tone mapper so the curve's output maximum is the
reported headroom on the scRGB path; see the M5 as-built and
[Rendering: Camera](api/rendering/camera.md#72-projection-clear-hdr-and-tone-mapping).

**Risks.** WSL's Vulkan 1.2 implementation may expose a different feature set than native Windows.
HDR swapchain support is highly driver-dependent; the SDR fallback is the guaranteed path. The
device policy is tested on both before M4 relies on bindless.

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
  hardware, native Linux, and WSLg dzn hardware.
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
  The array is fixed-size rather than a runtime array: dzn's DXIL lowering needs a declared resource
  range, and dzn's sampler heap is limited to 2048 descriptors. Samplers are cached per device and
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
  mesh shader, and must exit `0` on the Windows hardware driver, native Linux, and the WSLg dzn
  hardware path. The sample is a plain user-facing example and contains no test-only draw assertion.
  `samples/HelloInterop` has the same per-sample layout and build-time bake and remains the M1-M3
  diagnostic host.
- **Castle sample scope.** The sample is an orbitable castle scene with a model, its diffuse
  texture, an orbital camera, and the reference XZ grid. It deliberately leaves out features that
  need later milestones: model/texture drag-and-drop (post-1.0 platform work), click selection and a
  wire-box overlay (the engine ships `Collision.GetRayBox` and, from M5, `Camera.DrawBoundingBox` and
  `Camera.DrawWireCube`; the sample does not implement selection), and the
  `DrawText`/`DrawFPS` overlay (runtime text arrives in M6). The grid is
  `DrawGrid(20, 10f, Color.Gray)` in the same opaque, depth-tested pass as the castle. The model and
  its diffuse texture are drawn with the built-in `/shaders/mesh` material that the loaded model
  owns; there is no custom Castle shader. The sample draws only the castle and its grid; render
  statistics are available to games but are not drawn. The castle model and texture are
  sample-only assets with their own notice under `samples/Castle/`; that notice is deliberately not
  duplicated in the repository-root notices.
- **WSLg dzn compatibility.** On the WSL gate host WSLg selects
  `Microsoft Direct3D12 (AMD Radeon(TM) 8060S Graphics)` through Mesa's dzn driver and prints
  "dzn is not a conformant Vulkan implementation, testing use only". The original runtime
  `RayBorkTextures[]` declaration crashed dzn's DXIL lowering on the first geometry pipeline
  (`nir_to_dxil.c: emit_createhandle_call_dynamic` asserted "Resource access for undeclared range");
  a 4096-slot combined-sampler layout also exceeds dzn's 2048-descriptor sampler heap. The shipped
  fixed `RayBorkTextures[2048]` array gives dzn a declared resource range and exactly fills one
  sampler heap, so the published host now completes the 30-frame hardware gate on dzn with 2 draw
  calls, 2 instances, and 7168 triangles, with no `DZN_DEBUG` workaround. The validation layer's
  expected `WARNING-Shader-OutputNotConsumed` performance warnings for unused fixed vertex-layout
  attributes still appear; they are not errors. The Windows hardware gate passes on the same GPU,
  and the conformant lavapipe ICD remains available for a software-only Linux run.
- **M4 limitation: one frame block per frame slot.** The renderer writes one 256-byte `rbFrame`
  block per frame slot. A frame that records more than one camera pass overwrites that block for
  each pass, so every pass in that frame sees the last recorded pass' view-projection, view,
  projection, camera position, and viewport. Single-camera frames are correct; split-screen
  cameras and multiple cameras with different views need per-pass frame data, which lands with the
  M8 multiple-camera work. This is the honest M4 limitation, not an M8 feature already shipped.

**Risks.** This is the largest milestone. The shader ABI and per-instance layout can force changes
in the bake, the renderer, and the sample at the same time. Prototype the ABI on two shaders before
building the full batching path. The as-built WSLg dzn issue was fixed by the fixed-size texture
array; the remaining device constraint is the 2048-slot sampler heap, which the engine enforces at
device selection instead of clamping silently.

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
- `DrawTexture` moves to M7; the M5 surface does not include the screen-space texture helper.

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
- The suite is 1,881 tests, 92 of them GPU integration tests.
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
  zero-area triangles. `DrawRay` draws `ray.Origin` to `ray.Origin + ray.Direction` (the direction is
  not normalized); `DrawBoundingBox` draws the twelve axis-aligned edges and skips the coincident
  edges of a zero-extent box.
  `DrawCube` and `DrawWireCube` scale the unit cube by `size` around `position`, with explicit
  width/height/length and `Transform` forms. `DrawSphere` instances the unit UV sphere (`32` slices by
  `16` rings, no LOD) or composes a tessellated `rings` by `slices` surface
  (`2 * slices * (rings - 1)` triangles); `DrawWireSphere` is three great circles in the simple form
  and `slices * (2 * rings - 1)` line instances in the tessellated form. `DrawCylinder` records
  `sides` side quads plus `2 * sides` cap triangles with the base cap at `position` and the axis along
  world `+Y`; the full form accepts unequal top/bottom radii (a tapered cylinder is one triangle draw
  with `4 * sides` triangles, and a zero radius at one end is a cone with `2 * sides` triangles), and
  the start/end form treats the endpoints as cap centers. `DrawWireCylinder` records one vertical edge
  per side plus one closed ring per non-zero radius. `DrawCapsule` is `slices` side quads plus
  `2 * slices * (2 * rings - 1)` cap triangles in two instanced draws; the start/end form treats
  `start` and `end` as spherical cap centers, and the `Transform` form takes the cap-center distance
  as `height`. `DrawWireCapsule` records `slices * (4 * rings + 1)` line instances. `DrawPlane`
  scales the `XZ` unit plane, whose front faces world `+Y`. `DrawCircle` is a closed 36-segment
  circle in the local `XZ` plane with local `+Y` normal. `DrawGrid` is one instanced line draw with
  `2 * (slices + 1)` instances spanning `slices * spacing` per axis. Every helper works in world
  space; each mesh is a unit reference, and the instance transform carries the shape. The eight meshes
  are engine-owned, built once before `IGame.Setup` through the procedural GPU bridge from
  deterministic CPU geometry, and are not public assets. The tessellated sphere and wire sphere,
  cylinder and wire cylinder, capsule and wire capsule, and circle compose their geometry from the
  unit quad, triangle, and line meshes.
- **Overloads and Transform composition.** The primitive families use ordinary C# overloads; the
  engine has no `Ex`, `Rec`, `Pro`, or `V` suffix names. Every transformable solid or wire shape
  (cube, sphere, plane, cylinder, capsule, circle, wire cube, and wire sphere) has a shape/position
  overload and a `Transform` overload; the line, point, triangle, strip, ray, bounding box, and grid
  helpers are world-space draws without a `Transform` form. The transform supplies position,
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
  immediately afterwards, and the frame record retains the texture. `EngineDefaults` is unchanged: it
  still exposes only `CreateMeshShader`, `White`, and `Error`. `/shaders/primitive` is an ordinary
  built-in shader entry, so a game can load a material over it by path for its own vertex-color
  meshes; the primitive meshes have no public accessor. `DrawTexture` is not built or shipped in M5;
  the screen-space overlay path is re-homed to M7.
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
  on the shared diagonal; a degenerate quad is a miss, and the direction is not required to be
  normalized. A ray/plane query normalizes the plane normal and misses when the ray is parallel,
  including when it lies in the plane. `RayCollision.Distance` is the ray parameter, which is a world
  distance only for a normalized direction, and a miss is `default(RayCollision)`. `GetRayMesh`
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
  assertion. The M5 suite is 1,881 tests, 92 of them GPU integration tests, and every sample solution
  builds.
- **DrawTexture boundary.** `DrawTexture` is not part of M5. The preview screen-space overlay helper
  is removed and re-homed to M7, where it returns with the orthographic/sprite contract and
  destination rectangles; the M5 surface has no screen-space texture draw. The migration note is in
  [Versioning](api/versioning.md#14-versioning-and-compatibility).
- **Expected validation warnings.** The validation layer reports two performance warnings on the
  gate path; neither is an error and neither fails the gate. The fixed vertex layout declares
  attributes a shader does not consume, and the primitive program's vertex stage writes the engine
  instance-index varying that its fragment stage never reads, because that stage needs no
  per-instance data. The draw tests assert that no validation *errors* are recorded.
- **Castle and the M4 forward references.** `samples/Castle` keeps its orbitable scene and adds
  `DrawGrid(20, 10f, Color.Gray)` under the model in the same opaque, depth-tested pass, so the
  reference XZ grid is no longer deferred. Two M4 forward references are corrected: the grid ships in
  M5, and `DrawBoundingBox` now ships as a public M5 helper. The Castle sample still does not
  implement click selection, so it does not use the new helper. Castle remains a buildable and
  publishable sample; the M5 AOT gate is the gallery. The M4 limitation of one `rbFrame` block per
  frame slot is unchanged by M5.

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

**Goal.** World-space text that looks correct at arbitrary sizes, with no pre-baked atlas.

**Scope.**

- TTF/OTF face parsing: cmap, glyf/CFF outlines, metrics, kerning, composite glyphs, replacement
  glyph.
- Runtime MSDF generation: one texture per glyph, 32 px longest side, range/padding, deterministic
  field output, validation against a sampled distance field.
- Glyph cache with bounds, eviction, preload ranges, and per-font bindless textures.
- Layout: advance, kerning, line height, alignment, word/character wrapping, max width, measurement.
- MSDF shader: median reconstruction, screen-space antialiasing, linear tint, gamma-correct
  sampling in the HDR scene target.
- `DrawText` world-plane orientation and `DrawTextBillboard`.
- Missing-glyph fallback to the replacement glyph; no complex shaping in v1.
- Font sidecar ranges and MSDF parameters; preload ASCII on load.

**Exit criteria.**

- The Text sample renders a paragraph at multiple sizes, rotations, alignments, and wraps.
- Glyphs remain sharp at large sizes and readable at small sizes; no atlas seams.
- A glyph generated lazily during a tick appears without corrupting the frame; preloading avoids
  the hitch.
- Kerning matches the font's metrics within tolerance for a test string.
- A font with 5,000+ mapped glyphs loads without generating all of them.
- The glyph cache does not exceed the configured texture budget; eviction is correct.
- Native AOT publish renders text on Windows and Linux.
- Fuzz tests cover malformed cmap/glyf/CFF tables without crashes or unbounded allocation.

**Risks.** Managed TrueType/CFF parsing and MSDF generation are the highest-risk runtime libraries
in the project. If quality or performance misses, the fallback is a pinned native msdfgen/FreeType
path, which changes the AOT/native packaging story.

### M7 — 2D, orthographic rendering, and sprites

**Goal.** Raylib-style 2D games are practical without a separate 2D renderer.

**Scope.**

- Orthographic camera whose visible height maps world units to pixels; pixel-perfect mapping for
  the default window size and DPI.
- `DrawTexture` with destination rectangles, linear tint, and point/linear sampling, re-homed from
  M5 with the orthographic/sprite contract.
- 2D primitives: lines, rectangles, circles, triangles, and grids, built on the same draw path.
- 2D text through the M6 font path.
- Sprite batching with bindless textures and per-instance UV/color.
- Screen-space overlay pass (an orthographic camera with no depth write).
- HDR scene target and AgX present apply to 2D as well; LDR sprites are still authored linearly.
- DPI and resize behavior; letterboxing is a game choice, not an engine mode.
- Pong sample: two paddles, ball, score text, keyboard/gamepad input, pause, restart.

**Exit criteria.**

- Pong runs at 1280x720 on Windows and WSL, resizes correctly, and keeps a stable aspect.
- A texture drawn at integer pixel coordinates is pixel-aligned at 100% DPI.
- A sprite sheet or multiple textures batch into one draw when mesh/shader/state match.
- Text renders correctly in the same frame as sprites.
- The overlay pass does not disturb 3D depth.
- Native AOT publish runs Pong on Windows and Linux.

### M8 — Render targets, multiple cameras, and readback

**Goal.** Offscreen rendering, post-processing building blocks, split-screen, and screenshots.

**Scope.**

- `RenderTexture` creation with `Rgba16Float` default, color/depth attachments, MSAA resolve,
  resize, sampling, and lifetime.
- Camera target routing and viewport rectangles.
- Image and resolve barriers between passes; write-after-read validation.
- Multiple cameras to the HDR scene target and to offscreen targets.
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

- A text field sample accepts typed text and committed IME text on Windows and WSL.
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

- Frame pacing: Mailbox/FIFO/Immediate, target FPS, latency measurement.
- Pipeline cache persisted to disk; cache invalidation on driver/version change.
- Buffer/descriptor/staging pools; no steady-state per-frame allocations in the renderer.
- Bindless slot reuse and retirement; memory budget counters.
- `RenderStats` overlay in a sample; GPU timestamps where supported.
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

- Samples: Castle (3D textured/custom shader/HDR; the sample and its assets executable ship in M4,
  and M11 polishes them), Pong (2D/input/text), Text (MSDF/layout), Instancing (batching/stats),
  RenderTarget (offscreen/MSAA/post-process).
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

- All five samples run on Windows and WSL from a clean checkout and from the packaged NuGet.
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
- CI matrix: Windows hardware Vulkan, WSL hardware Vulkan, lavapipe software Vulkan, AOT smoke,
  added to GitHub Actions after the first sample.
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
| Drawing (2D/3D begin/end) | Covered | M4, M7 | Cameras are the begin/end scope; no global draw state. |
| Shader load/uniforms | Covered | M4 | SPIR-V at bake; `SetUniform` snapshot; entry points `main_vertex`/`main_fragment`. |
| Screen-space helpers | Covered | M4, M7 | `ScreenToWorldRay`, `WorldToScreen`, orthographic cameras. |
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
| Text/fonts | Covered | M6 | Runtime MSDF; Latin-style shaping; no bidi/complex scripts. |
| Textures | Covered | M3, M5, M7, M8 | Loaded textures, billboard texture sampling, screen-space sprites, and HDR/MSAA render targets. |
| Images | Partial | M3 | Bake decodes; no runtime image manipulation API. |
| Models | Covered | M4, M5 | glTF 2.0/OBJ are source formats; packs contain the engine format; static meshes; wireframe model drawing. |
| Custom data | Covered | M3 | TOML data with `[GenerateShape]`; `WithAssetType<T>` then `Load<T>`. |
| Meshes | Covered | M3, M4, M5 | Loaded meshes, procedural `MeshBuilder` creation including the nine `Generate*` factories, and primitive meshes; fixed vertex layout. |
| Materials | Rethought | M4 | No `Material`; shader state + per-instance uniforms. |
| Model animations | Post | — | Skeletal/morph post-1.0. |
| 3D shapes | Covered | M5 | Lines, points, triangles, strips, rays, bounding boxes, cube, sphere, plane, cylinder/wire cylinder, capsule/wire capsule, circle, grid, and wire shapes, with shape/position and `Transform` forms. |
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
  the baked payload by codec and loader tests. The M5 suite is 1,881 tests, 92 of them GPU
  integration tests; every sample solution builds.
- The HDR AgX rework is covered by unit tests that pin the combined matrices, the Godot curve
  parameters, neutral-gray preservation, the crossover continuity, the SDR and scRGB responses, and
  the present shader source, plus the `HdrPresentGpuTests` presented-pixel probes for the SDR image,
  scRGB paper white and peak at two headrooms, the frame tone-mapping snapshot, and the direct
  `None` path. A host without the scRGB swapchain path reports the test as a skip with its actual
  selected format.
- The architecture tests enforce the dependency rules, the no-native/backend-types-in-public-API
  rule, and the absence of a public `Material`.
- CI runs on Windows and WSL/Linux. The WSL job uses the hardware Vulkan path when available and
  lavapipe otherwise.
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
| WSL Vulkan 1.2 feature gaps | Bindless/instancing unavailable on a target platform | Feature-probe early in M2; document required device features; test both native and WSL | M2 |
| HDR swapchain variability | HDR path unavailable or inconsistent | Prefer HDR, fall back to sRGB; report `EngineInfo.HdrOutput`; test both | M2/M10 |
| HDR tone mapping is display-dependent | HDR highlights are capped at the reported display headroom; HDR10/PQ output is deferred | The AgX curve's output maximum is the reported headroom on scRGB, so the curve itself reaches the display peak; HDR10/PQ remains deferred and is never selected; probe paper white and peak on an HDR display | M2/M4/M5 |
| Wireframe needs `fillModeNonSolid` | `DrawWireModel` unavailable on a device without the feature | Feature-probe at device creation; `RB3001` names the device and feature; no silent fallback; GPU tests cover the pipeline split | M5 |
| SDR AgX differs from the M4 image | Exposure or AgX white tuned against M4 changes the image | Pinned CPU-reference and presented-pixel tests; runtime exposure/white/contrast; migration note | M5 |
| sRGB-authored preset values | `Gray`/`Grey`, `Yellow`, and `DarkCyan` change from their earlier linear constants | 0.x migration note; aliases; use `new Color` or `FromSrgb` for an intentional raw value | M5 |
| MSAA + HDR resolve | Extra memory/bandwidth or resolve bugs | 4x default, 1/2/4/8 support or fallback; resolve tests; validation | M2/M8 |
| Shader ABI complexity | Bake/runtime/sample churn; custom shaders hard to author | Prototype two shaders before M4 batching; explicit sidecar; publish ABI guide | M4 |
| Per-instance uniform limits | Batches split unexpectedly; performance regressions | Size estimator, device-limit checks, documented split behavior, per-draw fallback | M4 |
| Managed font/MSDF library | Quality or performance miss; AOT/packaging surprises | Benchmark early; keep native fallback scoped; fuzz parser | M6 |
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
