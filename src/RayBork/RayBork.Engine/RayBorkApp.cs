using System.Diagnostics;
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
        TickRunner? tickRunner = null;
        RenderLoop? renderLoop = null;
        bool tickStarted = false;
        bool renderStarted = false;
        Exception? failure = null;
        string? exitReason = null;

        try {
            platform = CreatePlatform(loggerFactory);
            platform.Initialize(CreatePlatformOptions(configuration), RequestExit);

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
            renderLoop = new RenderLoop(frameRing, _rendererBackendFactory, backendOptions, _renderStatus, gpuAssetBridge);
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

            EngineContext context = new(
                platform,
                loggerFactory,
                assetCatalog,
                _exitControl,
                CaptureInput(platform),
                configuration.MaxDeltaTime,
                () => clock.Time,
                _renderStatus);

            tickRunner = new TickRunner(
                platform,
                context,
                configuration.Game,
                clock,
                _timeProvider,
                new TickPacer(configuration.TargetFps),
                frameRing);

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
            tickRunner,
            renderLoop,
            tickStarted,
            renderStarted,
            teardownQueue,
            failure);

        if (failure is not null) {
            logger.LogError(failure, "RayBork stopped with an error.");
        } else {
            logger.LogInformation("RayBork exiting: {ExitReason}.", exitReason ?? "requested");
        }

        if (defaultLoggerFactory is not null) {
            try {
                defaultLoggerFactory.Dispose();
            } catch (Exception exception) {
                // The default factory owns the only logger, so a dispose failure cannot be logged
                // through it; keep it as the failure instead.
                failure ??= exception;
            }
        }

        if (failure is not null) {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
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

    private static Exception? Cleanup(
        ILogger logger,
        IEnginePlatform? platform,
        MainThreadSurfaceChannel? surfaceChannel,
        FrameRing? frameRing,
        AssetCatalog? assetCatalog,
        TickRunner? tickRunner,
        RenderLoop? renderLoop,
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
