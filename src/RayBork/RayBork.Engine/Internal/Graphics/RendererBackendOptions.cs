using Microsoft.Extensions.Logging;

namespace RayBork.Internal;

/// <summary>
/// The configuration a <see cref="IRendererBackend"/> receives when it initializes.
/// </summary>
/// <remarks>
/// Built on the main thread from the app configuration and platform snapshots, then consumed once
/// on the render thread. Main-thread work drains through <see cref="TeardownQueue"/> after the
/// render thread stops.
/// </remarks>
internal sealed class RendererBackendOptions
{
    /// <summary>Gets the requested settings.</summary>
    public required RenderSettings Settings { get; init; }

    /// <summary>Gets the required Vulkan instance extensions in reported order.</summary>
    public required IReadOnlyList<string> InstanceExtensions { get; init; }

    /// <summary>Gets the seam that creates and destroys the native presentation surface.</summary>
    public required IRendererSurfaceProvider SurfaceProvider { get; init; }

    /// <summary>Gets the render-thread presentation state provider; it never blocks.</summary>
    public required Func<WindowSurfaceState> WindowStateProvider { get; init; }

    /// <summary>Gets the logger for the renderer category.</summary>
    public required ILogger Logger { get; init; }

    /// <summary>Gets the queue for teardown work that must run on the main thread.</summary>
    public required MainThreadTeardownQueue TeardownQueue { get; init; }
}
