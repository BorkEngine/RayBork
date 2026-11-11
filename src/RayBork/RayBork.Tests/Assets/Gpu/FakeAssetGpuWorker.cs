using RayBork.Internal;

namespace RayBork.Tests.Assets.Gpu;

/// <summary>
/// An <see cref="IAssetGpuWorker"/> that records every call and lets a test inject failures or a
/// custom restore result.
/// </summary>
/// <remarks>
/// The worker is called from the render thread in integration tests and from the test thread in unit
/// tests, so the record lists are guarded by a lock. <see cref="Upload"/> and <see cref="Restore"/>
/// complete synchronously, which keeps the tests deterministic.
/// </remarks>
internal sealed class FakeAssetGpuWorker : IAssetGpuWorker {
    private readonly Lock _gate = new();
    private readonly List<AssetGpuUpload> _uploadCalls = [];
    private readonly List<AssetGpuUpload> _restoreCalls = [];
    private readonly List<GpuAssetResource> _destroyCalls = [];
    private readonly ManualResetEventSlim _restored = new(false);

    private long _nextHandle = 1;

    /// <summary>
    /// Gets or sets an exception thrown by <see cref="Upload"/>.
    /// </summary>
    internal Exception? UploadException { get; set; }

    /// <summary>
    /// Gets or sets an exception thrown by <see cref="Restore"/>.
    /// </summary>
    internal Exception? RestoreException { get; set; }

    /// <summary>
    /// Gets or sets an optional replacement for the default upload behavior.
    /// </summary>
    internal Func<AssetGpuUpload, GpuAssetResource>? UploadHandler { get; set; }

    /// <summary>
    /// Gets or sets an optional replacement for the default restore behavior.
    /// </summary>
    internal Func<AssetGpuUpload, GpuAssetResource, GpuAssetResource>? RestoreHandler { get; set; }

    /// <summary>
    /// Gets the number of <see cref="Upload"/> calls.
    /// </summary>
    internal int UploadCallCount {
        get {
            lock (_gate) {
                return _uploadCalls.Count;
            }
        }
    }

    /// <summary>
    /// Gets the uploads passed to <see cref="Upload"/>, in call order.
    /// </summary>
    internal IReadOnlyList<AssetGpuUpload> UploadCalls {
        get {
            lock (_gate) {
                return [.. _uploadCalls];
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
    /// Gets the next handle the default upload and restore behavior uses.
    /// </summary>
    /// <returns>A fresh non-zero handle.</returns>
    internal long NextHandle() => Interlocked.Increment(ref _nextHandle);

    /// <inheritdoc />
    public GpuAssetResource Upload(AssetGpuUpload upload) {
        lock (_gate) {
            _uploadCalls.Add(upload);
        }

        if (UploadException is not null) {
            throw UploadException;
        }

        return UploadHandler is not null
            ? UploadHandler(upload)
            : new FakeGpuAssetResource(upload, NextHandle());
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
}
