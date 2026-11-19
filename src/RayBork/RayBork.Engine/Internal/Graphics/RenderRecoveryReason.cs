namespace RayBork.Internal;

/// <summary>A recoverable renderer failure that a backend can attempt to recover from.</summary>
/// <remarks>
/// Failures are handled on the render thread without calling game code; an unrecoverable failure,
/// out-of-memory condition, or recovery loop is fatal.
/// </remarks>
internal enum RenderRecoveryReason
{
    /// <summary>The swapchain is out of date; recreate it at a frame boundary.</summary>
    SwapchainOutOfDate,

    /// <summary>The image is suboptimal; recreate the swapchain at the next frame.</summary>
    SwapchainSuboptimal,

    /// <summary>The surface was lost; recreate it and the swapchain.</summary>
    SurfaceLost,

    /// <summary>The graphics device was lost; recreate it and all GPU resources.</summary>
    DeviceLost,
}
