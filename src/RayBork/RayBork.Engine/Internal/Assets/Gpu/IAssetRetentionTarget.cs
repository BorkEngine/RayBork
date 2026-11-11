namespace RayBork.Internal;

/// <summary>
/// An engine object whose GPU lifetime is extended by a frame that references it.
/// </summary>
/// <remarks>
/// <para>
/// A camera recording a draw calls <see cref="AddFrameHold"/> for every engine asset it records; the
/// frame ring releases the matching <see cref="ReleaseFrameHold"/> only after the consuming frame has
/// been presented and its slot is released. An asset disposed while a frame still holds it defers its
/// GPU destruction until the last hold is gone.
/// </para>
/// <para>
/// Both members are called from more than one thread: <see cref="AddFrameHold"/> on the tick thread
/// while a frame is recording, and <see cref="ReleaseFrameHold"/> on the render thread after the
/// frame completes. Implementations must be safe to call concurrently, and
/// <see cref="ReleaseFrameHold"/> must not run while the frame ring lock is held.
/// </para>
/// </remarks>
internal interface IAssetRetentionTarget {
    /// <summary>
    /// Adds one frame hold. The matching <see cref="ReleaseFrameHold"/> is called exactly once.
    /// </summary>
    void AddFrameHold();

    /// <summary>
    /// Releases one frame hold. It may queue the asset's GPU destruction on the render thread; the
    /// frame ring calls it outside its own lock for that reason.
    /// </summary>
    void ReleaseFrameHold();
}
