namespace RayBork.Bake.Textures;

/// <summary>Maps the channel and compression choice to the concrete texture format.</summary>
/// <remarks>
/// <see cref="TextureChannelType.Rgba"/> uses sRGB storage unless linear is requested; the other
/// channel types are linear. <see cref="TextureChannelType.Rg"/> always maps to BC5.
/// </remarks>
internal static class TextureFormatMapping
{
    /// <summary>Maps a channel type and compression mode to the concrete format.</summary>
    internal static TextureFormat Map(
        TextureChannelType channelType,
        TextureCompression compression,
        bool isSrgb
    )
    {
        if (!IsDefined(channelType, compression))
        {
            throw new ArgumentOutOfRangeException(
                nameof(channelType),
                channelType,
                "The channel type and compression mode must be defined enum values."
            );
        }

        return channelType switch
        {
            TextureChannelType.Rgba => isSrgb
                ? compression == TextureCompression.Auto
                    ? TextureFormat.Bc7Srgb
                    : TextureFormat.Rgba8Srgb
                : compression == TextureCompression.Auto
                    ? TextureFormat.Bc7Unorm
                    : TextureFormat.Rgba8Unorm,
            TextureChannelType.Rgb => compression == TextureCompression.Auto
                ? TextureFormat.Bc7Unorm
                : TextureFormat.Rgba8Unorm,
            TextureChannelType.Rg => TextureFormat.Bc5Unorm,
            _ => TextureFormat.R8Unorm,
        };
    }

    /// <summary>Gets whether both values are defined enum members.</summary>
    internal static bool IsDefined(
        TextureChannelType channelType,
        TextureCompression compression
    ) => Enum.IsDefined(channelType) && Enum.IsDefined(compression);
}
