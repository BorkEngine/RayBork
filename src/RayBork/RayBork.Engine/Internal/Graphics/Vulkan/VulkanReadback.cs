using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace RayBork.Internal;

/// <summary>
/// Single-pixel readback probes for the scene target, used by GPU tests to assert clear and resolve
/// values numerically.
/// </summary>
/// <remarks>
/// <para>
/// These probes are diagnostics, not a renderer path: each call creates its own command pool, staging
/// buffer, and fence, submits one copy, and waits for the GPU. The call stalls the render thread and
/// must never be used per frame. Every temporary object is destroyed in a <c>finally</c> block so a
/// failed probe cannot leak.
/// </para>
/// <para>
/// The caller passes a device and scene target from a single backend and runs on that backend's
/// render thread. Nothing here is thread-safe.
/// </para>
/// </remarks>
internal static unsafe class VulkanReadback {
    private const ulong WAIT_TIMEOUT = ulong.MaxValue;
    private const uint COLOR_PIXEL_BYTES = 8;
    private const uint DEPTH_PIXEL_BYTES = 4;

    /// <summary>
    /// Reads one resolved linear scene color pixel.
    /// </summary>
    /// <param name="device">The device that owns the scene target.</param>
    /// <param name="target">The scene target to read.</param>
    /// <param name="x">The pixel column, in pixels from the left edge.</param>
    /// <param name="y">The pixel row, in pixels from the top edge.</param>
    /// <param name="r">The linear red component.</param>
    /// <param name="g">The linear green component.</param>
    /// <param name="b">The linear blue component.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The pixel lies outside the target extent.
    /// </exception>
    /// <exception cref="GraphicsException">
    /// A Vulkan object could not be created or the copy failed. The diagnostic code is <c>RB3001</c>.
    /// </exception>
    internal static void ReadSceneColorPixel(
        VulkanDevice device,
        VulkanSceneTarget target,
        uint x,
        uint y,
        out float r,
        out float g,
        out float b) {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(target);
        ValidatePixel(target, x, y);

        byte[] pixel = ReadPixel(
            device,
            target.ResolvedColorImage,
            ImageAspectFlags.ColorBit,
            ImageLayout.ShaderReadOnlyOptimal,
            COLOR_PIXEL_BYTES,
            x,
            y);
        r = (float)BitConverter.ToHalf(pixel, 0);
        g = (float)BitConverter.ToHalf(pixel, 2);
        b = (float)BitConverter.ToHalf(pixel, 4);
    }

    /// <summary>
    /// Reads one reverse-Z scene depth pixel.
    /// </summary>
    /// <param name="device">The device that owns the scene target.</param>
    /// <param name="target">The single-sample scene target to read.</param>
    /// <param name="x">The pixel column, in pixels from the left edge.</param>
    /// <param name="y">The pixel row, in pixels from the top edge.</param>
    /// <param name="depth">The stored depth value.</param>
    /// <exception cref="InvalidOperationException">
    /// The target is multisampled and has no single-sample depth attachment.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The pixel lies outside the target extent.
    /// </exception>
    /// <exception cref="GraphicsException">
    /// A Vulkan object could not be created or the copy failed. The diagnostic code is <c>RB3001</c>.
    /// </exception>
    internal static void ReadSceneDepthPixel(
        VulkanDevice device,
        VulkanSceneTarget target,
        uint x,
        uint y,
        out float depth) {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(target);
        if (target.SampleCount != 1) {
            throw new InvalidOperationException(
                $"The scene target is multisampled with {target.SampleCount} samples and has no single-sample depth attachment to read.");
        }

        ValidatePixel(target, x, y);
        byte[] pixel = ReadPixel(
            device,
            target.DepthImage,
            ImageAspectFlags.DepthBit,
            ImageLayout.DepthStencilAttachmentOptimal,
            DEPTH_PIXEL_BYTES,
            x,
            y);
        depth = BitConverter.ToSingle(pixel, 0);
    }

    private static byte[] ReadPixel(
        VulkanDevice device,
        Image image,
        ImageAspectFlags aspect,
        ImageLayout originalLayout,
        uint byteCount,
        uint x,
        uint y) {
        Vk vulkan = device.Vulkan;
        Device deviceHandle = device.Device;
        CommandPool commandPool = default;
        Buffer stagingBuffer = default;
        DeviceMemory stagingMemory = default;
        Fence fence = default;
        void* mapped = null;
        try {
            CommandPoolCreateInfo poolInfo = new() {
                SType = StructureType.CommandPoolCreateInfo,
                Flags = CommandPoolCreateFlags.TransientBit,
                QueueFamilyIndex = device.Capabilities.GraphicsQueueFamily,
            };
            ThrowIfFailed(vulkan.CreateCommandPool(deviceHandle, &poolInfo, null, out commandPool), "vkCreateCommandPool");

            CommandBufferAllocateInfo allocateInfo = new() {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = commandPool,
                Level = CommandBufferLevel.Primary,
                CommandBufferCount = 1,
            };
            ThrowIfFailed(
                vulkan.AllocateCommandBuffers(deviceHandle, &allocateInfo, out CommandBuffer commandBuffer),
                "vkAllocateCommandBuffers");

            BufferCreateInfo bufferInfo = new() {
                SType = StructureType.BufferCreateInfo,
                Size = byteCount,
                Usage = BufferUsageFlags.TransferDstBit,
                SharingMode = SharingMode.Exclusive,
            };
            ThrowIfFailed(vulkan.CreateBuffer(deviceHandle, &bufferInfo, null, out stagingBuffer), "vkCreateBuffer");
            MemoryRequirements requirements = vulkan.GetBufferMemoryRequirements(deviceHandle, stagingBuffer);
            PhysicalDeviceMemoryProperties memoryProperties = vulkan.GetPhysicalDeviceMemoryProperties(device.PhysicalDevice);
            MemoryAllocateInfo memoryInfo = new() {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = requirements.Size,
                MemoryTypeIndex = FindHostVisibleMemoryType(requirements.MemoryTypeBits, memoryProperties),
            };
            ThrowIfFailed(vulkan.AllocateMemory(deviceHandle, &memoryInfo, null, out stagingMemory), "vkAllocateMemory");
            ThrowIfFailed(vulkan.BindBufferMemory(deviceHandle, stagingBuffer, stagingMemory, 0), "vkBindBufferMemory");
            ThrowIfFailed(
                vulkan.MapMemory(deviceHandle, stagingMemory, 0, byteCount, MemoryMapFlags.None, &mapped),
                "vkMapMemory");

            CommandBufferBeginInfo beginInfo = new() {
                SType = StructureType.CommandBufferBeginInfo,
                Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
            };
            ThrowIfFailed(vulkan.BeginCommandBuffer(commandBuffer, &beginInfo), "vkBeginCommandBuffer");
            RecordCopy(vulkan, commandBuffer, image, aspect, originalLayout, stagingBuffer, x, y);
            ThrowIfFailed(vulkan.EndCommandBuffer(commandBuffer), "vkEndCommandBuffer");

            FenceCreateInfo fenceInfo = new() { SType = StructureType.FenceCreateInfo };
            ThrowIfFailed(vulkan.CreateFence(deviceHandle, &fenceInfo, null, out fence), "vkCreateFence");
            SubmitInfo submitInfo = new() {
                SType = StructureType.SubmitInfo,
                CommandBufferCount = 1,
                PCommandBuffers = &commandBuffer,
            };
            ThrowIfFailed(
                vulkan.QueueSubmit(device.GraphicsQueue, 1, &submitInfo, fence),
                "vkQueueSubmit");
            ThrowIfFailed(
                vulkan.WaitForFences(deviceHandle, 1, &fence, true, WAIT_TIMEOUT),
                "vkWaitForFences");

            byte[] pixel = new byte[byteCount];
            new ReadOnlySpan<byte>(mapped, (int)byteCount).CopyTo(pixel);
            return pixel;
        } finally {
            if (mapped is not null) {
                vulkan.UnmapMemory(deviceHandle, stagingMemory);
            }

            if (fence.Handle != 0) {
                vulkan.DestroyFence(deviceHandle, fence, null);
            }

            if (stagingBuffer.Handle != 0) {
                vulkan.DestroyBuffer(deviceHandle, stagingBuffer, null);
            }

            if (stagingMemory.Handle != 0) {
                vulkan.FreeMemory(deviceHandle, stagingMemory, null);
            }

            if (commandPool.Handle != 0) {
                vulkan.DestroyCommandPool(deviceHandle, commandPool, null);
            }
        }
    }

    private static void RecordCopy(
        Vk vulkan,
        CommandBuffer commandBuffer,
        Image image,
        ImageAspectFlags aspect,
        ImageLayout originalLayout,
        Buffer stagingBuffer,
        uint x,
        uint y) {
        bool depth = (aspect & ImageAspectFlags.DepthBit) != 0;
        PipelineStageFlags attachmentStage = depth
            ? PipelineStageFlags.LateFragmentTestsBit
            : PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.FragmentShaderBit;
        AccessFlags attachmentAccess = depth
            ? AccessFlags.DepthStencilAttachmentWriteBit
            : AccessFlags.ColorAttachmentWriteBit | AccessFlags.ShaderReadBit;
        ImageMemoryBarrier barrier = new() {
            SType = StructureType.ImageMemoryBarrier,
            SrcAccessMask = attachmentAccess,
            DstAccessMask = AccessFlags.TransferReadBit,
            OldLayout = originalLayout,
            NewLayout = ImageLayout.TransferSrcOptimal,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = new ImageSubresourceRange {
                AspectMask = aspect,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1,
            },
        };
        vulkan.CmdPipelineBarrier(
            commandBuffer,
            attachmentStage,
            PipelineStageFlags.TransferBit,
            0,
            0,
            null,
            0,
            null,
            1,
            &barrier);
        BufferImageCopy copy = new() {
            BufferOffset = 0,
            BufferRowLength = 0,
            BufferImageHeight = 0,
            ImageSubresource = new ImageSubresourceLayers {
                AspectMask = aspect,
                MipLevel = 0,
                BaseArrayLayer = 0,
                LayerCount = 1,
            },
            ImageOffset = new Offset3D { X = (int)x, Y = (int)y, Z = 0 },
            ImageExtent = new Extent3D { Width = 1, Height = 1, Depth = 1 },
        };
        vulkan.CmdCopyImageToBuffer(
            commandBuffer,
            image,
            ImageLayout.TransferSrcOptimal,
            stagingBuffer,
            1,
            &copy);

        ImageMemoryBarrier restore = barrier;
        restore.SrcAccessMask = AccessFlags.TransferReadBit;
        restore.DstAccessMask = attachmentAccess;
        restore.OldLayout = ImageLayout.TransferSrcOptimal;
        restore.NewLayout = originalLayout;
        vulkan.CmdPipelineBarrier(
            commandBuffer,
            PipelineStageFlags.TransferBit,
            attachmentStage,
            0,
            0,
            null,
            0,
            null,
            1,
            &restore);
    }

    private static uint FindHostVisibleMemoryType(
        uint typeBits,
        PhysicalDeviceMemoryProperties memoryProperties) {
        MemoryPropertyFlags requiredProperties = MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit;
        for (uint index = 0; index < memoryProperties.MemoryTypeCount; index++) {
            bool typeSupported = (typeBits & (1u << (int)index)) != 0;
            bool propertiesMatch = (memoryProperties.MemoryTypes[(int)index].PropertyFlags & requiredProperties) == requiredProperties;
            if (typeSupported && propertiesMatch) {
                return index;
            }
        }

        throw new GraphicsException(
            $"No host-visible Vulkan memory type is available for staging with type bits 0x{typeBits:X8}.",
            DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
            null);
    }

    private static void ValidatePixel(VulkanSceneTarget target, uint x, uint y) {
        if (x >= target.Extent.Width || y >= target.Extent.Height) {
            throw new ArgumentOutOfRangeException(
                nameof(x),
                $"The pixel ({x}, {y}) lies outside the {target.Extent.Width}x{target.Extent.Height} scene target.");
        }
    }

    private static void ThrowIfFailed(Result result, string operation) {
        if (result == Result.Success) {
            return;
        }

        throw new GraphicsException(
            $"The Vulkan command {operation} failed with {result}.",
            DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
            null);
    }
}
