using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace RayBork.Internal;

/// <summary>
/// Owns the render thread: backend lifecycle, GPU asset and render-texture servicing, and frame
/// presentation.
/// </summary>
/// <remarks>
/// The constructor runs on the main thread; the render thread owns the backend afterwards and
/// marshals surface work to the main thread through
/// <see cref="RendererBackendOptions.SurfaceProvider"/>. Exceptions never escape the thread:
/// <see cref="Failure"/> holds them, and the backend is disposed in every case.
/// </remarks>
internal sealed class RenderLoop
{
    private const string THREAD_NAME = "RayBork.Render";

    /// <summary>
    /// The bounded wait, in milliseconds, used while GPU asset or render-texture work is pending.
    /// </summary>
    private const int UPLOAD_POLL_TIMEOUT_MILLISECONDS = 2;

    private readonly FrameRing _frameRing;
    private readonly RendererBackendFactory _backendFactory;
    private readonly RendererBackendOptions _options;
    private readonly RenderStatus _status;
    private readonly GpuAssetBridge _gpuAssetBridge;
    private readonly RenderTextureService _renderTextures;
    private readonly WaitHandle[] _waitHandles;
    private readonly Thread _thread;

    private IAssetGpuWorker? _assetGpuWorker;
    private volatile Exception? _failure;
    private volatile bool _isReady;
    private volatile bool _isCompleted;
    private int _started;

    /// <summary>Initializes the loop without starting the render thread.</summary>
    /// <param name="frameRing">The consumer side of the frame handoff.</param>
    /// <param name="backendFactory">Creates the backend on the render thread.</param>
    /// <param name="options">The backend configuration and surface seams.</param>
    /// <param name="status">Publishes <see cref="EngineInfo"/> and <see cref="RenderStats"/>.</param>
    /// <param name="gpuAssetBridge">The bridge for GPU asset uploads from any thread.</param>
    /// <param name="renderTextures">The render-texture queue serviced before each frame.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    internal RenderLoop(
        FrameRing frameRing,
        RendererBackendFactory backendFactory,
        RendererBackendOptions options,
        RenderStatus status,
        GpuAssetBridge gpuAssetBridge,
        RenderTextureService renderTextures
    )
    {
        ArgumentNullException.ThrowIfNull(frameRing);
        ArgumentNullException.ThrowIfNull(backendFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(gpuAssetBridge);
        ArgumentNullException.ThrowIfNull(renderTextures);

        _frameRing = frameRing;
        _backendFactory = backendFactory;
        _options = options;
        _status = status;
        _gpuAssetBridge = gpuAssetBridge;
        _renderTextures = renderTextures;
        _waitHandles = [frameRing.ReadySignal, gpuAssetBridge.Signal, renderTextures.Signal];
        _thread = new Thread(RunThread) { IsBackground = true, Name = THREAD_NAME };
    }

    /// <summary>Gets the render thread, which runs after <see cref="Start"/>.</summary>
    internal Thread Thread => _thread;

    /// <summary>Gets the render thread name.</summary>
    internal string ThreadName => _thread.Name ?? THREAD_NAME;

    /// <summary>Gets the failure that stopped the loop, or <see langword="null"/>.</summary>
    internal Exception? Failure => _failure;

    /// <summary>Gets whether the backend initialized and the loop is ready to tick.</summary>
    internal bool IsReady => _isReady;

    /// <summary>Gets whether the render thread finished teardown.</summary>
    internal bool IsCompleted => _isCompleted;

    /// <summary>Starts the render thread, which can run only once.</summary>
    /// <exception cref="InvalidOperationException">The loop was already started.</exception>
    internal void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("The render loop has already been started.");
        }

        _thread.Start();
    }

    /// <summary>
    /// Blocks until the render thread exits; call it after the frame ring completes while still
    /// servicing <see cref="RendererBackendOptions.SurfaceProvider"/>.
    /// </summary>
    internal void Join() => _thread.Join();

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "The render thread records the failure and stops; an exception may not escape the thread and kill the process."
    )]
    private void RunThread()
    {
        IRendererBackend? backend = null;
        try
        {
            backend = _backendFactory()
                ?? throw new GraphicsException(
                    "The renderer backend factory returned null.",
                    DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                    null
                );
            backend.Initialize(_options);
            AttachAssetGpuWorker(backend);

            EngineInfo publishedInfo = backend.Info;
            _status.SetInfo(publishedInfo);
            _isReady = true;

            int appliedDeviceGeneration = backend.DeviceGeneration;
            while (true)
            {
                ProcessPendingAssets();
                backend.ProcessRenderTextureRequests(_renderTextures);

                if (_frameRing.TryAcquireNewest(out FrameLease lease))
                {
                    try
                    {
                        RenderFramePacket packet = lease.Packet
                            ?? throw new InvalidOperationException(
                                "The frame ring returned a rendering lease without a frame packet."
                            );
                        backend.Submit(packet);
                        backend.Present();
                        _status.SetStats(backend.Stats);
                        int deviceGeneration = backend.DeviceGeneration;
                        if (deviceGeneration != appliedDeviceGeneration)
                        {
                            appliedDeviceGeneration = deviceGeneration;
                            RestoreAssets();
                        }

                        EngineInfo info = backend.Info;
                        if (info != publishedInfo)
                        {
                            _status.SetInfo(info);
                            publishedInfo = info;
                        }
                    }
                    finally
                    {
                        // The presentation attempt is finished for this frame even when Submit or
                        // Present threw, so a FIFO-paced tick that is waiting for it is released
                        // before the render loop's failure stops the app.
                        _frameRing.MarkPresented(lease.FrameIndex);
                        _frameRing.Release(lease);
                    }

                    continue;
                }

                if (_frameRing.IsCompleted)
                {
                    return;
                }

                // An upload in flight must be polled even when no frame is published, and a destroy
                // deferred by an in-flight frame must be retried until the frame fences signal; the
                // bridge signal covers queues, but neither a GPU copy finishing nor a fence retiring
                // a frame signals. A minimized window therefore keeps polling instead of stranding
                // the work.
                int timeout =
                    _gpuAssetBridge.HasPendingWork
                    || _gpuAssetBridge.HasPendingDestroys
                    || _renderTextures.HasPendingWork
                        ? UPLOAD_POLL_TIMEOUT_MILLISECONDS
                        : Timeout.Infinite;
                WaitHandle.WaitAny(_waitHandles, timeout);
            }
        }
        catch (Exception exception)
        {
            _failure = exception;
        }
        finally
        {
            WaitForFrameResourcesIdle();
            try
            {
                _renderTextures.Close(_failure);
            }
            catch (Exception exception)
            {
                if (_failure is null)
                {
                    _failure = exception;
                }
                else
                {
                    _options.Logger.LogWarning(
                        exception,
                        "The render texture service failed to close after the render loop stopped."
                    );
                }
            }

            try
            {
                _gpuAssetBridge.Close(_failure);
            }
            catch (Exception exception)
            {
                if (_failure is null)
                {
                    _failure = exception;
                }
                else
                {
                    _options.Logger.LogWarning(
                        exception,
                        "The GPU asset bridge failed to close after the render loop stopped."
                    );
                }
            }

            try
            {
                backend?.Dispose();
            }
            catch (Exception exception)
            {
                if (_failure is null)
                {
                    _failure = exception;
                }
                else
                {
                    _options.Logger.LogWarning(
                        exception,
                        "The renderer backend failed to dispose after the render loop stopped."
                    );
                }
            }

            _isCompleted = true;
        }
    }

    private void ProcessPendingAssets()
    {
        if (_assetGpuWorker is not null)
        {
            _gpuAssetBridge.ProcessPending(_assetGpuWorker);
        }
    }

    /// <summary>
    /// Waits for every submitted frame to complete before the bridge destroys live resources.
    /// </summary>
    /// <remarks>A lost device cannot signal its fences; shutdown logs and continues.</remarks>
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "The drain runs during teardown, after the backend may already be gone; a failure must not prevent the loop from stopping."
    )]
    private void WaitForFrameResourcesIdle()
    {
        if (_assetGpuWorker is null)
        {
            return;
        }

        try
        {
            _assetGpuWorker.WaitForFrameResourcesIdle();
        }
        catch (Exception exception)
        {
            _options.Logger.LogWarning(
                exception,
                "The GPU asset worker could not wait for frame idleness before the bridge closed; resources are destroyed best effort."
            );
        }
    }

    private void RestoreAssets()
    {
        if (_assetGpuWorker is not null)
        {
            _gpuAssetBridge.Restore(_assetGpuWorker);
        }
    }

    private void AttachAssetGpuWorker(IRendererBackend backend)
    {
        if (backend is not IAssetGpuWorkerProvider provider)
        {
            return;
        }

        IAssetGpuWorker worker = provider.CreateAssetGpuWorker()
            ?? throw new GraphicsException(
                "The renderer backend returned no GPU asset worker.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null
            );
        _gpuAssetBridge.AttachWorker(worker);
        _assetGpuWorker = worker;
    }
}
