namespace RayBork.Tests;

/// <summary>
/// A deterministic <see cref="TimeProvider"/> whose timestamp moves only when a test advances it.
/// The frequency is <see cref="TimeSpan.TicksPerSecond"/>, so elapsed time round-trips exactly
/// through <see cref="TimeSpan"/> and tests do not depend on wall-clock scheduling.
/// </summary>
/// <remarks>
/// <see cref="TimeProvider.GetElapsedTime(long)"/> is not virtual and is derived from
/// <see cref="TimestampFrequency"/> and <see cref="GetTimestamp"/>, so the inherited implementation
/// is already consistent with the manual timestamps.
/// </remarks>
internal sealed class ManualTimeProvider : TimeProvider {
    private const long TIMESTAMP_FREQUENCY = TimeSpan.TicksPerSecond;

    private long _timestamp;

    public override long TimestampFrequency => TIMESTAMP_FREQUENCY;

    /// <summary>
    /// Gets or sets the current timestamp in <see cref="TimestampFrequency"/> units.
    /// </summary>
    public long Timestamp {
        get => _timestamp;
        set => _timestamp = value;
    }

    /// <summary>
    /// Moves the timestamp forward by <paramref name="elapsed"/>.
    /// </summary>
    public void Advance(TimeSpan elapsed) => _timestamp += elapsed.Ticks;

    public override long GetTimestamp() => _timestamp;
}
