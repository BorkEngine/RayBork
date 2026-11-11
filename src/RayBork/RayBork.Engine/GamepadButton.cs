namespace RayBork;

/// <summary>
/// Identifies a gamepad button. Values match the SDL3 gamepad button indices for the shared buttons.
/// </summary>
public enum GamepadButton {
    /// <summary>
    /// The bottom face button (A on Xbox layouts, cross on PlayStation layouts).
    /// </summary>
    South = 0,

    /// <summary>
    /// The right face button (B on Xbox layouts, circle on PlayStation layouts).
    /// </summary>
    East = 1,

    /// <summary>
    /// The left face button (X on Xbox layouts, square on PlayStation layouts).
    /// </summary>
    West = 2,

    /// <summary>
    /// The top face button (Y on Xbox layouts, triangle on PlayStation layouts).
    /// </summary>
    North = 3,

    /// <summary>
    /// The back or select button.
    /// </summary>
    Back = 4,

    /// <summary>
    /// The guide or home button.
    /// </summary>
    Guide = 5,

    /// <summary>
    /// The start or menu button.
    /// </summary>
    Start = 6,

    /// <summary>
    /// The left stick click.
    /// </summary>
    LeftStick = 7,

    /// <summary>
    /// The right stick click.
    /// </summary>
    RightStick = 8,

    /// <summary>
    /// The left shoulder button.
    /// </summary>
    LeftShoulder = 9,

    /// <summary>
    /// The right shoulder button.
    /// </summary>
    RightShoulder = 10,

    /// <summary>
    /// The up direction on the directional pad.
    /// </summary>
    DpadUp = 11,

    /// <summary>
    /// The down direction on the directional pad.
    /// </summary>
    DpadDown = 12,

    /// <summary>
    /// The left direction on the directional pad.
    /// </summary>
    DpadLeft = 13,

    /// <summary>
    /// The right direction on the directional pad.
    /// </summary>
    DpadRight = 14,

    /// <summary>
    /// The first miscellaneous button.
    /// </summary>
    Misc1 = 15,

    /// <summary>
    /// The touchpad click.
    /// </summary>
    Touchpad = 20,

    /// <summary>
    /// The second miscellaneous button.
    /// </summary>
    Misc2 = 21,

    /// <summary>
    /// The third miscellaneous button.
    /// </summary>
    Misc3 = 22,
}
