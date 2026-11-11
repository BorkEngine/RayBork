namespace RayBork.Internal;

/// <summary>
/// The startup options that the main thread passes to <see cref="IEnginePlatform.Initialize"/>.
/// </summary>
internal sealed record PlatformOptions {
    /// <summary>
    /// Gets the initial window title.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Gets the initial client width in logical units.
    /// </summary>
    public required int Width { get; init; }

    /// <summary>
    /// Gets the initial client height in logical units.
    /// </summary>
    public required int Height { get; init; }

    /// <summary>
    /// Gets whether the window can be resized by the user.
    /// </summary>
    public required bool Resizable { get; init; }

    /// <summary>
    /// Gets the window state to apply at startup.
    /// </summary>
    public required WindowState WindowState { get; init; }
}
