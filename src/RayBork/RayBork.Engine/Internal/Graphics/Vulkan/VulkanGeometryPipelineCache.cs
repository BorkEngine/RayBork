using System.Text;
using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// Creates and caches the geometry pipelines of one scene render pass.
/// </summary>
/// <remarks>
/// <para>
/// A pipeline is keyed by the shader GPU resource, the effective <see cref="RenderState"/>, and the
/// mesh topology. Every pipeline shares the one geometry pipeline layout and the fixed 88-byte vertex
/// input layout, and is created against the current scene render pass and its sample count, so the
/// cache is discarded when the scene target, render pass, or logical device is recreated.
/// </para>
/// <para>
/// The cache owns every pipeline it creates. A creation failure is a fatal <c>RB3001</c> error that
/// names the shader, render state, topology, and sample count; the caller drops the frame instead of
/// presenting a partial one. Every member is render-thread only.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanGeometryPipelineCache : IDisposable {
    private readonly Vk _vulkan;
    private readonly Device _device;
    private readonly RenderPass _renderPass;
    private readonly PipelineLayout _pipelineLayout;
    private readonly int _sampleCount;
    private readonly int _deviceGeneration;
    private readonly Dictionary<PipelineKey, Pipeline> _pipelines = [];
    private bool _disposed;

    private VulkanGeometryPipelineCache(
        Vk vulkan,
        Device device,
        RenderPass renderPass,
        PipelineLayout pipelineLayout,
        int sampleCount,
        int deviceGeneration) {
        _vulkan = vulkan;
        _device = device;
        _renderPass = renderPass;
        _pipelineLayout = pipelineLayout;
        _sampleCount = sampleCount;
        _deviceGeneration = deviceGeneration;
    }

    /// <summary>Gets the number of cached pipelines. Diagnostic, for tests and statistics.</summary>
    internal int Count => _pipelines.Count;

    /// <summary>
    /// Creates a cache for one scene render pass and sample count.
    /// </summary>
    /// <param name="device">The device that owns the pipelines.</param>
    /// <param name="renderPass">The scene render pass the pipelines render into.</param>
    /// <param name="pipelineLayout">The shared geometry pipeline layout.</param>
    /// <param name="sampleCount">The scene target's sample count.</param>
    /// <param name="deviceGeneration">The logical-device generation that owns the shader modules.</param>
    /// <returns>The created cache.</returns>
    internal static VulkanGeometryPipelineCache Create(
        VulkanDevice device,
        RenderPass renderPass,
        PipelineLayout pipelineLayout,
        int sampleCount,
        int deviceGeneration) {
        ArgumentNullException.ThrowIfNull(device);
        if (renderPass.Handle == 0) {
            throw new ArgumentException("The scene render pass must not be the default handle.", nameof(renderPass));
        }

        if (pipelineLayout.Handle == 0) {
            throw new ArgumentException("The pipeline layout must not be the default handle.", nameof(pipelineLayout));
        }

        if (sampleCount <= 0) {
            throw new ArgumentOutOfRangeException(nameof(sampleCount), sampleCount, "The sample count must be positive.");
        }

        return new VulkanGeometryPipelineCache(
            device.Vulkan,
            device.Device,
            renderPass,
            pipelineLayout,
            sampleCount,
            deviceGeneration);
    }

    /// <summary>
    /// Gets the pipeline for a shader, render state, and topology, creating it on first use.
    /// </summary>
    /// <param name="shader">The shader GPU resource the pipeline uses.</param>
    /// <param name="renderState">The effective render state.</param>
    /// <param name="topology">The mesh topology.</param>
    /// <returns>The cached pipeline.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="shader"/> is <c>null</c>.</exception>
    /// <exception cref="GraphicsException">
    /// The shader resource belongs to a destroyed device, its upload is not a shader upload, or pipeline
    /// creation failed. The diagnostic code is <c>RB3001</c>.
    /// </exception>
    internal Pipeline GetOrCreate(
        VulkanShaderAssetResource shader,
        RenderState renderState,
        RayBork.PrimitiveTopology topology) {
        ArgumentNullException.ThrowIfNull(shader);
        ObjectDisposedException.ThrowIf(_disposed, this);
        PipelineKey key = new(shader, renderState, topology);
        if (_pipelines.TryGetValue(key, out Pipeline cached)) {
            return cached;
        }

        if (shader.DeviceGeneration != _deviceGeneration) {
            throw new GraphicsException(
                $"The shader '{AssetPath.ToPublic(shader.Upload.Path)}' belongs to a destroyed Vulkan "
                    + "device and cannot create a pipeline on the current one.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null);
        }

        Pipeline pipeline = CreatePipeline(shader, renderState, topology);
        _pipelines.Add(key, pipeline);
        return pipeline;
    }

    /// <summary>
    /// Destroys every cached pipeline. The call is idempotent and must run before the scene render pass
    /// and the logical device are destroyed.
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
    }

    private Pipeline CreatePipeline(
        VulkanShaderAssetResource shader,
        RenderState renderState,
        RayBork.PrimitiveTopology topology) {
        if (shader.Upload is not ShaderGpuUpload upload) {
            throw new GraphicsException(
                $"The shader GPU resource '{AssetPath.ToPublic(shader.Upload.Path)}' does not carry a "
                    + "shader upload descriptor.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null);
        }

        ShaderAssetDescriptor descriptor = upload.Shader;
        byte[] vertexEntryPoint = Encoding.UTF8.GetBytes(descriptor.VertexEntry + "\0");
        byte[] fragmentEntryPoint = Encoding.UTF8.GetBytes(descriptor.FragmentEntry + "\0");
        PipelineShaderStageCreateInfo* stages = stackalloc PipelineShaderStageCreateInfo[2];
        VertexInputBindingDescription* vertexBindings = stackalloc VertexInputBindingDescription[1];
        vertexBindings[0] = new VertexInputBindingDescription {
            Binding = 0,
            Stride = MeshAssetDescriptor.VERTEX_STRIDE,
            InputRate = VertexInputRate.Vertex,
        };
        VertexInputAttributeDescription* attributes = stackalloc VertexInputAttributeDescription[8];
        attributes[0] = Attribute(0, Format.R32G32B32Sfloat, 0);
        attributes[1] = Attribute(1, Format.R32G32B32Sfloat, 12);
        attributes[2] = Attribute(2, Format.R32G32Sfloat, 24);
        attributes[3] = Attribute(3, Format.R32G32Sfloat, 32);
        attributes[4] = Attribute(4, Format.R32G32Sfloat, 40);
        attributes[5] = Attribute(5, Format.R32G32Sfloat, 48);
        attributes[6] = Attribute(6, Format.R32G32B32A32Sfloat, 56);
        attributes[7] = Attribute(7, Format.R32G32B32A32Sfloat, 72);
        PipelineVertexInputStateCreateInfo vertexInput = new() {
            SType = StructureType.PipelineVertexInputStateCreateInfo,
            VertexBindingDescriptionCount = 1,
            PVertexBindingDescriptions = vertexBindings,
            VertexAttributeDescriptionCount = 8,
            PVertexAttributeDescriptions = attributes,
        };
        PipelineInputAssemblyStateCreateInfo inputAssembly = new() {
            SType = StructureType.PipelineInputAssemblyStateCreateInfo,
            Topology = ToVulkanTopology(topology),
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
            CullMode = ToCullMode(renderState.Cull),
            FrontFace = FrontFace.CounterClockwise,
            DepthBiasEnable = false,
            DepthBiasConstantFactor = 0f,
            DepthBiasClamp = 0f,
            DepthBiasSlopeFactor = 0f,
            LineWidth = 1f,
        };
        PipelineMultisampleStateCreateInfo multisample = new() {
            SType = StructureType.PipelineMultisampleStateCreateInfo,
            RasterizationSamples = ToSampleCountFlag(_sampleCount),
            SampleShadingEnable = false,
            MinSampleShading = 0f,
            PSampleMask = null,
            AlphaToCoverageEnable = false,
            AlphaToOneEnable = false,
        };
        PipelineDepthStencilStateCreateInfo depthStencil = CreateDepthStencilState(renderState);
        PipelineColorBlendAttachmentState blendAttachment = CreateBlendAttachment(renderState.Blend);
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
            PDepthStencilState = &depthStencil,
            PColorBlendState = &colorBlend,
            PDynamicState = &dynamicState,
            Layout = _pipelineLayout,
            RenderPass = _renderPass,
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
                Module = shader.VertexModule,
                PName = vertexName,
            };
            stages[1] = new() {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit,
                Module = shader.FragmentModule,
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
                $"The geometry pipeline for shader '{AssetPath.ToPublic(shader.Upload.Path)}', "
                    + $"state (blend {renderState.Blend}, cull {renderState.Cull}, depth "
                    + $"{renderState.DepthTest}, write {renderState.DepthWrite}), topology {topology}, "
                    + $"and {_sampleCount} sample(s) could not be created: {result}.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null);
        }

        return pipeline;
    }

    private static VertexInputAttributeDescription Attribute(uint location, Format format, uint offset) => new() {
        Location = location,
        Binding = 0,
        Format = format,
        Offset = offset,
    };

    private static PipelineDepthStencilStateCreateInfo CreateDepthStencilState(RenderState renderState) {
        bool depthTest = renderState.DepthTest != DepthTest.Disabled;
        return new PipelineDepthStencilStateCreateInfo {
            SType = StructureType.PipelineDepthStencilStateCreateInfo,
            DepthTestEnable = depthTest,
            DepthWriteEnable = depthTest && renderState.DepthWrite,
            DepthCompareOp = ToCompareOp(renderState.DepthTest),
            DepthBoundsTestEnable = false,
            StencilTestEnable = false,
            Front = default,
            Back = default,
            MinDepthBounds = VulkanDepthState.MIN_DEPTH,
            MaxDepthBounds = VulkanDepthState.MAX_DEPTH,
        };
    }

    private static PipelineColorBlendAttachmentState CreateBlendAttachment(BlendMode blendMode) {
        PipelineColorBlendAttachmentState attachment = new() {
            BlendEnable = blendMode != BlendMode.Opaque,
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
        switch (blendMode) {
            case BlendMode.Opaque:
                break;
            case BlendMode.Alpha:
                attachment.SrcColorBlendFactor = BlendFactor.SrcAlpha;
                attachment.DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha;
                attachment.SrcAlphaBlendFactor = BlendFactor.One;
                attachment.DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha;
                break;
            case BlendMode.Additive:
                attachment.SrcColorBlendFactor = BlendFactor.SrcAlpha;
                attachment.DstColorBlendFactor = BlendFactor.One;
                attachment.SrcAlphaBlendFactor = BlendFactor.One;
                attachment.DstAlphaBlendFactor = BlendFactor.One;
                break;
            case BlendMode.Multiply:
                attachment.SrcColorBlendFactor = BlendFactor.DstColor;
                attachment.DstColorBlendFactor = BlendFactor.Zero;
                attachment.SrcAlphaBlendFactor = BlendFactor.DstAlpha;
                attachment.DstAlphaBlendFactor = BlendFactor.Zero;
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(blendMode),
                    blendMode,
                    "The blend mode is not defined by the M4 render state.");
        }

        return attachment;
    }

    private static CullModeFlags ToCullMode(CullMode cullMode) => cullMode switch {
        CullMode.None => CullModeFlags.None,
        CullMode.Front => CullModeFlags.FrontBit,
        CullMode.Back => CullModeFlags.BackBit,
        _ => throw new ArgumentOutOfRangeException(
            nameof(cullMode),
            cullMode,
            "The cull mode is not defined by the M4 render state."),
    };

    private static CompareOp ToCompareOp(DepthTest depthTest) => depthTest switch {
        DepthTest.Disabled => CompareOp.Always,
        DepthTest.Greater => CompareOp.Greater,
        DepthTest.GreaterOrEqual => CompareOp.GreaterOrEqual,
        DepthTest.Less => CompareOp.Less,
        DepthTest.LessOrEqual => CompareOp.LessOrEqual,
        DepthTest.Always => CompareOp.Always,
        _ => throw new ArgumentOutOfRangeException(
            nameof(depthTest),
            depthTest,
            "The depth test is not defined by the M4 render state."),
    };

    private static Silk.NET.Vulkan.PrimitiveTopology ToVulkanTopology(RayBork.PrimitiveTopology topology) =>
        topology switch {
            RayBork.PrimitiveTopology.TriangleList => Silk.NET.Vulkan.PrimitiveTopology.TriangleList,
            RayBork.PrimitiveTopology.TriangleStrip => Silk.NET.Vulkan.PrimitiveTopology.TriangleStrip,
            RayBork.PrimitiveTopology.LineList => Silk.NET.Vulkan.PrimitiveTopology.LineList,
            RayBork.PrimitiveTopology.LineStrip => Silk.NET.Vulkan.PrimitiveTopology.LineStrip,
            RayBork.PrimitiveTopology.PointList => Silk.NET.Vulkan.PrimitiveTopology.PointList,
            _ => throw new ArgumentOutOfRangeException(
                nameof(topology),
                topology,
                "The primitive topology is not defined for the M4 geometry path."),
        };

    private static SampleCountFlags ToSampleCountFlag(int sampleCount) => sampleCount switch {
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

    private readonly record struct PipelineKey(
        VulkanShaderAssetResource Shader,
        RenderState RenderState,
        RayBork.PrimitiveTopology Topology);
}
