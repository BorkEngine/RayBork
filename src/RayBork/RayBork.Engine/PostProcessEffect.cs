using RayBork.Internal;

namespace RayBork;

/// <summary>
/// One camera post-process effect: an entry of a <see cref="PostProcessStack"/> that the renderer runs
/// for a camera pass after its geometry.
/// </summary>
/// <remarks>
/// <para>
/// The type is an engine-fixed base: its constructor and capture member are internal, so a game cannot
/// derive its own effect type. A custom effect is a <see cref="ShaderPostProcessEffect"/> over a baked
/// shader; built-in effects such as SSAO and bloom are engine types added in later M8 tasks.
/// </para>
/// <para>
/// <see cref="Enabled"/> is read and written on the tick thread. A camera snapshots the stack, the
/// effects, and each effect's <see cref="Enabled"/> value when it is created, so a change made after
/// that affects only later cameras. An effect and its stack own no engine object and do not dispose the
/// effect's shader.
/// </para>
/// </remarks>
public abstract class PostProcessEffect {
    private bool _enabled = true;

    private protected PostProcessEffect() {
    }

    /// <summary>
    /// Gets or sets whether this effect runs. The default is <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// The setter is tick-thread only. A camera snapshots the value at creation; disabling the effect
    /// afterwards does not remove it from an already recorded frame.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The caller is not the tick thread.</exception>
    public bool Enabled {
        get => _enabled;
        set {
            TickThread.RequireCurrent(nameof(Enabled));
            _enabled = value;
        }
    }

    /// <summary>
    /// Captures this effect's run state for one camera pass. Called on the tick thread when the camera
    /// is created, after the public validation has passed.
    /// </summary>
    /// <param name="context">The target information the capture validates against.</param>
    /// <returns>The immutable render-thread snapshot the pass runs.</returns>
    /// <exception cref="ObjectDisposedException">The effect's shader or a bound texture was disposed.</exception>
    /// <exception cref="InvalidOperationException">
    /// The effect's shader violates the custom post-process contract, or the target cannot supply a
    /// source the shader declares.
    /// </exception>
    internal abstract PostProcessEffectSnapshot Capture(PostProcessCaptureContext context);
}
