using RayBork.Internal;

namespace RayBork.Tests;

public sealed class InputSnapshotTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SnapshotIsNotChangedByLaterEvents() {
        InputState state = new(new TestGamepadVibrationSink());
        state.HandleKeyDown(Key.Q, repeat: false);
        state.HandleMouseButton(MouseButton.Left, down: true);
        state.AppendText("held");

        InputSnapshot snapshot = state.Capture();
        state.HandleKeyUp(Key.Q);
        state.HandleMouseButton(MouseButton.Left, down: false);
        state.AppendText("later");
        state.Capture();

        Assert.True(snapshot.IsKeyDown(Key.Q));
        Assert.True(snapshot.IsKeyPressed(Key.Q));
        Assert.True(snapshot.IsMouseButtonDown(MouseButton.Left));
        Assert.Equal("held", snapshot.TextInput);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EmptySnapshotReportsNeutralValues() {
        InputState state = new(new TestGamepadVibrationSink());

        InputSnapshot snapshot = state.Capture();

        Assert.Equal(string.Empty, snapshot.TextInput);
        Assert.Equal(System.Numerics.Vector2.Zero, snapshot.MousePosition);
        Assert.Equal(System.Numerics.Vector2.Zero, snapshot.MouseDelta);
        Assert.Equal(0f, snapshot.MouseWheel);
        Assert.Empty(snapshot.Gamepads);
        Assert.Null(snapshot.GetGamepad(0));
        Assert.True(snapshot.IsKeyUp(Key.Unknown));
        Assert.False(snapshot.IsKeyPressed(Key.Unknown));
        Assert.True(snapshot.IsMouseButtonUp(MouseButton.Left));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GamepadListIsFixedForTheSnapshot() {
        InputState state = new(new TestGamepadVibrationSink());
        bool[] buttons = new bool[InputMappings.GAMEPAD_BUTTON_COUNT];
        float[] axes = new float[InputMappings.GAMEPAD_AXIS_SLOT_COUNT];
        state.ConnectGamepad(9, "First", buttons, axes);
        InputSnapshot firstSnapshot = state.Capture();

        state.ConnectGamepad(10, "Second", buttons, axes);
        state.Capture();

        Assert.Single(firstSnapshot.Gamepads);
        Assert.Equal(9, firstSnapshot.Gamepads[0].InstanceId);
        Assert.NotNull(firstSnapshot.GetGamepad(0));
        Assert.Null(firstSnapshot.GetGamepad(1));
    }
}
