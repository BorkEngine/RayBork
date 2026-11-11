using Microsoft.Extensions.Logging;

namespace RayBork.Internal;

/// <summary>
/// The internal renderer seam. The render thread owns the implementation; every member except
/// <see cref="Info"/> and <see cref="Stats"/> is called on the render thread only.
/// </summary>
/// <remarks>
/// <para>
/// The seam is per frame or per resource decision, never per draw: there is no draw, uniform, or
/// texture-barrier method. The tick thread never calls the backend, and the backend never calls
/// game code. The implementation owns the GPU device, swapchain, pipelines, and resources; the
/// native surface is created and destroyed through <see cref="RendererBackendOptions.SurfaceProvider"/>.
/// </para>
/// <para>
/// A recoverable failure is handled inside the implementation, which recreates the affected
/// resources and drops the frame instead of presenting a partial image. A failure it cannot recover
/// throws <see cref="GraphicsException"/> (or a subclass); the app stops and rethrows it.
/// </para>
/// </remarks>
internal interface IRendererBackend : IDisposable {
    /// <summary>
    /// Gets the immutable renderer, device, and presentation snapshot. It is valid after
    /// <see cref="Initialize"/> and is refreshed when a recovery changes the device or presentation
    /// state.
    /// </summary>
    EngineInfo Info { get; }

    /// <summary>
    /// Gets the statistics of the last completed frame. It is written by the render thread and read
    /// by the render host, which publishes it to <see cref="EngineContext.Stats"/>.
    /// </summary>
    RenderStats Stats { get; }

    /// <summary>
    /// Creates the GPU device, swapchain, and fixed render state.
    /// </summary>
    /// <remarks>
    /// Runs on the render thread after the main thread has created the window. The surface is
    /// created through the options' surface provider during initialization. Called once; a failure
    /// is fatal for the app.
    /// </remarks>
    /// <param name="options">The renderer configuration and surface seams.</param>
    /// <exception cref="GraphicsException">Device or presentation initialization failed.</exception>
    void Initialize(RendererBackendOptions options);

    /// <summary>
    /// Updates the presentation size after the window changed size.
    /// </summary>
    /// <remarks>
    /// Runs on the render thread. Recreates the swapchain and the render targets whose size changed
    /// at the next frame boundary; in-flight frames are retired first. Idempotent for an unchanged
    /// size; a zero width or height marks the surface as having no drawable extent until the next
    /// non-zero resize.
    /// </remarks>
    /// <param name="renderWidth">The new physical render width in pixels.</param>
    /// <param name="renderHeight">The new physical render height in pixels.</param>
    void Resize(int renderWidth, int renderHeight);

    /// <summary>
    /// Consumes one sealed frame: waits for a frame slot, acquires the presentation image, records
    /// and submits the passes, and tracks the frame fence.
    /// </summary>
    /// <remarks>
    /// Runs on the render thread. Does not call game code and does not present; <see cref="Present"/>
    /// publishes the newest submitted frame. While the surface is minimized or has no extent the
    /// frame is skipped and no image is acquired.
    /// </remarks>
    /// <param name="frame">The sealed frame packet.</param>
    void Submit(RenderFramePacket frame);

    /// <summary>
    /// Presents the newest submitted frame, or idles when the surface is minimized, has no extent,
    /// or no frame was submitted.
    /// </summary>
    /// <remarks>Runs on the render thread.</remarks>
    void Present();
}
