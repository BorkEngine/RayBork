using RayBork.Bake.Textures;

namespace RayBork.Bake;

/// <summary>
/// Builds a complete KTX2 2.0 texture payload from straight-alpha RGBA8 pixels.
/// </summary>
/// <remarks>
/// <para>
/// The helper is the one KTX2 producer for the bake: the built-in texture processor calls it for
/// decoded image sources, and a custom processor (for example one that generates a texture) calls it
/// directly so it never hand-writes KTX2 headers or block data. It generates the full mip chain with
/// a deterministic box filter, block-encodes every level when compression is requested, and returns
/// the payload together with the descriptor format and mip count.
/// </para>
/// <para>
/// Input pixels are straight-alpha RGBA8, row-major with a top-left origin. The helper applies no
/// gamma conversion, premultiplication, ICC handling, flipping, or resampling beyond the mip box
/// filter. <see cref="TextureChannelType.Rgb"/> forces alpha to <c>255</c>;
/// <see cref="TextureChannelType.Rg"/> and <see cref="TextureChannelType.R"/> keep only the channels
/// they name.
/// </para>
/// <para>
/// <see cref="TextureChannelType.Rgba"/> is authored color and is stored in the sRGB transfer
/// function by <see cref="Bake(ReadOnlySpan{byte}, int, int, TextureChannelType, TextureCompression)"/>;
/// the other channel types are linear. The built-in texture processor selects the transfer function
/// explicitly from its sidecar.
/// </para>
/// </remarks>
public static class TextureBaker {
    /// <summary>Bakes one texture into a complete KTX2 payload.</summary>
    /// <param name="rgba8Pixels">
    /// The base-level straight-alpha RGBA8 pixels, exactly <c>width × height × 4</c> bytes.
    /// </param>
    /// <param name="width">The base-level pixel width; must be positive.</param>
    /// <param name="height">The base-level pixel height; must be positive.</param>
    /// <param name="channelType">The semantic channel type.</param>
    /// <param name="compression">The compression mode.</param>
    /// <returns>
    /// The KTX2 payload and the descriptor format and mip count that describe it.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A dimension is not positive or exceeds the bake's limit, or an enum value is undefined.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The pixel buffer length is not <c>width × height × 4</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Block encoding or KTX2 container writing failed.
    /// </exception>
    public static BakedTexture Bake(
        ReadOnlySpan<byte> rgba8Pixels,
        int width,
        int height,
        TextureChannelType channelType,
        TextureCompression compression)
        => BakeCore(
            rgba8Pixels,
            width,
            height,
            channelType,
            compression,
            isSrgb: channelType == TextureChannelType.Rgba);

    /// <summary>
    /// Bakes one texture with an explicit transfer function for <see cref="TextureChannelType.Rgba"/>.
    /// </summary>
    /// <remarks>
    /// The built-in texture processor uses this overload so a sidecar's <c>srgb</c> key selects the
    /// transfer function; the public helper always stores <see cref="TextureChannelType.Rgba"/> as
    /// sRGB. The flag is ignored for the linear channel types.
    /// </remarks>
    /// <param name="rgba8Pixels">The base-level straight-alpha RGBA8 pixels.</param>
    /// <param name="width">The base-level pixel width; must be positive.</param>
    /// <param name="height">The base-level pixel height; must be positive.</param>
    /// <param name="channelType">The semantic channel type.</param>
    /// <param name="compression">The compression mode.</param>
    /// <param name="isSrgb">Whether an RGBA payload uses the sRGB transfer function.</param>
    /// <returns>The KTX2 payload and the descriptor format and mip count that describe it.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A dimension is not positive or exceeds the bake's limit, or an enum value is undefined.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The pixel buffer length is not <c>width × height × 4</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Block encoding or KTX2 container writing failed.
    /// </exception>
    internal static BakedTexture Bake(
        ReadOnlySpan<byte> rgba8Pixels,
        int width,
        int height,
        TextureChannelType channelType,
        TextureCompression compression,
        bool isSrgb)
        => BakeCore(rgba8Pixels, width, height, channelType, compression, isSrgb);

    private static BakedTexture BakeCore(
        ReadOnlySpan<byte> rgba8Pixels,
        int width,
        int height,
        TextureChannelType channelType,
        TextureCompression compression,
        bool isSrgb) {
        if (!TextureFormatMapping.IsDefined(channelType, compression)) {
            throw new ArgumentOutOfRangeException(
                nameof(channelType),
                channelType,
                "The channel type and compression mode must be defined enum values.");
        }

        if (width <= 0) {
            throw new ArgumentOutOfRangeException(nameof(width), width, "The width must be positive.");
        }

        if (width > TextureLimits.MAX_DIMENSION) {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                width,
                $"The width must be at most {TextureLimits.MAX_DIMENSION} pixels.");
        }

        if (height <= 0) {
            throw new ArgumentOutOfRangeException(nameof(height), height, "The height must be positive.");
        }

        if (height > TextureLimits.MAX_DIMENSION) {
            throw new ArgumentOutOfRangeException(
                nameof(height),
                height,
                $"The height must be at most {TextureLimits.MAX_DIMENSION} pixels.");
        }

        long expectedLength = (long)width * height * 4;
        if (rgba8Pixels.Length != expectedLength) {
            throw new ArgumentException(
                $"The RGBA8 pixel buffer is {rgba8Pixels.Length} bytes; expected {expectedLength} "
                    + "(width × height × 4).",
                nameof(rgba8Pixels));
        }

        TextureFormat format = TextureFormatMapping.Map(channelType, compression, isSrgb);
        TextureFormatInfo formatInfo = TextureFormatInfo.For(format);
        TextureLevel[] levels = TextureMipChain.Generate(rgba8Pixels, width, height, channelType);
        var storedLevels = new byte[levels.Length][];
        for (int level = 0; level < levels.Length; level++) {
            storedLevels[level] = formatInfo.IsBlockCompressed
                ? BcTextureEncoder.Encode(levels[level], format)
                : PackUncompressed(levels[level], format);
        }

        byte[] payload = Ktx2Writer.Write(formatInfo, width, height, storedLevels);
        return new BakedTexture(payload, format, (uint)levels.Length);
    }

    private static byte[] PackUncompressed(TextureLevel level, TextureFormat format) {
        switch (format) {
            case TextureFormat.Rgba8Unorm:
            case TextureFormat.Rgba8Srgb:
                return level.Pixels;

            case TextureFormat.R8Unorm:
                var red = new byte[level.Width * level.Height];
                for (int pixel = 0; pixel < red.Length; pixel++) {
                    red[pixel] = level.Pixels[pixel * 4];
                }

                return red;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(format),
                    format,
                    "The format is not an uncompressed texture format.");
        }
    }
}
