using BCnEncoder.Encoder;
using BCnEncoder.Shared;

namespace RayBork.Bake.Textures;

/// <summary>Encodes one RGBA8 mip level with the BCnEncoder.Net block encoder.</summary>
/// <remarks>
/// BC7 stores RGBA and opaque RGB; BC5 stores RG explicitly. The caller supplies the finished mip
/// chain and the encoder runs at best quality without generating mips.
/// </remarks>
internal static class BcTextureEncoder
{
    private static readonly PixelFormat _pixelFormat = PixelFormat.Rgba32;

    /// <summary>Encodes one level and checks the encoder's output shape.</summary>
    /// <returns>The raw block data for the level, without headers or padding.</returns>
    internal static byte[] Encode(TextureLevel level, TextureFormat format)
    {
        ArgumentNullException.ThrowIfNull(level);

        CompressionFormat compressionFormat = format switch
        {
            TextureFormat.Bc7Unorm or TextureFormat.Bc7Srgb => CompressionFormat.Bc7,
            TextureFormat.Bc5Unorm => CompressionFormat.Bc5,
            _ => throw new ArgumentOutOfRangeException(
                nameof(format),
                format,
                "The format is not block compressed."
            ),
        };

        byte[] encoded;
        int encodedWidth;
        int encodedHeight;
        try
        {
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
                out encodedHeight
            );
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw new InvalidOperationException(
                $"Block encoding failed for {level.Width}x{level.Height} {format}: "
                    + exception.Message,
                exception
            );
        }

        int expectedLength = TextureFormatInfo
            .For(format)
            .GetLevelByteLength(level.Width, level.Height);
        if (
            encodedWidth != level.Width
            || encodedHeight != level.Height
            || encoded.Length != expectedLength
        )
        {
            throw new InvalidOperationException(
                $"Block encoding returned {encodedWidth}x{encodedHeight} and {encoded.Length} bytes "
                    + $"for {level.Width}x{level.Height} {format}; expected {level.Width}x"
                    + $"{level.Height} and {expectedLength} bytes."
            );
        }

        return encoded;
    }
}
