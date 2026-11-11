using RayBork.Internal;

namespace RayBork.Tests;

public sealed class InputStateTests {
    private const uint GAMEPAD_INSTANCE_ID = 42;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void KeyPressAndReleaseProduceOneTickEdges() {
        InputState state = new(new TestGamepadVibrationSink());
        state.HandleKeyDown(Key.W, repeat: false);

        InputSnapshot pressed = state.Capture();
        Assert.True(pressed.IsKeyDown(Key.W));
        Assert.True(pressed.IsKeyPressed(Key.W));
        Assert.False(pressed.IsKeyPressedRepeat(Key.W));
        Assert.False(pressed.IsKeyReleased(Key.W));
        Assert.False(pressed.IsKeyUp(Key.W));

        InputSnapshot held = state.Capture();
        Assert.True(held.IsKeyDown(Key.W));
        Assert.False(held.IsKeyPressed(Key.W));

        state.HandleKeyUp(Key.W);
        InputSnapshot released = state.Capture();
        Assert.False(released.IsKeyDown(Key.W));
        Assert.True(released.IsKeyReleased(Key.W));
        Assert.True(released.IsKeyUp(Key.W));

        InputSnapshot up = state.Capture();
        Assert.False(up.IsKeyReleased(Key.W));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RepeatEventsDoNotProduceFirstPressEdges() {
        InputState state = new(new TestGamepadVibrationSink());
        state.HandleKeyDown(Key.A, repeat: false);
        state.Capture();

        state.HandleKeyDown(Key.A, repeat: true);
        InputSnapshot repeat = state.Capture();

        Assert.True(repeat.IsKeyDown(Key.A));
        Assert.False(repeat.IsKeyPressed(Key.A));
        Assert.True(repeat.IsKeyPressedRepeat(Key.A));

        InputSnapshot afterRepeat = state.Capture();
        Assert.False(afterRepeat.IsKeyPressedRepeat(Key.A));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TextInputAppendsInEventOrderAndClearsEachTick() {
        InputState state = new(new TestGamepadVibrationSink());
        state.AppendText("Ray");
        state.AppendText("Bork");

        Assert.Equal("RayBork", state.Capture().TextInput);
        Assert.Equal(string.Empty, state.Capture().TextInput);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MouseMotionSetsPositionAndAccumulatesRelativeDelta() {
        InputState state = new(new TestGamepadVibrationSink());
        state.HandleMouseMotion(10f, 20f, 1.5f, -2.5f);
        state.HandleMouseMotion(12f, 18f, 0.5f, 0.5f);

        InputSnapshot first = state.Capture();
        Assert.Equal(new System.Numerics.Vector2(12f, 18f), first.MousePosition);
        Assert.Equal(new System.Numerics.Vector2(2f, -2f), first.MouseDelta);

        InputSnapshot second = state.Capture();
        Assert.Equal(System.Numerics.Vector2.Zero, second.MouseDelta);
        Assert.Equal(new System.Numerics.Vector2(12f, 18f), second.MousePosition);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MouseWheelSumsAndResetsEachTick() {
        InputState state = new(new TestGamepadVibrationSink());
        state.HandleMouseWheel(1.25f);
        state.HandleMouseWheel(-0.25f);

        Assert.Equal(1.0f, state.Capture().MouseWheel);
        Assert.Equal(0f, state.Capture().MouseWheel);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MouseButtonEdgesAndLevelsFollowTheButtonState() {
        InputState state = new(new TestGamepadVibrationSink());
        state.HandleMouseButton(MouseButton.Right, down: true);

        InputSnapshot pressed = state.Capture();
        Assert.True(pressed.IsMouseButtonDown(MouseButton.Right));
        Assert.True(pressed.IsMouseButtonPressed(MouseButton.Right));
        Assert.False(pressed.IsMouseButtonUp(MouseButton.Right));

        state.HandleMouseButton(MouseButton.Right, down: false);
        InputSnapshot released = state.Capture();
        Assert.False(released.IsMouseButtonDown(MouseButton.Right));
        Assert.True(released.IsMouseButtonReleased(MouseButton.Right));
        Assert.True(released.IsMouseButtonUp(MouseButton.Right));
        Assert.False(released.IsMouseButtonPressed(MouseButton.Right));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void FocusLossClearsKeyboardAndMouseLevelsAndPendingEdges() {
        InputState state = new(new TestGamepadVibrationSink());
        state.HandleKeyDown(Key.Space, repeat: false);
        state.HandleMouseButton(MouseButton.Left, down: true);
        state.Capture();

        state.HandleKeyDown(Key.Space, repeat: true);
        state.ResetLevels();

        InputSnapshot snapshot = state.Capture();
        Assert.True(snapshot.IsKeyUp(Key.Space));
        Assert.False(snapshot.IsKeyPressed(Key.Space));
        Assert.False(snapshot.IsKeyPressedRepeat(Key.Space));
        Assert.False(snapshot.IsKeyReleased(Key.Space));
        Assert.True(snapshot.IsMouseButtonUp(MouseButton.Left));
        Assert.False(snapshot.IsMouseButtonPressed(MouseButton.Left));
        Assert.False(snapshot.IsMouseButtonReleased(MouseButton.Left));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void InvalidKeyboardAndMouseEnumsThrow() {
        InputState state = new(new TestGamepadVibrationSink());
        InputSnapshot snapshot = state.Capture();

        Assert.Throws<ArgumentOutOfRangeException>(() => snapshot.IsKeyDown((Key)512));
        Assert.Throws<ArgumentOutOfRangeException>(() => snapshot.IsKeyPressed((Key)(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => snapshot.IsKeyPressedRepeat((Key)512));
        Assert.Throws<ArgumentOutOfRangeException>(() => snapshot.IsKeyReleased((Key)512));
        Assert.Throws<ArgumentOutOfRangeException>(() => snapshot.IsKeyUp((Key)512));
        Assert.Throws<ArgumentOutOfRangeException>(() => snapshot.IsMouseButtonDown((MouseButton)5));
        Assert.Throws<ArgumentOutOfRangeException>(() => snapshot.IsMouseButtonPressed((MouseButton)(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => snapshot.IsMouseButtonReleased((MouseButton)5));
        Assert.Throws<ArgumentOutOfRangeException>(() => snapshot.IsMouseButtonUp((MouseButton)5));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GamepadButtonsReportOneTickEdges() {
        InputState state = CreateStateWithGamepad();

        state.HandleGamepadButton(GAMEPAD_INSTANCE_ID, GamepadButton.South, down: true);
        InputSnapshot pressed = state.Capture();
        IGamepad pad = Assert.Single(pressed.Gamepads);
        Assert.True(pad.IsButtonDown(GamepadButton.South));
        Assert.True(pad.IsButtonPressed(GamepadButton.South));
        Assert.False(pad.IsButtonReleased(GamepadButton.South));

        InputSnapshot held = state.Capture();
        Assert.True(held.GetGamepad(0)!.IsButtonDown(GamepadButton.South));
        Assert.False(held.GetGamepad(0)!.IsButtonPressed(GamepadButton.South));

        state.HandleGamepadButton(GAMEPAD_INSTANCE_ID, GamepadButton.South, down: false);
        InputSnapshot released = state.Capture();
        Assert.False(released.GetGamepad(0)!.IsButtonDown(GamepadButton.South));
        Assert.True(released.GetGamepad(0)!.IsButtonReleased(GamepadButton.South));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void InitialGamepadPollDoesNotReportPressedEdges() {
        InputState state = new(new TestGamepadVibrationSink());
        bool[] buttons = new bool[InputMappings.GAMEPAD_BUTTON_COUNT];
        buttons[(int)GamepadButton.North] = true;
        float[] axes = new float[InputMappings.GAMEPAD_AXIS_SLOT_COUNT];
        axes[InputMappings.AxisSlot(GamepadAxis.RightX)] = 0.75f;

        state.ConnectGamepad(GAMEPAD_INSTANCE_ID, "Test Pad", buttons, axes);
        InputSnapshot snapshot = state.Capture();
        IGamepad pad = Assert.Single(snapshot.Gamepads);

        Assert.True(pad.IsButtonDown(GamepadButton.North));
        Assert.False(pad.IsButtonPressed(GamepadButton.North));
        Assert.Equal(0.75f, pad.GetAxis(GamepadAxis.RightX));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DisconnectedGamepadKeepsItsStableHandle() {
        InputState state = CreateStateWithGamepad();
        InputSnapshot first = state.Capture();
        IGamepad handle = Assert.Single(first.Gamepads);
        Assert.True(handle.IsConnected);
        Assert.Equal((int)GAMEPAD_INSTANCE_ID, handle.InstanceId);

        state.DisconnectGamepad(GAMEPAD_INSTANCE_ID);
        InputSnapshot second = state.Capture();

        Assert.False(handle.IsConnected);
        Assert.Empty(second.Gamepads);
        Assert.Null(second.GetGamepad(0));
        Assert.Null(second.GetGamepad(-1));

        bool[] buttons = new bool[InputMappings.GAMEPAD_BUTTON_COUNT];
        float[] axes = new float[InputMappings.GAMEPAD_AXIS_SLOT_COUNT];
        state.ConnectGamepad(GAMEPAD_INSTANCE_ID, "Test Pad", buttons, axes);
        IGamepad reconnected = Assert.Single(state.Capture().Gamepads);
        Assert.Same(handle, reconnected);
        Assert.True(handle.IsConnected);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GamepadIndicesFollowConnectedOrderAcrossHotplug() {
        InputState state = new(new TestGamepadVibrationSink());
        bool[] buttons = new bool[InputMappings.GAMEPAD_BUTTON_COUNT];
        float[] axes = new float[InputMappings.GAMEPAD_AXIS_SLOT_COUNT];
        state.ConnectGamepad(1, "First", buttons, axes);
        state.ConnectGamepad(2, "Second", buttons, axes);

        InputSnapshot both = state.Capture();
        Assert.Equal(2, both.Gamepads.Count);
        Assert.Equal(0, both.Gamepads[0].Index);
        Assert.Equal(1, both.Gamepads[1].Index);

        state.DisconnectGamepad(1);
        InputSnapshot secondOnly = state.Capture();
        IGamepad remaining = Assert.Single(secondOnly.Gamepads);
        Assert.Equal(2, remaining.InstanceId);
        Assert.Equal(0, remaining.Index);

        state.ConnectGamepad(1, "First", buttons, axes);
        InputSnapshot reconnected = state.Capture();
        Assert.Equal(2, reconnected.Gamepads.Count);
        Assert.Equal(2, reconnected.Gamepads[0].InstanceId);
        Assert.Equal(0, reconnected.Gamepads[0].Index);
        Assert.Equal(1, reconnected.Gamepads[1].InstanceId);
        Assert.Equal(1, reconnected.Gamepads[1].Index);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GamepadAxesAndTriggersClampAndValidate() {
        InputState state = CreateStateWithGamepad();
        state.HandleGamepadStickAxis(GAMEPAD_INSTANCE_ID, GamepadAxis.LeftY, 5f);
        state.HandleGamepadStickAxis(GAMEPAD_INSTANCE_ID, GamepadAxis.RightY, -5f);
        state.HandleGamepadTrigger(GAMEPAD_INSTANCE_ID, GamepadTrigger.Right, 5f);

        IGamepad pad = Assert.Single(state.Capture().Gamepads);
        Assert.Equal(1f, pad.GetAxis(GamepadAxis.LeftY));
        Assert.Equal(-1f, pad.GetAxis(GamepadAxis.RightY));
        Assert.Equal(1f, pad.GetTrigger(GamepadTrigger.Right));
        Assert.Equal(0f, pad.GetTrigger(GamepadTrigger.Left));

        Assert.Throws<ArgumentOutOfRangeException>(() => pad.GetAxis((GamepadAxis)4));
        Assert.Throws<ArgumentOutOfRangeException>(() => pad.GetTrigger((GamepadTrigger)2));
        Assert.Throws<ArgumentOutOfRangeException>(() => pad.IsButtonDown((GamepadButton)16));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.HandleGamepadButton(GAMEPAD_INSTANCE_ID, (GamepadButton)16, true));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GamepadEventsForUnknownDevicesAreIgnored() {
        InputState state = new(new TestGamepadVibrationSink());
        state.HandleGamepadButton(GAMEPAD_INSTANCE_ID, GamepadButton.South, down: true);
        state.HandleGamepadStickAxis(GAMEPAD_INSTANCE_ID, GamepadAxis.LeftX, 1f);

        Assert.Empty(state.Capture().Gamepads);
    }

    private static InputState CreateStateWithGamepad() {
        InputState state = new(new TestGamepadVibrationSink());
        bool[] buttons = new bool[InputMappings.GAMEPAD_BUTTON_COUNT];
        float[] axes = new float[InputMappings.GAMEPAD_AXIS_SLOT_COUNT];
        state.ConnectGamepad(GAMEPAD_INSTANCE_ID, "Test Pad", buttons, axes);
        return state;
    }
}
