using RayBork.Internal;

namespace RayBork.Tests;

internal sealed class TestGamepadVibrationSink : IGamepadVibrationSink {
    public List<VibrationCall> Calls { get; } = [];

    public void SetGamepadVibration(int instanceId, float lowFrequency, float highFrequency, TimeSpan duration) {
        Calls.Add(new VibrationCall(instanceId, lowFrequency, highFrequency, duration, false));
    }

    public void StopGamepadVibration(int instanceId) {
        Calls.Add(new VibrationCall(instanceId, 0f, 0f, TimeSpan.Zero, true));
    }

    public readonly record struct VibrationCall(
        int InstanceId,
        float LowFrequency,
        float HighFrequency,
        TimeSpan Duration,
        bool Stop);
}
