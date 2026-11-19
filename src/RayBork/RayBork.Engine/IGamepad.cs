namespace RayBork;

/// <summary>
/// A connected gamepad and its state for the current tick.
/// </summary>
/// <remarks>
/// Read on the tick thread. RayBork applies no deadzone or trigger curve; games choose their own.
/// </remarks>
public interface IGamepad
{
    /// <summary>Gets this pad's index in <see cref="IInput.Gamepads"/>; hotplug moves it.</summary>
    int Index { get; }

    /// <summary>
    /// Gets the device instance id, stable while connected, for per-device state.
    /// </summary>
    int InstanceId { get; }

    /// <summary>Gets the platform-reported device name.</summary>
    string Name { get; }

    /// <summary>Gets whether the device is still connected.</summary>
    bool IsConnected { get; }

    /// <summary>Gets whether a button is held down.</summary>
    bool IsButtonDown(GamepadButton button);

    /// <summary>Gets whether a button was pressed this tick.</summary>
    bool IsButtonPressed(GamepadButton button);

    /// <summary>Gets whether a button was released this tick.</summary>
    bool IsButtonReleased(GamepadButton button);

    /// <summary>Gets an analog stick axis in <c>[-1, 1]</c>, with no deadzone.</summary>
    float GetAxis(GamepadAxis axis);

    /// <summary>Gets an analog trigger in <c>[0, 1]</c>, with no curve.</summary>
    float GetTrigger(GamepadTrigger trigger);

    /// <summary>
    /// Queues best-effort vibration for the given duration; unsupported devices log at Debug.
    /// </summary>
    /// <param name="lowFrequency">The low-frequency intensity in <c>[0, 1]</c>.</param>
    /// <param name="highFrequency">The high-frequency intensity in <c>[0, 1]</c>.</param>
    /// <param name="seconds">The duration in seconds.</param>
    void SetVibration(float lowFrequency, float highFrequency, double seconds);

    /// <summary>Queues a request to stop vibration.</summary>
    void StopVibration();
}
