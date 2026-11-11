namespace RayBork.Bake.Textures;

/// <summary>
/// Maps the channel and compression choice to the concrete texture format.
/// </summary>
/// <remarks>
/// <see cref="TextureChannelType.Rgba"/> is authored color and uses the sRGB transfer function unless
/// the caller asks for linear storage; <see cref="TextureChannelType.Rgb"/>,
/// <see cref="TextureChannelType.Rg"/>, and <see cref="TextureChannelType.R"/> are linear data
/// layouts. RayBork has no uncompressed two-channel format, so <see cref="TextureChannelType.Rg"/>
/// always maps to BC5.
/// </remarks>
internal static class TextureFormatMapping {
    /// <summary>Maps a channel type and compression mode to the concrete format.</summary>
    /// <param name="channelType">The channel type.</param>
    /// <param name="compression">The compression mode.</param>
    /// <param name="isSrgb">Whether an <see cref="TextureChannelType.Rgba"/> texture is sRGB.</param>
    /// <returns>The concrete format.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A value is not a defined enum member.</exception>
    internal static TextureFormat Map(
        TextureChannelType channelType,
        TextureCompression compression,
        bool isSrgb) {
        if (!IsDefined(channelType, compression)) {
            throw new ArgumentOutOfRangeException(
                nameof(channelType),
                channelType,
                "The channel type and compression mode must be defined enum values.");
        }

        return channelType switch {
            TextureChannelType.Rgba => isSrgb
                ? compression == TextureCompression.Auto ? TextureFormat.Bc7Srgb : TextureFormat.Rgba8Srgb
                : compression == TextureCompression.Auto ? TextureFormat.Bc7Unorm : TextureFormat.Rgba8Unorm,
            TextureChannelType.Rgb => compression == TextureCompression.Auto
                ? TextureFormat.Bc7Unorm
                : TextureFormat.Rgba8Unorm,
            TextureChannelType.Rg => TextureFormat.Bc5Unorm,
            _ => TextureFormat.R8Unorm,
        };
    }

    /// <summary>Gets whether both values are defined enum members.</summary>
    /// <param name="channelType">The channel type.</param>
    /// <param name="compression">The compression mode.</param>
    /// <returns><see langword="true"/> when both values are defined.</returns>
    internal static bool IsDefined(TextureChannelType channelType, TextureCompression compression)
        => Enum.IsDefined(channelType) && Enum.IsDefined(compression);
}
