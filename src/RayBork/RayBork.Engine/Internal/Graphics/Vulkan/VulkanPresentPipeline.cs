using System.Runtime.InteropServices;
using System.Text;
using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// The 32-byte push-constant block the present fragment shader reads.
/// </summary>
/// <remarks>
/// The layout is the shader ABI: four floats at offsets 0, 4, 8, and 12, followed by four 32-bit
/// integers at offsets 16, 20, 24, and 28, for a total of 32 bytes. The struct is sequential and
/// contains only unmanaged fields so it can be passed to <c>vkCmdPushConstants</c> directly. Keep the
/// field order; <c>Present.frag.glsl</c> declares the same block.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct PresentPushConstants {
    /// <summary>The exposure multiplier, computed on the CPU as <c>exp2(exposureEv)</c>.</summary>
    public float ExposureMultiplier;

    /// <summary>
    /// The paper white in scRGB units: the window's reported SDR white level on an HDR window, or
    /// one on an SDR window.
    /// </summary>
    public float HdrWhiteScale;

    /// <summary>
    /// The displayed peak in scRGB units: the paper white multiplied by the reported HDR headroom,
    /// or one on an SDR window.
    /// </summary>
    public float HdrPeakScale;

    /// <summary>
    /// The AgX white point in scene-linear luminance after exposure; the highlight knee.
    /// </summary>
    public float AgxWhiteSceneLuminance;

    /// <summary>The tone mapping selector: <c>0</c> is AgX and <c>1</c> is none.</summary>
    public int Tonemapping;

    /// <summary>The encoding selector, as a <see cref="PresentEncoding"/> value.</summary>
    public int Encoding;

    /// <summary>Whether the HDR highlight extension is active: <c>1</c> when it is, else <c>0</c>.</summary>
    public int HdrHighlight;

    /// <summary>Reserved padding, always <c>0</c>; keeps the block 32 bytes.</summary>
    public int Reserved;
}

/// <summary>
/// The present pass: a fullscreen triangle that samples the resolved linear scene color and writes
/// the display-referred value into the swapchain image.
/// </summary>
/// <remarks>
/// <para>
/// The pipeline bakes the swapchain format into its render pass, so a format change requires a new
/// instance. The viewport and scissor are dynamic, so an extent-only change reuses the pipeline and
/// only rebuilds the framebuffers through <see cref="RecreateFramebuffers"/>.
/// </para>
/// <para>
/// Every member is render-thread only. The caller waits for the device to idle before recreating or
/// disposing. Dispose is idempotent.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanPresentPipeline : IDisposable {
    private const uint SUBPASS_EXTERNAL = uint.MaxValue;
    private const uint PUSH_CONSTANT_OFFSET = 0;

    private readonly Vk _vulkan;
    private readonly Device _device;
    private readonly Format _format;

    private DescriptorSetLayout _descriptorSetLayout;
    private DescriptorPool _descriptorPool;
    private DescriptorSet _descriptorSet;
    private Sampler _sampler;
    private PipelineLayout _pipelineLayout;
    private RenderPass _renderPass;
    private Pipeline _pipeline;
    private Framebuffer[] _framebuffers = [];
    private Extent2D _extent;
    private bool _disposed;

    private VulkanPresentPipeline(Vk vulkan, Device device, Extent2D extent, Format format) {
        _vulkan = vulkan;
        _device = device;
        _extent = extent;
        _format = format;
    }

    /// <summary>
    /// Gets the swapchain format the pipeline renders to.
    /// </summary>
    internal Format Format => _format;

    /// <summary>
    /// Creates the descriptor state, render pass, and pipeline.
    /// </summary>
    /// <remarks>
    /// The framebuffers are created by <see cref="RecreateFramebuffers"/>, because the caller owns the
    /// swapchain image views and passes them right after creation and again after every swapchain
    /// recreation.
    /// </remarks>
    /// <param name="device">The device that owns the pipeline.</param>
    /// <param name="extent">The positive swapchain extent.</param>
    /// <param name="swapchainFormat">The swapchain color attachment format.</param>
    /// <param name="sceneColorView">The resolved scene color view the fragment shader samples.</param>
    /// <returns>The created present pipeline.</returns>
    /// <exception cref="GraphicsException">
    /// Descriptor state, the render pass, the shader modules, or the pipeline could not be created.
    /// The diagnostic code is <c>RB3001</c>.
    /// </exception>
    internal static VulkanPresentPipeline Create(
        VulkanDevice device,
        Extent2D extent,
        Format swapchainFormat,
        ImageView sceneColorView) {
        ArgumentNullException.ThrowIfNull(device);
        if (extent.Width == 0 || extent.Height == 0) {
            throw new ArgumentOutOfRangeException(nameof(extent), extent, "The present extent must be positive.");
        }

        if (sceneColorView.Handle == 0) {
            throw new ArgumentException("The scene color view must not be the default handle.", nameof(sceneColorView));
        }

        VulkanPresentPipeline pipeline = new(device.Vulkan, device.Device, extent, swapchainFormat);
        try {
            pipeline.CreateResources(sceneColorView);
            return pipeline;
        } catch {
            pipeline.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Writes the descriptor set to sample a new resolved scene color view.
    /// </summary>
    /// <remarks>
    /// The caller must ensure the descriptor set is not in use, which a device wait-idle provides.
    /// </remarks>
    /// <param name="sceneColorView">The resolved scene color view the fragment shader samples.</param>
    internal void WriteDescriptor(ImageView sceneColorView) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (sceneColorView.Handle == 0) {
            throw new ArgumentException("The scene color view must not be the default handle.", nameof(sceneColorView));
        }

        DescriptorImageInfo imageInfo = new() {
            Sampler = _sampler,
            ImageView = sceneColorView,
            ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
        };
        WriteDescriptorSet write = new() {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _descriptorSet,
            DstBinding = 0,
            DstArrayElement = 0,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.CombinedImageSampler,
            PImageInfo = &imageInfo,
        };
        _vulkan.UpdateDescriptorSets(_device, 1, &write, 0, null);
    }

    /// <summary>
    /// Rebuilds the per-swapchain-image framebuffers for a new set of swapchain image views.
    /// </summary>
    /// <remarks>
    /// The caller must wait for the device to idle first. The pipeline and render pass are reused, so
    /// the format and the render pass must be unchanged.
    /// </remarks>
    /// <param name="extent">The new swapchain extent.</param>
    /// <param name="swapchainImageViews">The swapchain image views, in image index order.</param>
    internal void RecreateFramebuffers(Extent2D extent, IReadOnlyList<ImageView> swapchainImageViews) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(swapchainImageViews);
        if (extent.Width == 0 || extent.Height == 0) {
            throw new ArgumentOutOfRangeException(nameof(extent), extent, "The present extent must be positive.");
        }

        DestroyFramebuffers();
        _extent = extent;
        _framebuffers = new Framebuffer[swapchainImageViews.Count];
        try {
            for (int index = 0; index < swapchainImageViews.Count; index++) {
                _framebuffers[index] = CreateFramebuffer(swapchainImageViews[index]);
            }
        } catch {
            DestroyFramebuffers();
            throw;
        }
    }

    /// <summary>
    /// Records the present pass for one swapchain image.
    /// </summary>
    /// <param name="vulkan">The loaded Vulkan API.</param>
    /// <param name="commandBuffer">The command buffer to record into.</param>
    /// <param name="imageIndex">The acquired swapchain image index.</param>
    /// <param name="constants">
    /// The exposure, paper white, peak, AgX knee, tone mapping, encoding, and highlight values.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="imageIndex"/> has no framebuffer because <see cref="RecreateFramebuffers"/> has
    /// not run for the current swapchain.
    /// </exception>
    internal void Record(
        Vk vulkan,
        CommandBuffer commandBuffer,
        uint imageIndex,
        PresentPushConstants constants) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (imageIndex >= (uint)_framebuffers.Length) {
            throw new ArgumentOutOfRangeException(
                nameof(imageIndex),
                imageIndex,
                "The present pipeline has no framebuffer for the swapchain image.");
        }

        Viewport viewport = new() {
            X = 0f,
            Y = 0f,
            Width = _extent.Width,
            Height = _extent.Height,
            MinDepth = VulkanDepthState.MIN_DEPTH,
            MaxDepth = VulkanDepthState.MAX_DEPTH,
        };
        Rect2D scissor = new() {
            Offset = new Offset2D { X = 0, Y = 0 },
            Extent = _extent,
        };
        RenderPassBeginInfo beginInfo = new() {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _renderPass,
            Framebuffer = _framebuffers[imageIndex],
            RenderArea = scissor,
            ClearValueCount = 0,
            PClearValues = null,
        };
        DescriptorSet descriptorSet = _descriptorSet;
        vulkan.CmdBeginRenderPass(commandBuffer, &beginInfo, SubpassContents.Inline);
        vulkan.CmdSetViewport(commandBuffer, 0, 1, &viewport);
        vulkan.CmdSetScissor(commandBuffer, 0, 1, &scissor);
        vulkan.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, _pipeline);
        vulkan.CmdBindDescriptorSets(
            commandBuffer,
            PipelineBindPoint.Graphics,
            _pipelineLayout,
            0,
            1,
            &descriptorSet,
            0,
            null);
        vulkan.CmdPushConstants(
            commandBuffer,
            _pipelineLayout,
            ShaderStageFlags.FragmentBit,
            PUSH_CONSTANT_OFFSET,
            (uint)sizeof(PresentPushConstants),
            &constants);
        vulkan.CmdDraw(commandBuffer, 3, 1, 0, 0);
        vulkan.CmdEndRenderPass(commandBuffer);
    }

    /// <summary>
    /// Destroys the framebuffers, pipeline, pipeline layout, descriptor pool and set layout, sampler,
    /// and render pass. The call is idempotent.
    /// </summary>
    public void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;
        DestroyFramebuffers();
        if (_pipeline.Handle != 0) {
            _vulkan.DestroyPipeline(_device, _pipeline, null);
            _pipeline = default;
        }

        if (_pipelineLayout.Handle != 0) {
            _vulkan.DestroyPipelineLayout(_device, _pipelineLayout, null);
            _pipelineLayout = default;
        }

        if (_descriptorPool.Handle != 0) {
            _vulkan.DestroyDescriptorPool(_device, _descriptorPool, null);
            _descriptorPool = default;
            _descriptorSet = default;
        }

        if (_descriptorSetLayout.Handle != 0) {
            _vulkan.DestroyDescriptorSetLayout(_device, _descriptorSetLayout, null);
            _descriptorSetLayout = default;
        }

        if (_sampler.Handle != 0) {
            _vulkan.DestroySampler(_device, _sampler, null);
            _sampler = default;
        }

        if (_renderPass.Handle != 0) {
            _vulkan.DestroyRenderPass(_device, _renderPass, null);
            _renderPass = default;
        }
    }

    private void CreateResources(ImageView sceneColorView) {
        _descriptorSetLayout = CreateDescriptorSetLayout();
        _descriptorPool = CreateDescriptorPool();
        _descriptorSet = AllocateDescriptorSet();
        _sampler = CreateSampler();
        WriteDescriptor(sceneColorView);
        _pipelineLayout = CreatePipelineLayout();
        _renderPass = CreateRenderPass();
        _pipeline = CreatePipeline();
    }

    private DescriptorSetLayout CreateDescriptorSetLayout() {
        DescriptorSetLayoutBinding binding = new() {
            Binding = 0,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit,
            PImmutableSamplers = null,
        };
        DescriptorSetLayoutCreateInfo createInfo = new() {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 1,
            PBindings = &binding,
        };
        Result result = _vulkan.CreateDescriptorSetLayout(_device, &createInfo, null, out DescriptorSetLayout layout);
        ThrowIfFailed(result, "vkCreateDescriptorSetLayout");
        return layout;
    }

    private DescriptorPool CreateDescriptorPool() {
        DescriptorPoolSize poolSize = new() {
            Type = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
        };
        DescriptorPoolCreateInfo createInfo = new() {
            SType = StructureType.DescriptorPoolCreateInfo,
            MaxSets = 1,
            PoolSizeCount = 1,
            PPoolSizes = &poolSize,
        };
        Result result = _vulkan.CreateDescriptorPool(_device, &createInfo, null, out DescriptorPool pool);
        ThrowIfFailed(result, "vkCreateDescriptorPool");
        return pool;
    }

    private DescriptorSet AllocateDescriptorSet() {
        DescriptorSetLayout layout = _descriptorSetLayout;
        DescriptorSetAllocateInfo allocateInfo = new() {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = _descriptorPool,
            DescriptorSetCount = 1,
            PSetLayouts = &layout,
        };
        Result result = _vulkan.AllocateDescriptorSets(_device, &allocateInfo, out DescriptorSet descriptorSet);
        ThrowIfFailed(result, "vkAllocateDescriptorSets");
        return descriptorSet;
    }

    private Sampler CreateSampler() {
        SamplerCreateInfo createInfo = new() {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            MipmapMode = SamplerMipmapMode.Nearest,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
            MipLodBias = 0f,
            AnisotropyEnable = false,
            MaxAnisotropy = 1f,
            CompareEnable = false,
            CompareOp = CompareOp.Always,
            MinLod = 0f,
            MaxLod = 0f,
            BorderColor = BorderColor.FloatOpaqueBlack,
            UnnormalizedCoordinates = false,
        };
        Result result = _vulkan.CreateSampler(_device, &createInfo, null, out Sampler sampler);
        ThrowIfFailed(result, "vkCreateSampler");
        return sampler;
    }

    private PipelineLayout CreatePipelineLayout() {
        DescriptorSetLayout setLayout = _descriptorSetLayout;
        PushConstantRange pushConstantRange = new() {
            StageFlags = ShaderStageFlags.FragmentBit,
            Offset = PUSH_CONSTANT_OFFSET,
            Size = (uint)sizeof(PresentPushConstants),
        };
        PipelineLayoutCreateInfo createInfo = new() {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &setLayout,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &pushConstantRange,
        };
        Result result = _vulkan.CreatePipelineLayout(_device, &createInfo, null, out PipelineLayout layout);
        ThrowIfFailed(result, "vkCreatePipelineLayout");
        return layout;
    }

    private RenderPass CreateRenderPass() {
        AttachmentDescription attachment = new() {
            Format = _format,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.DontCare,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.PresentSrcKhr,
        };
        AttachmentReference colorReference = new() { Attachment = 0, Layout = ImageLayout.ColorAttachmentOptimal };
        SubpassDescription subpass = new() {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorReference,
            PResolveAttachments = null,
            PDepthStencilAttachment = null,
        };
        SubpassDependency dependency = new() {
            SrcSubpass = SUBPASS_EXTERNAL,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            SrcAccessMask = 0,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit,
            DependencyFlags = 0,
        };
        RenderPassCreateInfo createInfo = new() {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 1,
            PAttachments = &attachment,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 1,
            PDependencies = &dependency,
        };
        Result result = _vulkan.CreateRenderPass(_device, &createInfo, null, out RenderPass renderPass);
        ThrowIfFailed(result, "vkCreateRenderPass");
        return renderPass;
    }

    private Pipeline CreatePipeline() {
        byte[] vertexEntryPoint = Encoding.UTF8.GetBytes(ShaderAssets.PRESENT_VERTEX_ENTRY_POINT + "\0");
        byte[] fragmentEntryPoint = Encoding.UTF8.GetBytes(ShaderAssets.PRESENT_FRAGMENT_ENTRY_POINT + "\0");
        ShaderModule vertexModule = CreateShaderModule(ShaderAssets.PresentVertex);
        try {
            ShaderModule fragmentModule = CreateShaderModule(ShaderAssets.PresentFragment);
            try {
                fixed (byte* vertexName = vertexEntryPoint)
                fixed (byte* fragmentName = fragmentEntryPoint) {
                    PipelineShaderStageCreateInfo* stages = stackalloc PipelineShaderStageCreateInfo[2];
                    stages[0] = new() {
                        SType = StructureType.PipelineShaderStageCreateInfo,
                        Stage = ShaderStageFlags.VertexBit,
                        Module = vertexModule,
                        PName = vertexName,
                    };
                    stages[1] = new() {
                        SType = StructureType.PipelineShaderStageCreateInfo,
                        Stage = ShaderStageFlags.FragmentBit,
                        Module = fragmentModule,
                        PName = fragmentName,
                    };
                    PipelineVertexInputStateCreateInfo vertexInput = new() {
                        SType = StructureType.PipelineVertexInputStateCreateInfo,
                        VertexBindingDescriptionCount = 0,
                        PVertexBindingDescriptions = null,
                        VertexAttributeDescriptionCount = 0,
                        PVertexAttributeDescriptions = null,
                    };
                    PipelineInputAssemblyStateCreateInfo inputAssembly = new() {
                        SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                        Topology = Silk.NET.Vulkan.PrimitiveTopology.TriangleList,
                        PrimitiveRestartEnable = false,
                    };
                    PipelineViewportStateCreateInfo viewportState = new() {
                        SType = StructureType.PipelineViewportStateCreateInfo,
                        ViewportCount = 1,
                        PViewports = null,
                        ScissorCount = 1,
                        PScissors = null,
                    };
                    PipelineRasterizationStateCreateInfo rasterization = new() {
                        SType = StructureType.PipelineRasterizationStateCreateInfo,
                        DepthClampEnable = false,
                        RasterizerDiscardEnable = false,
                        PolygonMode = PolygonMode.Fill,
                        CullMode = CullModeFlags.None,
                        FrontFace = FrontFace.CounterClockwise,
                        DepthBiasEnable = false,
                        DepthBiasConstantFactor = 0f,
                        DepthBiasClamp = 0f,
                        DepthBiasSlopeFactor = 0f,
                        LineWidth = 1f,
                    };
                    PipelineMultisampleStateCreateInfo multisample = new() {
                        SType = StructureType.PipelineMultisampleStateCreateInfo,
                        RasterizationSamples = SampleCountFlags.Count1Bit,
                        SampleShadingEnable = false,
                        MinSampleShading = 0f,
                        PSampleMask = null,
                        AlphaToCoverageEnable = false,
                        AlphaToOneEnable = false,
                    };
                    PipelineColorBlendAttachmentState blendAttachment = new() {
                        BlendEnable = false,
                        SrcColorBlendFactor = BlendFactor.One,
                        DstColorBlendFactor = BlendFactor.Zero,
                        ColorBlendOp = BlendOp.Add,
                        SrcAlphaBlendFactor = BlendFactor.One,
                        DstAlphaBlendFactor = BlendFactor.Zero,
                        AlphaBlendOp = BlendOp.Add,
                        ColorWriteMask = ColorComponentFlags.RBit
                            | ColorComponentFlags.GBit
                            | ColorComponentFlags.BBit
                            | ColorComponentFlags.ABit,
                    };
                    PipelineColorBlendStateCreateInfo colorBlend = new() {
                        SType = StructureType.PipelineColorBlendStateCreateInfo,
                        LogicOpEnable = false,
                        LogicOp = LogicOp.Copy,
                        AttachmentCount = 1,
                        PAttachments = &blendAttachment,
                    };
                    DynamicState* dynamicStates = stackalloc DynamicState[2];
                    dynamicStates[0] = DynamicState.Viewport;
                    dynamicStates[1] = DynamicState.Scissor;
                    PipelineDynamicStateCreateInfo dynamicState = new() {
                        SType = StructureType.PipelineDynamicStateCreateInfo,
                        DynamicStateCount = 2,
                        PDynamicStates = dynamicStates,
                    };
                    GraphicsPipelineCreateInfo createInfo = new() {
                        SType = StructureType.GraphicsPipelineCreateInfo,
                        StageCount = 2,
                        PStages = stages,
                        PVertexInputState = &vertexInput,
                        PInputAssemblyState = &inputAssembly,
                        PViewportState = &viewportState,
                        PRasterizationState = &rasterization,
                        PMultisampleState = &multisample,
                        PDepthStencilState = null,
                        PColorBlendState = &colorBlend,
                        PDynamicState = &dynamicState,
                        Layout = _pipelineLayout,
                        RenderPass = _renderPass,
                        Subpass = 0,
                        BasePipelineHandle = default,
                        BasePipelineIndex = -1,
                    };
                    Result result = _vulkan.CreateGraphicsPipelines(
                        _device,
                        default,
                        1,
                        &createInfo,
                        null,
                        out Pipeline pipeline);
                    ThrowIfFailed(result, "vkCreateGraphicsPipelines");
                    return pipeline;
                }
            } finally {
                _vulkan.DestroyShaderModule(_device, fragmentModule, null);
            }
        } finally {
            _vulkan.DestroyShaderModule(_device, vertexModule, null);
        }
    }

    private ShaderModule CreateShaderModule(ReadOnlySpan<byte> code) {
        ShaderModule module;
        fixed (byte* codePointer = code) {
            ShaderModuleCreateInfo createInfo = new() {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (UIntPtr)(uint)code.Length,
                PCode = (uint*)codePointer,
            };
            Result result = _vulkan.CreateShaderModule(_device, &createInfo, null, out module);
            ThrowIfFailed(result, "vkCreateShaderModule");
        }

        return module;
    }

    private Framebuffer CreateFramebuffer(ImageView imageView) {
        FramebufferCreateInfo createInfo = new() {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = _renderPass,
            AttachmentCount = 1,
            PAttachments = &imageView,
            Width = _extent.Width,
            Height = _extent.Height,
            Layers = 1,
        };
        Result result = _vulkan.CreateFramebuffer(_device, &createInfo, null, out Framebuffer framebuffer);
        ThrowIfFailed(result, "vkCreateFramebuffer");
        return framebuffer;
    }

    private void DestroyFramebuffers() {
        foreach (Framebuffer framebuffer in _framebuffers) {
            if (framebuffer.Handle != 0) {
                _vulkan.DestroyFramebuffer(_device, framebuffer, null);
            }
        }

        _framebuffers = [];
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
