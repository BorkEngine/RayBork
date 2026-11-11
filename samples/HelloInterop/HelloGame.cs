using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RayBork;
using SDL3;

namespace HelloInterop;

internal sealed class HelloGame : IGame {
    private const int TICK_LOG_INTERVAL = 60;

    private static readonly Key[] _loggedKeys = [
        Key.Space,
        Key.Enter,
        Key.Tab,
        Key.F1,
        Key.F11,
        Key.F12,
    ];

    private static readonly MouseButton[] _loggedMouseButtons = [
        MouseButton.Left,
        MouseButton.Middle,
        MouseButton.Right,
        MouseButton.X1,
        MouseButton.X2,
    ];

    private static readonly GamepadButton[] _loggedGamepadButtons = [
        GamepadButton.South,
        GamepadButton.East,
        GamepadButton.West,
        GamepadButton.North,
        GamepadButton.LeftShoulder,
        GamepadButton.RightShoulder,
        GamepadButton.Start,
    ];

    private readonly int _frameLimit;
    private readonly Dictionary<int, string> _gamepads = [];

    private ILogger _logger = NullLogger.Instance;
    private bool _lastFocused;
    private bool _lastMinimized;
    private WindowState _lastWindowState;
    private long _lastFrameCount;
    private double _lastTime;
    private double _lastFps;
    private int _lastWindowWidth;
    private int _lastWindowHeight;
    private int _lastRenderWidth;
    private int _lastRenderHeight;
    private float _lastDpiScale;
    private string _exitReason = "no ticks were run";

    internal HelloGame(int frameLimit) {
        _frameLimit = frameLimit;
    }

    public Task Setup(EngineContext context) {
        _logger = context.LoggerFactory.CreateLogger("HelloInterop");
        IWindow window = context.Window;

        _logger.LogInformation("RayBork {EngineVersion} M1 diagnostic host starting.", EngineVersion.Current);

        int sdlVersion = SDL.GetVersion();
        int major = sdlVersion / 1_000_000;
        int minor = (sdlVersion / 1_000) % 1_000;
        int patch = sdlVersion % 1_000;
        _logger.LogInformation(
            "SDL {Major}.{Minor}.{Patch}, revision {Revision}, video driver '{VideoDriver}'.",
            major,
            minor,
            patch,
            SDL.GetRevision() ?? "unknown",
            SDL.GetCurrentVideoDriver() ?? "unknown");

        _logger.LogInformation(
            "Window '{Title}': {Width}x{Height}, render {RenderWidth}x{RenderHeight}, DPI scale {DpiScale:0.###}, state {State}, resizable {Resizable}.",
            window.Title,
            window.Width,
            window.Height,
            window.RenderWidth,
            window.RenderHeight,
            window.DpiScale,
            window.State,
            window.IsResizable);

        var monitors = window.Monitors;
        _logger.LogInformation("Monitors: {MonitorCount}.", monitors.Count);
        foreach (var monitor in monitors) {
            _logger.LogInformation(
                "Monitor {Index} '{Name}': bounds {Bounds}, work area {WorkArea}, DPI scale {DpiScale:0.###}, refresh {RefreshRate} Hz.",
                monitor.Index,
                monitor.Name,
                monitor.Bounds,
                monitor.WorkArea,
                monitor.DpiScale,
                monitor.RefreshRate);
        }

        var currentMonitor = window.CurrentMonitor;
        _logger.LogInformation(
            "Current monitor: {Index} '{Name}', DPI scale {DpiScale:0.###}.",
            currentMonitor.Index,
            currentMonitor.Name,
            currentMonitor.DpiScale);

        _logger.LogInformation("Tick thread: {ThreadName}.", Thread.CurrentThread.Name ?? "unnamed");
        _logger.LogInformation(
            "Controls: Escape exits; Space, Enter, Tab, F1, F11, and F12 are logged; mouse buttons, the wheel, and gamepad buttons are logged.");
        _logger.LogInformation(
            "Frame limit: {FrameLimit}.",
            _frameLimit == 0 ? "none (run until close)" : _frameLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));

        _lastFocused = window.IsFocused;
        _lastMinimized = window.IsMinimized;
        _lastWindowState = window.State;
        _lastWindowWidth = window.Width;
        _lastWindowHeight = window.Height;
        _lastRenderWidth = window.RenderWidth;
        _lastRenderHeight = window.RenderHeight;
        _lastDpiScale = window.DpiScale;

        return Task.CompletedTask;
    }

    public void Tick(EngineContext context) {
        ILogger logger = _logger;
        IInput input = context.Input;
        IWindow window = context.Window;
        string? exitReason = null;

        if (input.IsKeyPressed(Key.Escape)) {
            exitReason = "Escape";
            logger.LogInformation("Escape pressed; requesting exit.");
            context.RequestExit();
        }

        foreach (Key key in _loggedKeys) {
            if (input.IsKeyPressed(key)) {
                logger.LogInformation("Key pressed: {Key}.", key);
            }
        }

        string text = input.TextInput;
        if (text.Length > 0) {
            logger.LogInformation("Text input: '{Text}'.", text);
        }

        foreach (MouseButton button in _loggedMouseButtons) {
            if (input.IsMouseButtonPressed(button)) {
                logger.LogInformation(
                    "Mouse {Button} pressed at ({X:0.#}, {Y:0.#}).",
                    button,
                    input.MousePosition.X,
                    input.MousePosition.Y);
            }

            if (input.IsMouseButtonReleased(button)) {
                logger.LogInformation("Mouse {Button} released.", button);
            }
        }

        if (input.MouseWheel != 0f) {
            logger.LogInformation("Mouse wheel: {Wheel:0.###}.", input.MouseWheel);
        }

        if (window.WasResized) {
            logger.LogInformation(
                "Window resized: {Width}x{Height}, render {RenderWidth}x{RenderHeight}, DPI scale {DpiScale:0.###}.",
                window.Width,
                window.Height,
                window.RenderWidth,
                window.RenderHeight,
                window.DpiScale);
        }

        bool focused = window.IsFocused;
        if (focused != _lastFocused) {
            logger.LogInformation(focused ? "Window focus gained." : "Window focus lost.");
            _lastFocused = focused;
        }

        bool minimized = window.IsMinimized;
        if (minimized != _lastMinimized) {
            logger.LogInformation(minimized ? "Window minimized." : "Window restored from minimized state.");
        }

        WindowState state = window.State;
        if (state != _lastWindowState && minimized == _lastMinimized) {
            logger.LogInformation("Window state changed: {PreviousState} -> {State}.", _lastWindowState, state);
        }

        _lastMinimized = minimized;
        _lastWindowState = state;

        LogGamepads(input.Gamepads, logger);

        if (context.FrameCount % TICK_LOG_INTERVAL == 0) {
            logger.LogInformation(
                "Tick {FrameCount}: time {Time:0.000}s, fps {Fps:0.0}, delta {DeltaTime:0.0000}s.",
                context.FrameCount,
                context.Time,
                context.Fps,
                context.DeltaTime);
        }

        _lastFrameCount = context.FrameCount;
        _lastTime = context.Time;
        _lastFps = context.Fps;
        _lastWindowWidth = window.Width;
        _lastWindowHeight = window.Height;
        _lastRenderWidth = window.RenderWidth;
        _lastRenderHeight = window.RenderHeight;
        _lastDpiScale = window.DpiScale;

        if (exitReason is null && _frameLimit > 0 && context.FrameCount >= _frameLimit) {
            exitReason = $"frame limit ({_frameLimit})";
            logger.LogInformation("Frame limit reached; requesting exit.");
            context.RequestExit();
        }

        _exitReason = exitReason ?? "window close or engine request";
    }

    public Task Shutdown() {
        _logger.LogInformation(
            "HelloInterop stopped after {FrameCount} tick(s): time {Time:0.000}s, fps {Fps:0.0}, last window {Width}x{Height} (render {RenderWidth}x{RenderHeight}, DPI scale {DpiScale:0.###}), exit {ExitReason}.",
            _lastFrameCount,
            _lastTime,
            _lastFps,
            _lastWindowWidth,
            _lastWindowHeight,
            _lastRenderWidth,
            _lastRenderHeight,
            _lastDpiScale,
            _exitReason);

        return Task.CompletedTask;
    }

    private void LogGamepads(IReadOnlyList<IGamepad> gamepads, ILogger logger) {
        HashSet<int> connected = [];
        foreach (IGamepad gamepad in gamepads) {
            connected.Add(gamepad.InstanceId);
            if (!_gamepads.ContainsKey(gamepad.InstanceId)) {
                _gamepads.Add(gamepad.InstanceId, gamepad.Name);
                logger.LogInformation(
                    "Gamepad connected: '{Name}' (index {Index}, instance {InstanceId}).",
                    gamepad.Name,
                    gamepad.Index,
                    gamepad.InstanceId);
            }

            foreach (GamepadButton button in _loggedGamepadButtons) {
                if (gamepad.IsButtonPressed(button)) {
                    logger.LogInformation(
                        "Gamepad '{Name}' (index {Index}) button pressed: {Button}.",
                        gamepad.Name,
                        gamepad.Index,
                        button);
                }
            }
        }

        List<int>? disconnected = null;
        foreach (var gamepad in _gamepads) {
            if (!connected.Contains(gamepad.Key)) {
                disconnected ??= [];
                disconnected.Add(gamepad.Key);
            }
        }

        if (disconnected is null) {
            return;
        }

        foreach (int instanceId in disconnected) {
            logger.LogInformation(
                "Gamepad disconnected: '{Name}' (instance {InstanceId}).",
                _gamepads[instanceId],
                instanceId);
            _gamepads.Remove(instanceId);
        }
    }
}
