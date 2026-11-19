using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// Submits one upload to <see cref="VulkanDevice.GraphicsQueue"/>: a transient pool, one command
/// buffer, one fence.
/// </summary>
/// <remarks>
/// The pool is never the renderer's frame pool, so an upload cannot collide with frame commands.
/// <see cref="RecordAndSubmit"/> queues without waiting, <see cref="TryComplete"/> polls the fence,
/// and <see cref="Wait"/> serves device recovery and cancellation.
/// </remarks>
internal static unsafe class VulkanAssetUploadCommands
{
    private const ulong WAIT_TIMEOUT = ulong.MaxValue;

    /// <summary>Records and submits one upload command buffer without waiting.</summary>
    /// <exception cref="AssetLoadException">
    /// A Vulkan command failed (<c>RB1010</c>); partial submission objects are destroyed first.
    /// </exception>
    internal static UploadSubmission RecordAndSubmit(
        VulkanDevice device,
        ILogger logger,
        string canonicalPath,
        Action<Vk, CommandBuffer> record
    )
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(record);

        Vk vulkan = device.Vulkan;
        Device deviceHandle = device.Device;
        CommandPool commandPool = default;
        Fence fence = default;
        bool submitted = false;
        try
        {
            CommandPoolCreateInfo poolInfo = new()
            {
                SType = StructureType.CommandPoolCreateInfo,
                Flags = CommandPoolCreateFlags.TransientBit,
                QueueFamilyIndex = device.Capabilities.GraphicsQueueFamily,
            };
            VulkanAssetErrors.ThrowIfFailed(
                vulkan.CreateCommandPool(deviceHandle, &poolInfo, null, out commandPool),
                canonicalPath,
                "vkCreateCommandPool"
            );

            CommandBufferAllocateInfo allocateInfo = new()
            {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = commandPool,
                Level = CommandBufferLevel.Primary,
                CommandBufferCount = 1,
            };
            VulkanAssetErrors.ThrowIfFailed(
                vulkan.AllocateCommandBuffers(
                    deviceHandle,
                    &allocateInfo,
                    out CommandBuffer commandBuffer
                ),
                canonicalPath,
                "vkAllocateCommandBuffers"
            );

            CommandBufferBeginInfo beginInfo = new()
            {
                SType = StructureType.CommandBufferBeginInfo,
                Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
            };
            VulkanAssetErrors.ThrowIfFailed(
                vulkan.BeginCommandBuffer(commandBuffer, &beginInfo),
                canonicalPath,
                "vkBeginCommandBuffer"
            );
            record(vulkan, commandBuffer);
            VulkanAssetErrors.ThrowIfFailed(
                vulkan.EndCommandBuffer(commandBuffer),
                canonicalPath,
                "vkEndCommandBuffer"
            );

            FenceCreateInfo fenceInfo = new() { SType = StructureType.FenceCreateInfo };
            VulkanAssetErrors.ThrowIfFailed(
                vulkan.CreateFence(deviceHandle, &fenceInfo, null, out fence),
                canonicalPath,
                "vkCreateFence"
            );

            SubmitInfo submitInfo = new()
            {
                SType = StructureType.SubmitInfo,
                CommandBufferCount = 1,
                PCommandBuffers = &commandBuffer,
            };
            VulkanAssetErrors.ThrowIfFailed(
                vulkan.QueueSubmit(device.GraphicsQueue, 1, &submitInfo, fence),
                canonicalPath,
                "vkQueueSubmit"
            );
            submitted = true;
            return new UploadSubmission(commandPool, fence);
        }
        finally
        {
            if (!submitted)
            {
                DestroyFence(vulkan, deviceHandle, fence, logger);
                DestroyCommandPool(vulkan, deviceHandle, commandPool, logger);
            }
        }
    }

    /// <summary>Polls whether the submission finished, without blocking.</summary>
    /// <returns><see langword="true"/> once signaled; <see langword="false"/> if pending.</returns>
    /// <exception cref="AssetLoadException">The fence query failed (<c>RB1010</c>).</exception>
    internal static bool TryComplete(
        VulkanDevice device,
        UploadSubmission submission,
        string canonicalPath
    )
    {
        ArgumentNullException.ThrowIfNull(device);

        Result result = device.Vulkan.GetFenceStatus(device.Device, submission.Fence);
        if (result == Result.NotReady)
        {
            return false;
        }

        VulkanAssetErrors.ThrowIfFailed(result, canonicalPath, "vkGetFenceStatus");
        return true;
    }

    /// <summary>Waits for the submission, for device recovery and cancellation only.</summary>
    /// <exception cref="AssetLoadException">The fence wait failed (<c>RB1010</c>).</exception>
    internal static void Wait(
        VulkanDevice device,
        UploadSubmission submission,
        string canonicalPath
    )
    {
        ArgumentNullException.ThrowIfNull(device);

        Fence fence = submission.Fence;
        VulkanAssetErrors.ThrowIfFailed(
            device.Vulkan.WaitForFences(device.Device, 1, &fence, true, WAIT_TIMEOUT),
            canonicalPath,
            "vkWaitForFences"
        );
    }

    /// <summary>Destroys the fence and command pool of a finished or abandoned submission.</summary>
    internal static void Destroy(VulkanDevice device, ILogger logger, UploadSubmission submission)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(logger);

        DestroyFence(device.Vulkan, device.Device, submission.Fence, logger);
        DestroyCommandPool(device.Vulkan, device.Device, submission.CommandPool, logger);
    }

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Fence destruction runs on teardown paths where the device may already be lost; the failure is logged instead of thrown."
    )]
    private static void DestroyFence(Vk vulkan, Device device, Fence fence, ILogger logger)
    {
        if (fence.Handle == 0)
        {
            return;
        }

        try
        {
            vulkan.DestroyFence(device, fence, null);
        }
        catch (Exception exception)
        {
            // Cleanup runs while an upload error may already be propagating; a failed destroy must
            // not mask it. The fence belongs to a stopped upload, so logging is the only action.
            logger.LogWarning(exception, "The Vulkan asset upload fence could not be destroyed.");
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Command-pool destruction runs on teardown paths where the device may already be lost; the failure is logged instead of thrown."
    )]
    private static void DestroyCommandPool(
        Vk vulkan,
        Device device,
        CommandPool commandPool,
        ILogger logger
    )
    {
        if (commandPool.Handle == 0)
        {
            return;
        }

        try
        {
            // Destroying the pool also frees the upload command buffer allocated from it.
            vulkan.DestroyCommandPool(device, commandPool, null);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "The Vulkan asset upload command pool could not be destroyed."
            );
        }
    }

    /// <summary>The command pool and fence of one submitted upload.</summary>
    internal readonly record struct UploadSubmission(CommandPool CommandPool, Fence Fence);
}
