using System.Numerics;

namespace RayBork.Internal;

/// <summary>
/// The immutable per-tick input snapshot captured on the tick thread. Read it only on the tick
/// thread, from <c>Setup</c>, <c>Tick</c>, or <c>Shutdown</c>.
/// </summary>
/// <remarks>
/// The owning <see cref="InputState"/> clones its arrays before constructing the snapshot, so later
/// events never change the keyboard or mouse view. Gamepad entries are stable
/// <see cref="GamepadHandle"/> objects whose state advances with each tick; use
/// <see cref="IGamepad.InstanceId"/> for per-device state across hotplug.
/// </remarks>
internal sealed class InputSnapshot : IInput {
    private readonly bool[] _keyDown;
    private readonly bool[] _keyPressed;
    private readonly bool[] _keyRepeat;
    private readonly bool[] _keyReleased;
    private readonly string _text;
    private readonly Vector2 _mousePosition;
    private readonly Vector2 _mouseDelta;
    private readonly float _mouseWheel;
    private readonly bool[] _mouseDown;
    private readonly bool[] _mousePressed;
    private readonly bool[] _mouseReleased;
    private readonly IReadOnlyList<IGamepad> _gamepads;

    internal InputSnapshot(
        bool[] keyDown,
        bool[] keyPressed,
        bool[] keyRepeat,
        bool[] keyReleased,
        string text,
        Vector2 mousePosition,
        Vector2 mouseDelta,
        float mouseWheel,
        bool[] mouseDown,
        bool[] mousePressed,
        bool[] mouseReleased,
        IReadOnlyList<IGamepad> gamepads) {
        _keyDown = keyDown;
        _keyPressed = keyPressed;
        _keyRepeat = keyRepeat;
        _keyReleased = keyReleased;
        _text = text;
        _mousePosition = mousePosition;
        _mouseDelta = mouseDelta;
        _mouseWheel = mouseWheel;
        _mouseDown = mouseDown;
        _mousePressed = mousePressed;
        _mouseReleased = mouseReleased;
        _gamepads = gamepads;
    }

    /// <inheritdoc />
    public string TextInput => _text;

    /// <inheritdoc />
    public Vector2 MousePosition => _mousePosition;

    /// <inheritdoc />
    public Vector2 MouseDelta => _mouseDelta;

    /// <inheritdoc />
    public float MouseWheel => _mouseWheel;

    /// <inheritdoc />
    public IReadOnlyList<IGamepad> Gamepads => _gamepads;

    /// <inheritdoc />
    public bool IsKeyDown(Key key) {
        InputMappings.ValidateKey(key);
        return _keyDown[(int)key];
    }

    /// <inheritdoc />
    public bool IsKeyPressed(Key key) {
        InputMappings.ValidateKey(key);
        return _keyPressed[(int)key];
    }

    /// <inheritdoc />
    public bool IsKeyPressedRepeat(Key key) {
        InputMappings.ValidateKey(key);
        return _keyRepeat[(int)key];
    }

    /// <inheritdoc />
    public bool IsKeyReleased(Key key) {
        InputMappings.ValidateKey(key);
        return _keyReleased[(int)key];
    }

    /// <inheritdoc />
    public bool IsKeyUp(Key key) {
        InputMappings.ValidateKey(key);
        return !_keyDown[(int)key];
    }

    /// <inheritdoc />
    public bool IsMouseButtonDown(MouseButton button) {
        InputMappings.ValidateMouseButton(button);
        return _mouseDown[(int)button];
    }

    /// <inheritdoc />
    public bool IsMouseButtonPressed(MouseButton button) {
        InputMappings.ValidateMouseButton(button);
        return _mousePressed[(int)button];
    }

    /// <inheritdoc />
    public bool IsMouseButtonReleased(MouseButton button) {
        InputMappings.ValidateMouseButton(button);
        return _mouseReleased[(int)button];
    }

    /// <inheritdoc />
    public bool IsMouseButtonUp(MouseButton button) {
        InputMappings.ValidateMouseButton(button);
        return !_mouseDown[(int)button];
    }

    /// <inheritdoc />
    public IGamepad? GetGamepad(int index) {
        if ((uint)index >= (uint)_gamepads.Count) {
            return null;
        }

        return _gamepads[index];
    }
}
