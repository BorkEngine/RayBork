namespace RayBork;

/// <summary>
/// A connected gamepad and its state for the current tick.
/// </summary>
/// <remarks>
/// Read gamepad state on the tick thread. RayBork applies no deadzone and no trigger curve; games
/// choose their own.
/// </remarks>
public interface IGamepad {
    /// <summary>
    /// Gets the position of this pad in <see cref="IInput.Gamepads"/> for the current tick. The value
    /// can change across a hotplug.
    /// </summary>
    int Index { get; }

    /// <summary>
    /// Gets the device instance id, which is stable for the lifetime of the connected device. Use it
    /// for per-device state.
    /// </summary>
    int InstanceId { get; }

    /// <summary>
    /// Gets the device name reported by the platform.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets whether the device is still connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Determines whether a button is held down.
    /// </summary>
    /// <param name="button">The button to test.</param>
    /// <returns><see langword="true"/> while the button is down.</returns>
    bool IsButtonDown(GamepadButton button);

    /// <summary>
    /// Determines whether a button was pressed during the current tick.
    /// </summary>
    /// <param name="button">The button to test.</param>
    /// <returns><see langword="true"/> for exactly one tick.</returns>
    bool IsButtonPressed(GamepadButton button);

    /// <summary>
    /// Determines whether a button was released during the current tick.
    /// </summary>
    /// <param name="button">The button to test.</param>
    /// <returns><see langword="true"/> for exactly one tick.</returns>
    bool IsButtonReleased(GamepadButton button);

    /// <summary>
    /// Gets the raw value of an analog stick axis.
    /// </summary>
    /// <param name="axis">The axis to read.</param>
    /// <returns>The axis value in the range <c>[-1, 1]</c>, with no deadzone applied.</returns>
    float GetAxis(GamepadAxis axis);

    /// <summary>
    /// Gets the raw value of an analog trigger.
    /// </summary>
    /// <param name="trigger">The trigger to read.</param>
    /// <returns>The trigger value in the range <c>[0, 1]</c>.</returns>
    float GetTrigger(GamepadTrigger trigger);

    /// <summary>
    /// Requests vibration for the given duration. Rumble is best effort; an unsupported device logs
    /// at Debug and ignores the call. The request is queued to the platform.
    /// </summary>
    /// <param name="lowFrequency">The low-frequency motor intensity in the range <c>[0, 1]</c>.</param>
    /// <param name="highFrequency">The high-frequency motor intensity in the range <c>[0, 1]</c>.</param>
    /// <param name="seconds">The duration in seconds.</param>
    void SetVibration(float lowFrequency, float highFrequency, double seconds);

    /// <summary>
    /// Stops vibration. The request is queued to the platform.
    /// </summary>
    void StopVibration();
}
