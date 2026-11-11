using RayBork.Internal;

namespace RayBork.Tests;

/// <summary>
/// A fake <see cref="IEnginePlatform"/> that records lifecycle calls and exposes deterministic hooks
/// for pumping, input capture, and startup failure.
/// </summary>
internal sealed class FakeEnginePlatform : IEnginePlatform {
    private Action? _onExitRequested;

    internal FakeEnginePlatform() {
        FakeWindow = new FakePlatformWindow(RaiseExitRequested);
    }

    public FakePlatformWindow FakeWindow { get; }

    public IPlatformWindow Window => FakeWindow;

    public PlatformOptions? Options { get; private set; }

    public int InitializeCount { get; private set; }

    public int PumpCount { get; private set; }

    public int CaptureInputCount { get; private set; }

    public int DisposeCount { get; private set; }

    public bool IsDisposed => DisposeCount > 0;

    public Exception? InitializeException { get; set; }

    public Exception? CaptureInputException { get; set; }

    public Action? InitializeHook { get; set; }

    public Action<FakeEnginePlatform>? PumpHook { get; set; }

    public List<VibrationCall> VibrationCalls { get; } = [];

    public void Initialize(PlatformOptions options, Action onExitRequested) {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(onExitRequested);

        InitializeCount++;
        Options = options;
        _onExitRequested = onExitRequested;
        FakeWindow.ApplyStartupOptions(options);
        InitializeHook?.Invoke();

        if (InitializeException is not null) {
            throw InitializeException;
        }
    }

    public void Pump(TimeSpan maxWait) {
        PumpCount++;
        PumpHook?.Invoke(this);
        FakeWindow.ApplyPendingCommands();
    }

    public IInput CaptureInput() {
        CaptureInputCount++;
        if (CaptureInputException is not null) {
            throw CaptureInputException;
        }

        return new FakeInput();
    }

    public void SetGamepadVibration(int instanceId, float lowFrequency, float highFrequency, TimeSpan duration) {
        VibrationCalls.Add(new VibrationCall(instanceId, lowFrequency, highFrequency, duration, Stop: false));
    }

    public void StopGamepadVibration(int instanceId) {
        VibrationCalls.Add(new VibrationCall(instanceId, 0f, 0f, TimeSpan.Zero, Stop: true));
    }

    public void Dispose() => DisposeCount++;

    internal void RaiseExitRequested() => _onExitRequested?.Invoke();

    internal readonly record struct VibrationCall(
        int InstanceId,
        float LowFrequency,
        float HighFrequency,
        TimeSpan Duration,
        bool Stop);
}
