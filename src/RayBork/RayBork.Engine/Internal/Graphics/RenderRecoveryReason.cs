namespace RayBork.Internal;

/// <summary>
/// A recoverable renderer failure that a backend can attempt to recover from.
/// </summary>
/// <remarks>
/// Recoverable failures are handled on the render thread without calling game code. A failure that
/// cannot be recovered, an out-of-memory condition, or a recovery loop is fatal.
/// </remarks>
internal enum RenderRecoveryReason {
    /// <summary>
    /// The swapchain no longer matches the surface, for example after a resize or a display change.
    /// The swapchain and dependent images are recreated at a frame boundary.
    /// </summary>
    SwapchainOutOfDate,

    /// <summary>
    /// The driver marked the presented image as suboptimal. Presentation continues; the swapchain is
    /// recreated at the next frame boundary.
    /// </summary>
    SwapchainSuboptimal,

    /// <summary>
    /// The native surface was lost, for example after a window or display topology change. The
    /// surface is recreated through the platform, then the swapchain.
    /// </summary>
    SurfaceLost,

    /// <summary>
    /// The graphics device was lost. The device, swapchain, pipelines, and GPU resources are
    /// recreated from their retained descriptions.
    /// </summary>
    DeviceLost,
}
