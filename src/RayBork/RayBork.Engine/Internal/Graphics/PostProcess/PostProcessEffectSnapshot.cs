namespace RayBork.Internal;

/// <summary>
/// The immutable run state of one <see cref="PostProcessEffect"/> inside a recorded camera pass.
/// </summary>
/// <remarks>
/// <para>
/// A camera creates one snapshot per stack entry when it is created. The snapshot freezes the effect's
/// enabled value, the shader cursor and texture bindings of a custom effect, and anything a future
/// built-in effect needs; the tick thread never touches the recorded frame again, and a later material
/// or stack change affects only later cameras.
/// </para>
/// <para>
/// The base class is internal and engine-owned. A built-in multi-pass effect (SSAO, bloom) adds its own
/// subclass that overrides <see cref="Run"/> and uses <see cref="PostProcessContext"/>'s target rental,
/// fullscreen-pass recording, and store-back hooks. The custom shader effect, its uniforms and
/// textures, and any built-in effect state are retained by the frame's retention set until the render
/// thread releases the slot.
/// </para>
/// <para>
/// A snapshot is pooled per effect. <see cref="Reset"/> runs on the tick thread when the camera pass that
/// held the snapshot is recycled, releases its state, and returns it to the owning effect's pool so a
/// later camera reuses the same object, uniform buffer, and texture array instead of allocating them
/// again. The shared disabled snapshot is never pooled.
/// </para>
/// </remarks>
internal abstract class PostProcessEffectSnapshot
{
    private PostProcessEffect? _owner;
    private bool _attached;

    /// <summary>
    /// Gets whether the snapshot is a process-wide shared instance that is never attached or pooled.
    /// </summary>
    internal virtual bool IsShared => false;

    /// <summary>Gets whether the effect runs for this pass.</summary>
    internal virtual bool Enabled => true;

    /// <summary>
    /// Gets the texture bindings the effect samples through the shader ABI, in sidecar declaration
    /// order, or an empty span for a built-in effect that does not use the custom shader path.
    /// </summary>
    internal virtual ReadOnlySpan<Texture?> TextureValues => default;

    /// <summary>
    /// Gets the ordered shader-ABI instance records the effect's run appends, or an empty span for an
    /// effect that does not draw through the shader-instance buffer.
    /// </summary>
    internal virtual ReadOnlySpan<PostProcessInstanceRecord> InstanceLayout => default;

    /// <summary>
    /// Gets whether a texture binding index is a reserved engine binding that the renderer overrides.
    /// </summary>
    /// <remarks>
    /// A reserved binding's material value is never sampled, validated, retained, or registered as a
    /// sampled render target; the engine replaces it with the chain source or the target's depth.
    /// </remarks>
    /// <param name="index">The zero-based sidecar texture declaration index.</param>
    /// <returns><see langword="true"/> when the engine overrides that binding.</returns>
    internal virtual bool IsReservedTextureBinding(int index) => false;

    /// <summary>
    /// Adds this effect's engine-asset holds to the frame's retention set. Called on the tick thread
    /// after the camera captured the effect.
    /// </summary>
    /// <param name="retention">The recording frame's retention set.</param>
    internal virtual void Retain(FrameAssetRetention retention) { }

    /// <summary>
    /// Attaches the owning effect so <see cref="Reset"/> can return the snapshot to its pool. Called on
    /// the tick thread right after the capture. A shared snapshot ignores the call.
    /// </summary>
    /// <param name="owner">The effect that captured this snapshot.</param>
    /// <exception cref="ArgumentNullException"><paramref name="owner"/> is <c>null</c>.</exception>
    internal void Attach(PostProcessEffect owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (IsShared)
        {
            return;
        }

        _owner = owner;
        _attached = true;
    }

    /// <summary>
    /// Releases the state the snapshot holds and returns the snapshot to its effect's pool. Called on the
    /// tick thread when the pass that held it is recycled. Idempotent, and a no-op for a shared snapshot.
    /// </summary>
    internal void Reset()
    {
        if (IsShared || !_attached)
        {
            return;
        }

        _attached = false;
        ReleaseState();
        _owner?.ReturnSnapshot(this);
    }

    /// <summary>
    /// Releases the references and state a reused snapshot must not keep between runs.
    /// </summary>
    protected virtual void ReleaseState() { }

    /// <summary>
    /// Records this effect's passes into the current post-process chain.
    /// </summary>
    /// <param name="context">The per-camera-pass post-process context.</param>
    internal abstract void Run(PostProcessContext context);
}
