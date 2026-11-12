using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// Runs one short-lived upload submission on the render thread: a transient command pool, one command
/// buffer, and one fence, submitted to <see cref="VulkanDevice.GraphicsQueue"/> without waiting.
/// </summary>
/// <remarks>
/// <para>
/// Asset uploads are infrequent, so a dedicated one-shot pool per upload is simpler and safer than
/// sharing the renderer's frame command pool: the upload can never collide with a frame command
/// buffer. The caller records the copies and barriers; this helper owns the submission objects.
/// <see cref="RecordAndSubmit"/> returns as soon as the queue accepted the submission;
/// <see cref="TryComplete"/> polls the fence without blocking, and <see cref="Wait"/> is reserved for
/// the rare device-recovery and cancellation paths. <see cref="Destroy"/> frees the fence and pool
/// after the copy finished or was abandoned. Render-thread only.
/// </para>
/// </remarks>
internal static unsafe class VulkanAssetUploadCommands {
    private const ulong WAIT_TIMEOUT = ulong.MaxValue;

    /// <summary>
    /// Records and submits one upload command buffer without waiting for it to finish.
    /// </summary>
    /// <param name="device">The device that owns the submission.</param>
    /// <param name="logger">The logger for best-effort cleanup failures.</param>
    /// <param name="canonicalPath">The canonical asset path, for diagnostics.</param>
    /// <param name="record">Records the copies and barriers into the command buffer.</param>
    /// <returns>The submission objects that <see cref="TryComplete"/> or <see cref="Destroy"/> consume.</returns>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    /// <exception cref="AssetLoadException">
    /// A Vulkan command failed. The diagnostic code is <c>RB1010</c>. The helper destroyed its partial
    /// submission objects before throwing.
    /// </exception>
    internal static UploadSubmission RecordAndSubmit(
        VulkanDevice device,
        ILogger logger,
        string canonicalPath,
        Action<Vk, CommandBuffer> record) {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(record);

        Vk vulkan = device.Vulkan;
        Device deviceHandle = device.Device;
        CommandPool commandPool = default;
        Fence fence = default;
        bool submitted = false;
        try {
            CommandPoolCreateInfo poolInfo = new() {
                SType = StructureType.CommandPoolCreateInfo,
                Flags = CommandPoolCreateFlags.TransientBit,
                QueueFamilyIndex = device.Capabilities.GraphicsQueueFamily,
            };
            VulkanAssetErrors.ThrowIfFailed(
                vulkan.CreateCommandPool(deviceHandle, &poolInfo, null, out commandPool),
                canonicalPath,
                "vkCreateCommandPool");

            CommandBufferAllocateInfo allocateInfo = new() {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = commandPool,
                Level = CommandBufferLevel.Primary,
                CommandBufferCount = 1,
            };
            VulkanAssetErrors.ThrowIfFailed(
                vulkan.AllocateCommandBuffers(deviceHandle, &allocateInfo, out CommandBuffer commandBuffer),
                canonicalPath,
                "vkAllocateCommandBuffers");

            CommandBufferBeginInfo beginInfo = new() {
                SType = StructureType.CommandBufferBeginInfo,
                Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
            };
            VulkanAssetErrors.ThrowIfFailed(
                vulkan.BeginCommandBuffer(commandBuffer, &beginInfo),
                canonicalPath,
                "vkBeginCommandBuffer");
            record(vulkan, commandBuffer);
            VulkanAssetErrors.ThrowIfFailed(
                vulkan.EndCommandBuffer(commandBuffer),
                canonicalPath,
                "vkEndCommandBuffer");

            FenceCreateInfo fenceInfo = new() { SType = StructureType.FenceCreateInfo };
            VulkanAssetErrors.ThrowIfFailed(
                vulkan.CreateFence(deviceHandle, &fenceInfo, null, out fence),
                canonicalPath,
                "vkCreateFence");

            SubmitInfo submitInfo = new() {
                SType = StructureType.SubmitInfo,
                CommandBufferCount = 1,
                PCommandBuffers = &commandBuffer,
            };
            VulkanAssetErrors.ThrowIfFailed(
                vulkan.QueueSubmit(device.GraphicsQueue, 1, &submitInfo, fence),
                canonicalPath,
                "vkQueueSubmit");
            submitted = true;
            return new UploadSubmission(commandPool, fence);
        } finally {
            if (!submitted) {
                DestroyFence(vulkan, deviceHandle, fence, logger);
                DestroyCommandPool(vulkan, deviceHandle, commandPool, logger);
            }
        }
    }

    /// <summary>
    /// Polls whether the upload submission finished, without blocking.
    /// </summary>
    /// <param name="device">The device that owns the submission.</param>
    /// <param name="submission">The submission objects.</param>
    /// <param name="canonicalPath">The canonical asset path, for diagnostics.</param>
    /// <returns><see langword="true"/> once the fence signaled; <see langword="false"/> while the copy is pending.</returns>
    /// <exception cref="AssetLoadException">
    /// The fence status could not be queried. The diagnostic code is <c>RB1010</c>.
    /// </exception>
    internal static bool TryComplete(
        VulkanDevice device,
        UploadSubmission submission,
        string canonicalPath) {
        ArgumentNullException.ThrowIfNull(device);

        Result result = device.Vulkan.GetFenceStatus(device.Device, submission.Fence);
        if (result == Result.NotReady) {
            return false;
        }

        VulkanAssetErrors.ThrowIfFailed(result, canonicalPath, "vkGetFenceStatus");
        return true;
    }

    /// <summary>
    /// Waits for the upload submission to finish. Runs only during a device recovery or cancellation.
    /// </summary>
    /// <param name="device">The device that owns the submission.</param>
    /// <param name="submission">The submission objects.</param>
    /// <param name="canonicalPath">The canonical asset path, for diagnostics.</param>
    /// <exception cref="AssetLoadException">
    /// The fence wait failed. The diagnostic code is <c>RB1010</c>.
    /// </exception>
    internal static void Wait(
        VulkanDevice device,
        UploadSubmission submission,
        string canonicalPath) {
        ArgumentNullException.ThrowIfNull(device);

        Fence fence = submission.Fence;
        VulkanAssetErrors.ThrowIfFailed(
            device.Vulkan.WaitForFences(device.Device, 1, &fence, true, WAIT_TIMEOUT),
            canonicalPath,
            "vkWaitForFences");
    }

    /// <summary>
    /// Destroys the fence and command pool of a finished or abandoned submission. Best effort.
    /// </summary>
    /// <param name="device">The device that owns the submission.</param>
    /// <param name="logger">The logger for cleanup failures.</param>
    /// <param name="submission">The submission objects; default handles are ignored.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    internal static void Destroy(VulkanDevice device, ILogger logger, UploadSubmission submission) {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(logger);

        DestroyFence(device.Vulkan, device.Device, submission.Fence, logger);
        DestroyCommandPool(device.Vulkan, device.Device, submission.CommandPool, logger);
    }

    private static void DestroyFence(Vk vulkan, Device device, Fence fence, ILogger logger) {
        if (fence.Handle == 0) {
            return;
        }

        try {
            vulkan.DestroyFence(device, fence, null);
        } catch (Exception exception) {
            // Cleanup runs while an upload error may already be propagating; a failed destroy must
            // not mask it. The fence belongs to a stopped upload, so logging is the only action.
            logger.LogWarning(exception, "The Vulkan asset upload fence could not be destroyed.");
        }
    }

    private static void DestroyCommandPool(Vk vulkan, Device device, CommandPool commandPool, ILogger logger) {
        if (commandPool.Handle == 0) {
            return;
        }

        try {
            // Destroying the pool also frees the upload command buffer allocated from it.
            vulkan.DestroyCommandPool(device, commandPool, null);
        } catch (Exception exception) {
            logger.LogWarning(exception, "The Vulkan asset upload command pool could not be destroyed.");
        }
    }

    /// <summary>
    /// The command pool and fence of one submitted upload.
    /// </summary>
    /// <param name="CommandPool">The transient command pool.</param>
    /// <param name="Fence">The fence signaled when the copy finished.</param>
    internal readonly record struct UploadSubmission(CommandPool CommandPool, Fence Fence);
}
