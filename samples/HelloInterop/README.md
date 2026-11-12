# HelloInterop

`HelloInterop` is the RayBork M1-M3 diagnostic host. It opens an SDL3 window, runs `Setup`, `Tick`,
and `Shutdown` on the tick thread, and exercises the Vulkan 1.2 renderer end to end: the renderer
clears the linear HDR scene target, the present pass applies `--tonemapping` and `--exposure`, MSAA
is resolved before presentation, and the swapchain prefers an HDR (scRGB) format when the OS/window
HDR state is enabled and falls back to the SDR path otherwise. The M4 AOT gate is
[samples/Castle](../Castle/README.md); this host intentionally has no camera or draw calls.

The host logs the renderer, GPU, and presentation state selected at startup, the effective MSAA
sample count and present mode after fallback, per-second `RenderStats` lines, one final statistics
line and the elapsed runtime on shutdown, and the window, DPI, monitor, input, and gamepad activity of
the M1 host. It exits on the window close request, Escape, or a scripted frame limit. It also proves
that the renderer initializes, presents, and shuts down through a Native AOT publish.

## M3 asset loading

When `--assets` is supplied, the host also mounts the pack and loads one shared instance of each M3
engine asset type plus the registered custom data:

| Asset | Path | Logged metadata |
| --- | --- | --- |
| `Texture` | `/textures/circle` | Size, mip count, format, sRGB, sampler filter/wrap, maximum anisotropy. |
| `Mesh` | `/models/box/body` | Vertex count, index count, topology, bounds. The single-part OBJ group is `body`, so the part path is `/models/box/body`. |
| `Shader` | `/shaders/simple` | Stages, uniform declarations, texture bindings, default render state. |
| `HelloSettings` | `/data/hello` | `Name` and `Difficulty` from the registered TOML data type. |

The host stores the three engine assets and disposes them in `Shutdown` on the tick thread. This
diagnostic host does not draw even though M4 ships the camera and draw API; the draw gate is
[samples/Castle](../Castle/README.md). The M2 renderer-only behavior is unchanged when `--assets` is
absent. The custom data type is registered with
`WithAssetType<HelloSettings>()`, which is backed by the compiled `[GenerateShape]` in
`HelloSettings.cs`, so the load path stays reflection-free and Native AOT compatible. See
[docs/api/assets.md](../../docs/api/assets.md) §6 and
[docs/api/aot.md](../../docs/api/aot.md) §12.

`Setup` also builds one procedural mesh and one procedural texture through the M3 builders, whether
or not `--assets` is supplied:

| Asset | Built from | Logged metadata |
| --- | --- | --- |
| `Mesh` | `ctx.CreateMeshBuilder()` with four vertices and two triangles; smooth normals and tangents computed | Synthetic `/_procedural/mesh/N` path, vertex/index counts, bounds, `IsProcedural`. |
| `Texture` | `ctx.CreateTextureBuilder(2, 2, Rgba8Srgb)` from four `Color` pixels with mip generation | Synthetic `/_procedural/texture/N` path, size, mip count, format, sRGB, `IsProcedural`. |

Both builds are awaited in `Setup`, so they exercise the non-blocking GPU upload path and the
`BuildAsync` contract in the Native AOT gate. The assets are disposed in `Shutdown`. See
[docs/api/rendering/meshes.md](../../docs/api/rendering/meshes.md) §7.5 and
[docs/api/rendering/textures.md](../../docs/api/rendering/textures.md) §7.6.

The host uses `RayBorkAppBuilder` with a resizable window, a 60 FPS tick cap, a 0.25 second maximum
delta, and the renderer options on the command line. It is an `Exe` with `PublishAot=true`, so console
logging is visible and the AOT gate can run it on Windows and Linux. HDR presentation is automatic
from the OS/window state: there is no HDR override, and the selected state is reported through
`EngineContext.Info` as described in [docs/api/lifecycle.md](../../docs/api/lifecycle.md) §3.4,
[docs/api/rendering/camera.md](../../docs/api/rendering/camera.md) §7.2,
[docs/api/rendering/render-targets.md](../../docs/api/rendering/render-targets.md) §7.12, and
[docs/api/diagnostics.md](../../docs/api/diagnostics.md) §10.3.

## Options

| Argument | Contract |
| --- | --- |
| `--assets PATH` | Mount a baked asset pack and load the M3 sample assets. A relative path resolves against the current directory. Default: no pack. |
| `--frames N` | Run `N` ticks, then request exit. `0` runs until the window closes. Default `0`. |
| `--title TEXT` | Window title. It must not be empty or whitespace. Default `HelloInterop`. |
| `--size WxH` | Window client size in window units. Both values must be positive. Default `1280x720`. |
| `--msaa N` | Requested MSAA sample count, a positive integer. Default `4`. The renderer selects the highest supported count that does not exceed the request and logs a warning when the effective count differs. |
| `--present-mode NAME` | `Mailbox`, `Fifo`, or `Immediate`, case-insensitive. Default `Mailbox`; a surface that does not support Mailbox falls back to `Fifo` with a warning. `Immediate` is used only when explicitly requested. |
| `--tonemapping NAME` | `AgX` or `None`, case-insensitive. Default `AgX`. `None` writes linear values through the swapchain transfer function for custom presentation paths. |
| `--exposure EV` | Exposure in stops applied before tone mapping, an invariant finite float. Default `0`; `+1` doubles linear exposure. |
| `--validation on\|off` | Request or disable the Vulkan validation layer. When the option is omitted, the engine default applies: on in Debug, off in Release. The effective state is reported by `EngineContext.Info.ValidationEnabled`. |
| `--log-level NAME` | A defined `Microsoft.Extensions.Logging.LogLevel` name, case-insensitive: `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, or `None`. Default `Information`. |
| `--help` | Print usage and exit `0`. |

A successful run exits `0`; a failed run prints the exception to stderr and exits `1`; an invalid
command line prints the error and usage to stderr and exits `2`.

## Expected log output

The renderer logs its device and presentation selection before `Setup` runs. On the HDR test host
(SDL reports an SDR white level of `2.0` and an HDR headroom of `6.25`, and the surface advertises no
`Mailbox` mode), `--frames 120` reports:

```text
Vulkan 1.2 renderer initialized: AMD Radeon(TM) 8060S Graphics, API 1.4.359, validation enabled.
Presentation path: R16G16B16A16_SFLOAT/ColorSpaceExtendedSrgbLinearExt, HDR True, present mode Fifo, 4x MSAA, 3 swapchain image(s), 1280x720.
Window HDR state: enabled True, SDR white level 2, HDR headroom 6.25.
The requested present mode Mailbox is not supported by the window surface; using Fifo.
Engine RayBork 0.1.0.0: renderer Vulkan 1.4, GPU 'AMD Radeon(TM) 8060S Graphics', Vulkan 1.4.359, validation True.
Presentation: swapchain R16G16B16A16_SFLOAT, HDR True, 4x MSAA, present mode Fifo.
Requested scene: exposure 0 EV, tone mapping AgX.
Tick render stats: frame 59, 0 draw call(s), 0 instance(s), 0 triangle(s), 0 culled instance(s), 0 pipeline(s), 2 texture(s), 4x MSAA, HDR True, CPU 0.536 ms, 1 frame(s) in flight, 0 recoveries.
Final render stats: frame 119, 0 draw call(s), 0 instance(s), 0 triangle(s), 0 culled instance(s), 0 pipeline(s), 2 texture(s), 4x MSAA, HDR True, CPU 0.463 ms, 1 frame(s) in flight, 0 recoveries.
Elapsed runtime: 2.664s.
HelloInterop stopped after 120 tick(s): time 2.664s, fps 60.9, last window 1280x720 (render 1280x720, DPI scale 1.5), exit frame limit (120).
```

With `--assets`, the host adds the pack path and one metadata line per loaded asset before the first
tick:

```text
Assets pack: /absolute/path/samples/HelloInterop.Assets/bin/Release/net11.0/hello.pck.
Texture /textures/circle: 64x64, 7 mip level(s), format Rgba8Srgb, sRGB True, sampler Linear/Repeat, max anisotropy 1.
Mesh /models/box/body: 24 vertex/vertices, 36 index/indices, topology TriangleList, bounds <-0.5, -0.5, -0.5>..<0.5, 0.5, 0.5>.
Shader /shaders/simple: stages Vertex, Fragment, 0 uniform(s), 0 texture binding(s), default state Opaque/Back/GreaterOrEqual, depth write True.
Custom data /data/hello: name 'RayBork M3 Sample', difficulty 2.
Procedural mesh /_procedural/mesh/1: 4 vertex/vertices, 6 index/indices, bounds <-0.5, -0.5, 0>..<0.5, 0.5, 0>, procedural True.
Procedural texture /_procedural/texture/1: 2x2, 2 mip level(s), format Rgba8Srgb, sRGB True, procedural True.
```

- The device is the `Vulkan 1.2 renderer initialized` line; `EngineInfo.RendererName` reports the
  device API major and minor (for example `Vulkan 1.4`), and `EngineInfo.VulkanApiVersion` is
  formatted as `major.minor.patch`.
- The presentation path is the `Presentation path` line: `R16G16B16A16_SFLOAT` with
  `ColorSpaceExtendedSrgbLinearExt` is the scRGB HDR path; without OS HDR it falls back to
  `B8G8R8A8_SRGB` (or `R8G8B8A8_SRGB`) and reports `HDR False`.
- The HDR state is repeated by the engine `Window HDR state` line and by the host
  `Presentation`/`Requested scene` lines, so a run shows both the state that was requested and the
  effective sample count and present mode.
- `RenderStats` is logged every 60 ticks (about one second at the 60 FPS cap) and once more during
  shutdown. The shutdown snapshot is the last frame the render thread completed; it can lag the final
  tick by one frame because the tick thread enters `Shutdown` before the frame ring is drained. This
  host records no camera passes, so the draw counters stay `0`; the present triangle is not a scene
  draw and `TextureCount` reports the live bindless slots (the built-in white and error textures).

## Bare renderer run

From the repository root:

```powershell
dotnet build samples/HelloInterop/HelloInterop.csproj -c Debug
dotnet run --project samples/HelloInterop/HelloInterop.csproj -- --frames 120 --log-level Information
```

Run the explicit SDR-ish configuration to exercise the option surface:

```powershell
dotnet run --project samples/HelloInterop/HelloInterop.csproj -- --msaa 8 --present-mode Fifo --tonemapping None --exposure 1.5
```

Pass `--help` to print the full usage text. The sample uses `var` for monitor locals because
`RayBork.Monitor` collides with `System.Threading.Monitor` under implicit usings; callers that import
`System.Threading` should qualify `RayBork.Monitor` or use `var`.

## Bake the diagnostic pack

From the repository root:

```powershell
dotnet run --project samples/HelloInterop.Assets/HelloInterop.Assets.csproj -c Release -- bake
dotnet run --project samples/HelloInterop/HelloInterop.csproj -- --assets samples/HelloInterop.Assets/bin/Release/net11.0/hello.pck --frames 3
```

Require exit `0` from the bake, an existing
`samples/HelloInterop.Assets/bin/Release/net11.0/hello.pck`, exit `0` from the host, and the four
asset metadata lines above. The pack is generated output under `bin/` and is gitignored; see
[HelloInterop.Assets/README.md](../HelloInterop.Assets/README.md) for the source layout and the
builder configuration.

## Windows Native AOT

Publish and run the Windows host from the repository root:

```powershell
dotnet publish samples/HelloInterop/HelloInterop.csproj -c Release -r win-x64 -p:PublishAot=true
samples/HelloInterop/bin/Release/net11.0/win-x64/publish/HelloInterop.exe --assets samples/HelloInterop.Assets/bin/Release/net11.0/hello.pck --frames 30
```

The publish must finish with zero AOT/trim warnings and the published host must exit `0` after
mounting the pack, loading the texture, mesh, shader, and custom data, and presenting 30 frames. This
is the diagnostic-host publish; the M4 per-milestone AOT gate is `samples/Castle`.

## WSL Linux Native AOT

Publish the Linux x64 host from the repository root inside WSL, then run it on the WSLg display:

```bash
$HOME/.dotnet/dotnet publish samples/HelloInterop/HelloInterop.csproj -c Release -r linux-x64 -p:PublishAot=true
DISPLAY=:0 XDG_RUNTIME_DIR=/mnt/wslg/runtime-dir \
  samples/HelloInterop/bin/Release/net11.0/linux-x64/publish/HelloInterop --assets samples/HelloInterop.Assets/bin/Release/net11.0/hello.pck --frames 30
```

WSLg provides the X display and the Wayland socket that SDL3 needs. Without a display the renderer
cannot create its Vulkan surface and the host exits `1`. This diagnostic publish is reported as
unavailable when the host has no WSL or no Vulkan 1.2 device; it is never faked.

## No dummy video driver

The Vulkan backend creates its presentation surface from a real SDL3 window, so unlike the M1 host
this sample cannot run with SDL's dummy video driver. `SDL_VIDEODRIVER=dummy` fails during renderer
initialization because the dummy driver has no window system for a Vulkan surface; run the sample on
a desktop session or under WSLg instead.

Publish output is generated under `samples/HelloInterop/bin/` and is gitignored. Both publishes must
finish with zero AOT/trim warnings. This host remains the M1-M3 diagnostic host; the per-milestone
gate is [samples/Castle](../Castle/README.md) and the gate commands are in
[AGENTS.md](../../AGENTS.md) §4.
