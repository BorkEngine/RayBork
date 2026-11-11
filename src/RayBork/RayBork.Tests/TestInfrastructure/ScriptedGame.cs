namespace RayBork.Tests;

/// <summary>
/// A scripted <see cref="IGame"/> that records callback counts, thread ids, thread names, and the
/// first-tick engine context snapshot.
/// </summary>
internal sealed class ScriptedGame : IGame {
    public Func<EngineContext, Task>? SetupHandler { get; set; }

    public Action<EngineContext>? TickHandler { get; set; }

    public Func<Task>? ShutdownHandler { get; set; }

    public bool YieldInSetup { get; set; }

    public bool YieldInShutdown { get; set; }

    public int SetupCount { get; private set; }

    public int TickCount { get; private set; }

    public int ShutdownCount { get; private set; }

    public int SetupThreadId { get; private set; }

    public int SetupCompletionThreadId { get; private set; }

    public string? SetupThreadName { get; private set; }

    public int ShutdownThreadId { get; private set; }

    public int ShutdownCompletionThreadId { get; private set; }

    public string? ShutdownThreadName { get; private set; }

    public int FirstTickThreadId { get; private set; }

    public int LastTickThreadId { get; private set; }

    public EngineContext? SetupContext { get; private set; }

    public EngineContext? LastTickContext { get; private set; }

    public long FirstTickFrameCount { get; private set; }

    public double FirstTickDeltaTime { get; private set; }

    public double FirstTickTime { get; private set; }

    public bool FirstTickIsFirstTick { get; private set; }

    public double LastTickTime { get; private set; }

    public long LastTickFrameCount { get; private set; }

    public async Task Setup(EngineContext context) {
        SetupCount++;
        SetupThreadId = Environment.CurrentManagedThreadId;
        SetupThreadName = Thread.CurrentThread.Name;
        SetupContext = context;

        if (SetupHandler is not null) {
            await SetupHandler(context);
        }

        if (YieldInSetup) {
            await Task.Yield();
        }

        SetupCompletionThreadId = Environment.CurrentManagedThreadId;
    }

    public void Tick(EngineContext context) {
        TickCount++;
        LastTickThreadId = Environment.CurrentManagedThreadId;
        LastTickContext = context;
        LastTickTime = context.Time;
        LastTickFrameCount = context.FrameCount;

        if (TickCount == 1) {
            FirstTickThreadId = LastTickThreadId;
            FirstTickFrameCount = context.FrameCount;
            FirstTickDeltaTime = context.DeltaTime;
            FirstTickTime = context.Time;
            FirstTickIsFirstTick = context.IsFirstTick;
        }

        TickHandler?.Invoke(context);
    }

    public async Task Shutdown() {
        ShutdownCount++;
        ShutdownThreadId = Environment.CurrentManagedThreadId;
        ShutdownThreadName = Thread.CurrentThread.Name;

        if (ShutdownHandler is not null) {
            await ShutdownHandler();
        }

        if (YieldInShutdown) {
            await Task.Yield();
        }

        ShutdownCompletionThreadId = Environment.CurrentManagedThreadId;
    }
}
