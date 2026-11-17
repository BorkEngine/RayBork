using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace RayBork.Internal;

/// <summary>
/// Executes the camera post-process chain on the render thread: the target-sized Rgba16Float ping-pong
/// targets, the custom-effect and built-in SSAO and bloom fullscreen passes, the engine store-back pass,
/// and the per-target store-back framebuffers.
/// </summary>
/// <remarks>
/// <para>
/// The chain is target-sized: the camera target's resolved color and depth are the source, an enabled
/// custom effect records one fullscreen pass from the current source into the next single-sample
/// <c>Rgba16Float</c> post target, and two effects therefore chain into two passes. The engine then
/// records one store-back pass that samples the final post target and writes the target's MSAA color
/// attachment (or its single-sample color image) for the camera viewport only, resolving the result for
/// a later camera, the present pass, or a readback. Depth is never part of the store-back.
/// </para>
/// <para>
/// The post targets are pooled per frame ring slot and per target extent, so one frame can run effects
/// on the scene target and on render targets of different sizes without destroying images a recorded
/// pass still references. <see cref="BeginFrame"/> keeps the extents the previous frame on that slot used
/// and destroys the rest after the slot's fence signaled, so a resize or a changed target set recycles
/// its images one frame later and the steady state allocates nothing. A device recovery disposes and
/// recreates the whole processor. Each post target allocates one bindless slot so an effect can sample
/// it through the shader ABI, and one descriptor set for the store-back pass' own sampler layout.
/// Render-thread only; the normal path allocates nothing after warm-up.
/// </para>
/// <para>
/// SSAO and bloom hook in through <see cref="PostProcessEffectSnapshot"/> and
/// <see cref="PostProcessContext"/>. A built-in rents a target-sized or
/// smaller color target from the same pool, creates a fullscreen pipeline against the post render pass,
/// writes one instance record per
/// pass, and records the pass with the engine quad and frame descriptor set. It completes its final
/// target-sized result with <see cref="PostProcessContext.CompleteColorTarget"/>, or releases an
/// intermediate target with <see cref="PostProcessContext.CompleteAuxiliaryTarget"/>; this processor then
/// runs the same store-back. <see cref="SsaoEffect"/> and <see cref="BloomEffect"/> are the built-in
/// effects on these hooks.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanPostProcessor : IDisposable
{
    private const Format POST_FORMAT = Format.R16G16B16A16Sfloat;
    private const int POST_SAMPLE_COUNT = 1;

    /// <summary>
    /// The forced render state of every custom post-process pipeline: opaque, no culling, no depth test
    /// or write, no blending. A shader's baked default render state never reaches a post pass.
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

    /// <summary>
    /// Gets the number of times a frame slot's target-sized post-target set was created, for GPU tests.
    /// Test-only; it is not part of the renderer seam.
    /// </summary>
    internal int TargetRecreationCount { get; private set; }

    /// <summary>
    /// Creates the post render pass, the custom-effect pipeline cache, the store-back pipeline set, the
    /// fullscreen quad, and the run context.
    /// </summary>
    /// <param name="device">The device that owns the resources.</param>
    /// <param name="bindless">The live bindless table.</param>
    /// <param name="frameResources">The frame slot resources that own the instance buffer.</param>
    /// <param name="geometryPipelineLayout">The shared geometry pipeline layout the custom effect uses.</param>
    /// <param name="deviceGeneration">The logical device generation the shader modules belong to.</param>
    /// <returns>The created processor.</returns>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    /// <exception cref="GraphicsException">
    /// A Vulkan object could not be created. The diagnostic code is <c>RB3001</c>.
    /// </exception>
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

    /// <summary>
    /// Starts one frame for a frame ring slot and releases the target extents the previous frame on that
    /// slot did not use.
    /// </summary>
    /// <remarks>
    /// The caller has waited for the slot's fence, so the previous submission on this slot completed and
    /// no in-flight command buffer references its post targets. Extents this frame reuses are kept; a
    /// stale extent (a window resize, a resized or released render target) is destroyed here. The call is
    /// idempotent for the frame and does nothing when the slot never ran effects.
    /// </remarks>
    /// <param name="frameSlot">The frame ring slot being recorded.</param>
    /// <param name="frameIndex">The frame's index, for the previous-frame comparison.</param>
    internal void BeginFrame(int frameSlot, long frameIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _slots[frameSlot].BeginFrame(frameIndex);
    }

    /// <summary>
    /// Runs the enabled effects of one camera pass and the store-back for its viewport.
    /// </summary>
    /// <param name="vulkan">The Vulkan functions.</param>
    /// <param name="commandBuffer">The command buffer recording the frame.</param>
    /// <param name="frame">The frame packet.</param>
    /// <param name="pass">The camera pass being recorded.</param>
    /// <param name="passIndex">The frame-wide camera pass index.</param>
    /// <param name="frameSlot">The frame ring slot.</param>
    /// <param name="target">The camera's render target.</param>
    /// <param name="targetColorSlot">The target's resolved color bindless slot.</param>
    /// <param name="targetDepthSlot">The target's resolved depth bindless slot, or <c>0</c>.</param>
    /// <param name="renderArea">The camera viewport rectangle the store-back writes.</param>
    /// <param name="instanceByteOffset">
    /// The next unused instance-buffer byte offset; advanced past the recorded effect instances.
    /// </param>
    /// <exception cref="GraphicsException">
    /// A shader is not a live GPU resource, a post target could not be created, or an effect declares a
    /// depth source the target cannot supply. The diagnostic code is <c>RB3001</c>.
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

    /// <summary>
    /// Disposes one camera target's store-back framebuffer. The caller invokes it before the target's
    /// images are replaced or destroyed. Safe with a target that has no cached store-back.
    /// </summary>
    /// <param name="target">The target whose framebuffer is no longer valid.</param>
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

    /// <summary>
    /// Gets the color-only single-sample render pass every post target is built against. A built-in
    /// effect creates its pipelines against this pass.
    /// </summary>
    internal RenderPass PostRenderPass => _postRenderPass;

    /// <summary>
    /// Gets the engine pipeline layout a fullscreen post pipeline must use so it can read the frame
    /// block and instance records and receive the pass-index push constant.
    /// </summary>
    internal PipelineLayout PipelineLayout => _geometryPipelineLayout;

    /// <summary>
    /// Gets the forced render state a post-process pipeline uses: opaque, no culling, no depth test or
    /// write, because the post render pass has no depth attachment.
    /// </summary>
    internal static RenderState FullscreenRenderState => _forcedRenderState;

    /// <summary>
    /// Gets the frame descriptor set that binds the frame block and instance-record storage buffer.
    /// </summary>
    /// <param name="frameSlot">The frame ring slot being recorded.</param>
    /// <returns>The descriptor set to bind at set 0.</returns>
    internal DescriptorSet GetFrameDescriptorSet(int frameSlot) =>
        _frameResources.GetDescriptorSet(frameSlot);

    /// <summary>
    /// Gets a writable range of the frame slot's instance-record storage.
    /// </summary>
    /// <param name="frameSlot">The frame ring slot being recorded.</param>
    /// <param name="byteOffset">The record's byte offset.</param>
    /// <param name="stride">The record's size in bytes.</param>
    /// <returns>The writable record span.</returns>
    internal Span<byte> GetInstanceRecordSpan(int frameSlot, ulong byteOffset, int stride) =>
        _frameResources.GetInstanceSpan(frameSlot, byteOffset, (ulong)stride);

    /// <summary>
    /// Gets or creates a fullscreen pipeline for a baked shader against the post render pass, using the
    /// engine pipeline layout and the requested render state.
    /// </summary>
    /// <param name="program">The internal shader program asset.</param>
    /// <param name="renderState">
    /// The render state to bake into the pipeline. It must be the forced fullscreen state, because the
    /// post render pass has no depth attachment; a depth-tested, culled, or blended state is rejected
    /// before pipeline creation.
    /// </param>
    /// <returns>The cached pipeline.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="renderState"/> is not <see cref="FullscreenRenderState"/>.
    /// </exception>
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

    /// <summary>
    /// Rents a target-sized post target for the next pass and transitions it for writing.
    /// </summary>
    /// <param name="context">The current camera pass context.</param>
    /// <returns>The target to write.</returns>
    internal VulkanPostTarget RentColorTarget(PostProcessContext context) =>
        RentColorTarget(context, context.Extent);

    /// <summary>
    /// Rents a post target of any extent from the per-extent pool and transitions it for writing.
    /// </summary>
    /// <remarks>
    /// The rent rotates through the extent's three-target set and skips the current chain source, so a
    /// prior effect's completed output is preserved while consecutive passes do not overwrite the image
    /// they just sampled. A built-in effect uses this for an intermediate pass at a different resolution,
    /// such as a bloom downsample, and releases it with
    /// <see cref="PostProcessContext.CompleteAuxiliaryTarget"/>.
    /// </remarks>
    /// <param name="context">The current camera pass context.</param>
    /// <param name="extent">The positive target extent.</param>
    /// <returns>The target to write.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="extent"/> has a zero dimension.</exception>
    /// <exception cref="InvalidOperationException">
    /// No post chain is active; only an effect's <c>Run</c> may rent a target.
    /// </exception>
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

    /// <summary>
    /// Transitions a rendered post target to a shader read and makes it the chain source. The target
    /// must be camera-target-sized, because the store-back maps the camera viewport through it.
    /// </summary>
    /// <param name="context">The current camera pass context.</param>
    /// <param name="target">The target the effect rendered into.</param>
    /// <exception cref="InvalidOperationException">
    /// The target is not the camera target's size; complete an intermediate-sized result with
    /// <see cref="PostProcessContext.CompleteAuxiliaryTarget"/> instead.
    /// </exception>
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

    /// <summary>
    /// Transitions an auxiliary post target to a shader read without making it the chain source, so a
    /// built-in effect can sample it in a later pass.
    /// </summary>
    /// <param name="context">The current camera pass context.</param>
    /// <param name="target">The auxiliary target the effect rendered into.</param>
    internal void CompleteAuxiliaryTarget(PostProcessContext context, VulkanPostTarget target)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(target);
        target.TransitionToShaderRead(context.Vulkan, context.CommandBuffer);
    }

    /// <summary>
    /// Records one fullscreen pass from a post target into another with a pipeline created against the
    /// post render pass, the engine fullscreen quad, and the frame descriptor set.
    /// </summary>
    /// <remarks>
    /// The caller reserves and writes the pass' instance record first with
    /// <see cref="PostProcessContext.ReserveInstanceRecord"/> and
    /// <see cref="PostProcessContext.GetInstanceRecordSpan"/>, and writes every texture index slot
    /// itself. A custom shader effect and a built-in multi-pass effect both use this recorder.
    /// </remarks>
    /// <param name="context">The current camera pass context.</param>
    /// <param name="destination">The rented target to write.</param>
    /// <param name="pipeline">The fullscreen pipeline to draw with.</param>
    /// <param name="instanceByteOffset">The reserved record's stride-aligned byte offset.</param>
    /// <param name="stride">The record's size in bytes.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="pipeline"/> is the default handle or <paramref name="instanceByteOffset"/> is not
    /// stride-aligned.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="stride"/> is not positive.</exception>
    /// <exception cref="InvalidOperationException">No post chain is active.</exception>
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

    /// <summary>
    /// Records one custom effect's fullscreen pass from the current source into
    /// <paramref name="destination"/>.
    /// </summary>
    /// <param name="context">The current camera pass context.</param>
    /// <param name="destination">The rented target to write.</param>
    /// <param name="snapshot">The frozen effect state.</param>
    /// <param name="program">The effect's internal shader program asset.</param>
    /// <exception cref="GraphicsException">
    /// The shader is not a live GPU resource, the depth source is missing, or the instance record does
    /// not fit the frame slot. The diagnostic code is <c>RB3001</c>.
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

    /// <summary>
    /// Records the engine store-back pass from the final chain target into the camera target for the
    /// camera viewport and returns the resolved color to a shader read.
    /// </summary>
    /// <param name="context">The current camera pass context.</param>
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

    /// <summary>
    /// Resolves one material texture binding to its bindless slot.
    /// </summary>
    /// <param name="texture">The texture, or <see langword="null"/> for the white fallback.</param>
    /// <returns>The bindless slot.</returns>
    internal static uint ResolveTextureSlot(Texture? texture) =>
        VulkanTextureBinding.ResolveSlot(texture);

    /// <summary>
    /// Destroys every cached store-back framebuffer, the target-sized post targets, the pipeline caches,
    /// the fullscreen quad, and the store-back pipeline set. The call is idempotent and must run before
    /// the device, render targets, and bindless table are destroyed.
    /// </summary>
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

    /// <summary>
    /// Gets or creates the post-target set for one extent in a frame slot and marks it used by this
    /// frame.
    /// </summary>
    /// <param name="slot">The frame slot's pool.</param>
    /// <param name="extent">The positive target extent.</param>
    /// <param name="frameIndex">The recording frame's index.</param>
    /// <returns>The set to ping-pong through.</returns>
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

    /// <summary>
    /// The store-back render pass and framebuffer of one camera target.
    /// </summary>
    private readonly record struct TargetStoreBack(RenderPass RenderPass, Framebuffer Framebuffer);

    /// <summary>
    /// One post-target set the renderer ping-pongs through for one extent. The set owns three
    /// target-sized images; <see cref="Rent"/> rotates through them and skips the current chain source,
    /// so a prior effect's completed output survives and consecutive passes do not overwrite the image
    /// they just sampled.
    /// </summary>
    private sealed class PostTargetSet(
        VulkanPostTarget first,
        VulkanPostTarget second,
        VulkanPostTarget third
    )
    {
        private readonly VulkanPostTarget[] _targets = [first, second, third];
        private int _nextIndex;

        /// <summary>
        /// Gets the next target to write, skipping <paramref name="source"/> so the chain's current
        /// source image is preserved.
        /// </summary>
        /// <param name="source">The current chain source, or <see langword="null"/> before an effect.</param>
        /// <returns>The target to write.</returns>
        /// <exception cref="InvalidOperationException">Every target is the current source.</exception>
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

    /// <summary>
    /// One frame slot's target-sized post targets, keyed by target extent, and the last frame index the
    /// slot recorded.
    /// </summary>
    /// <remarks>
    /// A frame can run effects on the scene target and on several render targets of different sizes, so a
    /// single set per slot would destroy images a recorded pass still references. The slot therefore
    /// keeps one set per extent and releases the extents the previous frame on the slot did not use; the
    /// caller invokes <see cref="BeginFrame"/> only after the slot's fence signaled, so those images are
    /// no longer in flight.
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
