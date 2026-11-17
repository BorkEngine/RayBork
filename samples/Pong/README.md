# Pong

`Pong` is the M7 sample and Native AOT gate: a player-vs-AI match drawn with two ordinary screen-space
orthographic cameras. A 1280x720 design playfield is letterboxed into the current render pixels; a
scene camera draws the background, border, net, textured paddles, and ball through the built-in
fast dual-filter bloom effect, and a later HUD camera draws the score panels, scores, status line, and
pause/winner overlay with `Camera.DrawQuad`, `Camera.DrawDisc`, and `Camera.DrawText` through
`Camera.ScreenToWorldTransform` without a post-process stack. Ball, paddle, and wall collisions flash
scene-linear HDR colors above the bloom threshold and decay over a few frames. The left paddle is the
human; the right paddle is a deterministic AI opponent. The paddle texture and the design values
come from the sample's own baked `pong.pck`, so the sample also exercises the texture and TOML data
asset paths end to end.

The sample contains no test-only logic: it opens a window, plays the match, and exits cleanly. Build
and run it from the repository root:

```powershell
dotnet build samples/Pong/Pong.slnx -c Debug
dotnet run --project samples/Pong/Pong.csproj -c Debug -- --frames 30
```

## Scene

Every tick records two sequential camera passes, both with an orthographic camera at `(0, 0, 1)`
looking down `-Z`. The camera's visible height is `Window.RenderHeight`, so the screen plane at
distance `1` has a world-units-per-pixel scale of exactly one:

```csharp
Transform cameraTransform = new(new Vector3(0f, 0f, SCREEN_PLANE_DISTANCE));
```

The scene camera clears color and depth with `ClearMode.ColorAndDepth` and the letterbox color, and it
runs the app-lifetime bloom stack. After it is disposed, the HUD camera uses `ClearMode.Depth` to load
the scene color and clear depth and has no stack:

```csharp
using (Camera scene = context.CreateCamera(
    cameraTransform,
    projection: CameraProjection.Orthographic,
    orthographicHeight: context.Window.RenderHeight,
    clearMode: ClearMode.ColorAndDepth,
    clearColor: _letterboxColor,
    near: CAMERA_NEAR,
    far: CAMERA_FAR,
    postProcess: _postProcess)) {
    DrawScene(scene);
}

using Camera hud = context.CreateCamera(
    cameraTransform,
    projection: CameraProjection.Orthographic,
    orthographicHeight: context.Window.RenderHeight,
    clearMode: ClearMode.Depth,
    near: CAMERA_NEAR,
    far: CAMERA_FAR);
DrawHud(hud);
```

The 16:9 design playfield is fitted into the current render pixels and centered; the remainder is the
scene camera's clear color, which reads as the letterbox bars. `scale = playfieldWidth / 1280` maps
design pixels to render pixels, and every size passed to a draw is already in render pixels:

```csharp
Vector2 pixel = new(playfieldLeft + (design.X * scale), playfieldTop + (design.Y * scale));
Transform screen = camera.ScreenToWorldTransform(pixel, 1f);
```

Cameras are sequential: the scene pass is disposed before the HUD camera is created. Both cameras use
the same projection, full viewport, and screen-plane distance, so the returned transform is identical
in both passes and every HUD position is unchanged. `ClearMode.Depth` loads the scene color and clears
depth, so HUD text is not occluded by scene depth; there is no separate overlay pass, and the HUD
camera has no post-process stack.

The scene pass draws the playfield, the inset border, the dashed net, the two textured paddles, and
the ball disc. The HUD pass draws the two score panels, the scores, and the status line in the
built-in mono face; when paused or finished it records an alpha-blended shade before that text and
draws the pause/winner title and hint on top. The shade uses `RenderState.AlphaBlend`; every other
quad/disc draw uses the default opaque state, and `DrawText` records the engine's alpha text state.
The paddles and the ball are `DrawQuad` and `DrawDisc` with the screen transform, and all text is
`DrawText` with pixel sizes.

The palette is authored with `Color.FromSrgbBytes`: a dark navy playfield, a warm border and net, a
warm paddle texture, a pale gold ball, and a dimmed overlay.

## Bloom and collision flash

The sample owns one app-lifetime `BloomEffect` and adds it to one `PostProcessStack` in `Setup`; only
the scene camera receives the stack. The effect uses its approved defaults: threshold `1`, soft knee
`0.5`, intensity `0.7`, scatter `0.7`, and a white tint. It runs the five-level fast dual-filter
pyramid over the scene target and adds the result back into the scene before the HUD camera draws, so
the scores, status line, and overlay keep their exact colors and are never bloomed. `B` toggles
`BloomEffect.Enabled`; the change applies to cameras created after that tick.

Every collision starts a short flash that decays over `0.15` seconds. The ball flashes gold, the hit
paddle flashes stronger warm white, and a top or bottom wall hit flashes the border amber. The flash
colors are scene-linear values above `1` (for example `6, 5, 3` for a paddle), so they pass the bloom
threshold and produce a halo; the ordinary playfield colors stay below it. There are no test-only
diagnostics: the status line shows `bloom on` or `bloom off` with the rest of the controls.

## Gameplay

The match runs in design space: the playfield is `1280x720` with its origin at the top-left and `+Y`
pointing down. The human moves the left paddle; the AI moves the right paddle. Paddles are `18x110`,
the ball radius is `12`, the human paddle moves at `520` design pixels per second, and the ball
serves at `420` design pixels per second. These values are the defaults in `PongSettings` and the
values baked into `Assets/data/game.toml`.

Simulation uses fixed `1/120` second steps with a `0.25` second ceiling per tick, so a slow tick
cannot move the ball through a paddle or a side. The ball bounces off the top and bottom, and a
paddle collision uses `Collision.CheckBoxSphere` with the paddle as an axis-aligned box and the ball
as a sphere. The contact offset along the paddle sets the return angle. Every paddle hit adds
`BallSpeedupPerHit` (`24` design pixels per second) to the ball speed up to `MaxBallSpeed` (`900`),
for both the human and the AI, so a rally gets faster; a serve or reset restores `BallSpeed`
(`420`). When the ball center passes a side, the other player scores; the loser receives the next
serve. The opening serve is fixed toward the right, so repeated runs start from the same state and no
RNG is used. The first player to `MaxScore` wins, and the overlay offers a restart.

The AI is deterministic and runs inside the same fixed `1/120` substeps. While the ball moves toward
it (positive X velocity) it moves toward the ball's Y at `AiPaddleSpeed` (`400` design pixels per
second); once the ball moves away it returns toward the playfield center. A `5` design-pixel deadzone
stops it from jittering at its target, and the paddle stays clamped to the playfield. `AiPaddleSpeed`
is below the human `PaddleSpeed`, and the hit speed-up eventually carries the ball past
`AiPaddleSpeed`, so the AI is beatable rather than a wall.

Controls:

| Input | Action |
| --- | --- |
| `W` / `S` | Move the left paddle. |
| `Up` / `Down` | Move the left paddle. |
| Gamepad 0 left stick | Move the left paddle. |
| Gamepad 0 d-pad up / down | Move the left paddle. |
| `Space` / gamepad Start | Pause or resume. |
| `R` / gamepad Back | Restart the match. |
| `B` | Toggle bloom for the scene camera. |
| `Escape` | Request exit. |

The sample applies a `0.2` stick deadzone because the engine applies none. A missing gamepad is
ignored, and the keyboard controls stay available. The gamepad's right stick does not move the AI
paddle.

## Settings asset

`Assets/data/game.toml` selects the registered `Pong.PongSettings` type. The keys are snake_case and
the values are design pixels or design pixels per second:

```toml
[raybork]
type = "Pong.PongSettings"

paddle_texture = "/textures/paddle"
ball_radius = 12.0
max_score = 11
paddle_width = 18.0
paddle_height = 110.0
paddle_speed = 520.0
ball_speed = 420.0
ball_speedup_per_hit = 24.0
max_ball_speed = 900.0
ai_paddle_speed = 400.0
```

`PongSettings` lives only in `samples/Pong/PongSettings.cs` and is compiled only by `Pong.csproj`.
`Pong.Assets.csproj` references `Pong.csproj` directly and compiles against that project reference;
it does not link or duplicate the source file. The host registers the type with
`WithAssetType<PongSettings>()`, and the assets executable registers it with
`RegisterAsset<PongSettings>()`; both are backed by the compiled `[GenerateShape]`, so the runtime
load is reflection-free and Native AOT compatible.

`Setup` loads the settings from `/data/game` and rejects bad values with a clear
`InvalidOperationException` that names the setting: the texture path must be non-empty; the ball
radius, paddle size, paddle speeds, ball speeds, and AI speed must be positive and finite;
`BallSpeedupPerHit` must be zero or positive and finite; `MaxBallSpeed` must be at least `BallSpeed`;
the paddle size and ball radius must fit the playfield; and `MaxScore` must be positive. A missing or
malformed settings entry fails the bake or the asset load.

## Assets

The checked-in sources live under `Pong.Assets/Assets/`; the solution's assets build bakes them into
`pong.pck`. No manual bake step is required.

| Source | Path in the pack | Notes |
| --- | --- | --- |
| `Assets/textures/paddle.png` | `/textures/paddle` | 32x128 RGB paddle art, baked by the ordinary texture processor with the default linear sampler and the full mip chain. |
| `Assets/data/game.toml` | `/data/game` | TOML data bound to `Pong.PongSettings`. |

The paddle texture is original sample art made for this repository: a vertical warm gradient with a
dark border and a left highlight, generated deterministically and committed as an opaque RGB PNG. It
is covered by RayBork's MIT license, not a third-party asset. The texture is stretched onto the
`18x110` paddle quad; the runtime texture is opaque, so no alpha blending is involved in the paddle
draw.

The pack is generated output under `bin/` and `obj/` and is gitignored.

## Options

| Argument | Contract |
| --- | --- |
| `--frames N` | Run `N` ticks, then request exit. `0` runs until the window closes. Default `0`. |
| `--help` | Print usage and exit `0`. |

The sample uses a 1280x720 resizable window, a `0.25` second delta clamp, and FIFO presentation. It
loads `pong.pck` from beside the executable, so it has no `--assets` argument. A missing pack prints
an error and exits `1`.

## Build and run

The sample is a self-contained solution, `samples/Pong/Pong.slnx`, containing `Pong.csproj` and
`Pong.Assets/Pong.Assets.csproj`. It is not part of `src/RayBork/RayBork.slnx`. The solution builds
the sample and then the JIT assets executable; `Pong.Assets` runs the bake executable out of process
over `Assets/`, bakes `pong.pck`, and copies it beside the sample executable. The JIT bake tool and
its packages never enter the sample's AOT publish graph.

The sample and its assets executable form a small project graph. `Pong` references
`RayBork.Engine`; `Pong.Assets` references `RayBork.Bake` and `Pong` so the bake binds the same
`PongSettings` type. The project reference orders the solution build: `Pong` is built once, then
`Pong.Assets`, which bakes and copies the pack. `Pong` does not reference or build `Pong.Assets`, so
the graph has no cycle. The assets project can be built on its own with
`dotnet build samples/Pong/Pong.Assets/Pong.Assets.csproj -c Debug`; it builds `Pong` and the bake
tool, then bakes and copies the pack.

`dotnet publish` builds only the sample and does not build `Pong.Assets`, so the pack must come from
a preceding Release solution build. The publish target copies that pack into the publish directory
and fails with a clear error when it is missing.

## Native AOT

`Pong` is the M7 Native AOT gate. Build the solution in Release first so `Pong.Assets` bakes and
copies `pong.pck`, then publish and run the Windows host from the repository root:

```powershell
dotnet build samples/Pong/Pong.slnx -c Release
dotnet publish samples/Pong/Pong.csproj -c Release -r win-x64 -p:PublishAot=true
samples/Pong/bin/Release/net11.0/win-x64/publish/Pong.exe --frames 30
```

Publish the Linux host from the repository root inside WSL and run it on the WSLg display with the
conformant lavapipe ICD:

```bash
$HOME/.dotnet/dotnet build samples/Pong/Pong.slnx -c Release
$HOME/.dotnet/dotnet publish samples/Pong/Pong.csproj -c Release -r linux-x64 -p:PublishAot=true
VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json \
DISPLAY=:0 XDG_RUNTIME_DIR=/mnt/wslg/runtime-dir \
  samples/Pong/bin/Release/net11.0/linux-x64/publish/Pong --frames 30
```

The Release solution build is what bakes and copies the pack; the publish copies it beside the host.
Both publishes finished with zero AOT/trim warnings. The Windows `win-x64` publish directory contains
`pong.pck` beside `Pong.exe` and no `Pong.Assets.dll`, `RayBork.Bake.dll`, Tomlyn, StbImageSharp,
BCnEncoder, shaderc native libraries, or glTF packages; the published Windows host ran `--frames 30`
to exit `0` on the hardware driver. The Linux `linux-x64` publish directory contains `pong.pck` beside
`Pong` and no bake tooling; the published Linux host ran `--frames 30` to exit `0` on llvmpipe
(lavapipe) with an SDR `B8G8R8A8_SRGB` swapchain, FIFO presentation, 4x MSAA, and a 1280x720 window.
WSLg's hardware Vulkan path (Mesa dzn) is not a target.

## Limitations

The sample is a single human against a deterministic AI, with no audio and no gamepad rumble. It has
one fixed 16:9 design layout; the letterbox bars absorb any window aspect. It draws no texture atlas
or sprite batching beyond the engine's normal batch path.
