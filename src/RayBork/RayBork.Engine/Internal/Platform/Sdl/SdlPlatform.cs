using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SDL3;

namespace RayBork.Internal;

/// <summary>
/// The SDL3 platform backend. <see cref="Initialize"/>, <see cref="Pump"/>, and window command
/// application run on the process main thread; <see cref="CaptureInput"/> runs on the tick thread.
/// Vibration calls run on the tick thread and only enqueue work for the main thread, so SDL is never
/// touched from the tick thread.
/// </summary>
internal sealed class SdlPlatform : IEnginePlatform, IGamepadVibrationSink {
    private const double GAMEPAD_POLL_SECONDS = 0.25;
    private const string WINDOW_LOGGER_CATEGORY = "RayBork.Window";
    private const string INPUT_LOGGER_CATEGORY = "RayBork.Input";

    private static readonly SDL.GamepadButton[] _sdlGamepadButtons = [
        SDL.GamepadButton.South,
        SDL.GamepadButton.East,
        SDL.GamepadButton.West,
        SDL.GamepadButton.North,
        SDL.GamepadButton.Back,
        SDL.GamepadButton.Guide,
        SDL.GamepadButton.Start,
        SDL.GamepadButton.LeftStick,
        SDL.GamepadButton.RightStick,
        SDL.GamepadButton.LeftShoulder,
        SDL.GamepadButton.RightShoulder,
        SDL.GamepadButton.DPadUp,
        SDL.GamepadButton.DPadDown,
        SDL.GamepadButton.DPadLeft,
        SDL.GamepadButton.DPadRight,
        SDL.GamepadButton.Misc1,
        SDL.GamepadButton.Touchpad,
        SDL.GamepadButton.Misc2,
        SDL.GamepadButton.Misc3,
    ];

    private static readonly SDL.GamepadAxis[] _sdlGamepadAxes = [
        SDL.GamepadAxis.LeftX,
        SDL.GamepadAxis.LeftY,
        SDL.GamepadAxis.RightX,
        SDL.GamepadAxis.RightY,
        SDL.GamepadAxis.LeftTrigger,
        SDL.GamepadAxis.RightTrigger,
    ];

    private readonly ILogger _windowLogger;
    private readonly ILogger _inputLogger;
    private readonly SdlInput _input;
    private readonly MonitorRegistry _monitors = new();
    private readonly Dictionary<uint, IntPtr> _openGamepads = [];
    private readonly Lock _vibrationGate = new();
    private readonly Queue<VibrationRequest> _vibrationRequests = new();

    private IntPtr _windowHandle;
    private SdlWindow? _window;
    private long _lastGamepadPollTimestamp;
    private bool _sdlInitialized;
    private bool _textInputStarted;
    private bool _initialized;
    private bool _disposed;

    internal SdlPlatform(ILoggerFactory loggerFactory) {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _windowLogger = loggerFactory.CreateLogger(WINDOW_LOGGER_CATEGORY);
        _inputLogger = loggerFactory.CreateLogger(INPUT_LOGGER_CATEGORY);
        _input = new SdlInput(this);
    }

    /// <inheritdoc />
    public IPlatformWindow Window => _window ?? throw new InvalidOperationException("The platform has not been initialized.");

    /// <inheritdoc />
    public void Initialize(PlatformOptions options, Action onExitRequested) {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(onExitRequested);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized) {
            throw new InvalidOperationException("The platform has already been initialized.");
        }

        SDL.InitFlags initFlags = SDL.InitFlags.Video | SDL.InitFlags.Events | SDL.InitFlags.Gamepad;
        if (!SDL.Init(initFlags)) {
            throw new PlatformException(
                $"SDL initialization failed: {SDL.GetError()}",
                DiagnosticCodes.PLATFORM_INITIALIZATION_FAILED,
                null);
        }

        _sdlInitialized = true;
        SDL.WindowFlags windowFlags = 0;
        if (options.Resizable) {
            windowFlags |= SDL.WindowFlags.Resizable;
        }

        if (options.WindowState == WindowState.Hidden) {
            windowFlags |= SDL.WindowFlags.Hidden;
        }

        _windowHandle = SDL.CreateWindow(options.Title, options.Width, options.Height, windowFlags);
        if (_windowHandle == IntPtr.Zero) {
            string error = SDL.GetError();
            SDL.Quit();
            _sdlInitialized = false;
            throw new WindowException(
                $"Window creation failed: {error}",
                DiagnosticCodes.WINDOW_CREATION_FAILED,
                null);
        }

        _window = new SdlWindow(_windowHandle, _monitors, _windowLogger, options.Title, options.Width, options.Height, onExitRequested);
        _textInputStarted = SDL.StartTextInput(_windowHandle);
        if (!_textInputStarted) {
            _windowLogger.LogWarning("SDL failed to start text input: {Error}", SDL.GetError());
        }

        RefreshMonitors();
        _window.RefreshSnapshot();
        _window.ApplyStartupState(options.WindowState);
        ReconcileGamepads();
        _lastGamepadPollTimestamp = Stopwatch.GetTimestamp();
        _initialized = true;

        _windowLogger.LogInformation(
            "Window created: {Title} {Width}x{Height} (render {RenderWidth}x{RenderHeight}, dpi scale {DpiScale}, state {State}).",
            options.Title,
            _window.Width,
            _window.Height,
            _window.RenderWidth,
            _window.RenderHeight,
            _window.DpiScale,
            _window.State);
        _windowLogger.LogInformation(
            "Detected {MonitorCount} monitor(s); current monitor {MonitorIndex} '{MonitorName}'.",
            _monitors.Count,
            _window.CurrentMonitor.Index,
            _window.CurrentMonitor.Name);
        _inputLogger.LogInformation("Input initialized with {GamepadCount} gamepad(s).", _openGamepads.Count);
    }

    /// <inheritdoc />
    public void Pump(TimeSpan maxWait) {
        EnsureInitialized();
        bool appliedCommands = _window!.ApplyCommands();
        ApplyVibrationRequests();

        if (maxWait > TimeSpan.Zero && !appliedCommands) {
            if (SDL.WaitEventTimeout(out SDL.Event firstEvent, GetTimeoutMilliseconds(maxWait))) {
                HandleEvent(firstEvent);
            }
        }

        while (SDL.PollEvent(out SDL.Event sdlEvent)) {
            HandleEvent(sdlEvent);
        }

        PollGamepadsIfDue();
    }

    /// <inheritdoc />
    public IInput CaptureInput() {
        EnsureInitialized();
        return _input.Capture();
    }

    /// <inheritdoc />
    public void SetGamepadVibration(int instanceId, float lowFrequency, float highFrequency, TimeSpan duration) {
        lock (_vibrationGate) {
            _vibrationRequests.Enqueue(new VibrationRequest(instanceId, lowFrequency, highFrequency, duration, Stop: false));
        }
    }

    /// <inheritdoc />
    public void StopGamepadVibration(int instanceId) {
        lock (_vibrationGate) {
            _vibrationRequests.Enqueue(new VibrationRequest(instanceId, 0f, 0f, TimeSpan.Zero, Stop: true));
        }
    }

    /// <inheritdoc />
    public void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;
        _initialized = false;
        try {
            CloseGamepads();
            if (_windowHandle != IntPtr.Zero) {
                if (_textInputStarted) {
                    SDL.StopTextInput(_windowHandle);
                    _textInputStarted = false;
                }

                SDL.DestroyWindow(_windowHandle);
                _windowHandle = IntPtr.Zero;
            }
        } finally {
            if (_sdlInitialized) {
                SDL.Quit();
                _sdlInitialized = false;
            }
        }
    }

    private void EnsureInitialized() {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_initialized || _window is null) {
            throw new InvalidOperationException("The platform has not been initialized.");
        }
    }

    private void HandleEvent(in SDL.Event sdlEvent) {
        SDL.EventType type = (SDL.EventType)sdlEvent.Type;
        switch (type) {
            case SDL.EventType.WindowResized:
            case SDL.EventType.WindowPixelSizeChanged:
            case SDL.EventType.WindowMoved:
            case SDL.EventType.WindowDisplayChanged:
            case SDL.EventType.WindowDisplayScaleChanged:
                _window!.HandleResizeEvent();
                if (type == SDL.EventType.WindowDisplayChanged) {
                    RefreshMonitors();
                }
                break;
            case SDL.EventType.WindowMinimized:
            case SDL.EventType.WindowMaximized:
            case SDL.EventType.WindowRestored:
            case SDL.EventType.WindowShown:
            case SDL.EventType.WindowHidden:
            case SDL.EventType.WindowEnterFullscreen:
            case SDL.EventType.WindowLeaveFullscreen:
            case SDL.EventType.WindowFocusGained:
                _window!.HandleStateEvent();
                break;
            case SDL.EventType.WindowFocusLost:
                _input.ResetLevels();
                _window!.HandleStateEvent();
                break;
            case SDL.EventType.WindowCloseRequested:
            case SDL.EventType.Quit:
                _window!.RequestClose();
                break;
            case SDL.EventType.DisplayAdded:
            case SDL.EventType.DisplayRemoved:
            case SDL.EventType.DisplayMoved:
            case SDL.EventType.DisplayOrientation:
            case SDL.EventType.DisplayDesktopModeChanged:
            case SDL.EventType.DisplayCurrentModeChanged:
            case SDL.EventType.DisplayContentScaleChanged:
            case SDL.EventType.UsableBoundsChanged:
                RefreshMonitors();
                break;
            case SDL.EventType.GamepadAdded:
            case SDL.EventType.GamepadRemoved:
                ReconcileGamepads();
                break;
            default:
                _input.HandleEvent(sdlEvent);
                break;
        }
    }

    private void RefreshMonitors() {
        List<MonitorInfo> monitors = BuildMonitorInfos();
        _monitors.Update(monitors, SDL.GetPrimaryDisplay());
        _window?.RefreshSnapshot();
        _windowLogger.LogDebug("Monitors refreshed: {MonitorCount} display(s).", monitors.Count);
    }

    private static List<MonitorInfo> BuildMonitorInfos() {
        uint[] displayIds = SDL.GetDisplays(out _) ?? [];
        List<MonitorInfo> monitors = new(displayIds.Length);
        foreach (uint displayId in displayIds) {
            string name = SDL.GetDisplayName(displayId) ?? string.Empty;
            Rect bounds = default;
            if (SDL.GetDisplayBounds(displayId, out SDL.Rect boundsRect)) {
                bounds = ToRect(boundsRect);
            }

            Rect workArea = bounds;
            if (SDL.GetDisplayUsableBounds(displayId, out SDL.Rect usableRect)) {
                workArea = ToRect(usableRect);
            }

            float dpiScale = SDL.GetDisplayContentScale(displayId);
            if (!(dpiScale > 0f)) {
                dpiScale = 1f;
            }

            int refreshRate = 0;
            SDL.DisplayMode? mode = SDL.GetCurrentDisplayMode(displayId);
            if (mode.HasValue && float.IsFinite(mode.Value.RefreshRate) && mode.Value.RefreshRate > 0f) {
                refreshRate = (int)MathF.Round(mode.Value.RefreshRate);
            }

            monitors.Add(new MonitorInfo(displayId, name, bounds, workArea, dpiScale, refreshRate));
        }

        return monitors;
    }

    private static Rect ToRect(SDL.Rect rect) => new(rect.X, rect.Y, rect.W, rect.H);

    private void ReconcileGamepads() {
        uint[] connectedIds = SDL.GetGamepads(out _) ?? [];
        HashSet<uint> connectedSet = [.. connectedIds];
        foreach (uint instanceId in connectedIds) {
            if (_openGamepads.ContainsKey(instanceId)) {
                continue;
            }

            IntPtr gamepad = SDL.OpenGamepad(instanceId);
            if (gamepad == IntPtr.Zero) {
                _inputLogger.LogWarning(
                    "SDL failed to open gamepad {InstanceId}: {Error}",
                    instanceId,
                    SDL.GetError());
                continue;
            }

            _openGamepads.Add(instanceId, gamepad);
            string name = SDL.GetGamepadName(gamepad) ?? string.Empty;
            ConnectGamepadState(instanceId, gamepad, name);
            _inputLogger.LogInformation("Gamepad connected: {Name} ({InstanceId}).", name, instanceId);
        }

        List<uint>? disconnected = null;
        foreach (uint instanceId in _openGamepads.Keys) {
            if (!connectedSet.Contains(instanceId)) {
                disconnected ??= [];
                disconnected.Add(instanceId);
            }
        }

        if (disconnected is null) {
            return;
        }

        foreach (uint instanceId in disconnected) {
            SDL.CloseGamepad(_openGamepads[instanceId]);
            _openGamepads.Remove(instanceId);
            _input.State.DisconnectGamepad(instanceId);
            _inputLogger.LogInformation("Gamepad disconnected: {InstanceId}.", instanceId);
        }
    }

    private void ConnectGamepadState(uint instanceId, IntPtr gamepad, string name) {
        bool[] buttons = new bool[InputMappings.GAMEPAD_BUTTON_COUNT];
        foreach (SDL.GamepadButton sdlButton in _sdlGamepadButtons) {
            if (InputMappings.TryGetGamepadButton(sdlButton, out GamepadButton button) && SDL.GetGamepadButton(gamepad, sdlButton)) {
                buttons[(int)button] = true;
            }
        }

        float[] axes = new float[InputMappings.GAMEPAD_AXIS_SLOT_COUNT];
        foreach (SDL.GamepadAxis sdlAxis in _sdlGamepadAxes) {
            short value = SDL.GetGamepadAxis(gamepad, sdlAxis);
            if (InputMappings.TryGetGamepadAxis((byte)sdlAxis, out GamepadAxis axis)) {
                axes[InputMappings.AxisSlot(axis)] = InputMappings.NormalizeStickAxis(value);
            } else if (InputMappings.TryGetGamepadTrigger((byte)sdlAxis, out GamepadTrigger trigger)) {
                axes[InputMappings.TriggerSlot(trigger)] = InputMappings.NormalizeTrigger(value);
            }
        }

        _input.State.ConnectGamepad(instanceId, name, buttons, axes);
    }

    private void PollGamepadsIfDue() {
        long now = Stopwatch.GetTimestamp();
        double elapsedSeconds = (now - _lastGamepadPollTimestamp) / (double)Stopwatch.Frequency;
        if (elapsedSeconds < GAMEPAD_POLL_SECONDS) {
            return;
        }

        _lastGamepadPollTimestamp = now;
        ReconcileGamepads();
    }

    private void ApplyVibrationRequests() {
        VibrationRequest[] requests;
        lock (_vibrationGate) {
            if (_vibrationRequests.Count == 0) {
                return;
            }

            requests = [.. _vibrationRequests];
            _vibrationRequests.Clear();
        }

        foreach (VibrationRequest request in requests) {
            uint instanceId = unchecked((uint)request.InstanceId);
            if (!_openGamepads.TryGetValue(instanceId, out IntPtr gamepad)) {
                continue;
            }

            bool applied = request.Stop
                ? SDL.RumbleGamepad(gamepad, 0, 0, 0)
                : SDL.RumbleGamepad(
                    gamepad,
                    ToRumbleIntensity(request.LowFrequency),
                    ToRumbleIntensity(request.HighFrequency),
                    ToDurationMilliseconds(request.Duration));
            if (!applied) {
                _inputLogger.LogDebug(
                    "Gamepad vibration is unavailable for instance {InstanceId}: {Error}",
                    request.InstanceId,
                    SDL.GetError());
            }
        }
    }

    private void CloseGamepads() {
        foreach (KeyValuePair<uint, IntPtr> entry in _openGamepads) {
            SDL.CloseGamepad(entry.Value);
            _input.State.DisconnectGamepad(entry.Key);
        }

        _openGamepads.Clear();
    }

    private static ushort ToRumbleIntensity(float intensity) {
        float clamped = Math.Clamp(intensity, 0f, 1f);
        if (float.IsNaN(clamped)) {
            return 0;
        }

        return (ushort)MathF.Round(clamped * ushort.MaxValue);
    }

    private static uint ToDurationMilliseconds(TimeSpan duration) {
        double milliseconds = duration.TotalMilliseconds;
        if (milliseconds <= 0.0) {
            return 0;
        }

        if (milliseconds >= uint.MaxValue) {
            return uint.MaxValue;
        }

        return (uint)Math.Ceiling(milliseconds);
    }

    private static int GetTimeoutMilliseconds(TimeSpan maxWait) {
        double milliseconds = maxWait.TotalMilliseconds;
        if (milliseconds >= int.MaxValue) {
            return int.MaxValue;
        }

        return milliseconds <= 0.0 ? 0 : (int)Math.Ceiling(milliseconds);
    }

    private readonly record struct VibrationRequest(
        int InstanceId,
        float LowFrequency,
        float HighFrequency,
        TimeSpan Duration,
        bool Stop);
}
