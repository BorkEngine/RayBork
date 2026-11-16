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
/// </remarks>
internal abstract class PostProcessEffectSnapshot {
    /// <summary>Gets whether the effect runs for this pass.</summary>
    internal virtual bool Enabled => true;

    /// <summary>
    /// Gets the texture bindings the effect samples through the shader ABI, in sidecar declaration
    /// order, or an empty span for a built-in effect that does not use the custom shader path.
    /// </summary>
    internal virtual ReadOnlySpan<Texture?> TextureValues => default;

    /// <summary>
    /// Gets the bytes one instance record for this effect needs, including the fixed instance header,
    /// or <c>0</c> for an effect that does not draw through the shader-instance buffer.
    /// </summary>
    internal virtual int InstanceStride => 0;

    /// <summary>
    /// Gets the number of instance records this effect appends to the frame slot's instance buffer when
    /// it runs, or <c>0</c> for an effect that does not draw through the shader-instance buffer.
    /// </summary>
    /// <remarks>
    /// The frame capacity simulation uses this with <see cref="InstanceStride"/> to reserve the effect's
    /// records. A custom shader effect records one fullscreen pass; a built-in multi-pass effect reports
    /// one record per pass so its instances fit the frame's instance storage buffer.
    /// </remarks>
    internal virtual int InstanceRecordCount => 0;

    /// <summary>
    /// Adds this effect's engine-asset holds to the frame's retention set. Called on the tick thread
    /// after the camera captured the effect.
    /// </summary>
    /// <param name="retention">The recording frame's retention set.</param>
    internal virtual void Retain(FrameAssetRetention retention) {
    }

    /// <summary>
    /// Releases the references the snapshot holds until a later camera or the pass reset replaces it.
    /// </summary>
    internal virtual void Reset() {
    }

    /// <summary>
    /// Records this effect's passes into the current post-process chain.
    /// </summary>
    /// <param name="context">The per-camera-pass post-process context.</param>
    internal abstract void Run(PostProcessContext context);
}
