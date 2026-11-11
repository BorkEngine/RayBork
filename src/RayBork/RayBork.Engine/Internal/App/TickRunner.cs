namespace RayBork.Internal;

/// <summary>
/// Owns the tick thread: it runs the <see cref="IGame"/> callbacks, advances the clock, captures
/// input, and produces frames for the <see cref="FrameRing"/>.
/// </summary>
/// <remarks>
/// The app creates one runner per run. The constructor runs on the main thread, <see cref="Start"/>
/// starts the tick thread, and <see cref="Stop"/> may be called from any thread. The thread records
/// failures in <see cref="Failure"/> instead of letting them escape, so an abandoned tick thread can
/// never crash the process. Read <see cref="Failure"/> after <see cref="Join"/>.
/// </remarks>
internal sealed class TickRunner {
    private const string THREAD_NAME = "RayBork.Tick";
    private const int UNCAPPED_WAIT_MILLISECONDS = 1;

    private readonly IEnginePlatform _platform;
    private readonly EngineContext _context;
    private readonly IGame _game;
    private readonly EngineClock _clock;
    private readonly TimeProvider _timeProvider;
    private readonly TickPacer _pacer;
    private readonly FrameRing _frameRing;
    private readonly CancellationTokenSource _stopSource = new();
    private readonly ManualResetEventSlim _stopSignal = new(false);
    private readonly Thread _thread;

    private volatile Exception? _failure;
    private volatile bool _completed;
    private int _threadId;
    private int _started;

    /// <summary>
    /// Initializes the runner without starting the tick thread.
    /// </summary>
    /// <param name="platform">The initialized platform.</param>
    /// <param name="context">The app-lifetime engine context.</param>
    /// <param name="game">The game callbacks.</param>
    /// <param name="clock">The started clock.</param>
    /// <param name="timeProvider">The time provider that measures how long each tick takes.</param>
    /// <param name="pacer">The tick pacer.</param>
    /// <param name="frameRing">The frame ring producer side.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    internal TickRunner(
        IEnginePlatform platform,
        EngineContext context,
        IGame game,
        EngineClock clock,
        TimeProvider timeProvider,
        TickPacer pacer,
        FrameRing frameRing) {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(pacer);
        ArgumentNullException.ThrowIfNull(frameRing);

        _platform = platform;
        _context = context;
        _game = game;
        _clock = clock;
        _timeProvider = timeProvider;
        _pacer = pacer;
        _frameRing = frameRing;
        _thread = new Thread(RunThread) {
            IsBackground = true,
            Name = THREAD_NAME,
        };
    }

    /// <summary>
    /// Gets the tick thread. It is created by the constructor and runs after <see cref="Start"/>.
    /// </summary>
    internal Thread Thread => _thread;

    /// <summary>
    /// Gets whether the tick thread has finished. It becomes <see langword="true"/> after
    /// <see cref="IGame.Shutdown"/> and the synchronization-context disposal complete.
    /// </summary>
    internal bool IsCompleted => _completed;

    /// <summary>
    /// Gets the failure that stopped the tick loop, or <see langword="null"/>. It is either a
    /// <see cref="GameException"/> for a callback failure or another engine exception for an
    /// unexpected failure.
    /// </summary>
    internal Exception? Failure => _failure;

    /// <summary>
    /// Gets whether the calling thread is the tick thread.
    /// </summary>
    internal bool IsOnTickThread => _threadId != 0 && _threadId == Environment.CurrentManagedThreadId;

    /// <summary>
    /// Starts the tick thread. The runner can be started once.
    /// </summary>
    /// <exception cref="InvalidOperationException">The runner was already started.</exception>
    internal void Start() {
        if (Interlocked.Exchange(ref _started, 1) != 0) {
            throw new InvalidOperationException("The tick runner has already been started.");
        }

        _thread.Start();
    }

    /// <summary>
    /// Asks the tick thread to stop after the current tick. Idempotent and safe from any thread.
    /// </summary>
    internal void Stop() {
        _stopSignal.Set();
        _stopSource.Cancel();
    }

    /// <summary>
    /// Blocks until the tick thread exits.
    /// </summary>
    internal void Join() => _thread.Join();

    private void RunThread() {
        _threadId = Environment.CurrentManagedThreadId;
        SynchronizationContext? previousContext = SynchronizationContext.Current;
        TickSynchronizationContext synchronizationContext = new();
        SynchronizationContext.SetSynchronizationContext(synchronizationContext);

        try {
            RunCallbacks(synchronizationContext);
        } catch (Exception exception) {
            _failure ??= WrapUnexpectedFailure(exception);
        } finally {
            try {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            } catch (Exception exception) {
                _failure ??= WrapUnexpectedFailure(exception);
            }

            try {
                synchronizationContext.Dispose();
            } catch (Exception exception) {
                _failure ??= WrapUnexpectedFailure(exception);
            }

            _completed = true;
        }
    }

    private void RunCallbacks(TickSynchronizationContext synchronizationContext) {
        try {
            _context.SetInput(CaptureInput());
        } catch (Exception exception) {
            _failure = WrapPlatformFailure(exception);
            return;
        }

        bool setupCompleted = false;
        try {
            synchronizationContext.PumpUntilComplete(_game.Setup(_context));
            setupCompleted = true;
        } catch (Exception exception) {
            _failure = CreateGameException("Setup", exception);
        }

        if (!setupCompleted) {
            return;
        }

        TickFailure? tickFailure = null;
        if (!_stopSignal.IsSet && !_context.IsExiting) {
            try {
                tickFailure = RunTickLoop();
            } catch (Exception exception) {
                tickFailure = new TickFailure(WrapUnexpectedFailure(exception), IsGameCallback: false);
            }
        }

        Exception? shutdownFailure = null;
        try {
            synchronizationContext.PumpUntilComplete(_game.Shutdown());
        } catch (Exception exception) {
            shutdownFailure = exception;
        }

        _failure = CombineFailures(tickFailure, shutdownFailure);
    }

    private TickFailure? RunTickLoop() {
        CancellationToken stopToken = _stopSource.Token;
        while (true) {
            if (_stopSignal.IsSet || _context.IsExiting) {
                return null;
            }

            FrameLease? lease;
            try {
                lease = _frameRing.AcquireWritable(stopToken);
            } catch (OperationCanceledException) when (stopToken.IsCancellationRequested) {
                // A cancellation while waiting for a slot is a normal stop, not a failure.
                return null;
            }

            if (lease is null) {
                return null;
            }

            // UnscaledDeltaTime is the interval since the previous tick, not how long this tick has
            // been running, so it cannot drive pacing. Measure the current tick from its start.
            long tickStartedAt = _timeProvider.GetTimestamp();
            TickTime tick = _clock.BeginTick();

            try {
                IPlatformWindow window = _platform.Window;
                window.BeginTick();
                _context.SetInput(CaptureInput());
            } catch (Exception exception) {
                TryAbandon(lease.Value);
                return new TickFailure(WrapPlatformFailure(exception), IsGameCallback: false);
            }

            _context.SetFrame(new FrameTime(
                tick.Time,
                tick.DeltaTime,
                tick.UnscaledDeltaTime,
                tick.FrameCount,
                tick.Fps,
                tick.IsFirstTick));

            try {
                _game.Tick(_context);
            } catch (Exception exception) {
                TryAbandon(lease.Value);
                return new TickFailure(exception, IsGameCallback: true);
            }

            try {
                _frameRing.Publish(lease.Value);
            } catch (Exception exception) {
                TryAbandon(lease.Value);
                return new TickFailure(WrapRingFailure("publishing a frame", exception), IsGameCallback: false);
            }

            if (_stopSignal.IsSet || _context.IsExiting) {
                return null;
            }

            bool lowPower;
            try {
                IPlatformWindow window = _platform.Window;
                lowPower = window.IsMinimized || !window.IsFocused;
            } catch (Exception exception) {
                return new TickFailure(WrapPlatformFailure(exception), IsGameCallback: false);
            }

            TimeSpan wait = CalculateWait(_timeProvider, tickStartedAt, _pacer, lowPower);
            _stopSignal.Wait(wait > TimeSpan.Zero ? wait : TimeSpan.FromMilliseconds(UNCAPPED_WAIT_MILLISECONDS));
        }
    }

    /// <summary>
    /// Computes the remaining wait for the current tick from the time elapsed since
    /// <paramref name="tickStartedAt"/>.
    /// </summary>
    /// <param name="timeProvider">The time provider used by the engine clock.</param>
    /// <param name="tickStartedAt">The timestamp captured immediately before the tick started.</param>
    /// <param name="pacer">The tick pacer.</param>
    /// <param name="lowPower">Whether the window is minimized or unfocused.</param>
    /// <returns>The remaining tick interval, or zero when the tick already exceeded it.</returns>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    internal static TimeSpan CalculateWait(TimeProvider timeProvider, long tickStartedAt, TickPacer pacer, bool lowPower) {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(pacer);

        TimeSpan elapsed = timeProvider.GetElapsedTime(tickStartedAt);
        return pacer.GetWait(elapsed, lowPower);
    }

    private IInput CaptureInput() {
        IInput input = _platform.CaptureInput();
        return input ?? throw new PlatformException("The platform returned a null input snapshot.");
    }

    private void TryAbandon(FrameLease lease) {
        try {
            _frameRing.Abandon(lease);
        } catch (InvalidOperationException) {
            // The tick already failed; the ring self-check is diagnostic only during shutdown.
        }
    }

    private static Exception? CombineFailures(TickFailure? tickFailure, Exception? shutdownFailure) {
        if (tickFailure is null) {
            return shutdownFailure is null ? null : CreateGameException("Shutdown", shutdownFailure);
        }

        TickFailure tick = tickFailure.Value;
        if (shutdownFailure is null) {
            return tick.IsGameCallback ? CreateGameException("Tick", tick.Exception) : tick.Exception;
        }

        AggregateException aggregate = new(tick.Exception, shutdownFailure);
        if (tick.IsGameCallback) {
            return CreateGameException("Tick", aggregate);
        }

        return new PlatformException(
            "The tick loop failed and IGame.Shutdown also threw.",
            aggregate);
    }

    private static GameException CreateGameException(string callback, Exception exception) {
        return new GameException(
            $"IGame.{callback} threw an exception.",
            DiagnosticCodes.GAME_CALLBACK_FAILED,
            exception);
    }

    private static Exception WrapPlatformFailure(Exception exception) {
        return exception is RayBorkException
            ? exception
            : new PlatformException("A platform operation failed on the tick thread.", exception);
    }

    private static Exception WrapRingFailure(string operation, Exception exception) {
        return exception is RayBorkException
            ? exception
            : new RayBorkException($"The frame handoff failed while {operation}.", exception);
    }

    private static Exception WrapUnexpectedFailure(Exception exception) {
        return exception is RayBorkException
            ? exception
            : new RayBorkException("The tick thread failed unexpectedly.", exception);
    }

    private readonly record struct TickFailure(Exception Exception, bool IsGameCallback);
}
