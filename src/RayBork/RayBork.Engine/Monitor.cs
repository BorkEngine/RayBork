namespace RayBork;

/// <summary>
/// Describes a monitor as reported by the platform.
/// </summary>
/// <param name="Index">The stable monitor index for the process lifetime.</param>
/// <param name="Name">The platform display name.</param>
/// <param name="Bounds">The monitor rectangle in desktop coordinates.</param>
/// <param name="WorkArea">The desktop rectangle not covered by taskbars and other reserved areas.</param>
/// <param name="DpiScale">The OS display scale for the monitor, such as <c>1.5</c> at 150%.</param>
/// <param name="RefreshRate">The current refresh rate in hertz.</param>
public readonly record struct Monitor(
    int Index,
    string Name,
    Rect Bounds,
    Rect WorkArea,
    float DpiScale,
    int RefreshRate);
