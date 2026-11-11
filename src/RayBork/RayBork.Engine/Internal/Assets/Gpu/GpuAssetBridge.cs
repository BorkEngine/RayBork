namespace RayBork.Internal;

/// <summary>
/// The thread-safe handoff between asset loaders and the render thread for GPU asset uploads,
/// destructions, and device-recovery replays.
/// </summary>
/// <remarks>
/// <para>
/// Any thread may call <see cref="UploadAsync"/> and <see cref="Destroy"/>; the render thread calls
/// <see cref="ProcessPending"/>, <see cref="Restore"/>, and <see cref="Close"/>. An upload is queued
/// with one completion task, and the render thread creates the resource through the
/// <see cref="IAssetGpuWorker"/> attached by the renderer backend. The bridge retains the upload
/// description of every live resource so <see cref="Restore"/> can recreate it after a device
/// recovery.
/// </para>
/// <para>
/// The render loop waits on <see cref="Signal"/> together with the frame-ring ready signal and calls
/// <see cref="ProcessPending"/> on every loop turn, so an upload queued during <c>Setup</c> completes
/// before the first tick. A canceled request completes as canceled and, when the worker had already
/// created the resource, that resource is destroyed instead of leaked. A failed request completes
/// with <see cref="AssetLoadException"/> and never stops the render loop. Destroy requests are
/// deduplicated and run on the render thread exactly once.
/// </para>
/// <para>
/// Shutdown order: the main thread completes and drains the frame ring, the render loop exits, and
/// <see cref="Close"/> runs before the backend is disposed. Close completes any pending upload with
/// the render failure (or <see cref="ObjectDisposedException"/>), destroys every live and queued
/// resource, and rejects later uploads, so no caller can hang on a stopped render thread.
/// </para>
/// </remarks>
internal sealed class GpuAssetBridge {
    /// <summary>
    /// The maximum number of queued uploads. The asset loader coalesces loads, so the bound is a
    /// backstop against a render thread that stopped draining, not a normal concurrency limit.
    /// </summary>
    internal const int MAX_PENDING_UPLOADS = 1024;

    /// <summary>
    /// The maximum number of queued destructions. A resource is queued at most once and only after it
    /// left the live table, so the bound is reached only when the render thread stopped draining.
    /// </summary>
    private const int MAX_PENDING_DESTROYS = 4096;

    private readonly Lock _gate = new();
    private readonly Queue<UploadRequest> _uploads = new();
    private readonly Queue<GpuAssetResource> _destroys = new();
    private readonly HashSet<GpuAssetResource> _live = new();
    private readonly AutoResetEvent _workAvailable = new(false);

    private IAssetGpuWorker? _worker;
    private bool _closed;

    /// <summary>
    /// Gets the wait handle the render loop waits on with the frame-ring ready signal. It is signaled
    /// while upload or destroy work is queued and is auto-reset by the wait.
    /// </summary>
    internal WaitHandle Signal => _workAvailable;

    /// <summary>
    /// Gets whether <see cref="Close"/> already ran.
    /// </summary>
    internal bool IsClosed {
        get {
            lock (_gate) {
                return _closed;
            }
        }
    }

    /// <summary>
    /// Gets whether a worker is attached and the bridge accepts uploads.
    /// </summary>
    internal bool HasWorker {
        get {
            lock (_gate) {
                return _worker is not null;
            }
        }
    }

    /// <summary>
    /// Gets the number of queued upload requests. Diagnostic, for tests.
    /// </summary>
    internal int PendingUploadCount {
        get {
            lock (_gate) {
                return _uploads.Count;
            }
        }
    }

    /// <summary>
    /// Gets the number of queued destroy requests. Diagnostic, for tests.
    /// </summary>
    internal int PendingDestroyCount {
        get {
            lock (_gate) {
                return _destroys.Count;
            }
        }
    }

    /// <summary>
    /// Gets the number of live resources. Diagnostic, for tests.
    /// </summary>
    internal int LiveResourceCount {
        get {
            lock (_gate) {
                return _live.Count;
            }
        }
    }

    /// <summary>
    /// Attaches the worker for the backend's current device. The render loop calls it on the render
    /// thread after the backend initialized and before the loop publishes readiness.
    /// </summary>
    /// <param name="worker">The worker that creates, recreates, and destroys GPU resources.</param>
    /// <exception cref="ArgumentNullException"><paramref name="worker"/> is <c>null</c>.</exception>
    /// <exception cref="ObjectDisposedException">The bridge is closed.</exception>
    /// <exception cref="InvalidOperationException">A worker is already attached.</exception>
    internal void AttachWorker(IAssetGpuWorker worker) {
        ArgumentNullException.ThrowIfNull(worker);

        lock (_gate) {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_worker is not null) {
                throw new InvalidOperationException("The GPU asset bridge already has a worker.");
            }

            _worker = worker;
        }
    }

    /// <summary>
    /// Queues a GPU upload and returns the task that completes after the render thread created the
    /// resource. Safe to call from any thread.
    /// </summary>
    /// <param name="upload">The retained upload description.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>
    /// The created resource, or a canceled or faulted task. A canceled request that had already
    /// created a resource destroys it instead of leaking it.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="upload"/> is <c>null</c>.</exception>
    internal Task<GpuAssetResource> UploadAsync(AssetGpuUpload upload, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(upload);
        if (cancellationToken.IsCancellationRequested) {
            return Task.FromCanceled<GpuAssetResource>(cancellationToken);
        }

        UploadRequest request = new(this, upload, cancellationToken);
        lock (_gate) {
            if (_closed) {
                return Task.FromException<GpuAssetResource>(CreateClosedException());
            }

            if (_worker is null) {
                return Task.FromException<GpuAssetResource>(new GraphicsException(
                    "The GPU asset bridge has no worker; the renderer backend cannot upload GPU assets.",
                    DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                    null));
            }

            if (_uploads.Count >= MAX_PENDING_UPLOADS) {
                return Task.FromException<GpuAssetResource>(new AssetLoadException(
                    $"The GPU asset upload queue is full ({MAX_PENDING_UPLOADS} pending requests); " +
                    "the render thread is not draining it.",
                    DiagnosticCodes.ASSET_GPU_UPLOAD_FAILED,
                    null));
            }

            _uploads.Enqueue(request);
        }

        if (cancellationToken.CanBeCanceled) {
            request.RegisterCancellation();
        }

        _workAvailable.Set();
        return request.Completion.Task;
    }

    /// <summary>
    /// Queues the destruction of a live resource. Safe to call from any thread; the render thread
    /// destroys the resource exactly once. Destroying an unknown or already-queued resource is a
    /// no-op, and a call after <see cref="Close"/> is ignored because Close already destroyed every
    /// live resource.
    /// </summary>
    /// <param name="resource">The resource to destroy.</param>
    /// <exception cref="ArgumentNullException"><paramref name="resource"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">The destroy queue is full.</exception>
    internal void Destroy(GpuAssetResource resource) {
        ArgumentNullException.ThrowIfNull(resource);

        lock (_gate) {
            if (_closed || !_live.Remove(resource)) {
                return;
            }

            if (_destroys.Count >= MAX_PENDING_DESTROYS) {
                _live.Add(resource);
                throw new InvalidOperationException(
                    $"The GPU asset bridge destroy queue is full ({MAX_PENDING_DESTROYS} pending " +
                    "requests); the render thread is not draining it.");
            }

            _destroys.Enqueue(resource);
        }

        _workAvailable.Set();
    }

    /// <summary>
    /// Drains the queued uploads and destructions on the render thread and completes every upload's
    /// task. A single failed upload or destruction never throws out of the call.
    /// </summary>
    /// <param name="worker">The worker for the current device.</param>
    /// <exception cref="ArgumentNullException"><paramref name="worker"/> is <c>null</c>.</exception>
    internal void ProcessPending(IAssetGpuWorker worker) {
        ArgumentNullException.ThrowIfNull(worker);

        while (true) {
            UploadRequest? request = TryDequeueUpload();
            if (request is null) {
                break;
            }

            ProcessUpload(worker, request);
        }

        while (true) {
            GpuAssetResource? resource = TryDequeueDestroy();
            if (resource is null) {
                break;
            }

            DestroyResourceNow(worker, resource);
        }
    }

    /// <summary>
    /// Recreates every live resource from its retained upload after a successful device recovery. Runs
    /// on the render thread and keeps each resource's identity, so public asset objects stay valid.
    /// </summary>
    /// <param name="worker">The worker for the recreated device.</param>
    /// <exception cref="ArgumentNullException"><paramref name="worker"/> is <c>null</c>.</exception>
    /// <exception cref="AssetLoadException">
    /// One or more resources could not be recreated. The diagnostic code is <c>RB1010</c>.
    /// </exception>
    internal void Restore(IAssetGpuWorker worker) {
        ArgumentNullException.ThrowIfNull(worker);

        GpuAssetResource[] live;
        lock (_gate) {
            if (_closed) {
                return;
            }

            live = [.. _live];
        }

        List<Exception>? failures = null;
        foreach (GpuAssetResource resource in live) {
            string path = AssetPath.ToPublic(resource.Upload.Path);
            try {
                GpuAssetResource restored = worker.Restore(resource.Upload, resource)
                    ?? throw new InvalidOperationException(
                        $"The GPU asset worker returned no resource while restoring '{path}'.");
                resource.AdoptRestored(restored);
            } catch (Exception exception) {
                failures ??= [];
                failures.Add(IsUploadFailure(exception)
                    ? exception
                    : new AssetLoadException(
                        $"The GPU asset re-upload of '{path}' failed after a device recovery.",
                        DiagnosticCodes.ASSET_GPU_UPLOAD_FAILED,
                        exception));
            }
        }

        if (failures is null) {
            return;
        }

        if (failures.Count == 1) {
            throw failures[0];
        }

        throw new AssetLoadException(
            $"The GPU asset bridge could not re-upload {failures.Count} live resources after a device recovery.",
            DiagnosticCodes.ASSET_GPU_UPLOAD_FAILED,
            new AggregateException(failures));
    }

    /// <summary>
    /// Stops the bridge during teardown: completes every pending upload, destroys every live and
    /// queued resource, and rejects later uploads. Runs on the render thread after the frame ring is
    /// complete and drained, and before the backend is disposed. Idempotent.
    /// </summary>
    /// <param name="failure">
    /// The render loop failure that stopped the app, or <see langword="null"/> for a graceful stop.
    /// Pending uploads receive it; a graceful stop uses <see cref="ObjectDisposedException"/>.
    /// </param>
    internal void Close(Exception? failure) {
        UploadRequest[] uploads;
        GpuAssetResource[] queuedDestroys;
        GpuAssetResource[] live;
        IAssetGpuWorker? worker;
        lock (_gate) {
            if (_closed) {
                return;
            }

            _closed = true;
            worker = _worker;
            uploads = [.. _uploads];
            _uploads.Clear();
            queuedDestroys = [.. _destroys];
            _destroys.Clear();
            live = [.. _live];
            _live.Clear();
        }

        Exception completion = failure ?? CreateClosedException();
        foreach (UploadRequest request in uploads) {
            request.DisposeRegistration();
            request.Completion.TrySetException(completion);
        }

        if (worker is null) {
            return;
        }

        foreach (GpuAssetResource resource in queuedDestroys) {
            DestroyResourceNow(worker, resource);
        }

        foreach (GpuAssetResource resource in live) {
            DestroyResourceNow(worker, resource);
        }
    }

    private UploadRequest? TryDequeueUpload() {
        lock (_gate) {
            if (_closed || _uploads.Count == 0) {
                return null;
            }

            return _uploads.Dequeue();
        }
    }

    private GpuAssetResource? TryDequeueDestroy() {
        lock (_gate) {
            if (_closed || _destroys.Count == 0) {
                return null;
            }

            return _destroys.Dequeue();
        }
    }

    private void ProcessUpload(IAssetGpuWorker worker, UploadRequest request) {
        try {
            if (request.Completion.Task.IsCompleted) {
                // The caller canceled before the render thread started the upload; no resource exists.
                return;
            }

            GpuAssetResource resource;
            try {
                resource = worker.Upload(request.Upload);
            } catch (OperationCanceledException) when (request.CancellationToken.IsCancellationRequested) {
                request.Completion.TrySetCanceled(request.CancellationToken);
                return;
            } catch (Exception exception) {
                request.Completion.TrySetException(CreateUploadFailure(request.Upload, exception));
                return;
            }

            if (resource is null) {
                request.Completion.TrySetException(new AssetLoadException(
                    $"The GPU asset worker returned no resource for '{AssetPath.ToPublic(request.Upload.Path)}'.",
                    DiagnosticCodes.ASSET_GPU_UPLOAD_FAILED,
                    null));
                return;
            }

            if (resource.Kind != request.Upload.Kind) {
                request.Completion.TrySetException(new AssetLoadException(
                    $"The GPU asset worker created a {resource.Kind} resource for the " +
                    $"{request.Upload.Kind} upload '{AssetPath.ToPublic(request.Upload.Path)}'.",
                    DiagnosticCodes.ASSET_GPU_UPLOAD_FAILED,
                    null));
                DestroyResourceNow(worker, resource);
                return;
            }

            AddLive(resource);
            if (!request.Completion.TrySetResult(resource)) {
                // The caller canceled while the worker created the resource; destroy it instead of
                // leaking a live GPU resource that no asset will ever release.
                RemoveLive(resource);
                DestroyResourceNow(worker, resource);
            }
        } finally {
            request.DisposeRegistration();
        }
    }

    private void AddLive(GpuAssetResource resource) {
        lock (_gate) {
            _live.Add(resource);
        }
    }

    private void RemoveLive(GpuAssetResource resource) {
        lock (_gate) {
            _live.Remove(resource);
        }
    }

    private static void DestroyResourceNow(IAssetGpuWorker worker, GpuAssetResource resource) {
        if (!resource.TryMarkDestroyed()) {
            return;
        }

        try {
            worker.Destroy(resource);
        } catch (Exception) {
            // The app is tearing down or the driver is already gone; a failed destroy cannot be
            // reported to a caller and must not stop the render thread.
        }
    }

    private static Exception CreateUploadFailure(AssetGpuUpload upload, Exception exception) {
        if (exception is AssetLoadException) {
            return exception;
        }

        return new AssetLoadException(
            $"The GPU upload of '{AssetPath.ToPublic(upload.Path)}' failed.",
            DiagnosticCodes.ASSET_GPU_UPLOAD_FAILED,
            exception);
    }

    private static bool IsUploadFailure(Exception exception) =>
        exception is AssetLoadException assetFailure
        && string.Equals(
            assetFailure.DiagnosticCode,
            DiagnosticCodes.ASSET_GPU_UPLOAD_FAILED,
            StringComparison.Ordinal);

    private static ObjectDisposedException CreateClosedException() => new(
        nameof(GpuAssetBridge),
        "The GPU asset bridge is closed; the render loop has stopped servicing GPU assets.");

    /// <summary>
    /// One queued upload and its single completion task.
    /// </summary>
    private sealed class UploadRequest {
        private readonly GpuAssetBridge _owner;
        private CancellationTokenRegistration _registration;

        internal UploadRequest(GpuAssetBridge owner, AssetGpuUpload upload, CancellationToken cancellationToken) {
            _owner = owner;
            Upload = upload;
            CancellationToken = cancellationToken;
            Completion = new TaskCompletionSource<GpuAssetResource>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        internal AssetGpuUpload Upload { get; }

        internal CancellationToken CancellationToken { get; }

        internal TaskCompletionSource<GpuAssetResource> Completion { get; }

        internal void RegisterCancellation() {
            _registration = CancellationToken.Register(
                static state => ((UploadRequest)state!).Cancel(),
                this);
        }

        internal void DisposeRegistration() => _registration.Dispose();

        private void Cancel() {
            if (Completion.TrySetCanceled(CancellationToken)) {
                // Wake the render thread so it can drop the queued request without creating a
                // resource, or destroy one it already created.
                _owner._workAvailable.Set();
            }
        }
    }
}
