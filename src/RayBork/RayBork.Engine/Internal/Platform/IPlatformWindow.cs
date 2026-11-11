namespace RayBork.Internal;

/// <summary>
/// The platform window: the public snapshot surface plus the main-thread command and tick-boundary
/// hooks that the host uses.
/// </summary>
internal interface IPlatformWindow : IWindow {
    /// <summary>
    /// Applies queued window commands and refreshes the window snapshots. Runs on the main thread.
    /// </summary>
    void ApplyPendingCommands();

    /// <summary>
    /// Starts a new tick snapshot and resets the per-tick window flags, including
    /// <see cref="IWindow.WasResized"/>. Runs on the tick thread.
    /// </summary>
    void BeginTick();
}
