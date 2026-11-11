using BCnEncoder.Encoder;
using BCnEncoder.Shared;

namespace RayBork.Bake.Textures;

/// <summary>
/// Encodes one RGBA8 mip level with the BCnEncoder.Net block encoder.
/// </summary>
/// <remarks>
/// <para>
/// The bake uses BC7 for RGBA and opaque RGB storage and BC5 for RG storage. BC5 reads the red and
/// green components explicitly so the channel mapping does not depend on the library defaults.
/// </para>
/// <para>
/// Quality is the encoder's best setting. The input is the level's straight-alpha RGBA8 data, so the
/// caller must generate the mip chain before encoding; the encoder is told not to generate its own.
/// </para>
/// </remarks>
internal static class BcTextureEncoder {
    private static readonly PixelFormat _pixelFormat = PixelFormat.Rgba32;

    /// <summary>Encodes one level and validates the encoder's output shape.</summary>
    /// <param name="level">The straight-alpha RGBA8 level.</param>
    /// <param name="format">The compressed format to encode.</param>
    /// <returns>The raw block data for the level, without headers or padding.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="level"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="format"/> is not a block-compressed format.
    /// </exception>
    /// <exception cref="InvalidOperationException">The encoder failed or returned an unexpected shape.</exception>
    internal static byte[] Encode(TextureLevel level, TextureFormat format) {
        ArgumentNullException.ThrowIfNull(level);

        CompressionFormat compressionFormat = format switch {
            TextureFormat.Bc7Unorm or TextureFormat.Bc7Srgb => CompressionFormat.Bc7,
            TextureFormat.Bc5Unorm => CompressionFormat.Bc5,
            _ => throw new ArgumentOutOfRangeException(
                nameof(format),
                format,
                "The format is not block compressed."),
        };

        byte[] encoded;
        int encodedWidth;
        int encodedHeight;
        try {
            var encoder = new BcEncoder(compressionFormat);
            encoder.OutputOptions.GenerateMipMaps = false;
            encoder.OutputOptions.Quality = CompressionQuality.BestQuality;
            encoder.InputOptions.Bc5Component1 = ColorComponent.R;
            encoder.InputOptions.Bc5Component2 = ColorComponent.G;

            encoded = encoder.EncodeToRawBytes(
                level.Pixels,
                level.Width,
                level.Height,
                _pixelFormat,
                mipLevel: 0,
                out encodedWidth,
                out encodedHeight);
        } catch (Exception exception) when (exception is not OutOfMemoryException) {
            throw new InvalidOperationException(
                $"Block encoding failed for {level.Width}x{level.Height} {format}: "
                    + exception.Message,
                exception);
        }

        int expectedLength = TextureFormatInfo
            .For(format)
            .GetLevelByteLength(level.Width, level.Height);
        if (encodedWidth != level.Width
            || encodedHeight != level.Height
            || encoded.Length != expectedLength) {
            throw new InvalidOperationException(
                $"Block encoding returned {encodedWidth}x{encodedHeight} and {encoded.Length} bytes "
                    + $"for {level.Width}x{level.Height} {format}; expected {level.Width}x"
                    + $"{level.Height} and {expectedLength} bytes.");
        }

        return encoded;
    }
}
