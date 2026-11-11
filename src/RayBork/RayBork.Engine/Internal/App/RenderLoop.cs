using Microsoft.Extensions.Logging;

namespace RayBork.Internal;

/// <summary>
/// Owns the render thread: it creates and initializes the renderer backend, consumes the newest
/// published frame, submits and presents it, and publishes the backend status.
/// </summary>
/// <remarks>
/// <para>
/// The constructor runs on the main thread, and the render thread owns the backend for the rest of
/// its life. The loop never calls user code and never touches the platform directly: surface work is
/// marshalled to the main thread through <see cref="RendererBackendOptions.SurfaceProvider"/>.
/// </para>
/// <para>
/// <see cref="IsReady"/> becomes <see langword="true"/> after the backend initialized and its
/// <see cref="EngineInfo"/> was published, so the main thread can start the tick thread only after
/// <see cref="EngineContext.Info"/> is valid. A failure before that point is an initialization
/// failure; a failure after it is a run-time failure. Exceptions never escape the thread:
/// <see cref="Failure"/> holds them and the backend is disposed in every case.
/// </para>
/// <para>
/// <see cref="Start"/> may be called once. Read <see cref="Failure"/> after <see cref="Join"/>
/// returned or after <see cref="IsCompleted"/> became <see langword="true"/>.
/// </para>
/// </remarks>
internal sealed class RenderLoop {
    private const string THREAD_NAME = "RayBork.Render";

    private readonly FrameRing _frameRing;
    private readonly RendererBackendFactory _backendFactory;
    private readonly RendererBackendOptions _options;
    private readonly RenderStatus _status;
    private readonly Thread _thread;

    private volatile Exception? _failure;
    private volatile bool _isReady;
    private volatile bool _isCompleted;
    private int _started;

    /// <summary>
    /// Initializes the loop without starting the render thread.
    /// </summary>
    /// <param name="frameRing">The consumer side of the frame handoff.</param>
    /// <param name="backendFactory">Creates the backend on the render thread.</param>
    /// <param name="options">The backend configuration and surface seams.</param>
    /// <param name="status">The status holder the loop publishes <see cref="EngineInfo"/> and
    /// <see cref="RenderStats"/> to.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    internal RenderLoop(
        FrameRing frameRing,
        RendererBackendFactory backendFactory,
        RendererBackendOptions options,
        RenderStatus status) {
        ArgumentNullException.ThrowIfNull(frameRing);
        ArgumentNullException.ThrowIfNull(backendFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(status);

        _frameRing = frameRing;
        _backendFactory = backendFactory;
        _options = options;
        _status = status;
        _thread = new Thread(RunThread) {
            IsBackground = true,
            Name = THREAD_NAME,
        };
    }

    /// <summary>
    /// Gets the render thread. It is created by the constructor and runs after <see cref="Start"/>.
    /// </summary>
    internal Thread Thread => _thread;

    /// <summary>
    /// Gets the render thread name.
    /// </summary>
    internal string ThreadName => _thread.Name ?? THREAD_NAME;

    /// <summary>
    /// Gets the unexpected failure that stopped the loop, or <see langword="null"/>. It is an
    /// initialization failure when <see cref="IsReady"/> is still <see langword="false"/>, and a
    /// run-time failure otherwise.
    /// </summary>
    internal Exception? Failure => _failure;

    /// <summary>
    /// Gets whether the backend initialized successfully and published its <see cref="EngineInfo"/>.
    /// </summary>
    internal bool IsReady => _isReady;

    /// <summary>
    /// Gets whether the render thread finished disposing the backend. Read it together with
    /// <see cref="Failure"/> after a bounded join.
    /// </summary>
    internal bool IsCompleted => _isCompleted;

    /// <summary>
    /// Starts the render thread. The loop can be started once.
    /// </summary>
    /// <exception cref="InvalidOperationException">The loop was already started.</exception>
    internal void Start() {
        if (Interlocked.Exchange(ref _started, 1) != 0) {
            throw new InvalidOperationException("The render loop has already been started.");
        }

        _thread.Start();
    }

    /// <summary>
    /// Blocks until the render thread exits. Call it after the frame ring is complete, while the
    /// caller still services <see cref="RendererBackendOptions.SurfaceProvider"/>.
    /// </summary>
    internal void Join() => _thread.Join();

    private void RunThread() {
        IRendererBackend? backend = null;
        try {
            backend = _backendFactory() ?? throw new GraphicsException(
                "The renderer backend factory returned null.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null);
            backend.Initialize(_options);

            EngineInfo publishedInfo = backend.Info;
            _status.SetInfo(publishedInfo);
            _isReady = true;

            while (true) {
                FrameLease? lease = _frameRing.AcquireNewest(CancellationToken.None);
                if (lease is null) {
                    return;
                }

                try {
                    backend.Submit(new RenderFramePacket(lease.Value.FrameIndex));
                    backend.Present();
                    _status.SetStats(backend.Stats);

                    EngineInfo info = backend.Info;
                    if (info != publishedInfo) {
                        _status.SetInfo(info);
                        publishedInfo = info;
                    }
                } finally {
                    _frameRing.Release(lease.Value);
                }
            }
        } catch (Exception exception) {
            _failure = exception;
        } finally {
            try {
                backend?.Dispose();
            } catch (Exception exception) {
                if (_failure is null) {
                    _failure = exception;
                } else {
                    _options.Logger.LogWarning(exception, "The renderer backend failed to dispose after the render loop stopped.");
                }
            }

            _isCompleted = true;
        }
    }
}
