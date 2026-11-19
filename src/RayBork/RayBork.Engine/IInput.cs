using System.Numerics;

namespace RayBork;

/// <summary>
/// The per-tick input snapshot.
/// </summary>
/// <remarks>
/// Read only on the tick thread, from <see cref="IGame.Setup"/>, <see cref="IGame.Tick"/>, or
/// <see cref="IGame.Shutdown"/>. Events that arrive while a tick runs apply to the next tick.
/// </remarks>
public interface IInput
{
    /// <summary>Gets whether a key is held down.</summary>
    bool IsKeyDown(Key key);

    /// <summary>Gets whether a key was first pressed this tick, excluding OS repeats.</summary>
    bool IsKeyPressed(Key key);

    /// <summary>Gets whether an OS key repeat occurred this tick; excludes the first press.</summary>
    bool IsKeyPressedRepeat(Key key);

    /// <summary>Gets whether a key was released this tick.</summary>
    bool IsKeyReleased(Key key);

    /// <summary>Gets whether a key is up.</summary>
    bool IsKeyUp(Key key);

    /// <summary>Gets the UTF-16 text entered this tick in layout order; empty when none.</summary>
    string TextInput { get; }

    /// <summary>
    /// Gets the mouse position in fractional render pixels with a top-left origin, not window units.
    /// </summary>
    Vector2 MousePosition { get; }

    /// <summary>
    /// Gets mouse movement in render pixels since the previous tick, even while the cursor is
    /// disabled.
    /// </summary>
    Vector2 MouseDelta { get; }

    /// <summary>Gets the vertical wheel delta for this tick.</summary>
    float MouseWheel { get; }

    /// <summary>Gets whether a mouse button is held down.</summary>
    bool IsMouseButtonDown(MouseButton button);

    /// <summary>Gets whether a mouse button was pressed this tick.</summary>
    bool IsMouseButtonPressed(MouseButton button);

    /// <summary>Gets whether a mouse button was released this tick.</summary>
    bool IsMouseButtonReleased(MouseButton button);

    /// <summary>Gets whether a mouse button is up.</summary>
    bool IsMouseButtonUp(MouseButton button);

    /// <summary>Gets connected gamepads this tick; positions can change on hotplug.</summary>
    IReadOnlyList<IGamepad> Gamepads { get; }

    /// <summary>Gets a gamepad by index from <see cref="Gamepads"/>; null when absent.</summary>
    IGamepad? GetGamepad(int index);
}
