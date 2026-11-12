# Input

[API overview](../api.md) · [document map](../api.md#document-map)

## 9. Input

```csharp
public interface IInput {
    bool IsKeyDown(Key key);
    bool IsKeyPressed(Key key);
    bool IsKeyPressedRepeat(Key key);
    bool IsKeyReleased(Key key);
    bool IsKeyUp(Key key);

    string TextInput { get; }

    Vector2 MousePosition { get; }
    Vector2 MouseDelta { get; }
    float MouseWheel { get; }

    bool IsMouseButtonDown(MouseButton button);
    bool IsMouseButtonPressed(MouseButton button);
    bool IsMouseButtonReleased(MouseButton button);
    bool IsMouseButtonUp(MouseButton button);

    IReadOnlyList<IGamepad> Gamepads { get; }
    IGamepad? GetGamepad(int index);
}

public enum MouseButton {
    Left,
    Middle,
    Right,
    X1,
    X2,
}
```

### 9.1 Keyboard and text

```csharp
public enum Key {
    A, B, C, D, E, F, G, H, I, J, K, L, M,
    N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    Escape, Tab, CapsLock, Space, Enter, Backspace, Delete, Insert,
    Home, End, PageUp, PageDown,
    Left, Right, Up, Down,
    LeftShift, RightShift, LeftControl, RightControl, LeftAlt, RightAlt, LeftSuper, RightSuper,
    NumLock, ScrollLock, PrintScreen, Pause, Menu,
    Minus, Equal, LeftBracket, RightBracket, Backslash, Semicolon, Apostrophe,
    Comma, Period, Slash, Grave,
    Keypad0, Keypad1, Keypad2, Keypad3, Keypad4,
    Keypad5, Keypad6, Keypad7, Keypad8, Keypad9,
    KeypadDecimal, KeypadDivide, KeypadMultiply, KeypadSubtract, KeypadAdd, KeypadEnter,
    // The full enum mirrors SDL3 scancodes; aliases are stable names, not layout characters.
}
```

- `Key` is a **physical** scancode. `Key.W` is the key in the W position on a US layout, regardless
  of the user's keyboard layout. This is what games want for movement.
- `TextInput` is the UTF-16 text entered during the current tick, in layout order. It is the right
  API for names, chat, and console input. It is empty when no text was entered.
- `IsKeyPressed` is true for the first press of a key only; OS-generated repeat events do not set
  it. `IsKeyPressedRepeat` is true only for an OS-generated repeat event, never for the first
  press. `IsKeyDown`/`IsKeyUp` are level state.
- Input is snapshotted at the start of `Tick`; events that arrive while `Tick` runs are applied to
  the next tick. A slow `Tick` therefore coalesces input.
- IME composition, text editing, and clipboard image are M9. v1 handles direct text input.

### 9.2 Mouse

- `MousePosition` is in window coordinates, top-left origin.
- `MouseDelta` is the movement since the previous tick. It is especially useful with
  `CursorMode.Disabled`.
- `MouseWheel` is the vertical wheel delta for the tick. Horizontal wheel and high-resolution
  scroll are post-1.0.
- Mouse buttons use the five-button `MouseButton` enum.

### 9.3 Gamepads

```csharp
public interface IGamepad {
    int Index { get; }
    int InstanceId { get; }
    string Name { get; }
    bool IsConnected { get; }

    bool IsButtonDown(GamepadButton button);
    bool IsButtonPressed(GamepadButton button);
    bool IsButtonReleased(GamepadButton button);

    float GetAxis(GamepadAxis axis);
    float GetTrigger(GamepadTrigger trigger);

    void SetVibration(float lowFrequency, float highFrequency, double seconds);
    void StopVibration();
}

public enum GamepadButton {
    South,
    East,
    West,
    North,
    LeftShoulder,
    RightShoulder,
    LeftStick,
    RightStick,
    Back,
    Start,
    Guide,
    DpadUp,
    DpadDown,
    DpadLeft,
    DpadRight,
    Misc1,
    Misc2,
    Misc3,
    Touchpad,
}

public enum GamepadAxis {
    LeftX,
    LeftY,
    RightX,
    RightY,
}

public enum GamepadTrigger {
    Left,
    Right,
}
```

- `Index` is the position in `Input.Gamepads` for the current tick. It can change across hotplug.
  `InstanceId` is stable for the lifetime of the connected device; use it for per-device state.
- `Input.Gamepads` contains connected pads. `GetGamepad(index)` returns the pad at that index or
  `null` when disconnected. Handles are stable objects; `IsConnected` becomes false on removal.
- Axes are raw `[-1, 1]`; triggers are `[0, 1]`. RayBork applies **no deadzone**; games choose
  their own. SDL's gamecontroller mapping database supplies the button/axis mapping.
- Hotplug is polled at tick boundaries. Rumble is best-effort; an unsupported device logs at Debug
  and ignores the call.
- Touch, motion sensors, and haptics beyond simple rumble are post-1.0.
