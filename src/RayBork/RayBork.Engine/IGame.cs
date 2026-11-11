namespace RayBork;

/// <summary>
/// Defines the game lifecycle callbacks that the engine runs on the tick thread.
/// </summary>
/// <remarks>
/// Every callback runs on the tick thread. <see cref="Setup"/> and <see cref="Shutdown"/> are awaited
/// to completion; their continuations resume on the tick thread unless
/// <see cref="Task.ConfigureAwait(bool)"/> opts out.
/// </remarks>
public interface IGame {
    /// <summary>
    /// Runs once before the first tick. This is where assets are loaded, shaders are configured, and
    /// render targets are created.
    /// </summary>
    /// <param name="context">The engine context for the app.</param>
    /// <returns>A task that completes when setup is finished.</returns>
    /// <remarks>
    /// The engine awaits this task. Exceptions are fatal and <see cref="Shutdown"/> is not called.
    /// </remarks>
    Task Setup(EngineContext context);

    /// <summary>
    /// Runs once per simulated frame.
    /// </summary>
    /// <param name="context">The engine context for the current frame.</param>
    /// <remarks>
    /// Must be synchronous and must not block. Exceptions are fatal; <see cref="Shutdown"/> is still
    /// called.
    /// </remarks>
    void Tick(EngineContext context);

    /// <summary>
    /// Runs once after the loop stops, including after a <see cref="Tick"/> exception.
    /// </summary>
    /// <returns>A task that completes when shutdown is finished.</returns>
    /// <remarks>
    /// The engine awaits this task. Exceptions are aggregated with any earlier failure.
    /// </remarks>
    Task Shutdown();
}
