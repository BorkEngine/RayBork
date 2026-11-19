using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// One target-sized single-sample <c>Rgba16Float</c> post target with its framebuffer, bindless
/// slot, and store-back descriptor set.
/// </summary>
/// <remarks>
/// The post chain rotates three targets per extent and frame slot, so an effect samples the previous
/// pass' output while writing a different target. The layout moves from <c>Undefined</c> to
/// <c>ColorAttachmentOptimal</c> for a write and back to <c>ShaderReadOnlyOptimal</c> so the next
/// effect or the store-back can sample it.
/// </remarks>
internal sealed unsafe class VulkanPostTarget : IDisposable
{
    private readonly Vk _vulkan;
    private readonly Device _device;
    private readonly VulkanBindlessTextureTable _bindless;

    private Image _image;
    private DeviceMemory _memory;
    private ImageView _view;
    private Framebuffer _framebuffer;
    private DescriptorPool _storeBackPool;
    private DescriptorSet _storeBackSet;
    private uint _bindlessSlot;
    private bool _disposed;

    private VulkanPostTarget(Vk vulkan, Device device, VulkanBindlessTextureTable bindless)
    {
        _vulkan = vulkan;
        _device = device;
        _bindless = bindless;
    }

    /// <summary>Gets the single-sample color image view the post pass attaches and samples.</summary>
    internal ImageView View => _view;

    /// <summary>Gets the framebuffer the post pass renders into.</summary>
    internal Framebuffer Framebuffer => _framebuffer;

    /// <summary>Gets the bindless slot that exposes the image to a custom effect shader.</summary>
    internal uint BindlessSlot => _bindlessSlot;

    /// <summary>Gets the descriptor set the store-back pass binds for a final target.</summary>
    internal DescriptorSet StoreBackSet => _storeBackSet;

    /// <summary>Gets the target extent in pixels.</summary>
    internal Extent2D Extent { get; private set; }

    /// <summary>Gets the tracked image layout.</summary>
    private ImageLayout Layout { get; set; } = ImageLayout.Undefined;

    /// <summary>Creates all resources for one post target.</summary>
    internal static VulkanPostTarget Create(
        VulkanDevice device,
        Extent2D extent,
        RenderPass postRenderPass,
        VulkanBindlessTextureTable bindless,
        VulkanPostStoreBackPipeline storeBack
    )
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(bindless);
        ArgumentNullException.ThrowIfNull(storeBack);
        if (extent.Width == 0 || extent.Height == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(extent),
                extent,
                "The post target extent must be positive."
            );
        }

        VulkanPostTarget target = new(device.Vulkan, device.Device, bindless);
        try
        {
            target.CreateResources(device, extent, postRenderPass, storeBack);
            return target;
        }
        catch
        {
            target.Dispose();
            throw;
        }
    }

    /// <summary>Transitions the image to <c>ColorAttachmentOptimal</c> for a writing pass.</summary>
    internal void TransitionToColorAttachment(Vk vulkan, CommandBuffer commandBuffer)
    {
        if (Layout == ImageLayout.ColorAttachmentOptimal)
        {
            return;
        }

        ImageLayout previous = Layout;
        ImageMemoryBarrier barrier = CreateBarrier(
            previous,
            ImageLayout.ColorAttachmentOptimal,
            previous == ImageLayout.ShaderReadOnlyOptimal
                ? AccessFlags.ShaderReadBit
                : AccessFlags.None,
            AccessFlags.ColorAttachmentWriteBit
        );
        PipelineStageFlags sourceStage =
            previous == ImageLayout.ShaderReadOnlyOptimal
                ? PipelineStageFlags.FragmentShaderBit
                : PipelineStageFlags.TopOfPipeBit;
        RecordBarrier(
            vulkan,
            commandBuffer,
            sourceStage,
            PipelineStageFlags.ColorAttachmentOutputBit,
            barrier
        );
        Layout = ImageLayout.ColorAttachmentOptimal;
    }

    /// <summary>Transitions the image to <c>ShaderReadOnlyOptimal</c> for sampling.</summary>
    internal void TransitionToShaderRead(Vk vulkan, CommandBuffer commandBuffer)
    {
        if (Layout == ImageLayout.ShaderReadOnlyOptimal)
        {
            return;
        }

        ImageMemoryBarrier barrier = CreateBarrier(
            Layout,
            ImageLayout.ShaderReadOnlyOptimal,
            AccessFlags.ColorAttachmentWriteBit,
            AccessFlags.ShaderReadBit
        );
        RecordBarrier(
            vulkan,
            commandBuffer,
            PipelineStageFlags.ColorAttachmentOutputBit,
            PipelineStageFlags.FragmentShaderBit,
            barrier
        );
        Layout = ImageLayout.ShaderReadOnlyOptimal;
    }

    /// <summary>Destroys the resources and releases the bindless slot; idempotent.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_storeBackPool.Handle != 0)
        {
            _vulkan.DestroyDescriptorPool(_device, _storeBackPool, null);
            _storeBackPool = default;
            _storeBackSet = default;
        }

        if (_framebuffer.Handle != 0)
        {
            _vulkan.DestroyFramebuffer(_device, _framebuffer, null);
            _framebuffer = default;
        }

        if (_bindlessSlot != 0)
        {
            _bindless.Release(_bindlessSlot);
            _bindlessSlot = 0;
        }

        if (_view.Handle != 0)
        {
            _vulkan.DestroyImageView(_device, _view, null);
            _view = default;
        }

        if (_image.Handle != 0)
        {
            _vulkan.DestroyImage(_device, _image, null);
            _image = default;
        }

        if (_memory.Handle != 0)
        {
            _vulkan.FreeMemory(_device, _memory, null);
            _memory = default;
        }

        Layout = ImageLayout.Undefined;
    }

    private void CreateResources(
        VulkanDevice device,
        Extent2D extent,
        RenderPass postRenderPass,
        VulkanPostStoreBackPipeline storeBack
    )
    {
        Extent = extent;
        ImageCreateInfo imageInfo = new()
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.R16G16B16A16Sfloat,
            Extent = new Extent3D
            {
                Width = extent.Width,
                Height = extent.Height,
                Depth = 1,
            },
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };
        ThrowIfFailed(
            _vulkan.CreateImage(_device, &imageInfo, null, out _image),
            "vkCreateImage for the post-process target"
        );

        MemoryRequirements requirements = _vulkan.GetImageMemoryRequirements(_device, _image);
        PhysicalDeviceMemoryProperties memoryProperties = _vulkan.GetPhysicalDeviceMemoryProperties(
            device.PhysicalDevice
        );
        MemoryAllocateInfo allocateInfo = new()
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = requirements.Size,
            MemoryTypeIndex = FindDeviceLocalMemoryType(
                requirements.MemoryTypeBits,
                memoryProperties
            ),
        };
        ThrowIfFailed(
            _vulkan.AllocateMemory(_device, &allocateInfo, null, out _memory),
            "vkAllocateMemory for the post-process target"
        );
        ThrowIfFailed(
            _vulkan.BindImageMemory(_device, _image, _memory, 0),
            "vkBindImageMemory for the post-process target"
        );

        ImageViewCreateInfo viewInfo = new()
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _image,
            ViewType = ImageViewType.Type2D,
            Format = Format.R16G16B16A16Sfloat,
            Components = new ComponentMapping
            {
                R = ComponentSwizzle.Identity,
                G = ComponentSwizzle.Identity,
                B = ComponentSwizzle.Identity,
                A = ComponentSwizzle.Identity,
            },
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1,
            },
        };
        ThrowIfFailed(
            _vulkan.CreateImageView(_device, &viewInfo, null, out _view),
            "vkCreateImageView for the post-process target"
        );

        ImageView attachmentView = _view;
        FramebufferCreateInfo framebufferInfo = new()
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = postRenderPass,
            AttachmentCount = 1,
            PAttachments = &attachmentView,
            Width = extent.Width,
            Height = extent.Height,
            Layers = 1,
        };
        ThrowIfFailed(
            _vulkan.CreateFramebuffer(_device, &framebufferInfo, null, out _framebuffer),
            "vkCreateFramebuffer for the post-process target"
        );

        if (!_bindless.TryAllocate(out _bindlessSlot))
        {
            _bindlessSlot = 0;
            throw new GraphicsException(
                $"The bindless texture table is full ({_bindless.Capacity} slots, including the reserved "
                    + "white slot), so the post-process target has no slot.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null
            );
        }

        _bindless.WriteTexture(_bindlessSlot, _view, storeBack.Sampler);
        _storeBackSet = storeBack.CreateDescriptorSet(_view, out _storeBackPool);
    }

    private ImageMemoryBarrier CreateBarrier(
        ImageLayout oldLayout,
        ImageLayout newLayout,
        AccessFlags sourceAccess,
        AccessFlags destinationAccess
    ) =>
        new()
        {
            SType = StructureType.ImageMemoryBarrier,
            SrcAccessMask = sourceAccess,
            DstAccessMask = destinationAccess,
            OldLayout = oldLayout,
            NewLayout = newLayout,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = _image,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1,
            },
        };

    private static void RecordBarrier(
        Vk vulkan,
        CommandBuffer commandBuffer,
        PipelineStageFlags sourceStage,
        PipelineStageFlags destinationStage,
        ImageMemoryBarrier barrier
    )
    {
        ImageMemoryBarrier local = barrier;
        vulkan.CmdPipelineBarrier(
            commandBuffer,
            sourceStage,
            destinationStage,
            0,
            0,
            null,
            0,
            null,
            1,
            &local
        );
    }

    private static uint FindDeviceLocalMemoryType(
        uint typeBits,
        PhysicalDeviceMemoryProperties memoryProperties
    )
    {
        const MemoryPropertyFlags required = MemoryPropertyFlags.DeviceLocalBit;
        for (uint index = 0; index < memoryProperties.MemoryTypeCount; index++)
        {
            bool typeSupported = (typeBits & (1u << (int)index)) != 0;
            bool propertiesMatch =
                (memoryProperties.MemoryTypes[(int)index].PropertyFlags & required) == required;
            if (typeSupported && propertiesMatch)
            {
                return index;
            }
        }

        throw new GraphicsException(
            $"No device-local Vulkan memory type is available for the post-process target "
                + $"(type bits 0x{typeBits:X8}).",
            DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
            null
        );
    }

    private static void ThrowIfFailed(Result result, string operation)
    {
        if (result == Result.Success)
        {
            return;
        }

        throw new GraphicsException(
            $"The Vulkan command {operation} failed with {result}.",
            DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
            null
        );
    }
}
