namespace RayBork.Internal;

/// <summary>
/// A snapshot of the engine clock for one tick. The values are tick-thread affine and are not
/// updated after the tick that produced them.
/// </summary>
internal readonly record struct TickTime(
    double Time,
    double DeltaTime,
    double UnscaledDeltaTime,
    long FrameCount,
    double Fps,
    bool IsFirstTick);
