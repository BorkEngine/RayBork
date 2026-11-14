# Primitive Gallery

`Primitive Gallery` is a small RayBork sample that draws the M5 primitive helper families in one perspective
camera pass: the solid shapes (cube, sphere, cylinder and cone, capsule, plane, triangle, triangle
strip, point), the wire shapes (wire cube, wire sphere, wire cylinder, wire capsule, bounding box),
the line and curve helpers (line, polyline, ray, circle), a reference grid, two billboards, and a row
of meshes generated with `MeshBuilder.Generate*` and drawn with the built-in mesh shader. It builds
its texture and its generated meshes at runtime, so it needs no asset pack, no manual bake, and no
`--assets` argument.

The sample is also a Native AOT gate, but it contains no test-only logic: it opens a window, draws the
gallery, and exits cleanly. Build and run it from the repository root:

```powershell
dotnet build samples/PrimitiveGallery/PrimitiveGallery.slnx -c Debug
dotnet run --project samples/PrimitiveGallery/PrimitiveGallery.csproj -c Debug -- --frames 30
```

## Scene

The scene has no lights: the solid, wire, line, and billboard helpers are unlit and use the built-in
primitive shader, and the generated row uses the built-in mesh shader through a caller-owned
material. The reference grid lies on the ground plane, and the shapes are arranged in three rows
above it. The rows run along the initial camera's horizontal axis, so the composition reads left to
right before the first orbit. Every draw uses a named `Color` preset, and each preset is the
Bork2d/Unity sRGB color converted to linear.

The lower row is six stations above the grid. The first five pair a helper with the M5 form of
the same family: the scalar solid and wire cube, the fixed and tessellated sphere, the equal-radius
cylinder and its cone, the solid and wire capsule, and the triangle with its strip. Every pair is
spaced with a clear gap at the starting camera distance, so no two shapes touch or intersect. The last
station puts the point above the plane.

The middle row is five generated meshes; the [Generated meshes](#generated-meshes) section covers the
build path. The upper row floats above it: the wire shapes on the left, the line and curve helpers in
the middle, and the ray/bounding-box pair and the two billboards on the right.

| Helper | Color | Placement |
| --- | --- | --- |
| `DrawGrid(20, 1.0f)` | `Color.Gray5` | The reference grid on the ground plane, centered on the origin. |
| `DrawCube` (scalar) | `Color.LightSeaGreen` | Lower row, far left. A solid 1-unit cube resting on the grid, using the explicit `width`/`height`/`length` form. |
| `DrawWireCube` (scalar) | `Color.Orange` | Lower row, far left. The 1-unit scalar wire cube beside the solid cube, with a clear gap between them. |
| `DrawSphere` | `Color.RoyalBlue` | Lower row, left-center. The fixed-mesh UV sphere. |
| `DrawSphere` (rings/slices) | `Color.CadetBlue` | Lower row, left-center. The tessellated sphere beside it, faceted at 5 rings by 10 slices. |
| `DrawCylinder` | `Color.Purple` | Lower row, center-left. A capped equal-radius cylinder standing on the grid. |
| `DrawCylinder` (`Transform`, cone) | `Color.MediumOrchid` | Lower row, center-left. A cone (`radiusTop` 0, `radiusBottom` 0.5) leaning around the view axis: the gallery's `Transform` example. |
| `DrawCapsule` | `Color.IndianRed` | Lower row, center. The solid capsule between two spherical cap centers. |
| `DrawWireCapsule` | `Color.LightCoral` | Lower row, center. The wire capsule beside it. |
| `DrawTriangle` | `Color.LimeGreen` | Lower row, center-right. A front-facing triangle in the row's vertical plane. |
| `DrawTriangleStrip` | `Color.YellowGreen` | Lower row, center-right. A six-point strip beside the triangle. |
| `DrawPoint` | `Color.Yellow` | Lower row, right. A camera-facing quad of `0.55` world units above the plane. |
| `DrawPlane` | `Color.MediumVioletRed` | Lower row, right. A `2.2 x 2.2` XZ plane just above the grid. |
| `GenerateCube` then `DrawMesh` | `Color.Coral` | Generated row, left. A `0.9`-unit cube built in `Setup` and drawn with the mesh shader. |
| `GenerateSphere` then `DrawMesh` | `Color.Aquamarine` | Generated row, left-center. A `0.5`-radius sphere, 12 rings by 24 slices. |
| `GenerateTorus` then `DrawMesh` | `Color.GoldenRod` | Generated row, center. A `0.42` major radius by `0.14` tube torus, turned a quarter around the row axis so its hole faces the initial camera. |
| `GenerateKnot` then `DrawMesh` | `Color.Violet` | Generated row, center-right. A `0.4` winding radius by `0.12` tube trefoil knot. |
| `GenerateCone` then `DrawMesh` | `Color.SeaGreen` | Generated row, right. A `0.42` base radius by `0.9` height cone; the transform puts its base half a height below the row center. |
| `DrawWireCube` | `Color.Orange` | Upper row, far left. The vector `size` form of the wire cube. |
| `DrawWireSphere` | `Color.Turquoise` | Upper row, left-center. The fixed-mesh wire sphere (three great circles). |
| `DrawWireSphere` (rings/slices) | `Color.SkyBlue` | Upper row, left-center. The tessellated wire sphere beside it, 4 rings by 10 slices. |
| `DrawWireCylinder` | `Color.DarkCyan` | Upper row, center-left. A 12-sided wire cylinder. |
| `DrawCircle` | `Color.MediumSlateBlue` | Upper row, center-left. The axis-angle form rotated to a vertical 36-segment ring facing the initial camera. |
| `DrawLine` | `Color.SoftRed` | Upper row, center. One diagonal line segment. |
| `DrawLines` | `Color.DarkOrange` | Upper row, center. A four-point zigzag; three segments. |
| `DrawBoundingBox` | `Color.Silver` | Upper row, center-right. The twelve edges of a world-space axis-aligned box. |
| `DrawRay` | `Color.Gold` | Upper row, center-right. An effectively infinite ray that starts just outside the box and passes through its center; the engine normalizes its direction and draws a long segment, so the ray continues well past the box. |
| `DrawBillboard` | `Color.Beige` tint | Upper row, right. A full-texture quad that faces the camera. |
| `DrawBillboard` (source rect) | `Color.Beige` tint | Upper row, right. The same texture cropped to its warm left half through the `source` rectangle. |

The cone is the gallery's one `Transform` overload and doubles as the placement contract: the
transform position is the bottom-cap center, the rotation around the initial view axis leans the
shape, and the unit scale keeps the authored `radiusTop`/`radiusBottom`/`height`. The circle instead
uses the axis-angle form: its base circle lies in the local `XZ` plane, and a 90 degree rotation
around the row axis stands it up to face the initial camera. The two billboards sit side by side so
the per-instance source rectangle reads against the uncropped quad.

The triangle and the triangle strip are single-sided, like the engine's primitive material, so they
are culled when the camera orbits behind them. The billboards face the orbiting camera.

## Generated meshes

`Setup` creates one `MeshBuilder` with `EngineContext.CreateMeshBuilder` and reuses it for five
builds: `GenerateCube(0.9, 0.9, 0.9)`, `GenerateSphere(0.5, 12, 24)`,
`GenerateTorus(0.42, 0.14, 32, 12)`, `GenerateKnot(0.4, 0.12, 48, 8)`, and
`GenerateCone(0.42, 0.9, 24)`. Each `Generate*` call replaces the builder's geometry and fills
positions, outward normals, and UVs; `BuildAsync` then uploads that snapshot as a caller-owned mesh,
so reusing the builder is safe. The five meshes are immutable triangle lists uploaded during
`Setup`, before the first tick.

The row is drawn with `Camera.DrawMesh`, one material created by
`EngineDefaults.CreateMeshShader`, and a per-shape tint. The material's `mainTexture` stays unbound
and samples the renderer's white fallback, so the tint is the shape color, and the mesh shader's
default opaque render state culls back faces exactly like the primitive helpers. The five meshes and
the material are disposed in `Shutdown`, alongside the texture. The path needs no bake and no
optional device feature, so it stays inside the gallery's Native AOT gate.

## Texture

`Setup` creates one `128 x 128` `Rgba8Srgb` texture with `EngineContext.CreateTextureBuilder`. A warm
left half, a cool right half, and a 16-pixel checkerboard make the texture's orientation visible on
the billboards. The warm checker colors are `Color.GoldenRod` and `Color.SaddleBrown`; the cool pair is
`Color.CornflowerBlue` and `Color.MidnightBlue`. The texture is uploaded with `BuildAsync` and
disposed in `Shutdown`, together with the generated meshes and the mesh material.

## Options

| Argument | Contract |
| --- | --- |
| `--frames N` | Run `N` ticks, then request exit. `0` runs until the window closes. Default `0`. |
| `--help` | Print usage and exit `0`. |

The sample uses an 800x450 resizable window, a 60 FPS target, a `0.25` second delta clamp, and the
engine's default renderer settings. An unknown option prints the usage text and exits `2`.

## Controls

| Input | Action |
| --- | --- |
| Left mouse drag | Orbit the camera around the scene. Pitch is clamped to ±80 degrees. |
| Mouse wheel | Zoom the orbit distance, clamped to 6–60. |
| Escape | Request exit. |

The camera starts at `(8, 6.2, 12.5)`, looks at `(0, 2.4, 0)`, and uses a 45 degree vertical field of
view with a `0.05` near plane and an infinite far plane. The background is `Color.Gray3` (sRGB
`#4D4D4D`, stored linear as `0.073239`) and the grid is `Color.Gray5` (sRGB `#808080`, stored linear
as `0.214041`). The camera pass draws the whole scene.

## Build and run

The sample is a self-contained solution, `samples/PrimitiveGallery/PrimitiveGallery.slnx`, containing
`PrimitiveGallery.csproj`. It is not part of `src/RayBork/RayBork.slnx`. The project references
`RayBork.Engine` directly; the engine's built-in pack supplies the primitive meshes, the primitive
shader, and the mesh shader program, and `Setup` builds the texture and the procedural meshes, so the
sample has no bake step and no pack of its own.

## Native AOT

Publish and run the Windows host from the repository root:

```powershell
dotnet publish samples/PrimitiveGallery/PrimitiveGallery.csproj -c Release -r win-x64 -p:PublishAot=true
samples/PrimitiveGallery/bin/Release/net11.0/win-x64/publish/PrimitiveGallery.exe --frames 30
```

Publish the Linux host on a Linux machine (or from WSL) and run it on a desktop session or WSLg
display:

```bash
dotnet publish samples/PrimitiveGallery/PrimitiveGallery.csproj -c Release -r linux-x64 -p:PublishAot=true
samples/PrimitiveGallery/bin/Release/net11.0/linux-x64/publish/PrimitiveGallery --frames 30
```

Both publishes must finish with zero AOT/trim warnings. The publish directory must not contain
`RayBork.Bake.dll`, Tomlyn, StbImageSharp, BCnEncoder, the shaderc native libraries, or the glTF
packages: the sample has no bake tooling and no external pack, and the generated meshes upload at
`Setup`. The published host opens a real window, so run it on a desktop session or under WSLg.

## What the sample omits

- **Text labels.** `DrawText` arrives in M6, so the README documents which helper is which instead of
  drawing labels.
- **Thick lines and point sprites.** M5 draws one-pixel lines and camera-facing point quads.
- **Overload completeness.** The gallery shows one representative call per helper family instead of
  every overload. The start/end `DrawCylinder` form, the other `Transform` overloads, the non-facing
  billboard forms, and the billboard origin pivot are documented in
  [docs/api/rendering/camera.md](../../docs/api/rendering/camera.md) and
  [docs/api/rendering/draws.md](../../docs/api/rendering/draws.md).
- **Generated-mesh coverage.** The generated row shows five of the nine `Generate*` methods.
  `GeneratePoly`, `GeneratePlane`, `GenerateHemiSphere`, and `GenerateCylinder` are documented in
  [docs/api/rendering/meshes.md](../../docs/api/rendering/meshes.md) and are not drawn, and the row
  uses the default render state with no texture binding. `DrawInstanced`, `ComputeNormals`,
  `ComputeTangents`, and `GenerateCollisions` are likewise documented but not shown.
- **Wireframe model drawing.** `DrawWireModel` is deliberately not part of this sample: the gallery is
  the Native AOT gate and must not depend on the optional `fillModeNonSolid` device feature. The
  `ModelWireframeGpuTests` suite covers the wireframe pipeline; the Castle sample no longer draws a
  wireframe overlay.
- **Animation.** The scene is static; only the orbit camera moves.
