namespace RayBork.Internal;

/// <summary>
/// A sealed frame handed from the tick thread to the renderer.
/// </summary>
/// <remarks>
/// The tick thread seals the packet before publishing it, and the render thread consumes it. The
/// packet never references a live game object: it carries value snapshots only. M2 has no draw data,
/// so the packet identifies the frame; M4 adds the camera passes and their captured shader state.
/// </remarks>
/// <param name="FrameIndex">The tick frame index assigned by the frame ring.</param>
internal readonly record struct RenderFramePacket(long FrameIndex);
