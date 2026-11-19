namespace RayBork.Bake.Textures;

/// <summary>A Khronos DFD sample: the bit layout and range of one channel.</summary>
/// <remarks>
/// Sample positions are always zero for the formats this bake writes, so they are not stored. The
/// channel type is the low nibble of the DFD channel byte.
/// </remarks>
internal readonly record struct DfdSample(
    ushort BitOffset,
    byte BitLength,
    byte ChannelType,
    uint Lower,
    uint Upper
);
