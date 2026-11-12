namespace RayBork.Internal;

/// <summary>
/// One GPU upload that was started on the render thread and may still be copying on the device.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IAssetGpuWorker.BeginUpload"/> returns the operation without waiting; the bridge polls
/// it with <see cref="IAssetGpuWorker.TryComplete"/> on later render-loop turns and destroys it with
/// <see cref="IAssetGpuWorker.Cancel"/> when the caller canceled or the bridge is closing. The
/// operation retains the <see cref="Upload"/> so it can finish the request without the bridge keeping
/// the payload separately.
/// </para>
/// <para>
/// An operation is owned by the render thread from <c>BeginUpload</c> to <c>TryComplete</c> or
/// <c>Cancel</c>. It is single-use: the bridge completes each request exactly once and never polls a
/// finished operation again.
/// </para>
/// </remarks>
internal abstract class GpuAssetUploadOperation {
    private protected GpuAssetUploadOperation(AssetGpuUpload upload) {
        ArgumentNullException.ThrowIfNull(upload);

        Upload = upload;
    }

    /// <summary>
    /// Gets the upload description this operation creates.
    /// </summary>
    internal AssetGpuUpload Upload { get; }
}
