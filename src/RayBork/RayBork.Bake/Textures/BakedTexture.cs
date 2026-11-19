namespace RayBork.Bake;

/// <summary>A complete KTX2 2.0 texture payload.</summary>
/// <remarks>
/// Produced by <see cref="TextureBaker.Bake"/>: a self-contained KTX2 2.0 file with the full mip
/// chain. <see cref="Format"/> and <see cref="MipLevels"/> must match the KTX2 header values.
/// </remarks>
public sealed class BakedTexture
{
    /// <summary>Initializes a baked texture.</summary>
    internal BakedTexture(byte[] payload, TextureFormat format, uint mipLevels)
    {
        ArgumentNullException.ThrowIfNull(payload);

        Payload = payload;
        Format = format;
        MipLevels = mipLevels;
    }

    /// <summary>Gets the complete KTX2 payload bytes.</summary>
    public ReadOnlyMemory<byte> Payload { get; }

    /// <summary>Gets the concrete format stored in the payload.</summary>
    public TextureFormat Format { get; }

    /// <summary>Gets the mip level count, at least one.</summary>
    public uint MipLevels { get; }
}
