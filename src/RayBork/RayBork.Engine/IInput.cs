using System.Numerics;

namespace RayBork;

/// <summary>
/// The per-tick input snapshot.
/// </summary>
/// <remarks>
/// Read input only on the tick thread, from <see cref="IGame.Setup"/>, <see cref="IGame.Tick"/>, or
/// <see cref="IGame.Shutdown"/>. The snapshot is captured at the start of <c>Tick</c>; events that
/// arrive while a tick runs apply to the next tick.
/// </remarks>
public interface IInput {
    /// <summary>
    /// Determines whether a key is held down.
    /// </summary>
    /// <param name="key">The physical key to test.</param>
    /// <returns><see langword="true"/> while the key is down.</returns>
    bool IsKeyDown(Key key);

    /// <summary>
    /// Determines whether a key had its first press during the current tick. OS-generated key repeats
    /// do not set this value.
    /// </summary>
    /// <param name="key">The physical key to test.</param>
    /// <returns><see langword="true"/> for exactly one tick.</returns>
    bool IsKeyPressed(Key key);

    /// <summary>
    /// Determines whether a key generated an OS repeat event during the current tick. The first press
    /// does not set this value.
    /// </summary>
    /// <param name="key">The physical key to test.</param>
    /// <returns><see langword="true"/> for one tick after an OS repeat event.</returns>
    bool IsKeyPressedRepeat(Key key);

    /// <summary>
    /// Determines whether a key was released during the current tick.
    /// </summary>
    /// <param name="key">The physical key to test.</param>
    /// <returns><see langword="true"/> for exactly one tick.</returns>
    bool IsKeyReleased(Key key);

    /// <summary>
    /// Determines whether a key is up.
    /// </summary>
    /// <param name="key">The physical key to test.</param>
    /// <returns><see langword="true"/> while the key is not down.</returns>
    bool IsKeyUp(Key key);

    /// <summary>
    /// Gets the UTF-16 text entered during the current tick, in layout order. Empty when no text was
    /// entered.
    /// </summary>
    string TextInput { get; }

    /// <summary>
    /// Gets the mouse position in logical window coordinates with a top-left origin.
    /// </summary>
    Vector2 MousePosition { get; }

    /// <summary>
    /// Gets the mouse movement in logical units since the previous tick. This is the preferred value
    /// for look controls while the cursor mode is <see cref="CursorMode.Disabled"/>.
    /// </summary>
    Vector2 MouseDelta { get; }

    /// <summary>
    /// Gets the vertical mouse wheel delta for the current tick.
    /// </summary>
    float MouseWheel { get; }

    /// <summary>
    /// Determines whether a mouse button is held down.
    /// </summary>
    /// <param name="button">The mouse button to test.</param>
    /// <returns><see langword="true"/> while the button is down.</returns>
    bool IsMouseButtonDown(MouseButton button);

    /// <summary>
    /// Determines whether a mouse button was pressed during the current tick.
    /// </summary>
    /// <param name="button">The mouse button to test.</param>
    /// <returns><see langword="true"/> for exactly one tick.</returns>
    bool IsMouseButtonPressed(MouseButton button);

    /// <summary>
    /// Determines whether a mouse button was released during the current tick.
    /// </summary>
    /// <param name="button">The mouse button to test.</param>
    /// <returns><see langword="true"/> for exactly one tick.</returns>
    bool IsMouseButtonReleased(MouseButton button);

    /// <summary>
    /// Determines whether a mouse button is up.
    /// </summary>
    /// <param name="button">The mouse button to test.</param>
    /// <returns><see langword="true"/> while the button is not down.</returns>
    bool IsMouseButtonUp(MouseButton button);

    /// <summary>
    /// Gets the connected gamepads for the current tick. The position of a pad in this list can change
    /// across a hotplug; use <see cref="IGamepad.InstanceId"/> for per-device state.
    /// </summary>
    IReadOnlyList<IGamepad> Gamepads { get; }

    /// <summary>
    /// Gets the gamepad at the given index in <see cref="Gamepads"/>.
    /// </summary>
    /// <param name="index">The position in <see cref="Gamepads"/>.</param>
    /// <returns>The gamepad, or <see langword="null"/> when no pad is connected at the index.</returns>
    IGamepad? GetGamepad(int index);
}
