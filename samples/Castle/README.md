# Castle

`Castle` is a small RayBork sample: an orbitable castle scene with a model, a diffuse texture, an
orbital perspective camera, and the reference XZ grid. It loads the model and texture, binds the
texture on the model's materials, and draws the castle and its grid every tick. The source is kept
small and commented so the scene can be read and adapted as a starting point.

`samples/PrimitiveGallery` is the M5 Native AOT gate. This sample remains a buildable and publishable
example, and it contains no test-only logic: it opens the window, draws the scene, and exits cleanly.
Build and run it from the repository root:

```powershell
dotnet build samples/Castle/Castle.slnx -c Debug
dotnet run --project samples/Castle/Castle.csproj -c Debug -- --frames 120
```

## Assets

The checked-in sources live under `Castle.Assets/Assets/`. The sample build bakes them into
`castle.pck`; no manual bake step is required.

| Source | Path in the pack | Notes |
| --- | --- | --- |
| `Assets/models/obj/castle.obj` | `/models/obj/castle` | raylib's `castle.obj`, copied verbatim. One `castle` part with 12,150 vertices and 21,504 indices. |
| `Assets/models/obj/castle_diffuse.png` | `/models/obj/castle_diffuse` | raylib's 1024x1024 sRGB diffuse texture, copied verbatim. |
| `Assets/models/obj/castle.toml` | (sidecar) | `front = "-z"` keeps raylib's authored orientation; no part shader is set, so the bake uses the built-in `shaders/mesh` material. |

The pack is generated output under `bin/` and `obj/` and is gitignored. The castle model and texture
are sample-only and are **not** covered by RayBork's MIT license; see
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Options

| Argument | Contract |
| --- | --- |
| `--assets PATH` | Mount a Castle pack instead of the `castle.pck` beside the executable. A relative path resolves against the current directory. |
| `--frames N` | Run `N` ticks, then request exit. `0` runs until the window closes. Default `0`. |
| `--help` | Print usage and exit `0`. |

The sample uses an 800x450 window, a 60 FPS target, and the engine's default renderer settings. A
missing pack prints an error and exits `1`.

## Controls

| Input | Action |
| --- | --- |
| Left mouse drag | Orbit the camera around the castle. Pitch is clamped to ±89 degrees. |
| Mouse wheel | Zoom the orbit distance, clamped to 5–500. |
| Escape | Request exit. |

The camera starts at `(50, 50, 50)`, looks at `(0, 12, 0)`, and uses a 45 degree vertical field of
view with a `0.01` near plane and an infinite far plane. The background is a warm off-white
(`#F5F5F5`, about `0.913` per channel).

## Scene

Each tick records one perspective pass: the castle model with its built-in mesh material, then the
reference grid under it:

```csharp
camera.DrawModel(_model!, Transform.Identity, Color.White);
camera.DrawGrid(GRID_SLICES, GRID_SPACING, Color.Gray);
```

`GRID_SLICES` is `20` and `GRID_SPACING` is `10f`, so the grid spans 200 world units per axis in 20
cells, centered on the world origin where the castle stands. It is raylib's reference grid, drawn in
the same opaque, depth-tested pass as the model, so the castle occludes the lines behind it. The
grid is one instanced line draw and contributes no triangles.

## Build and run

The sample is a self-contained solution, `samples/Castle/Castle.slnx`, containing `Castle.csproj` and
`Castle.Assets/Castle.Assets.csproj`. It is not part of `src/RayBork/RayBork.slnx`. The build bakes
`castle.pck` and copies it beside the sample executable; `dotnet run` loads that adjacent pack
automatically, and `--assets` is only an override. The JIT bake tool and its packages never enter the
sample's AOT publish graph.

## Native AOT

Publish and run the Windows host from the repository root:

```powershell
dotnet publish samples/Castle/Castle.csproj -c Release -r win-x64 -p:PublishAot=true
samples/Castle/bin/Release/net11.0/win-x64/publish/Castle.exe --frames 30
```

Publish the Linux host from the repository root inside WSL and run it on the WSLg display:

```bash
$HOME/.dotnet/dotnet publish samples/Castle/Castle.csproj -c Release -r linux-x64 -p:PublishAot=true
DISPLAY=:0 XDG_RUNTIME_DIR=/mnt/wslg/runtime-dir \
  samples/Castle/bin/Release/net11.0/linux-x64/publish/Castle --frames 30
```

Both publishes must finish with zero AOT/trim warnings. The publish directory must contain
`castle.pck` beside the host and must not contain `Castle.Assets.dll`, `RayBork.Bake.dll`, Tomlyn,
StbImageSharp, BCnEncoder, the shaderc native libraries, or the glTF packages. The WSLg Mesa dzn
hardware device is supported; the conformant lavapipe ICD remains available for a software-only run:

```bash
VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json \
DISPLAY=:0 XDG_RUNTIME_DIR=/mnt/wslg/runtime-dir \
  samples/Castle/bin/Release/net11.0/linux-x64/publish/Castle --frames 30
```

## What the sample omits

The sample does not implement these features yet:

- **Drag-and-drop of a new model or texture.** Window file-drop events are post-1.0.
- **Click selection.** The engine ships `Collision.GetRayBox` and `Camera.DrawWireCube`, but there is
  no public `DrawBoundingBox`, and the sample does not pick the castle from the mouse. Adding
  selection would mean converting the pointer to a world ray with `ScreenToWorldRay`, testing the
  castle's bounds, and drawing a wire box for the hit.
- **`DrawText` and `DrawFPS`.** Runtime text and font/MSDF support arrive in M6.

The sample cannot run with SDL's dummy video driver because the Vulkan backend needs a real window
surface; run it on a desktop session or under WSLg.

## Notices and license

The castle model and texture are by Alberto Cano (Medieval Assets Pack) under CC BY-NC 4.0 and are
sample-only; they are not covered by RayBork's MIT license and may not be used commercially. See
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for attribution and license links.
