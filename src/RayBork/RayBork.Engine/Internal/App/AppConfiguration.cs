using Microsoft.Extensions.Logging;

namespace RayBork.Internal;

/// <summary>The validated configuration that a <see cref="RayBorkAppBuilder"/> snapshots.</summary>
/// <remarks>Immutable after construction; the single-use builder cannot change it.</remarks>
internal sealed class AppConfiguration
{
    /// <summary>Gets the game whose callbacks run on the tick thread.</summary>
    public required IGame Game { get; init; }

    /// <summary>Gets the initial window title.</summary>
    public required string Title { get; init; }

    /// <summary>Gets the initial client width in logical units.</summary>
    public required int Width { get; init; }

    /// <summary>Gets the initial client height in logical units.</summary>
    public required int Height { get; init; }

    /// <summary>Gets whether the window can be resized by the user.</summary>
    public required bool Resizable { get; init; }

    /// <summary>Gets the per-tick delta time clamp, in seconds.</summary>
    public required double MaxDeltaTime { get; init; }

    /// <summary>Gets the tick cap, or zero for an uncapped loop.</summary>
    public required int TargetFps { get; init; }

    /// <summary>Gets the window state applied at startup.</summary>
    public required WindowState WindowState { get; init; }

    /// <summary>Gets the renderer options snapshot captured from the builder.</summary>
    public required RenderSettings RenderSettings { get; init; }

    /// <summary>
    /// Gets the user asset pack paths in mount order; relative paths resolve against
    /// <see cref="System.AppContext.BaseDirectory"/> when the app starts.
    /// </summary>
    public required IReadOnlyList<string> AssetPackPaths { get; init; }

    /// <summary>Gets the custom data type registrations captured from the builder.</summary>
    public required IReadOnlyList<AssetTypeRegistration> AssetTypes { get; init; }

    /// <summary>Gets the logger factory, or <see langword="null"/> for the app default.</summary>
    public required ILoggerFactory? LoggerFactory { get; init; }

    /// <summary>
    /// Gets the minimum level of the default logger factory; ignored for a caller-supplied factory.
    /// </summary>
    public required LogLevel LogLevel { get; init; }
}
