namespace RayBork;

/// <summary>
/// The presentation strategy the renderer uses for the swapchain.
/// </summary>
/// <remarks>
/// <see cref="Mailbox"/> is requested by default. A surface does not support every mode, so the
/// effective mode after fallback is reported by <see cref="EngineInfo.PresentMode"/>.
/// </remarks>
public enum PresentMode {
    /// <summary>
    /// Presents the newest completed image and replaces a queued image, so the render thread does
    /// not block on the display and tearing is avoided. The default.
    /// </summary>
    Mailbox,

    /// <summary>
    /// Presents through a first-in, first-out queue and throttles to the display refresh rate. The
    /// fallback when <see cref="Mailbox"/> is unavailable.
    /// </summary>
    Fifo,

    /// <summary>
    /// Presents immediately with no queueing, which may tear. Used only when explicitly selected.
    /// </summary>
    Immediate,
}
