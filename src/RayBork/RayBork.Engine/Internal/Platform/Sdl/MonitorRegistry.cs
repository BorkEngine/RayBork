namespace RayBork.Internal;

/// <summary>One display snapshot supplied by the platform.</summary>
internal readonly record struct MonitorInfo(
    uint DisplayId,
    string Name,
    Rect Bounds,
    Rect WorkArea,
    float DpiScale,
    int RefreshRate
);

/// <summary>Keeps the connected monitor snapshot with stable process-lifetime indices.</summary>
/// <remarks>
/// Indices are allocated once and never reused; snapshot arrays are replaced, never mutated. The
/// registry is main-thread only.
/// </remarks>
internal sealed class MonitorRegistry
{
    private const float DEFAULT_DPI_SCALE = 1f;

    private readonly Dictionary<uint, int> _stableIndices = [];
    private readonly Dictionary<uint, Monitor> _byDisplayId = [];
    private Monitor[] _monitors = [];
    private uint _primaryDisplayId;
    private int _nextIndex;

    /// <summary>Gets the snapshot of monitors ordered by stable index.</summary>
    internal IReadOnlyList<Monitor> Monitors => _monitors;

    /// <summary>Gets the number of connected monitors.</summary>
    internal int Count => _monitors.Length;

    /// <summary>Gets the SDL3 display id reported as primary.</summary>
    internal uint PrimaryDisplayId => _primaryDisplayId;

    /// <summary>Replaces the snapshot and allocates indices for new display ids.</summary>
    internal void Update(IReadOnlyList<MonitorInfo> monitors, uint primaryDisplayId)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        _primaryDisplayId = primaryDisplayId;

        List<(uint DisplayId, Monitor Monitor)> entries = new(monitors.Count);
        for (int i = 0; i < monitors.Count; i++)
        {
            MonitorInfo info = monitors[i];
            if (!_stableIndices.TryGetValue(info.DisplayId, out int index))
            {
                index = _nextIndex++;
                _stableIndices.Add(info.DisplayId, index);
            }

            entries.Add(
                (
                    info.DisplayId,
                    new Monitor(
                        index,
                        info.Name,
                        info.Bounds,
                        info.WorkArea,
                        info.DpiScale,
                        info.RefreshRate
                    )
                )
            );
        }

        entries.Sort(static (left, right) => left.Monitor.Index.CompareTo(right.Monitor.Index));
        Monitor[] next = new Monitor[entries.Count];
        _byDisplayId.Clear();
        for (int i = 0; i < entries.Count; i++)
        {
            (uint displayId, Monitor monitor) = entries[i];
            next[i] = monitor;
            if (!_byDisplayId.TryAdd(displayId, monitor))
            {
                throw new InvalidOperationException(
                    $"The monitor list contains duplicate display id {displayId}."
                );
            }
        }

        _monitors = next;
    }

    /// <summary>Gets a monitor by display id, falling back to primary then first.</summary>
    internal Monitor GetCurrent(uint displayId)
    {
        if (displayId != 0 && _byDisplayId.TryGetValue(displayId, out Monitor monitor))
        {
            return monitor;
        }

        if (_primaryDisplayId != 0 && _byDisplayId.TryGetValue(_primaryDisplayId, out monitor))
        {
            return monitor;
        }

        return _monitors.Length > 0
            ? _monitors[0]
            : new Monitor(0, string.Empty, default, default, DEFAULT_DPI_SCALE, 0);
    }

    /// <summary>Gets whether a snapshot contains a stable index; any-thread helper.</summary>
    /// <remarks>Indices can have gaps, so the snapshot count is not an upper bound.</remarks>
    internal static bool ContainsMonitorIndex(IReadOnlyList<Monitor> monitors, int monitorIndex)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        for (int i = 0; i < monitors.Count; i++)
        {
            if (monitors[i].Index == monitorIndex)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Looks up the SDL3 display id for a stable monitor index.</summary>
    internal bool TryGetDisplayId(int monitorIndex, out uint displayId)
    {
        foreach (KeyValuePair<uint, Monitor> pair in _byDisplayId)
        {
            if (pair.Value.Index == monitorIndex)
            {
                displayId = pair.Key;
                return true;
            }
        }

        displayId = 0;
        return false;
    }
}
