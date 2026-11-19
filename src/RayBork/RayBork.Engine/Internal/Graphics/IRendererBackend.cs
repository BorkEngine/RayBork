using Microsoft.Extensions.Logging;

namespace RayBork.Internal;

/// <summary>
/// The internal renderer seam, owned and driven by the render thread.
/// </summary>
/// <remarks>
/// Work is per frame or per resource decision, never per draw; the backend never calls game code.
/// A failure it cannot recover throws <see cref="GraphicsException"/> and the app stops.
/// </remarks>
internal interface IRendererBackend : IDisposable
{
    /// <summary>
    /// Gets the immutable renderer, device, and presentation snapshot, refreshed on recovery.
    /// </summary>
    EngineInfo Info { get; }

    /// <summary>
    /// Gets the statistics of the last completed frame, written by the render thread.
    /// </summary>
    RenderStats Stats { get; }

    /// <summary>
    /// Gets the logical-device generation: <c>0</c> before initialization, <c>1</c> for the first
    /// device, and incremented only when device loss recreates the logical device.
    /// </summary>
    /// <remarks>GPU objects created before an increment belong to a destroyed device.</remarks>
    int DeviceGeneration { get; }

    /// <summary>
    /// Creates the GPU device, swapchain, and fixed render state once; a failure is fatal.
    /// </summary>
    void Initialize(RendererBackendOptions options);

    /// <summary>
    /// Resizes presentation at the next frame boundary, retiring in-flight frames first.
    /// </summary>
    /// <remarks>Idempotent when unchanged; a zero extent means no drawable surface.</remarks>
    void Resize(int renderWidth, int renderHeight);

    /// <summary>
    /// Services queued render-texture creation, resize, readback, and release on the render thread.
    /// </summary>
    /// <remarks>
    /// Each request completes with a <see cref="RenderTexture"/> or its failure; a readback of a
    /// never-drawn target completes with opaque black. A failed request never stops the loop.
    /// </remarks>
    void ProcessRenderTextureRequests(RenderTextureService service);

    /// <summary>
    /// Records and submits one sealed frame without presenting; a frame with no extent is skipped.
    /// </summary>
    void Submit(RenderFramePacket frame);

    /// <summary>
    /// Presents the newest submitted frame, or idles when there is nothing to present.
    /// </summary>
    void Present();

    /// <summary>
    /// Attempts one recovery from a recoverable failure and resumes with the newest completed tick.
    /// </summary>
    /// <remarks>Recovery is bounded; a repeated failure loop is unrecoverable.</remarks>
    /// <returns>
    /// <see langword="true"/> if the renderer recovered; otherwise the failure is fatal.
    /// </returns>
    bool Recover(RenderRecoveryReason reason);
}
