using System.Numerics;
using System.Runtime.InteropServices;
using RayBork.Internal;
using SDL3;

namespace RayBork.Tests;

[Collection("SDL")]
public sealed class SdlInputTests {
    private const uint GAMEPAD_INSTANCE_ID = 5;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void KeyEventsTranslateDownRepeatAndUp() {
        SdlInput input = new(new TestGamepadVibrationSink());

        input.HandleEvent(KeyboardEvent(SDL.Scancode.W, down: true, repeat: false));
        IInput pressed = input.Capture();
        Assert.True(pressed.IsKeyDown(Key.W));
        Assert.True(pressed.IsKeyPressed(Key.W));
        Assert.False(pressed.IsKeyPressedRepeat(Key.W));

        input.HandleEvent(KeyboardEvent(SDL.Scancode.W, down: true, repeat: true));
        IInput repeated = input.Capture();
        Assert.True(repeated.IsKeyPressedRepeat(Key.W));
        Assert.False(repeated.IsKeyPressed(Key.W));

        input.HandleEvent(KeyboardEvent(SDL.Scancode.W, down: false, repeat: false));
        IInput released = input.Capture();
        Assert.True(released.IsKeyReleased(Key.W));
        Assert.False(released.IsKeyDown(Key.W));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TextInputEventsAppendUtf16InOrder() {
        SdlInput input = new(new TestGamepadVibrationSink());
        IntPtr first = Marshal.StringToCoTaskMemUTF8("hé");
        IntPtr second = Marshal.StringToCoTaskMemUTF8("llo");
        try {
            input.HandleEvent(TextEvent(first));
            input.HandleEvent(TextEvent(second));
        } finally {
            Marshal.FreeCoTaskMem(first);
            Marshal.FreeCoTaskMem(second);
        }

        Assert.Equal("héllo", input.Capture().TextInput);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MouseEventsTranslatePositionDeltaButtonsAndWheel() {
        SdlInput input = new(new TestGamepadVibrationSink());

        input.HandleEvent(new SDL.Event {
            Type = (uint)SDL.EventType.MouseMotion,
            Motion = new SDL.MouseMotionEvent {
                Type = SDL.EventType.MouseMotion,
                X = 10f,
                Y = 20f,
                XRel = 1.5f,
                YRel = -2.5f,
            },
        });
        input.HandleEvent(MouseButtonEvent(1, down: true));
        input.HandleEvent(MouseButtonEvent(3, down: true));
        input.HandleEvent(new SDL.Event {
            Type = (uint)SDL.EventType.MouseWheel,
            Wheel = new SDL.MouseWheelEvent {
                Type = SDL.EventType.MouseWheel,
                Y = 1.25f,
            },
        });

        IInput snapshot = input.Capture();
        Assert.Equal(new Vector2(10f, 20f), snapshot.MousePosition);
        Assert.Equal(new Vector2(1.5f, -2.5f), snapshot.MouseDelta);
        Assert.Equal(1.25f, snapshot.MouseWheel);
        Assert.True(snapshot.IsMouseButtonDown(MouseButton.Left));
        Assert.True(snapshot.IsMouseButtonPressed(MouseButton.Left));
        Assert.True(snapshot.IsMouseButtonDown(MouseButton.Right));
        Assert.False(snapshot.IsMouseButtonDown(MouseButton.Middle));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UnsupportedMouseButtonsAreIgnored() {
        SdlInput input = new(new TestGamepadVibrationSink());

        input.HandleEvent(MouseButtonEvent(6, down: true));
        input.HandleEvent(MouseButtonEvent(0, down: true));

        IInput snapshot = input.Capture();
        Assert.False(snapshot.IsMouseButtonDown(MouseButton.Left));
        Assert.False(snapshot.IsMouseButtonDown(MouseButton.X2));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GamepadButtonAndAxisEventsTranslateToTheRawState() {
        SdlInput input = new(new TestGamepadVibrationSink());
        input.State.ConnectGamepad(
            GAMEPAD_INSTANCE_ID,
            "Pad",
            new bool[InputMappings.GAMEPAD_BUTTON_COUNT],
            new float[InputMappings.GAMEPAD_AXIS_SLOT_COUNT]);

        input.HandleEvent(GamepadButtonEvent((byte)SDL.GamepadButton.South, down: true));
        input.HandleEvent(GamepadAxisEvent(0, 32767));
        input.HandleEvent(GamepadAxisEvent(4, 32767));

        IInput snapshot = input.Capture();
        IGamepad pad = Assert.Single(snapshot.Gamepads);
        Assert.True(pad.IsButtonDown(GamepadButton.South));
        Assert.True(pad.IsButtonPressed(GamepadButton.South));
        Assert.Equal(1f, pad.GetAxis(GamepadAxis.LeftX));
        Assert.Equal(1f, pad.GetTrigger(GamepadTrigger.Left));

        input.HandleEvent(GamepadButtonEvent((byte)SDL.GamepadButton.South, down: false));
        IGamepad released = Assert.Single(input.Capture().Gamepads);
        Assert.True(released.IsButtonReleased(GamepadButton.South));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UnsupportedGamepadButtonsAreIgnored() {
        SdlInput input = new(new TestGamepadVibrationSink());
        input.State.ConnectGamepad(
            GAMEPAD_INSTANCE_ID,
            "Pad",
            new bool[InputMappings.GAMEPAD_BUTTON_COUNT],
            new float[InputMappings.GAMEPAD_AXIS_SLOT_COUNT]);

        input.HandleEvent(GamepadButtonEvent((byte)SDL.GamepadButton.RightPaddle1, down: true));
        input.HandleEvent(GamepadButtonEvent((byte)SDL.GamepadButton.Misc6, down: true));

        IGamepad pad = Assert.Single(input.Capture().Gamepads);
        Assert.All(Enum.GetValues<GamepadButton>(), button => Assert.False(pad.IsButtonDown(button)));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CaptureClearsEdgesButKeepsLevels() {
        SdlInput input = new(new TestGamepadVibrationSink());
        input.HandleEvent(KeyboardEvent(SDL.Scancode.Space, down: true, repeat: false));
        input.HandleEvent(MouseButtonEvent(1, down: true));

        input.Capture();
        IInput second = input.Capture();

        Assert.True(second.IsKeyDown(Key.Space));
        Assert.False(second.IsKeyPressed(Key.Space));
        Assert.True(second.IsMouseButtonDown(MouseButton.Left));
        Assert.False(second.IsMouseButtonPressed(MouseButton.Left));
    }

    private static SDL.Event KeyboardEvent(SDL.Scancode scancode, bool down, bool repeat) {
        SDL.EventType type = down ? SDL.EventType.KeyDown : SDL.EventType.KeyUp;
        return new SDL.Event {
            Type = (uint)type,
            Key = new SDL.KeyboardEvent {
                Type = type,
                Scancode = scancode,
                Down = down,
                Repeat = repeat,
            },
        };
    }

    private static SDL.Event TextEvent(IntPtr text) {
        return new SDL.Event {
            Type = (uint)SDL.EventType.TextInput,
            Text = new SDL.TextInputEvent {
                Type = SDL.EventType.TextInput,
                Text = text,
            },
        };
    }

    private static SDL.Event MouseButtonEvent(byte button, bool down) {
        SDL.EventType type = down ? SDL.EventType.MouseButtonDown : SDL.EventType.MouseButtonUp;
        return new SDL.Event {
            Type = (uint)type,
            Button = new SDL.MouseButtonEvent {
                Type = type,
                Button = button,
                Down = down,
            },
        };
    }

    private static SDL.Event GamepadButtonEvent(byte button, bool down) {
        SDL.EventType type = down ? SDL.EventType.GamepadButtonDown : SDL.EventType.GamepadButtonUp;
        return new SDL.Event {
            Type = (uint)type,
            GButton = new SDL.GamepadButtonEvent {
                Type = type,
                Which = GAMEPAD_INSTANCE_ID,
                Button = button,
                Down = down,
            },
        };
    }

    private static SDL.Event GamepadAxisEvent(byte axis, short value) {
        return new SDL.Event {
            Type = (uint)SDL.EventType.GamepadAxisMotion,
            GAxis = new SDL.GamepadAxisEvent {
                Type = SDL.EventType.GamepadAxisMotion,
                Which = GAMEPAD_INSTANCE_ID,
                Axis = axis,
                Value = value,
            },
        };
    }
}
