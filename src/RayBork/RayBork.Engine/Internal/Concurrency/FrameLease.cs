namespace RayBork.Internal;

/// <summary>
/// Identifies a slot generation in a <see cref="FrameRing"/>. A lease is valid only for the ring and
/// state transition that produced it; a stale or default lease is rejected by the ring.
/// </summary>
/// <remarks>
/// A lease produced by the ring also carries the slot's reusable <see cref="RenderFramePacket"/>. The
/// producer records into it and publishes it; the consumer reads it while it holds the rendering lease.
/// A default or hand-constructed lease has no packet.
/// </remarks>
internal readonly struct FrameLease {
    /// <summary>
    /// Initializes a lease for <paramref name="slotIndex"/> and <paramref name="frameIndex"/> without a
    /// packet. Leases are normally produced by the ring, which attaches the slot packet.
    /// </summary>
    internal FrameLease(int slotIndex, long frameIndex) {
        SlotIndex = slotIndex;
        FrameIndex = frameIndex;
        Packet = null;
    }

    /// <summary>
    /// Initializes a lease with the slot's reusable frame packet.
    /// </summary>
    internal FrameLease(int slotIndex, long frameIndex, RenderFramePacket packet) {
        ArgumentNullException.ThrowIfNull(packet);
        SlotIndex = slotIndex;
        FrameIndex = frameIndex;
        Packet = packet;
    }

    /// <summary>
    /// Gets the three-slot ring index.
    /// </summary>
    public int SlotIndex { get; }

    /// <summary>
    /// Gets the frame generation that owns the slot.
    /// </summary>
    public long FrameIndex { get; }

    /// <summary>
    /// Gets the slot's reusable frame packet, or <see langword="null"/> for a hand-constructed lease.
    /// The producer writes it while recording; the consumer reads it while rendering.
    /// </summary>
    internal RenderFramePacket? Packet { get; }
}
