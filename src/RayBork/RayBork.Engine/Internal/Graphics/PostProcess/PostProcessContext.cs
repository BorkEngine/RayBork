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
/// <see cref="RecordCustomShaderPass"/>. A built-in multi-pass effect (SSAO, bloom) is the
/// documented extension point: it subclasses <see cref="PostProcessEffectSnapshot"/>, rents one or more
/// target-sized color targets with <see cref="RentColorTarget"/>, records its own pipeline passes
/// between them, and completes its final target with <see cref="CompleteColorTarget"/>. The backend then
/// runs <see cref="RecordStoreBack"/> once for the chain.
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

    /// <summary>Gets the target extent in pixels; the chain is always target-sized.</summary>
    internal Extent2D Extent { get; private set; }

    /// <summary>Gets the camera's clamped viewport rectangle. The store-back writes only this area.</summary>
    internal Rect2D RenderArea { get; private set; }

    /// <summary>Gets the camera's clamped viewport, for the store-back pass.</summary>
    internal Viewport Viewport { get; private set; }

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
    /// Gets or sets the next unused byte offset in the frame slot's instance storage buffer.
    /// </summary>
    internal ulong InstanceByteOffset { get; set; }

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
    /// <param name="viewport">The camera viewport for the store-back pass.</param>
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
        Viewport viewport,
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
        Viewport = viewport;
        TargetColorSlot = targetColorSlot;
        TargetDepthSlot = targetDepthSlot;
        InstanceByteOffset = instanceByteOffset;
        _currentColorTarget = null;
    }

    /// <summary>Rents a target-sized color target for the next pass and transitions it for writing.</summary>
    /// <returns>The target to render into.</returns>
    internal VulkanPostTarget RentColorTarget() => _processor.RentColorTarget(this);

    /// <summary>
    /// Transitions a rendered target to a shader read and makes it the chain source for the next
    /// effect or the store-back.
    /// </summary>
    /// <param name="target">The target the effect rendered into.</param>
    internal void CompleteColorTarget(VulkanPostTarget target) => _processor.CompleteColorTarget(this, target);

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
