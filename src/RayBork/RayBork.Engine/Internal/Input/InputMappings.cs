using SDL3;

namespace RayBork.Internal;

/// <summary>
/// Translates SDL3 input identifiers into the RayBork contracts and owns the shared array sizes and
/// validation rules used by the raw input state and the tick snapshots.
/// </summary>
internal static class InputMappings {
    internal const int KEY_COUNT = 512;
    internal const int MOUSE_BUTTON_COUNT = 5;
    internal const int GAMEPAD_BUTTON_COUNT = 23;
    internal const int GAMEPAD_AXIS_COUNT = 4;
    internal const int GAMEPAD_TRIGGER_COUNT = 2;
    internal const int GAMEPAD_AXIS_SLOT_COUNT = GAMEPAD_AXIS_COUNT + GAMEPAD_TRIGGER_COUNT;

    private const float AXIS_SCALE = 32767f;

    /// <summary>
    /// Converts an SDL3 scancode to a RayBork key. The two enums share numeric values.
    /// </summary>
    internal static Key ToKey(SDL.Scancode scancode) => (Key)(int)scancode;

    /// <summary>
    /// Maps the one-based SDL3 mouse button number to the zero-based <see cref="MouseButton"/>.
    /// </summary>
    internal static bool TryGetMouseButton(byte sdlButton, out MouseButton button) {
        if (sdlButton >= 1 && sdlButton <= MOUSE_BUTTON_COUNT) {
            button = (MouseButton)(sdlButton - 1);
            return true;
        }

        button = default;
        return false;
    }

    /// <summary>
    /// Maps an SDL3 gamepad button, rejecting paddle, Misc4, Misc5, and Misc6 buttons that have no
    /// RayBork name.
    /// </summary>
    internal static bool TryGetGamepadButton(SDL.GamepadButton sdlButton, out GamepadButton button) {
        int value = (int)sdlButton;
        if ((value >= (int)GamepadButton.South && value <= (int)GamepadButton.Misc1)
            || (value >= (int)GamepadButton.Touchpad && value <= (int)GamepadButton.Misc3)) {
            button = (GamepadButton)value;
            return true;
        }

        button = default;
        return false;
    }

    /// <summary>
    /// Maps an SDL3 stick axis index to a <see cref="GamepadAxis"/>.
    /// </summary>
    internal static bool TryGetGamepadAxis(byte sdlAxis, out GamepadAxis axis) {
        if (sdlAxis < GAMEPAD_AXIS_COUNT) {
            axis = (GamepadAxis)sdlAxis;
            return true;
        }

        axis = default;
        return false;
    }

    /// <summary>
    /// Maps SDL3 axis index 4 or 5 to a <see cref="GamepadTrigger"/>.
    /// </summary>
    internal static bool TryGetGamepadTrigger(byte sdlAxis, out GamepadTrigger trigger) {
        int value = sdlAxis - GAMEPAD_AXIS_COUNT;
        if (value >= 0 && value < GAMEPAD_TRIGGER_COUNT) {
            trigger = (GamepadTrigger)value;
            return true;
        }

        trigger = default;
        return false;
    }

    /// <summary>
    /// Scales a raw SDL3 stick value from <c>[-32768, 32767]</c> to <c>[-1, 1]</c>.
    /// </summary>
    internal static float NormalizeStickAxis(short value) => Math.Clamp(value / AXIS_SCALE, -1f, 1f);

    /// <summary>
    /// Scales a raw SDL3 trigger value from <c>[-32768, 32767]</c> to <c>[0, 1]</c>.
    /// </summary>
    internal static float NormalizeTrigger(short value) => Math.Clamp(value / AXIS_SCALE, 0f, 1f);

    /// <summary>
    /// Gets the storage slot of a stick axis in the flat gamepad axis array.
    /// </summary>
    internal static int AxisSlot(GamepadAxis axis) => (int)axis;

    /// <summary>
    /// Gets the storage slot of a trigger in the flat gamepad axis array.
    /// </summary>
    internal static int TriggerSlot(GamepadTrigger trigger) => GAMEPAD_AXIS_COUNT + (int)trigger;

    internal static void ValidateKey(Key key) {
        int value = (int)key;
        if (value < 0 || value >= KEY_COUNT) {
            throw new ArgumentOutOfRangeException(nameof(key), key, "The key is not a defined SDL3 scancode.");
        }
    }

    internal static void ValidateMouseButton(MouseButton button) {
        int value = (int)button;
        if (value < 0 || value >= MOUSE_BUTTON_COUNT) {
            throw new ArgumentOutOfRangeException(nameof(button), button, "The mouse button is not defined.");
        }
    }

    internal static void ValidateGamepadButton(GamepadButton button) {
        if (!Enum.IsDefined(button)) {
            throw new ArgumentOutOfRangeException(nameof(button), button, "The gamepad button is not defined.");
        }
    }

    internal static void ValidateGamepadAxis(GamepadAxis axis) {
        int value = (int)axis;
        if (value < 0 || value >= GAMEPAD_AXIS_COUNT) {
            throw new ArgumentOutOfRangeException(nameof(axis), axis, "The gamepad axis is not defined.");
        }
    }

    internal static void ValidateGamepadTrigger(GamepadTrigger trigger) {
        int value = (int)trigger;
        if (value < 0 || value >= GAMEPAD_TRIGGER_COUNT) {
            throw new ArgumentOutOfRangeException(nameof(trigger), trigger, "The gamepad trigger is not defined.");
        }
    }

    internal static void ValidateCursorMode(CursorMode mode) {
        int value = (int)mode;
        if (value < 0 || value > (int)CursorMode.Disabled) {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "The cursor mode is not defined.");
        }
    }
}
