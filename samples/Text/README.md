# Text

`Text` was the M6 sample and Native AOT gate: a world-space text scene drawn in one perspective pass
with the built-in Fira Sans and JetBrains Mono faces and a custom baked Lora face. It shows the same
paragraph at three sizes, three alignments, both wrapping modes, and two rotations; Latin and Cyrillic
paragraphs in both proportional faces; a camera-facing billboard; a line that appends one new Greek
glyph every 30 ticks; and a live status line in the mono face. It loads the custom face from its own
baked `text.pck`, so it also exercises the font asset path end to end.

The sample contains no test-only logic: it opens a window, draws the scene, and exits cleanly. Build and
run it from the repository root:

```powershell
dotnet build samples/Text/Text.slnx -c Debug
dotnet run --project samples/Text/Text.csproj -c Debug -- --frames 30
```

## Scene

The scene is one vertical wall of text at the world origin, viewed by an orbiting perspective camera.
Text is world-space geometry: `Camera.DrawText` lays it out in the transform's local XY plane with +X
to the right, +Y up, and the readable face toward local +Z. The starting camera sits at `(0, 0.9, 9.6)`
looking at `(0, 0.9, 0)`, so it reads the wall straight on; the default 1280x720 window frames the full
wall. Dragging and the wheel orbit and zoom that camera, and the billboard keeps facing it from its
own point in front of the wall.

Every text call records through the same built-in MSDF program, quad, and render state, so the whole
scene batches into one text draw call regardless of face, size, or string. `ctx.Stats` reports that
draw, the submitted glyph instances, and the render CPU time in the status line; the presented rate
comes from `ctx.Stats.PresentedFrames` and the tick rate from `ctx.Fps`.

## Layout demonstration

The same paragraph is drawn three times in the built-in face, each copy with a different size,
alignment, wrapping mode, and rotation:

| Copy | Size | Alignment | Wrapping | Rotation | maxWidth |
| --- | --- | --- | --- | --- | --- |
| Large | `0.32` em | `Left` | `Word` | none | `4.2` world units |
| Medium | `0.16` em | `Center` | `Word` | `-5` degrees around Z | `4.2` world units |
| Small | `0.12` em | `Right` | `Character` | `10` degrees around Y | `4.2` world units |

`size` is the em height in world units, while `maxWidth` is an absolute world-space box width at that
size, so one box holds a different number of lines at each size. The large copy contains
`LongUnbreakableIdentifierThatOverflowsItsLine`, a single word wider than the box: `Word` wrapping
leaves it whole and lets it overflow its line, and the paragraph's other lines stay inside the box.
The small copy uses `Character` wrapping, which breaks between any two glyphs, so its lines split
mid-word. The medium and small copies rotate the text plane itself, which shows that layout happens in
the transform's local space. At 1280x720 the `0.12` em copy is readable, but it is the smallest, so it
is best read up close: the mouse wheel zooms the orbit distance down to `2.5` world units.

Latin and Cyrillic paragraphs are drawn in both faces: `Fira Sans, Latin`, `Lora, Latin`,
`Fira Sans, Cyrillic`, and `Lora, Cyrillic`. Each heading is drawn in the face it names, and each
paragraph wraps inside a `3.9` world-unit box. Both faces map the Latin and Cyrillic codepoints
directly; no fallback font is involved.

The billboard label is drawn with `Camera.DrawTextBillboard` at `(2.3, -0.7, 1.5)`. It centers the
union of the layout box and every glyph quad on that point and uses the camera's right and up axes as
its plane, so it turns to face the orbiting camera instead of staying on the wall.

## Lazy glyphs

The titles and paragraphs draw Latin and Cyrillic glyphs. The Greek line starts with only its prefix
and gains one lowercase Greek letter every 30 ticks, beginning with the first tick. Greek is not drawn
anywhere else in the scene, so each new letter is a glyph the font has not generated yet. The first
draw that includes it skips that glyph while the `RayBork.Font` thread generates and uploads its MSDF
texture; the letter appears on a later frame without stalling the tick. The built-in Fira Sans face maps
the whole Greek alphabet used by the line.

## Status line

The built-in mono face draws the presented frames per second, the tick rate, the render CPU time, and
the last completed frame's `DrawCalls` and `Instances` from `ctx.Stats` at the top-right of the wall.
The monospaced face keeps the changing counters from shifting the text around them. The presented rate
is a one-second delta of `ctx.Stats.PresentedFrames`, which counts successful
presentations since startup; the tick rate is `ctx.Fps` and is labelled `tick`. The render time is
`ctx.Stats.CpuFrameMilliseconds`, which measures only the render thread's planning, recording, and
submission and excludes the fence, acquire, and present waits. The status block is ordinary
world-space text on the same wall, not a screen-space HUD.

## Assets

The checked-in source is `Text.Assets/Assets/fonts/lora.ttf`; the sample build bakes it into `text.pck`.
No manual bake step is required.

| Source | Path in the pack | Notes |
| --- | --- | --- |
| `Assets/fonts/lora.ttf` | `/fonts/lora` | Lora Regular 3.008, copied verbatim. One font descriptor and the whole face as the sibling `/fonts/lora-face` raw payload. No sidecar, so the MSDF defaults `32`/`4`/`2` apply: a 32-pixel padded texture, a 4-pixel distance range, and a 2-pixel border. |

The built-in Fira Sans and JetBrains Mono faces are not in this pack. The engine preloads them at
`/fonts/fira-sans` and `/fonts/jetbrains-mono` and exposes them as `ctx.Defaults.DefaultFont` and
`ctx.Defaults.DefaultMonoFont`; the sample uses those objects directly and never disposes them. The
Lora load is the sample's own reference and is released in `Shutdown`.

`Load<Font>` is CPU-only: it parses the face and queues no GPU upload. The engine preloads both
built-in faces before `Setup`, so they are already available; `Setup` loads Lora with `Load<Font>`,
which parses the Lora face and queues no upload. Every glyph texture is generated on demand on the
engine's font thread, so no load generates a single glyph texture.

The pack is generated output under `bin/` and `obj/` and is gitignored. The Lora font is sample-only and
is **not** covered by RayBork's MIT license; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Options

| Argument | Contract |
| --- | --- |
| `--frames N` | Run `N` ticks, then request exit. `0` runs until the window closes. Default `0`. |
| `--help` | Print usage and exit `0`. |

The sample uses a 1280x720 resizable window, a `0.25` second delta clamp, and the engine's default
renderer settings, and it sets no tick target. It requests FIFO presentation, so the display refresh
paces the tick thread. It loads `text.pck` from beside the executable, so it has no `--assets`
argument. A missing pack prints an error and exits `1`.

## Controls

| Input | Action |
| --- | --- |
| Left mouse drag | Orbit the camera around the text wall. Pitch is clamped to ±75 degrees. |
| Mouse wheel | Zoom the orbit distance, clamped to 2.5–40. |
| Escape | Request exit. |

The camera uses a 45 degree vertical field of view, a `0.05` near plane, and an infinite far plane. The
background is `Color.Gray2` (sRGB `#333333`). The wall is about 13.5 by 6 world units and is 9.6 world
units from the starting camera.

## Build and run

The sample is a self-contained solution, `samples/Text/Text.slnx`, containing `Text.csproj` and
`Text.Assets/Text.Assets.csproj`. It is not part of `src/RayBork/RayBork.slnx`. The build bakes
`text.pck` and copies it beside the sample executable; `dotnet run` loads that adjacent pack
automatically. The JIT bake tool and its packages never enter the sample's AOT publish graph.

## Native AOT

`Text` was the M6 Native AOT gate. Publish and run the Windows host from the repository root:

```powershell
dotnet publish samples/Text/Text.csproj -c Release -r win-x64 -p:PublishAot=true
samples/Text/bin/Release/net11.0/win-x64/publish/Text.exe --frames 30
```

Publish the Linux host from the repository root inside WSL and run it on the WSLg display with the
conformant lavapipe ICD:

```bash
$HOME/.dotnet/dotnet publish samples/Text/Text.csproj -c Release -r linux-x64 -p:PublishAot=true
VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json \
DISPLAY=:0 XDG_RUNTIME_DIR=/mnt/wslg/runtime-dir \
  samples/Text/bin/Release/net11.0/linux-x64/publish/Text --frames 30
```

Both publishes must finish with zero AOT/trim warnings. The publish directory must contain `text.pck`
beside the host and must not contain `Text.Assets.dll`, `RayBork.Bake.dll`, Tomlyn, StbImageSharp,
BCnEncoder, the shaderc native libraries, or the glTF packages. The Linux host runs on the
conformant lavapipe software driver; WSLg's hardware Vulkan path (Mesa dzn) is not a target.

## M6 limitations

- **Text is world space.** `DrawText` and `DrawTextBillboard` place geometry in the scene. M7 adds
  the screen-space authoring pattern through `Camera.ScreenToWorldTransform` and the same `Transform`
  overload; there is no `DrawText(Vector2)` screen overload and no `DrawTexture`. This sample's
  status line stays on the wall because the scene is a text-layout demonstration; Castle,
  PrimitiveGallery, and Pong anchor their HUDs with the screen transform.
- **Tick recording cost.** The scene records 1,196 glyph instances in one draw call, but the
  per-glyph tick work (layout, metrics lookup, cache acquire, and frame retention) measures about
  10-13 ms per tick in Debug (~80 ticks/s) and 2.7-3 ms per tick in Release (~300 ticks/s) on the
  reference machine. The render thread's plan, record, and submit is about 1-2 ms, and the GPU is
  not the bottleneck. This is a measured observation, not a target; optimizing the text recording
  path is deferred to the M10 optimization milestone.
- **Variable fonts load their default instance.** The bake packs the face file, and the runtime renders
  the face's default instance; named variation axes are not exposed.
- **No shaping.** Ligatures, contextual shaping (Arabic, Indic), bidi ordering, font fallback, color
  fonts, and emoji are post-1.0. Latin, Cyrillic, and Greek map codepoint to glyph directly, which is
  what this sample shows.
- **CFF2 is unsupported.** The runtime resolves TrueType `glyf` and CFF 1.0 outlines; a CFF2 face fails
  the bake with `RB4010`. Lora is a TrueType face.
- **No hinting.** MSDF antialiasing uses screen-space derivatives, so a face is not grid-fitted at small
  sizes.
- **No atlas.** One glyph is one padded texture; a text draw batches bindlessly rather than packing a
  glyph atlas.
- **Fonts are world-space assets.** A font is loaded and drawn like any other asset, which is why this
  sample is a text scene rather than a HUD.

## Notices and license

The Lora font is sample-only and is not covered by RayBork's MIT license. See
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for its copyright and the full SIL Open Font License
1.1 text.
