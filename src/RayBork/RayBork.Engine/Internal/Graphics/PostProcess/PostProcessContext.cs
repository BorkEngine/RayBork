using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// One camera pass' post-process run context: the source views and slots, the target extent and camera
/// viewport, the frame and pass being recorded, the target-sized post-target pool, fullscreen-pass
/// recording, and the store-back hook.
/// </summary>
/// <remarks>
/// <para>
/// The renderer reuses one instance per backend and resets it before each camera pass with a stack, so the
/// normal path allocates nothing. An effect reads <see cref="SourceColorSlot"/> and
/// <see cref="SourceDepthSlot"/> only until it calls <see cref="CompleteColorTarget"/>; that call makes
/// its output the source for the next effect. Custom effects use
/// <see cref="RecordCustomShaderPass"/>. A built-in multi-pass effect (SSAO, bloom) is the documented
/// extension point: it subclasses <see cref="PostProcessEffectSnapshot"/>, rents a target-sized color
/// target with <see cref="RentColorTarget()"/> and smaller intermediates with
/// <see cref="RentColorTarget(Extent2D)"/>, creates a fullscreen pipeline with
/// <see cref="GetOrCreateFullscreenPipeline"/>, writes one instance record per pass with
/// <see cref="ReserveInstanceRecord"/> and <see cref="GetInstanceRecordSpan"/>, records the pass with
/// <see cref="RecordFullscreenPass"/>, and releases intermediates with
/// <see cref="CompleteAuxiliaryTarget"/>. Its final camera-target-sized target is completed with
/// <see cref="CompleteColorTarget"/>, after which the backend runs <see cref="RecordStoreBack"/> once for
/// the chain. A built-in adds its own shaders and effect type; it does not need to change the public
/// stack, the camera snapshot path, or the processor.
/// </para>
/// <para>
/// The per-extent pool holds two targets per extent, so a built-in can have two intermediate targets of
/// one size before it completes or reuses one; a different size gets its own pair. A target rented at a
/// different extent must be completed with <see cref="CompleteAuxiliaryTarget"/>; a chain's final target
/// must be the camera target's size.
/// </para>
/// <para>
/// Render-thread only. The context is valid for exactly one camera pass and is reset before the next.
/// </para>
/// </remarks>
internal sealed class PostProcessContext {
    private readonly VulkanPostProcessor _processor;
    private VulkanPostTarget? _currentColorTarget;

    internal PostProcessContext(VulkanPostProcessor processor) {
        ArgumentNullException.ThrowIfNull(processor);
        _processor = processor;
    }

    /// <summary>Gets the Vulkan functions of the recording device.</summary>
    internal Vk Vulkan { get; private set; } = null!;

    /// <summary>Gets the logical device that owns the post render pass, pipelines, and targets.</summary>
    internal VulkanDevice Device => _processor.Device;

    /// <summary>
    /// Gets the color-only single-sample render pass every post target is built against. A built-in
    /// effect creates its pipelines against this pass.
    /// </summary>
    internal RenderPass PostRenderPass => _processor.PostRenderPass;

    /// <summary>
    /// Gets the engine pipeline layout a fullscreen post pipeline must use so it can read the frame
    /// block and instance records and receive the pass-index push constant.
    /// </summary>
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

    /// <summary>
    /// Gets the bindless slot of the camera target's resolved color image, which is the chain source
    /// before the first effect completes a target.
    /// </summary>
    internal uint TargetColorSlot { get; private set; }

    /// <summary>
    /// Gets the bindless slot of the camera target's resolved depth image, or <c>0</c> when the target
    /// has no depth attachment.
    /// </summary>
    internal uint TargetDepthSlot { get; private set; }

    /// <summary>
    /// Gets the next unused byte offset in the frame slot's instance storage buffer.
    /// </summary>
    internal ulong InstanceByteOffset { get; private set; }

    /// <summary>Gets the frame descriptor set that binds the frame block and instance storage buffer.</summary>
    internal DescriptorSet FrameDescriptorSet => _processor.GetFrameDescriptorSet(FrameSlot);

    /// <summary>
    /// Gets the current chain source color slot: the last completed effect's target, or the camera
    /// target's resolved color before the first effect.
    /// </summary>
    internal uint SourceColorSlot => _currentColorTarget?.BindlessSlot ?? TargetColorSlot;

    /// <summary>
    /// Gets the camera target's resolved depth slot, or <c>0</c>. Every effect in one chain samples the
    /// same target depth.
    /// </summary>
    internal uint SourceDepthSlot => TargetDepthSlot;

    /// <summary>Gets the last completed effect's target, or <c>null</c> before the first effect.</summary>
    internal VulkanPostTarget? CurrentColorTarget => _currentColorTarget;

    /// <summary>
    /// Gets or creates a fullscreen pipeline for a baked shader against the post render pass.
    /// </summary>
    /// <param name="program">The internal shader program asset.</param>
    /// <param name="renderState">The render state to bake into the pipeline.</param>
    /// <returns>The cached pipeline.</returns>
    internal Pipeline GetOrCreateFullscreenPipeline(EngineAsset program, RenderState renderState) =>
        _processor.GetOrCreateFullscreenPipeline(program, renderState);

    /// <summary>
    /// Reserves one stride-aligned instance record in the frame slot's instance buffer and advances the
    /// chain's next offset past it.
    /// </summary>
    /// <param name="stride">The record's size in bytes, including the fixed instance header.</param>
    /// <returns>The record's byte offset, aligned to <paramref name="stride"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="stride"/> is not positive.</exception>
    internal ulong ReserveInstanceRecord(int stride) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stride);
        ulong byteOffset = VulkanInstanceRecordWriter.AlignUp(InstanceByteOffset, (ulong)stride);
        InstanceByteOffset = byteOffset + (ulong)stride;
        return byteOffset;
    }

    /// <summary>
    /// Gets a writable span of one reserved instance record.
    /// </summary>
    /// <param name="byteOffset">The offset <see cref="ReserveInstanceRecord"/> returned.</param>
    /// <param name="stride">The record's size in bytes.</param>
    /// <returns>The writable record span.</returns>
    internal Span<byte> GetInstanceRecordSpan(ulong byteOffset, int stride) =>
        _processor.GetInstanceRecordSpan(FrameSlot, byteOffset, stride);

    /// <summary>
    /// Resets the context for one camera pass. Called by the renderer before the pass' first effect.
    /// </summary>
    /// <param name="vulkan">The Vulkan functions.</param>
    /// <param name="commandBuffer">The command buffer the frame records into.</param>
    /// <param name="frame">The frame packet.</param>
    /// <param name="pass">The camera pass being recorded.</param>
    /// <param name="passIndex">The frame-wide pass index.</param>
    /// <param name="frameSlot">The frame ring slot.</param>
    /// <param name="target">The camera's render target.</param>
    /// <param name="extent">The target extent.</param>
    /// <param name="renderArea">The camera viewport rectangle the store-back writes.</param>
    /// <param name="targetColorSlot">The resolved color slot of the camera target.</param>
    /// <param name="targetDepthSlot">The resolved depth slot, or <c>0</c>.</param>
    /// <param name="instanceByteOffset">The next unused instance-buffer byte offset.</param>
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
        ulong instanceByteOffset) {
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

    /// <summary>Rents a camera-target-sized color target for the next pass and transitions it for writing.</summary>
    /// <returns>The target to write.</returns>
    internal VulkanPostTarget RentColorTarget() => _processor.RentColorTarget(this);

    /// <summary>
    /// Rents a color target of any extent for an intermediate pass and transitions it for writing.
    /// </summary>
    /// <param name="extent">The positive target extent.</param>
    /// <returns>The target to write.</returns>
    internal VulkanPostTarget RentColorTarget(Extent2D extent) => _processor.RentColorTarget(this, extent);

    /// <summary>
    /// Transitions a rendered camera-target-sized target to a shader read and makes it the chain source
    /// for the next effect or the store-back.
    /// </summary>
    /// <param name="target">The target the effect rendered into.</param>
    internal void CompleteColorTarget(VulkanPostTarget target) => _processor.CompleteColorTarget(this, target);

    /// <summary>
    /// Transitions an auxiliary target to a shader read without making it the chain source, so a later
    /// pass can sample it.
    /// </summary>
    /// <param name="target">The auxiliary target the effect rendered into.</param>
    internal void CompleteAuxiliaryTarget(VulkanPostTarget target) =>
        _processor.CompleteAuxiliaryTarget(this, target);

    /// <summary>
    /// Records one fullscreen pass into a rented target with a pipeline created against the post render
    /// pass. The caller uses <see cref="ReserveInstanceRecord"/> and <see cref="GetInstanceRecordSpan"/>
    /// first and writes every texture index slot itself.
    /// </summary>
    /// <param name="destination">The rented target to write.</param>
    /// <param name="pipeline">The fullscreen pipeline to draw with.</param>
    /// <param name="instanceByteOffset">The reserved record's stride-aligned byte offset.</param>
    /// <param name="stride">The record's size in bytes.</param>
    internal void RecordFullscreenPass(
        VulkanPostTarget destination,
        Pipeline pipeline,
        ulong instanceByteOffset,
        int stride) => _processor.RecordFullscreenPass(this, destination, pipeline, instanceByteOffset, stride);

    /// <summary>
    /// Records one fullscreen custom-effect pass from the current source into
    /// <paramref name="destination"/>, then the caller completes the target.
    /// </summary>
    /// <param name="destination">The rented target to write.</param>
    /// <param name="snapshot">The frozen custom-effect state.</param>
    /// <param name="program">The effect's internal shader program asset.</param>
    internal void RecordCustomShaderPass(
        VulkanPostTarget destination,
        ShaderPostProcessEffectSnapshot snapshot,
        EngineAsset program) => _processor.RecordCustomShaderPass(this, destination, snapshot, program);

    /// <summary>
    /// Records the engine store-back pass from the final chain target into the camera target for the
    /// camera viewport.
    /// </summary>
    internal void RecordStoreBack() => _processor.RecordStoreBack(this);

    /// <summary>Resolves one material texture binding to its bindless slot.</summary>
    /// <param name="texture">The texture, or <see langword="null"/> for the white fallback.</param>
    /// <returns>The bindless slot.</returns>
    internal static uint ResolveTextureSlot(Texture? texture) => VulkanPostProcessor.ResolveTextureSlot(texture);

    /// <summary>Marks <paramref name="target"/> as the chain source. Called after a completed pass.</summary>
    /// <param name="target">The target that now holds the chain's current output.</param>
    internal void SetCurrentColorTarget(VulkanPostTarget target) {
        ArgumentNullException.ThrowIfNull(target);
        _currentColorTarget = target;
    }
}
