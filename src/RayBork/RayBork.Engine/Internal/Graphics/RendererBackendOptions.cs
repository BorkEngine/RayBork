using Microsoft.Extensions.Logging;

namespace RayBork.Internal;

/// <summary>
/// The configuration a <see cref="IRendererBackend"/> receives when it initializes.
/// </summary>
/// <remarks>
/// The value is built on the main thread from the app configuration and the platform snapshots, then
/// consumed once on the render thread. The instance extension list must be obtained from the
/// platform on the main thread before the renderer starts; the surface provider marshals surface
/// creation and destruction to the main thread. The render thread enqueues teardown work that needs
/// the main thread on <see cref="TeardownQueue"/>; the main thread drains it after the render thread
/// has stopped.
/// </remarks>
internal sealed class RendererBackendOptions {
    /// <summary>
    /// Gets the requested present mode, sample count, tone mapping, exposure, validation, and scene
    /// clear color.
    /// </summary>
    public required RenderSettings Settings { get; init; }

    /// <summary>
    /// Gets the Vulkan instance extensions the platform requires for its surface, in the order the
    /// platform reported them.
    /// </summary>
    public required IReadOnlyList<string> InstanceExtensions { get; init; }

    /// <summary>
    /// Gets the surface seam that creates and destroys the platform's native presentation surface.
    /// </summary>
    public required IRendererSurfaceProvider SurfaceProvider { get; init; }

    /// <summary>
    /// Gets the render-thread snapshot provider for the presentation surface. It is called on the
    /// render thread and never blocks.
    /// </summary>
    public required Func<WindowSurfaceState> WindowStateProvider { get; init; }

    /// <summary>
    /// Gets the logger for the renderer category.
    /// </summary>
    public required ILogger Logger { get; init; }

    /// <summary>
    /// Gets the queue for teardown work that must run on the main thread after the render thread has
    /// stopped, such as destroying the Vulkan instance and its loader.
    /// </summary>
    public required MainThreadTeardownQueue TeardownQueue { get; init; }
}
