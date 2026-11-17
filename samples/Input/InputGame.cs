using System.Globalization;
using System.Numerics;
using RayBork;

namespace Input;

/// <summary>
/// The M9 input and window sample: a single-line text field with a blinking caret, a clipboard and
/// cursor playground, window/monitor/DPI telemetry, an event log, and gamepad hotplug and rumble
/// feedback.
/// </summary>
/// <remarks>
/// <para>
/// Every tick records one orthographic HUD camera whose visible height is the current render
/// height, so a screen-space plane anchored with <see cref="Camera.ScreenToWorldTransform"/> has a
/// scale of exactly one render pixel per local unit. Every draw is a <see cref="Camera.DrawQuad"/>
/// or a <see cref="Camera.DrawText"/> on that plane; the sample has no scene geometry, no asset
/// pack, no render target, and no post-process stack.
/// </para>
/// <para>
/// The text field is the sample's centerpiece. <see cref="IInput.TextInput"/> appends committed IME
/// text in layout order, and <see cref="IWindow.SetTextInputArea"/> places the native candidate
/// window over the field every tick in window coordinates. Clipboard reads are asynchronous in the
/// engine, so a paste records one tick before it reads and appends the refreshed cache. The event
/// log keeps a bounded view of what the user just did.
/// </para>
/// </remarks>
internal sealed class InputGame : IGame
{
    private const int MIN_WINDOW_WIDTH = 640;
    private const int MIN_WINDOW_HEIGHT = 360;

    private const float PLANE_DISTANCE = 1f;
    private const float CAMERA_NEAR = 0.01f;
    private const float CAMERA_FAR = 100f;

    private const float CARET_BLINK_SECONDS = 0.5f;
    private const float CARET_WIDTH = 2f;
    private const float FIELD_TEXT_INSET = 12f;
    private const float ACCENT_WIDTH = 4f;
    private const float PANEL_TEXT_INSET = 10f;

    private const float RUMBLE_LOW = 0.7f;
    private const float RUMBLE_HIGH = 0.4f;
    private const double RUMBLE_SECONDS = 0.4;
    private const float SOUTH_RUMBLE_LOW = 0.6f;
    private const float SOUTH_RUMBLE_HIGH = 0.9f;
    private const double SOUTH_RUMBLE_SECONDS = 0.15;

    private const int PREVIEW_LENGTH = 24;
    private const int MONITOR_NAME_LENGTH = 24;
    private const int GAMEPAD_NAME_LENGTH = 30;

    private const string WINDOW_PANEL_TITLE = "WINDOW & MONITORS";
    private const string GAMEPAD_PANEL_TITLE = "GAMEPADS";
    private const string EVENT_LOG_TITLE = "EVENT LOG (last 10)";
    private const string FIELD_LABEL = "TEXT FIELD";
    private const string FIELD_HINT = "type or IME commit | Ctrl+C/X/V | Backspace | Delete clears";
    private const string HEADER_SUBTITLE =
        "text field, clipboard, cursor, fullscreen, monitors, DPI, focus, and gamepads";
    private const string CONTROLS_TEXT =
        "Alt+Enter fullscreen | B borderless | M monitor | C cursor | R rumble | Ctrl+C/X/V clipboard | Esc exit";

    private static readonly Color _clearColor = Color.FromSrgbBytes(13, 16, 22);
    private static readonly Color _panelColor = Color.FromSrgbBytes(27, 32, 41);
    private static readonly Color _fieldColor = Color.FromSrgbBytes(19, 24, 32);
    private static readonly Color _textColor = Color.FromSrgbBytes(234, 239, 247);
    private static readonly Color _panelTextColor = Color.FromSrgbBytes(206, 215, 228);
    private static readonly Color _mutedColor = Color.FromSrgbBytes(148, 160, 178);
    private static readonly Color _windowAccent = Color.FromSrgbBytes(255, 186, 91);
    private static readonly Color _gamepadAccent = Color.FromSrgbBytes(89, 214, 176);
    private static readonly Color _fieldAccent = Color.FromSrgbBytes(110, 170, 255);
    private static readonly Color _logAccent = Color.FromSrgbBytes(255, 128, 128);
    private static readonly Color _caretColor = Color.FromSrgbBytes(255, 236, 170);

    private readonly int _frameLimit;
    private readonly TextField _field = new();
    private readonly EventLog _events = new();
    private readonly List<string> _windowLines = [];
    private readonly List<string> _gamepadLines = [];
    private readonly List<int> _gamepadIds = [];
    private readonly List<int> _previousGamepadIds = [];

    private CursorMode _cursorMode = CursorMode.Normal;
    private bool _borderless;
    private bool _pastePending;
    private bool _hasWindowBaseline;
    private bool _lastFocused;
    private bool _lastMinimized;
    private float _lastDpiScale;
    private int _lastRenderWidth;
    private int _lastRenderHeight;
    private int _frameCount;

    /// <summary>
    /// Creates the sample.
    /// </summary>
    /// <param name="frameLimit">
    /// The number of ticks to run, or zero to run until the window closes.
    /// </param>
    internal InputGame(int frameLimit)
    {
        _frameLimit = frameLimit;
    }

    /// <inheritdoc/>
    public Task Setup(EngineContext context)
    {
        // The layout is designed for 960x600 and stays valid at the minimum; the engine queues this
        // request to the main thread before the first tick.
        context.Window.SetMinSize(MIN_WINDOW_WIDTH, MIN_WINDOW_HEIGHT);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void Tick(EngineContext context)
    {
        IInput input = context.Input;
        IWindow window = context.Window;

        HandleWindowInput(context, input, window);
        HandleTextInput(input, window);

        TrackWindowChanges(window);
        UpdateGamepads(input);
        BuildWindowLines(window);

        int renderWidth = window.RenderWidth;
        int renderHeight = window.RenderHeight;
        if (renderWidth > 0 && renderHeight > 0)
        {
            Draw(context, renderWidth, renderHeight);
        }

        _frameCount++;
        if (_frameLimit > 0 && _frameCount >= _frameLimit)
        {
            context.RequestExit();
        }
    }

    /// <inheritdoc/>
    public Task Shutdown()
    {
        // The sample owns no GPU assets: both fonts and the built-in pack belong to the engine.
        _pastePending = false;
        return Task.CompletedTask;
    }

    private void HandleWindowInput(EngineContext context, IInput input, IWindow window)
    {
        if (input.IsKeyPressed(Key.Escape))
        {
            context.RequestExit();
        }

        bool control = IsControlDown(input);
        bool alt = IsAltDown(input);

        if (alt && (input.IsKeyPressed(Key.Enter) || input.IsKeyPressed(Key.KeypadEnter)))
        {
            bool fullscreen = !window.IsFullscreen;
            window.SetFullscreen(fullscreen);
            _events.Add(fullscreen ? "fullscreen requested on" : "fullscreen requested off");
        }

        if (control || alt)
        {
            return;
        }

        if (input.IsKeyPressed(Key.B))
        {
            _borderless = !_borderless;
            window.SetBorderless(_borderless);
            _events.Add(_borderless ? "borderless requested on" : "borderless requested off");
        }

        if (input.IsKeyPressed(Key.M))
        {
            SelectNextMonitor(window);
        }

        if (input.IsKeyPressed(Key.C))
        {
            CycleCursor(window);
        }

        if (input.IsKeyPressed(Key.R))
        {
            RumbleAll(input);
        }
    }

    private void HandleTextInput(IInput input, IWindow window)
    {
        // A paste that was requested last tick waits one pump so the queued main-thread clipboard
        // read has refreshed the cache. The second read advances the cache for the next paste too.
        if (_pastePending)
        {
            _pastePending = false;
            string clipboard = window.GetClipboardText();
            string appended = _field.Append(clipboard);
            _events.Add(
                appended.Length == 0
                    ? "clipboard paste (empty)"
                    : $"clipboard paste {Quote(appended)}"
            );
        }

        string committed = _field.Append(input.TextInput);
        if (committed.Length > 0)
        {
            _events.Add($"text {Quote(committed)}");
        }

        if (input.IsKeyPressed(Key.Backspace) || input.IsKeyPressedRepeat(Key.Backspace))
        {
            _field.RemoveLastElement();
        }

        if (input.IsKeyPressed(Key.Delete))
        {
            _field.Clear();
            _events.Add("field cleared");
        }

        bool control = IsControlDown(input);
        if (control && input.IsKeyPressed(Key.C))
        {
            CopyField(window);
        }

        if (control && input.IsKeyPressed(Key.X))
        {
            CutField(window);
        }

        if (control && input.IsKeyPressed(Key.V) && !_pastePending)
        {
            // Queue the read now; the next tick appends the refreshed cache.
            window.GetClipboardText();
            _pastePending = true;
        }
    }

    private void CopyField(IWindow window)
    {
        window.SetClipboardText(_field.Text);
        _events.Add(
            _field.Length == 0 ? "clipboard copy (empty)" : $"clipboard copy {Quote(_field.Text)}"
        );
    }

    private void CutField(IWindow window)
    {
        string text = _field.Text;
        window.SetClipboardText(text);
        _field.Clear();
        _events.Add(text.Length == 0 ? "clipboard cut (empty)" : $"clipboard cut {Quote(text)}");
    }

    private void SelectNextMonitor(IWindow window)
    {
        IReadOnlyList<RayBork.Monitor> monitors = window.Monitors;
        if (monitors.Count <= 1)
        {
            _events.Add("monitor switch: only one monitor");
            return;
        }

        // Stable indices can have gaps, so the next monitor is the next entry in the index-ordered
        // snapshot, not index + 1.
        int currentIndex = window.CurrentMonitor.Index;
        int currentPosition = -1;
        for (int index = 0; index < monitors.Count; index++)
        {
            if (monitors[index].Index == currentIndex)
            {
                currentPosition = index;
                break;
            }
        }

        int nextPosition = currentPosition < 0 ? 0 : (currentPosition + 1) % monitors.Count;
        RayBork.Monitor next = monitors[nextPosition];
        window.SetMonitor(next.Index);
        _events.Add(
            FormattableString.Invariant($"monitor switch #{currentIndex} -> #{next.Index}")
        );
    }

    private void CycleCursor(IWindow window)
    {
        _cursorMode = _cursorMode switch
        {
            CursorMode.Normal => CursorMode.Hidden,
            CursorMode.Hidden => CursorMode.Disabled,
            _ => CursorMode.Normal,
        };
        window.SetCursor(_cursorMode);
        _events.Add($"cursor mode {_cursorMode}");
    }

    private void RumbleAll(IInput input)
    {
        IReadOnlyList<IGamepad> pads = input.Gamepads;
        if (pads.Count == 0)
        {
            _events.Add("rumble: no gamepad connected");
            return;
        }

        foreach (IGamepad pad in pads)
        {
            pad.SetVibration(RUMBLE_LOW, RUMBLE_HIGH, RUMBLE_SECONDS);
        }

        // Rumble is best effort: the platform may ignore it and the engine logs at Debug.
        _events.Add(FormattableString.Invariant($"rumble all {pads.Count} pad(s) (best effort)"));
    }

    private void TrackWindowChanges(IWindow window)
    {
        bool focused = window.IsFocused;
        bool minimized = window.IsMinimized;
        float dpiScale = window.DpiScale;
        int renderWidth = window.RenderWidth;
        int renderHeight = window.RenderHeight;

        if (!_hasWindowBaseline)
        {
            _hasWindowBaseline = true;
        }
        else
        {
            if (focused != _lastFocused)
            {
                _events.Add(focused ? "window focus gained" : "window focus lost");
            }

            if (minimized != _lastMinimized)
            {
                _events.Add(minimized ? "window minimized" : "window restored");
            }

            if (dpiScale != _lastDpiScale)
            {
                _events.Add(
                    FormattableString.Invariant($"dpi {_lastDpiScale:0.00} -> {dpiScale:0.00}")
                );
            }

            if (renderWidth != _lastRenderWidth || renderHeight != _lastRenderHeight)
            {
                _events.Add(
                    FormattableString.Invariant(
                        $"render {_lastRenderWidth}x{_lastRenderHeight} -> {renderWidth}x{renderHeight}"
                    )
                );
            }
        }

        _lastFocused = focused;
        _lastMinimized = minimized;
        _lastDpiScale = dpiScale;
        _lastRenderWidth = renderWidth;
        _lastRenderHeight = renderHeight;
    }

    private void UpdateGamepads(IInput input)
    {
        _gamepadLines.Clear();
        _gamepadIds.Clear();

        foreach (IGamepad pad in input.Gamepads)
        {
            _gamepadIds.Add(pad.InstanceId);
            if (!_previousGamepadIds.Contains(pad.InstanceId))
            {
                _events.Add(
                    $"gamepad added {pad.InstanceId} \"{Shorten(pad.Name, GAMEPAD_NAME_LENGTH)}\""
                );
            }

            float leftX = pad.GetAxis(GamepadAxis.LeftX);
            float leftY = pad.GetAxis(GamepadAxis.LeftY);
            float rightX = pad.GetAxis(GamepadAxis.RightX);
            float rightY = pad.GetAxis(GamepadAxis.RightY);
            float leftTrigger = pad.GetTrigger(GamepadTrigger.Left);
            float rightTrigger = pad.GetTrigger(GamepadTrigger.Right);

            _gamepadLines.Add(FormattableString.Invariant($"#{pad.Index}  id {pad.InstanceId}"));
            _gamepadLines.Add($"  {Shorten(pad.Name, GAMEPAD_NAME_LENGTH)}");
            _gamepadLines.Add(
                FormattableString.Invariant(
                    $"  L {leftX:0.00},{leftY:0.00}  R {rightX:0.00},{rightY:0.00}"
                )
            );
            _gamepadLines.Add(
                FormattableString.Invariant($"  LT {leftTrigger:0.00}  RT {rightTrigger:0.00}")
            );

            if (pad.IsButtonPressed(GamepadButton.South))
            {
                pad.SetVibration(SOUTH_RUMBLE_LOW, SOUTH_RUMBLE_HIGH, SOUTH_RUMBLE_SECONDS);
                _events.Add(
                    FormattableString.Invariant(
                        $"rumble pad {pad.InstanceId}: south press (best effort)"
                    )
                );
            }
        }

        foreach (int instanceId in _previousGamepadIds)
        {
            if (!_gamepadIds.Contains(instanceId))
            {
                _events.Add(FormattableString.Invariant($"gamepad removed {instanceId}"));
            }
        }

        _previousGamepadIds.Clear();
        _previousGamepadIds.AddRange(_gamepadIds);

        if (_gamepadLines.Count == 0)
        {
            _gamepadLines.Add("no gamepad connected");
            _gamepadLines.Add("connect a pad, then press R or South");
        }
    }

    private void BuildWindowLines(IWindow window)
    {
        RayBork.Monitor currentMonitor = window.CurrentMonitor;
        IReadOnlyList<RayBork.Monitor> monitors = window.Monitors;

        string dpi = window.DpiScale.ToString("0.00", CultureInfo.InvariantCulture);
        _windowLines.Clear();
        _windowLines.Add(
            $"state {window.State}  fullscreen {YesNo(window.IsFullscreen)}"
                + $"  minimized {YesNo(window.IsMinimized)}"
        );
        _windowLines.Add(
            $"focused {YesNo(window.IsFocused)}"
                + $"  cursor on screen {YesNo(window.IsCursorOnScreen)}  dpi {dpi}"
        );
        _windowLines.Add(
            $"window {window.Width}x{window.Height}  render {window.RenderWidth}x{window.RenderHeight}"
        );
        _windowLines.Add($"cursor {_cursorMode}  borderless {YesNo(_borderless)}");
        _windowLines.Add(
            $"current monitor #{currentMonitor.Index}"
                + $" {Shorten(currentMonitor.Name, MONITOR_NAME_LENGTH)}"
        );
        _windowLines.Add($"monitors ({monitors.Count})");

        foreach (RayBork.Monitor monitor in monitors)
        {
            string marker = monitor.Index == currentMonitor.Index ? " *" : string.Empty;
            string scale = monitor.DpiScale.ToString("0.00", CultureInfo.InvariantCulture);
            string bounds = FormattableString.Invariant(
                $"{monitor.Bounds.X:0},{monitor.Bounds.Y:0} {monitor.Bounds.Width:0}x{monitor.Bounds.Height:0}"
            );
            string work = FormattableString.Invariant(
                $"{monitor.WorkArea.X:0},{monitor.WorkArea.Y:0} {monitor.WorkArea.Width:0}x{monitor.WorkArea.Height:0}"
            );
            _windowLines.Add(
                $"  #{monitor.Index} {Shorten(monitor.Name, MONITOR_NAME_LENGTH)}{marker}"
                    + $"  {scale}x  {monitor.RefreshRate} Hz"
            );
            _windowLines.Add($"    bounds {bounds}  work {work}");
        }
    }

    private void Draw(EngineContext context, int renderWidth, int renderHeight)
    {
        Font monoFont = context.Defaults.DefaultMonoFont;
        Font fieldFont = context.Defaults.DefaultFont;
        HudStyle style = CreateStyle(monoFont.LineHeight, renderHeight);
        HudLayout layout = ComputeLayout(
            renderWidth,
            renderHeight,
            style,
            _windowLines.Count,
            _gamepadLines.Count
        );
        float fieldTextWidth = MathF.Max(
            0f,
            layout.Field.Width - (FIELD_TEXT_INSET * 2f) - CARET_WIDTH
        );
        TextField.View view = _field.Fit(fieldFont, style.FieldTextSize, fieldTextWidth);

        // A single orthographic pass clears color and depth and records the whole screen-space HUD.
        // The visible height equals the render height, so anchored plane units are render pixels.
        using Camera camera = context.CreateCamera(
            new Transform(new Vector3(0f, 0f, PLANE_DISTANCE)),
            projection: CameraProjection.Orthographic,
            orthographicHeight: renderHeight,
            clearMode: ClearMode.ColorAndDepth,
            clearColor: _clearColor,
            near: CAMERA_NEAR,
            far: CAMERA_FAR
        );

        DrawHeader(camera, layout, style, monoFont);
        DrawField(camera, layout, style, monoFont, fieldFont, view, context.Time);
        DrawPanel(
            camera,
            layout.WindowPanel,
            WINDOW_PANEL_TITLE,
            _windowAccent,
            _windowLines,
            style,
            monoFont
        );
        DrawPanel(
            camera,
            layout.GamepadPanel,
            GAMEPAD_PANEL_TITLE,
            _gamepadAccent,
            _gamepadLines,
            style,
            monoFont
        );
        DrawEventLog(camera, layout, style, monoFont);
        DrawStatus(camera, layout, style, monoFont, context);
        DrawControls(camera, layout, style, monoFont);

        // The platform area follows the same field rectangle in logical window units. Render and
        // window extents differ on high-density displays; a minimized window may report zero render
        // extents, and that case skipped the whole HUD above.
        UpdateTextInputArea(
            context.Window,
            layout.Field,
            FIELD_TEXT_INSET + view.Width,
            renderWidth,
            renderHeight
        );
    }

    private static void DrawHeader(Camera camera, HudLayout layout, HudStyle style, Font font)
    {
        DrawQuad(camera, layout.TitleAccent, _fieldAccent);
        DrawText(camera, font, "RayBork Input", layout.Title, style.TitleSize, _textColor);
        if (style.ShowSubtitle)
        {
            DrawText(
                camera,
                font,
                HEADER_SUBTITLE,
                layout.Subtitle,
                style.SubtitleSize,
                _mutedColor
            );
        }
    }

    private static void DrawField(
        Camera camera,
        HudLayout layout,
        HudStyle style,
        Font monoFont,
        Font fieldFont,
        TextField.View view,
        double time
    )
    {
        DrawQuad(camera, layout.Field, _fieldColor);
        DrawQuad(
            camera,
            new Rect(layout.Field.X, layout.Field.Y, ACCENT_WIDTH, layout.Field.Height),
            _fieldAccent
        );
        DrawText(camera, monoFont, FIELD_LABEL, layout.FieldLabel, style.LabelSize, _mutedColor);

        float textX = layout.Field.X + FIELD_TEXT_INSET;
        float lineHeight = fieldFont.LineHeight * style.FieldTextSize;
        float textY = layout.Field.Y + MathF.Max(2f, (layout.Field.Height - lineHeight) * 0.5f);
        DrawText(
            camera,
            fieldFont,
            view.Text,
            new Vector2(textX, textY),
            style.FieldTextSize,
            _textColor
        );

        // The caret blinks from the engine clock: 0.5 seconds on, 0.5 seconds off.
        long blink = (long)Math.Floor(time / CARET_BLINK_SECONDS);
        if ((blink & 1) == 0)
        {
            DrawQuad(
                camera,
                new Rect(textX + view.Width + 1f, textY, CARET_WIDTH, lineHeight),
                _caretColor
            );
        }

        DrawText(camera, monoFont, FIELD_HINT, layout.FieldHint, style.HintSize, _mutedColor);
    }

    private static void DrawPanel(
        Camera camera,
        Rect rect,
        string title,
        Color accent,
        IReadOnlyList<string> lines,
        HudStyle style,
        Font font
    )
    {
        if (rect.Width <= 0f || rect.Height <= 0f)
        {
            return;
        }

        DrawQuad(camera, rect, _panelColor);
        DrawQuad(camera, new Rect(rect.X, rect.Y, ACCENT_WIDTH, rect.Height), accent);

        float textX = rect.X + ACCENT_WIDTH + PANEL_TEXT_INSET;
        float textWidth = MathF.Max(0f, rect.Width - ACCENT_WIDTH - (PANEL_TEXT_INSET * 2f));
        float y = rect.Y + style.PanelPadding;
        DrawText(camera, font, title, new Vector2(textX, y), style.BodySize, accent);
        y += style.PanelHeaderRow;

        // The layout allocates the panel from its content, but a window at the minimum size can
        // still leave fewer rows than the content needs. Draw only whole rows and report the
        // remainder instead of drawing a partial or inverted row.
        int capacity = (int)MathF.Floor((rect.Bottom - style.PanelPadding - y) / style.BodyRow);
        capacity = Math.Max(0, capacity);
        if (capacity == 0)
        {
            return;
        }

        int visible = Math.Min(lines.Count, capacity);
        bool clipped = visible < lines.Count;
        int drawn = clipped ? Math.Max(0, visible - 1) : visible;
        for (int index = 0; index < drawn; index++)
        {
            DrawText(
                camera,
                font,
                FitLine(font, lines[index], style.BodySize, textWidth),
                new Vector2(textX, y + (index * style.BodyRow)),
                style.BodySize,
                _panelTextColor
            );
        }

        if (clipped)
        {
            string more = FormattableString.Invariant($"... +{lines.Count - drawn} more");
            DrawText(
                camera,
                font,
                more,
                new Vector2(textX, y + (drawn * style.BodyRow)),
                style.BodySize,
                _mutedColor
            );
        }
    }

    private void DrawEventLog(Camera camera, HudLayout layout, HudStyle style, Font font)
    {
        Rect rect = layout.EventLog;
        if (rect.Width <= 0f || rect.Height <= 0f)
        {
            return;
        }

        DrawQuad(camera, rect, _panelColor);
        DrawQuad(camera, new Rect(rect.X, rect.Y, ACCENT_WIDTH, rect.Height), _logAccent);

        float textX = rect.X + ACCENT_WIDTH + PANEL_TEXT_INSET;
        float textWidth = MathF.Max(0f, rect.Width - ACCENT_WIDTH - (PANEL_TEXT_INSET * 2f));
        float y = rect.Y + style.LogPadding;
        DrawText(camera, font, EVENT_LOG_TITLE, new Vector2(textX, y), style.LogSize, _logAccent);
        y += style.LogHeaderRow;

        int capacity = (int)MathF.Floor((rect.Bottom - style.LogPadding - y) / style.LogRow);
        capacity = Math.Max(0, capacity);
        if (capacity == 0)
        {
            return;
        }

        IReadOnlyList<string> entries = _events.Entries;
        if (entries.Count == 0)
        {
            DrawText(
                camera,
                font,
                "no events yet",
                new Vector2(textX, y),
                style.LogSize,
                _mutedColor
            );
            return;
        }

        // Keep the newest events in view when the strip is short at the minimum window size.
        int visible = Math.Min(entries.Count, capacity);
        int start = entries.Count - visible;
        for (int index = 0; index < visible; index++)
        {
            bool newest = start + index == entries.Count - 1;
            DrawText(
                camera,
                font,
                FitLine(font, entries[start + index], style.LogSize, textWidth),
                new Vector2(textX, y + (index * style.LogRow)),
                style.LogSize,
                newest ? _textColor : _panelTextColor
            );
        }
    }

    private void DrawStatus(
        Camera camera,
        HudLayout layout,
        HudStyle style,
        Font font,
        EngineContext context
    )
    {
        RenderStats stats = context.Stats;
        string fps = context.Fps.ToString("0.0", CultureInfo.InvariantCulture);
        string status =
            $"fps {fps}  draw calls {stats.DrawCalls}"
            + $"  instances {stats.Instances}  chars {_field.Length}"
            + $"  focus {YesNo(context.Window.IsFocused)}"
            + $"  cursor on-screen {YesNo(context.Window.IsCursorOnScreen)}"
            + $"  mode {_cursorMode}";
        DrawText(camera, font, status, layout.Status, style.StatusSize, _mutedColor);
    }

    private static void DrawControls(Camera camera, HudLayout layout, HudStyle style, Font font)
    {
        DrawText(camera, font, CONTROLS_TEXT, layout.Controls, style.ControlsSize, _mutedColor);
    }

    private static HudStyle CreateStyle(float lineHeight, float renderHeight)
    {
        bool compact = renderHeight < 480f;
        float titleSize = compact ? 18f : 22f;
        float subtitleSize = compact ? 11f : 12f;
        float bodySize = compact ? 11f : 13f;
        float labelSize = compact ? 10f : 12f;
        float hintSize = compact ? 9f : 11f;
        float logSize = compact ? 9f : 11f;
        float statusSize = compact ? 10f : 12f;
        float controlsSize = compact ? 9f : 11f;

        return new HudStyle(
            TitleSize: titleSize,
            TitleRow: lineHeight * titleSize,
            SubtitleSize: subtitleSize,
            SubtitleRow: lineHeight * subtitleSize,
            BodySize: bodySize,
            BodyRow: lineHeight * bodySize,
            PanelHeaderRow: (lineHeight * bodySize) + 4f,
            PanelPadding: compact ? 6f : 8f,
            LabelSize: labelSize,
            LabelRow: lineHeight * labelSize,
            HintSize: hintSize,
            HintRow: lineHeight * hintSize,
            FieldTextSize: compact ? 15f : 19f,
            LogSize: logSize,
            LogRow: lineHeight * logSize,
            LogHeaderRow: (lineHeight * logSize) + 3f,
            LogPadding: compact ? 6f : 8f,
            StatusSize: statusSize,
            StatusRow: lineHeight * statusSize,
            ControlsSize: controlsSize,
            ControlsRow: lineHeight * controlsSize,
            ShowSubtitle: !compact
        );
    }

    private static HudLayout ComputeLayout(
        float width,
        float height,
        HudStyle style,
        int windowLineCount,
        int gamepadLineCount
    )
    {
        bool compact = height < 480f;
        float margin = compact ? 10f : 16f;
        float gap = compact ? 8f : 12f;
        float fieldHeight = compact ? 32f : 44f;
        float contentWidth = MathF.Max(0f, width - (margin * 2f));

        float y = margin;
        Rect titleAccent = new(margin, y + 2f, ACCENT_WIDTH, MathF.Max(0f, style.TitleRow - 4f));
        Rect title = new(margin + ACCENT_WIDTH + 8f, y, contentWidth, style.TitleRow);
        y += style.TitleRow;

        Rect subtitle = default;
        if (style.ShowSubtitle)
        {
            y += 2f;
            subtitle = new Rect(margin, y, contentWidth, style.SubtitleRow);
            y += style.SubtitleRow;
        }

        y += gap;
        Rect fieldLabel = new(margin, y, contentWidth, style.LabelRow);
        y += style.LabelRow + 3f;
        Rect field = new(margin, y, contentWidth, fieldHeight);
        y += fieldHeight + 3f;
        Rect fieldHint = new(margin, y, contentWidth, style.HintRow);
        y += style.HintRow + gap;
        float panelsTop = y;

        float controlsY = height - margin - style.ControlsRow;
        Rect controls = new(margin, controlsY, contentWidth, style.ControlsRow);
        float statusY = controlsY - 4f - style.StatusRow;
        Rect status = new(margin, statusY, contentWidth, style.StatusRow);
        float logBottom = statusY - gap;

        float available = MathF.Max(0f, logBottom - panelsTop);
        float panelDesired =
            (style.PanelPadding * 2f)
            + style.PanelHeaderRow
            + (Math.Max(windowLineCount, gamepadLineCount) * style.BodyRow);
        float logDesired =
            (style.LogPadding * 2f) + style.LogHeaderRow + (EventLog.CAPACITY * style.LogRow);
        float logMinimum = (style.LogPadding * 2f) + style.LogHeaderRow + style.LogRow;

        float panelHeight;
        float logHeight;
        if (panelDesired + gap + logDesired <= available)
        {
            logHeight = logDesired;
            panelHeight = available - gap - logHeight;
        }
        else
        {
            panelHeight = MathF.Min(panelDesired, MathF.Max(0f, available - gap - logMinimum));
            logHeight = MathF.Max(0f, available - gap - panelHeight);
        }

        // Round the log down to whole rows so the strip never draws a partial row, and give the
        // remainder to the panels. At the minimum window size the panels keep their content and the
        // log keeps at least one row.
        int logRows = 0;
        if (logHeight > 0f)
        {
            float rowsSpace = logHeight - (style.LogPadding * 2f) - style.LogHeaderRow;
            logRows = (int)MathF.Floor(rowsSpace / style.LogRow);
            logRows = Math.Clamp(logRows, 1, EventLog.CAPACITY);
            logHeight = (style.LogPadding * 2f) + style.LogHeaderRow + (logRows * style.LogRow);
        }

        logHeight = MathF.Min(logHeight, available);
        panelHeight = MathF.Max(0f, available - gap - logHeight);
        if (panelHeight <= 0f)
        {
            panelHeight = 0f;
            logHeight = available;
        }

        float logTop = logBottom - logHeight;
        Rect eventLog = new(margin, logTop, contentWidth, logHeight);
        float leftWidth = MathF.Max(0f, (contentWidth - gap) * 0.55f);
        float rightWidth = MathF.Max(0f, contentWidth - gap - leftWidth);
        Rect windowPanel = new(margin, panelsTop, leftWidth, panelHeight);
        Rect gamepadPanel = new(margin + leftWidth + gap, panelsTop, rightWidth, panelHeight);

        return new HudLayout(
            titleAccent,
            title,
            subtitle,
            fieldLabel,
            field,
            fieldHint,
            windowPanel,
            gamepadPanel,
            eventLog,
            status,
            controls
        );
    }

    private static void UpdateTextInputArea(
        IWindow window,
        Rect field,
        float caretPixels,
        int renderWidth,
        int renderHeight
    )
    {
        if (renderWidth <= 0 || renderHeight <= 0)
        {
            return;
        }

        float scaleX = window.Width / (float)renderWidth;
        float scaleY = window.Height / (float)renderHeight;
        Rect area = new(
            field.X * scaleX,
            field.Y * scaleY,
            field.Width * scaleX,
            field.Height * scaleY
        );
        int cursorOffset = (int)MathF.Round(MathF.Max(0f, caretPixels * scaleX));
        window.SetTextInputArea(area, cursorOffset);
    }

    private static void DrawText(
        Camera camera,
        Font font,
        string text,
        Rect anchor,
        float size,
        Color color
    ) => DrawText(camera, font, text, anchor.Position, size, color);

    private static void DrawText(
        Camera camera,
        Font font,
        string text,
        Vector2 position,
        float size,
        Color color
    )
    {
        if (text.Length == 0)
        {
            return;
        }

        camera.DrawText(
            font,
            text,
            camera.ScreenToWorldTransform(position, PLANE_DISTANCE),
            size,
            color
        );
    }

    private static void DrawQuad(Camera camera, Rect rect, Color color)
    {
        if (rect.Width <= 0f || rect.Height <= 0f)
        {
            return;
        }

        Vector2 center = new(rect.X + (rect.Width * 0.5f), rect.Y + (rect.Height * 0.5f));
        camera.DrawQuad(camera.ScreenToWorldTransform(center, PLANE_DISTANCE), rect.Size, color);
    }

    private static string FitLine(Font font, string text, float size, float maxWidth)
    {
        if (text.Length == 0 || maxWidth <= 0f)
        {
            return string.Empty;
        }

        if (font.Measure(text, size).X <= maxWidth)
        {
            return text;
        }

        const string suffix = "...";
        float suffixWidth = font.Measure(suffix, size).X;
        if (suffixWidth > maxWidth)
        {
            return string.Empty;
        }

        int length = text.Length;
        while (length > 0)
        {
            length--;
            if (
                length > 0
                && char.IsHighSurrogate(text[length - 1])
                && char.IsLowSurrogate(text[length])
            )
            {
                length--;
            }

            if (font.Measure(text.AsSpan(0, length), size).X + suffixWidth <= maxWidth)
            {
                break;
            }
        }

        return string.Concat(text.AsSpan(0, length), suffix);
    }

    private static string Quote(string text) =>
        text.Length <= PREVIEW_LENGTH
            ? $"\"{text}\""
            : FormattableString.Invariant($"\"{Preview(text)}\" ({text.Length} chars)");

    private static string Preview(string text)
    {
        if (text.Length <= PREVIEW_LENGTH)
        {
            return text;
        }

        int length = PREVIEW_LENGTH;
        if (char.IsHighSurrogate(text[length - 1]))
        {
            length--;
        }

        return string.Concat(text.AsSpan(0, length), "...");
    }

    private static string Shorten(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        int length = Math.Max(1, maxLength);
        if (char.IsHighSurrogate(text[length - 1]))
        {
            length--;
        }

        return string.Concat(text.AsSpan(0, length), "...");
    }

    private static string YesNo(bool value) => value ? "yes" : "no";

    private static bool IsControlDown(IInput input) =>
        input.IsKeyDown(Key.LeftControl) || input.IsKeyDown(Key.RightControl);

    private static bool IsAltDown(IInput input) =>
        input.IsKeyDown(Key.LeftAlt) || input.IsKeyDown(Key.RightAlt);

    /// <summary>
    /// The text sizes and line rows for one frame, derived from the mono face metrics and whether
    /// the window is at its compact height.
    /// </summary>
    private readonly record struct HudStyle(
        float TitleSize,
        float TitleRow,
        float SubtitleSize,
        float SubtitleRow,
        float BodySize,
        float BodyRow,
        float PanelHeaderRow,
        float PanelPadding,
        float LabelSize,
        float LabelRow,
        float HintSize,
        float HintRow,
        float FieldTextSize,
        float LogSize,
        float LogRow,
        float LogHeaderRow,
        float LogPadding,
        float StatusSize,
        float StatusRow,
        float ControlsSize,
        float ControlsRow,
        bool ShowSubtitle
    );

    /// <summary>
    /// The screen-space rectangles for one frame. Every height is clamped to zero or more before it
    /// reaches a draw, so a small or transient render extent cannot produce an inverted rectangle.
    /// </summary>
    private readonly record struct HudLayout(
        Rect TitleAccent,
        Rect Title,
        Rect Subtitle,
        Rect FieldLabel,
        Rect Field,
        Rect FieldHint,
        Rect WindowPanel,
        Rect GamepadPanel,
        Rect EventLog,
        Rect Status,
        Rect Controls
    );
}
