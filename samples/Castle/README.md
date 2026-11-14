# Castle

`Castle` is a small RayBork sample: an orbitable castle scene with a model, a diffuse texture, an
orbital perspective camera, the reference XZ grid, and a solid-plus-wireframe model draw. It loads the
model and texture, binds the texture on the model's materials, and draws the castle solid, then again
as a wireframe overlay, and the grid every tick. The source is kept small and commented so the scene
can be read and adapted as a starting point.

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
view with a `0.01` near plane and an infinite far plane. The background is the `Color.WhiteSmoke`
preset: the Bork2d/Unity sRGB `#F5F5F5`, stored linear as `0.913099` per channel. The grid uses
`Color.Gray`, the sRGB display mid-gray stored linear as `0.214041`.

## Scene

Each tick records one perspective pass: the castle model with its built-in mesh material, the same
model again as a wireframe overlay, then the reference grid under it:

```csharp
camera.DrawModel(_model!, Transform.Identity, Color.White);
camera.DrawWireModel(
    _model!,
    new Transform(Vector3.Zero, Quaternion.Identity, new Vector3(WIREFRAME_OVERLAY_SCALE)),
    Color.Black);
camera.DrawGrid(GRID_SLICES, GRID_SPACING, Color.Gray);
```

`GRID_SLICES` is `20` and `GRID_SPACING` is `10f`, so the grid spans 200 world units per axis in 20
cells, centered on the world origin where the castle stands. It is raylib's reference grid, drawn in
the same opaque, depth-tested pass as the model, so the castle occludes the lines behind it. The grid
is one instanced line draw and contributes no triangles.

### Wireframe overlay

`DrawWireModel` is the raylib `DrawModelWires`/`DrawModelWiresEx` equivalent. It records exactly the
parts, material snapshots, tint, and `local * transform` matrices of `DrawModel`, but rasterizes the
triangles in polygon-mode line, so the visible triangle edges of the front-facing geometry are drawn
with the castle's own material and texture. Culling, depth test, depth write, and blending are
unchanged, and the tint and sampled texture apply to the line fragments exactly as they do to filled
fragments.

The overlay is not the identity transform: it uses `Transform` scale `WIREFRAME_OVERLAY_SCALE`
(`1.002`). Scaling the wireframe a hair past the solid pushes its edges just outside the shaded
surface, so the depth test never discards them against the coplanar solid triangles; at the camera's
distance the `0.2` percent offset is invisible. `Color.Black` multiplies the sampled texture to a dark
edge, so the wireframe reads over the stone at any orbit angle.

The wireframe pipeline needs the device's core `fillModeNonSolid` feature, which the renderer enables
at device creation when the physical device advertises it. A device without the feature reports the
draw as an unsupported `GraphicsException` (`RB3001`) naming the device and the feature instead of
silently drawing nothing, and M5 has no CPU line-geometry fallback. That is also why `DrawWireModel`
lives in this sample and not in the gallery: `PrimitiveGallery` is the M5 Native AOT gate and stays
independent of the optional feature, while Castle is the sample that demonstrates it on the HDR test
host.

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
StbImageSharp, BCnEncoder, the shaderc native libraries, or the glTF packages. The published host
needs a Vulkan device with `fillModeNonSolid` for the wireframe overlay; a device without it reports
`RB3001` and exits `1` instead of drawing a solid-only scene. The WSLg Mesa dzn hardware device is
supported; the conformant lavapipe ICD remains available for a software-only run:

```bash
VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json \
DISPLAY=:0 XDG_RUNTIME_DIR=/mnt/wslg/runtime-dir \
  samples/Castle/bin/Release/net11.0/linux-x64/publish/Castle --frames 30
```

## What the sample omits

The sample does not implement these features yet:

- **Wireframe toggle.** The solid and the wireframe overlay are always drawn; there is no key to hide
  the overlay or draw the wireframe on its own.
- **Drag-and-drop of a new model or texture.** Window file-drop events are post-1.0.
- **Click selection.** The engine ships `Collision.GetRayBox`, `Camera.DrawWireCube`, and the M5
  `Camera.DrawBoundingBox`, but the sample does not pick the castle from the mouse. Adding selection
  would mean converting the pointer to a world ray with `ScreenToWorldRay`, testing the castle's
  bounds, and drawing a wire box for the hit.
- **`DrawText` and `DrawFPS`.** Runtime text and font/MSDF support arrive in M6.

The sample cannot run with SDL's dummy video driver because the Vulkan backend needs a real window
surface; run it on a desktop session or under WSLg.

## Notices and license

The castle model and texture are by Alberto Cano (Medieval Assets Pack) under CC BY-NC 4.0 and are
sample-only; they are not covered by RayBork's MIT license and may not be used commercially. See
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for attribution and license links.
