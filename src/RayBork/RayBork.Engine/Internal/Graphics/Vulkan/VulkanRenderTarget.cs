using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// One render target: the color attachment (multisampled or single-sample), the single-sample resolved
/// color image a shader samples, the optional depth attachment with its resolved depth image, and the
/// render pass and framebuffer every camera pass renders into.
/// </summary>
/// <remarks>
/// <para>
/// The app scene target and a public <see cref="RenderTexture"/> are the same kind of resource. Every
/// camera pass is its own render pass instance with a viewport-sized render area. The color and depth
/// attachments load the previous pass' stored content and store their own, so a later camera with
/// <see cref="ClearMode.None"/> sees the earlier pass' pixels and depth, and a camera that clears only
/// touches its clamped viewport with <c>vkCmdClearAttachments</c>. A multisampled target resolves color
/// and depth into their single-sample images for its render area only. Vulkan 1.2 performs the depth
/// resolve through <c>VkSubpassDescriptionDepthStencilResolve</c> in a <c>vkCreateRenderPass2</c> render
/// pass; a single-sample target has no resolve attachment because its depth image is the resolved depth.
/// </para>
/// <para>
/// The scene target discards its content at the start of every frame, so a first camera that does not
/// clear sees undefined content. A runtime render target keeps its content across frames: the target
/// records the layout of its attachments, transitions the color and depth images to their attachment
/// layouts before a writing pass, and transitions the single-sample resolved images to
/// <c>ShaderReadOnlyOptimal</c> after the pass so a later camera can sample them. A later pass that
/// writes the same target transitions the resolved images back. The caller waits for the previous frame
/// that used a runtime target before it records a new use, so a frame never overwrites content another
/// in-flight frame still reads.
/// </para>
/// <para>
/// Every member is render-thread only. The caller recreates a target only after a device wait-idle and
/// disposes it before the device. Dispose is idempotent.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanRenderTarget : IDisposable {
    private const uint SUBPASS_EXTERNAL = uint.MaxValue;
    private const AccessFlags COLOR_ATTACHMENT_ACCESS =
        AccessFlags.ColorAttachmentReadBit | AccessFlags.ColorAttachmentWriteBit;
    private const AccessFlags DEPTH_ATTACHMENT_ACCESS =
        AccessFlags.DepthStencilAttachmentReadBit | AccessFlags.DepthStencilAttachmentWriteBit;

    private readonly Vk _vulkan;
    private readonly Device _device;
    private readonly PhysicalDevice _physicalDevice;
    private readonly bool _hasDepth;
    private readonly ResolveModeFlags _depthResolveMode;
    private readonly List<ImageResources> _imageResources = [];

    private RenderPass _renderPass;
    private Framebuffer _framebuffer;
    private Image _multisampledColorImage;
    private Image _multisampledDepthImage;
    private Image _resolvedColorImage;
    private Image _resolvedDepthImage;
    private ImageView _resolvedColorView;
    private ImageView _resolvedDepthView;
    private Extent2D _extent;
    private ImageLayout _multisampledColorLayout = ImageLayout.Undefined;
    private ImageLayout _resolvedColorLayout = ImageLayout.Undefined;
    private ImageLayout _multisampledDepthLayout = ImageLayout.Undefined;
    private ImageLayout _resolvedDepthLayout = ImageLayout.Undefined;
    private bool _disposed;

    private VulkanRenderTarget(
        VulkanDevice device,
        Extent2D extent,
        int sampleCount,
        Format colorFormat,
        bool hasDepth) {
        _vulkan = device.Vulkan;
        _device = device.Device;
        _physicalDevice = device.PhysicalDevice;
        _extent = extent;
        SampleCount = sampleCount;
        ColorFormat = colorFormat;
        _hasDepth = hasDepth;
        _depthResolveMode = device.Capabilities.DepthResolveMode;
    }

    /// <summary>Gets the current target extent.</summary>
    internal Extent2D Extent => _extent;

    /// <summary>Gets the effective sample count of the color and optional depth attachments.</summary>
    internal int SampleCount { get; }

    /// <summary>Gets the color format of the color attachment and the resolved color image.</summary>
    internal Format ColorFormat { get; }

    /// <summary>Gets whether the target owns a depth attachment.</summary>
    internal bool HasDepth => _hasDepth;

    /// <summary>Gets whether the color and depth attachments are multisampled.</summary>
    internal bool IsMultisampled => SampleCount > 1;

    /// <summary>Gets the render pass every camera pass records into.</summary>
    internal RenderPass RenderPass => _renderPass;

    /// <summary>Gets the framebuffer that binds the target attachments for the render pass.</summary>
    internal Framebuffer Framebuffer => _framebuffer;

    /// <summary>Gets the single-sample color image a shader samples.</summary>
    internal Image ResolvedColorImage => _resolvedColorImage;

    /// <summary>Gets the single-sample color image view a shader samples.</summary>
    internal ImageView ResolvedColorView => _resolvedColorView;

    /// <summary>
    /// Gets the single-sample depth image a shader samples. It is the depth attachment itself for a
    /// single-sample target and the resolved depth image for a multisampled target.
    /// </summary>
    internal Image ResolvedDepthImage => _resolvedDepthImage;

    /// <summary>Gets the single-sample depth image view a shader samples.</summary>
    internal ImageView ResolvedDepthView => _resolvedDepthView;

    /// <summary>Gets the current layout of the single-sample resolved color image.</summary>
    internal ImageLayout ResolvedColorLayout => _resolvedColorLayout;

    /// <summary>Gets the current layout of the single-sample resolved depth image.</summary>
    internal ImageLayout ResolvedDepthLayout => _resolvedDepthLayout;

    /// <summary>
    /// Creates the target images, render pass, and framebuffer.
    /// </summary>
    /// <param name="device">The device that owns the target.</param>
    /// <param name="extent">The positive target extent.</param>
    /// <param name="sampleCount">The effective MSAA sample count.</param>
    /// <param name="colorFormat">The color attachment and resolved color format.</param>
    /// <param name="depth">Whether the target owns a depth attachment.</param>
    /// <returns>The created target.</returns>
    /// <exception cref="GraphicsException">
    /// The device does not support the color format, or an image, image view, memory allocation, render
    /// pass, or framebuffer could not be created. The diagnostic code is <c>RB3001</c>.
    /// </exception>
    internal static VulkanRenderTarget Create(
        VulkanDevice device,
        Extent2D extent,
        int sampleCount,
        Format colorFormat,
        bool depth) {
        ArgumentNullException.ThrowIfNull(device);
        if (sampleCount <= 0) {
            throw new ArgumentOutOfRangeException(nameof(sampleCount), sampleCount, "The sample count must be positive.");
        }

        if (extent.Width == 0 || extent.Height == 0) {
            throw new ArgumentOutOfRangeException(nameof(extent), extent, "The render target extent must be positive.");
        }

        ValidateColorFormat(device, colorFormat);
        VulkanRenderTarget target = new(device, extent, sampleCount, colorFormat, depth);
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
        _multisampledDepthImage = default;
        _resolvedColorImage = default;
        _resolvedDepthImage = default;
        _resolvedColorView = default;
        _resolvedDepthView = default;
        _multisampledColorLayout = ImageLayout.Undefined;
        _resolvedColorLayout = ImageLayout.Undefined;
        _multisampledDepthLayout = ImageLayout.Undefined;
        _resolvedDepthLayout = ImageLayout.Undefined;
    }

    /// <summary>
    /// Transitions every attachment to its attachment layout from <c>Undefined</c>, discarding the
    /// previous content. The scene target calls it once at the start of a frame.
    /// </summary>
    /// <param name="vulkan">The Vulkan functions.</param>
    /// <param name="commandBuffer">The command buffer recording the frame.</param>
    internal void DiscardContentAndTransitionToAttachments(Vk vulkan, CommandBuffer commandBuffer) {
        bool multisampled = IsMultisampled;
        int capacity = (multisampled ? 2 : 1) + (_hasDepth ? (multisampled ? 2 : 1) : 0);
        Span<ImageMemoryBarrier> barriers = stackalloc ImageMemoryBarrier[capacity];
        int count = 0;
        if (multisampled) {
            barriers[count++] = CreateImageBarrier(
                _multisampledColorImage,
                ImageAspectFlags.ColorBit,
                ImageLayout.Undefined,
                ImageLayout.ColorAttachmentOptimal,
                AccessFlags.None,
                COLOR_ATTACHMENT_ACCESS);
        }

        // Every render pass loads its attachments, so the destination scope includes the attachment
        // read access as well as the write even though Undefined discards the old content.
        barriers[count++] = CreateImageBarrier(
            _resolvedColorImage,
            ImageAspectFlags.ColorBit,
            ImageLayout.Undefined,
            ImageLayout.ColorAttachmentOptimal,
            AccessFlags.None,
            COLOR_ATTACHMENT_ACCESS);
        if (_hasDepth) {
            if (multisampled) {
                barriers[count++] = CreateImageBarrier(
                    _multisampledDepthImage,
                    ImageAspectFlags.DepthBit,
                    ImageLayout.Undefined,
                    ImageLayout.DepthStencilAttachmentOptimal,
                    AccessFlags.None,
                    DEPTH_ATTACHMENT_ACCESS);
            }

            barriers[count++] = CreateImageBarrier(
                _resolvedDepthImage,
                ImageAspectFlags.DepthBit,
                ImageLayout.Undefined,
                ImageLayout.DepthStencilAttachmentOptimal,
                AccessFlags.None,
                ResolvedDepthWriteAccess());
        }

        _multisampledColorLayout = multisampled ? ImageLayout.ColorAttachmentOptimal : ImageLayout.Undefined;
        _resolvedColorLayout = ImageLayout.ColorAttachmentOptimal;
        _multisampledDepthLayout = _hasDepth && multisampled
            ? ImageLayout.DepthStencilAttachmentOptimal
            : ImageLayout.Undefined;
        _resolvedDepthLayout = _hasDepth ? ImageLayout.DepthStencilAttachmentOptimal : ImageLayout.Undefined;
        RecordImageBarriers(
            vulkan,
            commandBuffer,
            PipelineStageFlags.TopOfPipeBit,
            PipelineStageFlags.ColorAttachmentOutputBit
                | PipelineStageFlags.EarlyFragmentTestsBit
                | PipelineStageFlags.LateFragmentTestsBit,
            barriers[..count]);
    }

    /// <summary>
    /// Transitions the color and depth images to their attachment layouts for a writing pass and
    /// synchronizes them with the previous pass that wrote or sampled the target.
    /// </summary>
    /// <remarks>
    /// The method is also the between-pass barrier for two consecutive writing passes. It reads the
    /// tracked layouts, so it covers the previous frame's write, a read in an earlier pass of the same
    /// frame, and a resolve a previous pass wrote. A multisampled target transitions both the
    /// multisampled depth attachment and the resolved depth image; the depth attachment is
    /// synchronized only when the target has one.
    /// </remarks>
    /// <param name="vulkan">The Vulkan functions.</param>
    /// <param name="commandBuffer">The command buffer recording the frame.</param>
    internal void TransitionForWrite(Vk vulkan, CommandBuffer commandBuffer) {
        bool multisampled = IsMultisampled;
        int capacity = (multisampled ? 2 : 1) + (_hasDepth ? (multisampled ? 2 : 1) : 0);
        Span<ImageMemoryBarrier> barriers = stackalloc ImageMemoryBarrier[capacity];
        int count = 0;
        if (multisampled) {
            barriers[count++] = CreateImageBarrier(
                _multisampledColorImage,
                ImageAspectFlags.ColorBit,
                _multisampledColorLayout,
                ImageLayout.ColorAttachmentOptimal,
                ColorSourceAccess(_multisampledColorLayout),
                COLOR_ATTACHMENT_ACCESS);
        }

        barriers[count++] = CreateImageBarrier(
            _resolvedColorImage,
            ImageAspectFlags.ColorBit,
            _resolvedColorLayout,
            ImageLayout.ColorAttachmentOptimal,
            ColorSourceAccess(_resolvedColorLayout),
            COLOR_ATTACHMENT_ACCESS);
        if (_hasDepth) {
            if (multisampled) {
                barriers[count++] = CreateImageBarrier(
                    _multisampledDepthImage,
                    ImageAspectFlags.DepthBit,
                    _multisampledDepthLayout,
                    ImageLayout.DepthStencilAttachmentOptimal,
                    DepthSourceAccess(_multisampledDepthLayout),
                    DEPTH_ATTACHMENT_ACCESS);
            }

            barriers[count++] = CreateImageBarrier(
                _resolvedDepthImage,
                ImageAspectFlags.DepthBit,
                _resolvedDepthLayout,
                ImageLayout.DepthStencilAttachmentOptimal,
                ResolvedDepthSourceAccess(_resolvedDepthLayout),
                ResolvedDepthWriteAccess());
        }

        // TopOfPipe waits for every earlier command, which covers an attachment that was Undefined, but
        // it supports no access flags. The combined mask keeps the shader-read and attachment-write
        // access flags legal for the images that are already in a defined layout.
        PipelineStageFlags sourceStage = PipelineStageFlags.TopOfPipeBit
            | PipelineStageFlags.ColorAttachmentOutputBit
            | PipelineStageFlags.EarlyFragmentTestsBit
            | PipelineStageFlags.LateFragmentTestsBit
            | PipelineStageFlags.FragmentShaderBit;
        RecordImageBarriers(
            vulkan,
            commandBuffer,
            sourceStage,
            PipelineStageFlags.ColorAttachmentOutputBit
                | PipelineStageFlags.EarlyFragmentTestsBit
                | PipelineStageFlags.LateFragmentTestsBit,
            barriers[..count]);
        _multisampledColorLayout = multisampled ? ImageLayout.ColorAttachmentOptimal : ImageLayout.Undefined;
        _resolvedColorLayout = ImageLayout.ColorAttachmentOptimal;
        _multisampledDepthLayout = _hasDepth && multisampled
            ? ImageLayout.DepthStencilAttachmentOptimal
            : ImageLayout.Undefined;
        _resolvedDepthLayout = _hasDepth ? ImageLayout.DepthStencilAttachmentOptimal : ImageLayout.Undefined;
    }

    /// <summary>
    /// Transitions the single-sample resolved color and depth images to <c>ShaderReadOnlyOptimal</c>
    /// after a writing pass, so a later camera can sample them and a later writing pass can transition
    /// them back.
    /// </summary>
    /// <param name="vulkan">The Vulkan functions.</param>
    /// <param name="commandBuffer">The command buffer recording the frame.</param>
    internal void TransitionResolvedToShaderRead(Vk vulkan, CommandBuffer commandBuffer) {
        bool colorReadable = _resolvedColorLayout == ImageLayout.ShaderReadOnlyOptimal;
        bool depthReadable = !_hasDepth || _resolvedDepthLayout == ImageLayout.ShaderReadOnlyOptimal;
        if (colorReadable && depthReadable) {
            return;
        }

        Span<ImageMemoryBarrier> barriers = stackalloc ImageMemoryBarrier[_hasDepth ? 2 : 1];
        int count = 0;
        PipelineStageFlags sourceStage = PipelineStageFlags.None;
        if (!colorReadable) {
            sourceStage |= ColorSourceStage(_resolvedColorLayout);
            barriers[count++] = CreateImageBarrier(
                _resolvedColorImage,
                ImageAspectFlags.ColorBit,
                _resolvedColorLayout,
                ImageLayout.ShaderReadOnlyOptimal,
                ColorSourceAccess(_resolvedColorLayout),
                AccessFlags.ShaderReadBit);
            _resolvedColorLayout = ImageLayout.ShaderReadOnlyOptimal;
        }

        if (!depthReadable) {
            sourceStage |= ResolvedDepthSourceStage(_resolvedDepthLayout);
            barriers[count++] = CreateImageBarrier(
                _resolvedDepthImage,
                ImageAspectFlags.DepthBit,
                _resolvedDepthLayout,
                ImageLayout.ShaderReadOnlyOptimal,
                ResolvedDepthSourceAccess(_resolvedDepthLayout),
                AccessFlags.ShaderReadBit);
            _resolvedDepthLayout = ImageLayout.ShaderReadOnlyOptimal;
        }

        RecordImageBarriers(
            vulkan,
            commandBuffer,
            sourceStage,
            PipelineStageFlags.FragmentShaderBit,
            barriers[..count]);
    }

    /// <summary>
    /// Prepares the resolved color and depth images for a sampling pass: transitions them to
    /// <c>ShaderReadOnlyOptimal</c>, or synchronizes the previous frame's writes with the read when they
    /// are already in that layout.
    /// </summary>
    /// <param name="vulkan">The Vulkan functions.</param>
    /// <param name="commandBuffer">The command buffer recording the frame.</param>
    internal void PrepareSampled(Vk vulkan, CommandBuffer commandBuffer) {
        Span<ImageMemoryBarrier> barriers = stackalloc ImageMemoryBarrier[_hasDepth ? 2 : 1];
        int count = 0;
        PipelineStageFlags sourceStage = PipelineStageFlags.None;
        if (_resolvedColorLayout == ImageLayout.ShaderReadOnlyOptimal) {
            // The image is already readable; the barrier keeps the previous frame's write visible to
            // this frame's fragment shader reads across submissions on the same queue.
            barriers[count++] = CreateImageBarrier(
                _resolvedColorImage,
                ImageAspectFlags.ColorBit,
                ImageLayout.ShaderReadOnlyOptimal,
                ImageLayout.ShaderReadOnlyOptimal,
                AccessFlags.ShaderReadBit,
                AccessFlags.ShaderReadBit);
            sourceStage |= PipelineStageFlags.FragmentShaderBit | PipelineStageFlags.ColorAttachmentOutputBit;
        } else {
            sourceStage |= ColorSourceStage(_resolvedColorLayout);
            barriers[count++] = CreateImageBarrier(
                _resolvedColorImage,
                ImageAspectFlags.ColorBit,
                _resolvedColorLayout,
                ImageLayout.ShaderReadOnlyOptimal,
                ColorSourceAccess(_resolvedColorLayout),
                AccessFlags.ShaderReadBit);
            _resolvedColorLayout = ImageLayout.ShaderReadOnlyOptimal;
        }

        if (_hasDepth) {
            if (_resolvedDepthLayout == ImageLayout.ShaderReadOnlyOptimal) {
                PipelineStageFlags depthSourceStage =
                    PipelineStageFlags.FragmentShaderBit | PipelineStageFlags.LateFragmentTestsBit;
                AccessFlags depthSourceAccess = AccessFlags.ShaderReadBit;
                if (IsMultisampled) {
                    // The previous frame's resolve wrote the image at the color-attachment stage.
                    depthSourceStage |= PipelineStageFlags.ColorAttachmentOutputBit;
                    depthSourceAccess |= AccessFlags.ColorAttachmentWriteBit;
                }

                barriers[count++] = CreateImageBarrier(
                    _resolvedDepthImage,
                    ImageAspectFlags.DepthBit,
                    ImageLayout.ShaderReadOnlyOptimal,
                    ImageLayout.ShaderReadOnlyOptimal,
                    depthSourceAccess,
                    AccessFlags.ShaderReadBit);
                sourceStage |= depthSourceStage;
            } else {
                sourceStage |= ResolvedDepthSourceStage(_resolvedDepthLayout);
                barriers[count++] = CreateImageBarrier(
                    _resolvedDepthImage,
                    ImageAspectFlags.DepthBit,
                    _resolvedDepthLayout,
                    ImageLayout.ShaderReadOnlyOptimal,
                    ResolvedDepthSourceAccess(_resolvedDepthLayout),
                    AccessFlags.ShaderReadBit);
                _resolvedDepthLayout = ImageLayout.ShaderReadOnlyOptimal;
            }
        }

        RecordImageBarriers(vulkan, commandBuffer, sourceStage, PipelineStageFlags.FragmentShaderBit, barriers[..count]);
    }

    /// <summary>
    /// Synchronizes the previous camera pass' attachment writes with the next pass' loads and resolves
    /// for two consecutive writing passes of the app scene target.
    /// </summary>
    /// <param name="vulkan">The Vulkan functions.</param>
    /// <param name="commandBuffer">The command buffer recording the frame.</param>
    internal void SynchronizeBetweenPasses(Vk vulkan, CommandBuffer commandBuffer) {
        bool multisampled = IsMultisampled;
        int capacity = (multisampled ? 2 : 1) + (_hasDepth ? (multisampled ? 2 : 1) : 0);
        Span<ImageMemoryBarrier> barriers = stackalloc ImageMemoryBarrier[capacity];
        int count = 0;
        if (multisampled) {
            barriers[count++] = CreateImageBarrier(
                _multisampledColorImage,
                ImageAspectFlags.ColorBit,
                ImageLayout.ColorAttachmentOptimal,
                ImageLayout.ColorAttachmentOptimal,
                AccessFlags.ColorAttachmentWriteBit,
                COLOR_ATTACHMENT_ACCESS);
        }

        // The next pass loads the previous pass' color and depth and resolves into the resolved images,
        // so its destination scope includes the attachment reads and writes.
        barriers[count++] = CreateImageBarrier(
            _resolvedColorImage,
            ImageAspectFlags.ColorBit,
            ImageLayout.ColorAttachmentOptimal,
            ImageLayout.ColorAttachmentOptimal,
            AccessFlags.ColorAttachmentWriteBit,
            COLOR_ATTACHMENT_ACCESS);
        if (_hasDepth) {
            if (multisampled) {
                barriers[count++] = CreateImageBarrier(
                    _multisampledDepthImage,
                    ImageAspectFlags.DepthBit,
                    ImageLayout.DepthStencilAttachmentOptimal,
                    ImageLayout.DepthStencilAttachmentOptimal,
                    AccessFlags.DepthStencilAttachmentWriteBit,
                    DEPTH_ATTACHMENT_ACCESS);
            }

            barriers[count++] = CreateImageBarrier(
                _resolvedDepthImage,
                ImageAspectFlags.DepthBit,
                ImageLayout.DepthStencilAttachmentOptimal,
                ImageLayout.DepthStencilAttachmentOptimal,
                ResolvedDepthSourceAccess(ImageLayout.DepthStencilAttachmentOptimal),
                ResolvedDepthWriteAccess());
        }

        RecordImageBarriers(
            vulkan,
            commandBuffer,
            PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.LateFragmentTestsBit,
            PipelineStageFlags.ColorAttachmentOutputBit
                | PipelineStageFlags.EarlyFragmentTestsBit
                | PipelineStageFlags.LateFragmentTestsBit,
            barriers[..count]);
    }

    private static AccessFlags ColorSourceAccess(ImageLayout layout) => layout switch {
        ImageLayout.Undefined => AccessFlags.None,
        ImageLayout.ShaderReadOnlyOptimal => AccessFlags.ShaderReadBit,
        ImageLayout.ColorAttachmentOptimal => COLOR_ATTACHMENT_ACCESS,
        _ => AccessFlags.None,
    };

    private static AccessFlags DepthSourceAccess(ImageLayout layout) => layout switch {
        ImageLayout.Undefined => AccessFlags.None,
        ImageLayout.ShaderReadOnlyOptimal => AccessFlags.ShaderReadBit,
        ImageLayout.DepthStencilAttachmentOptimal => DEPTH_ATTACHMENT_ACCESS,
        _ => AccessFlags.None,
    };

    private static PipelineStageFlags ColorSourceStage(ImageLayout layout) => layout switch {
        ImageLayout.Undefined => PipelineStageFlags.TopOfPipeBit,
        ImageLayout.ShaderReadOnlyOptimal => PipelineStageFlags.FragmentShaderBit,
        ImageLayout.ColorAttachmentOptimal => PipelineStageFlags.ColorAttachmentOutputBit,
        _ => PipelineStageFlags.AllCommandsBit,
    };

    private static PipelineStageFlags DepthSourceStage(ImageLayout layout) => layout switch {
        ImageLayout.Undefined => PipelineStageFlags.TopOfPipeBit,
        ImageLayout.ShaderReadOnlyOptimal => PipelineStageFlags.FragmentShaderBit,
        ImageLayout.DepthStencilAttachmentOptimal => PipelineStageFlags.LateFragmentTestsBit,
        _ => PipelineStageFlags.AllCommandsBit,
    };

    // Vulkan performs a depth/stencil resolve in the color-attachment output stage and with
    // color-attachment write access even though the destination is a depth image. The resolved depth
    // image is therefore written at ColorAttachmentOutput/ColorAttachmentWrite, not at the depth
    // attachment's LateFragmentTests/DepthStencilAttachmentWrite scope. A single-sample depth image has
    // no resolve: the depth test writes it in late fragment tests.
    private AccessFlags ResolvedDepthSourceAccess(ImageLayout layout) => IsMultisampled
        ? DepthSourceAccess(layout) | AccessFlags.ColorAttachmentWriteBit
        : DepthSourceAccess(layout);

    private PipelineStageFlags ResolvedDepthSourceStage(ImageLayout layout) => IsMultisampled
        ? DepthSourceStage(layout) | PipelineStageFlags.ColorAttachmentOutputBit
        : DepthSourceStage(layout);

    private AccessFlags ResolvedDepthWriteAccess() => IsMultisampled
        ? DEPTH_ATTACHMENT_ACCESS | AccessFlags.ColorAttachmentWriteBit
        : DEPTH_ATTACHMENT_ACCESS;

    private void CreateResources() {
        PhysicalDeviceMemoryProperties memoryProperties = _vulkan.GetPhysicalDeviceMemoryProperties(_physicalDevice);
        bool multisampled = IsMultisampled;
        SampleCountFlags samples = ToSampleCountFlags(SampleCount);

        ImageResources? multisampledColor = null;
        if (multisampled) {
            multisampledColor = CreateAttachment(
                ColorFormat,
                samples,
                ImageUsageFlags.ColorAttachmentBit,
                ImageAspectFlags.ColorBit,
                memoryProperties);
        }

        ImageResources resolvedColor = CreateAttachment(
            ColorFormat,
            SampleCountFlags.Count1Bit,
            ImageUsageFlags.SampledBit | ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit,
            ImageAspectFlags.ColorBit,
            memoryProperties);

        ImageResources multisampledDepth = default;
        ImageResources resolvedDepth = default;
        if (_hasDepth) {
            if (multisampled) {
                multisampledDepth = CreateAttachment(
                    Format.D32Sfloat,
                    samples,
                    ImageUsageFlags.DepthStencilAttachmentBit,
                    ImageAspectFlags.DepthBit,
                    memoryProperties);
            }

            // The resolved depth image is sampled by the public DepthTexture view and by an internal
            // consumer such as the scene target's SSAO pass. A single-sample target uses this image as
            // its depth attachment directly, so it carries both usages and no resolve attachment.
            resolvedDepth = CreateAttachment(
                Format.D32Sfloat,
                SampleCountFlags.Count1Bit,
                ImageUsageFlags.DepthStencilAttachmentBit
                    | ImageUsageFlags.SampledBit
                    | ImageUsageFlags.TransferSrcBit,
                ImageAspectFlags.DepthBit,
                memoryProperties);
        }

        _renderPass = CreateRenderPass(multisampled);
        ImageView[] attachments = multisampled
            ? (_hasDepth
                ? [multisampledColor!.Value.View, resolvedColor.View, multisampledDepth.View, resolvedDepth.View]
                : [multisampledColor!.Value.View, resolvedColor.View])
            : (_hasDepth
                ? [resolvedColor.View, resolvedDepth.View]
                : [resolvedColor.View]);
        _framebuffer = CreateFramebuffer(attachments);
        _multisampledColorImage = multisampled ? multisampledColor!.Value.Image : default;
        _multisampledDepthImage = multisampled && _hasDepth ? multisampledDepth.Image : default;
        _resolvedColorImage = resolvedColor.Image;
        _resolvedDepthImage = _hasDepth ? resolvedDepth.Image : default;
        _resolvedColorView = resolvedColor.View;
        _resolvedDepthView = _hasDepth ? resolvedDepth.View : default;
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

    private RenderPass CreateRenderPass(bool multisampled) {
        int attachmentCount = (multisampled ? 2 : 1) + (_hasDepth ? (multisampled ? 2 : 1) : 0);
        AttachmentDescription2[] attachments = new AttachmentDescription2[attachmentCount];
        int index = 0;
        if (multisampled) {
            attachments[index++] = CreateAttachmentDescription(
                ColorFormat,
                ToSampleCountFlags(SampleCount),
                AttachmentLoadOp.Load,
                AttachmentStoreOp.Store,
                ImageLayout.ColorAttachmentOptimal);
            attachments[index++] = CreateAttachmentDescription(
                ColorFormat,
                SampleCountFlags.Count1Bit,
                AttachmentLoadOp.DontCare,
                AttachmentStoreOp.Store,
                ImageLayout.ColorAttachmentOptimal);
        } else {
            attachments[index++] = CreateAttachmentDescription(
                ColorFormat,
                SampleCountFlags.Count1Bit,
                AttachmentLoadOp.Load,
                AttachmentStoreOp.Store,
                ImageLayout.ColorAttachmentOptimal);
        }

        uint depthAttachmentIndex = multisampled ? 2u : 1u;
        uint resolvedDepthAttachmentIndex = multisampled ? 3u : 1u;
        if (_hasDepth) {
            if (multisampled) {
                attachments[index++] = CreateAttachmentDescription(
                    Format.D32Sfloat,
                    ToSampleCountFlags(SampleCount),
                    AttachmentLoadOp.Load,
                    AttachmentStoreOp.Store,
                    ImageLayout.DepthStencilAttachmentOptimal);
            }

            // A multisampled target resolves the depth attachment into the sampled image with
            // VK_RESOLVE_MODE_SAMPLE_ZERO_BIT (or the selected fallback). A single-sample target has no
            // resolve attachment: its depth image is already the resolved depth.
            attachments[index] = CreateAttachmentDescription(
                Format.D32Sfloat,
                SampleCountFlags.Count1Bit,
                multisampled ? AttachmentLoadOp.DontCare : AttachmentLoadOp.Load,
                AttachmentStoreOp.Store,
                ImageLayout.DepthStencilAttachmentOptimal);
        }

        AttachmentReference2 colorReference = new() {
            SType = StructureType.AttachmentReference2,
            Attachment = 0,
            Layout = ImageLayout.ColorAttachmentOptimal,
            AspectMask = ImageAspectFlags.ColorBit,
        };
        AttachmentReference2 resolveReference = new() {
            SType = StructureType.AttachmentReference2,
            Attachment = 1,
            Layout = ImageLayout.ColorAttachmentOptimal,
            AspectMask = ImageAspectFlags.ColorBit,
        };
        AttachmentReference2 depthReference = new() {
            SType = StructureType.AttachmentReference2,
            Attachment = depthAttachmentIndex,
            Layout = ImageLayout.DepthStencilAttachmentOptimal,
            AspectMask = ImageAspectFlags.DepthBit,
        };
        AttachmentReference2 resolvedDepthReference = new() {
            SType = StructureType.AttachmentReference2,
            Attachment = resolvedDepthAttachmentIndex,
            Layout = ImageLayout.DepthStencilAttachmentOptimal,
            AspectMask = ImageAspectFlags.DepthBit,
        };
        bool resolvesDepth = _hasDepth && multisampled;
        SubpassDescriptionDepthStencilResolve depthResolve = new() {
            SType = StructureType.SubpassDescriptionDepthStencilResolve,
            DepthResolveMode = _depthResolveMode,
            StencilResolveMode = ResolveModeFlags.None,
            PDepthStencilResolveAttachment = resolvesDepth ? &resolvedDepthReference : null,
        };
        SubpassDescription2 subpass = new() {
            SType = StructureType.SubpassDescription2,
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorReference,
            PResolveAttachments = multisampled ? &resolveReference : null,
            PDepthStencilAttachment = _hasDepth ? &depthReference : null,
            PNext = resolvesDepth ? &depthResolve : null,
        };

        // The backend transitions the attachments to their attachment layouts before the first pass and
        // records explicit barriers between passes, so the render pass needs only the external
        // dependency that orders each load after the preceding scene work. The destination access includes
        // the attachment reads because every attachment uses LoadOp=Load. There is no final dependency:
        // the backend transitions the resolved color and depth images for sampling or presentation itself.
        PipelineStageFlags sourceStage = PipelineStageFlags.ColorAttachmentOutputBit;
        AccessFlags sourceAccess = AccessFlags.ColorAttachmentWriteBit;
        PipelineStageFlags destinationStage = PipelineStageFlags.ColorAttachmentOutputBit;
        AccessFlags destinationAccess = COLOR_ATTACHMENT_ACCESS;
        if (_hasDepth) {
            sourceStage |= PipelineStageFlags.LateFragmentTestsBit;
            sourceAccess |= AccessFlags.DepthStencilAttachmentWriteBit;
            destinationStage |= PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit;
            destinationAccess |= DEPTH_ATTACHMENT_ACCESS;
        }

        SubpassDependency2 dependency = new() {
            SType = StructureType.SubpassDependency2,
            SrcSubpass = SUBPASS_EXTERNAL,
            DstSubpass = 0,
            SrcStageMask = sourceStage,
            DstStageMask = destinationStage,
            SrcAccessMask = sourceAccess,
            DstAccessMask = destinationAccess,
            DependencyFlags = 0,
        };
        fixed (AttachmentDescription2* attachmentsPointer = attachments) {
            RenderPassCreateInfo2 createInfo = new() {
                SType = StructureType.RenderPassCreateInfo2,
                AttachmentCount = (uint)attachments.Length,
                PAttachments = attachmentsPointer,
                SubpassCount = 1,
                PSubpasses = &subpass,
                DependencyCount = 1,
                PDependencies = &dependency,
            };
            Result result = _vulkan.CreateRenderPass2(_device, &createInfo, null, out RenderPass renderPass);
            ThrowIfFailed(result, "vkCreateRenderPass2");
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

    private static AttachmentDescription2 CreateAttachmentDescription(
        Format format,
        SampleCountFlags samples,
        AttachmentLoadOp loadOp,
        AttachmentStoreOp storeOp,
        ImageLayout layout) => new() {
            SType = StructureType.AttachmentDescription2,
            Format = format,
            Samples = samples,
            LoadOp = loadOp,
            StoreOp = storeOp,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            // The backend keeps each attachment in its attachment layout for the whole pass, so the
            // render pass never transitions it and loadOp always sees the previous pass' stored content
            // (or undefined content after the frame-start transition).
            InitialLayout = layout,
            FinalLayout = layout,
        };

    private static void ValidateColorFormat(VulkanDevice device, Format format) {
        FormatProperties properties = device.Vulkan.GetPhysicalDeviceFormatProperties(device.PhysicalDevice, format);
        FormatFeatureFlags required = FormatFeatureFlags.ColorAttachmentBit
            | FormatFeatureFlags.SampledImageBit
            | FormatFeatureFlags.TransferSrcBit;
        FormatFeatureFlags missing = required & ~properties.OptimalTilingFeatures;
        if (missing != 0) {
            throw new GraphicsException(
                $"The device does not support the render target color format {VulkanFormatInfo.Describe(format)}; "
                    + $"the required optimal-tiling features 0x{(uint)missing:X8} are missing "
                    + $"(supported features 0x{(uint)properties.OptimalTilingFeatures:X8}).",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null);
        }
    }

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

    private static ImageMemoryBarrier CreateImageBarrier(
        Image image,
        ImageAspectFlags aspect,
        ImageLayout oldLayout,
        ImageLayout newLayout,
        AccessFlags sourceAccess,
        AccessFlags destinationAccess) => new() {
            SType = StructureType.ImageMemoryBarrier,
            SrcAccessMask = sourceAccess,
            DstAccessMask = destinationAccess,
            OldLayout = oldLayout,
            NewLayout = newLayout,
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

    private static void RecordImageBarriers(
        Vk vulkan,
        CommandBuffer commandBuffer,
        PipelineStageFlags sourceStage,
        PipelineStageFlags destinationStage,
        ReadOnlySpan<ImageMemoryBarrier> barriers) {
        fixed (ImageMemoryBarrier* pointer = barriers) {
            vulkan.CmdPipelineBarrier(
                commandBuffer,
                sourceStage,
                destinationStage,
                0,
                0,
                null,
                0,
                null,
                checked((uint)barriers.Length),
                pointer);
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

    private readonly record struct ImageResources(Image Image, ImageView View, DeviceMemory Memory);
}
