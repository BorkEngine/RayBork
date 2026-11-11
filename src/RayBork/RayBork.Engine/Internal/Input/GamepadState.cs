namespace RayBork.Internal;

/// <summary>
/// The main-thread raw state of one connected gamepad. All access is guarded by the owning
/// <see cref="InputState"/> lock.
/// </summary>
internal sealed class GamepadState {
    private readonly bool[] _buttons = new bool[InputMappings.GAMEPAD_BUTTON_COUNT];
    private readonly bool[] _pressed = new bool[InputMappings.GAMEPAD_BUTTON_COUNT];
    private readonly bool[] _released = new bool[InputMappings.GAMEPAD_BUTTON_COUNT];
    private readonly float[] _axes = new float[InputMappings.GAMEPAD_AXIS_SLOT_COUNT];

    internal GamepadState(uint instanceId, string name) {
        InstanceId = instanceId;
        Name = name;
    }

    internal uint InstanceId { get; }

    internal string Name { get; }

    internal void SetButton(GamepadButton button, bool down) {
        int index = (int)button;
        bool wasDown = _buttons[index];
        _buttons[index] = down;
        if (down) {
            if (!wasDown) {
                _pressed[index] = true;
            }
        } else if (wasDown) {
            _released[index] = true;
        }
    }

    internal void SetAxis(int slot, float value) => _axes[slot] = value;

    /// <summary>
    /// Drops pending edges without touching the levels. Used by the initial poll so a button that is
    /// already held when the device connects does not report a press on the first tick.
    /// </summary>
    internal void ClearEdges() {
        Array.Clear(_pressed);
        Array.Clear(_released);
    }

    /// <summary>
    /// Copies the state for the tick snapshot and clears the per-tick edge sets.
    /// </summary>
    internal GamepadSnapshot CreateSnapshot() {
        GamepadSnapshot snapshot = new(
            InstanceId,
            Name,
            (bool[])_buttons.Clone(),
            (bool[])_pressed.Clone(),
            (bool[])_released.Clone(),
            (float[])_axes.Clone());
        ClearEdges();
        return snapshot;
    }
}
