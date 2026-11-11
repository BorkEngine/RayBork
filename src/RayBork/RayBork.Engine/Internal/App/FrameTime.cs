namespace RayBork.Internal;

/// <summary>
/// The clock snapshot that the tick runner publishes to <see cref="EngineContext"/> for one tick.
/// </summary>
/// <remarks>
/// The tick runner creates this on the tick thread at the start of a tick. It is a value snapshot;
/// later clock progress does not change it.
/// </remarks>
/// <param name="Time">Absolute seconds since the app started.</param>
/// <param name="DeltaTime">Clamped seconds since the previous tick.</param>
/// <param name="UnscaledDeltaTime">Unclamped seconds since the previous tick.</param>
/// <param name="FrameCount">The number of completed ticks, starting at one.</param>
/// <param name="Fps">The smoothed ticks per second.</param>
/// <param name="IsFirstTick">Whether this is the first tick.</param>
internal readonly record struct FrameTime(
    double Time,
    double DeltaTime,
    double UnscaledDeltaTime,
    long FrameCount,
    double Fps,
    bool IsFirstTick);
