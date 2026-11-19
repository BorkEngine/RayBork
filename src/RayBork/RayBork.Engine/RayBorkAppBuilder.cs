using Microsoft.Extensions.Logging;
using PolyType;
using RayBork.Internal;

namespace RayBork;

/// <summary>
/// Configures and builds a <see cref="RayBorkApp"/>.
/// </summary>
/// <remarks>
/// Single-use and not thread-safe: configure before <see cref="Build()"/>. <see cref="Build()"/> only
/// validates; SDL, the window, and the logger factory are created in <see cref="RayBorkApp.Run"/>.
/// </remarks>
public sealed class RayBorkAppBuilder
{
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
    /// Initializes a builder with the defaults: a <c>1280x720</c> non-resizable window titled
    /// <c>RayBork</c>, a <c>0.25</c> second delta clamp, uncapped ticks, and Information logging.
    /// </summary>
    public RayBorkAppBuilder() { }

    /// <summary>Sets the game whose callbacks run on the tick thread.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="game"/> is <c>null</c>.</exception>
    public RayBorkAppBuilder WithGame(IGame game)
    {
        ThrowIfBuilt();
        ArgumentNullException.ThrowIfNull(game);
        _game = game;
        return this;
    }

    /// <summary>Sets the initial window title; it must not be empty or whitespace.</summary>
    /// <exception cref="ArgumentException"><paramref name="title"/> is empty or whitespace.</exception>
    public RayBorkAppBuilder WithTitle(string title)
    {
        ThrowIfBuilt();
        ArgumentNullException.ThrowIfNull(title);
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException(
                "The window title must not be empty or whitespace.",
                nameof(title)
            );
        }

        _title = title;
        return this;
    }

    /// <summary>Sets the initial client size in logical units; both dimensions must be positive.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is zero or negative.</exception>
    public RayBorkAppBuilder WithSize(int width, int height)
    {
        ThrowIfBuilt();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        _width = width;
        _height = height;
        return this;
    }

    /// <summary>Sets whether the window can be resized by the user.</summary>
    public RayBorkAppBuilder WithResizable(bool resizable = true)
    {
        ThrowIfBuilt();
        _resizable = resizable;
        return this;
    }

    /// <summary>Sets the per-tick delta clamp, in seconds; it must be finite and positive.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="seconds"/> is not finite or not greater than zero.
    /// </exception>
    public RayBorkAppBuilder WithMaxDeltaTime(double seconds)
    {
        ThrowIfBuilt();
        if (!double.IsFinite(seconds) || seconds <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(seconds),
                seconds,
                "The maximum delta time must be finite and greater than zero."
            );
        }

        _maxDeltaTime = seconds;
        return this;
    }

    /// <summary>
    /// Adds one user asset pack after the built-in pack; later packs shadow earlier ones.
    /// </summary>
    /// <param name="path">
    /// The pack path; relative paths resolve against <see cref="AppContext.BaseDirectory"/> when the app
    /// starts, not when <see cref="Build()"/> runs.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty or whitespace.</exception>
    public RayBorkAppBuilder WithAssetsPack(string path)
    {
        ThrowIfBuilt();
        ValidatePackPath(path, nameof(path));
        _assetPackPaths.Add(path);
        return this;
    }

    /// <summary>Adds user asset packs in order; the last pack containing a path wins.</summary>
    /// <exception cref="ArgumentException">An element is empty or whitespace.</exception>
    public RayBorkAppBuilder WithAssetsPacks(params string[] paths)
    {
        ThrowIfBuilt();
        ArgumentNullException.ThrowIfNull(paths);
        foreach (string path in paths)
        {
            ValidatePackPath(path, nameof(paths));
            _assetPackPaths.Add(path);
        }

        return this;
    }

    /// <summary>
    /// Registers a custom data type for <see cref="IAssetCatalog.Load{T}"/> from its compiled
    /// <c>[GenerateShape]</c> MessagePack shape; register it with the bake independently. Registering the
    /// same type again is a no-op, but combining it with <see cref="WithAssetLoader{T}"/> throws.
    /// </summary>
    /// <typeparam name="T">The custom data type to register.</typeparam>
    /// <exception cref="InvalidOperationException">The type already has a different registration.</exception>
    public RayBorkAppBuilder WithAssetType<T>()
        where T : IShapeable<T>
    {
        ThrowIfBuilt();
        Type assetType = typeof(T);
        if (_dataTypes.Contains(assetType))
        {
            return this;
        }

        if (_assetTypes.ContainsKey(assetType))
        {
            throw new InvalidOperationException(
                $"The asset type '{assetType.FullName}' is registered more than once."
            );
        }

        _dataTypes.Add(assetType);
        _assetTypes.Add(
            assetType,
            new LoaderAssetTypeRegistration<T>(
                new MessagePackAssetLoader<T>(),
                "custom data could not be deserialized"
            )
        );
        return this;
    }

    /// <summary>
    /// Registers a custom asset type and its loader; no compiled <c>[GenerateShape]</c> is needed.
    /// </summary>
    /// <remarks>
    /// Each load produces a fresh caller-owned value. The synchronous loader part runs on the dedicated
    /// <c>RayBork.Asset</c> thread, so it must not block or call SDL, Vulkan, or tick-thread APIs; one
    /// instance serves concurrent loads, so it must be stateless or thread-safe. A type registers
    /// once; built-in types are already registered and fail at <see cref="Build()"/>.
    /// </remarks>
    /// <typeparam name="T">The custom asset type to register.</typeparam>
    /// <exception cref="InvalidOperationException">The type is already registered.</exception>
    public RayBorkAppBuilder WithAssetLoader<T>(IAssetLoader<T> loader)
    {
        ThrowIfBuilt();
        ArgumentNullException.ThrowIfNull(loader);
        if (_assetTypes.ContainsKey(typeof(T)))
        {
            throw new InvalidOperationException(
                $"The asset type '{typeof(T).FullName}' is registered more than once."
            );
        }

        _assetTypes.Add(typeof(T), new LoaderAssetTypeRegistration<T>(loader));
        return this;
    }

    /// <summary>Sets the requested presentation mode.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not a defined value.</exception>
    public RayBorkAppBuilder WithPresentMode(PresentMode mode = PresentMode.Mailbox)
    {
        ThrowIfBuilt();
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                "The present mode is not a defined value."
            );
        }

        _presentMode = mode;
        return this;
    }

    /// <summary>
    /// Sets the MSAA sample count; the renderer uses the highest supported count up to it.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sampleCount"/> is zero or negative.</exception>
    public RayBorkAppBuilder WithMSAA(int sampleCount = 4)
    {
        ThrowIfBuilt();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleCount);
        _sampleCount = sampleCount;
        return this;
    }

    /// <summary>Sets the startup tone-mapping mode, fixed for the app lifetime.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not a defined value.</exception>
    public RayBorkAppBuilder WithTonemapping(TonemappingMode mode = TonemappingMode.AgX)
    {
        ThrowIfBuilt();
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                "The tone mapping mode is not a defined value."
            );
        }

        _tonemapping = mode;
        return this;
    }

    /// <summary>
    /// Sets the initial exposure in stops; <c>0</c> is neutral and <c>+1</c> doubles linear exposure.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ev"/> is not finite.</exception>
    public RayBorkAppBuilder WithExposure(float ev = 0f)
    {
        ThrowIfBuilt();
        if (!float.IsFinite(ev))
        {
            throw new ArgumentOutOfRangeException(nameof(ev), ev, "The exposure must be finite.");
        }

        _exposure = ev;
        return this;
    }

    /// <summary>
    /// Sets the initial AgX white mapped to the display maximum; at least <c>2</c>, default <c>16.29</c>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Not finite or less than <c>2</c>.</exception>
    public RayBorkAppBuilder WithAgxWhite(float white = AgxToneMap.DEFAULT_AGX_WHITE)
    {
        ThrowIfBuilt();
        if (!float.IsFinite(white) || white < AgxCurveParameters.MIN_WHITE)
        {
            throw new ArgumentOutOfRangeException(
                nameof(white),
                white,
                "The AgX white must be finite and at least 2."
            );
        }

        _agxWhite = white;
        return this;
    }

    /// <summary>Sets the initial AgX curve contrast; at least <c>1</c>, default <c>1.25</c>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Not finite or less than <c>1</c>.</exception>
    public RayBorkAppBuilder WithAgxContrast(float contrast = AgxToneMap.DEFAULT_AGX_CONTRAST)
    {
        ThrowIfBuilt();
        if (!float.IsFinite(contrast) || contrast < 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(contrast),
                contrast,
                "The AgX contrast must be finite and at least 1."
            );
        }

        _agxContrast = contrast;
        return this;
    }

    /// <summary>
    /// Sets the tick cap, or zero for an uncapped loop. FIFO paces by refresh (a lower cap still sleeps),
    /// while Mailbox and Immediate apply the cap directly.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="framesPerSecond"/> is negative.</exception>
    public RayBorkAppBuilder WithTargetFps(int framesPerSecond)
    {
        ThrowIfBuilt();
        ArgumentOutOfRangeException.ThrowIfNegative(framesPerSecond);
        _targetFps = framesPerSecond;
        return this;
    }

    /// <summary>Sets the graphics validation request; enabled by default in Debug builds.</summary>
    public RayBorkAppBuilder WithValidation(bool enabled = true)
    {
        ThrowIfBuilt();
        _validationEnabled = enabled;
        return this;
    }

    /// <summary>Sets the window state applied at startup.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="state"/> is not a defined value.</exception>
    public RayBorkAppBuilder WithWindowState(WindowState state)
    {
        ThrowIfBuilt();
        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(
                nameof(state),
                state,
                "The window state is not a defined value."
            );
        }

        _windowState = state;
        return this;
    }

    /// <summary>Supplies a caller-owned logger factory that the app never disposes.</summary>
    public RayBorkAppBuilder WithLoggerFactory(ILoggerFactory loggerFactory)
    {
        ThrowIfBuilt();
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _loggerFactory = loggerFactory;
        return this;
    }

    /// <summary>Sets the default logger level; ignored with a caller factory.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="level"/> is not a defined value.</exception>
    public RayBorkAppBuilder WithLogLevel(LogLevel level)
    {
        ThrowIfBuilt();
        if (!Enum.IsDefined(level))
        {
            throw new ArgumentOutOfRangeException(
                nameof(level),
                level,
                "The log level is not a defined value."
            );
        }

        _logLevel = level;
        return this;
    }

    /// <summary>Validates the configuration and creates an app that has not run yet.</summary>
    /// <exception cref="InvalidOperationException">No game was supplied.</exception>
    public RayBorkApp Build()
    {
        return Build(
            loggerFactory => new SdlPlatform(loggerFactory),
            static () => new Vulkan12Backend(),
            TimeProvider.System
        );
    }

    /// <summary>Validates and creates the app with injected factories and clock; used by tests.</summary>
    /// <exception cref="InvalidOperationException">No game was supplied.</exception>
    internal RayBorkApp Build(
        PlatformFactory platformFactory,
        RendererBackendFactory rendererBackendFactory,
        TimeProvider timeProvider
    )
    {
        ArgumentNullException.ThrowIfNull(platformFactory);
        ArgumentNullException.ThrowIfNull(rendererBackendFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ThrowIfBuilt();
        if (_game is null)
        {
            throw new InvalidOperationException("A game is required. Call WithGame before Build.");
        }

        // Validate the merged registry before the app exists: a user registration that collides with
        // a built-in engine asset type fails the duplicate-registration check here.
        _ = new AssetTypeRegistry([.. BuiltInAssetLoaders.CreateDefault(), .. _assetTypes.Values]);

        AppConfiguration configuration = new()
        {
            Game = _game,
            Title = _title,
            Width = _width,
            Height = _height,
            Resizable = _resizable,
            MaxDeltaTime = _maxDeltaTime,
            TargetFps = _targetFps,
            WindowState = _windowState,
            RenderSettings = new RenderSettings
            {
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

    private static void ValidatePackPath(string path, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(path, parameterName);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                "An asset pack path must not be empty or whitespace.",
                parameterName
            );
        }
    }

    private void ThrowIfBuilt()
    {
        if (_built)
        {
            throw new InvalidOperationException(
                "This builder already built a RayBorkApp. A builder can be used only once."
            );
        }
    }
}
