namespace RayBork.Internal;

/// <summary>
/// One display snapshot supplied by the platform. <see cref="DisplayId"/> is the SDL3 display id
/// used to keep the public index stable.
/// </summary>
internal readonly record struct MonitorInfo(
    uint DisplayId,
    string Name,
    Rect Bounds,
    Rect WorkArea,
    float DpiScale,
    int RefreshRate);

/// <summary>
/// Assigns stable process-lifetime monitor indices to SDL3 display ids and keeps the current
/// snapshot of the connected monitors. Main-thread only.
/// </summary>
/// <remarks>
/// An index is allocated the first time a display id is seen and is never reused: removing a display
/// removes it from <see cref="Monitors"/> but keeps its index reserved, so a reconnected display gets
/// its old index back. The snapshot arrays are replaced, never mutated, so a caller may hold one
/// across a refresh.
/// </remarks>
internal sealed class MonitorRegistry {
    private const float DEFAULT_DPI_SCALE = 1f;

    private readonly Dictionary<uint, int> _stableIndices = [];
    private readonly Dictionary<uint, Monitor> _byDisplayId = [];
    private Monitor[] _monitors = [];
    private uint _primaryDisplayId;
    private int _nextIndex;

    /// <summary>
    /// Gets the snapped monitors ordered by their stable index.
    /// </summary>
    internal IReadOnlyList<Monitor> Monitors => _monitors;

    /// <summary>
    /// Gets the number of connected monitors.
    /// </summary>
    internal int Count => _monitors.Length;

    /// <summary>
    /// Gets the SDL3 display id reported as primary by the platform.
    /// </summary>
    internal uint PrimaryDisplayId => _primaryDisplayId;

    /// <summary>
    /// Replaces the monitor snapshot, allocating stable indices for display ids that were not seen
    /// before.
    /// </summary>
    internal void Update(IReadOnlyList<MonitorInfo> monitors, uint primaryDisplayId) {
        ArgumentNullException.ThrowIfNull(monitors);
        _primaryDisplayId = primaryDisplayId;

        List<(uint DisplayId, Monitor Monitor)> entries = new(monitors.Count);
        for (int i = 0; i < monitors.Count; i++) {
            MonitorInfo info = monitors[i];
            if (!_stableIndices.TryGetValue(info.DisplayId, out int index)) {
                index = _nextIndex++;
                _stableIndices.Add(info.DisplayId, index);
            }

            entries.Add((info.DisplayId, new Monitor(index, info.Name, info.Bounds, info.WorkArea, info.DpiScale, info.RefreshRate)));
        }

        entries.Sort(static (left, right) => left.Monitor.Index.CompareTo(right.Monitor.Index));
        Monitor[] next = new Monitor[entries.Count];
        _byDisplayId.Clear();
        for (int i = 0; i < entries.Count; i++) {
            (uint displayId, Monitor monitor) = entries[i];
            next[i] = monitor;
            if (!_byDisplayId.TryAdd(displayId, monitor)) {
                throw new InvalidOperationException($"The monitor list contains duplicate display id {displayId}.");
            }
        }

        _monitors = next;
    }

    /// <summary>
    /// Gets the monitor for a display id, falling back to the primary monitor and then the first
    /// connected monitor.
    /// </summary>
    internal Monitor GetCurrent(uint displayId) {
        if (displayId != 0 && _byDisplayId.TryGetValue(displayId, out Monitor monitor)) {
            return monitor;
        }

        if (_primaryDisplayId != 0 && _byDisplayId.TryGetValue(_primaryDisplayId, out monitor)) {
            return monitor;
        }

        return _monitors.Length > 0
            ? _monitors[0]
            : new Monitor(0, string.Empty, default, default, DEFAULT_DPI_SCALE, 0);
    }

    /// <summary>
    /// Looks up the SDL3 display id for a stable monitor index.
    /// </summary>
    internal bool TryGetDisplayId(int monitorIndex, out uint displayId) {
        foreach (KeyValuePair<uint, Monitor> pair in _byDisplayId) {
            if (pair.Value.Index == monitorIndex) {
                displayId = pair.Key;
                return true;
            }
        }

        displayId = 0;
        return false;
    }
}
