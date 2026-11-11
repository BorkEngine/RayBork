using Microsoft.Extensions.Logging;

namespace RayBork.Internal;

/// <summary>
/// Owns the render thread: it creates and initializes the renderer backend, services the GPU asset
/// bridge, consumes the newest published frame, submits and presents it, and publishes the backend
/// status.
/// </summary>
/// <remarks>
/// <para>
/// The constructor runs on the main thread, and the render thread owns the backend for the rest of
/// its life. The loop never calls user code and never touches the platform directly: surface work is
/// marshalled to the main thread through <see cref="RendererBackendOptions.SurfaceProvider"/>.
/// </para>
/// <para>
/// The loop waits on the frame-ring ready signal and the GPU asset bridge signal together, so an
/// upload queued during <c>Setup</c> completes without waiting for the first tick and no loop turn
/// busy-spins. The bridge is serviced before each wait and after every wake. When the backend's
/// <see cref="IRendererBackend.DeviceGeneration"/> changes, the loop asks the bridge to recreate the
/// live resources before the next frame is submitted; presentation-only recoveries leave the
/// generation unchanged and do not re-upload assets.
/// </para>
/// <para>
/// <see cref="IsReady"/> becomes <see langword="true"/> after the backend initialized, its
/// <see cref="EngineInfo"/> was published, and the GPU asset worker was attached, so the main thread
/// can start the tick thread only after <see cref="EngineContext.Info"/> is valid and no asset upload
/// can be queued without a worker. A failure before that point is an initialization failure; a
/// failure after it is a run-time failure. Exceptions never escape the thread: <see cref="Failure"/>
/// holds them and the backend is disposed in every case.
/// </para>
/// <para>
/// Teardown order in the loop's final block: the frame ring is already complete and drained, so the
/// bridge closes first (completing pending uploads with the failure or
/// <see cref="ObjectDisposedException"/> and destroying live resources), then the backend is disposed.
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
    private readonly GpuAssetBridge _gpuAssetBridge;
    private readonly WaitHandle[] _waitHandles;
    private readonly Thread _thread;

    private IAssetGpuWorker? _assetGpuWorker;
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
    /// <param name="gpuAssetBridge">
    /// The bridge that receives GPU asset uploads and destructions from any thread and is serviced by
    /// this loop on the render thread.
    /// </param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    internal RenderLoop(
        FrameRing frameRing,
        RendererBackendFactory backendFactory,
        RendererBackendOptions options,
        RenderStatus status,
        GpuAssetBridge gpuAssetBridge) {
        ArgumentNullException.ThrowIfNull(frameRing);
        ArgumentNullException.ThrowIfNull(backendFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(gpuAssetBridge);

        _frameRing = frameRing;
        _backendFactory = backendFactory;
        _options = options;
        _status = status;
        _gpuAssetBridge = gpuAssetBridge;
        _waitHandles = [frameRing.ReadySignal, gpuAssetBridge.Signal];
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
    /// Gets whether the backend initialized successfully, published its <see cref="EngineInfo"/>, and
    /// attached an asset GPU worker when the backend provides one.
    /// </summary>
    internal bool IsReady => _isReady;

    /// <summary>
    /// Gets whether the render thread finished closing the bridge and disposing the backend. Read it
    /// together with <see cref="Failure"/> after a bounded join.
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
            AttachAssetGpuWorker(backend);

            EngineInfo publishedInfo = backend.Info;
            _status.SetInfo(publishedInfo);
            _isReady = true;

            int appliedDeviceGeneration = backend.DeviceGeneration;
            while (true) {
                ProcessPendingAssets();

                if (_frameRing.TryAcquireNewest(out FrameLease lease)) {
                    try {
                        backend.Submit(new RenderFramePacket(lease.FrameIndex));
                        backend.Present();
                        _status.SetStats(backend.Stats);
                        int deviceGeneration = backend.DeviceGeneration;
                        if (deviceGeneration != appliedDeviceGeneration) {
                            appliedDeviceGeneration = deviceGeneration;
                            RestoreAssets();
                        }

                        EngineInfo info = backend.Info;
                        if (info != publishedInfo) {
                            _status.SetInfo(info);
                            publishedInfo = info;
                        }
                    } finally {
                        _frameRing.Release(lease);
                    }

                    continue;
                }

                if (_frameRing.IsCompleted) {
                    return;
                }

                WaitHandle.WaitAny(_waitHandles);
            }
        } catch (Exception exception) {
            _failure = exception;
        } finally {
            try {
                _gpuAssetBridge.Close(_failure);
            } catch (Exception exception) {
                if (_failure is null) {
                    _failure = exception;
                } else {
                    _options.Logger.LogWarning(exception, "The GPU asset bridge failed to close after the render loop stopped.");
                }
            }

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

    private void ProcessPendingAssets() {
        if (_assetGpuWorker is not null) {
            _gpuAssetBridge.ProcessPending(_assetGpuWorker);
        }
    }

    private void RestoreAssets() {
        if (_assetGpuWorker is not null) {
            _gpuAssetBridge.Restore(_assetGpuWorker);
        }
    }

    private void AttachAssetGpuWorker(IRendererBackend backend) {
        if (backend is not IAssetGpuWorkerProvider provider) {
            return;
        }

        IAssetGpuWorker worker = provider.CreateAssetGpuWorker()
            ?? throw new GraphicsException(
                "The renderer backend returned no GPU asset worker.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null);
        _gpuAssetBridge.AttachWorker(worker);
        _assetGpuWorker = worker;
    }
}
