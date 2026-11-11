using System.Runtime.CompilerServices;

namespace RayBork.Internal;

/// <summary>
/// The set of engine-asset holds owned by one frame slot.
/// </summary>
/// <remarks>
/// <para>
/// A frame slot stores at most one retention set. Recording a draw calls <see cref="Add"/>; the frame
/// ring calls <see cref="Release"/> once when the frame is released, superseded, or abandoned. The
/// set is thread-safe because the tick thread adds while the render thread may release the previous
/// frame's set concurrently.
/// </para>
/// <para>
/// <see cref="Add"/> is idempotent per target for the lifetime of the set: a target that is added
/// twice receives one <see cref="IAssetRetentionTarget.AddFrameHold"/> call and exactly one matching
/// release. Identity is reference identity, not the target's own equality. Adding after
/// <see cref="Release"/> is ignored, so a late add cannot leak a hold. <see cref="Release"/> is
/// idempotent and calls every target outside the set's lock, because a release may queue GPU
/// destruction through the <see cref="GpuAssetBridge"/>.
/// </para>
/// </remarks>
internal sealed class FrameAssetRetention {
    private readonly Lock _gate = new();
    private readonly HashSet<IAssetRetentionTarget> _targets = new(ReferenceComparer.Instance);

    private bool _released;

    /// <summary>
    /// Gets the number of distinct targets currently held by the set.
    /// </summary>
    internal int Count {
        get {
            lock (_gate) {
                return _targets.Count;
            }
        }
    }

    /// <summary>
    /// Gets whether <see cref="Release"/> already ran.
    /// </summary>
    internal bool IsReleased {
        get {
            lock (_gate) {
                return _released;
            }
        }
    }

    /// <summary>
    /// Adds one frame hold for <paramref name="target"/>, or does nothing when the target is already
    /// held or the set was already released.
    /// </summary>
    /// <param name="target">The asset to retain for this frame.</param>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is <c>null</c>.</exception>
    internal void Add(IAssetRetentionTarget target) {
        ArgumentNullException.ThrowIfNull(target);

        lock (_gate) {
            if (_released || !_targets.Add(target)) {
                return;
            }

            target.AddFrameHold();
        }
    }

    /// <summary>
    /// Releases every hold exactly once. Repeated calls are no-ops.
    /// </summary>
    /// <remarks>
    /// The target list is detached under the lock and released outside it, so a target that queues GPU
    /// destruction from <see cref="IAssetRetentionTarget.ReleaseFrameHold"/> cannot deadlock with a
    /// concurrent <see cref="Add"/>.
    /// </remarks>
    internal void Release() {
        IAssetRetentionTarget[] targets;
        lock (_gate) {
            if (_released) {
                return;
            }

            _released = true;
            targets = [.. _targets];
            _targets.Clear();
        }

        foreach (IAssetRetentionTarget target in targets) {
            target.ReleaseFrameHold();
        }
    }

    /// <summary>
    /// Compares retention targets by reference so an asset type that overrides equality cannot merge
    /// two distinct live objects into one hold.
    /// </summary>
    private sealed class ReferenceComparer : IEqualityComparer<IAssetRetentionTarget> {
        internal static readonly ReferenceComparer Instance = new();

        public bool Equals(IAssetRetentionTarget? x, IAssetRetentionTarget? y) => ReferenceEquals(x, y);

        public int GetHashCode(IAssetRetentionTarget target) => RuntimeHelpers.GetHashCode(target);
    }
}
