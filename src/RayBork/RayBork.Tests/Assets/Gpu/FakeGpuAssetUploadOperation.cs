using RayBork.Internal;

namespace RayBork.Tests.Assets.Gpu;

/// <summary>
/// One fake GPU upload. It completes immediately by default or stays pending until a test calls
/// <see cref="Complete"/> or <see cref="Fail"/> when the worker is in deferred mode.
/// </summary>
internal sealed class FakeGpuAssetUploadOperation : GpuAssetUploadOperation {
    private readonly Action _onProgress;
    private int _finished;
    private int _canceled;

    /// <summary>
    /// Initializes an operation for <paramref name="upload"/>.
    /// </summary>
    /// <param name="upload">The retained upload description.</param>
    /// <param name="resource">The resource the operation reports on success.</param>
    /// <param name="complete">Whether the operation is finished immediately.</param>
    /// <param name="onProgress">
    /// Signals the temporary render-loop pump that the operation state changed, so a deferred test
    /// never has to poll for it.
    /// </param>
    internal FakeGpuAssetUploadOperation(
        AssetGpuUpload upload,
        GpuAssetResource resource,
        bool complete,
        Action onProgress)
        : base(upload) {
        ArgumentNullException.ThrowIfNull(onProgress);

        Resource = resource;
        _onProgress = onProgress;
        if (complete) {
            MarkFinished();
        }
    }

    /// <summary>
    /// Gets the resource the operation reports on success.
    /// </summary>
    internal GpuAssetResource? Resource { get; }

    /// <summary>
    /// Gets the failure the operation reports, or <see langword="null"/>.
    /// </summary>
    internal Exception? Error { get; private set; }

    /// <summary>
    /// Gets whether the operation finished.
    /// </summary>
    internal bool IsFinished => Volatile.Read(ref _finished) != 0;

    /// <summary>
    /// Gets whether the worker canceled the operation.
    /// </summary>
    internal bool IsCanceled => Volatile.Read(ref _canceled) != 0;

    /// <summary>
    /// Finishes the operation successfully.
    /// </summary>
    internal void Complete() {
        MarkFinished();
        _onProgress();
    }

    /// <summary>
    /// Finishes the operation with <paramref name="error"/>.
    /// </summary>
    /// <param name="error">The failure the bridge observes.</param>
    internal void Fail(Exception error) {
        ArgumentNullException.ThrowIfNull(error);

        Error = error;
        MarkFinished();
        _onProgress();
    }

    /// <summary>
    /// Marks the operation canceled.
    /// </summary>
    /// <returns><see langword="true"/> for the first call; <see langword="false"/> afterwards.</returns>
    internal bool TryMarkCanceled() {
        if (Interlocked.Exchange(ref _canceled, 1) != 0) {
            return false;
        }

        _onProgress();
        return true;
    }

    private void MarkFinished() => Volatile.Write(ref _finished, 1);
}
