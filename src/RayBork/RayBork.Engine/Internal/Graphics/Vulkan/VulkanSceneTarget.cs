using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// The linear HDR scene target: the multisampled color and depth images, the resolved color image the
/// present pass samples, and the render pass and framebuffer every camera pass renders into.
/// </summary>
/// <remarks>
/// <para>
/// The color images use <c>R16G16B16A16_SFLOAT</c> and the depth image uses <c>D32_SFLOAT</c>. Every
/// camera pass is its own render pass instance with a viewport-sized render area. The color and depth
/// attachments load the previous pass' stored content and store their own, so a later camera with
/// <see cref="ClearMode.None"/> sees the earlier pass' pixels and depth, and a camera that clears only
/// touches its clamped viewport with <c>vkCmdClearAttachments</c>. The backend transitions the
/// attachments to undefined content at the start of the frame, so a first camera that does not clear
/// sees undefined content. The resolved color attachment is a resolve target with a
/// <c>DontCare</c> load and a <c>Store</c> store, so each pass resolves only its render area. The
/// single-sample resolved color image is readable and copyable so a GPU test can probe it; the
/// single-sample depth image is also a transfer source. Multisampled depth is not resolved in M8
/// because nothing samples depth.
/// </para>
/// <para>
/// Every member is render-thread only. The caller recreates the target after a device wait-idle and
/// disposes it before the device. Dispose is idempotent.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanSceneTarget : IDisposable {
    private const uint SUBPASS_EXTERNAL = uint.MaxValue;

    private readonly Vk _vulkan;
    private readonly Device _device;
    private readonly PhysicalDevice _physicalDevice;
    private readonly int _sampleCount;
    private readonly List<ImageResources> _imageResources = [];

    private RenderPass _renderPass;
    private Framebuffer _framebuffer;
    private Image _multisampledColorImage;
    private Image _resolvedColorImage;
    private Image _depthImage;
    private ImageView _resolvedColorView;
    private ImageView _depthView;
    private Extent2D _extent;
    private bool _disposed;

    private VulkanSceneTarget(VulkanDevice device, Extent2D extent, int sampleCount) {
        _vulkan = device.Vulkan;
        _device = device.Device;
        _physicalDevice = device.PhysicalDevice;
        _extent = extent;
        _sampleCount = sampleCount;
    }

    /// <summary>
    /// Gets the current target extent.
    /// </summary>
    internal Extent2D Extent => _extent;

    /// <summary>
    /// Gets the effective sample count of the color and depth attachments.
    /// </summary>
    internal int SampleCount => _sampleCount;

    /// <summary>
    /// Gets the render pass every camera pass records into. Each pass begins its own instance with a
    /// viewport-sized render area; the color and depth attachments load the previous pass' stored
    /// content and store their own.
    /// </summary>
    internal RenderPass RenderPass => _renderPass;

    /// <summary>
    /// Gets the framebuffer that binds the target attachments for the render pass.
    /// </summary>
    internal Framebuffer Framebuffer => _framebuffer;

    /// <summary>
    /// Gets the multisampled color image, or the default handle when the target is single-sample.
    /// </summary>
    /// <remarks>
    /// The backend transitions this image to <c>ColorAttachmentOptimal</c> at the start of a frame and
    /// synchronizes it between camera render passes. A single-sample target renders and resolves into
    /// <see cref="ResolvedColorImage"/> instead.
    /// </remarks>
    internal Image MultisampledColorImage => _multisampledColorImage;

    /// <summary>
    /// Gets the single-sample color image the present pass samples.
    /// </summary>
    internal Image ResolvedColorImage => _resolvedColorImage;

    /// <summary>
    /// Gets the single-sample color image view the present pass samples.
    /// </summary>
    internal ImageView ResolvedColorView => _resolvedColorView;

    /// <summary>
    /// Gets the single-sample depth image.
    /// </summary>
    /// <remarks>
    /// Multisampled targets have no single-sample depth image; <see cref="DepthImage"/> is the default
    /// handle and <see cref="DepthView"/> is the default view in that case.
    /// </remarks>
    internal Image DepthImage => _depthImage;

    /// <summary>
    /// Gets the single-sample depth view.
    /// </summary>
    /// <remarks>
    /// The handle is the default <see cref="ImageView"/> for a multisampled target. Use
    /// <see cref="SampleCount"/> to decide whether the view is usable.
    /// </remarks>
    internal ImageView DepthView => _depthView;

    /// <summary>
    /// Creates the scene target images, render pass, and framebuffer.
    /// </summary>
    /// <param name="device">The device that owns the target.</param>
    /// <param name="extent">The positive target extent.</param>
    /// <param name="sampleCount">The effective MSAA sample count.</param>
    /// <returns>The created scene target.</returns>
    /// <exception cref="GraphicsException">
    /// An image, image view, memory allocation, render pass, or framebuffer could not be created. The
    /// diagnostic code is <c>RB3001</c>.
    /// </exception>
    internal static VulkanSceneTarget Create(
        VulkanDevice device,
        Extent2D extent,
        int sampleCount) {
        ArgumentNullException.ThrowIfNull(device);
        if (sampleCount <= 0) {
            throw new ArgumentOutOfRangeException(nameof(sampleCount), sampleCount, "The sample count must be positive.");
        }

        if (extent.Width == 0 || extent.Height == 0) {
            throw new ArgumentOutOfRangeException(nameof(extent), extent, "The scene target extent must be positive.");
        }

        VulkanSceneTarget target = new(device, extent, sampleCount);
        try {
            target.CreateResources();
            return target;
        } catch {
            target.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Destroys the framebuffer, render pass, image views, images, and memory. The call is idempotent.
    /// </summary>
    public void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;
        if (_framebuffer.Handle != 0) {
            _vulkan.DestroyFramebuffer(_device, _framebuffer, null);
            _framebuffer = default;
        }

        if (_renderPass.Handle != 0) {
            _vulkan.DestroyRenderPass(_device, _renderPass, null);
            _renderPass = default;
        }

        foreach (ImageResources resources in _imageResources) {
            if (resources.View.Handle != 0) {
                _vulkan.DestroyImageView(_device, resources.View, null);
            }

            if (resources.Image.Handle != 0) {
                _vulkan.DestroyImage(_device, resources.Image, null);
            }

            if (resources.Memory.Handle != 0) {
                _vulkan.FreeMemory(_device, resources.Memory, null);
            }
        }

        _imageResources.Clear();
        _multisampledColorImage = default;
        _resolvedColorImage = default;
        _depthImage = default;
        _resolvedColorView = default;
        _depthView = default;
    }

    private void CreateResources() {
        PhysicalDeviceMemoryProperties memoryProperties = _vulkan.GetPhysicalDeviceMemoryProperties(_physicalDevice);
        bool multisampled = _sampleCount > 1;
        SampleCountFlags samples = ToSampleCountFlags(_sampleCount);

        ImageResources? multisampledColor = multisampled
            ? CreateAttachment(
                Format.R16G16B16A16Sfloat,
                samples,
                ImageUsageFlags.ColorAttachmentBit,
                ImageAspectFlags.ColorBit,
                memoryProperties)
            : null;
        ImageResources resolvedColor = CreateAttachment(
            Format.R16G16B16A16Sfloat,
            SampleCountFlags.Count1Bit,
            ImageUsageFlags.SampledBit | ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit,
            ImageAspectFlags.ColorBit,
            memoryProperties);
        ImageUsageFlags depthUsage = multisampled
            ? ImageUsageFlags.DepthStencilAttachmentBit
            : ImageUsageFlags.DepthStencilAttachmentBit | ImageUsageFlags.TransferSrcBit;
        ImageResources depth = CreateAttachment(
            Format.D32Sfloat,
            samples,
            depthUsage,
            ImageAspectFlags.DepthBit,
            memoryProperties);

        _renderPass = CreateRenderPass(multisampled, samples);
        ImageView[] attachments = multisampled
            ? [multisampledColor!.Value.View, resolvedColor.View, depth.View]
            : [resolvedColor.View, depth.View];
        _framebuffer = CreateFramebuffer(attachments);
        _multisampledColorImage = multisampled ? multisampledColor!.Value.Image : default;
        _resolvedColorImage = resolvedColor.Image;
        _resolvedColorView = resolvedColor.View;
        _depthImage = depth.Image;
        _depthView = multisampled ? default : depth.View;
    }

    private ImageResources CreateAttachment(
        Format format,
        SampleCountFlags samples,
        ImageUsageFlags usage,
        ImageAspectFlags aspect,
        PhysicalDeviceMemoryProperties memoryProperties) {
        Image image = default;
        DeviceMemory memory = default;
        ImageView view = default;
        try {
            image = CreateImage(format, samples, usage);
            memory = AllocateMemory(image, memoryProperties);
            ThrowIfFailed(_vulkan.BindImageMemory(_device, image, memory, 0), "vkBindImageMemory");
            view = CreateImageView(image, format, aspect);
            ImageResources resources = new(image, view, memory);
            _imageResources.Add(resources);
            return resources;
        } catch {
            if (view.Handle != 0) {
                _vulkan.DestroyImageView(_device, view, null);
            }

            if (image.Handle != 0) {
                _vulkan.DestroyImage(_device, image, null);
            }

            if (memory.Handle != 0) {
                _vulkan.FreeMemory(_device, memory, null);
            }

            throw;
        }
    }

    private Image CreateImage(Format format, SampleCountFlags samples, ImageUsageFlags usage) {
        ImageCreateInfo createInfo = new() {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = format,
            Extent = new Extent3D { Width = _extent.Width, Height = _extent.Height, Depth = 1 },
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = samples,
            Tiling = ImageTiling.Optimal,
            Usage = usage,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };
        Result result = _vulkan.CreateImage(_device, &createInfo, null, out Image image);
        ThrowIfFailed(result, "vkCreateImage");
        return image;
    }

    private DeviceMemory AllocateMemory(Image image, PhysicalDeviceMemoryProperties memoryProperties) {
        MemoryRequirements requirements = _vulkan.GetImageMemoryRequirements(_device, image);
        uint memoryTypeIndex = FindMemoryType(
            requirements.MemoryTypeBits,
            MemoryPropertyFlags.DeviceLocalBit,
            memoryProperties);
        MemoryAllocateInfo allocateInfo = new() {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = requirements.Size,
            MemoryTypeIndex = memoryTypeIndex,
        };
        Result result = _vulkan.AllocateMemory(_device, &allocateInfo, null, out DeviceMemory memory);
        ThrowIfFailed(result, "vkAllocateMemory");
        return memory;
    }

    private ImageView CreateImageView(Image image, Format format, ImageAspectFlags aspect) {
        ImageViewCreateInfo createInfo = new() {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = ImageViewType.Type2D,
            Format = format,
            Components = new ComponentMapping {
                R = ComponentSwizzle.Identity,
                G = ComponentSwizzle.Identity,
                B = ComponentSwizzle.Identity,
                A = ComponentSwizzle.Identity,
            },
            SubresourceRange = new ImageSubresourceRange {
                AspectMask = aspect,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1,
            },
        };
        Result result = _vulkan.CreateImageView(_device, &createInfo, null, out ImageView view);
        ThrowIfFailed(result, "vkCreateImageView");
        return view;
    }

    private RenderPass CreateRenderPass(bool multisampled, SampleCountFlags samples) {
        AttachmentDescription[] attachments = new AttachmentDescription[multisampled ? 3 : 2];
        if (multisampled) {
            attachments[0] = CreateAttachmentDescription(
                Format.R16G16B16A16Sfloat,
                samples,
                AttachmentLoadOp.Load,
                AttachmentStoreOp.Store,
                ImageLayout.ColorAttachmentOptimal);
            attachments[1] = CreateAttachmentDescription(
                Format.R16G16B16A16Sfloat,
                SampleCountFlags.Count1Bit,
                AttachmentLoadOp.DontCare,
                AttachmentStoreOp.Store,
                ImageLayout.ColorAttachmentOptimal);
            attachments[2] = CreateAttachmentDescription(
                Format.D32Sfloat,
                samples,
                AttachmentLoadOp.Load,
                AttachmentStoreOp.Store,
                ImageLayout.DepthStencilAttachmentOptimal);
        } else {
            attachments[0] = CreateAttachmentDescription(
                Format.R16G16B16A16Sfloat,
                SampleCountFlags.Count1Bit,
                AttachmentLoadOp.Load,
                AttachmentStoreOp.Store,
                ImageLayout.ColorAttachmentOptimal);
            attachments[1] = CreateAttachmentDescription(
                Format.D32Sfloat,
                SampleCountFlags.Count1Bit,
                AttachmentLoadOp.Load,
                AttachmentStoreOp.Store,
                ImageLayout.DepthStencilAttachmentOptimal);
        }

        AttachmentReference colorReference = new() { Attachment = 0, Layout = ImageLayout.ColorAttachmentOptimal };
        AttachmentReference resolveReference = new() { Attachment = 1, Layout = ImageLayout.ColorAttachmentOptimal };
        AttachmentReference depthReference = new() {
            Attachment = multisampled ? 2u : 1u,
            Layout = ImageLayout.DepthStencilAttachmentOptimal,
        };
        SubpassDescription subpass = new() {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorReference,
            PResolveAttachments = multisampled ? &resolveReference : null,
            PDepthStencilAttachment = &depthReference,
        };

        // The backend transitions the attachments to their attachment layouts before the first pass
        // and records explicit barriers between passes, so the render pass needs only the external
        // dependency that orders each load after the preceding scene work. There is no final
        // dependency: the backend transitions the resolved color to ShaderReadOnlyOptimal after the
        // last pass instead.
        SubpassDependency dependency = new() {
            SrcSubpass = SUBPASS_EXTERNAL,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.LateFragmentTestsBit,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit,
            DependencyFlags = 0,
        };
        fixed (AttachmentDescription* attachmentsPointer = attachments) {
            RenderPassCreateInfo createInfo = new() {
                SType = StructureType.RenderPassCreateInfo,
                AttachmentCount = (uint)attachments.Length,
                PAttachments = attachmentsPointer,
                SubpassCount = 1,
                PSubpasses = &subpass,
                DependencyCount = 1,
                PDependencies = &dependency,
            };
            Result result = _vulkan.CreateRenderPass(_device, &createInfo, null, out RenderPass renderPass);
            ThrowIfFailed(result, "vkCreateRenderPass");
            return renderPass;
        }
    }

    private Framebuffer CreateFramebuffer(ImageView[] attachments) {
        fixed (ImageView* attachmentsPointer = attachments) {
            FramebufferCreateInfo createInfo = new() {
                SType = StructureType.FramebufferCreateInfo,
                RenderPass = _renderPass,
                AttachmentCount = (uint)attachments.Length,
                PAttachments = attachmentsPointer,
                Width = _extent.Width,
                Height = _extent.Height,
                Layers = 1,
            };
            Result result = _vulkan.CreateFramebuffer(_device, &createInfo, null, out Framebuffer framebuffer);
            ThrowIfFailed(result, "vkCreateFramebuffer");
            return framebuffer;
        }
    }

    private static AttachmentDescription CreateAttachmentDescription(
        Format format,
        SampleCountFlags samples,
        AttachmentLoadOp loadOp,
        AttachmentStoreOp storeOp,
        ImageLayout layout) => new() {
            Format = format,
            Samples = samples,
            LoadOp = loadOp,
            StoreOp = storeOp,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            // The backend keeps each attachment in its attachment layout for the whole frame, so the
            // render pass never transitions it and loadOp always sees the previous pass' stored
            // content (or undefined content after the frame-start transition).
            InitialLayout = layout,
            FinalLayout = layout,
        };

    private static uint FindMemoryType(
        uint typeBits,
        MemoryPropertyFlags requiredProperties,
        PhysicalDeviceMemoryProperties memoryProperties) {
        for (uint index = 0; index < memoryProperties.MemoryTypeCount; index++) {
            bool typeSupported = (typeBits & (1u << (int)index)) != 0;
            bool propertiesMatch = (memoryProperties.MemoryTypes[(int)index].PropertyFlags & requiredProperties) == requiredProperties;
            if (typeSupported && propertiesMatch) {
                return index;
            }
        }

        throw new GraphicsException(
            $"No Vulkan memory type satisfies {requiredProperties} for type bits 0x{typeBits:X8}.",
            DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
            null);
    }

    private static SampleCountFlags ToSampleCountFlags(int sampleCount) => sampleCount switch {
        1 => SampleCountFlags.Count1Bit,
        2 => SampleCountFlags.Count2Bit,
        4 => SampleCountFlags.Count4Bit,
        8 => SampleCountFlags.Count8Bit,
        16 => SampleCountFlags.Count16Bit,
        32 => SampleCountFlags.Count32Bit,
        64 => SampleCountFlags.Count64Bit,
        _ => throw new ArgumentOutOfRangeException(
            nameof(sampleCount),
            sampleCount,
            "The sample count is not a supported Vulkan sample count."),
    };

    private static void ThrowIfFailed(Result result, string operation) {
        if (result == Result.Success) {
            return;
        }

        throw new GraphicsException(
            $"The Vulkan command {operation} failed with {result}.",
            DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
            null);
    }

    private readonly record struct ImageResources(Image Image, ImageView View, DeviceMemory Memory);
}
