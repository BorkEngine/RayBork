using System.Diagnostics;
using System.Numerics;
using Microsoft.Extensions.Logging;
using SDL3;

namespace RayBork.Internal;

/// <summary>The SDL3 platform backend.</summary>
/// <remarks>
/// <see cref="Initialize(PlatformOptions, Action)"/>, <see cref="Pump"/>, and window command
/// application run on the main thread; <see cref="CaptureInput"/> and vibration run on the tick
/// thread, where vibration queues work to the main thread.
/// </remarks>
internal sealed class SdlPlatform : IEnginePlatform, IGamepadVibrationSink
{
    private const double GAMEPAD_POLL_SECONDS = 0.25;
    private const string SDL_VIDEO_DRIVER_HINT = "SDL_VIDEO_DRIVER";
    private const string VULKAN_SURFACE_EXTENSION = "VK_KHR_surface";
    private const string WINDOW_LOGGER_CATEGORY = "RayBork.Window";
    private const string INPUT_LOGGER_CATEGORY = "RayBork.Input";

    private static readonly SDL.GamepadButton[] _sdlGamepadButtons =
    [
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

    private static readonly SDL.GamepadAxis[] _sdlGamepadAxes =
    [
        SDL.GamepadAxis.LeftX,
        SDL.GamepadAxis.LeftY,
        SDL.GamepadAxis.RightX,
        SDL.GamepadAxis.RightY,
        SDL.GamepadAxis.LeftTrigger,
        SDL.GamepadAxis.RightTrigger,
    ];

    // SDL's video subsystem, event queue, video-driver hint, and Vulkan loader are process global.
    // A test host creates one platform per test, so initialization and every SDL call that touches
    // process state are serialized and the SDL/Vulkan lifetimes are reference counted. Production
    // creates one platform, so these gates are uncontended there.
    private static readonly Lock _videoGate = new();
    private static readonly Dictionary<uint, SdlPlatform> _windowOwners = [];
    private static int _sdlUsers;
    private static int _vulkanUsers;

    private readonly ILogger _windowLogger;
    private readonly ILogger _inputLogger;
    private readonly SdlInput _input;
    private readonly MonitorRegistry _monitors = new();
    private readonly Dictionary<uint, IntPtr> _openGamepads = [];
    private readonly HashSet<IntPtr> _vulkanSurfaces = [];
    private readonly Lock _vibrationGate = new();
    private readonly Lock _hdrGate = new();
    private readonly Queue<VibrationRequest> _vibrationRequests = new();
    private readonly bool _initializeGamepads;
    private readonly bool _minimalTestMode;

    private IntPtr _windowHandle;
    private SdlWindow? _window;
    private WindowHdrState _windowHdrState = WindowHdrState.Sdr;
    private long _lastGamepadPollTimestamp;
    private bool _sdlInitialized;
    private bool _videoDriverHintSet;
    private bool _textInputStarted;
    private bool _vulkanEnabled;
    private bool _vulkanLoaderLoaded;
    private bool _initialized;
    private bool _disposed;

    /// <summary>Initializes a platform; the optional flags are test-host controls.</summary>
    internal SdlPlatform(
        ILoggerFactory loggerFactory,
        bool initializeGamepads = true,
        bool minimalTestMode = false
    )
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _windowLogger = loggerFactory.CreateLogger(WINDOW_LOGGER_CATEGORY);
        _inputLogger = loggerFactory.CreateLogger(INPUT_LOGGER_CATEGORY);
        _input = new SdlInput(this, GetMousePixelScale);
        _initializeGamepads = initializeGamepads;
        _minimalTestMode = minimalTestMode;
    }

    /// <inheritdoc />
    public IPlatformWindow Window =>
        _window ?? throw new InvalidOperationException("The platform has not been initialized.");

    /// <inheritdoc />
    public WindowHdrState WindowHdrState
    {
        get
        {
            lock (_hdrGate)
            {
                return _windowHdrState;
            }
        }
    }

    /// <summary>Gets the number of platforms holding the SDL video subsystem.</summary>
    internal static int SdlUserCountForTests => _sdlUsers;

    /// <summary>Gets the number of platforms holding the SDL Vulkan loader.</summary>
    internal static int VulkanLoaderUserCountForTests => _vulkanUsers;

    /// <inheritdoc />
    public void Initialize(PlatformOptions options, Action onExitRequested) =>
        Initialize(options, onExitRequested, videoDriverHint: null);

    /// <summary>Initializes with an optional process-global video-driver hint.</summary>
    /// <remarks>
    /// The hint and initialization share one gate; a failed initialization disposes the partial
    /// platform.
    /// </remarks>
    internal void Initialize(
        PlatformOptions options,
        Action onExitRequested,
        string? videoDriverHint
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(onExitRequested);

        lock (_videoGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_initialized)
            {
                throw new InvalidOperationException("The platform has already been initialized.");
            }

            ApplyVideoDriverHint(videoDriverHint);
            try
            {
                InitializeCore(options, onExitRequested);
            }
            catch
            {
                DisposeCore();
                throw;
            }
        }
    }

    private void ApplyVideoDriverHint(string? videoDriverHint)
    {
        if (videoDriverHint is null)
        {
            if (_videoDriverHintSet)
            {
                SDL.ResetHint(SDL_VIDEO_DRIVER_HINT);
                _videoDriverHintSet = false;
            }
        }
        else
        {
            SDL.SetHint(SDL_VIDEO_DRIVER_HINT, videoDriverHint);
            _videoDriverHintSet = true;
        }
    }

    private void InitializeCore(PlatformOptions options, Action onExitRequested)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(onExitRequested);

        SDL.InitFlags initFlags = SDL.InitFlags.Video | SDL.InitFlags.Events;
        if (_initializeGamepads)
        {
            initFlags |= SDL.InitFlags.Gamepad;
        }

        // The caller holds _videoGate; the process-wide counters are protected by it.
        if (_sdlUsers == 0)
        {
            if (!SDL.Init(initFlags))
            {
                throw new PlatformException(
                    $"SDL initialization failed: {SDL.GetError()}",
                    DiagnosticCodes.PLATFORM_INITIALIZATION_FAILED,
                    null
                );
            }
        }

        _sdlUsers++;
        _sdlInitialized = true;

        _vulkanEnabled = options.EnableVulkan;
        // Request the physical-pixel backbuffer. Windows window coordinates are already physical
        // pixels; on platforms that separate logical points from pixels this is what makes
        // RenderWidth/RenderHeight report the real drawable size.
        SDL.WindowFlags windowFlags = SDL.WindowFlags.HighPixelDensity;
        if (options.Resizable)
        {
            windowFlags |= SDL.WindowFlags.Resizable;
        }

        if (options.WindowState == WindowState.Hidden)
        {
            windowFlags |= SDL.WindowFlags.Hidden;
        }

        if (options.EnableVulkan)
        {
            windowFlags |= SDL.WindowFlags.Vulkan;
        }

        _windowHandle = SDL.CreateWindow(options.Title, options.Width, options.Height, windowFlags);
        if (_windowHandle == IntPtr.Zero)
        {
            string error = SDL.GetError();
            ReleaseSdl();
            _sdlInitialized = false;
            throw new WindowException(
                $"Window creation failed: {error}",
                DiagnosticCodes.WINDOW_CREATION_FAILED,
                null
            );
        }

        _window = new SdlWindow(
            _windowHandle,
            _monitors,
            _windowLogger,
            options.Title,
            options.Width,
            options.Height,
            onExitRequested
        );
        _windowOwners[_window.WindowId] = this;
        if (!_minimalTestMode)
        {
            StartTextInput();
        }

        if (!_minimalTestMode)
        {
            RefreshMonitors();
        }

        _window.RefreshSnapshot();
        _window.ApplyStartupState(options.WindowState);
        if (_initializeGamepads)
        {
            ReconcileGamepads();
        }

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
            _window.State
        );
        _windowLogger.LogInformation(
            "Detected {MonitorCount} monitor(s); current monitor {MonitorIndex} '{MonitorName}'.",
            _monitors.Count,
            _window.CurrentMonitor.Index,
            _window.CurrentMonitor.Name
        );
        _inputLogger.LogInformation(
            "Input initialized with {GamepadCount} gamepad(s).",
            _openGamepads.Count
        );
        if (!_minimalTestMode)
        {
            RefreshWindowHdrState();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetVulkanInstanceExtensions()
    {
        lock (_videoGate)
        {
            return GetVulkanInstanceExtensionsCore();
        }
    }

    private IReadOnlyList<string> GetVulkanInstanceExtensionsCore()
    {
        if (_disposed || !_initialized)
        {
            throw new PlatformException(
                "The platform has not been initialized.",
                DiagnosticCodes.VULKAN_SURFACE_CREATION_FAILED,
                null
            );
        }

        if (!_vulkanEnabled)
        {
            throw new PlatformException(
                "The window was not created for Vulkan.",
                DiagnosticCodes.VULKAN_SURFACE_CREATION_FAILED,
                null
            );
        }

        string[]? extensions = SDL.VulkanGetInstanceExtensions(out uint count);
        SortedSet<string> unique = new(StringComparer.Ordinal);
        if (extensions is not null && count > 0)
        {
            foreach (string extension in extensions)
            {
                if (!string.IsNullOrEmpty(extension))
                {
                    unique.Add(extension);
                }
            }
        }

        if (unique.Count == 0)
        {
            throw new PlatformException(
                $"SDL returned no Vulkan instance extensions: {SDL.GetError()}",
                DiagnosticCodes.VULKAN_SURFACE_CREATION_FAILED,
                null
            );
        }

        if (!unique.Contains(VULKAN_SURFACE_EXTENSION))
        {
            throw new PlatformException(
                $"SDL did not report the required {VULKAN_SURFACE_EXTENSION} instance extension.",
                DiagnosticCodes.VULKAN_SURFACE_CREATION_FAILED,
                null
            );
        }

        return [.. unique];
    }

    /// <inheritdoc />
    public IntPtr CreateVulkanSurface(IntPtr instance)
    {
        lock (_videoGate)
        {
            return CreateVulkanSurfaceCore(instance);
        }
    }

    private IntPtr CreateVulkanSurfaceCore(IntPtr instance)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_initialized)
        {
            throw new InvalidOperationException("The platform has not been initialized.");
        }

        if (!_vulkanEnabled)
        {
            throw new InvalidOperationException("The window was not created for Vulkan.");
        }

        if (!_vulkanLoaderLoaded)
        {
            // The caller holds _videoGate; _vulkanUsers is protected by it.
            if (_vulkanUsers == 0)
            {
                // SDL treats a null path as "load the platform default Vulkan loader".
                if (!SDL.VulkanLoadLibrary(null!))
                {
                    throw new PlatformException(
                        $"SDL failed to load the Vulkan loader: {SDL.GetError()}",
                        DiagnosticCodes.VULKAN_SURFACE_CREATION_FAILED,
                        null
                    );
                }
            }

            _vulkanUsers++;
            _vulkanLoaderLoaded = true;
        }

        if (!SDL.VulkanCreateSurface(_windowHandle, instance, IntPtr.Zero, out IntPtr surface))
        {
            throw new PlatformException(
                $"SDL failed to create the Vulkan surface: {SDL.GetError()}",
                DiagnosticCodes.VULKAN_SURFACE_CREATION_FAILED,
                null
            );
        }

        if (surface == IntPtr.Zero)
        {
            throw new PlatformException(
                "SDL returned a zero Vulkan surface handle.",
                DiagnosticCodes.VULKAN_SURFACE_CREATION_FAILED,
                null
            );
        }

        _vulkanSurfaces.Add(surface);
        return surface;
    }

    /// <inheritdoc />
    public void DestroyVulkanSurface(IntPtr instance, IntPtr surface)
    {
        lock (_videoGate)
        {
            DestroyVulkanSurfaceCore(instance, surface);
        }
    }

    private void DestroyVulkanSurfaceCore(IntPtr instance, IntPtr surface)
    {
        if (surface == IntPtr.Zero || _disposed || !_initialized || !_vulkanEnabled)
        {
            return;
        }

        if (!_vulkanSurfaces.Remove(surface))
        {
            return;
        }

        SDL.VulkanDestroySurface(instance, surface, IntPtr.Zero);
    }

    /// <inheritdoc />
    public void Pump(TimeSpan maxWait)
    {
        lock (_videoGate)
        {
            PumpCore(maxWait);
        }
    }

    private void PumpCore(TimeSpan maxWait)
    {
        EnsureInitialized();
        bool appliedCommands = _window!.ApplyCommands();
        ApplyVibrationRequests();

        if (maxWait > TimeSpan.Zero && !appliedCommands)
        {
            if (SDL.WaitEventTimeout(out SDL.Event firstEvent, GetTimeoutMilliseconds(maxWait)))
            {
                HandleEvent(firstEvent);
            }
        }

        while (SDL.PollEvent(out SDL.Event sdlEvent))
        {
            HandleEvent(sdlEvent);
        }

        PollGamepadsIfDue();
    }

    /// <inheritdoc />
    public IInput CaptureInput()
    {
        EnsureInitialized();
        return _input.Capture();
    }

    /// <summary>Gets the render-to-window pixel ratio from the current window snapshot.</summary>
    private Vector2 GetMousePixelScale()
    {
        SdlWindow? window = _window;
        if (window is null)
        {
            return Vector2.One;
        }

        return SdlInput.ComputePixelScale(
            window.RenderWidth,
            window.RenderHeight,
            window.Width,
            window.Height
        );
    }

    /// <inheritdoc />
    public void SetGamepadVibration(
        int instanceId,
        float lowFrequency,
        float highFrequency,
        TimeSpan duration
    )
    {
        lock (_vibrationGate)
        {
            _vibrationRequests.Enqueue(
                new VibrationRequest(instanceId, lowFrequency, highFrequency, duration, Stop: false)
            );
        }
    }

    /// <inheritdoc />
    public void StopGamepadVibration(int instanceId)
    {
        lock (_vibrationGate)
        {
            _vibrationRequests.Enqueue(
                new VibrationRequest(instanceId, 0f, 0f, TimeSpan.Zero, Stop: true)
            );
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_videoGate)
        {
            DisposeCore();
        }
    }

    private void DisposeCore()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _initialized = false;
        try
        {
            CloseGamepads();
            if (_window is not null)
            {
                _windowOwners.Remove(_window.WindowId);
            }

            if (_windowHandle != IntPtr.Zero)
            {
                StopTextInput();
                SDL.DestroyWindow(_windowHandle);
                _windowHandle = IntPtr.Zero;
            }

            lock (_hdrGate)
            {
                _windowHdrState = WindowHdrState.Sdr;
            }

            _vulkanSurfaces.Clear();
        }
        finally
        {
            if (_videoDriverHintSet)
            {
                SDL.ResetHint(SDL_VIDEO_DRIVER_HINT);
                _videoDriverHintSet = false;
            }

            if (_sdlInitialized)
            {
                if (_vulkanLoaderLoaded)
                {
                    // The caller holds _videoGate; _vulkanUsers is protected by it.
                    _vulkanUsers--;
                    if (_vulkanUsers == 0)
                    {
                        SDL.VulkanUnloadLibrary();
                    }

                    _vulkanLoaderLoaded = false;
                }

                ReleaseSdl();
                _sdlInitialized = false;
            }
        }
    }

    private static void ReleaseSdl()
    {
        // The caller holds _videoGate.
        _sdlUsers--;
        if (_sdlUsers == 0)
        {
            SDL.Quit();
        }
    }

    private void EnsureInitialized()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_initialized || _window is null)
        {
            throw new InvalidOperationException("The platform has not been initialized.");
        }
    }

    private void HandleEvent(in SDL.Event sdlEvent)
    {
        SDL.EventType type = (SDL.EventType)sdlEvent.Type;
        if (IsWindowScopedEvent(type))
        {
            uint eventWindowId = GetEventWindowId(sdlEvent, type);
            if (eventWindowId != 0 && eventWindowId != _window!.WindowId)
            {
                if (_windowOwners.TryGetValue(eventWindowId, out SdlPlatform? owner))
                {
                    owner.HandleEvent(sdlEvent);
                }

                return;
            }
        }

        // Display, quit, and gamepad events are process global and are handled by the pumping
        // platform; only window-scoped events are routed to their owner.
        switch (type)
        {
            case SDL.EventType.WindowResized:
            case SDL.EventType.WindowPixelSizeChanged:
            case SDL.EventType.WindowMoved:
            case SDL.EventType.WindowDisplayChanged:
            case SDL.EventType.WindowDisplayScaleChanged:
                _window!.HandleResizeEvent();
                if (type == SDL.EventType.WindowDisplayChanged)
                {
                    RefreshMonitors();
                    RefreshWindowHdrState();
                }
                break;
            case SDL.EventType.WindowHDRStateChanged:
                RefreshWindowHdrState();
                break;
            case SDL.EventType.WindowMouseEnter:
                _window!.SetCursorOnScreen(true);
                break;
            case SDL.EventType.WindowMouseLeave:
                _window!.SetCursorOnScreen(false);
                break;
            case SDL.EventType.WindowMinimized:
            case SDL.EventType.WindowHidden:
                _window!.HandleStateEvent();
                _window.SetCursorOnScreen(false);
                StopTextInput();
                break;
            case SDL.EventType.WindowMaximized:
            case SDL.EventType.WindowEnterFullscreen:
                _window!.HandleStateEvent();
                break;
            case SDL.EventType.WindowRestored:
            case SDL.EventType.WindowShown:
            case SDL.EventType.WindowFocusGained:
                _window!.HandleStateEvent();
                _window.RefreshCursorOnScreen();
                TryStartTextInput();
                break;
            case SDL.EventType.WindowLeaveFullscreen:
                _window!.HandleFullscreenLeave();
                break;
            case SDL.EventType.WindowFocusLost:
                _input.ResetLevels();
                _window!.HandleStateEvent();
                _window.RefreshCursorOnScreen();
                StopTextInput();
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
                if (
                    type
                    is SDL.EventType.DisplayCurrentModeChanged
                        or SDL.EventType.DisplayContentScaleChanged
                )
                {
                    RefreshWindowHdrState();
                }
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

    private static bool IsWindowScopedEvent(SDL.EventType type) =>
        type switch
        {
            SDL.EventType.KeyDown
            or SDL.EventType.KeyUp
            or SDL.EventType.TextInput
            or SDL.EventType.MouseMotion
            or SDL.EventType.MouseButtonDown
            or SDL.EventType.MouseButtonUp
            or SDL.EventType.MouseWheel => true,
            SDL.EventType.WindowResized
            or SDL.EventType.WindowPixelSizeChanged
            or SDL.EventType.WindowMoved
            or SDL.EventType.WindowDisplayChanged
            or SDL.EventType.WindowDisplayScaleChanged
            or SDL.EventType.WindowHDRStateChanged
            or SDL.EventType.WindowMinimized
            or SDL.EventType.WindowMaximized
            or SDL.EventType.WindowRestored
            or SDL.EventType.WindowShown
            or SDL.EventType.WindowHidden
            or SDL.EventType.WindowEnterFullscreen
            or SDL.EventType.WindowLeaveFullscreen
            or SDL.EventType.WindowFocusGained
            or SDL.EventType.WindowFocusLost
            or SDL.EventType.WindowMouseEnter
            or SDL.EventType.WindowMouseLeave
            or SDL.EventType.WindowCloseRequested => true,
            _ => false,
        };

    private static uint GetEventWindowId(in SDL.Event sdlEvent, SDL.EventType type) =>
        type switch
        {
            SDL.EventType.KeyDown or SDL.EventType.KeyUp => sdlEvent.Key.WindowID,
            SDL.EventType.TextInput => sdlEvent.Text.WindowID,
            SDL.EventType.MouseMotion => sdlEvent.Motion.WindowID,
            SDL.EventType.MouseButtonDown or SDL.EventType.MouseButtonUp => sdlEvent
                .Button
                .WindowID,
            SDL.EventType.MouseWheel => sdlEvent.Wheel.WindowID,
            _ => sdlEvent.Window.WindowID,
        };

    /// <summary>Starts text input on the main thread; failures are logged, not thrown.</summary>
    private void StartTextInput()
    {
        if (_minimalTestMode || _windowHandle == IntPtr.Zero)
        {
            return;
        }

        if (!SDL.StartTextInput(_windowHandle))
        {
            _windowLogger.LogWarning("SDL failed to start text input: {Error}", SDL.GetError());
            return;
        }

        _textInputStarted = true;
        _windowLogger.LogDebug("Text input started.");
    }

    /// <summary>Stops text input on the main thread; failures are logged, not thrown.</summary>
    private void StopTextInput()
    {
        if (_minimalTestMode || !_textInputStarted || _windowHandle == IntPtr.Zero)
        {
            return;
        }

        if (!SDL.StopTextInput(_windowHandle))
        {
            _windowLogger.LogWarning("SDL failed to stop text input: {Error}", SDL.GetError());
            return;
        }

        _textInputStarted = false;
        _windowLogger.LogDebug("Text input stopped.");
    }

    /// <summary>Restarts text input when the window is focused and visible.</summary>
    private void TryStartTextInput()
    {
        if (_minimalTestMode || _textInputStarted)
        {
            return;
        }

        SdlWindow? window = _window;
        if (
            window is null
            || !window.IsFocused
            || window.IsMinimized
            || window.State == WindowState.Hidden
        )
        {
            return;
        }

        StartTextInput();
    }

    private void RefreshWindowHdrState()
    {
        uint properties = SDL.GetWindowProperties(_windowHandle);
        if (properties == 0)
        {
            StoreWindowHdrState(WindowHdrState.Sdr);
            _windowLogger.LogDebug(
                "SDL returned no window property set; the window HDR state is SDR."
            );
            return;
        }

        WindowHdrState state = WindowHdrState.Normalize(
            SDL.GetBooleanProperty(properties, SDL.Props.WindowHDREnabledBoolean, false),
            SDL.GetFloatProperty(properties, SDL.Props.WindowSDRWhiteLevelFloat, 1f),
            SDL.GetFloatProperty(properties, SDL.Props.WindowHDRHeadroomFloat, 1f)
        );
        if (StoreWindowHdrState(state))
        {
            _windowLogger.LogInformation(
                "Window HDR state changed: enabled {HdrEnabled}, SDR white level {SdrWhiteLevel}, HDR headroom {HdrHeadroom}.",
                state.HdrEnabled,
                state.SdrWhiteLevel,
                state.HdrHeadroom
            );
        }

        _windowLogger.LogDebug(
            "Window HDR state refreshed: enabled {HdrEnabled}, SDR white level {SdrWhiteLevel}, HDR headroom {HdrHeadroom}.",
            state.HdrEnabled,
            state.SdrWhiteLevel,
            state.HdrHeadroom
        );
    }

    private bool StoreWindowHdrState(WindowHdrState state)
    {
        lock (_hdrGate)
        {
            bool changed = _windowHdrState != state;
            _windowHdrState = state;
            return changed;
        }
    }

    private void RefreshMonitors()
    {
        List<MonitorInfo> monitors = BuildMonitorInfos();
        _monitors.Update(monitors, SDL.GetPrimaryDisplay());
        _window?.RefreshSnapshot();
        _windowLogger.LogDebug("Monitors refreshed: {MonitorCount} display(s).", monitors.Count);
    }

    private static List<MonitorInfo> BuildMonitorInfos()
    {
        uint[] displayIds = SDL.GetDisplays(out _) ?? [];
        List<MonitorInfo> monitors = new(displayIds.Length);
        foreach (uint displayId in displayIds)
        {
            string name = SDL.GetDisplayName(displayId) ?? string.Empty;
            Rect bounds = default;
            if (SDL.GetDisplayBounds(displayId, out SDL.Rect boundsRect))
            {
                bounds = ToRect(boundsRect);
            }

            Rect workArea = bounds;
            if (SDL.GetDisplayUsableBounds(displayId, out SDL.Rect usableRect))
            {
                workArea = ToRect(usableRect);
            }

            float dpiScale = SDL.GetDisplayContentScale(displayId);
            if (!(dpiScale > 0f))
            {
                dpiScale = 1f;
            }

            int refreshRate = 0;
            SDL.DisplayMode? mode = SDL.GetCurrentDisplayMode(displayId);
            if (
                mode.HasValue
                && float.IsFinite(mode.Value.RefreshRate)
                && mode.Value.RefreshRate > 0f
            )
            {
                refreshRate = (int)MathF.Round(mode.Value.RefreshRate);
            }

            monitors.Add(new MonitorInfo(displayId, name, bounds, workArea, dpiScale, refreshRate));
        }

        return monitors;
    }

    private static Rect ToRect(SDL.Rect rect) => new(rect.X, rect.Y, rect.W, rect.H);

    private void ReconcileGamepads()
    {
        uint[] connectedIds = SDL.GetGamepads(out _) ?? [];
        HashSet<uint> connectedSet = [.. connectedIds];
        foreach (uint instanceId in connectedIds)
        {
            if (_openGamepads.ContainsKey(instanceId))
            {
                continue;
            }

            IntPtr gamepad = SDL.OpenGamepad(instanceId);
            if (gamepad == IntPtr.Zero)
            {
                _inputLogger.LogWarning(
                    "SDL failed to open gamepad {InstanceId}: {Error}",
                    instanceId,
                    SDL.GetError()
                );
                continue;
            }

            _openGamepads.Add(instanceId, gamepad);
            string name = SDL.GetGamepadName(gamepad) ?? string.Empty;
            ConnectGamepadState(instanceId, gamepad, name);
            _inputLogger.LogInformation(
                "Gamepad connected: {Name} ({InstanceId}).",
                name,
                instanceId
            );
        }

        List<uint>? disconnected = null;
        foreach (uint instanceId in _openGamepads.Keys)
        {
            if (!connectedSet.Contains(instanceId))
            {
                disconnected ??= [];
                disconnected.Add(instanceId);
            }
        }

        if (disconnected is null)
        {
            return;
        }

        foreach (uint instanceId in disconnected)
        {
            SDL.CloseGamepad(_openGamepads[instanceId]);
            _openGamepads.Remove(instanceId);
            _input.State.DisconnectGamepad(instanceId);
            _inputLogger.LogInformation("Gamepad disconnected: {InstanceId}.", instanceId);
        }
    }

    private void ConnectGamepadState(uint instanceId, IntPtr gamepad, string name)
    {
        bool[] buttons = new bool[InputMappings.GAMEPAD_BUTTON_COUNT];
        foreach (SDL.GamepadButton sdlButton in _sdlGamepadButtons)
        {
            if (
                InputMappings.TryGetGamepadButton(sdlButton, out GamepadButton button)
                && SDL.GetGamepadButton(gamepad, sdlButton)
            )
            {
                buttons[(int)button] = true;
            }
        }

        float[] axes = new float[InputMappings.GAMEPAD_AXIS_SLOT_COUNT];
        foreach (SDL.GamepadAxis sdlAxis in _sdlGamepadAxes)
        {
            short value = SDL.GetGamepadAxis(gamepad, sdlAxis);
            if (InputMappings.TryGetGamepadAxis((byte)sdlAxis, out GamepadAxis axis))
            {
                axes[InputMappings.AxisSlot(axis)] = InputMappings.NormalizeStickAxis(value);
            }
            else if (InputMappings.TryGetGamepadTrigger((byte)sdlAxis, out GamepadTrigger trigger))
            {
                axes[InputMappings.TriggerSlot(trigger)] = InputMappings.NormalizeTrigger(value);
            }
        }

        _input.State.ConnectGamepad(instanceId, name, buttons, axes);
    }

    private void PollGamepadsIfDue()
    {
        if (!_initializeGamepads)
        {
            return;
        }

        long now = Stopwatch.GetTimestamp();
        double elapsedSeconds = (now - _lastGamepadPollTimestamp) / (double)Stopwatch.Frequency;
        if (elapsedSeconds < GAMEPAD_POLL_SECONDS)
        {
            return;
        }

        _lastGamepadPollTimestamp = now;
        ReconcileGamepads();
    }

    private void ApplyVibrationRequests()
    {
        VibrationRequest[] requests;
        lock (_vibrationGate)
        {
            if (_vibrationRequests.Count == 0)
            {
                return;
            }

            requests = [.. _vibrationRequests];
            _vibrationRequests.Clear();
        }

        foreach (VibrationRequest request in requests)
        {
            uint instanceId = unchecked((uint)request.InstanceId);
            if (!_openGamepads.TryGetValue(instanceId, out IntPtr gamepad))
            {
                continue;
            }

            bool applied = request.Stop
                ? SDL.RumbleGamepad(gamepad, 0, 0, 0)
                : SDL.RumbleGamepad(
                    gamepad,
                    ToRumbleIntensity(request.LowFrequency),
                    ToRumbleIntensity(request.HighFrequency),
                    ToDurationMilliseconds(request.Duration)
                );
            if (!applied)
            {
                _inputLogger.LogDebug(
                    "Gamepad vibration is unavailable for instance {InstanceId}: {Error}",
                    request.InstanceId,
                    SDL.GetError()
                );
            }
        }
    }

    private void CloseGamepads()
    {
        foreach (KeyValuePair<uint, IntPtr> entry in _openGamepads)
        {
            SDL.CloseGamepad(entry.Value);
            _input.State.DisconnectGamepad(entry.Key);
        }

        _openGamepads.Clear();
    }

    private static ushort ToRumbleIntensity(float intensity)
    {
        float clamped = Math.Clamp(intensity, 0f, 1f);
        if (float.IsNaN(clamped))
        {
            return 0;
        }

        return (ushort)MathF.Round(clamped * ushort.MaxValue);
    }

    private static uint ToDurationMilliseconds(TimeSpan duration)
    {
        double milliseconds = duration.TotalMilliseconds;
        if (milliseconds <= 0.0)
        {
            return 0;
        }

        if (milliseconds >= uint.MaxValue)
        {
            return uint.MaxValue;
        }

        return (uint)Math.Ceiling(milliseconds);
    }

    private static int GetTimeoutMilliseconds(TimeSpan maxWait)
    {
        double milliseconds = maxWait.TotalMilliseconds;
        if (milliseconds >= int.MaxValue)
        {
            return int.MaxValue;
        }

        return milliseconds <= 0.0 ? 0 : (int)Math.Ceiling(milliseconds);
    }

    private readonly record struct VibrationRequest(
        int InstanceId,
        float LowFrequency,
        float HighFrequency,
        TimeSpan Duration,
        bool Stop
    );
}
