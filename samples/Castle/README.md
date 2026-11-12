# Castle

`Castle` is the RayBork M4 core 3D renderer sample and the M4 Native AOT gate. It recreates the
essential behavior of raylib's
[`models_loading`](https://github.com/raysan5/raylib/blob/master/examples/models/models_loading.c)
example: a castle model and its diffuse texture are drawn unlit with the built-in `/shaders/mesh`
material that the loaded model already owns, and an orbital camera looks at the castle from raylib's
initial position. There is no custom shader asset. The raylib reference XZ grid moves to M5 with
`Camera.DrawGrid`, so the M4 sample is the model, texture, and camera only. The sample has no
drag-and-drop, click selection, or text overlay; those raylib features need input/platform and text
work that arrives in M5, M6, and M9.

The sample is the gate, not just a demo: it records whether any completed frame reported both draw
calls and instances. When no completed frame ever draws, `Shutdown` throws `InvalidOperationException`
naming the Castle gate, so a published host that silently renders nothing exits `1` instead of
passing. A slow host is judged on completed frames instead of a fixed tick number.

The sample is also a self-contained solution: `samples/Castle/Castle.slnx` contains `Castle.csproj`
and `Castle.Assets/Castle.Assets.csproj`. It is not part of `src/RayBork/RayBork.slnx`, so the engine
solution never builds the sample or its JIT bake tool.

## Assets

The checked-in sources live under `Castle.Assets/Assets/`. The sample build bakes them into
`castle.pck`; no manual bake step is required.

| Source | Path in the pack | Notes |
| --- | --- | --- |
| `Assets/models/obj/castle.obj` | `/models/obj/castle` | raylib's `castle.obj` copied verbatim, including its header comments. One `castle` part with 12,150 vertices and 21,504 indices. |
| `Assets/models/obj/castle_diffuse.png` | `/models/obj/castle_diffuse` | raylib's 1024x1024 sRGB diffuse texture, copied verbatim. |
| `Assets/models/obj/castle.toml` | (sidecar) | `front = "-z"` keeps raylib's authored orientation; no part shader is set, so the bake defaults every part to the built-in `shaders/mesh` program. |

The pack is generated output under `bin/` and `obj/` and is gitignored. Only the sources are
committed. The castle model and texture are sample-only and are **not** covered by RayBork's MIT
license; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Options

| Argument | Contract |
| --- | --- |
| `--assets PATH` | Mount a Castle pack instead of the `castle.pck` beside the executable. A relative path resolves against the current directory. The default pack is baked by the sample build. |
| `--frames N` | Run `N` ticks, then request exit. `0` runs until the window closes. Default `0`. |
| `--size WxH` | Window client size in window units. Both values must be positive. Default `800x450`, raylib's example size. |
| `--msaa N` | Requested MSAA sample count, a positive integer. Default `4`. The renderer selects the highest supported count that does not exceed the request and logs a warning when the effective count differs. |
| `--present-mode NAME` | `Mailbox`, `Fifo`, or `Immediate`, case-insensitive. Default `Mailbox`; a surface that does not support Mailbox falls back to `Fifo` with a warning. `Immediate` is used only when explicitly requested. |
| `--validation on\|off` | Request or disable the Vulkan validation layer. When the option is omitted, the engine default applies: on in Debug, off in Release. |
| `--log-level NAME` | A defined `Microsoft.Extensions.Logging.LogLevel` name, case-insensitive: `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, or `None`. Default `Information`. |
| `--help` | Print usage and exit `0`. |

A successful run exits `0`; a failed run, including the gate assertion, prints the exception to
stderr and exits `1`; an invalid command line prints the error and usage to stderr and exits `2`.
When neither the default pack nor an `--assets` pack exists, the sample prints an error that says to
build the sample and exits `1` without opening a window.

## Controls

| Input | Action |
| --- | --- |
| Left mouse drag | Orbit the camera around the castle: yaw and pitch from `Input.MouseDelta`. Pitch is clamped to +/-89 degrees. |
| Mouse wheel | Zoom by changing the orbit distance, clamped to 5..500. |
| Escape | Request exit. |
| `--frames N` | Request exit after `N` ticks. |

The camera starts at raylib's position `(50, 50, 50)`, looks at `(0, 12, 0)`, and uses a 45 degree
vertical field of view, a `0.01` near plane, and an infinite far plane. The clear color is the linear
equivalent of raylib's `RAYWHITE` (`#F5F5F5`, about `0.913` per channel).

## Build and run

From the repository root, with a Vulkan 1.2 device and a real window system:

```powershell
dotnet build samples/Castle/Castle.slnx -c Debug
dotnet run --project samples/Castle/Castle.csproj -c Debug -- --frames 120 --log-level Information
```

The build bakes `castle.pck` and copies it beside the sample executable. `dotnet run` loads that
adjacent pack automatically; `--assets` is only an optional override. A Debug run writes
`samples/Castle/bin/Debug/net11.0/castle.pck`.

The pack is generated under `samples/Castle/obj/<Configuration>/net11.0/castle-pack/` and is also
recorded as a build output, so `dotnet clean` removes it and the next build bakes it again. The
sample build invokes `Castle.Assets/Castle.Assets.csproj` out of process with the same
configuration and without the sample's publish settings. The JIT bake tool and its packages therefore
never enter the sample's AOT publish graph.

## Native AOT on Windows

Publish and run the Windows host from the repository root:

```powershell
dotnet publish samples/Castle/Castle.csproj -c Release -r win-x64 -p:PublishAot=true
samples/Castle/bin/Release/net11.0/win-x64/publish/Castle.exe --frames 30
```

The publish must finish with zero AOT/trim warnings. Its output directory must contain `castle.pck`
beside `Castle.exe` and must not contain `Castle.Assets.dll`, `RayBork.Bake.dll`, Tomlyn,
StbImageSharp, BCnEncoder, the shaderc native libraries, or the glTF packages. The published host
must exit `0` after mounting the adjacent pack, loading and drawing the castle, presenting 30 frames,
and logging a completed frame with nonzero draw calls and instances.

## Native AOT on WSL Linux

Publish the Linux x64 host from the repository root inside WSL, then run it on the WSLg display:

```bash
$HOME/.dotnet/dotnet publish samples/Castle/Castle.csproj -c Release -r linux-x64 -p:PublishAot=true
DISPLAY=:0 XDG_RUNTIME_DIR=/mnt/wslg/runtime-dir \
  samples/Castle/bin/Release/net11.0/linux-x64/publish/Castle --frames 30
```

WSLg provides the X display and the Wayland socket that SDL3 needs. Without a display the renderer
cannot create its Vulkan surface and the host exits `1`.

On a WSLg host whose Vulkan device comes from Mesa's non-conformant dzn driver (`Microsoft
Direct3D12 ...` with the "dzn is not a conformant Vulkan implementation" warning), the sample runs
on that hardware driver with the engine's fixed 2048-slot bindless array. The published host must
print the dzn device and exit `0` after 30 frames with 2 draw calls and 2 instances. No
`DZN_DEBUG` workaround is needed. The conformant lavapipe ICD remains available for a software-only
Linux run:

```bash
VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json \
DISPLAY=:0 XDG_RUNTIME_DIR=/mnt/wslg/runtime-dir \
  samples/Castle/bin/Release/net11.0/linux-x64/publish/Castle --frames 30
```

The Linux publish also ships `castle.pck` beside the executable. The WSL driver detail and the
fixed-array rationale are recorded in [docs/roadmap.md](../../docs/roadmap.md) and
[docs/api/aot.md](../../docs/api/aot.md) §12.

## Expected output

The log at startup reports the engine, GPU, and presentation path, the model part, the texture and
built-in shader metadata, and the camera. Values vary by GPU and display:

```text
Model /models/obj/castle: 1 part(s), bounds <-23.99, -0.02, -16.34>..<18.23, 30.9, 16.34>.
Part castle: mesh /models/obj/castle/castle, 12150 vertex/vertices, 21504 index/indices, ... shader /shaders/mesh.
Texture /models/obj/castle_diffuse: 1024x1024, 11 mip level(s), format Rgba8Srgb, sRGB True, sampler Linear/Repeat.
Shader /shaders/mesh: stages Vertex, Fragment, 1 uniform(s), 1 texture binding(s), default state Opaque/Back/GreaterOrEqual, depth write True.
Castle gate: completed frame 1 reported 1 draw call(s) and 1 instance(s).
Final render stats: frame 29, 1 draw call(s), 1 instance(s), 7168 triangle(s), 0 culled instance(s), 1 pipeline(s), 4 texture(s), 4x MSAA, HDR True, ... 0 recoveries.
Castle stopped after 30 tick(s): exit frame limit (30).
```

The castle is one batch, so a frame reports one draw call and one instance. `TextureCount` counts
live bindless slots: the renderer's reserved white slot, the engine-owned white and error textures,
and the castle diffuse texture. The built-in `/shaders/mesh` program is unlit and consumes the
model's UVs and vertex color, so the sample only binds `mainTexture` on the part material before
drawing.

## What the recreation omits

The M4 sample does not implement these raylib `models_loading` features; each needs a later
milestone:

- **The reference XZ grid.** `DrawGrid(20, 10.0f)` and the built-in grid mesh arrive in M5. The M4
  sample draws the castle model only; Castle uses `DrawGrid` for the raylib reference grid in M5.
- **Drag-and-drop of a new model or texture.** Window file-drop events are post-1.0, and the host
  has no dropped-file API in M4/M5.
- **Click selection and the bounding box.** `GetScreenToWorldRay` and `Model.Bounds` exist in M4, but
  the ray/box collision query and `DrawBoundingBox` helper arrive in M5. The sample also does not
  need selection to exercise the renderer.
- **`DrawText` and the `DrawFPS` overlay.** Runtime text and font/MSDF support arrive in M6. Render
  statistics are logged instead of drawn.

## No dummy video driver

The Vulkan backend creates its presentation surface from a real SDL3 window, so this sample cannot
run with SDL's dummy video driver. `SDL_VIDEODRIVER=dummy` fails during renderer initialization
because the dummy driver has no window system for a Vulkan surface; run the sample on a desktop
session or under WSLg instead.

## Notices and license

The castle model and texture are by Alberto Cano (Medieval Assets Pack) under CC BY-NC 4.0 and are
sample-only; they are not covered by RayBork's MIT license and may not be used commercially. The
sample behavior follows raylib's zlib/libpng-licensed `models_loading` example. See
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for the attribution and license links.

Publish output is generated under `samples/Castle/bin/` and is gitignored. See
[docs/api/aot.md](../../docs/api/aot.md) §12 and
[docs/api/rendering/shaders.md](../../docs/api/rendering/shaders.md) §7.4 for the gate and shader ABI.
