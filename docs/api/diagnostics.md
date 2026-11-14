# Diagnostics

[API overview](../api.md) · [document map](../api.md#document-map)

## 10. Diagnostics

### 10.1 Logging

RayBork uses `Microsoft.Extensions.Logging` for its public logging contract. `EngineContext` exposes
an `ILoggerFactory`; the default implementation is configured with ZLogger's console provider.

```csharp
using Microsoft.Extensions.Logging;

public async Task Setup(EngineContext ctx) {
    ILogger logger = ctx.LoggerFactory.CreateLogger("Castle");
    logger.LogInformation("Setup started");
}
```

- `RayBorkAppBuilder.WithLoggerFactory(ILoggerFactory)` supplies a caller-owned factory. The app
  does not dispose it.
- If no factory is supplied, the engine creates a ZLogger console factory filtered at
  `WithLogLevel` (default `Information`). The app disposes that factory after `Shutdown`.
- `EngineContext.LoggerFactory` is never `null` and is safe to read from any thread.
- Engine categories are `RayBork.App`, `RayBork.Assets`, `RayBork.Render`, `RayBork.Window`,
  `RayBork.Input`, `RayBork.Font`, and `RayBork.Bake`.
- Recoverable renderer failures are logged at Warning with the recovery action; failed recovery is
  logged at Error before the exception surfaces.
- `ZLogger` is the default provider, not a public abstraction. Games may use `ILoggerFactory`,
  `ILogger`, and any MEL-compatible provider.

### 10.2 `EngineVersion`

```csharp
namespace RayBork;

public static class EngineVersion {
    public static Version Current { get; }
}
```

| Member | Thread | Contract |
| --- | --- | --- |
| `Current` | **Any** | The version of the loaded `RayBork.Engine` assembly. Immutable for the app lifetime. Safe to read from any thread. Throws no `RayBorkException`. |

`Current` is the loaded `RayBork.Engine` assembly's `Version`; it reports `0.1.0.0` in the 0.1
release, including M1 through M5. The build and package version is `0.1.0`. It describes the engine
assembly that is actually loaded, not the game assembly, the pack format, or the shader ABI. The
value cannot change while the process runs, and no RayBork API mutates it. `EngineVersion` is the
only public type added in M0. M1 adds the host, window, input, time, and logging surface listed in
§1.6; M2 adds the renderer diagnostics (`EngineInfo`, `RenderStats`, `PresentMode`,
`TonemappingMode`, `GraphicsException`, and `GraphicsDeviceLostException`); M3 adds the asset
exceptions; M4 adds the camera/draw path and the draw-arm `RenderStats` semantics in §10.6; M5 adds
the primitive and collision surface and the runtime tone-mapping settings without a new diagnostic
type. Every other type in this reference remains a contract for later milestones.

The getter has no dependency on a pack, window, GPU, renderer, or logger. It does not block, does not
require synchronization, and throws no `RayBorkException`; the value is available to loggers,
diagnostics, crash reports, and game-side compatibility checks. RayBork does not compare
`EngineVersion.Current` with a game's own version or enforce compatibility; that policy belongs to
the game.

### 10.3 `EngineInfo`

```csharp
public readonly record struct EngineInfo(
    string Name,
    Version Version,
    string RendererName,
    string GpuName,
    uint VulkanApiVersion,
    string SwapchainFormat,
    bool HdrOutput,
    int SampleCount,
    PresentMode PresentMode,
    bool ValidationEnabled);
```

`RendererName` identifies the active backend together with the selected device's Vulkan API version,
for example `"Vulkan 1.4"` on a 1.4 device; the backend itself requests and requires Vulkan 1.2.
The public API is renderer-agnostic; a future mobile/web backend changes this string, not game
code. `GpuName` is the selected device. `VulkanApiVersion` is the packed Vulkan version the device
reports (for example `1.4.0` encoded as `(1 << 22) | (4 << 12)`). `SwapchainFormat` is the selected
surface format token without the `VK_FORMAT_` prefix (for example `B8G8R8A8_SRGB` or
`R16G16B16A16_SFLOAT`), `HdrOutput` is true only when the window reports HDR and the selected path
presents through scRGB, `SampleCount` is the effective MSAA count, `PresentMode` is the effective
present mode after fallback, and `ValidationEnabled` reports whether the validation layer is
active.

### 10.4 Exceptions

```csharp
public class RayBorkException : Exception {
    public string? DiagnosticCode { get; }
}

public sealed class GameException : RayBorkException { }
public class AssetException : RayBorkException { }
public sealed class AssetNotFoundException : AssetException { }
public sealed class AssetTypeMismatchException : AssetException { }
public sealed class AssetPackException : AssetException { }
public sealed class AssetLoadException : AssetException { }
public class GraphicsException : RayBorkException { }
public sealed class GraphicsDeviceLostException : GraphicsException { }
public sealed class ShaderException : GraphicsException { }
public sealed class WindowException : RayBorkException { }
public sealed class PlatformException : RayBorkException { }
```

M1 ships `RayBorkException`, `GameException`, `PlatformException`, and `WindowException`. M2 ships
`GraphicsException` and `GraphicsDeviceLostException`; `GraphicsException` is not sealed, and
`GraphicsDeviceLostException` derives from it. M3 ships `AssetException` and its four sealed
subtypes. M4 ships the camera and draw path without a new exception type: shader interface
mismatches that escape the bake surface as `AssetLoadException` `RB1008` at load, and draw
misuse uses the standard exceptions in the table below. M5 ships the primitive, billboard, and
wireframe-model helpers and the collision queries without a new exception type either: helper
argument and lifecycle misuse uses the standard exceptions in the table below (including
`NotSupportedException`), a corrupt, missing, or mismatched baked collision payload fails the mesh
load as `AssetLoadException` `RB1008`, and `Collision.GetRayMesh` throws `NotSupportedException` for
a line or point topology and `ArgumentException` for a singular transform. A `DrawWireModel` frame
on a device without `fillModeNonSolid` fails on the render thread with `GraphicsException` `RB3001`
naming the device and the feature; there is no CPU line-geometry fallback. The nine
`MeshBuilder.Generate*` factories and `GenerateCollisions` validate their arguments with
`ArgumentOutOfRangeException`/`NotSupportedException`/`ObjectDisposedException` like the rest of the
builder API, and `Collision.GetRayQuad` is a pure numeric query that reports a miss instead of
throwing for numeric input. `EngineContext.Tonemapping` is copied into each sealed frame; the render
thread and the present pass read that snapshot, never the live settings object. `ShaderException`
remains a contract for a later milestone.

Game and platform diagnostic codes (M1, M2):

| Code | Exception | Meaning |
| --- | --- | --- |
| `RB0001` | `GameException` | A `Setup`, `Tick`, or `Shutdown` callback failed. |
| `RB2001` | `PlatformException` | SDL3/platform initialization failed. |
| `RB2002` | `WindowException` | Window creation failed. |
| `RB2003` | `PlatformException` | SDL3 Vulkan surface creation or loader failure. |

Graphics diagnostic codes (M2):

| Code | Exception | Meaning |
| --- | --- | --- |
| `RB3001` | `GraphicsException` | Graphics initialization or device creation failed, the Vulkan validation layer reported an error, or a wireframe model draw needed `fillModeNonSolid` on a device that does not support it. Fatal. |
| `RB3002` | `GraphicsDeviceLostException` | A bounded recovery failed or the consecutive-recovery limit was reached. Fatal. |
| `RB3003` | `GraphicsException` | An unrecoverable Vulkan acquire or present failure stopped the app. Fatal. |

Asset diagnostic codes (M3):

| Code | Exception | Meaning |
| --- | --- | --- |
| `RB1001` | `AssetPackException` | A pack could not be opened or is truncated. |
| `RB1002` | `AssetPackException` | Unsupported pack magic or version. |
| `RB1003` | `AssetPackException` | The pack index is structurally invalid, unsorted, or contains a duplicate path. |
| `RB1004` | `AssetPackException` | The index hash does not match the index bytes. |
| `RB1005` | `AssetLoadException` | A payload hash does not match the stored bytes. |
| `RB1006` | `AssetNotFoundException` | The path is absent from every mounted pack. |
| `RB1007` | `AssetTypeMismatchException` | The path exists with a different asset type. |
| `RB1008` | `AssetLoadException` | A descriptor, KTX2 payload, mesh payload, or custom-data shape could not be decoded. |
| `RB1009` | `AssetException` | An asset path is not canonical. |
| `RB1010` | `AssetLoadException` | A GPU upload or device-recovery re-upload failed. |

Bake diagnostic codes (M3, `RayBork.Bake`):

| Range | Meaning |
| --- | --- |
| `RB4001` | Builder configuration, pack/root paths, or registration errors. |
| `RB4002` | TOML syntax, binding, sidecar, or unknown-key errors. |
| `RB4003` | Source discovery, canonical path, duplicate path, or unsupported source errors. |
| `RB4004` | Pack layout, size overflow, or write errors. |
| `RB4005` | Incremental manifest or cache errors. |
| `RB4006` | A custom processor failed. |
| `RB4007` | Texture decode, KTX2, mip, or block-encoding errors. |
| `RB4008` | Shader source, shaderc compilation, or metadata errors. |
| `RB4009` | glTF/OBJ parse or model/mesh conversion errors. |
| `RB4010` | Font container, face validation, or packaging errors. |
| `RB4011` | Missing or invalid baked references. |
| `RB4012` | A semantic asset validator reported an error. |

| Exception | Typical cause |
| --- | --- |
| `GameException` | `Setup`, `Tick`, or `Shutdown` threw. `InnerException` is the game's exception. |
| `AssetNotFoundException` | `Load<T>` path is absent from every mounted pack. |
| `AssetTypeMismatchException` | Path exists, but was baked as a different type. |
| `AssetPackException` | Pack missing, bad magic/version, invalid index, hash failure. |
| `AssetLoadException` | Corrupt payload, decode failure, GPU upload failure, or custom-data shape failure. |
| `GraphicsException` | Unrecoverable device/swapchain/pipeline/resource failure, a validation error, or an unsupported required mode. |
| `GraphicsDeviceLostException` | Device loss, a failed surface or swapchain recovery, or the recovery bound. Fatal. |
| `ShaderException` | A runtime shader interface mismatch that escaped bake validation. |
| `WindowException` | SDL3 window creation or platform window failure. |
| `PlatformException` | SDL3 initialization, Vulkan surface or loader failure (`RB2003`), audio device (later), or native library failure. |

`DiagnosticCode` is a stable `RB`-prefixed code such as `RB1002` (unsupported pack version),
`RB2003` (SDL3 Vulkan surface or loader failure), or `RB3002` (bounded recovery failed). Codes are
documented in the runtime and bake diagnostics tables; tests assert them. They are stable within a
major version.

Lifecycle and argument errors use the standard BCL exceptions, not `RayBorkException`:
`ArgumentNullException`, `ArgumentException`, `InvalidOperationException`,
`ObjectDisposedException`, `NotSupportedException`.

### 10.5 Assertions and validation

- Debug builds enable Vulkan validation when available. Validation errors are fatal by default;
  warnings are logged. `WithValidation(false)` disables the layer explicitly.
- Debug builds use `Debug.Assert` for internal invariants. Public API misuse throws; it is never
  silently ignored.
- Release builds still validate public arguments and lifecycle rules; they skip the expensive
  internal assertions.
- GPU object names and debug labels are set in debug builds for validation messages and
  RenderDoc/Nsight captures.

### 10.6 `RenderStats`

`EngineContext.Stats` exposes the last completed frame's `RenderStats`. The full field table is in
[Rendering: render targets](rendering/render-targets.md#712-render-statistics). M4 draw-path
semantics:

- `DrawCalls` counts scene submissions after batch splitting and excludes the present pass'
  fullscreen triangle. A frame with no camera passes reports `0`.
- `Instances` counts instances that survived culling and were submitted; `Triangles` counts
  submitted triangles after instancing (`0` for line and point topologies); `CulledInstances`
  counts frustum rejections. A `DrawWireModel` record contributes its recorded triangle count even
  though the polygon-mode-line pipeline rasterizes edges: the topology and index count do not change.
- `PipelineCount` counts distinct scene pipelines and excludes the present pipeline.
- `TextureCount` counts live bindless slots including the reserved white slot at slot 0. A texture
  allocates its slot when its GPU upload completes and releases it only when its GPU resource is
  destroyed, after every frame that can reference it has completed.
- `GpuFrameMilliseconds` is `0` because GPU timestamps are not collected in v1.
- A frame that idles on a minimized or zero-extent surface reports zero for the draw counters and
  `TextureCount`, with zero CPU and GPU time.
