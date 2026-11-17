# RenderTarget

`RenderTarget` is the M8 sample and Native AOT gate: a procedural scene that renders a
second procedural scene into an offscreen `RenderTexture`, samples that target on a monitor in a later
camera pass, switches between one scene camera and two side-by-side cameras, applies a custom
vignette/tint post-process, and reads the offscreen target back to a screenshot. The scene needs no
imported models or textures; the only baked asset is the sample's post-process shader.

The sample contains no test-only logic: it opens a window, draws the scene, and exits cleanly. Build
and run it from the repository root:

```powershell
dotnet build samples/RenderTarget/RenderTarget.slnx -c Debug
dotnet run --project samples/RenderTarget/RenderTarget.csproj -c Debug -- --frames 30
```

## Scene

Every tick records up to four camera passes in creation order:

1. An offscreen perspective camera renders a small procedural scene into a `640x360`
   `Rgba16Float` render texture with a depth attachment and a requested 4x MSAA. The target is
   created asynchronously in `Setup` with `EngineContext.CreateRenderTextureAsync`, so the render
   thread allocates its images before the first tick.
2. The scene camera (single view) or the two split-screen cameras (left and right viewports) draw the
   main procedural scene. The main scene samples the offscreen target's public `ColorTexture` on the
   monitor quad; the offscreen pass is recorded first, so the renderer inserts the write-to-sample
   barrier for the later pass.
3. A later orthographic HUD camera loads the scene color, clears depth, and draws the status panel.

The offscreen scene has a central cube, a ring of four colored cubes that turns the opposite way while
the offscreen camera orbits, a sphere, and a grid. The main scene has a rotating ring of six cubes, a
stand, and the monitor that shows the offscreen color. The two rings and the offscreen camera orbit are
transform hierarchies: each ring turns a parent at its center and the cubes are fixed local transforms
attached to it, and the orbiting camera is a local transform on a rotating parent. Every moving group
advances its own wrapped angle, so no group derives its phase from another and a wrap in one angle
cannot move the others. The monitor sways around the vertical axis but never turns its back to the
camera, so the offscreen content stays readable. The monitor is a textured `DrawQuad`; it uses the same
world-space 2D primitive path as every other quad in the samples, and the target's `ColorTexture` is the
texture.

## Split-screen

`S` toggles the split-screen mode. In split mode the sample creates two independent perspective
cameras, each with half the render width as its viewport, a distinct clear color, and a different
camera position and aim:

```csharp
using Camera left = context.CreateCamera(
    LeftSplitCameraTransform(),
    projection: CameraProjection.Perspective,
    fieldOfView: 50f,
    clearMode: ClearMode.ColorAndDepth,
    clearColor: _splitLeftBackground,
    viewport: new Rect(0f, 0f, leftWidth, renderHeight));

using Camera right = context.CreateCamera(
    RightSplitCameraTransform(),
    projection: CameraProjection.Perspective,
    fieldOfView: 50f,
    clearMode: ClearMode.ColorAndDepth,
    clearColor: _splitRightBackground,
    viewport: new Rect(leftWidth, 0f, renderWidth - leftWidth, renderHeight));
```

Each camera pass is its own render pass with its own frame block, so the two views and clears stay
independent. The two viewports cover the render width exactly, including an odd pixel. The split
cameras do not receive the post-process stack: M8 documents a viewport camera's post-process source as
the full target, so a split-view effect would sample across the viewport boundary.

## Custom post-process effect

The single scene camera receives an app-lifetime `PostProcessStack` with one
`ShaderPostProcessEffect`. The effect shader is baked into `rendertarget.pck` from
`RenderTarget.Assets/Assets/shaders/` and loaded in `Setup`:

```csharp
_effectShader = await context.Assets.Load<Shader>("/shaders/vignette");
_effectShader.SetUniform("uTint", new Vector4(1.0f, 0.95f, 0.87f, 1.0f));
_effectShader.SetUniform("uVignette", 0.35f);
_postEffects.Add(new ShaderPostProcessEffect(_effectShader));
```

`E` toggles `PostProcessStack.Enabled`. The stack is snapshotted when a camera is created, so the
toggle applies to the next recorded camera, never to a frame that was already recorded. The sample
creates the main camera every tick for exactly that reason. The stack and the effect own no engine
asset; the sample disposes the shader itself in `Shutdown`.

The shader contract:

- `vignette.toml` is the shader source and sidecar. It names `vignette.vert` and `vignette.frag` and
  declares the reserved `sourceColor` texture binding. The engine binds `sourceColor` to the camera
  target's resolved color and overrides any material value for it. `sourceDepth` is not declared.
- `vignette.vert` maps the engine fullscreen quad exactly:

  ```glsl
  void main_vertex() {
      vUv = aUv;
      gl_Position = vec4(aPosition.xy * 2.0, 0.0, 1.0);
  }
  ```

- `vignette.frag` samples `sourceColor`, applies the material uniforms `uTint` (`vec4`) and
  `uVignette` (`float`), and writes scene-linear HDR color. It does not apply AgX, exposure, or sRGB
  encoding; the present pass owns display conversion.
- The engine forces opaque blending, no culling, and no depth test or depth write for the post pass.
  The shader's baked render state is therefore not used.
- The shader has both stages. A custom effect without both stages or without `sourceColor` is an
  `InvalidOperationException` from `CreateCamera`.

The stack is a target-sized chain: the engine rotates through three `Rgba16Float` post targets per
target extent per frame slot, stores the
result back into the camera target for the camera viewport, and includes it in both readback paths and
in a later camera's target sample.

## Readback and screenshot

`F2` reads the offscreen target once. It calls both `RenderTexture.ReadColorsAsync` and
`RenderTexture.ReadPixelsAsync`:

- `ReadColorsAsync` returns raw scene-linear colors indexed `[y, x]` with row zero at the top. The
  sample reads the center at `[Height / 2, Width / 2]`, prints the linear RGBA values, and preserves
  HDR components above `1`.
- `ReadPixelsAsync` returns display-referred RGBA8 in top-left row-major order after the frame's
  exposure, AgX tone mapping, and sRGB encoding. The sample prints the center RGB bytes and writes the
  full image as a 24-bit uncompressed BMP: bottom-up BGR rows with zero padding, and the readback's
  opaque alpha dropped.

Both readbacks wait for the last completed frame that wrote or sampled the target and stall the render
thread while they copy the image, so the sample calls them only on the F2 press, not every frame.
`IGame.Tick` is `void`, so the documented tick pattern is a deliberate `GetAwaiter().GetResult()` on
the two tasks; the sample comments that this is the screenshot path, not per-frame work. The target
keeps its layout and state, depth is never read, and later frames keep sampling and writing it as
usual.

The status line in the HUD and the console both report the target size, the center linear color, the
center RGBA8 color, and the saved file. The file is `rendertarget-readback.bmp` beside the running
executable (the build output directory for `dotnet run`, or the publish directory for a published
host). The BMP is standard, 24-bit, and uncompressed: it has a 14-byte `BITMAPFILEHEADER` and a 40-byte
`BITMAPINFOHEADER` with a positive height, so rows are stored bottom-up in BGR order with zero padding
to a 4-byte boundary, and the readback's opaque alpha is dropped. The sample writes it with
`BinaryPrimitives` and `FileStream`, so it needs no image codec or extra package.

## Controls

| Input | Action |
| --- | --- |
| `S` | Toggle the split-screen scene cameras. |
| `E` | Toggle the vignette post-process stack on the single scene camera. |
| `F2` | Read the offscreen target and save `rendertarget-readback.bmp`. |
| `R` | Reset the procedural rotation to zero and reverse its direction. |
| `Escape` | Request exit. |

The HUD shows the current split and effect state, the target description, and the last readback
status. The main camera, both split cameras, and the HUD camera all target the window render size; the
split cameras use left and right half-width viewports of it. The HUD is an ordinary later camera and
has no post-process stack.

## Options

| Argument | Contract |
| --- | --- |
| `--frames N` | Run `N` ticks, then request exit. `0` runs until the window closes. Default `0`. |
| `--help` | Print usage and exit `0`. |

The sample uses a 1280x720 resizable window, a `0.25` second delta clamp, and FIFO presentation. It
loads `rendertarget.pck` from beside the executable, so it has no `--assets` argument. A missing pack
prints an error and exits `1`.

## Assets

The checked-in source lives under `RenderTarget.Assets/Assets/`; the solution's assets build bakes it
into `rendertarget.pck`. No manual bake step is required.

| Source | Path in the pack | Notes |
| --- | --- | --- |
| `Assets/shaders/vignette.toml` | `/shaders/vignette` | Shader source and sidecar; declares `sourceColor`, `uTint`, and `uVignette`. |
| `Assets/shaders/vignette.vert` | dependency | Fullscreen post-process vertex stage; baked into the shader's SPIR-V payload. |
| `Assets/shaders/vignette.frag` | dependency | Vignette and tint fragment stage; baked into the shader's SPIR-V payload. |

The pack is generated output under `bin/` and `obj/` and is gitignored. The scene geometry, colors, and
text are procedural; the sample does not use the Castle or Pong assets.

## Build and run

The sample is a self-contained solution, `samples/RenderTarget/RenderTarget.slnx`, containing
`RenderTarget.csproj` and `RenderTarget.Assets/RenderTarget.Assets.csproj`. It is not part of
`src/RayBork/RayBork.slnx`. The solution builds the sample and the JIT assets executable;
`RenderTarget.Assets` runs the bake out of process over `Assets/`, bakes `rendertarget.pck`, and copies
it beside the sample executable. The JIT bake tool and its packages never enter the sample's AOT
publish graph.

`RenderTarget` references `RayBork.Engine`; `RenderTarget.Assets` references `RayBork.Bake` and does not
reference the sample. The assets project can be built on its own with
`dotnet build samples/RenderTarget/RenderTarget.Assets/RenderTarget.Assets.csproj -c Debug`; it builds
the bake tool, bakes the pack, and copies it into the sample's output directory.

`dotnet publish` builds only the sample and does not build `RenderTarget.Assets`, so the pack must come
from a preceding Release solution build. The publish target copies that pack into the publish directory
and fails with a clear error when it is missing.

## Native AOT

`RenderTarget` is the M8 sample and Native AOT gate. Build the solution in Release first so
`RenderTarget.Assets` bakes and copies `rendertarget.pck`, then publish and run the Windows host from
the repository root:

```powershell
dotnet build samples/RenderTarget/RenderTarget.slnx -c Release
dotnet publish samples/RenderTarget/RenderTarget.csproj -c Release -r win-x64 -p:PublishAot=true
samples/RenderTarget/bin/Release/net11.0/win-x64/publish/RenderTarget.exe --frames 30
```

Publish the Linux host from the repository root inside WSL and run it on the WSLg display with the
conformant lavapipe ICD:

```bash
$HOME/.dotnet/dotnet build samples/RenderTarget/RenderTarget.slnx -c Release
$HOME/.dotnet/dotnet publish samples/RenderTarget/RenderTarget.csproj -c Release -r linux-x64 -p:PublishAot=true
VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json \
DISPLAY=:0 XDG_RUNTIME_DIR=/mnt/wslg/runtime-dir \
  samples/RenderTarget/bin/Release/net11.0/linux-x64/publish/RenderTarget --frames 30
```

Both publishes finish with zero AOT/trim warnings. The Windows `win-x64` publish directory contains
`rendertarget.pck` beside `RenderTarget.exe` and no `RenderTarget.Assets.dll`, `RayBork.Bake.dll`,
Tomlyn, StbImageSharp, BCnEncoder, the shaderc native libraries, or the glTF packages; the published
Windows host runs on the hardware driver. The Linux `linux-x64` publish directory contains
`rendertarget.pck` beside `RenderTarget` and no bake tooling; the published Linux host runs on the
conformant lavapipe (llvmpipe) software driver with `VK_ICD_FILENAMES` pointing at `lvp_icd.json`.
WSLg's hardware Vulkan path (Mesa dzn) is not a target.

## Limitations

The sample has one fixed window layout and no orbit controls; the procedural rotation, the split
toggle, the effect toggle, and the readback are the whole interaction. It does not resize the
`RenderTexture` or demonstrate `DepthTexture` sampling, SSAO, bloom, or screen-space image capture of
the final window. The split-screen cameras have no post-process stack because offset effects are
documented as cross-viewport for M8; per-viewport effect sources are a follow-up. The screenshot path
is display-referred SDR RGBA8, not the scRGB HDR output.
