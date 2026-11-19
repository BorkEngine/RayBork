namespace RayBork.Internal;

/// <summary>Identifies a slot generation in a <see cref="FrameRing"/>.</summary>
/// <remarks>A ring lease carries the slot's packet; a hand-built lease has none.</remarks>
internal readonly struct FrameLease
{
    /// <summary>Initializes a lease without a packet.</summary>
    internal FrameLease(int slotIndex, long frameIndex)
    {
        SlotIndex = slotIndex;
        FrameIndex = frameIndex;
        Packet = null;
    }

    /// <summary>Initializes a lease with the slot's reusable frame packet.</summary>
    internal FrameLease(int slotIndex, long frameIndex, RenderFramePacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        SlotIndex = slotIndex;
        FrameIndex = frameIndex;
        Packet = packet;
    }

    /// <summary>Gets the three-slot ring index.</summary>
    public int SlotIndex { get; }

    /// <summary>Gets the frame generation that owns the slot.</summary>
    public long FrameIndex { get; }

    /// <summary>Gets the slot's reusable packet, or <see langword="null"/> when absent.</summary>
    internal RenderFramePacket? Packet { get; }
}
