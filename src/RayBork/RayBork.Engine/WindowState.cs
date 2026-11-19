namespace RayBork;

/// <summary>The current state of a window.</summary>
public enum WindowState
{
    /// <summary>Windowed and visible.</summary>
    Normal,

    /// <summary>Minimized; the tick loop is capped while minimized.</summary>
    Minimized,

    /// <summary>Maximized to the monitor work area.</summary>
    Maximized,

    /// <summary>Fullscreen in the platform's exclusive or borderless mode.</summary>
    Fullscreen,

    /// <summary>Covers the full monitor without a border or title bar.</summary>
    BorderlessFullscreen,

    /// <summary>Hidden.</summary>
    Hidden,
}
