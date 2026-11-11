using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// Runs one short-lived upload submission on the render thread: a transient command pool, one command
/// buffer, and one fence, submitted to <see cref="VulkanDevice.GraphicsQueue"/> and waited before the
/// call returns.
/// </summary>
/// <remarks>
/// Asset uploads are infrequent, so a dedicated one-shot pool per upload is simpler and safer than
/// sharing the renderer's frame command pool: the upload can never collide with a frame command
/// buffer, and the pool is destroyed in a <c>finally</c> block after the fence. The caller records the
/// copies and barriers; this helper only owns the submission objects. Render-thread only.
/// </remarks>
internal static unsafe class VulkanAssetUploadCommands {
    private const ulong WAIT_TIMEOUT = ulong.MaxValue;

    /// <summary>
    /// Records and synchronously submits one upload command buffer.
    /// </summary>
    /// <param name="device">The device that owns the submission.</param>
    /// <param name="logger">The logger for best-effort cleanup failures.</param>
    /// <param name="canonicalPath">The canonical asset path, for diagnostics.</param>
    /// <param name="record">Records the copies and barriers into the command buffer.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    /// <exception cref="AssetLoadException">
    /// A Vulkan command failed. The diagnostic code is <c>RB1010</c>.
    /// </exception>
    internal static void RecordAndSubmit(
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
            VulkanAssetErrors.ThrowIfFailed(
                vulkan.WaitForFences(deviceHandle, 1, &fence, true, WAIT_TIMEOUT),
                canonicalPath,
                "vkWaitForFences");
        } finally {
            DestroyFence(vulkan, deviceHandle, fence, logger);
            DestroyCommandPool(vulkan, deviceHandle, commandPool, logger);
        }
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
}
