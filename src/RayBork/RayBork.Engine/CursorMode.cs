namespace RayBork;

/// <summary>
/// Selects how the system cursor behaves over the window.
/// </summary>
public enum CursorMode {
    /// <summary>
    /// The cursor is visible and moves normally.
    /// </summary>
    Normal,

    /// <summary>
    /// The cursor is hidden but is not locked to the window.
    /// </summary>
    Hidden,

    /// <summary>
    /// The cursor is hidden and locked, and mouse deltas come from relative motion. Use this mode for
    /// mouse-look controls.
    /// </summary>
    Disabled,
}
