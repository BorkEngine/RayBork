using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace RayBork.Internal;

/// <summary>One submitted Vulkan upload that may still be copying.</summary>
/// <remarks>
/// It owns the destination resource, the staging buffer and its memory, and the submission's
/// command pool and fence, which the worker frees once the fence signals.
/// <see cref="ClearTransients"/> and <see cref="ClearResource"/> keep cleanup idempotent.
/// </remarks>
internal sealed class VulkanAssetUploadOperation : GpuAssetUploadOperation
{
    private int _finished;
    private int _canceled;

    /// <summary>Initializes an upload operation.</summary>
    internal VulkanAssetUploadOperation(
        AssetGpuUpload upload,
        VulkanDevice device,
        VulkanAssetUploadCommands.UploadSubmission submission,
        Buffer stagingBuffer,
        DeviceMemory stagingMemory,
        VulkanAssetResource resource
    )
        : base(upload)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(resource);

        Device = device;
        Submission = submission;
        StagingBuffer = stagingBuffer;
        StagingMemory = stagingMemory;
        Resource = resource;
    }

    /// <summary>
    /// Gets the device captured at begin time, so cancellation never touches a recreated device.
    /// </summary>
    internal VulkanDevice Device { get; }

    /// <summary>Gets the submitted command pool and fence.</summary>
    internal VulkanAssetUploadCommands.UploadSubmission Submission { get; private set; }

    /// <summary>Gets the staging buffer, or a default handle when no copy is recorded.</summary>
    internal Buffer StagingBuffer { get; private set; }

    /// <summary>Gets the staging memory, or a default handle when no copy is recorded.</summary>
    internal DeviceMemory StagingMemory { get; private set; }

    /// <summary>
    /// Gets whether the upload records a GPU copy that must finish before the resource is valid.
    /// </summary>
    internal bool HasCopy => Submission.Fence.Handle != 0;

    /// <summary>Gets the destination resource, or <see langword="null"/> once it was destroyed.</summary>
    internal VulkanAssetResource? Resource { get; private set; }

    /// <summary>Gets the failure the operation finished with, or <see langword="null"/>.</summary>
    internal Exception? Error { get; private set; }

    /// <summary>Gets whether the operation finished, successfully or with an error.</summary>
    internal bool IsFinished => Volatile.Read(ref _finished) != 0;

    /// <summary>Gets whether the operation was canceled.</summary>
    internal bool IsCanceled => Volatile.Read(ref _canceled) != 0;

    /// <summary>Marks the operation finished successfully.</summary>
    internal void MarkFinished() => Volatile.Write(ref _finished, 1);

    /// <summary>Marks the operation finished with the given failure.</summary>
    internal void MarkFailed(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);

        Error = error;
        MarkFinished();
    }

    /// <summary>Marks the operation canceled; only the first call returns <see langword="true"/>.</summary>
    internal bool TryMarkCanceled() => Interlocked.Exchange(ref _canceled, 1) == 0;

    /// <summary>Clears the staging and submission handles after the worker freed them.</summary>
    internal void ClearTransients()
    {
        Submission = default;
        StagingBuffer = default;
        StagingMemory = default;
    }

    /// <summary>Clears the destination resource after the worker destroyed it.</summary>
    internal void ClearResource() => Resource = null;
}
