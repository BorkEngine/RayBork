namespace RayBork;

/// <summary>
/// Describes the current state of a window.
/// </summary>
public enum WindowState {
    /// <summary>
    /// The window is windowed and visible.
    /// </summary>
    Normal,

    /// <summary>
    /// The window is minimized. The tick loop is capped while the window stays minimized.
    /// </summary>
    Minimized,

    /// <summary>
    /// The window is maximized to the monitor work area.
    /// </summary>
    Maximized,

    /// <summary>
    /// The window occupies the full monitor with the platform's exclusive or borderless mode.
    /// </summary>
    Fullscreen,

    /// <summary>
    /// The window covers the full monitor without a border or title bar.
    /// </summary>
    BorderlessFullscreen,

    /// <summary>
    /// The window is hidden.
    /// </summary>
    Hidden,
}
