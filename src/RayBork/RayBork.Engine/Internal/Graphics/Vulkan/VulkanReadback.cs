using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace RayBork.Internal;

/// <summary>
/// Single-pixel readback probes for the scene target and the presented swapchain image, used by GPU
/// tests to assert clear and resolve values numerically.
/// </summary>
/// <remarks>
/// <para>
/// These probes are diagnostics, not a renderer path: each call creates its own command pool, staging
/// buffer, and fence, submits one copy, and waits for the GPU. The call stalls the render thread and
/// must never be used per frame. Every temporary object is destroyed in a <c>finally</c> block so a
/// failed probe cannot leak.
/// </para>
/// <para>
/// The caller passes a device and scene target or swapchain from a single backend and runs on that
/// backend's render thread. Nothing here is thread-safe.
/// </para>
/// </remarks>
internal static unsafe class VulkanReadback {
    private const ulong WAIT_TIMEOUT = ulong.MaxValue;
    private const uint COLOR_PIXEL_BYTES = 8;
    private const uint DEPTH_PIXEL_BYTES = 4;
    private const uint PRESENTED_FLOAT_PIXEL_BYTES = 8;
    private const uint PRESENTED_BYTE_PIXEL_BYTES = 4;
    private const float SRGB_ENCODED_CUTOFF = 0.04045f;

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

    /// <summary>
    /// Reads one pixel from the swapchain image a submitted frame rendered, decoded to display-linear
    /// values.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The probe waits for the device to idle, transitions the acquired image from
    /// <c>PRESENT_SRC_KHR</c> to <c>TRANSFER_SRC_OPTIMAL</c>, copies one texel, transitions it back,
    /// and leaves the image in the presentation layout. The all-commands stage masks make the
    /// transition valid after the frame's render pass, whose final layout is <c>PRESENT_SRC_KHR</c>.
    /// </para>
    /// <para>
    /// The caller must call the probe after <see cref="Vulkan12Backend.Submit"/> and before
    /// <see cref="Vulkan12Backend.Present"/>, passing
    /// <see cref="Vulkan12Backend.PendingPresentImageIndex"/>. Once <c>vkQueuePresentKHR</c> runs, the
    /// presentation engine owns the image and a layout transition is no longer valid.
    /// </para>
    /// <para>
    /// A half-float scRGB image returns scRGB-linear values without a transfer function. An
    /// <c>_SRGB</c> image returns the hardware-decoded linear values. A <c>_UNORM</c> image returns
    /// the manual sRGB encode the present shader wrote, decoded to linear, so both 8-bit paths
    /// return the same display-linear value.
    /// </para>
    /// <para>
    /// This is a diagnostic probe for GPU tests: it stalls the device and is never part of the frame
    /// path.
    /// </para>
    /// </remarks>
    /// <param name="device">The device that owns the swapchain.</param>
    /// <param name="swapchain">The swapchain the backend renders to.</param>
    /// <param name="imageIndex">The submitted frame's swapchain image index.</param>
    /// <param name="x">The pixel column, in pixels from the left edge.</param>
    /// <param name="y">The pixel row, in pixels from the top edge.</param>
    /// <param name="r">The display-linear red component.</param>
    /// <param name="g">The display-linear green component.</param>
    /// <param name="b">The display-linear blue component.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The image index or the pixel lies outside the swapchain.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// The surface did not advertise transfer-source usage, so the swapchain images cannot be copied.
    /// </exception>
    /// <exception cref="GraphicsException">
    /// The swapchain format is not a supported presented-color format, or a Vulkan object could not
    /// be created or the copy failed. The diagnostic code is <c>RB3001</c>.
    /// </exception>
    internal static void ReadPresentedColorPixel(
        VulkanDevice device,
        VulkanSwapchain swapchain,
        uint imageIndex,
        uint x,
        uint y,
        out float r,
        out float g,
        out float b) {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(swapchain);
        if (!swapchain.SupportsPresentReadback) {
            throw new NotSupportedException(
                "The swapchain images were not created with VK_IMAGE_USAGE_TRANSFER_SRC_BIT, so the " +
                "presented pixel cannot be read back on this surface.");
        }

        if (imageIndex >= (uint)swapchain.ImageCount) {
            throw new ArgumentOutOfRangeException(
                nameof(imageIndex),
                imageIndex,
                $"The swapchain has {swapchain.ImageCount} image(s), so image index {imageIndex} is out of range.");
        }

        Extent2D extent = swapchain.Extent;
        if (x >= extent.Width || y >= extent.Height) {
            throw new ArgumentOutOfRangeException(
                nameof(x),
                $"The pixel ({x}, {y}) lies outside the {extent.Width}x{extent.Height} swapchain image.");
        }

        Format format = swapchain.Format;
        uint pixelBytes = PresentedColorPixelBytes(format, out bool halfFloat);

        // The submitted frame must have completed before the probe can transition the image out of
        // the render pass' PRESENT_SRC_KHR final layout.
        ThrowIfFailed(device.Vulkan.DeviceWaitIdle(device.Device), "vkDeviceWaitIdle");

        byte[] pixel = ReadPixel(
            device,
            swapchain.Images[(int)imageIndex],
            new PixelCopyBarrier(
                ImageAspectFlags.ColorBit,
                ImageLayout.PresentSrcKhr,
                PipelineStageFlags.AllCommandsBit,
                AccessFlags.None),
            pixelBytes,
            x,
            y);
        DecodePresentedColor(format, halfFloat, pixel, out r, out g, out b);
    }

    private static byte[] ReadPixel(
        VulkanDevice device,
        Image image,
        ImageAspectFlags aspect,
        ImageLayout originalLayout,
        uint byteCount,
        uint x,
        uint y) => ReadPixel(
            device,
            image,
            CreateAttachmentBarrier(aspect, originalLayout),
            byteCount,
            x,
            y);

    private static byte[] ReadPixel(
        VulkanDevice device,
        Image image,
        PixelCopyBarrier barrierInfo,
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
            RecordCopy(vulkan, commandBuffer, image, barrierInfo, stagingBuffer, x, y);
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
        PixelCopyBarrier barrierInfo,
        Buffer stagingBuffer,
        uint x,
        uint y) {
        ImageMemoryBarrier barrier = new() {
            SType = StructureType.ImageMemoryBarrier,
            SrcAccessMask = barrierInfo.Access,
            DstAccessMask = AccessFlags.TransferReadBit,
            OldLayout = barrierInfo.OriginalLayout,
            NewLayout = ImageLayout.TransferSrcOptimal,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = new ImageSubresourceRange {
                AspectMask = barrierInfo.Aspect,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1,
            },
        };
        vulkan.CmdPipelineBarrier(
            commandBuffer,
            barrierInfo.Stage,
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
                AspectMask = barrierInfo.Aspect,
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
        restore.DstAccessMask = barrierInfo.Access;
        restore.OldLayout = ImageLayout.TransferSrcOptimal;
        restore.NewLayout = barrierInfo.OriginalLayout;
        vulkan.CmdPipelineBarrier(
            commandBuffer,
            PipelineStageFlags.TransferBit,
            barrierInfo.Stage,
            0,
            0,
            null,
            0,
            null,
            1,
            &restore);
    }

    private static PixelCopyBarrier CreateAttachmentBarrier(
        ImageAspectFlags aspect,
        ImageLayout originalLayout) {
        bool depth = (aspect & ImageAspectFlags.DepthBit) != 0;
        PipelineStageFlags stage = depth
            ? PipelineStageFlags.LateFragmentTestsBit
            : PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.FragmentShaderBit;
        AccessFlags access = depth
            ? AccessFlags.DepthStencilAttachmentWriteBit
            : AccessFlags.ColorAttachmentWriteBit | AccessFlags.ShaderReadBit;
        return new PixelCopyBarrier(aspect, originalLayout, stage, access);
    }

    private static uint PresentedColorPixelBytes(Format format, out bool halfFloat) {
        switch (format) {
            case Format.R16G16B16A16Sfloat:
                halfFloat = true;
                return PRESENTED_FLOAT_PIXEL_BYTES;
            case Format.B8G8R8A8Srgb:
            case Format.B8G8R8A8Unorm:
            case Format.R8G8B8A8Srgb:
            case Format.R8G8B8A8Unorm:
                halfFloat = false;
                return PRESENTED_BYTE_PIXEL_BYTES;
            default:
                throw new GraphicsException(
                    $"The swapchain format {VulkanFormatInfo.Describe(format)} is not a supported presented-color probe format. " +
                    "The probe supports R16G16B16A16_SFLOAT and the 8-bit B8G8R8A8/R8G8B8A8 SRGB and UNORM formats.",
                    DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                    null);
        }
    }

    private static void DecodePresentedColor(
        Format format,
        bool halfFloat,
        byte[] pixel,
        out float r,
        out float g,
        out float b) {
        if (halfFloat) {
            r = (float)BitConverter.ToHalf(pixel, 0);
            g = (float)BitConverter.ToHalf(pixel, 2);
            b = (float)BitConverter.ToHalf(pixel, 4);
            return;
        }

        bool bgra = format is Format.B8G8R8A8Srgb or Format.B8G8R8A8Unorm;
        int redIndex = bgra ? 2 : 0;
        int greenIndex = 1;
        int blueIndex = bgra ? 0 : 2;
        r = SrgbDecode(pixel[redIndex]);
        g = SrgbDecode(pixel[greenIndex]);
        b = SrgbDecode(pixel[blueIndex]);
    }

    private static float SrgbDecode(byte encoded) {
        float channel = encoded / 255f;
        return channel <= SRGB_ENCODED_CUTOFF
            ? channel / 12.92f
            : MathF.Pow((channel + 0.055f) / 1.055f, 2.4f);
    }

    private readonly record struct PixelCopyBarrier(
        ImageAspectFlags Aspect,
        ImageLayout OriginalLayout,
        PipelineStageFlags Stage,
        AccessFlags Access);

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
