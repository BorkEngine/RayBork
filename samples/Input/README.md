# RayBork Input

`RayBork Input` is the permanent M9 sample and the milestone's Native AOT gate: a small,
user-facing text-field and input/window playground. It opens a `960x600` resizable window titled
`RayBork Input` and draws a screen-space HUD with a single-line text field, a window/monitor panel, a
gamepad panel, a bounded event log, and a render-statistics line. The text field accepts typed and
committed IME text, shows a blinking caret, copies and pastes text through the system clipboard, and
places the native text-input candidate area over the field.

The sample is a plain example, not a test fixture. It has no assertions, no test-only logging, no
hidden or test-only command-line options, and no `--text` or other injection flag. It uses no assets
executable, no asset pack, no bake step, and no `--assets` argument: the engine's built-in pack
supplies the primitive and text shaders, and the field text and HUD use the engine-owned
`EngineDefaults.DefaultFont` (Fira Sans) and `EngineDefaults.DefaultMonoFont` (JetBrains Mono). The
sample never disposes those engine defaults.

## Controls

| Input | Action |
| --- | --- |
| Type / IME | Append committed text to the field. Composition text is native and is not drawn by the sample. |
| `Ctrl+C` | Copy the whole field to the clipboard and log the action. |
| `Ctrl+X` | Copy the whole field to the clipboard, clear it, and log the action. |
| `Ctrl+V` | Append the clipboard text and log the action. The read waits one tick for the engine's queued clipboard snapshot. |
| Backspace | Remove the last UTF-16 text element; a surrogate pair is removed whole. OS key repeat is honored. |
| Delete | Clear the field. |
| `Alt+Enter` | Toggle fullscreen with `IWindow.SetFullscreen(!IWindow.IsFullscreen)`. `KeypadEnter` works too. |
| `B` | Toggle borderless with `IWindow.SetBorderless`. |
| `M` | Move the window to the next monitor in stable-index order. With one monitor, the event log says so. |
| `C` | Cycle the cursor mode `Normal` -> `Hidden` -> `Disabled` -> `Normal` and call `IWindow.SetCursor`. |
| `R` | Request `SetVibration(0.7f, 0.4f, 0.4s)` on every connected gamepad. |
| Gamepad South | Request a short rumble on that pad. |
| Escape | Request exit. |

`M` reads `IWindow.Monitors` in stable-index order rather than adding one to the current index, so a
hotplug that leaves gaps in the indices still selects a connected monitor. `C` cycles a local mode
because `IWindow` exposes no cursor-mode getter; the `Disabled` mode hides and locks the pointer and
makes `IInput.MouseDelta` relative, so press `C` again to restore the pointer.

The field has no separate focus state: every committed `IInput.TextInput` character is appended on
every tick. The single-letter playground controls are ordinary key presses, so pressing `B`, `C`,
`M`, or `R` while the window has keyboard focus also commits that letter to the field. Use
`Ctrl+C`/`Ctrl+V`, Backspace, and Delete for the clipboard and editing checks; the letter controls are
there to exercise the window and gamepad surface, not to be typed as text.

## Options

| Argument | Contract |
| --- | --- |
| `--frames N` | Run `N` ticks, then request exit. `0` runs until the window closes. Default `0`. |
| `--help` | Print usage and exit `0`. |

An unknown option prints the usage text and exits `2`. A non-integer or negative `--frames` value is
an invalid argument and exits `2`. An exception from the app prints and exits `1`. The sample requests
FIFO presentation, sets `WithMaxDeltaTime(0.25)`, sets no tick target, and mounts no assets pack. It
calls `IWindow.SetMinSize(640, 360)` in `Setup`.

## Layout

Each drawn tick records one orthographic HUD camera at `(0, 0, 1)` with its visible height equal to
`Window.RenderHeight`, `ClearMode.ColorAndDepth`, a dark clear color, a `0.01` near plane, and a `100`
far plane. Every draw anchors a screen-space plane with
`Camera.ScreenToWorldTransform(pixel, 1f)`, so one local unit is one render pixel, and the sample
draws only with `Camera.DrawQuad` and `Camera.DrawText`. It keeps no scene geometry, render target,
asset pack, or post-process stack, and creates and disposes exactly one camera per drawn tick. A tick
with a zero render extent (for example while the window is minimized) draws no HUD: it records no
camera and makes no `SetTextInputArea` call.

The layout is, from top to bottom:

1. A title block and a one-line description.
2. `TEXT FIELD`, the field itself with the blinking caret, and a hint line.
3. A window and monitor panel on the left and a gamepad panel on the right.
4. The event log strip.
5. A status line with the tick FPS, draw calls, instances, field length, focus, cursor-on-screen, and
   cursor mode.
6. A controls hint.

Every panel is a background quad plus a left accent bar and text; there are no rounded corners. All
colors are `Color.FromSrgbBytes` dark-UI values with high-contrast text.

The HUD is laid out in render pixels. Only `IWindow.SetTextInputArea` converts to logical window
coordinates, using `Window.Width / RenderWidth` and `Window.Height / RenderHeight` when the render
extents are positive; a tick with an unusable render extent draws no HUD, so the call is skipped with
it. Below a render height of `480` or a render width of `800`, the layout uses compact spacing and
hides the description. Every single-line row (the title, description, field label and hint, status,
and controls hint) is measured with `Font.Measure` and ends with `...` when the full text does not
fit, so a narrow window keeps the row inside the content width and still shows the full text where it
fits. Panel and event-log rows are clamped to whole rows, so a window at the `640x360` minimum never
draws a negative or inverted rectangle: the event log shows its newest rows (the buffer still keeps
ten), and a panel whose content no longer fits ends with `... +N more`.

## Text field

The field is one editable string. Each tick it appends `IInput.TextInput` in layout order; that is
the committed-IME path. Control characters and line breaks from a commit or a paste are replaced
with spaces, so the field stays single-line. Backspace removes one UTF-16 text element and cannot
split a surrogate pair, and Delete clears the field.

The caret blinks from `EngineContext.Time`: `0.5` seconds on and `0.5` seconds off. The sample uses
`Font.Measure` to fit the visible tail of long text against the field width and to place the caret
at the measured end. When the full text is too wide, the visible text starts with an ellipsis and
always keeps its tail, so a 200-character field keeps a stable field rectangle and caret position.

Ctrl+C calls `IWindow.SetClipboardText`, Ctrl+X does the same and then clears the field, and Ctrl+V
calls `IWindow.GetClipboardText` once to queue the main-thread read, waits one tick so the cached
snapshot can refresh, then calls it again, appends the text, and logs it. Nothing blocks, sleeps, or
polls in a loop.

Every drawn tick also calls `IWindow.SetTextInputArea` with the field rectangle in logical window
coordinates and the caret offset in window units, so native IME candidates follow the field. The
sample draws no composition preview or candidate list of its own.

### Faces and glyph coverage

The field text uses `EngineDefaults.DefaultFont` (Fira Sans Regular), and the title, panels, status,
and event log use `EngineDefaults.DefaultMonoFont` (JetBrains Mono, default instance Regular). Both
built-in faces cover Latin, Cyrillic, and Greek, and the engine has no font fallback: a rune the
face's cmap does not map draws as that face's `.notdef` box (see
[Text rendering](../../docs/cheatsheet.md#font)). The sample ships no font of its own and cannot
substitute one, so a committed CJK string is stored, copied, and counted but draws as `.notdef`
boxes. A non-BMP single-codepoint emoji such as U+1F600 is one surrogate pair, so it is stored as two
UTF-16 code units and decodes as one rune, and the field and the log each draw one `.notdef` box; a
BMP emoji is one code unit, and a multi-rune emoji sequence has more code units and draws more boxes.
The status line's `chars` value is the field length in UTF-16 code units, so an unmapped commit is
still verifiable through `chars` and the event log's `text "..."` line.

## Window and monitor panel

The panel reports `Window.State`, `IsFullscreen`, `IsMinimized`, `IsFocused`, `IsCursorOnScreen`,
`DpiScale`, `Width`/`Height`, `RenderWidth`/`RenderHeight`, `CurrentMonitor.Index`/`Name`, and the
sample's local cursor mode and borderless state. It then lists every `IWindow.Monitors` entry with its
stable index, name, bounds, work area, DPI scale, and refresh rate, marking the current monitor with
`*`.

A monitor hotplug may change the list between ticks; the panel simply draws the latest snapshot. The
event log records focus and minimize changes, DPI changes, render-size changes, monitor switches, and
fullscreen and borderless requests. The window setters are queued by the engine, so an event is
logged when the request is made while the panel may lag it by one event-pump cycle.

## Gamepad panel

The panel lists `IInput.Gamepads` with the per-tick `Index`, the stable `InstanceId`, the device name,
the raw left and right stick values, and the left and right trigger values. With no pad connected it
shows the no-device state.

The sample tracks the set of connected `InstanceId` values across ticks and logs `gamepad added` and
`gamepad removed` transitions. Rumble is requested on `R` and on a South-button press and logged as
best effort; an unsupported device ignores the request and the sample neither waits for nor asserts
any effect.

## Event log and status

The event log is bounded to the last ten user-visible events. It records text commits, clipboard
actions, field clears, cursor-mode changes, monitor switches, fullscreen and borderless requests,
focus and minimize changes, DPI and render-size changes, gamepad added/removed transitions, and
rumble requests. Older events are dropped, so the log never grows without bound.

The status line shows `ctx.Fps` (the tick rate), `ctx.Stats.DrawCalls`, `ctx.Stats.Instances`, the
field length in UTF-16 code units, the focus and cursor-on-screen state, and the local cursor mode.

## Build and run

The sample is a self-contained solution, `samples/Input/Input.slnx`, containing `Input.csproj`. It is
not part of `src/RayBork/RayBork.slnx`. The project targets `net11.0`, is an `Exe`, enables Native
AOT, and references `RayBork.Engine`, `PolyType`, `SDL3-CS`, and `SDL3-CS.Native`. It has no assets
executable, no pack, and no publish settings beyond `PublishAot`.

```powershell
dotnet build samples/Input/Input.slnx -c Debug
dotnet build samples/Input/Input.slnx -c Release
dotnet run --project samples/Input/Input.csproj -c Debug -- --help
dotnet run --project samples/Input/Input.csproj -c Debug -- --frames 30
```

The `--frames 30` run opens a real window and exits `0` after thirty ticks only on a machine with a
working video driver. On a headless host it fails while creating the window or the Vulkan device;
that failure is environmental, not a reason to add a flag that skips the sample.

## Native AOT

Publish and run the Windows host from the repository root:

```powershell
dotnet publish samples/Input/Input.csproj -c Release -r win-x64 -p:PublishAot=true
samples/Input/bin/Release/net11.0/win-x64/publish/Input.exe --frames 30
```

Publish the Linux host on a Linux desktop machine and run it with its hardware driver:

```bash
dotnet publish samples/Input/Input.csproj -c Release -r linux-x64 -p:PublishAot=true
samples/Input/bin/Release/net11.0/linux-x64/publish/Input --frames 30
```

Inside WSL, publish from the WSL clone (the repository copy under `$HOME`, for example
`$HOME/raybork`, never `/mnt/d`) with the WSL .NET, then run it on the WSLg display with the
conformant lavapipe ICD:

```bash
cd "$HOME/raybork"
$HOME/.dotnet/dotnet publish samples/Input/Input.csproj -c Release -r linux-x64 -p:PublishAot=true
VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json \
DISPLAY=:0 XDG_RUNTIME_DIR=/mnt/wslg/runtime-dir \
  samples/Input/bin/Release/net11.0/linux-x64/publish/Input --frames 30
```

WSLg's hardware Vulkan path (Mesa dzn) is not a target.

Both publishes must finish with zero AOT and trim warnings. The publish directory must not contain
`RayBork.Bake.dll`, Tomlyn, StbImageSharp, BCnEncoder, the shaderc native libraries, or the glTF
packages: the sample has no bake tooling and no external pack. The milestone lead runs this publish
gate; the sample's build and `--frames` run are the local checks.

## Manual verification

These are steps for you to run on a machine with a display. The automated `--frames 30` run proves
only that the sample starts, runs its tick loop, and exits cleanly; it does not verify anything drawn
on screen, so use these steps for the visual checks. Run the Windows checks on the hardware driver
and the WSL checks under WSLg with the lavapipe ICD and the environment above; the Monitors and DPI
section states which host covers each monitor check.

### Committed IME text and the text-input area

The built-in faces cover Latin, Cyrillic, and Greek only, and there is no fallback (see "Faces and
glyph coverage"). A keyboard layout in that coverage proves the visible-glyph commit path; the
native candidate-window placement check needs a real IME, because a keyboard layout has no candidate
window.

Windows:

1. Enable a Latin, Cyrillic, or Greek keyboard layout (for example Russian or Greek), run the
   sample, and type a word. The committed characters appear at the end of the field, the caret
   moves past them, and the event log records the `text "..."` line.
2. Add and switch to a real IME such as Microsoft Japanese IME, then start composition. The native
   candidate window should follow the field rectangle, not the top-left corner of the window.
3. Commit the composition. The built-in faces do not cover Japanese, so the committed text draws as
   `.notdef` boxes in the field and the log; verify the commit through the status line's `chars`
   count, which grows by the committed UTF-16 length, and the event log's `text "..."` line.
4. Resize the window or move it to another monitor and repeat the composition. The candidate window
   should still follow the field rectangle after the resize.

WSL/lavapipe:

1. Start the sample under WSLg with lavapipe. The WSLg session receives the host keyboard, but it may
   expose only a keyboard layout and no IME bridge.
2. With a Latin, Cyrillic, or Greek layout, type a word and confirm the visible glyphs in the field,
   the growing `chars` count, and the `text "..."` event-log line.
3. If the session exposes a real IME to the WSLg window, start composition and confirm the candidate
   window follows the field. A committed CJK string draws as `.notdef` boxes but is verifiable
   through `chars` and the event log, exactly as on Windows.
4. If the session has no IME bridge, typed ASCII still commits through `TextInput`; that is a
   session limitation rather than a sample path, because the sample draws no composition text of its
   own.

### Clipboard and caret

1. Type `hello`, then wait one second. The caret blinks on and off at 0.5-second intervals.
2. Press `Ctrl+C`; the event log records the copy. Open another application and paste to confirm the
   clipboard text.
3. Press `Ctrl+X`; the field empties and the log records the cut.
4. Press `Ctrl+V`; the text is appended on the next tick and logged. Paste a two-line string such as
   `one\r\ntwo`: the field becomes `one two`, because the CRLF pair folds to one space, and it stays
   one line. A lone `\n` or `\r` also becomes one space.
5. Paste a non-BMP single-codepoint emoji such as U+1F600. It is one surrogate pair, so the status
   `chars` count grows by two and the field draws one `.notdef` box, because the pair decodes as one
   rune and the built-in face maps no emoji glyph. A BMP emoji is one code unit and draws one box; a
   multi-rune emoji sequence is longer and draws more boxes, so the +2 and one-box expectation
   applies to that concrete example. Press Backspace once: the box disappears and `chars` drops by
   two, so the pair was removed as one text element. Press Delete; the field clears.
6. Paste or type a 200-character field; the field stays the same size, shows a leading ellipsis once
   the tail no longer fits, and keeps the caret at the end.

### Fullscreen and borderless

1. Press `Alt+Enter`. The window enters fullscreen, the panel reports `fullscreen yes`, and the event
   log records `fullscreen requested on`.
2. Press `Alt+Enter` again. The window returns to its previous size and position, the panel reports
   `fullscreen no`, and the log records `fullscreen requested off`.
3. Press `B`. The border disappears and the panel reports `borderless yes`; the event log records
   `borderless requested on`. The sample draws no custom drag or resize region, and a borderless
   window may have no native affordance for either, so do not try to resize or move it. Press `B`
   again to restore the border and confirm the panel reports `borderless no`.
4. Toggle fullscreen while borderless and borderless while fullscreen. Each panel line and log entry
   reflects only its own toggle: `Alt+Enter` changes the `fullscreen` line, and `B` changes the
   local `borderless` line, without changing the other.

### Monitors and DPI

Monitor hotplug, per-monitor bounds and work area, and per-monitor DPI changes are verified on the
Windows hardware driver: those checks need a multi-monitor, changeable-scale desktop host. WSLg
normally exposes one virtual display, so it cannot exercise multi-monitor behavior; do not report a
WSLg run as multi-monitor coverage. Under WSLg, verify only the one-monitor path: the panel lists the
one display with its bounds and work area, and the panel's `render` and `dpi` values and the event log
update if the session can change the WSLg display scale. The HUD and, when the session exposes an IME,
the native candidate area must remain valid for that display.

Windows hardware driver:

1. On a machine with two or more monitors, press `M` repeatedly. The window moves to the next
   monitor in stable-index order, the panel updates `current monitor`, and the monitor list marks the
   new current monitor. With a single monitor the event log says `monitor switch: only one monitor`.
2. Disconnect a secondary monitor while the sample runs. The monitor list shrinks at the next tick
   and the panel keeps drawing a valid layout; reconnect it and confirm its stable index returns.
3. Put the monitors at different display scales, or change the scale of the current monitor. The
   panel's `dpi` and `render` values change, the event log records the changes, and the HUD stays
   aligned with the window. The native IME candidate area should still follow the field.

Any host, including WSLg:

1. Resize the window down to its `640x360` minimum. No panel or row may be drawn inside out; the
   event log keeps its newest rows, panels may end with `... +N more`, and the status and controls
   rows stay inside the window, with `...` where the full text no longer fits (see "Layout").

### Focus loss and the 30 FPS cap

1. Note the `fps` value in the status line while the sample has focus.
2. Click another window or press `Alt+Tab` away. The event log records `window focus lost`, and the
   tick rate settles near 30 FPS in the status line.
3. Return focus. The log records `window focus gained` and the tick rate returns to the display-paced
   rate (FIFO presentation).
4. Minimize and restore the window; the log records `window minimized` and `window restored` and the
   same cap applies while minimized.

### Gamepad hotplug and rumble

1. Connect a gamepad while the sample runs. The gamepad panel lists its per-tick index, stable
   `InstanceId`, name, sticks, and triggers, and the event log records `gamepad added` with that
   instance ID.
2. Move the sticks and squeeze the triggers; the displayed values follow them with no deadzone.
3. Press `R`; the log records the best-effort request on every connected pad. Press the South button;
   the log records the short rumble request. Some pads ignore rumble or have no vibration motors; the
   sample must not report a failure and must keep running.
4. Disconnect the pad. The log records `gamepad removed` with the same stable instance ID and the
   panel returns to the no-device state. Reconnect it and confirm the panel and log update again.
5. On WSL, pass the gamepad into the distribution (for example with `usbipd`) before starting the
   sample. If no pad is passed through, the panel shows the no-device state.

## What the sample omits

- IME composition preview and candidate rendering. Only committed text is delivered through
  `TextInput`; candidate placement is the platform's native window.
- Font fallback and non-Latin glyph coverage. The built-in faces cover Latin, Cyrillic, and Greek,
  so CJK, emoji, and other unmapped runes commit, copy, and count normally but draw as `.notdef`
  boxes; the sample ships no font of its own and cannot substitute one.
- Text selection, mouse caret placement, arrow-key navigation, and a separate field focus state.
- Touch, window icon, window opacity, and drag-and-drop: all are post-1.0 platform work.
- Clipboard images, multiple windows, and accessibility.
- Screenshots and capture tooling. The sample contains no golden images or capture hooks.
- Test-only assertions and logging, and hidden or test-only command-line flags: the only arguments
  are `--frames` and `--help`.

## Related documentation

- [Input](../../docs/cheatsheet.md#input)
- [Window](../../docs/cheatsheet.md#window)
- [Text rendering](../../docs/cheatsheet.md#font)
- [Camera](../../docs/cheatsheet.md#camera)
- [Native AOT](../../docs/architecture.md#projects)
