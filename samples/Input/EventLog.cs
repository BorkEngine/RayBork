namespace Input;

/// <summary>
/// The sample's bounded log of the last <see cref="CAPACITY"/> user-visible events.
/// </summary>
/// <remarks>The log drops the oldest entry when full.</remarks>
internal sealed class EventLog
{
    /// <summary>The maximum number of retained events.</summary>
    internal const int CAPACITY = 10;

    private readonly List<string> _entries = new(CAPACITY);

    /// <summary>Gets the retained events, oldest first.</summary>
    internal IReadOnlyList<string> Entries => _entries;

    /// <summary>Adds one event and drops the oldest entry when full.</summary>
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
