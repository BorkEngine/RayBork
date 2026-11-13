# Primitive Gallery

`Primitive Gallery` is a small RayBork sample that draws every M5 primitive helper in one perspective
camera pass with the built-in primitive shader: lines, a polyline, a point, a triangle, a cube, a
sphere, a cylinder, a plane, a reference grid, a wire cube, a wire sphere, a billboard, and a
screen-space texture. It builds its only texture at runtime, so it needs no asset pack, no manual
bake, and no `--assets` argument.

The sample is also a Native AOT gate, but it contains no test-only logic: it opens a window, draws the
gallery, and exits cleanly. Build and run it from the repository root:

```powershell
dotnet build samples/PrimitiveGallery/PrimitiveGallery.slnx -c Debug
dotnet run --project samples/PrimitiveGallery/PrimitiveGallery.csproj -c Debug -- --frames 30
```

## Scene

The scene has no lights: every helper is unlit and uses the built-in vertex-color or texture shader.
The reference grid lies on the ground plane, and the helpers are arranged in two rows above it. The
rows run along the initial camera's horizontal axis, so the composition reads left to right before
the first orbit. Every helper gets its own linear color.

| Helper | Color | Placement |
| --- | --- | --- |
| `DrawGrid(20, 1.0f)` | dark gray | The reference grid on the ground plane, centered on the origin. |
| `DrawCube` | teal | Lower row, left. A solid cube resting on the grid. |
| `DrawSphere` | blue | Lower row, next to the cube. |
| `DrawCylinder` | purple | Lower row, center-left. A capped cylinder standing on the grid. |
| `DrawTriangle` | green | Lower row, center-right. A front-facing triangle in the row's vertical plane. |
| `DrawPoint` | yellow | Lower row, center-right. A camera-facing quad of `0.7` world units. |
| `DrawPlane` | magenta | Lower row, right. A `2.6 x 2.6` XZ plane just above the grid. |
| `DrawWireCube` | amber | Upper row, left. |
| `DrawWireSphere` | cyan | Upper row, next to the wire cube. |
| `DrawLine` | red | Upper row, center-left. One diagonal line segment. |
| `DrawLines` | orange | Upper row, center. A four-point zigzag; three segments. |
| `DrawBillboard` | warm white tint | Upper row, right. A world-space textured quad that faces the camera. |
| `DrawTexture` | white tint | A `160 x 90` screen-space overlay in the bottom-left corner, in render pixels. |

The billboard and the overlay sample the same procedural texture. The triangle is single-sided, like
the engine's primitive material, so it is culled when the camera orbits behind it.

## Texture

`Setup` creates one `128 x 128` `Rgba8Srgb` texture with `EngineContext.CreateTextureBuilder`. A warm
left half, a cool right half, and a 16-pixel checkerboard make the texture's orientation visible on
both the billboard and the screen-space quad. The texture is uploaded with `BuildAsync` and disposed
in `Shutdown`. It is the only asset the sample owns.

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
view with a `0.05` near plane and an infinite far plane. The background is a dark blue-gray
(`0.04, 0.05, 0.07` linear). The same camera pass draws the whole scene and the screen-space overlay;
the overlay's `DrawTexture` call maps its destination rectangle to render pixels and ignores scene
depth.

## Build and run

The sample is a self-contained solution, `samples/PrimitiveGallery/PrimitiveGallery.slnx`, containing
`PrimitiveGallery.csproj`. It is not part of `src/RayBork/RayBork.slnx`. The project references
`RayBork.Engine` directly; the engine's built-in pack supplies the primitive meshes and shaders, so
the sample has no bake step and no pack of its own.

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
packages: the sample has no bake tooling and no external pack. The published host opens a real window,
so run it on a desktop session or under WSLg.

## What the sample omits

- **Text labels.** `DrawText` arrives in M6, so the README documents which helper is which instead of
  drawing labels.
- **Thick lines and point sprites.** M5 draws one-pixel lines and camera-facing point quads.
- **Animation.** The scene is static; only the orbit camera moves.
