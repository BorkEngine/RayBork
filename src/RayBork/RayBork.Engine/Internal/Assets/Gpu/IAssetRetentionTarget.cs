namespace RayBork.Internal;

/// <summary>
/// An engine object whose GPU lifetime is extended by a frame that references it.
/// </summary>
/// <remarks>
/// The frame ring releases each hold only after the consuming frame's slot is released.
/// Implementations must be thread-safe and must not release while the frame ring lock is held.
/// </remarks>
internal interface IAssetRetentionTarget
{
    /// <summary>Adds one frame hold; its matching release runs exactly once.</summary>
    void AddFrameHold();

    /// <summary>Releases one frame hold and may queue GPU destruction.</summary>
    void ReleaseFrameHold();
}
