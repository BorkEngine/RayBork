namespace RayBork.Internal;

/// <summary>
/// The thread-safe handoff between asset loaders and the render thread for GPU asset uploads,
/// destructions, and device-recovery replays.
/// </summary>
/// <remarks>
/// <para>
/// Any thread may call <see cref="UploadAsync"/> and <see cref="Destroy"/>; the render thread calls
/// <see cref="ProcessPending"/>, <see cref="Restore"/>, and <see cref="Close"/>. An upload is queued
/// with one completion task, and the render thread starts it through the
/// <see cref="IAssetGpuWorker"/> attached by the renderer backend. <see cref="ProcessPending"/> starts
/// as many queued uploads as the in-flight bounds allow, polls the running ones without blocking, and
/// completes each request exactly once. The bridge retains the upload description of every live
/// resource so <see cref="Restore"/> can recreate it after a device recovery.
/// </para>
/// <para>
/// The render loop waits on <see cref="Signal"/> together with the frame-ring ready signal and calls
/// <see cref="ProcessPending"/> on every loop turn, so an upload queued during <c>Setup</c> starts
/// before the first tick. While an upload is in flight the loop uses a short bounded wait so it keeps
/// polling the GPU copy without publishing frames; a deferred upload therefore never stalls frame
/// submission. A canceled request completes as canceled and, when the worker already created or
/// partially created a resource, that operation is canceled so nothing leaks. A failed request
/// completes with <see cref="AssetLoadException"/> and never stops the render loop. Destroy requests
/// are deduplicated and run on the render thread exactly once.
/// </para>
/// <para>
/// Shutdown order: the main thread completes and drains the frame ring, the render loop exits, and
/// <see cref="Close"/> runs before the backend is disposed. Close fails every queued and in-flight
/// upload with the render failure (or <see cref="ObjectDisposedException"/>), cancels the in-flight
/// operations before destroying every live and queued resource, and rejects later uploads, so no
/// caller can hang on a stopped render thread.
/// </para>
/// </remarks>
internal sealed class GpuAssetBridge {
    /// <summary>
    /// The maximum number of queued uploads. The asset loader coalesces loads, so the bound is a
    /// backstop against a render thread that stopped draining, not a normal concurrency limit.
    /// </summary>
    internal const int MAX_PENDING_UPLOADS = 1024;

    /// <summary>
    /// The maximum number of uploads that may be copying on the GPU at the same time.
    /// </summary>
    internal const int MAX_IN_FLIGHT_UPLOADS = 8;

    /// <summary>
    /// The maximum number of payload bytes the in-flight uploads may retain together.
    /// </summary>
    internal const long MAX_IN_FLIGHT_UPLOAD_BYTES = 256L * 1024 * 1024;

    /// <summary>
    /// The maximum number of queued destructions. A resource is queued at most once and only after it
    /// left the live table, so the bound is reached only when the render thread stopped draining.
    /// </summary>
    private const int MAX_PENDING_DESTROYS = 4096;

    private readonly Lock _gate = new();
    private readonly Queue<UploadRequest> _uploads = new();
    private readonly List<InFlightUpload> _inFlight = [];
    private readonly Queue<GpuAssetResource> _destroys = new();
    private readonly HashSet<GpuAssetResource> _live = new();
    private readonly AutoResetEvent _workAvailable = new(false);
    private readonly int _maxInFlightUploads;
    private readonly long _maxInFlightUploadBytes;

    private IAssetGpuWorker? _worker;
    private bool _closed;
    private long _inFlightBytes;

    /// <summary>
    /// Initializes a bridge with the production in-flight bounds.
    /// </summary>
    internal GpuAssetBridge()
        : this(MAX_IN_FLIGHT_UPLOADS, MAX_IN_FLIGHT_UPLOAD_BYTES) {
    }

    /// <summary>
    /// Initializes a bridge with explicit in-flight bounds. Tests use small values.
    /// </summary>
    /// <param name="maxInFlightUploads">The maximum number of concurrent GPU copies.</param>
    /// <param name="maxInFlightUploadBytes">The maximum payload bytes the concurrent copies retain.</param>
    /// <exception cref="ArgumentOutOfRangeException">A bound is not positive.</exception>
    internal GpuAssetBridge(int maxInFlightUploads, long maxInFlightUploadBytes) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxInFlightUploads);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxInFlightUploadBytes);

        _maxInFlightUploads = maxInFlightUploads;
        _maxInFlightUploadBytes = maxInFlightUploadBytes;
    }

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
    /// Gets whether the bridge still has a queued or in-flight upload. The render loop uses it to
    /// choose the short polling wait while an upload is still copying on the GPU.
    /// </summary>
    internal bool HasPendingWork {
        get {
            lock (_gate) {
                return _uploads.Count > 0 || _inFlight.Count > 0;
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
    /// Gets the number of uploads whose GPU copy is in flight. Diagnostic, for tests.
    /// </summary>
    internal int InFlightUploadCount {
        get {
            lock (_gate) {
                return _inFlight.Count;
            }
        }
    }

    /// <summary>
    /// Gets the estimated payload bytes retained by the in-flight uploads. Diagnostic, for tests.
    /// </summary>
    internal long InFlightUploadBytes {
        get {
            lock (_gate) {
                return _inFlightBytes;
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
    /// Drains the queued uploads and destructions on the render thread, starts uploads while the
    /// in-flight bounds allow it, polls the running ones, and completes every finished upload's task.
    /// The call never blocks on a fence and a single failed upload or destruction never throws out of
    /// it.
    /// </summary>
    /// <param name="worker">The worker for the current device.</param>
    /// <exception cref="ArgumentNullException"><paramref name="worker"/> is <c>null</c>.</exception>
    internal void ProcessPending(IAssetGpuWorker worker) {
        ArgumentNullException.ThrowIfNull(worker);

        bool progress;
        do {
            progress = PollInFlight(worker);
            progress |= StartQueuedUploads(worker);
        } while (progress);

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
    /// <remarks>
    /// A recovery is rare and runs with the device idle, so this stays synchronous: the worker may
    /// wait for the recreated upload's fence before the bridge adopts its handles.
    /// </remarks>
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
    /// Stops the bridge during teardown: fails every queued and in-flight upload, cancels the in-flight
    /// operations, destroys every live and queued resource, and rejects later uploads. Runs on the
    /// render thread after the frame ring is complete and drained, and before the backend is disposed.
    /// Idempotent.
    /// </summary>
    /// <param name="failure">
    /// The render loop failure that stopped the app, or <see langword="null"/> for a graceful stop.
    /// Pending uploads receive it; a graceful stop uses <see cref="ObjectDisposedException"/>.
    /// </param>
    internal void Close(Exception? failure) {
        UploadRequest[] uploads;
        InFlightUpload[] inFlight;
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
            inFlight = [.. _inFlight];
            _inFlight.Clear();
            _inFlightBytes = 0;
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

        foreach (InFlightUpload upload in inFlight) {
            upload.Request.DisposeRegistration();
            upload.Request.Completion.TrySetException(completion);
        }

        if (worker is null) {
            return;
        }

        // A canceled in-flight operation owns staging and partial GPU objects; cancel it before the
        // backend destroys the device. This may wait for the upload fence, which is fine because the
        // loop is stopping.
        foreach (InFlightUpload upload in inFlight) {
            if (upload.Operation is not null) {
                CancelOperation(worker, upload.Operation);
            }
        }

        foreach (GpuAssetResource resource in queuedDestroys) {
            DestroyResourceNow(worker, resource);
        }

        foreach (GpuAssetResource resource in live) {
            DestroyResourceNow(worker, resource);
        }
    }

    private bool PollInFlight(IAssetGpuWorker worker) {
        bool progress = false;
        foreach (InFlightUpload upload in SnapshotInFlight()) {
            GpuAssetUploadOperation? operation = upload.Operation;
            if (operation is null) {
                continue;
            }

            bool finished;
            GpuAssetResource? resource = null;
            Exception? error = null;
            try {
                finished = worker.TryComplete(operation, out resource, out error);
            } catch (Exception exception) {
                CancelOperation(worker, operation);
                RemoveInFlight(upload);
                upload.Request.DisposeRegistration();
                upload.Request.Completion.TrySetException(
                    CreateUploadFailure(upload.Request.Upload, exception));
                progress = true;
                continue;
            }

            if (!finished) {
                if (upload.Request.Completion.Task.IsCompleted) {
                    // The caller canceled while the GPU copy was still pending. Cancellation is rare,
                    // so the worker may wait for the fence before releasing the staging and partial
                    // resource.
                    CancelOperation(worker, operation);
                    RemoveInFlight(upload);
                    upload.Request.DisposeRegistration();
                    progress = true;
                }

                continue;
            }

            RemoveInFlight(upload);
            upload.Request.DisposeRegistration();
            CompleteRequest(worker, upload.Request, resource, error);
            progress = true;
        }

        return progress;
    }

    private bool StartQueuedUploads(IAssetGpuWorker worker) {
        bool started = false;
        while (true) {
            UploadRequest? request;
            InFlightUpload upload;
            lock (_gate) {
                while (true) {
                    if (_uploads.Count == 0) {
                        return started;
                    }

                    request = _uploads.Peek();
                    if (!request.Completion.Task.IsCompleted) {
                        break;
                    }

                    _uploads.Dequeue();
                    request.DisposeRegistration();
                }

                long bytes = request.Upload.EstimatedBytes;
                if (_inFlight.Count >= _maxInFlightUploads
                    || (_inFlight.Count > 0 && _inFlightBytes + bytes > _maxInFlightUploadBytes)) {
                    return started;
                }

                _uploads.Dequeue();
                upload = new InFlightUpload(request, bytes);
                _inFlight.Add(upload);
                _inFlightBytes += bytes;
            }

            try {
                upload.Operation = worker.BeginUpload(request.Upload);
                started = true;
            } catch (Exception exception) {
                RemoveInFlight(upload);
                request.DisposeRegistration();
                request.Completion.TrySetException(CreateUploadFailure(request.Upload, exception));
                started = true;
            }
        }
    }

    private void CompleteRequest(
        IAssetGpuWorker worker,
        UploadRequest request,
        GpuAssetResource? resource,
        Exception? error) {
        if (error is not null) {
            request.Completion.TrySetException(CreateUploadFailure(request.Upload, error));
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
            // The caller canceled while the worker was finishing the upload; destroy the resource
            // instead of leaking a live GPU resource that no asset will ever release.
            RemoveLive(resource);
            DestroyResourceNow(worker, resource);
        }
    }

    private void CancelOperation(IAssetGpuWorker worker, GpuAssetUploadOperation operation) {
        try {
            worker.Cancel(operation);
        } catch (Exception) {
            // The device may already be gone during shutdown. Cancellation must never stop the loop;
            // the worker logs its own cleanup failures.
        }
    }

    private InFlightUpload[] SnapshotInFlight() {
        lock (_gate) {
            return [.. _inFlight];
        }
    }

    private void RemoveInFlight(InFlightUpload upload) {
        lock (_gate) {
            if (_inFlight.Remove(upload)) {
                _inFlightBytes -= upload.Bytes;
            }
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

    private GpuAssetResource? TryDequeueDestroy() {
        lock (_gate) {
            if (_closed || _destroys.Count == 0) {
                return null;
            }

            return _destroys.Dequeue();
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
    /// One upload that the render thread started and has not completed or canceled yet.
    /// </summary>
    private sealed class InFlightUpload {
        internal InFlightUpload(UploadRequest request, long bytes) {
            Request = request;
            Bytes = bytes;
        }

        internal UploadRequest Request { get; }

        internal long Bytes { get; }

        internal GpuAssetUploadOperation? Operation { get; set; }
    }

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
                // resource, or cancel one it already started.
                _owner._workAvailable.Set();
            }
        }
    }
}
