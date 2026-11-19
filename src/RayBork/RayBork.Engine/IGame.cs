namespace RayBork;

/// <summary>Defines the game callbacks the engine runs on the tick thread.</summary>
/// <remarks>
/// Every callback runs on the tick thread. The engine awaits <see cref="Setup"/> and
/// <see cref="Shutdown"/>; continuations resume on the tick thread unless configured otherwise.
/// </remarks>
public interface IGame
{
    /// <summary>Runs once before the first tick to load assets and create render targets.</summary>
    /// <remarks>Exceptions are fatal and <see cref="Shutdown"/> is not called.</remarks>
    Task Setup(EngineContext context);

    /// <summary>Runs once per simulated frame.</summary>
    /// <remarks>
    /// Must be synchronous and must not block; exceptions are fatal and shutdown still runs.
    /// </remarks>
    void Tick(EngineContext context);

    /// <summary>Runs once after the loop stops, even after a <see cref="Tick"/> exception.</summary>
    /// <remarks>The engine awaits it and aggregates exceptions with any earlier failure.</remarks>
    Task Shutdown();
}
