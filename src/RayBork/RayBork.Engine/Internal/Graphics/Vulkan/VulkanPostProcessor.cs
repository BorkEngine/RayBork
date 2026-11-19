using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace RayBork.Internal;

/// <summary>Runs the camera post-process chain on the render thread and stores the result back.</summary>
/// <remarks>
/// Post targets are target-sized single-sample <c>Rgba16Float</c> images; effects run in order, then one
/// store-back pass writes the final target into the camera viewport's color attachment. Targets are pooled
/// per frame slot and extent so a resize recycles images a frame later, and the processor is recreated
/// after a device recovery.
/// </remarks>
internal sealed unsafe class VulkanPostProcessor : IDisposable
{
    private const Format POST_FORMAT = Format.R16G16B16A16Sfloat;
    private const int POST_SAMPLE_COUNT = 1;

    /// <summary>
    /// The forced render state of every custom post-process pipeline: opaque, no culling, no depth test.
    /// </summary>
    private static readonly RenderState _forcedRenderState = new()
    {
        Blend = BlendMode.Opaque,
        Cull = CullMode.None,
        DepthTest = DepthTest.Disabled,
        DepthWrite = false,
    };

    private readonly VulkanDevice _device;
    private readonly Vk _vulkan;
    private readonly VulkanBindlessTextureTable _bindless;
    private readonly VulkanFrameResources _frameResources;
    private readonly PipelineLayout _geometryPipelineLayout;
    private readonly int _deviceGeneration;
    private readonly PostTargetSlot[] _slots = new PostTargetSlot[
        VulkanFrameResources.FRAMES_IN_FLIGHT
    ];
    private readonly Dictionary<VulkanRenderTarget, TargetStoreBack> _storeBacks = [];

    private RenderPass _postRenderPass;
    private VulkanGeometryPipelineCache? _postPipelines;
    private VulkanPostStoreBackPipeline? _storeBack;
    private VulkanFullscreenQuad? _quad;
    private PostProcessContext? _context;
    private PostTargetSlot? _activeSlot;
    private bool _disposed;

    private VulkanPostProcessor(
        VulkanDevice device,
        VulkanBindlessTextureTable bindless,
        VulkanFrameResources frameResources,
        PipelineLayout geometryPipelineLayout,
        int deviceGeneration
    )
    {
        _device = device;
        _vulkan = device.Vulkan;
        _bindless = bindless;
        _frameResources = frameResources;
        _geometryPipelineLayout = geometryPipelineLayout;
        _deviceGeneration = deviceGeneration;
        for (int index = 0; index < _slots.Length; index++)
        {
            _slots[index] = new PostTargetSlot();
        }
    }

    /// <summary>Gets the number of post-target set creations, for GPU tests only.</summary>
    internal int TargetRecreationCount { get; private set; }

    /// <summary>Creates the post render pass, pipeline caches, fullscreen quad, and run context.</summary>
    internal static VulkanPostProcessor Create(
        VulkanDevice device,
        VulkanBindlessTextureTable bindless,
        VulkanFrameResources frameResources,
        PipelineLayout geometryPipelineLayout,
        int deviceGeneration
    )
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(bindless);
        ArgumentNullException.ThrowIfNull(frameResources);
        if (geometryPipelineLayout.Handle == 0)
        {
            throw new ArgumentException(
                "The geometry pipeline layout must not be the default handle.",
                nameof(geometryPipelineLayout)
            );
        }

        VulkanPostProcessor processor = new(
            device,
            bindless,
            frameResources,
            geometryPipelineLayout,
            deviceGeneration
        );
        try
        {
            processor.CreateResources();
            return processor;
        }
        catch
        {
            processor.Dispose();
            throw;
        }
    }

    /// <summary>Starts one frame for a ring slot and releases the previous frame's unused extents.</summary>
    /// <remarks>The slot's fence signaled, so no in-flight command buffer references the stale targets.</remarks>
    internal void BeginFrame(int frameSlot, long frameIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _slots[frameSlot].BeginFrame(frameIndex);
    }

    /// <summary>Runs the enabled effects of one camera pass and the store-back for its viewport.</summary>
    /// <exception cref="GraphicsException">
    /// A shader is not live, a post target cannot be created, or a depth source is missing.
    /// </exception>
    internal void Process(
        Vk vulkan,
        CommandBuffer commandBuffer,
        RenderFramePacket frame,
        CameraPass pass,
        int passIndex,
        int frameSlot,
        VulkanRenderTarget target,
        uint targetColorSlot,
        uint targetDepthSlot,
        Rect2D renderArea,
        ref ulong instanceByteOffset
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(target);
        if (!pass.HasEnabledPostProcessEffects)
        {
            return;
        }

        PostTargetSlot slot = _slots[frameSlot];
        _activeSlot = slot;
        try
        {
            PostProcessContext context = _context!;
            context.Reset(
                vulkan,
                commandBuffer,
                frame,
                pass,
                passIndex,
                frameSlot,
                target,
                target.Extent,
                renderArea,
                targetColorSlot,
                targetDepthSlot,
                instanceByteOffset
            );
            foreach (PostProcessEffectSnapshot effect in pass.PostProcessEffects)
            {
                if (effect.Enabled)
                {
                    effect.Run(context);
                }
            }

            // A custom effect always completes a target, so a chain with enabled effects has a final
            // result. A future built-in effect that decides to no-op leaves the target content unchanged.
            if (context.CurrentColorTarget is not null)
            {
                context.RecordStoreBack();
            }

            instanceByteOffset = context.InstanceByteOffset;
        }
        finally
        {
            _activeSlot = null;
        }
    }

    /// <summary>Destroys a camera target's cached store-back framebuffer before its images are replaced.</summary>
    /// <remarks>Safe for a target without a cached store-back.</remarks>
    internal void DestroyStoreBack(VulkanRenderTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!_storeBacks.Remove(target, out TargetStoreBack storeBack))
        {
            return;
        }

        if (storeBack.Framebuffer.Handle != 0)
        {
            _vulkan.DestroyFramebuffer(_device.Device, storeBack.Framebuffer, null);
        }
    }

    /// <summary>Gets the Vulkan device that owns the post render pass, pipelines, and targets.</summary>
    internal VulkanDevice Device => _device;

    /// <summary>Gets the color-only single-sample render pass every post target is built against.</summary>
    internal RenderPass PostRenderPass => _postRenderPass;

    /// <summary>Gets the engine pipeline layout a fullscreen post pipeline must use.</summary>
    internal PipelineLayout PipelineLayout => _geometryPipelineLayout;

    /// <summary>Gets the forced render state a post-process pipeline uses; the pass has no depth.</summary>
    internal static RenderState FullscreenRenderState => _forcedRenderState;

    /// <summary>Gets the frame descriptor set binding the frame block and instance records.</summary>
    /// <returns>The descriptor set to bind at set 0.</returns>
    internal DescriptorSet GetFrameDescriptorSet(int frameSlot) =>
        _frameResources.GetDescriptorSet(frameSlot);

    /// <summary>Gets a writable span of the frame slot's instance-record storage.</summary>
    internal Span<byte> GetInstanceRecordSpan(int frameSlot, ulong byteOffset, int stride) =>
        _frameResources.GetInstanceSpan(frameSlot, byteOffset, (ulong)stride);

    /// <summary>Gets or creates a fullscreen pipeline for a baked shader against the post render pass.</summary>
    /// <remarks>Only <see cref="FullscreenRenderState"/> is valid; the pass has no depth.</remarks>
    internal Pipeline GetOrCreateFullscreenPipeline(EngineAsset program, RenderState renderState)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(program);
        if (renderState != _forcedRenderState)
        {
            throw new ArgumentException(
                "A post-process fullscreen pipeline must use the forced post render state: opaque "
                    + "blending, no culling, and no depth test or write. The post render pass has no "
                    + "depth attachment, so a depth-tested, culled, or blended state is invalid.",
                nameof(renderState)
            );
        }

        VulkanShaderAssetResource shader = Vulkan12Backend.RequireShaderResource(program);
        return _postPipelines!.GetOrCreate(
            shader,
            renderState,
            RayBork.PrimitiveTopology.TriangleList
        );
    }

    /// <summary>Rents a camera-target-sized post target and transitions it for writing.</summary>
    internal VulkanPostTarget RentColorTarget(PostProcessContext context) =>
        RentColorTarget(context, context.Extent);

    /// <summary>Rents a post target of any extent and transitions it for writing.</summary>
    /// <remarks>
    /// The rent skips the current chain source so a prior effect's output survives; release an
    /// intermediate target with <see cref="PostProcessContext.CompleteAuxiliaryTarget"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The extent has a zero dimension or exceeds the device framebuffer limit.
    /// </exception>
    /// <exception cref="InvalidOperationException">No post chain is active.</exception>
    internal VulkanPostTarget RentColorTarget(PostProcessContext context, Extent2D extent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(context);
        if (extent.Width == 0 || extent.Height == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(extent),
                extent,
                "The post target extent must be positive."
            );
        }

        if (
            extent.Width > _device.Capabilities.MaxFramebufferWidth
            || extent.Height > _device.Capabilities.MaxFramebufferHeight
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(extent),
                extent,
                $"The post target extent {extent.Width}x{extent.Height} exceeds the device's maximum "
                    + $"framebuffer size {_device.Capabilities.MaxFramebufferWidth}x"
                    + $"{_device.Capabilities.MaxFramebufferHeight}."
            );
        }

        PostTargetSlot slot = _activeSlot
            ?? throw new InvalidOperationException(
                "A post-process target can be rented only while the renderer runs a camera pass' effect chain."
            );
        PostTargetSet set = GetTargetSet(slot, extent, context.Frame.FrameIndex);
        VulkanPostTarget target = set.Rent(context.CurrentColorTarget);
        target.TransitionToColorAttachment(context.Vulkan, context.CommandBuffer);
        return target;
    }

    /// <summary>Transitions a camera-target-sized target to a shader read and makes it the chain source.</summary>
    /// <exception cref="InvalidOperationException">The target is not target-sized.</exception>
    internal void CompleteColorTarget(PostProcessContext context, VulkanPostTarget target)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(target);
        if (
            target.Extent.Width != context.Extent.Width
            || target.Extent.Height != context.Extent.Height
        )
        {
            throw new InvalidOperationException(
                $"The post-process chain completed a {target.Extent.Width}x{target.Extent.Height} target, "
                    + $"but the camera target is {context.Extent.Width}x{context.Extent.Height}. A chain's "
                    + "final target must be camera-target-sized; complete an intermediate result with "
                    + "CompleteAuxiliaryTarget and upscale it into a target-sized target."
            );
        }

        target.TransitionToShaderRead(context.Vulkan, context.CommandBuffer);
        context.SetCurrentColorTarget(target);
    }

    /// <summary>Transitions an auxiliary target to a shader read without making it the chain source.</summary>
    internal void CompleteAuxiliaryTarget(PostProcessContext context, VulkanPostTarget target)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(target);
        target.TransitionToShaderRead(context.Vulkan, context.CommandBuffer);
    }

    /// <summary>Records one fullscreen pass into a rented target with the engine fullscreen quad.</summary>
    /// <remarks>The caller reserves and fills the instance record and texture slots first.</remarks>
    /// <exception cref="ArgumentException">
    /// The pipeline is the default handle or the instance offset is not stride-aligned.
    /// </exception>
    internal void RecordFullscreenPass(
        PostProcessContext context,
        VulkanPostTarget destination,
        Pipeline pipeline,
        ulong instanceByteOffset,
        int stride
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(destination);
        if (pipeline.Handle == 0)
        {
            throw new ArgumentException(
                "The fullscreen pass pipeline must not be the default handle.",
                nameof(pipeline)
            );
        }

        if (stride <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stride),
                stride,
                "The instance record stride must be positive."
            );
        }

        if (instanceByteOffset % (ulong)stride != 0)
        {
            throw new ArgumentException(
                $"The instance byte offset {instanceByteOffset} is not aligned to the {stride}-byte record stride.",
                nameof(instanceByteOffset)
            );
        }

        if (_activeSlot is null)
        {
            throw new InvalidOperationException(
                "A fullscreen post-process pass can be recorded only while the renderer runs a camera pass' "
                    + "effect chain."
            );
        }

        Vk vulkan = context.Vulkan;
        CommandBuffer commandBuffer = context.CommandBuffer;
        Extent2D extent = destination.Extent;
        Rect2D renderArea = new()
        {
            Offset = new Offset2D { X = 0, Y = 0 },
            Extent = extent,
        };
        Viewport viewport = new()
        {
            X = 0f,
            Y = 0f,
            Width = extent.Width,
            Height = extent.Height,
            MinDepth = VulkanDepthState.MIN_DEPTH,
            MaxDepth = VulkanDepthState.MAX_DEPTH,
        };
        RenderPassBeginInfo beginInfo = new()
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _postRenderPass,
            Framebuffer = destination.Framebuffer,
            RenderArea = renderArea,
            ClearValueCount = 0,
            PClearValues = null,
        };
        DescriptorSet descriptorSet = _frameResources.GetDescriptorSet(context.FrameSlot);
        uint firstInstance = checked((uint)(instanceByteOffset / (ulong)stride));
        vulkan.CmdBeginRenderPass(commandBuffer, &beginInfo, SubpassContents.Inline);
        vulkan.CmdSetViewport(commandBuffer, 0, 1, &viewport);
        vulkan.CmdSetScissor(commandBuffer, 0, 1, &renderArea);
        vulkan.CmdBindDescriptorSets(
            commandBuffer,
            PipelineBindPoint.Graphics,
            _geometryPipelineLayout,
            0,
            1,
            &descriptorSet,
            0,
            null
        );
        vulkan.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, pipeline);
        Buffer vertexBuffer = _quad!.VertexBuffer;
        ulong vertexOffset = 0;
        vulkan.CmdBindVertexBuffers(commandBuffer, 0, 1, &vertexBuffer, &vertexOffset);
        vulkan.CmdBindIndexBuffer(commandBuffer, _quad.IndexBuffer, 0, IndexType.Uint32);
        PushPassIndex(vulkan, commandBuffer, context.PassIndex);
        vulkan.CmdDrawIndexed(
            commandBuffer,
            VulkanFullscreenQuad.IndexCount,
            1,
            0,
            0,
            firstInstance
        );
        vulkan.CmdEndRenderPass(commandBuffer);
    }

    /// <summary>Records one custom effect's fullscreen pass from the current chain source.</summary>
    /// <exception cref="GraphicsException">
    /// The shader is not live, the depth source is missing, or the instance record does not fit.
    /// </exception>
    internal void RecordCustomShaderPass(
        PostProcessContext context,
        VulkanPostTarget destination,
        ShaderPostProcessEffectSnapshot snapshot,
        EngineAsset program
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(program);

        Pipeline pipeline = GetOrCreateFullscreenPipeline(program, _forcedRenderState);
        int stride = snapshot.InstanceStride;
        ulong byteOffset = context.ReserveInstanceRecord(stride);
        Span<byte> record = context.GetInstanceRecordSpan(byteOffset, stride);
        VulkanInstanceRecordWriter.WriteMatrix(
            record,
            ShaderAbiLayout.TRANSFORM_OFFSET,
            Matrix4x4.Identity
        );
        VulkanInstanceRecordWriter.WriteVector4(record, ShaderAbiLayout.COLOR_OFFSET, Vector4.One);
        VulkanInstanceRecordWriter.WriteVector4(
            record,
            ShaderAbiLayout.UV_RECT_OFFSET,
            new Vector4(0f, 0f, 1f, 1f)
        );
        snapshot.InstanceData.CopyTo(record[ShaderAbiLayout.INSTANCE_HEADER_SIZE..]);

        ReadOnlySpan<Texture?> textures = snapshot.TextureValues;
        for (int index = 0; index < textures.Length; index++)
        {
            uint slot;
            if (index == snapshot.SourceColorBinding)
            {
                slot = context.SourceColorSlot;
            }
            else if (index == snapshot.SourceDepthBinding)
            {
                slot = context.SourceDepthSlot;
                if (slot == 0)
                {
                    throw new GraphicsException(
                        $"The post-process shader '{program.Path}' samples the reserved "
                            + $"'{PostProcessNames.SOURCE_DEPTH}' binding, but the camera target has no resolved "
                            + "depth image to bind to it.",
                        DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                        null
                    );
                }
            }
            else
            {
                slot = PostProcessContext.ResolveTextureSlot(textures[index]);
            }

            VulkanInstanceRecordWriter.WriteUInt32(
                record,
                ShaderAbiLayout.TEXTURE_INDEX_START + (index * ShaderAbiLayout.TEXTURE_INDEX_SIZE),
                slot
            );
        }

        RecordFullscreenPass(context, destination, pipeline, byteOffset, stride);
    }

    /// <summary>Records the store-back pass into the camera target for the camera viewport.</summary>
    /// <exception cref="GraphicsException">The store-back pipeline could not be created.</exception>
    internal void RecordStoreBack(PostProcessContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(context);
        VulkanPostTarget finalTarget = context.CurrentColorTarget
            ?? throw new InvalidOperationException(
                "The post-process chain has no completed target to store back."
            );
        VulkanRenderTarget target = context.Target;
        TargetStoreBack storeBack = GetStoreBack(target);
        Pipeline pipeline = _storeBack!.GetOrCreatePipeline(target.ColorFormat, target.SampleCount);

        target.PrepareStoreBack(context.Vulkan, context.CommandBuffer);
        Rect2D renderArea = context.RenderArea;
        Vector4 uvRect = new(
            renderArea.Offset.X / (float)context.Extent.Width,
            renderArea.Offset.Y / (float)context.Extent.Height,
            renderArea.Extent.Width / (float)context.Extent.Width,
            renderArea.Extent.Height / (float)context.Extent.Height
        );
        _storeBack.Record(
            context.Vulkan,
            context.CommandBuffer,
            storeBack.RenderPass,
            storeBack.Framebuffer,
            pipeline,
            finalTarget.StoreBackSet,
            renderArea,
            uvRect
        );
        target.CompleteStoreBack(context.Vulkan, context.CommandBuffer);
    }

    /// <summary>Resolves a material texture binding to its bindless slot.</summary>
    /// <param name="texture">The texture, or <see langword="null"/> for the white fallback.</param>
    internal static uint ResolveTextureSlot(Texture? texture) =>
        VulkanTextureBinding.ResolveSlot(texture);

    /// <summary>Destroys the cached store-backs, post targets, pipeline caches, quad, and render pass.</summary>
    /// <remarks>Idempotent; must run before the device, targets, and bindless table are destroyed.</remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (TargetStoreBack storeBack in _storeBacks.Values)
        {
            if (storeBack.Framebuffer.Handle != 0)
            {
                _vulkan.DestroyFramebuffer(_device.Device, storeBack.Framebuffer, null);
            }
        }

        _storeBacks.Clear();
        foreach (PostTargetSlot slot in _slots)
        {
            slot.DisposeTargets();
        }

        _quad?.Dispose();
        _quad = null;
        _postPipelines?.Dispose();
        _postPipelines = null;
        _storeBack?.Dispose();
        _storeBack = null;
        if (_postRenderPass.Handle != 0)
        {
            _vulkan.DestroyRenderPass(_device.Device, _postRenderPass, null);
            _postRenderPass = default;
        }

        _context = null;
        _activeSlot = null;
    }

    private void CreateResources()
    {
        _postRenderPass = CreatePostRenderPass();
        _postPipelines = VulkanGeometryPipelineCache.Create(
            _device,
            _postRenderPass,
            _geometryPipelineLayout,
            POST_SAMPLE_COUNT,
            _deviceGeneration
        );
        _storeBack = VulkanPostStoreBackPipeline.Create(_device);
        _quad = VulkanFullscreenQuad.Create(_device);
        _context = new PostProcessContext(this);
    }

    private RenderPass CreatePostRenderPass()
    {
        AttachmentDescription attachment = new()
        {
            Format = POST_FORMAT,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.DontCare,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.ColorAttachmentOptimal,
            FinalLayout = ImageLayout.ColorAttachmentOptimal,
        };
        AttachmentReference colorReference = new()
        {
            Attachment = 0,
            Layout = ImageLayout.ColorAttachmentOptimal,
        };
        SubpassDescription subpass = new()
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorReference,
            PResolveAttachments = null,
            PDepthStencilAttachment = null,
        };

        // The explicit target layout transition before every post pass carries the memory dependency; the
        // render pass dependency orders an earlier sampling read and attachment write before a reuse.
        SubpassDependency dependency = new()
        {
            SrcSubpass = uint.MaxValue,
            DstSubpass = 0,
            SrcStageMask =
                PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.FragmentShaderBit,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.ShaderReadBit,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit,
            DependencyFlags = 0,
        };
        RenderPassCreateInfo createInfo = new()
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 1,
            PAttachments = &attachment,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 1,
            PDependencies = &dependency,
        };
        Result result = _vulkan.CreateRenderPass(
            _device.Device,
            &createInfo,
            null,
            out RenderPass renderPass
        );
        ThrowIfFailed(result, "vkCreateRenderPass for the post-process chain");
        return renderPass;
    }

    [SuppressMessage(
        "Reliability",
        "CA2000",
        Justification = "The created set owns all three post targets; the catch disposes the partial set and the caller owns it on success."
    )]
    private PostTargetSet CreateSet(Extent2D extent)
    {
        VulkanPostTarget? first = null;
        VulkanPostTarget? second = null;
        VulkanPostTarget? third = null;
        try
        {
            first = VulkanPostTarget.Create(
                _device,
                extent,
                _postRenderPass,
                _bindless,
                _storeBack!
            );
            second = VulkanPostTarget.Create(
                _device,
                extent,
                _postRenderPass,
                _bindless,
                _storeBack!
            );
            third = VulkanPostTarget.Create(
                _device,
                extent,
                _postRenderPass,
                _bindless,
                _storeBack!
            );
            return new PostTargetSet(first, second, third);
        }
        catch
        {
            first?.Dispose();
            second?.Dispose();
            third?.Dispose();
            throw;
        }
    }

    /// <summary>Gets or creates an extent's target set in a frame slot and marks it used.</summary>
    private PostTargetSet GetTargetSet(PostTargetSlot slot, Extent2D extent, long frameIndex)
    {
        if (slot.TryGet(extent, frameIndex, out PostTargetSet? set))
        {
            return set;
        }

        set = CreateSet(extent);
        slot.Add(extent, set, frameIndex);
        TargetRecreationCount++;
        return set;
    }

    private TargetStoreBack GetStoreBack(VulkanRenderTarget target)
    {
        if (_storeBacks.TryGetValue(target, out TargetStoreBack cached))
        {
            return cached;
        }

        RenderPass renderPass = _storeBack!.GetOrCreateRenderPass(
            target.ColorFormat,
            target.SampleCount
        );
        // A multisampled target's store-back pass attaches the multisampled color image and resolves
        // into the single-sample resolved color image; a single-sample target attaches the resolved
        // color image directly.
        ImageView[] attachments = target.IsMultisampled
            ? [target.ColorAttachmentView, target.ResolvedColorView]
            : [target.ColorAttachmentView];
        FramebufferCreateInfo createInfo = new()
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = renderPass,
            AttachmentCount = (uint)attachments.Length,
            Width = target.Extent.Width,
            Height = target.Extent.Height,
            Layers = 1,
        };
        Result result;
        fixed (ImageView* attachmentsPointer = attachments)
        {
            createInfo.PAttachments = attachmentsPointer;
            result = _vulkan.CreateFramebuffer(
                _device.Device,
                &createInfo,
                null,
                out Framebuffer framebuffer
            );
            ThrowIfFailed(result, "vkCreateFramebuffer for the post-process store-back pass");
            TargetStoreBack storeBack = new(renderPass, framebuffer);
            _storeBacks.Add(target, storeBack);
            return storeBack;
        }
    }

    private void PushPassIndex(Vk vulkan, CommandBuffer commandBuffer, int passIndex)
    {
        uint index = checked((uint)passIndex);
        vulkan.CmdPushConstants(
            commandBuffer,
            _geometryPipelineLayout,
            VulkanDescriptorState.PUSH_CONSTANT_STAGES,
            VulkanDescriptorState.PASS_INDEX_PUSH_CONSTANT_OFFSET,
            VulkanDescriptorState.PASS_INDEX_PUSH_CONSTANT_SIZE,
            &index
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

    /// <summary>The store-back render pass and framebuffer of one camera target.</summary>
    private readonly record struct TargetStoreBack(RenderPass RenderPass, Framebuffer Framebuffer);

    /// <summary>
    /// One extent's ping-pong set of three post targets. <see cref="Rent"/> skips the current chain source
    /// so consecutive passes never overwrite the image they just sampled.
    /// </summary>
    private sealed class PostTargetSet(
        VulkanPostTarget first,
        VulkanPostTarget second,
        VulkanPostTarget third
    )
    {
        private readonly VulkanPostTarget[] _targets = [first, second, third];
        private int _nextIndex;

        /// <summary>Gets the next target to write, skipping <paramref name="source"/>.</summary>
        /// <param name="source">The current chain source, or <see langword="null"/> before an effect.</param>
        internal VulkanPostTarget Rent(VulkanPostTarget? source)
        {
            for (int offset = 0; offset < _targets.Length; offset++)
            {
                int index = (_nextIndex + offset) % _targets.Length;
                VulkanPostTarget target = _targets[index];
                if (ReferenceEquals(target, source))
                {
                    continue;
                }

                _nextIndex = (index + 1) % _targets.Length;
                return target;
            }

            throw new InvalidOperationException(
                "The post-target set has no target that is not the current chain source."
            );
        }

        internal void DisposeTargets()
        {
            _nextIndex = 0;
            foreach (VulkanPostTarget target in _targets)
            {
                target.Dispose();
            }
        }
    }

    /// <summary>One frame slot's target sets, keyed by extent, and the last recorded frame index.</summary>
    /// <remarks>
    /// A slot keeps one set per extent because one frame can run effects on targets of different sizes,
    /// and releases the previous frame's unused extents after the slot's fence signaled.
    /// </remarks>
    private sealed class PostTargetSlot
    {
        private readonly Dictionary<(uint Width, uint Height), SetEntry> _sets = [];
        private (uint Width, uint Height)[] _staleScratch = [];
        private long _previousFrameIndex = -1;

        internal void BeginFrame(long frameIndex)
        {
            if (_sets.Count == 0)
            {
                _previousFrameIndex = frameIndex;
                return;
            }

            // A frame can use more extents than the old fixed stack threshold (bloom alone can use six
            // per target size), so the slot keeps a reusable scratch array. It grows only when the
            // frame's extent count reaches a new maximum, never in steady state.
            if (_staleScratch.Length < _sets.Count)
            {
                _staleScratch = new (uint Width, uint Height)[_sets.Count];
            }

            Span<(uint Width, uint Height)> stale = _staleScratch.AsSpan(0, _sets.Count);
            int staleCount = 0;
            foreach (KeyValuePair<(uint Width, uint Height), SetEntry> set in _sets)
            {
                if (set.Value.LastUsedFrameIndex != _previousFrameIndex)
                {
                    stale[staleCount++] = set.Key;
                }
            }

            for (int index = 0; index < staleCount; index++)
            {
                _sets[stale[index]].Set.DisposeTargets();
                _sets.Remove(stale[index]);
            }

            _previousFrameIndex = frameIndex;
        }

        internal bool TryGet(
            Extent2D extent,
            long frameIndex,
            [NotNullWhen(true)] out PostTargetSet? set
        )
        {
            if (_sets.TryGetValue((extent.Width, extent.Height), out SetEntry? entry))
            {
                entry.LastUsedFrameIndex = frameIndex;
                set = entry.Set;
                return true;
            }

            set = null;
            return false;
        }

        internal void Add(Extent2D extent, PostTargetSet set, long frameIndex)
        {
            _sets.Add(
                (extent.Width, extent.Height),
                new SetEntry(set) { LastUsedFrameIndex = frameIndex }
            );
        }

        internal void DisposeTargets()
        {
            foreach (SetEntry entry in _sets.Values)
            {
                entry.Set.DisposeTargets();
            }

            _sets.Clear();
            _previousFrameIndex = -1;
        }

        private sealed class SetEntry(PostTargetSet set)
        {
            internal PostTargetSet Set { get; } = set;

            internal long LastUsedFrameIndex { get; set; } = -1;
        }
    }
}
