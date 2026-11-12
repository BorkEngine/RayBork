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
```

There is one typed loader. `Load<T>` handles both engine assets and registered custom data:

| `T` | Behavior |
| --- | --- |
| `Mesh`, `Texture`, `Shader` (M3) | Loads a shared, reference-counted engine asset. Completes after GPU upload. `T` is an `IAsset` and must be disposed once per successful load. |
| `Model` (M4), `Font` (M6), later `AudioClip` | The same shared, reference-counted contract; the types arrive with their milestones. |
| A custom type registered with `WithAssetType<T>()` | Deserializes a MessagePack data entry through the compiled `[GenerateShape]`. Returns a fresh caller-owned instance per call; no reference counting and no `Dispose`. |
| Any other type | `NotSupportedException` naming the type and asking for `WithAssetType<T>()`. |

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

`WithAssetType<T>()` requires `T : IShapeable<T>` and registers a non-reflective loader delegate. The
same type must be registered in the bake with `RegisterAsset<T>()`; the two registrations are
independent because the runtime never scans assemblies.

`Load<T>` is not cached for custom data; every call returns a new instance. Engine assets remain
cached and reference-counted by `(canonical path, engine asset type)`.

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
| `Shader` | GLSL vertex + fragment pair | SPIR-V plus sidecar metadata, entry points, and a default render state | M3 |
| `Model` | glTF 2.0, OBJ | Multi-part model with meshes, local transforms, shader references | M4 (baked in M3) |
| `Font` | TTF, OTF | Face metrics plus runtime-generated MSDF glyphs | M6 (baked in M3) |
| Custom data | TOML source | MessagePack payload deserialized through `Load<T>` after `WithAssetType<T>()` | M3 |
| Raw data | Any processor output or raw source | Opaque bytes through `LoadData` | M3 |

glTF and OBJ are **source formats only**. The bake converts them to the engine's packed mesh/model
format; the runtime never parses glTF or OBJ. The engine texture payload is a complete KTX2 2.0
file; PNG/JPEG/TGA/BMP are the M3 source formats and the runtime only reads the packed KTX2
payload. KTX2 source passthrough is a later addition, not an M3 source format.

`RenderTexture` is created at runtime and is not a pack asset. `AudioClip` is post-1.0.

`Model` is the normal entry point for a glTF/OBJ file from M4. A model's parts are separate `Mesh`
assets at `<model-path>/<part>`; M3 emits and validates both the model descriptor and the part mesh
entries. `Load<Model>` will load the model's referenced meshes and shaders and hold its own
references to them; disposing the model releases those references. If the game also loaded one of
those parts directly, it holds a separate reference and must dispose it separately.

`Load<Mesh>` is for a standalone mesh-part asset. Loading a multi-part model path as `Mesh` throws
`AssetTypeMismatchException`.

Texture formats in M3 are `Rgba8Unorm`, `Rgba8Srgb`, `Bc7Unorm`, `Bc7Srgb`, `Bc5Unorm`, and
`R8Unorm`. `Rgba16Float` and `Depth32Float` are render-target formats and are not accepted in a
packed texture in M3. The bake writes an sRGB transfer function into the KTX2 data format
descriptor for the sRGB formats; the runtime creates the matching Vulkan format.

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
  therefore safe; the frame keeps the GPU resource alive until its fence signals. M3 implements and
  tests the retain/release mechanism directly; M4 wires `Camera.Draw*` into it.
- A device recovery re-uploads the live GPU resources from the retained CPU descriptors before
  frame submission resumes. A public asset object stays valid across the recovery.
- Using a disposed engine asset (reading a property or passing it to a draw) throws
  `ObjectDisposedException`.

Because `Load<T>` returns the shared object for engine assets, `Dispose` must be called **exactly
once for each successful engine-asset load**. The engine cannot distinguish a double-dispose from a
second owner's release; a second `Dispose` after the count reaches zero throws. A debug build also
checks the count under the catalog lock.

A custom data type loaded with `Load<T>` is not an `IAsset`. It is a caller-owned value, has no
cache, and has no `Dispose`. It never keeps a GPU resource alive.

Loading the same engine path concurrently is coalesced: one load runs, all callers receive the same
result and each caller owns one reference. If the load fails, every caller observes the failure
and no reference is created. A caller that cancels its wait does not acquire a reference, even if
the shared load later succeeds. Custom data loads are independent; each call produces a new instance.

`LoadData` returns the decompressed bytes of the winning entry. For a typed engine asset entry that
is the serialized descriptor, not the GPU payload; tooling that needs a raw texture or mesh payload
uses the payload entry path emitted by the bake. For a `[raybork]` data entry it is the MessagePack
bytes.

### 6.5 Load pipeline and errors

```text
Load<T>(path)
  -> resolve winning entry in the mounted pack stack
  -> engine asset?  -> read/decompress -> descriptor -> queue GPU upload
                    -> loader continues with the next request
                    -> on upload completion -> construct -> refcount -> complete Load
  -> registered?    -> read/decompress -> MessagePack shape -> fresh instance -> return
  -> otherwise      -> NotSupportedException
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
- `Load` may be called from any thread, but awaiting it from the tick thread blocks that tick's
  continuation, not the loader or render thread. Load in `Setup` unless a hitch is acceptable.
- Cancellation before completion releases all partial resources and throws
  `OperationCanceledException`. A canceled caller does not acquire a reference. Cancellation after
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
| `AssetTypeMismatchException` | The path exists but was baked as a different engine asset type. |
| `AssetLoadException` | The payload is corrupt, decode fails, a custom-data shape fails, or GPU upload fails. |
| `OperationCanceledException` | The caller's token was canceled before completion. |
| `ObjectDisposedException` | The catalog or a required dependency is already disposed. |
| `NotSupportedException` | `T` is neither an engine asset nor registered with `WithAssetType<T>()`. |

`AssetPackException` is a startup failure for a pack passed to `WithAssetsPack`. A corrupt entry
inside an otherwise valid pack is an `AssetLoadException` for that path.

### 6.6 Pack mounting and built-in assets

- The engine's built-in pack is mounted first, below every user pack. In M3 it contains the
  placeholder white and error textures and the present shader pair, produced by the same
  `IAssetProcessor` pipeline as game content. The default draw shader, primitive meshes, and error
  glyph arrive with M4 and M6.
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
