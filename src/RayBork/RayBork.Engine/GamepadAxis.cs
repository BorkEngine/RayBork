namespace RayBork;

/// <summary>
/// Identifies a gamepad analog stick axis. Values match the SDL3 axis indices.
/// </summary>
public enum GamepadAxis {
    /// <summary>
    /// The horizontal axis of the left stick.
    /// </summary>
    LeftX = 0,

    /// <summary>
    /// The vertical axis of the left stick.
    /// </summary>
    LeftY = 1,

    /// <summary>
    /// The horizontal axis of the right stick.
    /// </summary>
    RightX = 2,

    /// <summary>
    /// The vertical axis of the right stick.
    /// </summary>
    RightY = 3,
}
