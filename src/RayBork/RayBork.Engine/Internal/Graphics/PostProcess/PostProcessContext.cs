using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>One camera pass' post-process context and the effect hooks into the chain.</summary>
/// <remarks>
/// The backend reuses and resets one instance per pass; the normal path allocates nothing.
/// <see cref="CompleteColorTarget"/> makes an effect's output the next source, a final target must be
/// camera-target-sized, and an intermediate one is released with
/// <see cref="CompleteAuxiliaryTarget"/> before the backend stores the chain back.
/// </remarks>
internal sealed class PostProcessContext
{
    private readonly VulkanPostProcessor _processor;
    private VulkanPostTarget? _currentColorTarget;

    internal PostProcessContext(VulkanPostProcessor processor)
    {
        ArgumentNullException.ThrowIfNull(processor);
        _processor = processor;
    }

    /// <summary>Gets the Vulkan functions of the recording device.</summary>
    internal Vk Vulkan { get; private set; } = null!;

    /// <summary>Gets the logical device that owns the post render pass, pipelines, and targets.</summary>
    internal VulkanDevice Device => _processor.Device;

    /// <summary>Gets the color-only single-sample render pass every post target is built against.</summary>
    internal RenderPass PostRenderPass => _processor.PostRenderPass;

    /// <summary>Gets the engine pipeline layout a fullscreen post pipeline must use.</summary>
    internal PipelineLayout PipelineLayout => _processor.PipelineLayout;

    /// <summary>Gets the forced render state a post-process pipeline uses.</summary>
    internal static RenderState FullscreenRenderState => VulkanPostProcessor.FullscreenRenderState;

    /// <summary>Gets the command buffer the frame records into.</summary>
    internal CommandBuffer CommandBuffer { get; private set; }

    /// <summary>Gets the frame packet the camera pass belongs to.</summary>
    internal RenderFramePacket Frame { get; private set; } = null!;

    /// <summary>Gets the camera pass being recorded.</summary>
    internal CameraPass Pass { get; private set; } = null!;

    /// <summary>Gets the frame-wide camera pass index the frame block uses.</summary>
    internal int PassIndex { get; private set; }

    /// <summary>Gets the frame ring slot that owns the frame resources and post-target pool.</summary>
    internal int FrameSlot { get; private set; }

    /// <summary>Gets the camera's render target.</summary>
    internal VulkanRenderTarget Target { get; private set; } = null!;

    /// <summary>Gets the target extent in pixels; the chain's final target is always this size.</summary>
    internal Extent2D Extent { get; private set; }

    /// <summary>Gets the camera's clamped viewport rectangle. The store-back writes only this area.</summary>
    internal Rect2D RenderArea { get; private set; }

    /// <summary>Gets the target's resolved color slot, the chain source before the first effect.</summary>
    internal uint TargetColorSlot { get; private set; }

    /// <summary>Gets the target's resolved depth slot, or <c>0</c> without a depth attachment.</summary>
    internal uint TargetDepthSlot { get; private set; }

    /// <summary>
    /// Gets the next unused byte offset in the frame slot's instance storage buffer.
    /// </summary>
    internal ulong InstanceByteOffset { get; private set; }

    /// <summary>Gets the frame descriptor set that binds the frame block and instance storage buffer.</summary>
    internal DescriptorSet FrameDescriptorSet => _processor.GetFrameDescriptorSet(FrameSlot);

    /// <summary>Gets the current chain source color slot: the last completed target or the target color.</summary>
    internal uint SourceColorSlot => _currentColorTarget?.BindlessSlot ?? TargetColorSlot;

    /// <summary>Gets the target's resolved depth slot; every effect in a chain samples the same depth.</summary>
    internal uint SourceDepthSlot => TargetDepthSlot;

    /// <summary>Gets the last completed effect's target, or <c>null</c> before the first effect.</summary>
    internal VulkanPostTarget? CurrentColorTarget => _currentColorTarget;

    /// <summary>Gets or creates a fullscreen pipeline for a baked shader against the post render pass.</summary>
    internal Pipeline GetOrCreateFullscreenPipeline(EngineAsset program, RenderState renderState) =>
        _processor.GetOrCreateFullscreenPipeline(program, renderState);

    /// <summary>Reserves one stride-aligned instance record and advances the next offset past it.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The stride is not positive.</exception>
    internal ulong ReserveInstanceRecord(int stride)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stride);
        ulong byteOffset = VulkanInstanceRecordWriter.AlignUp(InstanceByteOffset, (ulong)stride);
        InstanceByteOffset = byteOffset + (ulong)stride;
        return byteOffset;
    }

    /// <summary>Gets a writable span of one reserved instance record.</summary>
    internal Span<byte> GetInstanceRecordSpan(ulong byteOffset, int stride) =>
        _processor.GetInstanceRecordSpan(FrameSlot, byteOffset, stride);

    /// <summary>Resets the context for one camera pass before its first effect.</summary>
    internal void Reset(
        Vk vulkan,
        CommandBuffer commandBuffer,
        RenderFramePacket frame,
        CameraPass pass,
        int passIndex,
        int frameSlot,
        VulkanRenderTarget target,
        Extent2D extent,
        Rect2D renderArea,
        uint targetColorSlot,
        uint targetDepthSlot,
        ulong instanceByteOffset
    )
    {
        Vulkan = vulkan;
        CommandBuffer = commandBuffer;
        Frame = frame;
        Pass = pass;
        PassIndex = passIndex;
        FrameSlot = frameSlot;
        Target = target;
        Extent = extent;
        RenderArea = renderArea;
        TargetColorSlot = targetColorSlot;
        TargetDepthSlot = targetDepthSlot;
        InstanceByteOffset = instanceByteOffset;
        _currentColorTarget = null;
    }

    /// <summary>Rents a camera-target-sized color target and transitions it for writing.</summary>
    internal VulkanPostTarget RentColorTarget() => _processor.RentColorTarget(this);

    /// <summary>Rents an intermediate color target of a positive extent and transitions it for writing.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The extent has a zero dimension or exceeds the device limit.
    /// </exception>
    internal VulkanPostTarget RentColorTarget(Extent2D extent) =>
        _processor.RentColorTarget(this, extent);

    /// <summary>Transitions a camera-target-sized target to a shader read and makes it the chain source.</summary>
    /// <exception cref="InvalidOperationException">
    /// The target is not the camera target's size; complete it as auxiliary instead.
    /// </exception>
    internal void CompleteColorTarget(VulkanPostTarget target) =>
        _processor.CompleteColorTarget(this, target);

    /// <summary>Transitions an auxiliary target to a shader read without making it the chain source.</summary>
    internal void CompleteAuxiliaryTarget(VulkanPostTarget target) =>
        _processor.CompleteAuxiliaryTarget(this, target);

    /// <summary>Records one fullscreen pass into a rented target with the caller's instance record.</summary>
    /// <remarks>The caller reserves the record and writes every texture index slot.</remarks>
    internal void RecordFullscreenPass(
        VulkanPostTarget destination,
        Pipeline pipeline,
        ulong instanceByteOffset,
        int stride
    ) => _processor.RecordFullscreenPass(this, destination, pipeline, instanceByteOffset, stride);

    /// <summary>Records one fullscreen custom-effect pass from the current source.</summary>
    internal void RecordCustomShaderPass(
        VulkanPostTarget destination,
        ShaderPostProcessEffectSnapshot snapshot,
        EngineAsset program
    ) => _processor.RecordCustomShaderPass(this, destination, snapshot, program);

    /// <summary>Records the store-back pass into the camera target for the camera viewport.</summary>
    internal void RecordStoreBack() => _processor.RecordStoreBack(this);

    /// <summary>Resolves a material texture binding to its bindless slot.</summary>
    /// <param name="texture">The texture, or <see langword="null"/> for the white fallback.</param>
    internal static uint ResolveTextureSlot(Texture? texture) =>
        VulkanPostProcessor.ResolveTextureSlot(texture);

    /// <summary>Marks a target as the chain source after a completed pass.</summary>
    internal void SetCurrentColorTarget(VulkanPostTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        _currentColorTarget = target;
    }
}
