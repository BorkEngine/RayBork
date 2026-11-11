namespace RayBork.Internal;

/// <summary>
/// App-level exit control shared between the window/main thread and the tick loop.
/// </summary>
internal interface IAppControl {
    /// <summary>
    /// Requests a graceful exit. Idempotent and safe to call from any thread.
    /// </summary>
    void RequestExit();

    /// <summary>
    /// Gets whether a graceful exit has been requested. Safe to read from any thread.
    /// </summary>
    bool IsExitRequested { get; }
}
