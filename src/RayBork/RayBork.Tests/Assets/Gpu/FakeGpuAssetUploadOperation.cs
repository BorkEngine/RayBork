using RayBork.Internal;

namespace RayBork.Tests.Assets.Gpu;

/// <summary>A fake GPU upload, completed immediately or by the test when deferred.</summary>
internal sealed class FakeGpuAssetUploadOperation : GpuAssetUploadOperation
{
    private readonly Action _onProgress;
    private int _finished;
    private int _canceled;

    /// <summary>Initializes a fake operation for one upload.</summary>
    /// <param name="upload">The retained upload description.</param>
    /// <param name="resource">The resource reported on success.</param>
    /// <param name="complete">Whether the operation finishes immediately.</param>
    /// <param name="onProgress">Signals the pump that the operation state changed.</param>
    internal FakeGpuAssetUploadOperation(
        AssetGpuUpload upload,
        GpuAssetResource resource,
        bool complete,
        Action onProgress
    )
        : base(upload)
    {
        ArgumentNullException.ThrowIfNull(onProgress);

        Resource = resource;
        _onProgress = onProgress;
        if (complete)
        {
            MarkFinished();
        }
    }

    /// <summary>Gets the resource the operation reports on success.</summary>
    internal GpuAssetResource? Resource { get; }

    /// <summary>Gets the reported failure, or <see langword="null"/>.</summary>
    internal Exception? Error { get; private set; }

    /// <summary>Gets whether the operation finished.</summary>
    internal bool IsFinished => Volatile.Read(ref _finished) != 0;

    /// <summary>Gets whether the worker canceled the operation.</summary>
    internal bool IsCanceled => Volatile.Read(ref _canceled) != 0;

    /// <summary>Finishes the operation successfully.</summary>
    internal void Complete()
    {
        MarkFinished();
        _onProgress();
    }

    /// <summary>Finishes the operation with a failure.</summary>
    internal void Fail(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);

        Error = error;
        MarkFinished();
        _onProgress();
    }

    /// <summary>Marks the operation canceled; the first call wins.</summary>
    internal bool TryMarkCanceled()
    {
        if (Interlocked.Exchange(ref _canceled, 1) != 0)
        {
            return false;
        }

        _onProgress();
        return true;
    }

    private void MarkFinished() => Volatile.Write(ref _finished, 1);
}
