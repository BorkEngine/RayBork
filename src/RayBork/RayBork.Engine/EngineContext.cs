using Microsoft.Extensions.Logging;
using RayBork.Internal;

namespace RayBork;

/// <summary>
/// The M1 subset of the engine context: the window, input, logging, frame clock, and exit control.
/// </summary>
/// <remarks>
/// The context is stable for the app lifetime, but its per-frame members are tick-thread snapshots.
/// Read them from <see cref="IGame.Setup"/>, <see cref="IGame.Tick"/>, or <see cref="IGame.Shutdown"/>
/// instead of caching them across frames.
/// </remarks>
public sealed class EngineContext {
    private readonly IAppControl _appControl;
    private readonly Func<double> _elapsedTime;
    private readonly IEnginePlatform _platform;
    private bool _hasFrame;
    private double _time;

    internal EngineContext(
        IEnginePlatform platform,
        ILoggerFactory loggerFactory,
        IAppControl appControl,
        IInput initialInput,
        double maxDeltaTime,
        Func<double> elapsedTime) {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(appControl);
        ArgumentNullException.ThrowIfNull(initialInput);
        ArgumentNullException.ThrowIfNull(elapsedTime);

        _platform = platform;
        _appControl = appControl;
        _elapsedTime = elapsedTime;
        Input = initialInput;
        LoggerFactory = loggerFactory;
        MaxDeltaTime = maxDeltaTime;
    }

    /// <summary>
    /// Gets the input snapshot for the current tick. Read it on the tick thread.
    /// </summary>
    public IInput Input { get; private set; }

    /// <summary>
    /// Gets the application window. Getters are snapshots and setters are queued, so this member is
    /// safe to use from any thread.
    /// </summary>
    public IWindow Window => _platform.Window;

    /// <summary>
    /// Gets the app-owned logger factory. It is never <see langword="null"/> and is safe to use from
    /// any thread.
    /// </summary>
    public ILoggerFactory LoggerFactory { get; }

    /// <summary>
    /// Gets absolute monotonic seconds since the app started. During <see cref="IGame.Setup"/> this is
    /// the live elapsed time; during ticks it is the value captured at the start of the tick. It is
    /// not reset at the first tick. Read it on the tick thread.
    /// </summary>
    public double Time => _hasFrame ? _time : _elapsedTime();

    /// <summary>
    /// Gets the clamped seconds since the previous tick. It is <c>0</c> in
    /// <see cref="IGame.Setup"/> and on the first tick. Read it on the tick thread.
    /// </summary>
    public double DeltaTime { get; private set; }

    /// <summary>
    /// Gets the unclamped seconds since the previous tick. It is <c>0</c> in
    /// <see cref="IGame.Setup"/>. Read it on the tick thread.
    /// </summary>
    public double UnscaledDeltaTime { get; private set; }

    /// <summary>
    /// Gets the number of completed ticks. It is <c>0</c> in <see cref="IGame.Setup"/> and <c>1</c>
    /// during the first <see cref="IGame.Tick"/>. Read it on the tick thread.
    /// </summary>
    public long FrameCount { get; private set; }

    /// <summary>
    /// Gets the smoothed number of ticks per second. Read it on the tick thread.
    /// </summary>
    public double Fps { get; private set; }

    /// <summary>
    /// Gets whether the current tick is the first tick. It is <see langword="true"/> only during the
    /// first <see cref="IGame.Tick"/>. Read it on the tick thread.
    /// </summary>
    public bool IsFirstTick { get; private set; }

    /// <summary>
    /// Gets whether the app is exiting. It becomes <see langword="true"/> after
    /// <see cref="RequestExit"/>, a window close request, or an engine stop request. Read it on the
    /// tick thread.
    /// </summary>
    public bool IsExiting => _appControl.IsExitRequested;

    /// <summary>
    /// Gets the configured clamp for <see cref="DeltaTime"/>, in seconds. Read it on the tick thread.
    /// </summary>
    public double MaxDeltaTime { get; }

    /// <summary>
    /// Requests a graceful exit. The current tick completes and the final frame is presented. Call it
    /// on the tick thread.
    /// </summary>
    public void RequestExit() => _appControl.RequestExit();

    internal void SetInput(IInput input) {
        ArgumentNullException.ThrowIfNull(input);
        Input = input;
    }

    internal void SetFrame(in FrameTime frame) {
        _time = frame.Time;
        DeltaTime = frame.DeltaTime;
        UnscaledDeltaTime = frame.UnscaledDeltaTime;
        FrameCount = frame.FrameCount;
        Fps = frame.Fps;
        IsFirstTick = frame.IsFirstTick;
        _hasFrame = true;
    }
}
