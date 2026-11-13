# Assets

[API overview](../api.md) · [document map](../api.md#document-map)

## 6. Assets

### 6.1 `IAsset` and `IAssetCatalog`

```csharp
public interface IAsset : IDisposable {
    string Path { get; }
    bool IsDisposed { get; }
}

public interface IAssetCatalog {
    Task<T> Load<T>(string path, CancellationToken cancellationToken = default);
    Task<byte[]> LoadData(string path, CancellationToken cancellationToken = default);
    bool Exists(string path);
}

public enum AssetEntryType : ushort {
    Raw = 0,
    Mesh = 1,
    Model = 2,
    Texture = 3,
    Shader = 4,
    Font = 5,
    Data = 6,
}

public interface IAssetLoader<T> {
    AssetEntryType EntryType { get; }
    Task<T> LoadAsync(AssetLoadContext context);
}

public sealed class AssetLoadContext {
    public string Path { get; }
    public CancellationToken CancellationToken { get; }
    public Task<byte[]> LoadPayloadAsync();
    public Task<byte[]> LoadDataAsync(string path, CancellationToken cancellationToken = default);
    public Task<T> LoadAsync<T>(string path, CancellationToken cancellationToken = default);
    public bool Exists(string path);
}
```

`AssetEntryType` is the asset type stored in a pack index entry. The numeric values are part of the
RAYBORKP v1 wire format:

| Value | Entry |
| --- | --- |
| `Raw` | An opaque payload with no descriptor, written by `AddData` or an unclaimed raw source. Read it with `LoadData`. |
| `Data` | A MessagePack custom-data payload written by `AddAsset<T>` for a type that is not an engine descriptor. |
| `Mesh`, `Model`, `Texture`, `Shader`, `Font` | The baked descriptor for the matching built-in engine asset format. |

`IAssetLoader<T>` is the custom loader contract registered with `WithAssetLoader<T>`. `EntryType` is
the pack entry type the loader accepts; the catalog rejects a different type with
`AssetTypeMismatchException` before it invokes the loader. Every `Load<T>` call invokes the loader and
returns a fresh caller-owned value; the catalog never caches the result. `AssetLoadContext` carries
the canonical path, the linked caller/catalog cancellation token, the entry payload, and the nested
load methods. The loader contract, threading rules, and error behavior are in §6.7.

There is one typed loader. `Load<T>` handles both engine assets and registered custom assets:

| `T` | Behavior |
| --- | --- |
| `Mesh`, `Texture` | Loads a shared, reference-counted engine asset. Completes after GPU upload. `T` is an `IAsset` and must be disposed once per successful load. |
| `Shader` (M4) | Returns a **new independent material instance** every call, backed by one cached internal shader program for the path. `Shader` is not an `IAsset` and is never reference-counted. Dispose every material exactly once. |
| `Model` (M4) | Returns a **new `Model` wrapper** every call over the shared cached part meshes and programs, with one new material per part. `Model` is not an `IAsset`, is never reference-counted, and owns its part materials. Dispose every returned model exactly once. |
| `Font` (M6), later `AudioClip` | The shared, reference-counted engine-asset contract; the types arrive with their milestones. |
| A custom type registered with `WithAssetType<T>()` | Deserializes a MessagePack `Data` entry through the compiled `[GenerateShape]`. Returns a fresh caller-owned instance per call; no reference counting and no `Dispose`. |
| A custom type registered with `WithAssetLoader<T>` | Invokes the registered loader with the entry payload and returns its fresh caller-owned value. The loader declares the entry type it accepts, so the entry kind is not limited to `Data`. |
| Any other type | `NotSupportedException` naming the type and asking for `WithAssetType<T>()` or `WithAssetLoader<T>()`. |

> **M4 shader semantics.** The GPU program is an internal engine asset: there is no public
> shader-program type and no public program handle. `Load<Shader>` caches the internal program by
> `(canonical path, internal program type)` and coalesces program loads; it then constructs the
> material. Two materials over one program batch together, and disposing one never invalidates the
> other. See [Rendering: Shaders](rendering/shaders.md#73-shader-materials-and-setuniform).

`IAsset.Path` is the canonical path with a leading `/`; `Load` accepts the same path with or without
it. A disposed asset keeps `Path` and `IsDisposed` readable for diagnostics; every other member
throws `ObjectDisposedException`.

A custom type registration is explicit on the builder:

```csharp
using PolyType;

[GenerateShape]
public sealed partial class LevelConfig {
    public string Name { get; init; } = "";
    public int Difficulty { get; init; }
}
```

```csharp
using var app = new RayBorkAppBuilder()
    .WithGame(game)
    .WithAssetType<LevelConfig>()
    .WithAssetsPack(packPath)
    .Build();

LevelConfig level = await ctx.Assets.Load<LevelConfig>("/data/level");
ReadOnlySpan<byte> raw = await ctx.Assets.LoadData("/data/level-raw");
```

`WithAssetType<T>()` requires `T : IShapeable<T>` and registers a non-reflective shape for a
MessagePack `Data` entry. The same type must be registered in the bake with `RegisterAsset<T>()`; the
two registrations are independent because the runtime never scans assemblies. `WithAssetLoader<T>()`
registers an arbitrary `IAssetLoader<T>` for a type without a compiled shape; the loader decides how
to decode the payload, and the bake writes the matching entry with `AddData` or `AddAsset<T>`. A type
can have one registration only: registering the same type through both methods is an
`InvalidOperationException` from the second call.

`Load<T>` is not cached for a registered custom type; every call returns a new instance. Engine assets
remain cached and reference-counted by `(canonical path, engine asset type)`. `Load<Shader>` is the
exception to the reference-counted return shape: it caches the internal program and returns a new
material per call, because a material carries a mutable cursor and must never be shared implicitly.

`LoadData` returns a copy of a raw or decompressed payload for custom formats and tooling. There is
no `TryLoad` in v1: catch `AssetNotFoundException` when absence is expected.

### 6.2 Asset paths

| Rule | Example |
| --- | --- |
| Optional leading `/` | `/models/castle` and `models/castle` are the same |
| Forward slashes only | `/textures/castle/albedo` |
| Case-insensitive input | `/Textures/Castle` resolves `/textures/castle` |
| Canonical storage is lowercase | The bake and pack index store lowercase |
| No extension | `/shaders/castle`, not `/shaders/castle.glsl` |
| No `.` or `..` segments | |
| No trailing slash | |
| Segments use ASCII lowercase letters, digits, `_`, `-` | Bork2d-compatible canonical grammar |
| Maximum length is 4096 bytes | |

The runtime normalizes input by stripping one optional leading slash and lowercasing with invariant
rules. Backslashes are rejected; use forward slashes. The canonical path is lowercase ASCII with `/`
separators. A path that normalizes to the same key as another entry is a collision; the bake reports
it and does not publish the pack.

A canonical path is an asset identity. Two different source files cannot produce the same path in
one pack. Two packs may contain the same path; the later-mounted pack wins. The public `IAsset.Path`
is the canonical path with a leading `/`; the pack index stores it without one.

The `_procedural` first segment is reserved for assets created at runtime. A procedural `Mesh` or
`Texture` has a synthetic path under `/_procedural/mesh/` or `/_procedural/texture/`, never appears
in the catalog, and cannot be loaded with `Load<T>`. The bake must not publish an entry under that
prefix.

### 6.3 Asset types

| Type | Baked from | Runtime form | Milestone |
| --- | --- | --- | --- |
| `Mesh` | One primitive from glTF 2.0 or OBJ, or a `MeshBuilder` | GPU vertex/index buffers; CPU vertices and indices retained for collision and inspection | M3 |
| `Texture` | PNG, JPEG, TGA, BMP, or a `TextureBuilder` | GPU-ready KTX2 payload or procedural uncompressed levels, mips, sRGB flag, sampler state | M3 |
| `Shader` | GLSL vertex + fragment pair | Internal SPIR-V program plus sidecar metadata, entry points, and a default render state; public `Shader` materials are created over it | M3 program, M4 material |
| `Model` | glTF 2.0, OBJ | Multi-part wrapper with per-part materials over shared cached meshes and programs; not an engine asset | M4 (baked in M3) |
| `Font` | TTF, OTF | Face metrics plus runtime-generated MSDF glyphs | M6 (baked in M3) |
| Custom data | TOML source | MessagePack payload deserialized through `Load<T>` after `WithAssetType<T>()` | M3 |
| Custom asset | Any entry kind | Decoded by an `IAssetLoader<T>` after `WithAssetLoader<T>()`; `Load<T>` returns a fresh caller-owned value | M3 |
| Raw data | Any processor output or raw source | Opaque bytes through `LoadData` | M3 |

glTF and OBJ are **source formats only**. The bake converts them to the engine's packed mesh/model
format; the runtime never parses glTF or OBJ. The engine texture payload is a complete KTX2 2.0
file; PNG/JPEG/TGA/BMP have been the texture source formats since M3, and the runtime only reads the
packed KTX2 payload. KTX2 source passthrough is a later addition, not a supported source format.

`RenderTexture` is created at runtime and is not a pack asset. `AudioClip` is post-1.0.

`Model` is the normal entry point for a glTF/OBJ file from M4. A model's parts are separate `Mesh`
assets at `<model-path>/<part>`; the bake emits and validates both the model descriptor and the part
mesh entries. `Load<Model>` loads the model's referenced meshes and creates one material per part from
the part's shader program path; the model owns those materials. Disposing the model disposes its own
materials and releases its own mesh references. If the game also loaded one of those part meshes
directly, it holds a separate reference and must dispose it separately. A part's material is never
shared with another model: two models that name the same part program each get their own materials
over the one cached internal program.

`Model` is not an `IAsset`: it has no path, no cache entry, and no reference count. Every
`Load<Model>` call returns a new wrapper over the shared cached part meshes and programs, so two loads
never share the model or its materials even though they share the underlying GPU resources. A failed
or canceled model load disposes every material and releases every mesh reference it already acquired;
a retry starts a fresh wrapper. See [Rendering: Meshes](rendering/meshes.md#75-mesh-and-model).

`Load<Mesh>` is for a standalone mesh-part asset. Loading a multi-part model path as `Mesh` throws
`AssetTypeMismatchException`.

Texture formats in M4 are `Rgba8Unorm`, `Rgba8Srgb`, `Bc7Unorm`, `Bc7Srgb`, `Bc5Unorm`, and
`R8Unorm`. `Rgba16Float` and `Depth32Float` are render-target formats and are not accepted in a
packed texture. The bake writes an sRGB transfer function into the KTX2 data format descriptor for
the sRGB formats; the runtime creates the matching Vulkan format.

### 6.4 Identity, reference counting, and lifetime

`Load<T>` for engine assets is cached by `(canonical path, engine asset type)`:

- The first successful load creates the asset and returns it.
- A second load of the same path and type increments the reference count and returns the **same
  object**.
- A load of the same path with a different engine asset type throws `AssetTypeMismatchException`.
- `Dispose` releases one reference. Once the count reaches zero, the public object becomes
  disposed and GPU destruction is queued on the render thread.
- A frame packet that uses an engine asset holds its own reference for the lifetime of that frame.
  Recording a draw calls an internal retain on each referenced asset; the frame ring releases the
  hold after the frame is presented. Disposing an asset immediately after recording a draw is
  therefore safe; the frame keeps the GPU resource alive until its fence signals. The M4
  `Camera.Draw*` path calls the retain/release mechanism for every recorded mesh, internal shader
  program, and bound texture. A model and a material are not engine assets and are not retained.
- A device recovery re-uploads the live GPU resources from the retained CPU descriptors before
  frame submission resumes. A public asset object stays valid across the recovery.
- Using a disposed engine asset (reading a property or passing it to a draw) throws
  `ObjectDisposedException`.

`Shader` and `Model` are the exceptions to the shared-object rule for their public types.
`Load<Shader>` caches the internal program and returns a **new material per call**:

- The program is cached by `(canonical path, internal program type)` and reference-counted. Every
  successful `Load<Shader>` acquires one program reference for the material it creates, and the
  material releases it exactly once on `Dispose`.
- Two materials over the same path are distinct objects, are not `IAsset`s, and have independent
  uniform cursors and texture bindings.
- Disposing one material leaves every other material over the same program usable; the program's GPU
  resource is destroyed when the last material reference and every frame hold are gone.
- A frame that recorded a draw holds the internal program and the bound textures, not the material
  object. Disposing the material immediately after recording is safe.
- `Load<Shader>` cancellation follows the same rule: a caller that cancels before the material is
  constructed acquires no material and releases its program reference. An already-canceled caller
  does not receive a material even when the program is cached.
- The engine preloads `/shaders/mesh` and the white/error textures into `EngineContext.Defaults`; see
  [Rendering: Shaders](rendering/shaders.md#73-shader-materials-and-setuniform).

`Load<Model>` follows the same shape for a different reason: it loads or reuses every part mesh and
the internal program of every part material through the engine-asset cache, then returns a **new
`Model` wrapper** with one new material per part. A model is caller-owned, is not an `IAsset`, and has
no reference count; the wrapper acquires one reference to every distinct part mesh and owns every
distinct part material. Disposing the model disposes its materials and releases its mesh references
exactly once. A canceled model load releases every part it already acquired before reporting
`OperationCanceledException`.

Because `Load<T>` returns the shared object for engine assets, `Dispose` must be called **exactly
once for each successful engine-asset load**. The engine cannot distinguish a double-dispose from a
second owner's release; a second `Dispose` after the count reaches zero throws. A debug build also
checks the count under the catalog lock.

A custom data type loaded through `WithAssetType<T>()` is not an `IAsset`. It is a caller-owned
value, has no cache, and has no `Dispose`. It never keeps a GPU resource alive.

A value produced by a `WithAssetLoader<T>` loader is also caller-owned, uncached, and not an
`IAsset`. Any engine asset the loader loaded through its context belongs to the returned value; the
value must release those references when it is disposed or discarded.

Loading the same engine path concurrently is coalesced: one load runs, all callers receive the same
result and each caller owns one reference. `Load<Shader>` coalesces the program load the same way but
returns one new material per caller. `Load<Model>` coalesces the shared part mesh and program loads
but never coalesces the model itself: every caller gets its own wrapper and materials. If the load
fails, every caller observes the failure and no reference is created. A caller that cancels its wait
does not acquire a reference, even if the shared load later succeeds. Loads through a registered
loader are independent; each call produces a new instance.

`LoadData` returns the decompressed bytes of the winning entry. For a typed engine asset entry that
is the serialized descriptor, not the GPU payload; tooling that needs a raw texture or mesh payload
uses the payload entry path emitted by the bake. For a `[raybork]` data entry it is the MessagePack
bytes.

### 6.5 Load pipeline and errors

```text
Load<T>(path)
  -> resolve winning entry in the mounted pack stack
  -> resolve the registration for T in the merged registry
  -> Mesh, Texture  -> read/decompress -> descriptor -> queue GPU upload
                    -> loader continues with the next request
                    -> on upload completion -> construct -> refcount -> complete Load
  -> Shader         -> load the internal program through the engine-asset path
                    -> acquire one program reference for the new material -> return a new material
  -> Model          -> resolve every part mesh and internal program through the engine-asset path
                    -> create one material per part -> return a new model wrapper
  -> registered     -> read/decompress -> check the entry type
                    -> IAssetLoader<T>.LoadAsync(context) -> reject a null result
                    -> return a fresh caller-owned value
  -> otherwise      -> NotSupportedException naming both registration methods
```

Consequences:

- `Load<T>` for engine assets is asynchronous. The read runs on the dedicated `RayBork.Asset`
  thread; the upload is queued to the render thread. The loader does not wait for the GPU copy and
  immediately serves the next request; the load task completes only after the upload succeeds.
- The render thread records and submits uploads without waiting for their fences. It polls
  completion on later loop turns, so frames continue to submit and present while an upload is in
  flight. A bounded wait of a few milliseconds is used only while upload work is queued or in
  flight.
- Engine assets do not return half-uploaded, so the first draw never triggers a hidden upload.
- At most eight GPU uploads and 256 MiB of estimated upload payload are in flight at once. A single
  upload larger than the byte budget may start when nothing is in flight so a load always makes
  progress. Further requests wait in the loader queue; the loader keeps serving custom data and
  `LoadData` while engine uploads are pending.
- Custom data is CPU-only and can complete without the renderer; it is still a `Task` because pack
  I/O runs on the loader.
- A `WithAssetLoader<T>` value runs its loader's synchronous part on the `RayBork.Asset` thread; a
  continuation after an `await` may resume on the thread pool. The value is caller-owned and never
  cached. The loader must not block and must not call SDL, Vulkan, or tick-thread APIs. See §6.7.
- `Load` may be called from any thread, but awaiting it from the tick thread blocks that tick's
  continuation, not the loader or render thread. Load in `Setup` unless a hitch is acceptable.
- Cancellation before completion releases all partial resources and throws
  `OperationCanceledException`. A canceled caller does not acquire a reference. A canceled model load
  disposes the materials it created and releases the mesh references it acquired. Cancellation after
  the asset is returned has no effect. A canceled GPU upload destroys the just-created resource on
  the render thread instead of leaking it; a cancellation that arrives after the copy started may
  wait briefly for that fence before cleanup.
- A failed load is not cached as a permanently failed entry; retrying is allowed. A device loss
  cancels in-flight uploads with `AssetLoadException` `RB1010`; the load can be retried once the
  device has recovered.
- The loader is bounded: pack payload reads validate every length and offset before allocation, and
  a decompressed payload larger than its declared size is an `AssetLoadException`.

| Exception | When |
| --- | --- |
| `AssetException` | Base type for every asset pipeline failure with a diagnostic code. |
| `AssetPackException` | The pack is missing, truncated, has a bad magic/version, fails a hash, or has an invalid index. |
| `AssetNotFoundException` | The path is not in any mounted pack. |
| `AssetTypeMismatchException` | The path exists but was baked with a different `AssetEntryType`. |
| `AssetLoadException` | The payload is corrupt, decode fails, a custom-data shape fails, a registered loader fails or returns no value, or GPU upload fails. |
| `OperationCanceledException` | The caller's token was canceled before completion. |
| `ObjectDisposedException` | The catalog or a required dependency is already disposed. |
| `NotSupportedException` | `T` is neither a built-in engine asset nor registered with `WithAssetType<T>()` or `WithAssetLoader<T>()`. |

`AssetPackException` is a startup failure for a pack passed to `WithAssetsPack`. A corrupt entry
inside an otherwise valid pack is an `AssetLoadException` for that path.

### 6.6 Pack mounting and built-in assets

- The engine's built-in pack is mounted first, below every user pack. It contains the white and
  error textures, the present shader pair, and the built-in mesh draw shader program at
  `/shaders/mesh`, produced by the same `IAssetProcessor` pipeline as game content. The app preloads
  the mesh program and the two textures into `EngineContext.Defaults` before `IGame.Setup`; the
  built-in pack replaces the old `/shaders/default` program, and model packs baked against that path
  resolve to `/shaders/mesh` at load time. Primitive meshes arrive with M5 and the missing-glyph
  error glyph with M6.
- `WithAssetsPack` mounts user packs in call order after the built-in pack. Later packs shadow
  earlier packs for the same canonical path.
- The winning entry is the last mounted pack that contains the path. A shadowed entry is never
  partially used.
- v1 has no runtime pack mount/unmount API. Packs are fixed when the app is built.
- A missing pack file is an `AssetPackException` from `Run`, not from `Build`, because the bake may
  produce the pack as part of the build.
- Relative pack paths resolve against `AppContext.BaseDirectory`, so a published game finds its
  pack next to the executable.
- The built-in pack is generated at engine build time by a JIT-only generator that references
  `RayBork.Bake`, runs the ordinary `bake` command over the checked-in built-in sources into a
  temporary directory, and emits a C# byte array that `RayBork.Engine` compiles into the assembly.
  The generator never enters a runtime project or an AOT publish graph, and no generated pack is
  committed.
- Mounting validates every pack before it becomes visible. A failed mount leaves the previous pack
  stack unchanged, and startup fails with `AssetPackException`.

### 6.7 Custom asset loaders

`WithAssetLoader<T>` registers an `IAssetLoader<T>` when a type needs decoding that is not a
MessagePack custom-data shape. The loader declares the pack entry type it accepts and returns one
caller-owned value per load:

```csharp
using System.Text;
using RayBork;

public sealed class LevelLoader : IAssetLoader<LevelConfig> {
    public AssetEntryType EntryType => AssetEntryType.Raw;

    public async Task<LevelConfig> LoadAsync(AssetLoadContext context) {
        byte[] payload = await context.LoadPayloadAsync().ConfigureAwait(false);
        context.CancellationToken.ThrowIfCancellationRequested();
        return new LevelConfig { Name = Encoding.UTF8.GetString(payload) };
    }
}
```

```csharp
using var app = new RayBorkAppBuilder()
    .WithGame(game)
    .WithAssetLoader(new LevelLoader())
    .WithAssetsPack(packPath)
    .Build();

LevelConfig level = await ctx.Assets.Load<LevelConfig>("/data/level");
ReadOnlySpan<byte> raw = await ctx.Assets.LoadData("/data/level");
```

The registration maps `typeof(T)` to the loader. `EntryType` must match the packed entry; the catalog
rejects a different type with `AssetTypeMismatchException` before the loader runs. Each type may be
registered only once, and built-in engine asset types (`Mesh`, `Texture`, `Shader`, `Model`) are
already registered, so registering one fails at `Build`. `WithAssetType<T>` and
`WithAssetLoader<T>` are mutually exclusive for one type: there is no override.

`AssetLoadContext` is valid only during the `LoadAsync` call that received it:

| Member | Contract |
| --- | --- |
| `Path` | The canonical public path with a leading `/`. |
| `CancellationToken` | The caller token linked with the catalog stop token. |
| `LoadPayloadAsync()` | The entry's decompressed payload. It returns the same read-only array on every call. |
| `LoadDataAsync(path, cancellationToken = default)` | Another entry's decompressed bytes. The context token is used when the argument cannot be canceled. |
| `LoadAsync<T>(path, cancellationToken = default)` | A nested load. The context token is used when the argument cannot be canceled; the loader must not block on it. |
| `Exists(path)` | Whether another path resolves in the mounted packs. |

The entry kind a loader accepts is written by the bake:

| Entry | Written by | Loader |
| --- | --- | --- |
| `Raw` | `AddData`, or an unclaimed source through the raw processor | Declare `AssetEntryType.Raw`; the loader parses the bytes itself. |
| `Data` | `AddAsset<T>` for a shaped type that is not an engine descriptor | Declare `AssetEntryType.Data`; `WithAssetType<T>` is the MessagePack shortcut. |
| `Mesh`, `Model`, `Texture`, `Shader`, `Font` | An engine descriptor processor | Owned by the built-in engine registrations; a user registration for these types fails at `Build`. |

Execution and threading:

- The synchronous part of `LoadAsync` runs on the dedicated `RayBork.Asset` loader thread. A
  continuation after an `await` may resume on the thread pool.
- A loader must not block (no `.Result`, `.Wait()`, or synchronous wait) and must not call SDL,
  Vulkan, or tick-thread APIs.
- One loader instance serves concurrent loads, so it must be stateless or thread-safe.
- A nested load is queued on the same loader thread, and the loader keeps serving the queue while
  the returned task is incomplete. The load chain rejects a loader that requests the asset type and
  path it is currently loading instead of queueing it forever.

Ownership and cancellation:

- Every `Load<T>` call invokes the loader. Results are never cached and are caller-owned.
- Any asset the loader loads through the context stays owned by the returned value. The loader must
  release what it acquired when the load fails or is canceled; the catalog does not track it.
- `AssetLoadContext.CancellationToken` fires when the caller cancels or the catalog stops.
  Cancellation is cooperative: check the token, and release everything acquired before rethrowing.

Errors:

- An entry type mismatch is `AssetTypeMismatchException` before the loader is invoked.
- A loader exception is wrapped as `AssetLoadException` unless it is already an `AssetException`.
- A `null` result is a load failure (`AssetLoadException`, `RB1008`).
- A canceled caller observes `OperationCanceledException`; a catalog stop observes
  `ObjectDisposedException`.
