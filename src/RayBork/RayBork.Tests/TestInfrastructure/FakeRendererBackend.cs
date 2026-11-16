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
    private readonly Dictionary<long, FakeRenderTarget> _renderTargets = [];

    private RendererBackendOptions? _options;
    private IntPtr _surfaceHandle;
    private bool _surfaceDestroyed;
    private int _initializeCallCount;
    private int _presentCount;
    private int _recoverCalls;
    private int _disposeCallCount;
    private int _deviceGeneration;
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

    /// <inheritdoc />
    public int DeviceGeneration => Volatile.Read(ref _deviceGeneration);

    /// <summary>
    /// Simulates a logical-device recreation for the render loop's asset-restoration tests. The fake
    /// backend has no device, so the test drives the generation the backend would publish.
    /// </summary>
    internal void SimulateDeviceRecreationForTests() => Interlocked.Increment(ref _deviceGeneration);

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
    /// Gets or sets whether <see cref="Submit"/> returns without recording or publishing a frame,
    /// which models a frame dropped on a minimized surface or during a device recreation.
    /// </summary>
    internal bool SkipSubmitForTests { get; set; }

    /// <summary>
    /// Gets or sets an optional hook invoked at the end of <see cref="Submit"/>.
    /// </summary>
    internal Action<RenderFramePacket>? OnSubmit { get; set; }

    /// <summary>
    /// Gets or sets the exception that a queued readback request publishes. The default is
    /// <see langword="null"/>, which returns a deterministic fake image.
    /// </summary>
    internal Exception? ReadbackException { get; set; }

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

        Volatile.Write(ref _deviceGeneration, 1);
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
        if (SkipSubmitForTests) {
            return;
        }

        if (SubmitException is not null) {
            throw SubmitException;
        }

        if (PublishFrameStats) {
            Stats = new RenderStats(
                frame.FrameIndex,
                0,
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

    /// <summary>
    /// Gets the number of live fake render textures. Diagnostic, for app and service tests.
    /// </summary>
    internal int LiveRenderTextureCount {
        get {
            lock (_gate) {
                return _renderTargets.Count;
            }
        }
    }

    /// <summary>
    /// Gets whether a target id is still live in the fake registry. Diagnostic, for service tests.
    /// </summary>
    /// <param name="targetId">The target id to check.</param>
    /// <returns><see langword="true"/> when a target with the id is registered.</returns>
    internal bool IsRenderTargetLive(long targetId) {
        lock (_gate) {
            return _renderTargets.ContainsKey(targetId);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The fake has no GPU, so it completes each request with a public <see cref="RenderTexture"/> over
    /// fake lifetimes and destroys the target entry when the request's lifetime is released. The public
    /// API, the request handoff, and the shutdown behavior are exercised without a device.
    /// </remarks>
    public void ProcessRenderTextureRequests(RenderTextureService service) {
        ArgumentNullException.ThrowIfNull(service);

        foreach (RenderTextureService.CreateRequest request in service.DrainCreates()) {
            if (request.Completion.Task.IsCompleted) {
                request.DisposeRegistration();
                continue;
            }

            long targetId = service.NextTargetId();
            RenderTextureDescription description = request.Description;
            int sampleCount = RenderTextureValidation.SelectSampleCount(
                description.SampleCount,
                [1, 2, 4, 8]);
            RenderTextureTargetLifetime lifetime = new(targetId, description, sampleCount, service.QueueDestruction);
            RenderTextureViewResource viewResource = new(default, default) {
                BindlessSlot = 1,
            };
            RenderTextureViewLifetime viewLifetime = new(
                lifetime,
                viewResource,
                description.Width,
                description.Height);
            TextureAssetDescriptor descriptor = new() {
                Payload = string.Empty,
                Width = checked((uint)description.Width),
                Height = checked((uint)description.Height),
                MipLevels = 1,
                Format = description.ColorFormat,
                IsSrgb = description.ColorFormat == TextureFormat.Rgba8Srgb,
                Filter = TextureFilter.Linear,
                Wrap = TextureWrap.Clamp,
                MaxAnisotropy = 1f,
            };
            Texture colorTexture = new(viewLifetime, descriptor, isProcedural: false, isRenderTargetView: true);
            Texture? depthTexture = null;
            RenderTextureViewLifetime? depthLifetime = null;
            if (description.Depth) {
                RenderTextureViewResource depthResource = new(default, default) {
                    BindlessSlot = 2,
                };
                depthLifetime = new RenderTextureViewLifetime(
                    lifetime,
                    depthResource,
                    description.Width,
                    description.Height);
                TextureAssetDescriptor depthDescriptor = new() {
                    Payload = string.Empty,
                    Width = checked((uint)description.Width),
                    Height = checked((uint)description.Height),
                    MipLevels = 1,
                    Format = TextureFormat.Depth32Float,
                    IsSrgb = false,
                    Filter = TextureFilter.Nearest,
                    Wrap = TextureWrap.Clamp,
                    MaxAnisotropy = 1f,
                };
                depthTexture = new Texture(depthLifetime, depthDescriptor, isProcedural: false, isRenderTargetView: true);
            }

            RenderTexture renderTexture = new(lifetime, colorTexture, depthTexture, service);
            lock (_gate) {
                _renderTargets[targetId] = new FakeRenderTarget(lifetime, viewLifetime, depthLifetime);
            }

            request.DisposeRegistration();
            if (!request.Completion.TrySetResult(renderTexture)) {
                lock (_gate) {
                    _renderTargets.Remove(targetId);
                }
            }
        }

        foreach (RenderTextureService.ResizeRequest request in service.DrainResizes()) {
            FakeRenderTarget? target;
            lock (_gate) {
                _renderTargets.TryGetValue(request.TargetId, out target);
            }

            if (target is null) {
                request.Completion.TrySetException(new InvalidOperationException(
                    $"The fake render target {request.TargetId} is no longer alive."));
                continue;
            }

            if (target.Lifetime.IsDisposed) {
                request.Completion.TrySetException(new ObjectDisposedException(nameof(RenderTexture)));
                continue;
            }

            if (target.Lifetime.IsInUse) {
                request.Completion.TrySetException(new InvalidOperationException(
                    $"The fake render target {request.TargetId} is in use by a recorded or in-flight frame."));
                continue;
            }

            RenderTextureDescription description = target.Lifetime.Description;
            target.Lifetime.UpdateDescription(description with {
                Width = request.Width,
                Height = request.Height,
            });
            target.ColorView.SetDimensions(request.Width, request.Height);
            target.DepthView?.SetDimensions(request.Width, request.Height);
            request.Completion.TrySetResult();
        }

        foreach (RenderTextureService.ReadbackRequest request in service.DrainReadbacks()) {
            try {
                CompleteReadback(request);
            } finally {
                request.DisposeRegistration();
            }
        }

        foreach (RenderTextureTargetLifetime lifetime in service.DrainDestroys()) {
            lock (_gate) {
                _renderTargets.Remove(lifetime.TargetId);
            }
        }
    }

    private void CompleteReadback(RenderTextureService.ReadbackRequest request) {
        if (request.IsCompleted) {
            return;
        }

        FakeRenderTarget? target;
        lock (_gate) {
            _renderTargets.TryGetValue(request.TargetId, out target);
        }

        if (target is null) {
            request.TrySetException(new InvalidOperationException(
                $"The fake readback addresses render target {request.TargetId}, which is no longer alive."));
            return;
        }

        if (target.Lifetime.IsDisposed) {
            request.TrySetException(new ObjectDisposedException(nameof(RenderTexture)));
            return;
        }

        if (ReadbackException is not null) {
            request.TrySetException(ReadbackException);
            return;
        }

        int width = target.Lifetime.Description.Width;
        int height = target.Lifetime.Description.Height;
        switch (request) {
            case RenderTextureService.ReadbackRequest<byte[]> pixels:
                pixels.Completion.TrySetResult(FakeReadbackPixels(width, height));
                break;
            case RenderTextureService.ReadbackRequest<Color[,]> colors:
                colors.Completion.TrySetResult(FakeReadbackColors(width, height));
                break;
            default:
                request.TrySetException(new InvalidOperationException(
                    "The fake readback request has an unsupported completion value."));
                break;
        }
    }

    private static Color[,] FakeReadbackColors(int width, int height) {
        Color[,] colors = new Color[height, width];
        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++) {
                colors[y, x] = new Color(x / (float)width, y / (float)height, 0.5f, 1f);
            }
        }

        return colors;
    }

    private static byte[] FakeReadbackPixels(int width, int height) {
        byte[] pixels = new byte[checked(width * height * 4)];
        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++) {
                int offset = ((y * width) + x) * 4;
                pixels[offset] = (byte)(x & 0xFF);
                pixels[offset + 1] = (byte)(y & 0xFF);
                pixels[offset + 2] = 128;
                pixels[offset + 3] = byte.MaxValue;
            }
        }

        return pixels;
    }

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

    /// <summary>
    /// One fake render target: the lifetime and the view lifetimes whose dimensions follow a resize.
    /// </summary>
    /// <param name="Lifetime">The target lifetime.</param>
    /// <param name="ColorView">The color view lifetime.</param>
    /// <param name="DepthView">The depth view lifetime, or <see langword="null"/> for a depthless target.</param>
    private sealed record FakeRenderTarget(
        RenderTextureTargetLifetime Lifetime,
        RenderTextureViewLifetime ColorView,
        RenderTextureViewLifetime? DepthView);
}
