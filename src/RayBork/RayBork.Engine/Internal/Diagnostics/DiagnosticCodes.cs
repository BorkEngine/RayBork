namespace RayBork.Internal;

/// <summary>
/// Stable diagnostic codes for engine exceptions. Codes are stable within a major version.
/// </summary>
internal static class DiagnosticCodes {
    /// <summary>
    /// A game callback threw and the app stopped with <see cref="GameException"/>.
    /// </summary>
    public const string GAME_CALLBACK_FAILED = "RB0001";

    /// <summary>
    /// Platform initialization failed.
    /// </summary>
    public const string PLATFORM_INITIALIZATION_FAILED = "RB2001";

    /// <summary>
    /// Window creation failed.
    /// </summary>
    public const string WINDOW_CREATION_FAILED = "RB2002";

    /// <summary>
    /// SDL3 Vulkan surface creation or loader failure.
    /// </summary>
    public const string VULKAN_SURFACE_CREATION_FAILED = "RB2003";

    /// <summary>
    /// Graphics initialization or device creation failed.
    /// </summary>
    public const string GRAPHICS_INITIALIZATION_FAILED = "RB3001";

    /// <summary>
    /// The graphics device was lost after recovery failed.
    /// </summary>
    public const string GRAPHICS_DEVICE_LOST = "RB3002";

    /// <summary>
    /// An unrecoverable graphics failure stopped the app.
    /// </summary>
    public const string GRAPHICS_UNRECOVERABLE = "RB3003";
}
