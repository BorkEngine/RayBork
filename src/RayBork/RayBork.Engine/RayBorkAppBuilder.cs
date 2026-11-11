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

    private IGame? _game;
    private string _title = DEFAULT_TITLE;
    private int _width = DEFAULT_WIDTH;
    private int _height = DEFAULT_HEIGHT;
    private bool _resizable;
    private double _maxDeltaTime = DEFAULT_MAX_DELTA_TIME;
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
