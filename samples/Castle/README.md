# Castle

`Castle` is the RayBork M4 core 3D renderer sample and the M4 Native AOT gate. It loads a multi-part
castle model, a stone texture, and a custom shader from a baked pack, then draws the model every tick
with a perspective camera that orbits the castle while the model slowly turns. A procedural sky
clear color, reverse-Z depth, back-face culling, bindless textures, per-instance uniforms, and the
linear HDR/AgX present path are exercised end to end.

The sample is the gate, not just a demo: it records whether any completed frame reported both draw
calls and instances. When no completed frame ever draws, `Shutdown` throws `InvalidOperationException`
naming the Castle gate, so a published host that silently renders nothing exits `1` instead of
passing. A slow host is judged on completed frames instead of a fixed tick number.

## Assets

| Source | Path in the pack | Notes |
| --- | --- | --- |
| `Assets/models/castle.obj` | `/models/castle` | Keep, four corner towers, gatehouse, and ground. Seven parts with outward counter-clockwise faces and 2 m UV tiles. |
| `Assets/models/castle.toml` | (sidecar) | `front = "-z"` keeps the authored orientation; every part uses `shaders/castle`. |
| `Assets/textures/stone.png` | `/textures/stone` | 128x128 sRGB stone/brick texture with a deterministic tileable pattern. |
| `Assets/shaders/castle.toml`, `.vert`, `.frag` | `/shaders/castle` | Custom M4 shader pair: one directional light, a small ambient term, `uTint`, and a bindless `mainTexture`. |

The pack is generated output under `bin/` and is gitignored. Only the sources are committed.

## Options

| Argument | Contract |
| --- | --- |
| `--assets PATH` | Mount the baked Castle pack. A relative path resolves against the current directory. Default: `castle.pck` beside the executable. |
| `--frames N` | Run `N` ticks, then request exit. `0` runs until the window closes. Default `0`. |
| `--size WxH` | Window client size in window units. Both values must be positive. Default `1280x720`. |
| `--msaa N` | Requested MSAA sample count, a positive integer. Default `4`. The renderer selects the highest supported count that does not exceed the request and logs a warning when the effective count differs. |
| `--present-mode NAME` | `Mailbox`, `Fifo`, or `Immediate`, case-insensitive. Default `Mailbox`; a surface that does not support Mailbox falls back to `Fifo` with a warning. `Immediate` is used only when explicitly requested. |
| `--validation on\|off` | Request or disable the Vulkan validation layer. When the option is omitted, the engine default applies: on in Debug, off in Release. |
| `--log-level NAME` | A defined `Microsoft.Extensions.Logging.LogLevel` name, case-insensitive: `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, or `None`. Default `Information`. |
| `--help` | Print usage and exit `0`. |

A successful run exits `0`; a failed run, including the gate assertion, prints the exception to
stderr and exits `1`; an invalid command line prints the error and usage to stderr and exits `2`.

## Controls

| Input | Action |
| --- | --- |
| Escape | Request exit. |
| (automatic) | The camera orbits the castle at a fixed radius and the model turns slowly with `ctx.Time`. |

## Build and run

From the repository root, with a Vulkan 1.2 device and a real window system:

```powershell
dotnet build samples/Castle/Castle.csproj -c Debug
dotnet run --project samples/Castle/Castle.csproj -- --frames 120 --log-level Information
```

A run without a pack fails at the first asset load; bake one first.

## Bake and run

```powershell
dotnet run --project samples/Castle.Assets/Castle.Assets.csproj -c Release -- bake
dotnet run --project samples/Castle/Castle.csproj -c Release -- --assets samples/Castle.Assets/bin/Release/net11.0/castle.pck --frames 30
```

Require exit `0` from the bake, an existing
`samples/Castle.Assets/bin/Release/net11.0/castle.pck`, exit `0` from the host, and a final
statistics line with nonzero draw calls and instances. The pack contains 10 assets: the model, its
seven part meshes and payloads, the shader and its two SPIR-V payloads, and the texture and its KTX2
payload.

`Setup` loads `/models/castle`, `/textures/stone`, and `/shaders/castle`, logs the engine/GPU state
and every model part with its bounds, applies `mainTexture` and `uTint`, and keeps the three assets
in fields. `Shutdown` disposes the model, the shader, and the texture exactly once; the model owns
the part references, so the sample never disposes a part mesh or the part shader separately.

## Expected output

The log at startup reports the engine, GPU, and presentation path, the model parts, and the texture
and shader metadata. Values vary by GPU and display:

```text
RayBork 0.1.0.0 M4 Castle sample starting.
Engine RayBork 0.1.0.0: renderer Vulkan 1.4, GPU 'AMD Radeon(TM) 8060S Graphics', Vulkan 1.4.359, validation False.
Presentation: swapchain R16G16B16A16_SFLOAT, HDR True, 4x MSAA, present mode Fifo.
Model /models/castle: 7 part(s), bounds <-9, 0, -9>..<9, 8, 9>.
Part keep: mesh /models/castle/keep, 24 vertex/vertices, 36 index/indices, bounds <-3, 0, -2.5>..<3, 6.5, 2.5>, shader /shaders/castle.
Texture /textures/stone: 128x128, 8 mip level(s), format Rgba8Srgb, sRGB True, sampler Linear/Repeat.
Shader /shaders/castle: stages Vertex, Fragment, 1 uniform(s), 1 texture binding(s), default state Opaque/Back/GreaterOrEqual, depth write True.
Final render stats: frame 29, 7 draw call(s), 7 instance(s), 74 triangle(s), 0 culled instance(s), 1 pipeline(s), 2 texture(s), 4x MSAA, HDR True, CPU 0.383 ms, 1 frame(s) in flight, 0 recoveries.
Castle stopped after 30 tick(s): exit frame limit (30).
```

The seven parts produce seven batches because each part has its own mesh; the shader and the
material state are shared, so the frame uses one pipeline. `TextureCount` includes the renderer's
reserved white bindless slot.

## Windows Native AOT

Publish and run the Windows host from the repository root:

```powershell
dotnet publish samples/Castle/Castle.csproj -c Release -r win-x64 -p:PublishAot=true
samples/Castle/bin/Release/net11.0/win-x64/publish/Castle.exe --assets samples/Castle.Assets/bin/Release/net11.0/castle.pck --frames 30
```

The publish must finish with zero AOT/trim warnings and the published host must exit `0` after
mounting the pack, loading and drawing the castle, and presenting 30 frames.

## WSL Linux Native AOT

Publish the Linux x64 host from the repository root inside WSL, then run it on the WSLg display:

```bash
$HOME/.dotnet/dotnet publish samples/Castle/Castle.csproj -c Release -r linux-x64 -p:PublishAot=true
DISPLAY=:0 XDG_RUNTIME_DIR=/mnt/wslg/runtime-dir \
  samples/Castle/bin/Release/net11.0/linux-x64/publish/Castle \
  --assets samples/Castle.Assets/bin/Release/net11.0/castle.pck --frames 30
```

WSLg provides the X display and the Wayland socket that SDL3 needs. Without a display the renderer
cannot create its Vulkan surface and the host exits `1`.

On a WSLg host whose Vulkan device comes from Mesa's non-conformant dzn driver (`Microsoft
Direct3D12 ...` with the "dzn is not a conformant Vulkan implementation" warning), the first
geometry draw currently dies inside `libvulkan_dzn.so`. The M2 clear and present path works on the
same device, and the Windows hardware driver and the conformant lavapipe path both pass this sample.
For the Linux functional proof, run the same published host under lavapipe:

```bash
VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json \
DISPLAY=:0 XDG_RUNTIME_DIR=/mnt/wslg/runtime-dir \
  samples/Castle/bin/Release/net11.0/linux-x64/publish/Castle \
  --assets samples/Castle.Assets/bin/Release/net11.0/castle.pck --frames 30
```

The M4 as-built notes in [docs/roadmap.md](../../docs/roadmap.md) and
[docs/api/aot.md](../../docs/api/aot.md) §12 record the driver detail.

## No dummy video driver

The Vulkan backend creates its presentation surface from a real SDL3 window, so this sample cannot
run with SDL's dummy video driver. `SDL_VIDEODRIVER=dummy` fails during renderer initialization
because the dummy driver has no window system for a Vulkan surface; run the sample on a desktop
session or under WSLg instead.

Publish output is generated under `samples/Castle/bin/` and is gitignored. Both publishes must
finish with zero AOT/trim warnings. See [docs/api/aot.md](../../docs/api/aot.md) §12 and
[docs/api/rendering/shaders.md](../../docs/api/rendering/shaders.md) §7.4 for the gate and shader ABI.
