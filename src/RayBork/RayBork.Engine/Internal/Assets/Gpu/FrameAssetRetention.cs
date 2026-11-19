using System.Runtime.CompilerServices;

namespace RayBork.Internal;

/// <summary>
/// The set of engine-asset holds owned by one frame slot.
/// </summary>
/// <remarks>
/// The tick thread adds while the render thread may release the previous frame's set, so the
/// set is thread-safe; holds are idempotent per target by reference identity, and a late add is
/// ignored. Targets are released outside the lock because a release may queue GPU destruction
/// through the <see cref="GpuAssetBridge"/>.
/// </remarks>
internal sealed class FrameAssetRetention
{
    private readonly Lock _gate = new();
    private readonly HashSet<IAssetRetentionTarget> _targets = new(ReferenceComparer.Instance);

    private bool _released;

    /// <summary>Gets the number of distinct targets currently held.</summary>
    internal int Count
    {
        get
        {
            lock (_gate)
            {
                return _targets.Count;
            }
        }
    }

    /// <summary>Gets whether <see cref="Release"/> already ran.</summary>
    internal bool IsReleased
    {
        get
        {
            lock (_gate)
            {
                return _released;
            }
        }
    }

    /// <summary>Adds one frame hold; a held or already released set is unchanged.</summary>
    /// <param name="target">The asset to retain for this frame.</param>
    internal void Add(IAssetRetentionTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        lock (_gate)
        {
            if (_released || !_targets.Add(target))
            {
                return;
            }

            target.AddFrameHold();
        }
    }

    /// <summary>Releases every hold exactly once; repeated calls are no-ops.</summary>
    /// <remarks>
    /// Targets are detached under the lock and released outside it, so a release that queues GPU
    /// destruction cannot deadlock with a concurrent <see cref="Add"/>.
    /// </remarks>
    internal void Release()
    {
        IAssetRetentionTarget[] targets;
        lock (_gate)
        {
            if (_released)
            {
                return;
            }

            _released = true;
            targets = [.. _targets];
            _targets.Clear();
        }

        foreach (IAssetRetentionTarget target in targets)
        {
            target.ReleaseFrameHold();
        }
    }

    /// <summary>Compares targets by reference, ignoring overridden equality.</summary>
    private sealed class ReferenceComparer : IEqualityComparer<IAssetRetentionTarget>
    {
        internal static readonly ReferenceComparer Instance = new();

        public bool Equals(IAssetRetentionTarget? x, IAssetRetentionTarget? y) =>
            ReferenceEquals(x, y);

        public int GetHashCode(IAssetRetentionTarget target) => RuntimeHelpers.GetHashCode(target);
    }
}
