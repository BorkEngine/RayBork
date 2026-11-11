using RayBork.Internal;
using RayBork.Tests.Assets.Gpu;

namespace RayBork.Tests;

/// <summary>
/// A fake <see cref="IRendererBackend"/> that records lifecycle calls and exercises the real
/// render-thread-to-main-thread surface handshake without a GPU.
/// </summary>
/// <remarks>
/// <see cref="Initialize"/> creates the native surface through the configured provider exactly like
/// the Vulkan backend, so app tests drive <c>MainThreadSurfaceChannel</c>. <see cref="Dispose"/>
/// destroys that surface first, then enqueues <see cref="TeardownAction"/> on the options' main-thread
/// teardown queue, and is idempotent. Every recorded value except the configured properties is
/// guarded by a lock, so a test may read it after <see cref="RayBorkApp.Run"/> returns.
/// </remarks>
internal sealed class FakeRendererBackend : IRendererBackend, IAssetGpuWorkerProvider {
    private static readonly IntPtr _instanceHandle = new(0x5EED);

    private readonly Lock _gate = new();
    private readonly List<long> _submitFrames = [];

    private RendererBackendOptions? _options;
    private IntPtr _surfaceHandle;
    private bool _surfaceDestroyed;
    private int _initializeCallCount;
    private int _presentCount;
    private int _recoverCalls;
    private int _disposeCallCount;
    private WindowSurfaceState _lastWindowState;

    /// <inheritdoc />
    public EngineInfo Info { get; set; } = new(
        "RayBork",
        EngineVersion.Current,
        "Fake 1.0",
        "Fake GPU",
        4202496u,
        "B8G8R8A8_SRGB",
        false,
        4,
        PresentMode.Mailbox,
        false);

    /// <inheritdoc />
    public RenderStats Stats { get; set; }

    /// <summary>
    /// Gets or sets the exception that <see cref="Initialize"/> throws after it created the surface.
    /// </summary>
    internal Exception? InitializeException { get; set; }

    /// <summary>
    /// Gets or sets the exception that <see cref="Submit"/> throws.
    /// </summary>
    internal Exception? SubmitException { get; set; }

    /// <summary>
    /// Gets or sets the exception that <see cref="Present"/> throws.
    /// </summary>
    internal Exception? PresentException { get; set; }

    /// <summary>
    /// Gets or sets the result of <see cref="Recover"/>. The default is <see langword="true"/>.
    /// </summary>
    internal bool RecoverResult { get; set; } = true;

    /// <summary>
    /// Gets or sets whether <see cref="Submit"/> publishes a frame <see cref="RenderStats"/> snapshot.
    /// </summary>
    /// <remarks>
    /// App lifecycle tests that overwrite <see cref="RenderStatus"/> directly disable this so the
    /// render thread does not replace the value under test.
    /// </remarks>
    internal bool PublishFrameStats { get; set; } = true;

    /// <summary>
    /// Gets or sets an optional hook invoked at the end of <see cref="Submit"/>.
    /// </summary>
    internal Action<RenderFramePacket>? OnSubmit { get; set; }

    /// <summary>
    /// Gets or sets an optional hook invoked at the end of <see cref="Present"/>.
    /// </summary>
    internal Action? OnPresent { get; set; }

    /// <summary>
    /// Gets or sets the action <see cref="Dispose"/> enqueues on the options' main-thread teardown
    /// queue. The action runs on whichever thread drains the queue, not on the render thread.
    /// </summary>
    internal Action? TeardownAction { get; set; }

    /// <summary>
    /// Gets or sets the GPU asset worker the fake backend hands to the render loop. A test replaces it
    /// with a recording fake when it drives uploads through the bridge.
    /// </summary>
    internal IAssetGpuWorker AssetGpuWorker { get; set; } = new FakeAssetGpuWorker();

    /// <summary>
    /// Gets the options passed to <see cref="Initialize"/>, or <see langword="null"/> before it ran.
    /// </summary>
    internal RendererBackendOptions? Options {
        get {
            lock (_gate) {
                return _options;
            }
        }
    }

    /// <summary>
    /// Gets the number of <see cref="Initialize"/> calls.
    /// </summary>
    internal int InitializeCallCount {
        get {
            lock (_gate) {
                return _initializeCallCount;
            }
        }
    }

    /// <summary>
    /// Gets the native surface handle created during <see cref="Initialize"/>, or
    /// <see cref="IntPtr.Zero"/> when no surface was created.
    /// </summary>
    internal IntPtr SurfaceHandle {
        get {
            lock (_gate) {
                return _surfaceHandle;
            }
        }
    }

    /// <summary>
    /// Gets whether <see cref="Dispose"/> destroyed the native surface.
    /// </summary>
    internal bool SurfaceDestroyed {
        get {
            lock (_gate) {
                return _surfaceDestroyed;
            }
        }
    }

    /// <summary>
    /// Gets the frame indices passed to <see cref="Submit"/>, in call order.
    /// </summary>
    internal IReadOnlyList<long> SubmitFrames {
        get {
            lock (_gate) {
                return [.. _submitFrames];
            }
        }
    }

    /// <summary>
    /// Gets the number of <see cref="Present"/> calls.
    /// </summary>
    internal int PresentCount {
        get {
            lock (_gate) {
                return _presentCount;
            }
        }
    }

    /// <summary>
    /// Gets the number of <see cref="Recover"/> calls.
    /// </summary>
    internal int RecoverCalls {
        get {
            lock (_gate) {
                return _recoverCalls;
            }
        }
    }

    /// <summary>
    /// Gets the number of effective <see cref="Dispose"/> calls. Repeated calls are no-ops.
    /// </summary>
    internal int DisposeCallCount {
        get {
            lock (_gate) {
                return _disposeCallCount;
            }
        }
    }

    /// <summary>
    /// Gets the window surface snapshot observed most recently on the render thread.
    /// </summary>
    internal WindowSurfaceState LastWindowState {
        get {
            lock (_gate) {
                return _lastWindowState;
            }
        }
    }

    /// <inheritdoc />
    /// <exception cref="GraphicsException">The surface handshake failed.</exception>
    public void Initialize(RendererBackendOptions options) {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.TeardownQueue);
        lock (_gate) {
            _options = options;
            _initializeCallCount++;
        }

        UpdateWindowState();
        IntPtr surface = options.SurfaceProvider.CreateSurface(_instanceHandle);
        lock (_gate) {
            _surfaceHandle = surface;
        }

        if (InitializeException is not null) {
            throw InitializeException;
        }
    }

    /// <inheritdoc />
    public void Submit(RenderFramePacket frame) {
        lock (_gate) {
            _submitFrames.Add(frame.FrameIndex);
        }

        UpdateWindowState();
        if (SubmitException is not null) {
            throw SubmitException;
        }

        if (PublishFrameStats) {
            Stats = new RenderStats(
                frame.FrameIndex,
                1,
                0,
                0,
                0,
                0,
                0,
                0,
                false,
                0.0,
                0.0,
                0,
                0);
        }

        OnSubmit?.Invoke(frame);
    }

    /// <inheritdoc />
    public void Present() {
        lock (_gate) {
            _presentCount++;
        }

        if (PresentException is not null) {
            throw PresentException;
        }

        OnPresent?.Invoke();
    }

    /// <inheritdoc />
    /// <remarks>The fake has no swapchain to recreate, so a resize only records the window snapshot.</remarks>
    public void Resize(int renderWidth, int renderHeight) => UpdateWindowState();

    /// <inheritdoc />
    public bool Recover(RenderRecoveryReason reason) {
        lock (_gate) {
            _recoverCalls++;
        }

        return RecoverResult;
    }

    /// <inheritdoc />
    public IAssetGpuWorker CreateAssetGpuWorker() => AssetGpuWorker;

    /// <inheritdoc />
    public void Dispose() {
        RendererBackendOptions? options;
        IntPtr surface;
        Action? teardownAction = TeardownAction;
        lock (_gate) {
            if (_disposeCallCount > 0) {
                return;
            }

            _disposeCallCount = 1;
            options = _options;
            surface = _surfaceHandle;
        }

        if (options is not null) {
            if (surface != IntPtr.Zero) {
                options.SurfaceProvider.DestroySurface(_instanceHandle, surface);
                lock (_gate) {
                    _surfaceDestroyed = true;
                }
            }

            if (teardownAction is not null) {
                options.TeardownQueue.Enqueue(teardownAction);
            }
        }
    }

    private void UpdateWindowState() {
        RendererBackendOptions? options;
        lock (_gate) {
            options = _options;
        }

        if (options is null) {
            return;
        }

        WindowSurfaceState state = options.WindowStateProvider();
        lock (_gate) {
            _lastWindowState = state;
        }
    }
}
