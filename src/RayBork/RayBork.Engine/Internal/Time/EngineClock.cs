namespace RayBork.Internal;

/// <summary>
/// Produces frame timing from a <see cref="TimeProvider"/>. The clock is tick-thread affine and is
/// not thread-safe: <see cref="Start"/>, <see cref="BeginTick"/>, and <see cref="Time"/> must be
/// used from the owning tick thread. The clock does not allocate after construction.
/// </summary>
internal sealed class EngineClock {
    private const int FPS_WINDOW_CAPACITY = 2048;
    private const int FPS_WINDOW_MINIMUM_SAMPLES = 2;
    private const double FPS_WINDOW_SECONDS = 1.0;

    private readonly TimeProvider _timeProvider;
    private readonly double _maxDeltaTime;
    private readonly double[] _tickTimestamps = new double[FPS_WINDOW_CAPACITY];

    private long _startTimestamp;
    private long _lastTimestamp;
    private long _frameCount;
    private int _fpsHead;
    private int _fpsCount;
    private bool _started;

    /// <summary>
    /// Initializes a clock that measures elapsed time with <paramref name="timeProvider"/> and clamps
    /// each tick delta to <paramref name="maxDeltaTime"/> seconds.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="timeProvider"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="maxDeltaTime"/> is not finite or is not greater than zero.
    /// </exception>
    internal EngineClock(TimeProvider timeProvider, double maxDeltaTime) {
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (!double.IsFinite(maxDeltaTime) || maxDeltaTime <= 0.0) {
            throw new ArgumentOutOfRangeException(
                nameof(maxDeltaTime),
                maxDeltaTime,
                "The maximum delta time must be finite and greater than zero.");
        }

        _timeProvider = timeProvider;
        _maxDeltaTime = maxDeltaTime;
    }

    /// <summary>
    /// Gets the live elapsed seconds since <see cref="Start"/>, including time spent in asynchronous
    /// setup. Returns zero before the clock is started.
    /// </summary>
    internal double Time {
        get {
            if (!_started) {
                return 0.0;
            }
            return _timeProvider.GetElapsedTime(_startTimestamp).TotalSeconds;
        }
    }

    /// <summary>
    /// Records the start timestamp. A clock can be started exactly once.
    /// </summary>
    /// <exception cref="InvalidOperationException">The clock was already started.</exception>
    internal void Start() {
        if (_started) {
            throw new InvalidOperationException("The clock has already been started.");
        }

        _startTimestamp = _timeProvider.GetTimestamp();
        _lastTimestamp = _startTimestamp;
        _started = true;
    }

    /// <summary>
    /// Returns the timing for the next tick. The first tick has zero deltas and a zero FPS; later
    /// ticks measure the raw interval since the previous call.
    /// </summary>
    /// <exception cref="InvalidOperationException"><see cref="Start"/> has not been called.</exception>
    internal TickTime BeginTick() {
        if (!_started) {
            throw new InvalidOperationException("The clock must be started before the first tick.");
        }

        long timestamp = _timeProvider.GetTimestamp();
        double time = _timeProvider.GetElapsedTime(_startTimestamp, timestamp).TotalSeconds;
        double rawDeltaTime = _timeProvider.GetElapsedTime(_lastTimestamp, timestamp).TotalSeconds;
        _lastTimestamp = timestamp;
        _frameCount++;

        bool isFirstTick = _frameCount == 1;
        double unscaledDeltaTime = isFirstTick ? 0.0 : rawDeltaTime;
        double deltaTime = isFirstTick ? 0.0 : Math.Min(rawDeltaTime, _maxDeltaTime);
        double fps = UpdateFpsWindow(time);

        return new TickTime(time, deltaTime, unscaledDeltaTime, _frameCount, fps, isFirstTick);
    }

    private double UpdateFpsWindow(double timestamp) {
        if (_fpsCount == FPS_WINDOW_CAPACITY) {
            _tickTimestamps[_fpsHead] = timestamp;
            _fpsHead = (_fpsHead + 1) % FPS_WINDOW_CAPACITY;
        } else {
            _tickTimestamps[(_fpsHead + _fpsCount) % FPS_WINDOW_CAPACITY] = timestamp;
            _fpsCount++;
        }

        // Drop samples older than the window, but keep two so a single long stall still reports a
        // meaningful rate instead of dropping to zero.
        while (_fpsCount > FPS_WINDOW_MINIMUM_SAMPLES) {
            double oldest = _tickTimestamps[_fpsHead];
            if (timestamp - oldest <= FPS_WINDOW_SECONDS) {
                break;
            }
            _fpsHead = (_fpsHead + 1) % FPS_WINDOW_CAPACITY;
            _fpsCount--;
        }

        if (_fpsCount < FPS_WINDOW_MINIMUM_SAMPLES) {
            return 0.0;
        }

        double span = timestamp - _tickTimestamps[_fpsHead];
        return span > 0.0 ? (_fpsCount - 1) / span : 0.0;
    }
}
