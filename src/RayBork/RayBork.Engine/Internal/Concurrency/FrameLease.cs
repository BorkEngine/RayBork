namespace RayBork.Internal;

/// <summary>
/// Identifies a slot generation in a <see cref="FrameRing"/>. A lease is valid only for the ring and
/// state transition that produced it; a stale or default lease is rejected by the ring.
/// </summary>
internal readonly struct FrameLease {
    /// <summary>
    /// Initializes a lease for <paramref name="slotIndex"/> and <paramref name="frameIndex"/>.
    /// Leases are normally produced by the ring, not constructed by callers.
    /// </summary>
    internal FrameLease(int slotIndex, long frameIndex) {
        SlotIndex = slotIndex;
        FrameIndex = frameIndex;
    }

    /// <summary>
    /// Gets the three-slot ring index.
    /// </summary>
    public int SlotIndex { get; }

    /// <summary>
    /// Gets the frame generation that owns the slot.
    /// </summary>
    public long FrameIndex { get; }
}
