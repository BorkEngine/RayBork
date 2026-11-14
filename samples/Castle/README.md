# Castle

`Castle` is a small RayBork sample: an orbitable castle scene with a model, a diffuse texture, an
orbital perspective camera, and the reference XZ grid. It loads the model and texture, binds the
texture on the model's materials, draws the castle solid and the grid every tick, and outlines the
castle with a surface hit point when the pointer is over it. The source is kept small and commented so
the scene can be read and adapted as a starting point.

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
| Mouse hover | Outline the castle's world bounds and mark the nearest surface point. |
| Escape | Request exit. |

Hovering the castle draws its world-space bounding box in `Color.Cyan` and a `0.2` world-unit
`Color.Red` sphere at the nearest triangle hit. Moving off the box removes both; a ray that enters the
box but misses every triangle draws the box alone.

The camera starts at `(50, 50, 50)`, looks at `(0, 12, 0)`, and uses a 45 degree vertical field of
view with a `0.01` near plane and an infinite far plane. The background is the `Color.WhiteSmoke`
preset: the Bork2d/Unity sRGB `#F5F5F5`, stored linear as `0.913099` per channel. The grid uses
`Color.Gray`, the sRGB display mid-gray stored linear as `0.214041`.

## Scene

Each tick records one perspective pass: the castle model with its built-in mesh material, the
reference grid under it, then the hover overlays when the pointer is over the castle:

```csharp
camera.DrawModel(_model!, Transform.Identity, Color.White);
camera.DrawGrid(GRID_SLICES, GRID_SPACING, Color.Gray);
DrawHoverOverlay(camera, context);
```

`GRID_SLICES` is `20` and `GRID_SPACING` is `10f`, so the grid spans 200 world units per axis in 20
cells, centered on the world origin where the castle stands. It is raylib's reference grid, drawn in
the same opaque, depth-tested pass as the model, so the castle occludes the lines behind it. The grid
is one instanced line draw and contributes no triangles.

### Hover bounds and hit point

The castle is static, so `Setup` stores its world bounds once:

```csharp
_worldBounds = _model.Bounds;
```

`Model.Bounds` is the union of the part bounds, and `ModelPart.Bounds` is its mesh bounds after the
part's local transform. The castle is drawn at `Transform.Identity`, so those model-space bounds are
the world-space bounds. They span approximately `(-24.0, 0.0, -16.3)` to `(18.2, 30.9, 16.3)`, about
`42.2` by `30.9` by `32.7` world units.

Every tick, `DrawHoverOverlay` converts `Input.MousePosition` from logical window units to render
pixels with `Window.RenderWidth / Window.Width` and calls `Camera.ScreenToWorldRay`, which applies the
camera viewport. `Collision.GetRayBox` then tests the ray against `_worldBounds`; when it misses, the
overlay draws nothing. When it hits, the sample draws the box in `Color.Cyan` with
`Camera.DrawBoundingBox` and casts the same ray against the model:

```csharp
RayCollision hit = Collision.GetRayMesh(
    pointerRay,
    part.Mesh,
    part.LocalTransform.ToMatrix());
```

The mesh transform is the part's `LocalTransform` alone because the draw's instance transform is
`Transform.Identity`. The nearest hit across the parts wins, and `Camera.DrawSphere(hit.Point, 0.2f,
Color.Red)` marks it; the radius is `0.2` world units. `Color.Cyan` separates the bounds from the
`Color.Gray` grid and the `Color.WhiteSmoke` background. The sample assumes the castle's parts are
triangle meshes with invertible local transforms, the two requirements `Collision.GetRayMesh`
enforces; it has no per-part fallback for a part that violates either assumption.

`GetRayBox` is an axis-aligned box test, so it also hits empty space inside the box, such as the gap
above the battlements or the open courtyard. The triangle cast reports no hit there, and the sample
draws only the box. Both overlays are ordinary depth-tested draws in the same opaque pass as the
model, so geometry in front of them occludes them.

The sample's own per-frame code does not allocate: the bounds are computed once, the box and mesh
queries are allocation-free static math, and the part loop is indexed so the `IReadOnlyList<ModelPart>`
enumerator is not allocated. The camera's draw helpers add the engine's draw records, and the bundled
pack's castle mesh carries the `RBVC` collision structure, so `GetRayMesh` traverses it instead of
testing every triangle.

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
- **Click selection.** Hover reports the nearest surface point, but a click has no action and the
  sample keeps no selection state.
- **`DrawText` and `DrawFPS`.** Runtime text and font/MSDF support arrive in M6.

The sample cannot run with SDL's dummy video driver because the Vulkan backend needs a real window
surface; run it on a desktop session or under WSLg.

## Notices and license

The castle model and texture are by Alberto Cano (Medieval Assets Pack) under CC BY-NC 4.0 and are
sample-only; they are not covered by RayBork's MIT license and may not be used commercially. See
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for attribution and license links.
