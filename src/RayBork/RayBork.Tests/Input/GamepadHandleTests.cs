using RayBork.Internal;

namespace RayBork.Tests;

public sealed class GamepadHandleTests
{
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void VibrationClampsIntensitiesAndQueuesTheRequest()
    {
        TestGamepadVibrationSink sink = new();
        InputState state = new(sink);
        state.ConnectGamepad(
            7,
            "Pad",
            new bool[InputMappings.GAMEPAD_BUTTON_COUNT],
            new float[InputMappings.GAMEPAD_AXIS_SLOT_COUNT]
        );
        GamepadHandle pad = Assert.IsType<GamepadHandle>(Assert.Single(state.Capture().Gamepads));

        pad.SetVibration(-1f, 2f, 1.5);

        TestGamepadVibrationSink.VibrationCall call = Assert.Single(sink.Calls);
        Assert.Equal(7, call.InstanceId);
        Assert.Equal(0f, call.LowFrequency);
        Assert.Equal(1f, call.HighFrequency);
        Assert.Equal(TimeSpan.FromSeconds(1.5), call.Duration);
        Assert.False(call.Stop);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void VibrationRejectsInvalidDurations()
    {
        InputState state = new(new TestGamepadVibrationSink());
        state.ConnectGamepad(
            7,
            "Pad",
            new bool[InputMappings.GAMEPAD_BUTTON_COUNT],
            new float[InputMappings.GAMEPAD_AXIS_SLOT_COUNT]
        );
        IGamepad pad = Assert.Single(state.Capture().Gamepads);

        Assert.Throws<ArgumentOutOfRangeException>(() => pad.SetVibration(0f, 0f, -0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => pad.SetVibration(0f, 0f, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            pad.SetVibration(0f, 0f, double.PositiveInfinity)
        );
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DisconnectedGamepadIgnoresVibrationAndStops()
    {
        TestGamepadVibrationSink sink = new();
        GamepadHandle pad = new(11, sink);

        Assert.False(pad.IsConnected);
        pad.SetVibration(0.5f, 0.5f, 1.0);
        pad.StopVibration();

        Assert.Empty(sink.Calls);
        Assert.Throws<ArgumentOutOfRangeException>(() => pad.SetVibration(0f, 0f, -1.0));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void VibrationSubstitutesZeroForNonFiniteIntensitiesAndConvertsDurations()
    {
        TestGamepadVibrationSink sink = new();
        InputState state = new(sink);
        state.ConnectGamepad(
            9,
            "Pad",
            new bool[InputMappings.GAMEPAD_BUTTON_COUNT],
            new float[InputMappings.GAMEPAD_AXIS_SLOT_COUNT]
        );
        IGamepad pad = Assert.Single(state.Capture().Gamepads);

        pad.SetVibration(float.NaN, float.PositiveInfinity, 0.25);
        pad.SetVibration(0f, 0f, double.MaxValue);

        Assert.Equal(2, sink.Calls.Count);
        Assert.Equal(0f, sink.Calls[0].LowFrequency);
        Assert.Equal(1f, sink.Calls[0].HighFrequency);
        Assert.Equal(TimeSpan.FromSeconds(0.25), sink.Calls[0].Duration);
        Assert.Equal(TimeSpan.MaxValue, sink.Calls[1].Duration);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RumbleIsIgnoredWhileDisconnectedAndResumesAfterReconnect()
    {
        TestGamepadVibrationSink sink = new();
        InputState state = new(sink);
        state.ConnectGamepad(
            12,
            "Pad",
            new bool[InputMappings.GAMEPAD_BUTTON_COUNT],
            new float[InputMappings.GAMEPAD_AXIS_SLOT_COUNT]
        );
        IGamepad pad = Assert.Single(state.Capture().Gamepads);

        pad.SetVibration(1f, 1f, 1.0);
        Assert.Single(sink.Calls);

        state.DisconnectGamepad(12);
        state.Capture();
        Assert.False(pad.IsConnected);
        pad.SetVibration(1f, 1f, 1.0);
        pad.StopVibration();
        Assert.Single(sink.Calls);

        state.ConnectGamepad(
            12,
            "Pad",
            new bool[InputMappings.GAMEPAD_BUTTON_COUNT],
            new float[InputMappings.GAMEPAD_AXIS_SLOT_COUNT]
        );
        IGamepad reconnected = Assert.Single(state.Capture().Gamepads);
        Assert.Same(pad, reconnected);
        Assert.True(reconnected.IsConnected);
        reconnected.StopVibration();

        Assert.Equal(2, sink.Calls.Count);
        Assert.True(sink.Calls[1].Stop);
        Assert.Equal(12, sink.Calls[1].InstanceId);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void StopVibrationQueuesAStopRequest()
    {
        TestGamepadVibrationSink sink = new();
        InputState state = new(sink);
        state.ConnectGamepad(
            8,
            "Pad",
            new bool[InputMappings.GAMEPAD_BUTTON_COUNT],
            new float[InputMappings.GAMEPAD_AXIS_SLOT_COUNT]
        );
        IGamepad pad = Assert.Single(state.Capture().Gamepads);

        pad.StopVibration();

        TestGamepadVibrationSink.VibrationCall call = Assert.Single(sink.Calls);
        Assert.True(call.Stop);
        Assert.Equal(8, call.InstanceId);
    }
}
