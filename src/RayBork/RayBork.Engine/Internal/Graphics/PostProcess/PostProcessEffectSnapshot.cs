namespace RayBork.Internal;

/// <summary>The immutable run state of one <see cref="PostProcessEffect"/> inside a camera pass.</summary>
/// <remarks>
/// A snapshot freezes the effect's enabled value and shader state at camera creation, so later material
/// or stack changes affect only later cameras. A snapshot is pooled per effect and returns to the pool
/// from <see cref="Reset"/> when the pass that held it is recycled.
/// </remarks>
internal abstract class PostProcessEffectSnapshot
{
    private PostProcessEffect? _owner;
    private bool _attached;

    /// <summary>Gets whether the snapshot is a process-wide shared instance that is never pooled.</summary>
    internal virtual bool IsShared => false;

    /// <summary>Gets whether the effect runs for this pass.</summary>
    internal virtual bool Enabled => true;

    /// <summary>Gets the effect's texture bindings in sidecar declaration order, or an empty span.</summary>
    internal virtual ReadOnlySpan<Texture?> TextureValues => default;

    /// <summary>Gets the ordered shader-ABI instance records the effect's run appends.</summary>
    internal virtual ReadOnlySpan<PostProcessInstanceRecord> InstanceLayout => default;

    /// <summary>Gets whether the engine overrides a reserved texture binding.</summary>
    /// <param name="index">The zero-based sidecar texture declaration index.</param>
    /// <remarks>The engine replaces a reserved binding with the chain source or the target's depth.</remarks>
    internal virtual bool IsReservedTextureBinding(int index) => false;

    /// <summary>Adds this effect's engine-asset holds to the frame's retention set.</summary>
    /// <param name="retention">The recording frame's retention set.</param>
    internal virtual void Retain(FrameAssetRetention retention) { }

    /// <summary>Attaches the owning effect so <see cref="Reset"/> can return the snapshot to its pool.</summary>
    /// <param name="owner">The effect that captured this snapshot.</param>
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

    /// <summary>Releases the snapshot's state and returns it to its effect's pool.</summary>
    /// <remarks>Called when the pass is recycled; idempotent and a no-op for a shared snapshot.</remarks>
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

    /// <summary>Releases the references a reused snapshot must not keep between runs.</summary>
    protected virtual void ReleaseState() { }

    /// <summary>Records this effect's passes into the current post-process chain.</summary>
    /// <param name="context">The per-camera-pass post-process context.</param>
    internal abstract void Run(PostProcessContext context);
}
