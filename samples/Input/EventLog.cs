namespace Input;

/// <summary>
/// The sample's bounded event log: the last <see cref="CAPACITY"/> user-visible events in the order
/// they happened.
/// </summary>
/// <remarks>
/// The log is storage, not a diagnostic trace. It holds text commits, clipboard actions, window and
/// monitor changes, cursor changes, DPI and render-size changes, gamepad hotplug, and rumble
/// requests, and it drops the oldest entry when it reaches <see cref="CAPACITY"/> so it never grows
/// without bound.
/// </remarks>
internal sealed class EventLog
{
    /// <summary>The maximum number of retained events.</summary>
    internal const int CAPACITY = 10;

    private readonly List<string> _entries = new(CAPACITY);

    /// <summary>Gets the retained events, oldest first.</summary>
    internal IReadOnlyList<string> Entries => _entries;

    /// <summary>
    /// Adds one event and drops the oldest entry when the log is full.
    /// </summary>
    /// <param name="message">The user-visible event text.</param>
    internal void Add(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        _entries.Add(message);
        if (_entries.Count > CAPACITY)
        {
            _entries.RemoveAt(0);
        }
    }
}
