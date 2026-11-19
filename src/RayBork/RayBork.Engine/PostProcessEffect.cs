using RayBork.Internal;

namespace RayBork;

/// <summary>
/// One camera post-process effect: an entry of a <see cref="PostProcessStack"/>.
/// </summary>
/// <remarks>
/// The constructor and capture member are internal, so games cannot derive their own effect type. A
/// camera snapshots the effect's <see cref="Enabled"/> value at creation, and <see cref="Enabled"/> is
/// tick-thread only; an effect owns no engine object and never disposes its shader.
/// </remarks>
public abstract class PostProcessEffect
{
    private readonly List<PostProcessEffectSnapshot> _snapshotPool = [];
    private bool _enabled = true;

    private protected PostProcessEffect() { }

    /// <summary>Gets or sets whether this effect runs; enabled by default.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            TickThread.RequireCurrent(nameof(Enabled));
            _enabled = value;
        }
    }

    /// <summary>Captures this effect's run state for one camera pass.</summary>
    /// <remarks>Called on the tick thread when the camera is created, after validation.</remarks>
    internal abstract PostProcessEffectSnapshot Capture(PostProcessCaptureContext context);

    /// <summary>Takes a free pooled snapshot of <typeparamref name="T"/>, or <c>null</c>.</summary>
    internal T? TakeSnapshot<T>()
        where T : PostProcessEffectSnapshot
    {
        for (int index = _snapshotPool.Count - 1; index >= 0; index--)
        {
            if (_snapshotPool[index] is T snapshot)
            {
                _snapshotPool.RemoveAt(index);
                return snapshot;
            }
        }

        return null;
    }

    /// <summary>Returns a reset snapshot to this effect's pool.</summary>
    internal void ReturnSnapshot(PostProcessEffectSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _snapshotPool.Add(snapshot);
    }
}
