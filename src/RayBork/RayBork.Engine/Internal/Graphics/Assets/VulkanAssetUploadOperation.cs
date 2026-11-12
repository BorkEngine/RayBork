using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace RayBork.Internal;

/// <summary>
/// One Vulkan upload that was submitted to the graphics queue and may still be copying.
/// </summary>
/// <remarks>
/// <para>
/// A texture or mesh operation owns the destination resource, the staging buffer and its memory, and
/// the one-shot command pool and fence of the submission. The worker adds the destination resource to
/// its live set and frees the staging and submission objects only when the fence signals. A shader
/// operation records no GPU copy, so its first <see cref="IAssetGpuWorker.TryComplete"/> poll finishes
/// it immediately.
/// </para>
/// <para>
/// The operation is render-thread affine. <see cref="ClearTransients"/> makes the cleanup idempotent:
/// after the worker freed the staging and submission objects it clears the handles, and
/// <see cref="ClearResource"/> clears the destination resource after its destruction, so a later
/// <see cref="IAssetGpuWorker.Cancel"/> cannot destroy anything twice.
/// </para>
/// </remarks>
internal sealed class VulkanAssetUploadOperation : GpuAssetUploadOperation {
    private int _finished;
    private int _canceled;

    /// <summary>
    /// Initializes an upload operation.
    /// </summary>
    /// <param name="upload">The retained upload description.</param>
    /// <param name="device">The device that owns the submission and the created objects.</param>
    /// <param name="submission">The submitted command pool and fence; default when no copy is needed.</param>
    /// <param name="stagingBuffer">The staging buffer to free after the copy, or a default handle.</param>
    /// <param name="stagingMemory">The staging memory to free after the copy, or a default handle.</param>
    /// <param name="resource">The created destination resource.</param>
    internal VulkanAssetUploadOperation(
        AssetGpuUpload upload,
        VulkanDevice device,
        VulkanAssetUploadCommands.UploadSubmission submission,
        Buffer stagingBuffer,
        DeviceMemory stagingMemory,
        VulkanAssetResource resource)
        : base(upload) {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(resource);

        Device = device;
        Submission = submission;
        StagingBuffer = stagingBuffer;
        StagingMemory = stagingMemory;
        Resource = resource;
    }

    /// <summary>
    /// Gets the device that owns the submission. It is captured at begin time so cancellation never
    /// touches a recreated device.
    /// </summary>
    internal VulkanDevice Device { get; }

    /// <summary>
    /// Gets the submitted command pool and fence.
    /// </summary>
    internal VulkanAssetUploadCommands.UploadSubmission Submission { get; private set; }

    /// <summary>
    /// Gets the staging buffer, or a default handle when the upload records no GPU copy.
    /// </summary>
    internal Buffer StagingBuffer { get; private set; }

    /// <summary>
    /// Gets the staging memory, or a default handle when the upload records no GPU copy.
    /// </summary>
    internal DeviceMemory StagingMemory { get; private set; }

    /// <summary>
    /// Gets whether the upload records a GPU copy that must finish before the resource is valid.
    /// </summary>
    internal bool HasCopy => Submission.Fence.Handle != 0;

    /// <summary>
    /// Gets the created destination resource. It is not in the worker's live set until the copy
    /// finished or the worker canceled the operation.
    /// </summary>
    internal VulkanAssetResource? Resource { get; private set; }

    /// <summary>
    /// Gets the failure the operation finished with, or <see langword="null"/>.
    /// </summary>
    internal Exception? Error { get; private set; }

    /// <summary>
    /// Gets whether the operation finished, successfully or with an error.
    /// </summary>
    internal bool IsFinished => Volatile.Read(ref _finished) != 0;

    /// <summary>
    /// Gets whether the operation was canceled.
    /// </summary>
    internal bool IsCanceled => Volatile.Read(ref _canceled) != 0;

    /// <summary>
    /// Marks the operation finished successfully.
    /// </summary>
    internal void MarkFinished() => Volatile.Write(ref _finished, 1);

    /// <summary>
    /// Marks the operation finished with an error.
    /// </summary>
    /// <param name="error">The failure to report to the bridge.</param>
    internal void MarkFailed(Exception error) {
        ArgumentNullException.ThrowIfNull(error);

        Error = error;
        MarkFinished();
    }

    /// <summary>
    /// Marks the operation canceled so a repeated cancel is a no-op.
    /// </summary>
    /// <returns><see langword="true"/> for the first call; <see langword="false"/> afterwards.</returns>
    internal bool TryMarkCanceled() => Interlocked.Exchange(ref _canceled, 1) == 0;

    /// <summary>
    /// Clears the staging and submission handles after the worker released them. A later
    /// <see cref="IAssetGpuWorker.Cancel"/> then cannot destroy them twice.
    /// </summary>
    internal void ClearTransients() {
        Submission = default;
        StagingBuffer = default;
        StagingMemory = default;
    }

    /// <summary>
    /// Clears the destination resource after the worker destroyed it.
    /// </summary>
    internal void ClearResource() => Resource = null;
}
