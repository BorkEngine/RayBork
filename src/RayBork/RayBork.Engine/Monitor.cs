namespace RayBork;

/// <summary>A monitor as reported by the platform.</summary>
/// <remarks>
/// <see cref="Index"/> is stable for the process lifetime; bounds and work area use desktop
/// coordinates. <see cref="DpiScale"/> is the OS scale and <see cref="RefreshRate"/> is in hertz.
/// </remarks>
public readonly record struct Monitor(
    int Index,
    string Name,
    Rect Bounds,
    Rect WorkArea,
    float DpiScale,
    int RefreshRate
);
