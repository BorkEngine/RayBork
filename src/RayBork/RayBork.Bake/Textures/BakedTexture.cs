namespace RayBork.Bake;

/// <summary>
/// The result of <see cref="TextureBaker.Bake"/>: a complete KTX2 2.0 payload and the descriptor
/// values that describe it.
/// </summary>
/// <remarks>
/// The payload is a self-contained KTX2 2.0 file with the full mip chain. <see cref="Format"/> and
/// <see cref="MipLevels"/> are the values a <c>TextureAssetDescriptor</c> carries so the runtime can
/// cross-check them against the KTX2 header; the payload is emitted unchanged at the sibling raw
/// entry.
/// </remarks>
public sealed class BakedTexture {
    /// <summary>Initializes a baked texture.</summary>
    /// <param name="payload">The complete KTX2 payload bytes.</param>
    /// <param name="format">The concrete format stored in the payload.</param>
    /// <param name="mipLevels">The number of mip levels in the payload.</param>
    internal BakedTexture(byte[] payload, TextureFormat format, uint mipLevels) {
        ArgumentNullException.ThrowIfNull(payload);

        Payload = payload;
        Format = format;
        MipLevels = mipLevels;
    }

    /// <summary>Gets the complete KTX2 payload bytes.</summary>
    public ReadOnlyMemory<byte> Payload { get; }

    /// <summary>Gets the concrete format stored in the payload.</summary>
    public TextureFormat Format { get; }

    /// <summary>Gets the number of mip levels in the payload, at least one.</summary>
    public uint MipLevels { get; }
}
