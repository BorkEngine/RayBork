# Pong

`Pong` is the M7 sample and Native AOT gate: a complete two-paddle match drawn as one screen-space
orthographic pass. A 1280x720 design playfield is letterboxed into the current render pixels, and the
background, border, net, score panels, textured paddles, ball, scores, status line, and pause/winner
overlay are all recorded with `Camera.DrawQuad`, `Camera.DrawDisc`, and `Camera.DrawText` through
`Camera.ScreenToWorldTransform`. The paddle texture and the design values come from the sample's own
baked `pong.pck`, so the sample also exercises the texture and TOML data asset paths end to end.

The sample contains no test-only logic: it opens a window, plays the match, and exits cleanly. Build
and run it from the repository root:

```powershell
dotnet build samples/Pong/Pong.slnx -c Debug
dotnet run --project samples/Pong/Pong.csproj -c Debug -- --frames 30
```

## Scene

Every tick records exactly one camera pass with an orthographic camera at `(0, 0, 1)` looking down
`-Z`. The camera's visible height is `Window.RenderHeight`, so the screen plane at distance `1` has a
world-units-per-pixel scale of exactly one:

```csharp
using Camera camera = context.CreateCamera(
    new Transform(new Vector3(0f, 0f, 1f)),
    projection: CameraProjection.Orthographic,
    orthographicHeight: context.Window.RenderHeight,
    clearMode: ClearMode.ColorAndDepth,
    clearColor: letterboxColor,
    near: 0.01f,
    far: 100f);
```

The 16:9 design playfield is fitted into the current render pixels and centered; the remainder is the
camera's clear color, which reads as the letterbox bars. `scale = playfieldWidth / 1280` maps design
pixels to render pixels, and every size passed to a draw is already in render pixels:

```csharp
Vector2 pixel = new(playfieldLeft + (design.X * scale), playfieldTop + (design.Y * scale));
Transform screen = camera.ScreenToWorldTransform(pixel, 1f);
```

The pass draws the playfield, the inset border, the dashed net, the two score panels, the two textured
paddles, and the ball disc. It then draws the scores and the status line in the built-in mono face;
when paused or finished it records an alpha-blended shade before that text and draws the pause/winner
title and hint on top. The shade uses `RenderState.AlphaBlend`; every other quad/disc draw uses the
default opaque state, and `DrawText` records the engine's alpha text state. The paddles and the ball
are `DrawQuad` and `DrawDisc` with the screen transform, and all text is `DrawText` with pixel sizes.

The palette is authored with `Color.FromSrgbBytes`: a dark navy playfield, a warm border and net, a
warm paddle texture, a pale gold ball, and a dimmed overlay.

## Gameplay

The match runs in design space: the playfield is `1280x720` with its origin at the top-left and `+Y`
pointing down. Paddles are `18x110`, the ball radius is `12`, the paddles move at `520` design pixels
per second, and the ball moves at `420` design pixels per second. These values are the defaults in
`PongSettings` and the values baked into `Assets/data/game.toml`.

Simulation uses fixed `1/120` second steps with a `0.25` second ceiling per tick, so a slow tick
cannot move the ball through a paddle or a side. The ball bounces off the top and bottom, and a
paddle collision uses `Collision.CheckBoxSphere` with the paddle as an axis-aligned box and the ball
as a sphere. The contact offset along the paddle sets the return angle, and the speed is restored to
`BallSpeed`, so every serve and bounce keeps one speed. When the ball center passes a side, the other
player scores; the loser receives the next serve. The opening serve is fixed toward the right, so
repeated runs start from the same state and no RNG is used. The first player to `MaxScore` wins, and
the overlay offers a restart.

Controls:

| Input | Action |
| --- | --- |
| `W` / `S` | Move the left paddle. |
| `Up` / `Down` | Move the right paddle. |
| Gamepad 0 left stick / right stick | Move the left / right paddle. |
| Gamepad 0 d-pad up / down | Move the right paddle. |
| `Space` / gamepad Start | Pause or resume. |
| `R` / gamepad Back | Restart the match. |
| `Escape` | Request exit. |

The sample applies a `0.2` stick deadzone because the engine applies none. A missing gamepad is
ignored, and the keyboard controls stay available.

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
```

`PongSettings` is one file compiled by both projects: `Pong.csproj` includes it normally and
`Pong.Assets.csproj` links `../PongSettings.cs`, so the bake and the runtime bind the same type. The
host registers it with `WithAssetType<PongSettings>()`, and the assets executable registers it with
`RegisterAsset<PongSettings>()`; both are backed by the compiled `[GenerateShape]`, so the runtime
load is reflection-free and Native AOT compatible.

`Setup` loads the settings from `/data/game`, validates that the texture path is non-empty and that
the ball, paddle, and speed values are positive and finite, bounds the paddle size and ball radius to
the playfield, requires a positive `MaxScore`, then loads the paddle texture. Invalid values throw a
clear `InvalidOperationException`; a missing or malformed settings entry fails the bake or the asset
load.

## Assets

The checked-in sources live under `Pong.Assets/Assets/`; the sample build bakes them into `pong.pck`.
No manual bake step is required.

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
`Pong.Assets/Pong.Assets.csproj`. It is not part of `src/RayBork/RayBork.slnx`. The build runs the JIT
assets executable out of process, bakes `pong.pck` from `Assets/`, and copies it beside the sample
executable; the publish target copies the same pack beside the published host. The JIT bake tool and
its packages never enter the sample's AOT publish graph.

## Native AOT

`Pong` is the M7 Native AOT gate. Publish and run the Windows host from the repository root:

```powershell
dotnet publish samples/Pong/Pong.csproj -c Release -r win-x64 -p:PublishAot=true
samples/Pong/bin/Release/net11.0/win-x64/publish/Pong.exe --frames 30
```

Publish the Linux host from the repository root inside WSL and run it on the WSLg display with the
conformant lavapipe ICD:

```bash
$HOME/.dotnet/dotnet publish samples/Pong/Pong.csproj -c Release -r linux-x64 -p:PublishAot=true
VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json \
DISPLAY=:0 XDG_RUNTIME_DIR=/mnt/wslg/runtime-dir \
  samples/Pong/bin/Release/net11.0/linux-x64/publish/Pong --frames 30
```

Both publishes finished with zero AOT/trim warnings. The Windows `win-x64` publish directory contains
`pong.pck` (22,648 bytes) beside `Pong.exe` (11,271,680 bytes) and no `Pong.Assets.dll`,
`RayBork.Bake.dll`, Tomlyn, StbImageSharp, BCnEncoder, shaderc native libraries, or glTF packages;
the published Windows host ran `--frames 30` to exit `0` on the hardware driver. The Linux
`linux-x64` publish directory contains `pong.pck` beside `Pong` and no bake tooling; the published
Linux host ran `--frames 30` to exit `0` on llvmpipe (lavapipe) with an SDR `B8G8R8A8_SRGB`
swapchain, FIFO presentation, 4x MSAA, and a 1280x720 window. WSLg's hardware Vulkan path (Mesa
dzn) is not a target.

## Limitations

The sample is a local two-player match with no AI opponent, no audio, and no gamepad rumble. It has
one fixed 16:9 design layout; the letterbox bars absorb any window aspect. It draws no texture atlas
or sprite batching beyond the engine's normal batch path.
