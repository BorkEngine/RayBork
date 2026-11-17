using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;
using RayBork.Internal;

namespace RayBork;

/// <summary>
/// Runs one game: it opens the window, starts the tick and render threads, runs the
/// <see cref="IGame"/> callbacks, and joins every thread before <see cref="Run"/> returns.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Run"/> is main-thread only because SDL3 and the window require the process main
/// thread; the engine documents the rule but does not enforce it. An app runs at most once. Dispose
/// the app after <see cref="Run"/> returns to release the app object.
/// </para>
/// <para>
/// <see cref="RequestExit"/> and <see cref="IsRunning"/> are safe from any thread.
/// <see cref="Dispose"/> from the tick thread throws <see cref="InvalidOperationException"/> instead
/// of deadlocking; call it from another thread.
/// </para>
/// </remarks>
public sealed class RayBorkApp : IDisposable {
    private const string APP_LOGGER_CATEGORY = "RayBork.App";
    private const string GRAPHICS_LOGGER_CATEGORY = "RayBork.Graphics";
    private const string MAIN_THREAD_NAME = "RayBork.Main";
    private const int PUMP_INTERVAL_MILLISECONDS = 4;
    private const int SHUTDOWN_WAIT_SLICE_MILLISECONDS = 1;
    private const int RENDERER_READY_TIMEOUT_SECONDS = 30;
    private const int RENDERER_SHUTDOWN_TIMEOUT_SECONDS = 30;

    private static readonly TimeSpan _pumpInterval = TimeSpan.FromMilliseconds(PUMP_INTERVAL_MILLISECONDS);
    private static readonly TimeSpan _rendererReadyTimeout = TimeSpan.FromSeconds(RENDERER_READY_TIMEOUT_SECONDS);
    private static readonly TimeSpan _rendererShutdownTimeout = TimeSpan.FromSeconds(RENDERER_SHUTDOWN_TIMEOUT_SECONDS);

    private readonly AppConfiguration _configuration;
    private readonly PlatformFactory _platformFactory;
    private readonly RendererBackendFactory _rendererBackendFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ExitControl _exitControl;
    private readonly Lock _gate = new();
    private readonly ManualResetEventSlim _runCompletedSignal = new(false);
    private readonly RenderStatus _renderStatus = new();

    private TickRunner? _tickRunner;
    private RenderLoop? _renderLoop;
    private CameraService? _cameraService;
    private volatile bool _exitRequested;
    private volatile bool _isRunning;
    private volatile bool _isDisposed;
    private bool _runStarted;
    private bool _runCompleted;

    /// <summary>
    /// Initializes the app. The builder is the only public way to create one.
    /// </summary>
    /// <param name="configuration">The validated configuration.</param>
    /// <param name="platformFactory">The platform factory used by <see cref="Run"/>.</param>
    /// <param name="rendererBackendFactory">The renderer backend factory used by <see cref="Run"/>.</param>
    /// <param name="timeProvider">The time provider for the engine clock.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    internal RayBorkApp(
        AppConfiguration configuration,
        PlatformFactory platformFactory,
        RendererBackendFactory rendererBackendFactory,
        TimeProvider timeProvider) {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(platformFactory);
        ArgumentNullException.ThrowIfNull(rendererBackendFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _configuration = configuration;
        _platformFactory = platformFactory;
        _rendererBackendFactory = rendererBackendFactory;
        _timeProvider = timeProvider;
        _exitControl = new ExitControl(this);
    }

    /// <summary>
    /// Gets whether <see cref="Run"/> is executing. It is <see langword="true"/> from the start of
    /// the call, including during startup, until the call returns. Safe to read from any thread.
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// Gets whether the app was disposed. Safe to read from any thread.
    /// </summary>
    public bool IsDisposed => _isDisposed;

    /// <summary>
    /// Gets the validated configuration. Used by tests.
    /// </summary>
    internal AppConfiguration Configuration => _configuration;

    /// <summary>
    /// Gets the status holder that <see cref="EngineContext.Info"/> and
    /// <see cref="EngineContext.Stats"/> read. The render thread writes it.
    /// </summary>
    internal RenderStatus RenderStatus => _renderStatus;

    /// <summary>
    /// Gets the tick thread, or <see langword="null"/> before <see cref="Run"/>. Read it after
    /// <see cref="Run"/> returns to observe that the thread has exited.
    /// </summary>
    internal Thread? TickThread => _tickRunner?.Thread;

    /// <summary>
    /// Gets the app-lifetime camera recording service, or <see langword="null"/> before the service is
    /// created. Used by tests to observe the camera and primitive asset wiring.
    /// </summary>
    internal CameraService? Cameras => _cameraService;

    /// <summary>
    /// Gets the render thread, or <see langword="null"/> before <see cref="Run"/>.
    /// </summary>
    internal Thread? RenderThread => _renderLoop?.Thread;

    /// <summary>
    /// Gets the tick thread name, or <see langword="null"/> before <see cref="Run"/>.
    /// </summary>
    internal string? TickThreadName => TickThread?.Name;

    /// <summary>
    /// Gets the render thread name, or <see langword="null"/> before <see cref="Run"/>.
    /// </summary>
    internal string? RenderThreadName => RenderThread?.Name;

    /// <summary>
    /// Runs the app until <see cref="RequestExit"/>, a window close request, or a failure, then joins
    /// the tick and render threads before returning.
    /// </summary>
    /// <remarks>
    /// Call it on the process main thread. It is legal to call it only once per app and only before
    /// <see cref="Dispose"/>.
    /// </remarks>
    /// <exception cref="InvalidOperationException"><see cref="Run"/> was already called.</exception>
    /// <exception cref="ObjectDisposedException">The app was disposed.</exception>
    /// <exception cref="GameException">A game callback threw.</exception>
    /// <exception cref="PlatformException">The platform failed to start or the event pump failed.</exception>
    /// <exception cref="WindowException">The platform window failed.</exception>
    /// <exception cref="GraphicsException">The renderer failed to initialize or stopped the app.</exception>
    /// <exception cref="RayBorkException">A render-thread or other engine failure stopped the app.</exception>
    public void Run() {
        lock (_gate) {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (_runStarted) {
                throw new InvalidOperationException("Run has already been called. A RayBorkApp runs only once.");
            }

            _runStarted = true;
            _isRunning = true;
        }

        try {
            RunCore();
        } finally {
            _isRunning = false;
            lock (_gate) {
                _runCompleted = true;
            }

            _runCompletedSignal.Set();
        }
    }

    /// <summary>
    /// Requests a graceful exit after the current tick. The request is idempotent, is safe from any
    /// thread, and is honored when it arrives before <see cref="Run"/>.
    /// </summary>
    public void RequestExit() => _exitRequested = true;

    /// <summary>
    /// Stops a running app and waits for <see cref="Run"/> to complete. Idempotent.
    /// </summary>
    /// <remarks>
    /// Dispose runs are supported from another thread, but calling it on the tick thread throws
    /// <see cref="InvalidOperationException"/> because waiting for the tick thread to finish from
    /// inside a game callback would deadlock.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Called on the tick thread.</exception>
    public void Dispose() {
        bool waitForRun;
        lock (_gate) {
            if (_tickRunner is not null && _tickRunner.IsOnTickThread) {
                throw new InvalidOperationException(
                    "Dispose cannot be called on the tick thread; it would wait for the tick thread to stop.");
            }

            _isDisposed = true;
            waitForRun = _runStarted && !_runCompleted;
        }

        if (waitForRun) {
            RequestExit();
            _runCompletedSignal.Wait();
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "A logger-factory dispose failure must be recorded as the run failure instead of replacing it.")]
    [SuppressMessage(
        "Reliability",
        "CA2000",
        Justification = "The default logger factory is disposed by the finally on every path, including a throw from CreateLogger or the run body; the analyzer cannot model the try/catch that keeps a dispose failure from replacing the recorded run failure.")]
    private void RunCore() {
        Thread.CurrentThread.Name ??= MAIN_THREAD_NAME;

        AppConfiguration configuration = _configuration;
        ILoggerFactory? defaultLoggerFactory = null;
        ILoggerFactory loggerFactory;
        if (configuration.LoggerFactory is not null) {
            loggerFactory = configuration.LoggerFactory;
        } else {
            defaultLoggerFactory = LoggingBootstrap.CreateDefault(configuration.LogLevel);
            loggerFactory = defaultLoggerFactory;
        }

        Exception? failure = null;
        try {
            failure = RunCoreBody(configuration, loggerFactory);
        } finally {
            if (defaultLoggerFactory is not null) {
                try {
                    defaultLoggerFactory.Dispose();
                } catch (Exception exception) {
                    // The default factory owns the only logger, so a dispose failure cannot be logged
                    // through it. It becomes the reported failure only when the run itself succeeded.
                    failure ??= exception;
                }
            }
        }

        if (failure is not null) {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    /// <summary>
    /// Runs one app lifetime after the logger factory exists and returns the failure it ended with.
    /// </summary>
    /// <param name="configuration">The app configuration.</param>
    /// <param name="loggerFactory">
    /// The factory that creates the app logger. The caller owns it when it is the default factory.
    /// </param>
    /// <returns>The failure that stopped the run, or <see langword="null"/> for a graceful exit.</returns>
    /// <remarks>
    /// An unexpected logger, cleanup, or final-log exception is not converted to a failure here; it
    /// propagates to the caller, whose <c>finally</c> still disposes the default logger factory.
    /// </remarks>
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "The app converts any main-loop or startup failure into the reported run failure instead of crashing the host.")]
    private Exception? RunCoreBody(AppConfiguration configuration, ILoggerFactory loggerFactory) {
        ILogger logger = loggerFactory.CreateLogger(APP_LOGGER_CATEGORY);
        logger.LogInformation(
            "RayBork {EngineVersion} starting: title '{Title}', client size {Width}x{Height}.",
            EngineVersion.Current,
            configuration.Title,
            configuration.Width,
            configuration.Height);

        EngineClock clock = new(_timeProvider, configuration.MaxDeltaTime);
        clock.Start();

        MainThreadTeardownQueue teardownQueue = new();
        IEnginePlatform? platform = null;
        MainThreadSurfaceChannel? surfaceChannel = null;
        FrameRing? frameRing = null;
        AssetCatalog? assetCatalog = null;
        EngineDefaults? engineDefaults = null;
        PrimitiveAssets? primitiveAssets = null;
        SsaoAssets? ssaoAssets = null;
        BloomAssets? bloomAssets = null;
        CameraService? cameraService = null;
        TickRunner? tickRunner = null;
        RenderLoop? renderLoop = null;
        RenderTextureService? renderTextures = null;
        bool tickStarted = false;
        bool renderStarted = false;
        Exception? failure = null;
        string? exitReason = null;

        try {
            platform = CreatePlatform(loggerFactory);
            platform.Initialize(CreatePlatformOptions(configuration), RequestExit);
            cameraService = new CameraService(platform.Window);

            // One app-lifetime settings object is shared by the tick thread (live values) and the
            // camera service (per-frame snapshot). It is created and attached on this thread before
            // the tick thread exists, so neither side races the constructor.
            TonemappingSettings tonemapping = new(
                configuration.RenderSettings.Exposure,
                configuration.RenderSettings.AgxWhite,
                configuration.RenderSettings.AgxContrast);
            cameraService.AttachTonemapping(tonemapping);
            _cameraService = cameraService;

            // The platform owns the window, so the instance extensions and the native surface must be
            // obtained and created on this thread. The render thread owns the instance and is waiting
            // for the surface before it can initialize the device.
            IReadOnlyList<string> instanceExtensions = platform.GetVulkanInstanceExtensions();
            surfaceChannel = new MainThreadSurfaceChannel();
            RendererBackendOptions backendOptions = CreateRendererBackendOptions(
                configuration,
                platform,
                loggerFactory,
                instanceExtensions,
                surfaceChannel,
                teardownQueue);

            frameRing = new FrameRing();
            GpuAssetBridge gpuAssetBridge = new();
            renderTextures = new RenderTextureService();
            renderLoop = new RenderLoop(
                frameRing,
                _rendererBackendFactory,
                backendOptions,
                _renderStatus,
                gpuAssetBridge,
                renderTextures);
            _renderLoop = renderLoop;
            renderLoop.Start();
            renderStarted = true;
            WaitForRenderer(platform, surfaceChannel, renderLoop);

            // The renderer is ready and its GPU asset worker is attached, so asset loads can queue
            // uploads and Setup can await them. A pack failure here is a startup failure before any
            // game callback runs.
            assetCatalog = new AssetCatalog(
                loggerFactory,
                gpuAssetBridge,
                BuiltInAssetPack.Bytes,
                configuration.AssetPackPaths,
                configuration.AssetTypes);
            assetCatalog.Start();

            // The built-in defaults are engine-owned assets the game reads instead of loading them
            // itself. They must be ready before Setup and stay referenced until shutdown.
            engineDefaults = LoadEngineDefaults(assetCatalog);

            // The engine primitive meshes and materials are required built-ins too, and they must be
            // recordable from the first tick. Attach them before the tick thread starts; the primitive
            // and MSDF programs plus the three engine materials load here, while the mesh program is
            // shared with the defaults.
            primitiveAssets = LoadPrimitiveAssets(assetCatalog);
            cameraService.AttachPrimitives(primitiveAssets);

            // The built-in SSAO shader variants and blue-noise texture are engine-owned, so the app
            // loads them before Setup and attaches them to the camera service. The effect snapshot only
            // ever reads these preloaded assets on the tick thread.
            ssaoAssets = LoadSsaoAssets(assetCatalog);
            cameraService.AttachSsaoAssets(ssaoAssets);

            // The built-in bloom pyramid shaders are engine-owned too; the app preloads them before
            // Setup so a BloomEffect capture never loads an asset on the render thread.
            bloomAssets = LoadBloomAssets(assetCatalog);
            cameraService.AttachBloomAssets(bloomAssets);

            EngineContext context = new(
                platform,
                loggerFactory,
                assetCatalog,
                engineDefaults,
                _exitControl,
                CaptureInput(platform),
                configuration.MaxDeltaTime,
                () => clock.Time,
                _renderStatus,
                cameraService,
                tonemapping,
                renderTextures);

            tickRunner = new TickRunner(
                platform,
                context,
                configuration.Game,
                clock,
                _timeProvider,
                new TickPacer(configuration.TargetFps),
                frameRing,
                cameraService);

            _tickRunner = tickRunner;
            tickRunner.Start();
            tickStarted = true;

            while (true) {
                try {
                    surfaceChannel.TryService(platform);
                    platform.Pump(_pumpInterval);
                } catch (Exception exception) {
                    failure = exception is RayBorkException
                        ? exception
                        : new PlatformException("The platform event pump failed.", exception);
                    exitReason = "the platform event pump failed";
                    break;
                }

                if (_exitRequested) {
                    exitReason = "exit requested";
                    break;
                }

                if (tickRunner.IsCompleted) {
                    exitReason = "the tick loop completed";
                    break;
                }

                if (renderLoop.Failure is not null) {
                    exitReason = "the render loop failed";
                    break;
                }
            }
        } catch (Exception exception) {
            failure = exception is RayBorkException
                ? exception
                : new PlatformException("The app failed during startup.", exception);
            exitReason = "startup failed";
        }

        failure = Cleanup(
            logger,
            platform,
            surfaceChannel,
            frameRing,
            assetCatalog,
            engineDefaults,
            primitiveAssets,
            ssaoAssets,
            bloomAssets,
            cameraService,
            tickRunner,
            renderLoop,
            renderTextures,
            tickStarted,
            renderStarted,
            teardownQueue,
            failure);

        if (failure is not null) {
            logger.LogError(failure, "RayBork stopped with an error.");
        } else {
            logger.LogInformation("RayBork exiting: {ExitReason}.", exitReason ?? "requested");
        }

        return failure;
    }

    private IEnginePlatform CreatePlatform(ILoggerFactory loggerFactory) {
        return _platformFactory(loggerFactory)
            ?? throw new PlatformException("The platform factory returned null.");
    }

    private static PlatformOptions CreatePlatformOptions(AppConfiguration configuration) {
        return new PlatformOptions {
            Title = configuration.Title,
            Width = configuration.Width,
            Height = configuration.Height,
            Resizable = configuration.Resizable,
            WindowState = configuration.WindowState,
            EnableVulkan = true,
        };
    }

    private static RendererBackendOptions CreateRendererBackendOptions(
        AppConfiguration configuration,
        IEnginePlatform platform,
        ILoggerFactory loggerFactory,
        IReadOnlyList<string> instanceExtensions,
        IRendererSurfaceProvider surfaceProvider,
        MainThreadTeardownQueue teardownQueue) {
        IPlatformWindow window = platform.Window;
        return new RendererBackendOptions {
            Settings = configuration.RenderSettings,
            InstanceExtensions = instanceExtensions,
            SurfaceProvider = surfaceProvider,
            WindowStateProvider = () => new WindowSurfaceState(
                window.RenderWidth,
                window.RenderHeight,
                window.IsMinimized,
                window.IsFocused,
                platform.WindowHdrState),
            Logger = loggerFactory.CreateLogger(GRAPHICS_LOGGER_CATEGORY),
            TeardownQueue = teardownQueue,
        };
    }

    private static void WaitForRenderer(
        IEnginePlatform platform,
        MainThreadSurfaceChannel surfaceChannel,
        RenderLoop renderLoop) {
        long startedAt = Stopwatch.GetTimestamp();
        while (!renderLoop.IsReady) {
            if (renderLoop.IsCompleted) {
                throw CreateRendererStartupFailure(renderLoop.Failure);
            }

            if (Stopwatch.GetElapsedTime(startedAt) > _rendererReadyTimeout) {
                throw new GraphicsException(
                    $"The renderer did not become ready within {RENDERER_READY_TIMEOUT_SECONDS} seconds.",
                    DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                    null);
            }

            surfaceChannel.TryService(platform);
            platform.Pump(_pumpInterval);
        }
    }

    private static RayBorkException CreateRendererStartupFailure(Exception? failure) {
        if (failure is RayBorkException rayBorkFailure) {
            return rayBorkFailure;
        }

        return new GraphicsException(
            "The renderer backend failed to initialize.",
            DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
            failure);
    }

    private static IInput CaptureInput(IEnginePlatform platform) {
        return platform.CaptureInput()
            ?? throw new PlatformException("The platform returned a null input snapshot.");
    }

    /// <summary>
    /// Preloads the engine's built-in default assets before <see cref="IGame.Setup"/> runs.
    /// </summary>
    /// <remarks>
    /// The tick thread has not started yet, so blocking the main thread on the loads is safe: the
    /// loader and the render thread service the reads and uploads. <see cref="EngineDefaults.LoadAsync"/>
    /// already reports a missing or corrupt built-in default as an <see cref="AssetLoadException"/>.
    /// </remarks>
    private static EngineDefaults LoadEngineDefaults(IAssetCatalog assets)
        => EngineDefaults.LoadAsync(assets).GetAwaiter().GetResult();

    /// <summary>
    /// Builds the engine-owned primitive meshes and materials before <see cref="IGame.Setup"/> runs.
    /// </summary>
    /// <remarks>
    /// The tick thread has not started yet, so blocking the main thread on the loads is safe: the
    /// loader and the render thread service the reads and uploads. A missing or corrupt built-in is
    /// reported as an <see cref="AssetLoadException"/> by <see cref="PrimitiveAssets.LoadAsync"/>.
    /// </remarks>
    [SuppressMessage(
        "Reliability",
        "CA2000",
        Justification = "The returned PrimitiveAssets is stored by RunCore and disposed by the cleanup path that owns it.")]
    private static PrimitiveAssets LoadPrimitiveAssets(IAssetCatalog assets)
        => PrimitiveAssets.LoadAsync(assets).GetAwaiter().GetResult();

    /// <summary>
    /// Loads the engine-owned SSAO shader variants and blue-noise texture before <see cref="IGame.Setup"/>
    /// runs.
    /// </summary>
    /// <remarks>
    /// The tick thread has not started yet, so blocking on the loads is safe: the loader and render
    /// thread service the reads and uploads. A missing or corrupt built-in is reported as an
    /// <see cref="AssetLoadException"/> by <see cref="SsaoAssets.LoadAsync"/>.
    /// </remarks>
    [SuppressMessage(
        "Reliability",
        "CA2000",
        Justification = "The returned SsaoAssets is stored by RunCore and disposed by the cleanup path that owns it.")]
    private static SsaoAssets LoadSsaoAssets(IAssetCatalog assets)
        => SsaoAssets.LoadAsync(assets).GetAwaiter().GetResult();

    /// <summary>
    /// Loads the engine-owned bloom pyramid shader programs before <see cref="IGame.Setup"/> runs.
    /// </summary>
    /// <remarks>
    /// The tick thread has not started yet, so blocking on the loads is safe: the loader and render
    /// thread service the reads and uploads. A missing or corrupt built-in is reported as an
    /// <see cref="AssetLoadException"/> by <see cref="BloomAssets.LoadAsync"/>.
    /// </remarks>
    [SuppressMessage(
        "Reliability",
        "CA2000",
        Justification = "The returned BloomAssets is stored by RunCore and disposed by the cleanup path that owns it.")]
    private static BloomAssets LoadBloomAssets(IAssetCatalog assets)
        => BloomAssets.LoadAsync(assets).GetAwaiter().GetResult();

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Cleanup keeps going after a failed step: the failure is recorded and the remaining steps still run.")]
    private static Exception? Cleanup(
        ILogger logger,
        IEnginePlatform? platform,
        MainThreadSurfaceChannel? surfaceChannel,
        FrameRing? frameRing,
        AssetCatalog? assetCatalog,
        EngineDefaults? engineDefaults,
        PrimitiveAssets? primitiveAssets,
        SsaoAssets? ssaoAssets,
        BloomAssets? bloomAssets,
        CameraService? cameraService,
        TickRunner? tickRunner,
        RenderLoop? renderLoop,
        RenderTextureService? renderTextures,
        bool tickStarted,
        bool renderStarted,
        MainThreadTeardownQueue teardownQueue,
        Exception? failure) {
        if (tickRunner is not null && tickStarted) {
            failure = RunCleanupStep(logger, "stopping the tick runner", failure, tickRunner.Stop);

            bool pumpSuspended = false;
            while (!tickRunner.IsCompleted) {
                if (platform is not null && !pumpSuspended) {
                    try {
                        platform.Pump(_pumpInterval);
                    } catch (Exception exception) {
                        pumpSuspended = true;
                        failure = RecordCleanupFailure(
                            logger,
                            "pumping the platform while the tick runner stopped",
                            exception,
                            failure);
                    }
                } else {
                    Thread.Sleep(SHUTDOWN_WAIT_SLICE_MILLISECONDS);
                }
            }

            failure = RunCleanupStep(logger, "joining the tick thread", failure, tickRunner.Join);

            if (tickRunner.Failure is not null) {
                if (failure is null) {
                    failure = tickRunner.Failure;
                } else {
                    logger.LogError(tickRunner.Failure, "The tick runner also failed while the app was stopping.");
                }
            }
        }

        // Shutdown already ran on the tick thread, so the game can no longer request a render texture.
        // Stop rejects later requests and fails the queued ones; the render thread still services the
        // target destructions until it joins.
        if (renderTextures is not null) {
            failure = RunCleanupStep(
                logger,
                "stopping the render texture service",
                failure,
                renderTextures.Stop);
        }

        // Shutdown already ran on the tick thread, so the game can no longer use the defaults. Release
        // the engine's owner references before the catalog force-disposes whatever a leaked game
        // reference still holds.
        if (engineDefaults is not null) {
            failure = RunCleanupStep(
                logger,
                "releasing the engine default assets",
                failure,
                engineDefaults.Dispose);
        }

        // The SSAO assets are engine-owned built-ins. Detach them before releasing their owner
        // references so a later camera cannot capture an effect over disposed shader programs.
        if (ssaoAssets is not null) {
            failure = RunCleanupStep(
                logger,
                "releasing the engine SSAO assets",
                failure,
                () => {
                    cameraService?.DetachSsaoAssets();
                    ssaoAssets.Dispose();
                });
        }

        // The bloom assets are engine-owned built-ins too. Detach them before releasing their owner
        // references so a later camera cannot capture a BloomEffect over disposed programs.
        if (bloomAssets is not null) {
            failure = RunCleanupStep(
                logger,
                "releasing the engine bloom assets",
                failure,
                () => {
                    cameraService?.DetachBloomAssets();
                    bloomAssets.Dispose();
                });
        }

        // The primitive assets are engine-owned and required built-ins. Detach them from the camera
        // service before releasing their owner references so no later camera can reach a disposed mesh.
        if (primitiveAssets is not null) {
            failure = RunCleanupStep(
                logger,
                "releasing the engine primitive assets",
                failure,
                () => {
                    cameraService?.DetachPrimitives();
                    primitiveAssets.Dispose();
                });
        }

        // Shutdown already ran on the tick thread, so no game callback can still load an asset. Stop
        // the loader before the frame ring completes: the render thread must stay alive to service the
        // GPU destructions the catalog queues for its cached assets.
        if (assetCatalog is not null) {
            failure = RunCleanupStep(logger, "stopping the asset catalog", failure, assetCatalog.Stop);
        }

        if (frameRing is not null) {
            failure = RunCleanupStep(logger, "completing the frame ring", failure, frameRing.Complete);
        }

        if (renderLoop is not null && renderStarted) {
            failure = JoinRenderLoop(logger, platform, surfaceChannel, renderLoop, failure);

            if (renderLoop.Failure is not null) {
                Exception renderFailure = renderLoop.Failure is RayBorkException
                    ? renderLoop.Failure
                    : new RayBorkException("The render loop failed.", renderLoop.Failure);
                if (failure is null) {
                    failure = renderFailure;
                } else if (!ReferenceEquals(renderLoop.Failure, failure)) {
                    logger.LogError(renderLoop.Failure, "The render loop also failed while the app was stopping.");
                }
            }
        }

        if (surfaceChannel is not null) {
            failure = RunCleanupStep(logger, "closing the surface channel", failure, surfaceChannel.Close);
        }

        // The render thread has stopped, so no request can be serviced. The wait handle is released
        // after the loop that waited on it joined.
        if (renderTextures is not null) {
            failure = RunCleanupStep(
                logger,
                "disposing the render texture service",
                failure,
                renderTextures.Dispose);
        }

        // The render thread has stopped, so the deferred teardown can run on this thread now. It must
        // happen before the platform destroys the window that the teardown may still reference.
        failure = RunCleanupStep(
            logger,
            "running the main-thread teardown queue",
            failure,
            () => teardownQueue.RunPending(logger));

        if (platform is not null) {
            failure = RunCleanupStep(logger, "disposing the platform", failure, platform.Dispose);
        }

        return failure;
    }

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Waiting for the render thread must survive a failing platform pump and still join or time out.")]
    private static Exception? JoinRenderLoop(
        ILogger logger,
        IEnginePlatform? platform,
        MainThreadSurfaceChannel? surfaceChannel,
        RenderLoop renderLoop,
        Exception? failure) {
        long startedAt = Stopwatch.GetTimestamp();
        bool pumpSuspended = false;
        while (!renderLoop.Thread.Join(SHUTDOWN_WAIT_SLICE_MILLISECONDS)) {
            // The backend destroys its native surface from the render thread during disposal, so the
            // main thread must keep servicing surface requests until the thread exits.
            if (platform is not null) {
                if (surfaceChannel is not null) {
                    surfaceChannel.TryService(platform);
                }

                if (!pumpSuspended) {
                    try {
                        platform.Pump(_pumpInterval);
                    } catch (Exception exception) {
                        pumpSuspended = true;
                        failure = RecordCleanupFailure(
                            logger,
                            "pumping the platform while the render thread stopped",
                            exception,
                            failure);
                    }
                }
            }

            if (Stopwatch.GetElapsedTime(startedAt) > _rendererShutdownTimeout) {
                failure = RecordCleanupFailure(
                    logger,
                    "waiting for the render thread to stop",
                    new GraphicsException(
                        $"The render thread did not stop within {RENDERER_SHUTDOWN_TIMEOUT_SECONDS} seconds.",
                        DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                        null),
                    failure);
                break;
            }
        }

        return failure;
    }

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "A cleanup step failure is recorded and returned; cleanup must continue with the next step.")]
    private static Exception? RunCleanupStep(ILogger logger, string operation, Exception? failure, Action action) {
        try {
            action();
        } catch (Exception exception) {
            return RecordCleanupFailure(logger, operation, exception, failure);
        }

        return failure;
    }

    private static Exception? RecordCleanupFailure(ILogger logger, string operation, Exception exception, Exception? failure) {
        logger.LogError(exception, "Cleanup failed while {Operation}.", operation);
        return failure ?? exception;
    }

    /// <summary>
    /// Bridges <see cref="EngineContext"/> to the app exit flag without exposing the internal
    /// <see cref="IAppControl"/> on the public app type.
    /// </summary>
    private sealed class ExitControl : IAppControl {
        private readonly RayBorkApp _app;

        internal ExitControl(RayBorkApp app) {
            _app = app;
        }

        public void RequestExit() => _app.RequestExit();

        public bool IsExitRequested => _app._exitRequested;
    }
}
