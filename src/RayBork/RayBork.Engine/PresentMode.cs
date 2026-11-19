namespace RayBork;

/// <summary>The presentation strategy for the swapchain.</summary>
/// <remarks>
/// The engine requests <see cref="Mailbox"/> by default; the effective mode after fallback is
/// reported by <see cref="EngineInfo.PresentMode"/>.
/// </remarks>
public enum PresentMode
{
    /// <summary>Presents the newest image without blocking or tearing; the default.</summary>
    Mailbox,

    /// <summary>A display-throttled queue; the fallback for <see cref="Mailbox"/>.</summary>
    Fifo,

    /// <summary>Presents immediately and may tear.</summary>
    Immediate,
}
