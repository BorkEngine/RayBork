namespace RayBork.Internal;

/// <summary>
/// The subset of the platform used by <see cref="GamepadHandle"/> to queue rumble. Implemented by
/// the platform so handles created on the tick thread never call SDL directly.
/// </summary>
internal interface IGamepadVibrationSink {
    /// <summary>
    /// Queues a vibration request for the stable device instance id.
    /// </summary>
    void SetGamepadVibration(int instanceId, float lowFrequency, float highFrequency, TimeSpan duration);

    /// <summary>
    /// Queues a stop request for the stable device instance id.
    /// </summary>
    void StopGamepadVibration(int instanceId);
}

/// <summary>
/// The immutable per-tick state of one gamepad, captured from the main-thread raw state.
/// </summary>
internal sealed class GamepadSnapshot {
    private readonly bool[] _buttons;
    private readonly bool[] _pressed;
    private readonly bool[] _released;
    private readonly float[] _axes;

    internal GamepadSnapshot(uint instanceId, string name, bool[] buttons, bool[] pressed, bool[] released, float[] axes) {
        InstanceId = instanceId;
        Name = name;
        _buttons = buttons;
        _pressed = pressed;
        _released = released;
        _axes = axes;
    }

    internal uint InstanceId { get; }

    internal string Name { get; }

    internal bool IsDown(GamepadButton button) => _buttons[(int)button];

    internal bool IsPressed(GamepadButton button) => _pressed[(int)button];

    internal bool IsReleased(GamepadButton button) => _released[(int)button];

    internal float GetAxis(GamepadAxis axis) => _axes[InputMappings.AxisSlot(axis)];

    internal float GetTrigger(GamepadTrigger trigger) => _axes[InputMappings.TriggerSlot(trigger)];
}

/// <summary>
/// A stable game-facing gamepad object for one SDL3 instance id. The tick thread updates it during
/// <see cref="InputState.Capture"/>; game code reads it on the tick thread only.
/// </summary>
internal sealed class GamepadHandle : IGamepad {
    private readonly IGamepadVibrationSink _vibrationSink;

    private GamepadSnapshot? _snapshot;
    private string _name = string.Empty;
    private int _index;
    private bool _connected;

    internal GamepadHandle(uint sdlInstanceId, IGamepadVibrationSink vibrationSink) {
        ArgumentNullException.ThrowIfNull(vibrationSink);
        SdlInstanceId = sdlInstanceId;
        InstanceId = unchecked((int)sdlInstanceId);
        _vibrationSink = vibrationSink;
    }

    /// <summary>
    /// Gets the SDL3 instance id. The public contract exposes the same bits as a signed integer.
    /// </summary>
    internal uint SdlInstanceId { get; }

    /// <inheritdoc />
    public int Index => _index;

    /// <inheritdoc />
    public int InstanceId { get; }

    /// <inheritdoc />
    public string Name => _name;

    /// <inheritdoc />
    public bool IsConnected => _connected;

    /// <inheritdoc />
    public bool IsButtonDown(GamepadButton button) {
        InputMappings.ValidateGamepadButton(button);
        return _snapshot?.IsDown(button) ?? false;
    }

    /// <inheritdoc />
    public bool IsButtonPressed(GamepadButton button) {
        InputMappings.ValidateGamepadButton(button);
        return _snapshot?.IsPressed(button) ?? false;
    }

    /// <inheritdoc />
    public bool IsButtonReleased(GamepadButton button) {
        InputMappings.ValidateGamepadButton(button);
        return _snapshot?.IsReleased(button) ?? false;
    }

    /// <inheritdoc />
    public float GetAxis(GamepadAxis axis) {
        InputMappings.ValidateGamepadAxis(axis);
        return _snapshot?.GetAxis(axis) ?? 0f;
    }

    /// <inheritdoc />
    public float GetTrigger(GamepadTrigger trigger) {
        InputMappings.ValidateGamepadTrigger(trigger);
        return _snapshot?.GetTrigger(trigger) ?? 0f;
    }

    /// <inheritdoc />
    public void SetVibration(float lowFrequency, float highFrequency, double seconds) {
        if (!double.IsFinite(seconds) || seconds < 0.0) {
            throw new ArgumentOutOfRangeException(
                nameof(seconds),
                seconds,
                "The vibration duration must be finite and non-negative.");
        }

        float low = ClampIntensity(lowFrequency);
        float high = ClampIntensity(highFrequency);
        if (!_connected) {
            return;
        }

        TimeSpan duration = seconds >= TimeSpan.MaxValue.TotalSeconds
            ? TimeSpan.MaxValue
            : TimeSpan.FromSeconds(seconds);
        _vibrationSink.SetGamepadVibration(InstanceId, low, high, duration);
    }

    /// <inheritdoc />
    public void StopVibration() {
        if (!_connected) {
            return;
        }

        _vibrationSink.StopGamepadVibration(InstanceId);
    }

    /// <summary>
    /// Publishes the state captured for the current tick and marks the pad connected.
    /// </summary>
    internal void Update(int index, GamepadSnapshot snapshot) {
        ArgumentNullException.ThrowIfNull(snapshot);
        _index = index;
        _snapshot = snapshot;
        _name = snapshot.Name;
        _connected = true;
    }

    /// <summary>
    /// Marks the pad disconnected so stale levels and edges read as up.
    /// </summary>
    internal void MarkDisconnected() {
        _snapshot = null;
        _connected = false;
    }

    private static float ClampIntensity(float intensity) {
        float clamped = Math.Clamp(intensity, 0f, 1f);
        return float.IsNaN(clamped) ? 0f : clamped;
    }
}
