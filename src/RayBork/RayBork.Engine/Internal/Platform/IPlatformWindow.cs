namespace RayBork.Internal;

/// <summary>The platform window: snapshot getters plus main-thread and tick hooks.</summary>
internal interface IPlatformWindow : IWindow
{
    /// <summary>Applies queued window commands and refreshes snapshots.</summary>
    void ApplyPendingCommands();

    /// <summary>Starts a tick snapshot and clears <see cref="IWindow.WasResized"/>.</summary>
    void BeginTick();
}
