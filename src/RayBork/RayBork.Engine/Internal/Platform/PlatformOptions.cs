namespace RayBork.Internal;

/// <summary>Startup options for <see cref="IEnginePlatform.Initialize"/>.</summary>
internal sealed record PlatformOptions
{
    /// <summary>Gets the initial window title.</summary>
    public required string Title { get; init; }

    /// <summary>Gets the initial client width in logical units.</summary>
    public required int Width { get; init; }

    /// <summary>Gets the initial client height in logical units.</summary>
    public required int Height { get; init; }

    /// <summary>Gets whether the user can resize the window.</summary>
    public required bool Resizable { get; init; }

    /// <summary>Gets the startup window state.</summary>
    public required WindowState WindowState { get; init; }

    /// <summary>Gets whether the platform creates a Vulkan-capable window.</summary>
    /// <remarks>
    /// When <see langword="false"/>, window surface requests fail with
    /// <see cref="InvalidOperationException"/>.
    /// </remarks>
    public bool EnableVulkan { get; init; }
}
