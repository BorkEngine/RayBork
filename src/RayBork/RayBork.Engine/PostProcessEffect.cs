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
/// shader; <see cref="SsaoEffect"/> is the built-in SSAO effect, and bloom is a later M8 engine type.
/// </para>
/// <para>
/// <see cref="Enabled"/> is read and written on the tick thread. A camera snapshots the stack, the
/// effects, and each effect's <see cref="Enabled"/> value when it is created, so a change made after
/// that affects only later cameras. The snapshot is pooled by this effect and reused after the frame
/// that held it is released, so adding an effect to a stack does not allocate a new snapshot for every
/// camera. An effect and its stack own no engine object and do not dispose the effect's shader.
/// </para>
/// </remarks>
public abstract class PostProcessEffect {
    private readonly List<PostProcessEffectSnapshot> _snapshotPool = [];
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

    /// <summary>
    /// Takes a pooled snapshot of <typeparamref name="T"/> from this effect, or returns
    /// <see langword="null"/> when none is free. Called on the tick thread by a capture that reuses its
    /// snapshot object, uniform buffer, and texture array across cameras.
    /// </summary>
    /// <typeparam name="T">The concrete snapshot type the effect owns.</typeparam>
    /// <returns>A free snapshot to fill, or <see langword="null"/>.</returns>
    internal T? TakeSnapshot<T>() where T : PostProcessEffectSnapshot {
        for (int index = _snapshotPool.Count - 1; index >= 0; index--) {
            if (_snapshotPool[index] is T snapshot) {
                _snapshotPool.RemoveAt(index);
                return snapshot;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns a reset snapshot to this effect's pool. Called on the tick thread when the camera pass
    /// that held it is recycled.
    /// </summary>
    /// <param name="snapshot">The snapshot to pool.</param>
    internal void ReturnSnapshot(PostProcessEffectSnapshot snapshot) {
        ArgumentNullException.ThrowIfNull(snapshot);
        _snapshotPool.Add(snapshot);
    }
}
