using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// The 16-byte push-constant block the store-back fragment shader reads: the normalized post-target
/// sub-rectangle the camera viewport covers.
/// </summary>
/// <remarks>
/// The chain is target-sized while the store-back covers only the camera viewport, so the block maps the
/// viewport's normalized <c>vUv</c> to the matching post-target texels: <see cref="UvRect"/> x and y are
/// the origin and z and w the size.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct PostStoreBackPushConstants {
    /// <summary>The normalized post-target origin in x/y and size in z/w.</summary>
    public Vector4 UvRect;
}

/// <summary>
/// The engine store-back pass for the camera post-process chain: a fullscreen triangle with one
/// sampler that copies the final single-sample Rgba16Float post target into a camera target's color
/// attachment and resolves it.
/// </summary>
/// <remarks>
/// <para>
/// The pipeline is built against the target's color format and sample count, so a multisampled target
/// uses a color-only render pass with a resolve attachment and a single-sample target attaches its
/// resolved color image directly. The depth attachment is never part of the pass. The descriptor set
/// layout, pipeline layout, sampler, and shader modules are shared; the render passes and pipelines are
/// cached per <c>(color format, sample count)</c> and destroyed with this object on backend shutdown or
/// device recreation.
/// </para>
/// <para>
/// Each post target owns one descriptor set, allocated from its own small pool and written with the
/// target's view and the shared sampler. Render-thread only; every member assumes the device is idle
/// when the pipeline or render pass is created or destroyed.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanPostStoreBackPipeline : IDisposable {
    private readonly Vk _vulkan;
    private readonly Device _device;
    private readonly DescriptorSetLayout _descriptorSetLayout;
    private readonly PipelineLayout _pipelineLayout;
    private readonly Sampler _sampler;
    private readonly ShaderModule _vertexModule;
    private readonly ShaderModule _fragmentModule;
    private readonly Dictionary<StoreBackKey, RenderPass> _renderPasses = [];
    private readonly Dictionary<StoreBackKey, Pipeline> _pipelines = [];
    private bool _disposed;

    private VulkanPostStoreBackPipeline(
        Vk vulkan,
        Device device,
        DescriptorSetLayout descriptorSetLayout,
        PipelineLayout pipelineLayout,
        Sampler sampler,
        ShaderModule vertexModule,
        ShaderModule fragmentModule) {
        _vulkan = vulkan;
        _device = device;
        _descriptorSetLayout = descriptorSetLayout;
        _pipelineLayout = pipelineLayout;
        _sampler = sampler;
        _vertexModule = vertexModule;
        _fragmentModule = fragmentModule;
    }

    /// <summary>Gets the sampler every post target's store-back descriptor uses.</summary>
    internal Sampler Sampler => _sampler;

    /// <summary>
    /// Creates the descriptor state, sampler, shader modules, and the empty render-pass and pipeline
    /// caches.
    /// </summary>
    /// <param name="device">The device that owns the pipeline.</param>
    /// <returns>The created store-back pipeline set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is <c>null</c>.</exception>
    /// <exception cref="GraphicsException">
    /// A Vulkan object could not be created. The diagnostic code is <c>RB3001</c>.
    /// </exception>
    internal static VulkanPostStoreBackPipeline Create(VulkanDevice device) {
        ArgumentNullException.ThrowIfNull(device);

        Vk vulkan = device.Vulkan;
        DescriptorSetLayout setLayout = default;
        PipelineLayout pipelineLayout = default;
        Sampler sampler = default;
        ShaderModule vertexModule = default;
        ShaderModule fragmentModule = default;
        try {
            setLayout = CreateDescriptorSetLayout(vulkan, device.Device);
            pipelineLayout = CreatePipelineLayout(vulkan, device.Device, setLayout);
            sampler = CreateSampler(vulkan, device.Device);
            vertexModule = CreateShaderModule(vulkan, device.Device, ShaderAssets.PostStoreVertex, "PostStore.vert.spv");
            fragmentModule = CreateShaderModule(vulkan, device.Device, ShaderAssets.PostStoreFragment, "PostStore.frag.spv");
            return new VulkanPostStoreBackPipeline(
                vulkan,
                device.Device,
                setLayout,
                pipelineLayout,
                sampler,
                vertexModule,
                fragmentModule);
        } catch {
            if (fragmentModule.Handle != 0) {
                vulkan.DestroyShaderModule(device.Device, fragmentModule, null);
            }

            if (vertexModule.Handle != 0) {
                vulkan.DestroyShaderModule(device.Device, vertexModule, null);
            }

            if (sampler.Handle != 0) {
                vulkan.DestroySampler(device.Device, sampler, null);
            }

            if (pipelineLayout.Handle != 0) {
                vulkan.DestroyPipelineLayout(device.Device, pipelineLayout, null);
            }

            if (setLayout.Handle != 0) {
                vulkan.DestroyDescriptorSetLayout(device.Device, setLayout, null);
            }

            throw;
        }
    }

    /// <summary>
    /// Allocates one descriptor set for a post target and writes its image view and the shared sampler.
    /// </summary>
    /// <param name="view">The post target's single-sample color view.</param>
    /// <param name="pool">The descriptor pool that owns the set; the caller destroys it with the target.</param>
    /// <returns>The descriptor set the store-back draw binds.</returns>
    /// <exception cref="ArgumentException"><paramref name="view"/> is the default handle.</exception>
    /// <exception cref="GraphicsException">The pool or set could not be created.</exception>
    internal DescriptorSet CreateDescriptorSet(ImageView view, out DescriptorPool pool) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (view.Handle == 0) {
            throw new ArgumentException("The store-back source view must not be the default handle.", nameof(view));
        }

        DescriptorPoolSize poolSize = new() {
            Type = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
        };
        DescriptorPoolCreateInfo poolInfo = new() {
            SType = StructureType.DescriptorPoolCreateInfo,
            MaxSets = 1,
            PoolSizeCount = 1,
            PPoolSizes = &poolSize,
        };
        Result poolResult = _vulkan.CreateDescriptorPool(_device, &poolInfo, null, out pool);
        ThrowIfFailed(poolResult, "vkCreateDescriptorPool for the post-process store-back pass");

        DescriptorSetLayout layout = _descriptorSetLayout;
        DescriptorSetAllocateInfo allocateInfo = new() {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = pool,
            DescriptorSetCount = 1,
            PSetLayouts = &layout,
        };
        Result setResult = _vulkan.AllocateDescriptorSets(_device, &allocateInfo, out DescriptorSet descriptorSet);
        if (setResult != Result.Success || descriptorSet.Handle == 0) {
            _vulkan.DestroyDescriptorPool(_device, pool, null);
            throw new GraphicsException(
                $"The post-process store-back descriptor set could not be allocated: {setResult}.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null);
        }

        DescriptorImageInfo imageInfo = new() {
            Sampler = _sampler,
            ImageView = view,
            ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
        };
        WriteDescriptorSet write = new() {
            SType = StructureType.WriteDescriptorSet,
            DstSet = descriptorSet,
            DstBinding = 0,
            DstArrayElement = 0,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.CombinedImageSampler,
            PImageInfo = &imageInfo,
        };
        _vulkan.UpdateDescriptorSets(_device, 1, &write, 0, null);
        return descriptorSet;
    }

    /// <summary>
    /// Gets or creates the color-only render pass for a target format and sample count.
    /// </summary>
    /// <param name="format">The target's color attachment format.</param>
    /// <param name="sampleCount">The target's effective sample count.</param>
    /// <returns>The cached render pass.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sampleCount"/> is not a supported count.</exception>
    /// <exception cref="GraphicsException">The render pass could not be created.</exception>
    internal RenderPass GetOrCreateRenderPass(Format format, int sampleCount) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        StoreBackKey key = new(format, sampleCount);
        if (_renderPasses.TryGetValue(key, out RenderPass cached)) {
            return cached;
        }

        RenderPass renderPass = CreateRenderPass(format, sampleCount);
        _renderPasses.Add(key, renderPass);
        return renderPass;
    }

    /// <summary>
    /// Gets or creates the store-back pipeline for a target format and sample count.
    /// </summary>
    /// <param name="format">The target's color attachment format.</param>
    /// <param name="sampleCount">The target's effective sample count.</param>
    /// <returns>The cached pipeline.</returns>
    /// <exception cref="GraphicsException">The pipeline could not be created.</exception>
    internal Pipeline GetOrCreatePipeline(Format format, int sampleCount) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        StoreBackKey key = new(format, sampleCount);
        if (_pipelines.TryGetValue(key, out Pipeline cached)) {
            return cached;
        }

        RenderPass renderPass = GetOrCreateRenderPass(format, sampleCount);
        Pipeline pipeline = CreatePipeline(renderPass, format, sampleCount);
        _pipelines.Add(key, pipeline);
        return pipeline;
    }

    /// <summary>
    /// Records the store-back draw for one camera viewport.
    /// </summary>
    /// <param name="vulkan">The Vulkan functions.</param>
    /// <param name="commandBuffer">The command buffer recording the frame.</param>
    /// <param name="renderPass">The target's store-back render pass.</param>
    /// <param name="framebuffer">The target's store-back framebuffer.</param>
    /// <param name="pipeline">The store-back pipeline for the target format and sample count.</param>
    /// <param name="sourceSet">The final post target's descriptor set.</param>
    /// <param name="renderArea">The camera viewport rectangle.</param>
    /// <param name="uvRect">
    /// The normalized post-target sub-rectangle the camera viewport covers: origin in x/y and size in
    /// z/w.
    /// </param>
    internal void Record(
        Vk vulkan,
        CommandBuffer commandBuffer,
        RenderPass renderPass,
        Framebuffer framebuffer,
        Pipeline pipeline,
        DescriptorSet sourceSet,
        Rect2D renderArea,
        Vector4 uvRect) {
        Viewport viewport = new() {
            X = renderArea.Offset.X,
            Y = renderArea.Offset.Y,
            Width = renderArea.Extent.Width,
            Height = renderArea.Extent.Height,
            MinDepth = VulkanDepthState.MIN_DEPTH,
            MaxDepth = VulkanDepthState.MAX_DEPTH,
        };
        RenderPassBeginInfo beginInfo = new() {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = renderPass,
            Framebuffer = framebuffer,
            RenderArea = renderArea,
            ClearValueCount = 0,
            PClearValues = null,
        };
        DescriptorSet descriptorSet = sourceSet;
        vulkan.CmdBeginRenderPass(commandBuffer, &beginInfo, SubpassContents.Inline);
        vulkan.CmdSetViewport(commandBuffer, 0, 1, &viewport);
        vulkan.CmdSetScissor(commandBuffer, 0, 1, &renderArea);
        vulkan.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, pipeline);
        vulkan.CmdBindDescriptorSets(
            commandBuffer,
            PipelineBindPoint.Graphics,
            _pipelineLayout,
            0,
            1,
            &descriptorSet,
            0,
            null);
        PostStoreBackPushConstants constants = new() { UvRect = uvRect };
        vulkan.CmdPushConstants(
            commandBuffer,
            _pipelineLayout,
            ShaderStageFlags.FragmentBit,
            0,
            (uint)sizeof(PostStoreBackPushConstants),
            &constants);
        vulkan.CmdDraw(commandBuffer, 3, 1, 0, 0);
        vulkan.CmdEndRenderPass(commandBuffer);
    }

    /// <summary>
    /// Destroys every cached pipeline, render pass, and the shared state. The call is idempotent and
    /// must run before the device is destroyed.
    /// </summary>
    public void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;
        foreach (Pipeline pipeline in _pipelines.Values) {
            if (pipeline.Handle != 0) {
                _vulkan.DestroyPipeline(_device, pipeline, null);
            }
        }

        _pipelines.Clear();
        foreach (RenderPass renderPass in _renderPasses.Values) {
            if (renderPass.Handle != 0) {
                _vulkan.DestroyRenderPass(_device, renderPass, null);
            }
        }

        _renderPasses.Clear();
        if (_fragmentModule.Handle != 0) {
            _vulkan.DestroyShaderModule(_device, _fragmentModule, null);
        }

        if (_vertexModule.Handle != 0) {
            _vulkan.DestroyShaderModule(_device, _vertexModule, null);
        }

        if (_sampler.Handle != 0) {
            _vulkan.DestroySampler(_device, _sampler, null);
        }

        if (_pipelineLayout.Handle != 0) {
            _vulkan.DestroyPipelineLayout(_device, _pipelineLayout, null);
        }

        if (_descriptorSetLayout.Handle != 0) {
            _vulkan.DestroyDescriptorSetLayout(_device, _descriptorSetLayout, null);
        }
    }

    private RenderPass CreateRenderPass(Format format, int sampleCount) {
        bool resolves = sampleCount > 1;
        Span<AttachmentDescription> attachments = stackalloc AttachmentDescription[resolves ? 2 : 1];
        attachments[0] = new AttachmentDescription {
            Format = format,
            Samples = ToSampleCountFlags(sampleCount),
            LoadOp = AttachmentLoadOp.Load,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.ColorAttachmentOptimal,
            FinalLayout = ImageLayout.ColorAttachmentOptimal,
        };
        if (resolves) {
            attachments[1] = new AttachmentDescription {
                Format = format,
                Samples = SampleCountFlags.Count1Bit,
                LoadOp = AttachmentLoadOp.DontCare,
                StoreOp = AttachmentStoreOp.Store,
                StencilLoadOp = AttachmentLoadOp.DontCare,
                StencilStoreOp = AttachmentStoreOp.DontCare,
                InitialLayout = ImageLayout.ColorAttachmentOptimal,
                FinalLayout = ImageLayout.ColorAttachmentOptimal,
            };
        }

        AttachmentReference colorReference = new() {
            Attachment = 0,
            Layout = ImageLayout.ColorAttachmentOptimal,
        };
        AttachmentReference resolveReference = new() {
            Attachment = 1,
            Layout = ImageLayout.ColorAttachmentOptimal,
        };
        SubpassDescription subpass = new() {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorReference,
            PResolveAttachments = resolves ? &resolveReference : null,
            PDepthStencilAttachment = null,
        };

        // The pass loads the camera pass' color and its fragment shader samples the final post target.
        // An external dependency orders both earlier writes and reads before the pass begins.
        SubpassDependency dependency = new() {
            SrcSubpass = uint.MaxValue,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.FragmentShaderBit,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.ShaderReadBit,
            DstAccessMask = AccessFlags.ColorAttachmentReadBit | AccessFlags.ColorAttachmentWriteBit,
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
            ThrowIfFailed(result, "vkCreateRenderPass for the post-process store-back pass");
            return renderPass;
        }
    }

    private Pipeline CreatePipeline(RenderPass renderPass, Format format, int sampleCount) {
        byte[] vertexEntryPoint = Encoding.UTF8.GetBytes(ShaderAssets.POST_STORE_VERTEX_ENTRY_POINT + "\0");
        byte[] fragmentEntryPoint = Encoding.UTF8.GetBytes(ShaderAssets.POST_STORE_FRAGMENT_ENTRY_POINT + "\0");
        PipelineShaderStageCreateInfo* stages = stackalloc PipelineShaderStageCreateInfo[2];
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
            RasterizationSamples = ToSampleCountFlags(sampleCount),
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
            RenderPass = renderPass,
            Subpass = 0,
            BasePipelineHandle = default,
            BasePipelineIndex = -1,
        };
        Result result;
        Pipeline pipeline;
        fixed (byte* vertexName = vertexEntryPoint)
        fixed (byte* fragmentName = fragmentEntryPoint) {
            stages[0] = new() {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.VertexBit,
                Module = _vertexModule,
                PName = vertexName,
            };
            stages[1] = new() {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit,
                Module = _fragmentModule,
                PName = fragmentName,
            };
            result = _vulkan.CreateGraphicsPipelines(
                _device,
                default,
                1,
                &createInfo,
                null,
                out pipeline);
        }

        if (result != Result.Success || pipeline.Handle == 0) {
            throw new GraphicsException(
                $"The post-process store-back pipeline for color format {VulkanFormatInfo.Describe(format)} "
                    + $"and {sampleCount} sample(s) could not be created: {result}.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null);
        }

        return pipeline;
    }

    private static DescriptorSetLayout CreateDescriptorSetLayout(Vk vulkan, Device device) {
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
        Result result = vulkan.CreateDescriptorSetLayout(device, &createInfo, null, out DescriptorSetLayout layout);
        ThrowIfFailed(result, "vkCreateDescriptorSetLayout for the post-process store-back pass");
        return layout;
    }

    private static PipelineLayout CreatePipelineLayout(
        Vk vulkan,
        Device device,
        DescriptorSetLayout setLayout) {
        PushConstantRange uvRectRange = new() {
            StageFlags = ShaderStageFlags.FragmentBit,
            Offset = 0,
            Size = (uint)sizeof(PostStoreBackPushConstants),
        };
        PipelineLayoutCreateInfo createInfo = new() {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &setLayout,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &uvRectRange,
        };
        Result result = vulkan.CreatePipelineLayout(device, &createInfo, null, out PipelineLayout layout);
        ThrowIfFailed(result, "vkCreatePipelineLayout for the post-process store-back pass");
        return layout;
    }

    private static Sampler CreateSampler(Vk vulkan, Device device) {
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
        Result result = vulkan.CreateSampler(device, &createInfo, null, out Sampler sampler);
        ThrowIfFailed(result, "vkCreateSampler for the post-process store-back pass");
        return sampler;
    }

    private static ShaderModule CreateShaderModule(
        Vk vulkan,
        Device device,
        ReadOnlySpan<byte> code,
        string fileName) {
        fixed (byte* codePointer = code) {
            ShaderModuleCreateInfo createInfo = new() {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (UIntPtr)(uint)code.Length,
                PCode = (uint*)codePointer,
            };
            Result result = vulkan.CreateShaderModule(device, &createInfo, null, out ShaderModule module);
            if (result != Result.Success || module.Handle == 0) {
                throw new GraphicsException(
                    $"The post-process store-back shader module '{fileName}' could not be created: {result}.",
                    DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                    null);
            }

            return module;
        }
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

    /// <summary>The render-pass and pipeline cache key: the target color format and sample count.</summary>
    private readonly record struct StoreBackKey(Format Format, int SampleCount);
}
