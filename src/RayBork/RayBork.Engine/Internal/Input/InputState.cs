using System.Numerics;
using System.Text;

namespace RayBork.Internal;

/// <summary>
/// Owns the raw input state that the main thread writes and the tick thread snapshots.
/// </summary>
/// <remarks>
/// One lock guards every mutation and <see cref="Capture"/>, so an event applies to exactly one
/// snapshot. Levels persist across ticks; edges, text, mouse delta, and wheel are per tick.
/// </remarks>
internal sealed class InputState
{
    private readonly Lock _gate = new();
    private readonly bool[] _keyDown = new bool[InputMappings.KEY_COUNT];
    private readonly bool[] _keyPressed = new bool[InputMappings.KEY_COUNT];
    private readonly bool[] _keyRepeat = new bool[InputMappings.KEY_COUNT];
    private readonly bool[] _keyReleased = new bool[InputMappings.KEY_COUNT];
    private readonly StringBuilder _text = new();
    private readonly bool[] _mouseDown = new bool[InputMappings.MOUSE_BUTTON_COUNT];
    private readonly bool[] _mousePressed = new bool[InputMappings.MOUSE_BUTTON_COUNT];
    private readonly bool[] _mouseReleased = new bool[InputMappings.MOUSE_BUTTON_COUNT];
    private readonly List<GamepadState> _gamepadOrder = [];
    private readonly Dictionary<uint, GamepadState> _gamepadsById = [];
    private readonly Dictionary<uint, GamepadHandle> _gamepadHandles = [];
    private readonly IGamepadVibrationSink _vibrationSink;

    private Vector2 _mousePosition;
    private Vector2 _mouseDelta;
    private float _mouseWheel;

    internal InputState(IGamepadVibrationSink vibrationSink)
    {
        ArgumentNullException.ThrowIfNull(vibrationSink);
        _vibrationSink = vibrationSink;
    }

    /// <summary>Applies a key-down event, recording a press or repeat edge.</summary>
    internal void HandleKeyDown(Key key, bool repeat)
    {
        InputMappings.ValidateKey(key);
        lock (_gate)
        {
            int index = (int)key;
            if (repeat)
            {
                _keyRepeat[index] = true;
            }
            else if (!_keyDown[index])
            {
                _keyPressed[index] = true;
            }

            _keyDown[index] = true;
        }
    }

    /// <summary>Applies a key-up event; a release edge needs the key to be down.</summary>
    internal void HandleKeyUp(Key key)
    {
        InputMappings.ValidateKey(key);
        lock (_gate)
        {
            int index = (int)key;
            if (_keyDown[index])
            {
                _keyReleased[index] = true;
                _keyDown[index] = false;
            }
        }
    }

    /// <summary>Appends event-order UTF-16 text to the current tick buffer.</summary>
    internal void AppendText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return;
        }

        lock (_gate)
        {
            _text.Append(text);
        }
    }

    /// <summary>
    /// Applies mouse motion in render pixels, replacing the position and accumulating the delta.
    /// </summary>
    internal void HandleMouseMotion(float x, float y, float deltaX, float deltaY)
    {
        lock (_gate)
        {
            _mousePosition = new Vector2(x, y);
            _mouseDelta += new Vector2(deltaX, deltaY);
        }
    }

    /// <summary>Applies a mouse-button transition.</summary>
    internal void HandleMouseButton(MouseButton button, bool down)
    {
        InputMappings.ValidateMouseButton(button);
        lock (_gate)
        {
            int index = (int)button;
            bool wasDown = _mouseDown[index];
            _mouseDown[index] = down;
            if (down)
            {
                if (!wasDown)
                {
                    _mousePressed[index] = true;
                }
            }
            else if (wasDown)
            {
                _mouseReleased[index] = true;
            }
        }
    }

    /// <summary>Adds the vertical wheel delta for the tick.</summary>
    internal void HandleMouseWheel(float delta)
    {
        lock (_gate)
        {
            _mouseWheel += delta;
        }
    }

    /// <summary>Clears keyboard and mouse levels and edges, as on focus loss.</summary>
    internal void ResetLevels()
    {
        lock (_gate)
        {
            Array.Clear(_keyDown);
            Array.Clear(_keyPressed);
            Array.Clear(_keyRepeat);
            Array.Clear(_keyReleased);
            Array.Clear(_mouseDown);
            Array.Clear(_mousePressed);
            Array.Clear(_mouseReleased);
        }
    }

    /// <summary>Registers a connected gamepad and seeds its levels without pressed edges.</summary>
    internal void ConnectGamepad(
        uint instanceId,
        string name,
        ReadOnlySpan<bool> buttonStates,
        ReadOnlySpan<float> axes
    )
    {
        ArgumentNullException.ThrowIfNull(name);
        if (buttonStates.Length != InputMappings.GAMEPAD_BUTTON_COUNT)
        {
            throw new ArgumentException(
                "The initial gamepad button state has the wrong length.",
                nameof(buttonStates)
            );
        }

        if (axes.Length != InputMappings.GAMEPAD_AXIS_SLOT_COUNT)
        {
            throw new ArgumentException(
                "The initial gamepad axis state has the wrong length.",
                nameof(axes)
            );
        }

        lock (_gate)
        {
            if (_gamepadsById.ContainsKey(instanceId))
            {
                return;
            }

            GamepadState state = new(instanceId, name);
            for (int i = 0; i < buttonStates.Length; i++)
            {
                if (buttonStates[i])
                {
                    state.SetButton((GamepadButton)i, true);
                }
            }

            for (int i = 0; i < axes.Length; i++)
            {
                state.SetAxis(i, axes[i]);
            }

            state.ClearEdges();
            _gamepadsById.Add(instanceId, state);
            _gamepadOrder.Add(state);
        }
    }

    /// <summary>Removes a disconnected gamepad from the connected order.</summary>
    /// <remarks>Handles stay alive and report disconnected after the next capture.</remarks>
    internal void DisconnectGamepad(uint instanceId)
    {
        lock (_gate)
        {
            if (_gamepadsById.Remove(instanceId, out GamepadState? state))
            {
                _gamepadOrder.Remove(state);
            }
        }
    }

    /// <summary>
    /// Applies a gamepad button transition for a connected device; unknown devices are ignored.
    /// </summary>
    internal void HandleGamepadButton(uint instanceId, GamepadButton button, bool down)
    {
        InputMappings.ValidateGamepadButton(button);
        lock (_gate)
        {
            if (_gamepadsById.TryGetValue(instanceId, out GamepadState? state))
            {
                state.SetButton(button, down);
            }
        }
    }

    /// <summary>Applies a normalized stick axis value in <c>[-1, 1]</c>.</summary>
    internal void HandleGamepadStickAxis(uint instanceId, GamepadAxis axis, float value)
    {
        InputMappings.ValidateGamepadAxis(axis);
        float clamped = Math.Clamp(value, -1f, 1f);
        lock (_gate)
        {
            if (_gamepadsById.TryGetValue(instanceId, out GamepadState? state))
            {
                state.SetAxis(InputMappings.AxisSlot(axis), float.IsNaN(clamped) ? 0f : clamped);
            }
        }
    }

    /// <summary>Applies a normalized trigger value in <c>[0, 1]</c>.</summary>
    internal void HandleGamepadTrigger(uint instanceId, GamepadTrigger trigger, float value)
    {
        InputMappings.ValidateGamepadTrigger(trigger);
        float clamped = Math.Clamp(value, 0f, 1f);
        lock (_gate)
        {
            if (_gamepadsById.TryGetValue(instanceId, out GamepadState? state))
            {
                state.SetAxis(
                    InputMappings.TriggerSlot(trigger),
                    float.IsNaN(clamped) ? 0f : clamped
                );
            }
        }
    }

    /// <summary>Captures the next tick snapshot on the tick thread.</summary>
    /// <remarks>Copy and clears share a lock, so an event belongs to one snapshot.</remarks>
    internal InputSnapshot Capture()
    {
        bool[] keyDown;
        bool[] keyPressed;
        bool[] keyRepeat;
        bool[] keyReleased;
        string text;
        Vector2 mousePosition;
        Vector2 mouseDelta;
        float mouseWheel;
        bool[] mouseDown;
        bool[] mousePressed;
        bool[] mouseReleased;
        List<GamepadSnapshot> gamepads = [];

        lock (_gate)
        {
            keyDown = (bool[])_keyDown.Clone();
            keyPressed = (bool[])_keyPressed.Clone();
            keyRepeat = (bool[])_keyRepeat.Clone();
            keyReleased = (bool[])_keyReleased.Clone();
            text = _text.ToString();
            mousePosition = _mousePosition;
            mouseDelta = _mouseDelta;
            mouseWheel = _mouseWheel;
            mouseDown = (bool[])_mouseDown.Clone();
            mousePressed = (bool[])_mousePressed.Clone();
            mouseReleased = (bool[])_mouseReleased.Clone();

            _text.Clear();
            _mouseDelta = Vector2.Zero;
            _mouseWheel = 0f;
            Array.Clear(_keyPressed);
            Array.Clear(_keyRepeat);
            Array.Clear(_keyReleased);
            Array.Clear(_mousePressed);
            Array.Clear(_mouseReleased);

            for (int i = 0; i < _gamepadOrder.Count; i++)
            {
                gamepads.Add(_gamepadOrder[i].CreateSnapshot());
            }
        }

        // The handle table is tick-thread only: Capture is the sole writer.
        foreach (GamepadHandle handle in _gamepadHandles.Values)
        {
            handle.MarkDisconnected();
        }

        IGamepad[] handles = new IGamepad[gamepads.Count];
        for (int i = 0; i < gamepads.Count; i++)
        {
            GamepadSnapshot snapshot = gamepads[i];
            if (!_gamepadHandles.TryGetValue(snapshot.InstanceId, out GamepadHandle? handle))
            {
                handle = new GamepadHandle(snapshot.InstanceId, _vibrationSink);
                _gamepadHandles.Add(snapshot.InstanceId, handle);
            }

            handle.Update(i, snapshot);
            handles[i] = handle;
        }

        return new InputSnapshot(
            keyDown,
            keyPressed,
            keyRepeat,
            keyReleased,
            text,
            mousePosition,
            mouseDelta,
            mouseWheel,
            mouseDown,
            mousePressed,
            mouseReleased,
            handles
        );
    }
}
