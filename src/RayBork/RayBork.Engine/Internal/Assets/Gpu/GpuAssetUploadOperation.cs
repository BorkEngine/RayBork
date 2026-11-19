namespace RayBork.Internal;

/// <summary>
/// One GPU upload that was started on the render thread and may still be copying on the device.
/// </summary>
/// <remarks>
/// The bridge completes it with <see cref="IAssetGpuWorker.TryComplete"/> or cancels it with
/// <see cref="IAssetGpuWorker.Cancel"/>; it is single-use and retains its <see cref="Upload"/>.
/// </remarks>
internal abstract class GpuAssetUploadOperation
{
    private protected GpuAssetUploadOperation(AssetGpuUpload upload)
    {
        ArgumentNullException.ThrowIfNull(upload);

        Upload = upload;
    }

    /// <summary>Gets the upload description this operation creates.</summary>
    internal AssetGpuUpload Upload { get; }
}
