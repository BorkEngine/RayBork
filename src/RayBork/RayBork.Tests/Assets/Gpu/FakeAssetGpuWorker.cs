using RayBork.Internal;

namespace RayBork.Tests.Assets.Gpu;

/// <summary>
/// An <see cref="IAssetGpuWorker"/> that records every call and lets a test inject failures, defer an
/// upload, or provide a custom restore result.
/// </summary>
/// <remarks>
/// The worker is called from the render thread in integration tests and from the test thread in unit
/// tests, so the record lists are guarded by a lock. Uploads complete immediately by default, which
/// keeps the existing tests deterministic; <see cref="DeferUploads"/> returns a pending operation whose
/// <see cref="FakeGpuAssetUploadOperation.Complete"/> or
/// <see cref="FakeGpuAssetUploadOperation.Fail"/> the test controls. <see cref="Progress"/> is signaled
/// whenever an upload starts or a deferred operation finishes, so a test harness that stands in for
/// the render loop wakes without polling.
/// </remarks>
internal sealed class FakeAssetGpuWorker : IAssetGpuWorker {
    private readonly Lock _gate = new();
    private readonly List<AssetGpuUpload> _uploadCalls = [];
    private readonly List<FakeGpuAssetUploadOperation> _operations = [];
    private readonly List<GpuAssetUploadOperation> _cancelCalls = [];
    private readonly List<AssetGpuUpload> _restoreCalls = [];
    private readonly List<GpuAssetResource> _destroyCalls = [];
    private readonly ManualResetEventSlim _restored = new(false);
    private readonly AutoResetEvent _progress = new(false);

    private long _nextHandle = 1;

    /// <summary>
    /// Gets or sets an exception thrown by <see cref="BeginUpload"/>.
    /// </summary>
    internal Exception? UploadException { get; set; }

    /// <summary>
    /// Gets or sets an exception thrown by <see cref="Restore"/>.
    /// </summary>
    internal Exception? RestoreException { get; set; }

    /// <summary>
    /// Gets or sets whether a started upload stays pending until the test finishes its operation.
    /// </summary>
    internal bool DeferUploads { get; set; }

    /// <summary>
    /// Gets or sets an optional replacement for the default upload resource.
    /// </summary>
    internal Func<AssetGpuUpload, GpuAssetResource>? UploadHandler { get; set; }

    /// <summary>
    /// Gets or sets an optional replacement for the default restore behavior.
    /// </summary>
    internal Func<AssetGpuUpload, GpuAssetResource, GpuAssetResource>? RestoreHandler { get; set; }

    /// <summary>
    /// Gets the number of <see cref="BeginUpload"/> calls.
    /// </summary>
    internal int UploadCallCount {
        get {
            lock (_gate) {
                return _uploadCalls.Count;
            }
        }
    }

    /// <summary>
    /// Gets the uploads passed to <see cref="BeginUpload"/>, in call order.
    /// </summary>
    internal IReadOnlyList<AssetGpuUpload> UploadCalls {
        get {
            lock (_gate) {
                return [.. _uploadCalls];
            }
        }
    }

    /// <summary>
    /// Gets the operations <see cref="BeginUpload"/> returned, in call order.
    /// </summary>
    internal IReadOnlyList<FakeGpuAssetUploadOperation> Operations {
        get {
            lock (_gate) {
                return [.. _operations];
            }
        }
    }

    /// <summary>
    /// Gets the operations passed to <see cref="Cancel"/>, in call order.
    /// </summary>
    internal IReadOnlyList<GpuAssetUploadOperation> CancelCalls {
        get {
            lock (_gate) {
                return [.. _cancelCalls];
            }
        }
    }

    /// <summary>
    /// Gets the number of <see cref="Restore"/> calls.
    /// </summary>
    internal int RestoreCallCount {
        get {
            lock (_gate) {
                return _restoreCalls.Count;
            }
        }
    }

    /// <summary>
    /// Gets the uploads passed to <see cref="Restore"/>, in call order.
    /// </summary>
    internal IReadOnlyList<AssetGpuUpload> RestoreCalls {
        get {
            lock (_gate) {
                return [.. _restoreCalls];
            }
        }
    }

    /// <summary>
    /// Gets the resources passed to <see cref="Destroy"/>, in call order.
    /// </summary>
    internal IReadOnlyList<GpuAssetResource> DestroyCalls {
        get {
            lock (_gate) {
                return [.. _destroyCalls];
            }
        }
    }

    /// <summary>
    /// Gets the signal set after the first <see cref="Restore"/> call replaced the resource handles.
    /// </summary>
    internal ManualResetEventSlim Restored => _restored;

    /// <summary>
    /// Gets the auto-reset signal set when an upload starts or a deferred operation finishes.
    /// </summary>
    internal WaitHandle Progress => _progress;

    /// <summary>
    /// Gets the next handle the default upload and restore behavior uses.
    /// </summary>
    /// <returns>A fresh non-zero handle.</returns>
    internal long NextHandle() => Interlocked.Increment(ref _nextHandle);

    /// <summary>
    /// Waits until at least <paramref name="index"/> uploads started and returns that operation.
    /// </summary>
    /// <param name="index">The zero-based upload index.</param>
    /// <param name="timeout">The maximum wait.</param>
    /// <returns>The started operation.</returns>
    /// <exception cref="TimeoutException">The upload did not start in time.</exception>
    internal FakeGpuAssetUploadOperation WaitForUpload(int index, TimeSpan timeout) {
        FakeGpuAssetUploadOperation? operation = null;
        if (!SpinWait.SpinUntil(
            () => {
                lock (_gate) {
                    if (_operations.Count <= index) {
                        return false;
                    }

                    operation = _operations[index];
                    return true;
                }
            },
            timeout)) {
            throw new TimeoutException($"The fake GPU worker did not start upload {index}.");
        }

        return operation!;
    }

    /// <summary>
    /// Waits until the first upload started and returns it.
    /// </summary>
    /// <param name="timeout">The maximum wait.</param>
    /// <returns>The started operation.</returns>
    internal FakeGpuAssetUploadOperation WaitForUpload(TimeSpan timeout) => WaitForUpload(0, timeout);

    /// <inheritdoc />
    public GpuAssetUploadOperation BeginUpload(AssetGpuUpload upload) {
        ArgumentNullException.ThrowIfNull(upload);

        lock (_gate) {
            _uploadCalls.Add(upload);
        }

        if (UploadException is not null) {
            _progress.Set();
            throw UploadException;
        }

        GpuAssetResource resource = UploadHandler is not null
            ? UploadHandler(upload)
            : new FakeGpuAssetResource(upload, NextHandle());
        FakeGpuAssetUploadOperation operation = new(upload, resource, complete: !DeferUploads, () => _progress.Set());
        lock (_gate) {
            _operations.Add(operation);
        }

        _progress.Set();
        return operation;
    }

    /// <inheritdoc />
    public bool TryComplete(
        GpuAssetUploadOperation operation,
        out GpuAssetResource? resource,
        out Exception? error) {
        FakeGpuAssetUploadOperation fake = RequireOperation(operation);
        if (!fake.IsFinished) {
            resource = null;
            error = null;
            return false;
        }

        resource = fake.IsCanceled ? null : fake.Resource;
        error = fake.Error;
        return true;
    }

    /// <inheritdoc />
    public void Cancel(GpuAssetUploadOperation operation) {
        FakeGpuAssetUploadOperation fake = RequireOperation(operation);
        if (!fake.TryMarkCanceled()) {
            return;
        }

        lock (_gate) {
            _cancelCalls.Add(operation);
        }

        _progress.Set();
    }

    /// <inheritdoc />
    public GpuAssetResource Restore(AssetGpuUpload upload, GpuAssetResource previous) {
        lock (_gate) {
            _restoreCalls.Add(upload);
        }

        if (RestoreException is not null) {
            _restored.Set();
            throw RestoreException;
        }

        GpuAssetResource restored = RestoreHandler is not null
            ? RestoreHandler(upload, previous)
            : previous;
        if (ReferenceEquals(restored, previous)) {
            previous.PublishHandle(NextHandle());
        }

        _restored.Set();
        return restored;
    }

    /// <inheritdoc />
    public void Destroy(GpuAssetResource resource) {
        lock (_gate) {
            _destroyCalls.Add(resource);
        }
    }

    private static FakeGpuAssetUploadOperation RequireOperation(GpuAssetUploadOperation operation) {
        ArgumentNullException.ThrowIfNull(operation);

        return operation as FakeGpuAssetUploadOperation
            ?? throw new InvalidOperationException(
                $"The fake GPU worker cannot handle a {operation.GetType().Name} operation.");
    }
}
