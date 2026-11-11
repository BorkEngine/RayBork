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
}
