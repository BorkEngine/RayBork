using Microsoft.Extensions.Logging;
using RayBork.Internal;

namespace RayBork;

/// <summary>
/// Configures and builds a <see cref="RayBorkApp"/>.
/// </summary>
/// <remarks>
/// The builder is single-use and is not thread-safe: configure it before <see cref="Build()"/> and do
/// not touch it afterwards. <see cref="Build()"/> validates the configuration only; it does not
/// initialize SDL, create a window, or create the logger factory. Those happen in
/// <see cref="RayBorkApp.Run"/>.
/// </remarks>
public sealed class RayBorkAppBuilder {
    private const string DEFAULT_TITLE = "RayBork";
    private const int DEFAULT_WIDTH = 1280;
    private const int DEFAULT_HEIGHT = 720;
    private const double DEFAULT_MAX_DELTA_TIME = 0.25;
    private const int DEFAULT_TARGET_FPS = 0;
    private const WindowState DEFAULT_WINDOW_STATE = WindowState.Normal;
    private const LogLevel DEFAULT_LOG_LEVEL = LogLevel.Information;
    private const PresentMode DEFAULT_PRESENT_MODE = PresentMode.Mailbox;
    private const int DEFAULT_SAMPLE_COUNT = 4;
    private const TonemappingMode DEFAULT_TONEMAPPING = TonemappingMode.AgX;
    private const float DEFAULT_EXPOSURE = 0f;
#if DEBUG
    private const bool DEFAULT_VALIDATION_ENABLED = true;
#else
    private const bool DEFAULT_VALIDATION_ENABLED = false;
#endif

    private static readonly Color _defaultClearColor =
        Color.Black.A == 1f ? Color.Black : new Color(0f, 0f, 0f, 1f);

    private IGame? _game;
    private string _title = DEFAULT_TITLE;
    private int _width = DEFAULT_WIDTH;
    private int _height = DEFAULT_HEIGHT;
    private bool _resizable;
    private double _maxDeltaTime = DEFAULT_MAX_DELTA_TIME;
    private PresentMode _presentMode = DEFAULT_PRESENT_MODE;
    private int _sampleCount = DEFAULT_SAMPLE_COUNT;
    private TonemappingMode _tonemapping = DEFAULT_TONEMAPPING;
    private float _exposure = DEFAULT_EXPOSURE;
    private Color _clearColor = _defaultClearColor;
    private bool _validationEnabled = DEFAULT_VALIDATION_ENABLED;
    private int _targetFps = DEFAULT_TARGET_FPS;
    private WindowState _windowState = DEFAULT_WINDOW_STATE;
    private ILoggerFactory? _loggerFactory;
    private LogLevel _logLevel = DEFAULT_LOG_LEVEL;
    private bool _built;

    /// <summary>
    /// Initializes a new builder with the default configuration: a <c>1280x720</c>, non-resizable
    /// window titled <c>RayBork</c>, a <c>0.25</c> second delta clamp, an uncapped tick loop, and the
    /// default ZLogger console factory filtered at <see cref="LogLevel.Information"/>.
    /// </summary>
    public RayBorkAppBuilder() {
    }

    /// <summary>
    /// Sets the game whose callbacks run on the tick thread.
    /// </summary>
    /// <param name="game">The game to run.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="game"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">The builder already built an app.</exception>
    public RayBorkAppBuilder WithGame(IGame game) {
        ThrowIfBuilt();
        ArgumentNullException.ThrowIfNull(game);
        _game = game;
        return this;
    }

    /// <summary>
    /// Sets the initial window title.
    /// </summary>
    /// <param name="title">The title. It must not be empty or whitespace.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="title"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="title"/> is empty or whitespace.</exception>
    /// <exception cref="InvalidOperationException">The builder already built an app.</exception>
    public RayBorkAppBuilder WithTitle(string title) {
        ThrowIfBuilt();
        ArgumentNullException.ThrowIfNull(title);
        if (string.IsNullOrWhiteSpace(title)) {
            throw new ArgumentException("The window title must not be empty or whitespace.", nameof(title));
        }

        _title = title;
        return this;
    }

    /// <summary>
    /// Sets the initial client size in logical units.
    /// </summary>
    /// <param name="width">The client width. It must be positive.</param>
    /// <param name="height">The client height. It must be positive.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="width"/> or <paramref name="height"/> is zero or negative.
    /// </exception>
    /// <exception cref="InvalidOperationException">The builder already built an app.</exception>
    public RayBorkAppBuilder WithSize(int width, int height) {
        ThrowIfBuilt();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        _width = width;
        _height = height;
        return this;
    }

    /// <summary>
    /// Sets whether the window can be resized by the user.
    /// </summary>
    /// <param name="resizable">Whether the window is resizable.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="InvalidOperationException">The builder already built an app.</exception>
    public RayBorkAppBuilder WithResizable(bool resizable = true) {
        ThrowIfBuilt();
        _resizable = resizable;
        return this;
    }

    /// <summary>
    /// Sets the clamp applied to the per-tick delta time.
    /// </summary>
    /// <param name="seconds">The clamp in seconds. It must be finite and greater than zero.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="seconds"/> is not finite or is not greater than zero.
    /// </exception>
    /// <exception cref="InvalidOperationException">The builder already built an app.</exception>
    public RayBorkAppBuilder WithMaxDeltaTime(double seconds) {
        ThrowIfBuilt();
        if (!double.IsFinite(seconds) || seconds <= 0.0) {
            throw new ArgumentOutOfRangeException(
                nameof(seconds),
                seconds,
                "The maximum delta time must be finite and greater than zero.");
        }

        _maxDeltaTime = seconds;
        return this;
    }

    /// <summary>
    /// Sets the presentation mode requested from the renderer.
    /// </summary>
    /// <param name="mode">A defined <see cref="PresentMode"/> value. The default is
    /// <see cref="PresentMode.Mailbox"/>.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not a defined value.</exception>
    /// <exception cref="InvalidOperationException">The builder already built an app.</exception>
    public RayBorkAppBuilder WithPresentMode(PresentMode mode = PresentMode.Mailbox) {
        ThrowIfBuilt();
        if (!Enum.IsDefined(mode)) {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "The present mode is not a defined value.");
        }

        _presentMode = mode;
        return this;
    }

    /// <summary>
    /// Sets the requested MSAA sample count.
    /// </summary>
    /// <param name="sampleCount">
    /// The requested sample count. It must be positive. Unsupported but positive counts are accepted
    /// here; the renderer selects the highest supported count that does not exceed the request. The
    /// default is <c>4</c>.
    /// </param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="sampleCount"/> is zero or negative.
    /// </exception>
    /// <exception cref="InvalidOperationException">The builder already built an app.</exception>
    public RayBorkAppBuilder WithMSAA(int sampleCount = 4) {
        ThrowIfBuilt();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleCount);
        _sampleCount = sampleCount;
        return this;
    }

    /// <summary>
    /// Sets the tone mapping applied by the present pass.
    /// </summary>
    /// <param name="mode">A defined <see cref="TonemappingMode"/> value. The default is
    /// <see cref="TonemappingMode.AgX"/>.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not a defined value.</exception>
    /// <exception cref="InvalidOperationException">The builder already built an app.</exception>
    public RayBorkAppBuilder WithTonemapping(TonemappingMode mode = TonemappingMode.AgX) {
        ThrowIfBuilt();
        if (!Enum.IsDefined(mode)) {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "The tone mapping mode is not a defined value.");
        }

        _tonemapping = mode;
        return this;
    }

    /// <summary>
    /// Sets the exposure applied before tone mapping.
    /// </summary>
    /// <param name="ev">The exposure in stops. It must be finite; <c>0</c> is neutral and <c>+1</c>
    /// doubles linear exposure.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ev"/> is not finite.</exception>
    /// <exception cref="InvalidOperationException">The builder already built an app.</exception>
    public RayBorkAppBuilder WithExposure(float ev = 0f) {
        ThrowIfBuilt();
        if (!float.IsFinite(ev)) {
            throw new ArgumentOutOfRangeException(nameof(ev), ev, "The exposure must be finite.");
        }

        _exposure = ev;
        return this;
    }

    /// <summary>
    /// Sets the scene clear color. Until cameras provide their own clear color in M4, this is the
    /// color the scene pass clears to.
    /// </summary>
    /// <param name="color">
    /// The linear RGBA clear color. Every component must be finite. The default is linear opaque
    /// black.
    /// </param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">A component of <paramref name="color"/> is not finite.</exception>
    /// <exception cref="InvalidOperationException">The builder already built an app.</exception>
    public RayBorkAppBuilder WithClearColor(Color color) {
        ThrowIfBuilt();
        if (!float.IsFinite(color.R)
            || !float.IsFinite(color.G)
            || !float.IsFinite(color.B)
            || !float.IsFinite(color.A)) {
            throw new ArgumentException("The clear color components must all be finite.", nameof(color));
        }

        _clearColor = color;
        return this;
    }

    /// <summary>
    /// Sets the tick cap.
    /// </summary>
    /// <param name="framesPerSecond">The cap, or zero for an uncapped loop. It must not be negative.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="framesPerSecond"/> is negative.</exception>
    /// <exception cref="InvalidOperationException">The builder already built an app.</exception>
    public RayBorkAppBuilder WithTargetFps(int framesPerSecond) {
        ThrowIfBuilt();
        ArgumentOutOfRangeException.ThrowIfNegative(framesPerSecond);
        _targetFps = framesPerSecond;
        return this;
    }

    /// <summary>
    /// Sets whether the graphics validation layer is requested. It defaults to enabled in Debug
    /// builds and disabled in Release builds.
    /// </summary>
    /// <param name="enabled">Whether validation is requested.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="InvalidOperationException">The builder already built an app.</exception>
    public RayBorkAppBuilder WithValidation(bool enabled = true) {
        ThrowIfBuilt();
        _validationEnabled = enabled;
        return this;
    }

    /// <summary>
    /// Sets the window state applied at startup.
    /// </summary>
    /// <param name="state">A defined <see cref="WindowState"/> value.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="state"/> is not a defined value.</exception>
    /// <exception cref="InvalidOperationException">The builder already built an app.</exception>
    public RayBorkAppBuilder WithWindowState(WindowState state) {
        ThrowIfBuilt();
        if (!Enum.IsDefined(state)) {
            throw new ArgumentOutOfRangeException(nameof(state), state, "The window state is not a defined value.");
        }

        _windowState = state;
        return this;
    }

    /// <summary>
    /// Supplies a caller-owned logger factory. The app uses it but never disposes it.
    /// </summary>
    /// <param name="loggerFactory">The factory. It must not be <c>null</c>.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="loggerFactory"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">The builder already built an app.</exception>
    public RayBorkAppBuilder WithLoggerFactory(ILoggerFactory loggerFactory) {
        ThrowIfBuilt();
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _loggerFactory = loggerFactory;
        return this;
    }

    /// <summary>
    /// Sets the minimum level of the default logger factory. It has no effect when a caller-owned
    /// factory is supplied.
    /// </summary>
    /// <param name="level">A defined <see cref="LogLevel"/> value.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="level"/> is not a defined value.</exception>
    /// <exception cref="InvalidOperationException">The builder already built an app.</exception>
    public RayBorkAppBuilder WithLogLevel(LogLevel level) {
        ThrowIfBuilt();
        if (!Enum.IsDefined(level)) {
            throw new ArgumentOutOfRangeException(nameof(level), level, "The log level is not a defined value.");
        }

        _logLevel = level;
        return this;
    }

    /// <summary>
    /// Validates the configuration and creates the app.
    /// </summary>
    /// <returns>An app that has not run yet.</returns>
    /// <exception cref="InvalidOperationException">
    /// No game was supplied, or the builder already built an app.
    /// </exception>
    public RayBorkApp Build() {
        return Build(loggerFactory => new SdlPlatform(loggerFactory), TimeProvider.System);
    }

    /// <summary>
    /// Validates the configuration and creates the app with a substituted platform factory and time
    /// provider. Used by tests to avoid SDL and the wall clock.
    /// </summary>
    /// <param name="platformFactory">The platform factory.</param>
    /// <param name="timeProvider">The time provider for the engine clock.</param>
    /// <returns>An app that has not run yet.</returns>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">
    /// No game was supplied, or the builder already built an app.
    /// </exception>
    internal RayBorkApp Build(PlatformFactory platformFactory, TimeProvider timeProvider) {
        ArgumentNullException.ThrowIfNull(platformFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ThrowIfBuilt();
        if (_game is null) {
            throw new InvalidOperationException("A game is required. Call WithGame before Build.");
        }

        AppConfiguration configuration = new() {
            Game = _game,
            Title = _title,
            Width = _width,
            Height = _height,
            Resizable = _resizable,
            MaxDeltaTime = _maxDeltaTime,
            TargetFps = _targetFps,
            WindowState = _windowState,
            RenderSettings = new RenderSettings {
                PresentMode = _presentMode,
                SampleCount = _sampleCount,
                Tonemapping = _tonemapping,
                Exposure = _exposure,
                ValidationEnabled = _validationEnabled,
                ClearColor = _clearColor,
            },
            LoggerFactory = _loggerFactory,
            LogLevel = _logLevel,
        };

        _built = true;
        return new RayBorkApp(configuration, platformFactory, timeProvider);
    }

    private void ThrowIfBuilt() {
        if (_built) {
            throw new InvalidOperationException(
                "This builder already built a RayBorkApp. A builder can be used only once.");
        }
    }
}
