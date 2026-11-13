using Microsoft.Extensions.Logging;
using PolyType;
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

    private IGame? _game;
    private string _title = DEFAULT_TITLE;
    private int _width = DEFAULT_WIDTH;
    private int _height = DEFAULT_HEIGHT;
    private bool _resizable;
    private double _maxDeltaTime = DEFAULT_MAX_DELTA_TIME;
    private readonly List<string> _assetPackPaths = [];
    private readonly Dictionary<Type, AssetTypeRegistration> _assetTypes = [];
    private readonly HashSet<Type> _dataTypes = [];
    private PresentMode _presentMode = DEFAULT_PRESENT_MODE;
    private int _sampleCount = DEFAULT_SAMPLE_COUNT;
    private TonemappingMode _tonemapping = DEFAULT_TONEMAPPING;
    private float _exposure = DEFAULT_EXPOSURE;
    private float _agxWhite = AgxToneMap.DEFAULT_AGX_WHITE;
    private float _agxContrast = AgxToneMap.DEFAULT_AGX_CONTRAST;
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
    /// Adds one user asset pack. Packs mount after the built-in pack in call order, and a later pack
    /// shadows an earlier pack for the same canonical path.
    /// </summary>
    /// <param name="path">
    /// The pack path. It must not be empty or whitespace. Relative paths resolve against
    /// <see cref="AppContext.BaseDirectory"/> when the app starts; <see cref="Build()"/> does not open
    /// the file.
    /// </param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty or whitespace.</exception>
    /// <exception cref="InvalidOperationException">The builder already built an app.</exception>
    public RayBorkAppBuilder WithAssetsPack(string path) {
        ThrowIfBuilt();
        ValidatePackPath(path, nameof(path));
        _assetPackPaths.Add(path);
        return this;
    }

    /// <summary>
    /// Adds user asset packs in order. Each pack mounts after the built-in pack and after the earlier
    /// packs, so the last pack that contains a path wins.
    /// </summary>
    /// <param name="paths">The pack paths. Every path must be non-empty.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">The array or one of its elements is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">An element is empty or whitespace.</exception>
    /// <exception cref="InvalidOperationException">The builder already built an app.</exception>
    public RayBorkAppBuilder WithAssetsPacks(params string[] paths) {
        ThrowIfBuilt();
        ArgumentNullException.ThrowIfNull(paths);
        foreach (string path in paths) {
            ValidatePackPath(path, nameof(paths));
            _assetPackPaths.Add(path);
        }

        return this;
    }

    /// <summary>
    /// Registers a custom data type for <see cref="IAssetCatalog.Load{T}"/>. The type must carry a
    /// compiled <c>[GenerateShape]</c>; the runtime never scans assemblies or uses reflection. The
    /// payload is a MessagePack <see cref="AssetEntryType.Data"/> entry, deserialized through the
    /// compiled shape. The same type must be registered with the bake independently.
    /// </summary>
    /// <remarks>
    /// The type is idempotent: registering it again is a no-op. Registering it with
    /// <see cref="WithAssetLoader{T}"/> as well is an <see cref="InvalidOperationException"/> because
    /// a type may have only one registration. Use <see cref="WithAssetLoader{T}"/> instead when the
    /// payload is not a MessagePack custom-data entry.
    /// </remarks>
    /// <typeparam name="T">The custom data type to register.</typeparam>
    /// <returns>This builder.</returns>
    /// <exception cref="InvalidOperationException">
    /// The builder already built an app, or the type already has a different registration.
    /// </exception>
    public RayBorkAppBuilder WithAssetType<T>()
        where T : IShapeable<T> {
        ThrowIfBuilt();
        Type assetType = typeof(T);
        if (_dataTypes.Contains(assetType)) {
            return this;
        }

        if (_assetTypes.ContainsKey(assetType)) {
            throw new InvalidOperationException(
                $"The asset type '{assetType.FullName}' is registered more than once.");
        }

        _dataTypes.Add(assetType);
        _assetTypes.Add(
            assetType,
            new LoaderAssetTypeRegistration<T>(
                new MessagePackAssetLoader<T>(),
                "custom data could not be deserialized"));
        return this;
    }

    /// <summary>
    /// Registers a custom asset type and the loader that creates its instances for
    /// <see cref="IAssetCatalog.Load{T}"/>. The type does not need a compiled <c>[GenerateShape]</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The loader produces a fresh caller-owned value for every load; the catalog does not cache the
    /// result. The synchronous part of the loader runs on the dedicated <c>RayBork.Asset</c> loader
    /// thread, so it must not block (no <c>.Result</c> or <c>.Wait()</c>) and must not call SDL,
    /// Vulkan, or tick-thread APIs. One instance serves concurrent loads, so it must be stateless or
    /// thread-safe.
    /// </para>
    /// <para>
    /// Each type may be registered only once; a second registration of the same type is an
    /// <see cref="InvalidOperationException"/>. Built-in engine asset types are already registered by
    /// the engine, so registering one fails at <see cref="Build()"/>.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The custom asset type to register.</typeparam>
    /// <param name="loader">The loader that creates instances of <typeparamref name="T"/>.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="loader"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The builder already built an app, or <typeparamref name="T"/> is already registered.
    /// </exception>
    public RayBorkAppBuilder WithAssetLoader<T>(IAssetLoader<T> loader) {
        ThrowIfBuilt();
        ArgumentNullException.ThrowIfNull(loader);
        if (_assetTypes.ContainsKey(typeof(T))) {
            throw new InvalidOperationException(
                $"The asset type '{typeof(T).FullName}' is registered more than once.");
        }

        _assetTypes.Add(typeof(T), new LoaderAssetTypeRegistration<T>(loader));
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
    /// Sets the startup tone mapping applied by the present pass. The mode is fixed for the app
    /// lifetime; <see cref="EngineContext.Tonemapping"/> changes the exposure, AgX white, and AgX
    /// contrast at runtime.
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
    /// Sets the initial runtime exposure applied before tone mapping. The game can change it while it
    /// runs through <see cref="EngineContext.Tonemapping"/>.
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
    /// Sets the initial AgX white that the runtime tone mapping maps to the display maximum. The game
    /// can change it while it runs through <see cref="EngineContext.Tonemapping"/>.
    /// </summary>
    /// <param name="white">
    /// The scene-linear white before the display headroom scaling. It must be finite and at least
    /// <c>2</c>. The default is <c>16.29</c>.
    /// </param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="white"/> is not finite or is less than <c>2</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">The builder already built an app.</exception>
    public RayBorkAppBuilder WithAgxWhite(float white = AgxToneMap.DEFAULT_AGX_WHITE) {
        ThrowIfBuilt();
        if (!float.IsFinite(white) || white < AgxCurveParameters.MIN_WHITE) {
            throw new ArgumentOutOfRangeException(
                nameof(white),
                white,
                "The AgX white must be finite and at least 2.");
        }

        _agxWhite = white;
        return this;
    }

    /// <summary>
    /// Sets the initial AgX curve contrast used by the runtime tone mapping. The game can change it
    /// while it runs through <see cref="EngineContext.Tonemapping"/>.
    /// </summary>
    /// <param name="contrast">The curve contrast. It must be finite and at least <c>1</c>. The default
    /// is <c>1.25</c>.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="contrast"/> is not finite or is less than <c>1</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">The builder already built an app.</exception>
    public RayBorkAppBuilder WithAgxContrast(float contrast = AgxToneMap.DEFAULT_AGX_CONTRAST) {
        ThrowIfBuilt();
        if (!float.IsFinite(contrast) || contrast < 1f) {
            throw new ArgumentOutOfRangeException(
                nameof(contrast),
                contrast,
                "The AgX contrast must be finite and at least 1.");
        }

        _agxContrast = contrast;
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
        return Build(
            loggerFactory => new SdlPlatform(loggerFactory),
            static () => new Vulkan12Backend(),
            TimeProvider.System);
    }

    /// <summary>
    /// Validates the configuration and creates the app with substituted platform and renderer
    /// factories and time provider. Used by tests to avoid SDL, the GPU device, and the wall clock.
    /// </summary>
    /// <param name="platformFactory">The platform factory.</param>
    /// <param name="rendererBackendFactory">The renderer backend factory.</param>
    /// <param name="timeProvider">The time provider for the engine clock.</param>
    /// <returns>An app that has not run yet.</returns>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">
    /// No game was supplied, or the builder already built an app.
    /// </exception>
    internal RayBorkApp Build(
        PlatformFactory platformFactory,
        RendererBackendFactory rendererBackendFactory,
        TimeProvider timeProvider) {
        ArgumentNullException.ThrowIfNull(platformFactory);
        ArgumentNullException.ThrowIfNull(rendererBackendFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ThrowIfBuilt();
        if (_game is null) {
            throw new InvalidOperationException("A game is required. Call WithGame before Build.");
        }

        // Validate the merged registry before the app exists: a user registration that collides with
        // a built-in engine asset type fails the duplicate-registration check here.
        _ = new AssetTypeRegistry([.. BuiltInAssetLoaders.CreateDefault(), .. _assetTypes.Values]);

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
                AgxWhite = _agxWhite,
                AgxContrast = _agxContrast,
                ValidationEnabled = _validationEnabled,
            },
            LoggerFactory = _loggerFactory,
            LogLevel = _logLevel,
            AssetPackPaths = [.. _assetPackPaths],
            AssetTypes = [.. _assetTypes.Values],
        };

        _built = true;
        return new RayBorkApp(configuration, platformFactory, rendererBackendFactory, timeProvider);
    }

    private static void ValidatePackPath(string path, string parameterName) {
        ArgumentNullException.ThrowIfNull(path, parameterName);
        if (string.IsNullOrWhiteSpace(path)) {
            throw new ArgumentException("An asset pack path must not be empty or whitespace.", parameterName);
        }
    }

    private void ThrowIfBuilt() {
        if (_built) {
            throw new InvalidOperationException(
                "This builder already built a RayBorkApp. A builder can be used only once.");
        }
    }
}
