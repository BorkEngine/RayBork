using System.Runtime.InteropServices;
using SDL3;

namespace RayBork.Internal;

/// <summary>
/// Translates SDL3 input events into the raw <see cref="InputState"/>. Runs on the main thread; the
/// tick thread captures the state through <see cref="Capture"/>.
/// </summary>
internal sealed class SdlInput {
    private readonly InputState _state;

    internal SdlInput(IGamepadVibrationSink vibrationSink) {
        _state = new InputState(vibrationSink);
    }

    /// <summary>
    /// Gets the raw state for main-thread gamepad connect, disconnect, and initial polling.
    /// </summary>
    internal InputState State => _state;

    /// <summary>
    /// Captures the next tick snapshot. Runs on the tick thread.
    /// </summary>
    internal IInput Capture() => _state.Capture();

    /// <summary>
    /// Clears keyboard and mouse levels and pending edges when the window loses focus.
    /// </summary>
    internal void ResetLevels() => _state.ResetLevels();

    /// <summary>
    /// Applies one SDL3 event to the raw input state. Window, display, close, and gamepad device
    /// events are owned by the platform and are ignored here.
    /// </summary>
    internal void HandleEvent(in SDL.Event sdlEvent) {
        switch ((SDL.EventType)sdlEvent.Type) {
            case SDL.EventType.KeyDown:
                _state.HandleKeyDown(InputMappings.ToKey(sdlEvent.Key.Scancode), sdlEvent.Key.Repeat);
                break;
            case SDL.EventType.KeyUp:
                _state.HandleKeyUp(InputMappings.ToKey(sdlEvent.Key.Scancode));
                break;
            case SDL.EventType.TextInput:
                AppendTextInput(sdlEvent.Text.Text);
                break;
            case SDL.EventType.MouseMotion:
                _state.HandleMouseMotion(
                    sdlEvent.Motion.X,
                    sdlEvent.Motion.Y,
                    sdlEvent.Motion.XRel,
                    sdlEvent.Motion.YRel);
                break;
            case SDL.EventType.MouseButtonDown:
                ApplyMouseButton(sdlEvent.Button.Button, down: true);
                break;
            case SDL.EventType.MouseButtonUp:
                ApplyMouseButton(sdlEvent.Button.Button, down: false);
                break;
            case SDL.EventType.MouseWheel:
                _state.HandleMouseWheel(sdlEvent.Wheel.Y);
                break;
            case SDL.EventType.GamepadButtonDown:
                ApplyGamepadButton(sdlEvent.GButton.Which, sdlEvent.GButton.Button, down: true);
                break;
            case SDL.EventType.GamepadButtonUp:
                ApplyGamepadButton(sdlEvent.GButton.Which, sdlEvent.GButton.Button, down: false);
                break;
            case SDL.EventType.GamepadAxisMotion:
                ApplyGamepadAxis(sdlEvent.GAxis.Which, sdlEvent.GAxis.Axis, sdlEvent.GAxis.Value);
                break;
        }
    }

    private void AppendTextInput(IntPtr textPointer) {
        if (textPointer == IntPtr.Zero) {
            return;
        }

        string? text = Marshal.PtrToStringUTF8(textPointer);
        if (!string.IsNullOrEmpty(text)) {
            _state.AppendText(text);
        }
    }

    private void ApplyMouseButton(byte sdlButton, bool down) {
        if (InputMappings.TryGetMouseButton(sdlButton, out MouseButton button)) {
            _state.HandleMouseButton(button, down);
        }
    }

    private void ApplyGamepadButton(uint instanceId, byte sdlButton, bool down) {
        if (InputMappings.TryGetGamepadButton((SDL.GamepadButton)sdlButton, out GamepadButton button)) {
            _state.HandleGamepadButton(instanceId, button, down);
        }
    }

    private void ApplyGamepadAxis(uint instanceId, byte sdlAxis, short value) {
        if (InputMappings.TryGetGamepadAxis(sdlAxis, out GamepadAxis axis)) {
            _state.HandleGamepadStickAxis(instanceId, axis, InputMappings.NormalizeStickAxis(value));
        } else if (InputMappings.TryGetGamepadTrigger(sdlAxis, out GamepadTrigger trigger)) {
            _state.HandleGamepadTrigger(instanceId, trigger, InputMappings.NormalizeTrigger(value));
        }
    }
}
