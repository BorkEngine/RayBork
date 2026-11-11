namespace RayBork.Internal;

/// <summary>
/// Computes the tick interval and the remaining wait for a target frame rate. The pacer is
/// immutable, does not track elapsed time, and can be shared between threads.
/// </summary>
internal sealed class TickPacer {
    /// <summary>
    /// The tick cap applied while the window is minimized or unfocused.
    /// </summary>
    public const int LOW_POWER_FPS = 30;

    private static readonly TimeSpan _lowPowerInterval = TimeSpan.FromSeconds(1.0 / LOW_POWER_FPS);

    private readonly int _targetFps;

    /// <summary>
    /// Initializes a pacer for <paramref name="targetFps"/>; zero means uncapped.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="targetFps"/> is negative.</exception>
    internal TickPacer(int targetFps) {
        ArgumentOutOfRangeException.ThrowIfNegative(targetFps);
        _targetFps = targetFps;
    }

    /// <summary>
    /// Gets the interval between ticks. A focused, uncapped pacer has a zero interval; a low-power
    /// pacer never ticks faster than <see cref="LOW_POWER_FPS"/>.
    /// </summary>
    internal TimeSpan GetInterval(bool lowPower) {
        TimeSpan interval = _targetFps > 0
            ? TimeSpan.FromSeconds(1.0 / _targetFps)
            : TimeSpan.Zero;

        if (lowPower && interval < _lowPowerInterval) {
            return _lowPowerInterval;
        }

        return interval;
    }

    /// <summary>
    /// Gets the non-negative time left in the current tick interval after <paramref name="elapsed"/>.
    /// </summary>
    internal TimeSpan GetWait(TimeSpan elapsed, bool lowPower) {
        TimeSpan remaining = GetInterval(lowPower) - elapsed;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
}
