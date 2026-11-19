using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace RayBork.Internal;

/// <summary>
/// Converts readback staging bytes between scene-linear colors and display-referred RGBA8 pixels.
/// </summary>
/// <remarks>
/// Rows are tightly packed in top-left order, and color results index <c>[y, x]</c> with the first
/// dimension as height. Encoding applies the present shader's exposure and AgX transform and
/// writes opaque alpha.
/// </remarks>
internal static class RenderTextureReadbackConverter
{
    private const int RGBA8_PIXEL_BYTES = 4;
    private const int RGBA16F_PIXEL_BYTES = 8;
    private const float BYTE_SCALE = 255f;
    private const byte OPAQUE_ALPHA = byte.MaxValue;

    /// <summary>Decodes one resolved color image to raw colors indexed <c>[y, x]</c>.</summary>
    /// <returns>Scene-linear colors; alpha is linear and HDR values are preserved.</returns>
    /// <exception cref="ArgumentException">
    /// The buffer is not exactly one tightly packed image of the given size and format.
    /// </exception>
    [SuppressMessage(
        "Performance",
        "CA1814",
        Justification = "The public ReadColorsAsync contract requires a rectangular [y, x] Color array; a jagged array would change the API."
    )]
    internal static Color[,] DecodeColors(
        ReadOnlySpan<byte> pixels,
        int width,
        int height,
        TextureFormat format
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        int pixelBytes = GetPixelBytes(format);
        int expectedBytes = checked(width * height * pixelBytes);
        if (pixels.Length != expectedBytes)
        {
            throw new ArgumentException(
                $"The readback buffer has {pixels.Length} byte(s), but a {width}x{height} "
                    + $"{format} image needs {expectedBytes}.",
                nameof(pixels)
            );
        }

        Color[,] colors = new Color[height, width];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int offset = ((y * width) + x) * pixelBytes;
                colors[y, x] = DecodePixel(pixels.Slice(offset, pixelBytes), format);
            }
        }

        return colors;
    }

    /// <summary>Converts scene-linear colors to display-referred RGBA8 pixels.</summary>
    [SuppressMessage(
        "Performance",
        "CA1814",
        Justification = "The public ReadColorsAsync contract requires a rectangular [y, x] Color array; a jagged array would change the API."
    )]
    internal static byte[] EncodePixels(
        Color[,] colors,
        TonemappingSnapshot tonemapping,
        TonemappingMode tonemappingMode
    )
    {
        ArgumentNullException.ThrowIfNull(colors);

        int height = colors.GetLength(0);
        int width = colors.GetLength(1);
        byte[] pixels = new byte[checked(width * height * RGBA8_PIXEL_BYTES)];
        float exposureMultiplier = MathF.Pow(2f, tonemapping.Exposure);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color color = colors[y, x];
                Vector3 present = AgxToneMap.Present(
                    new Vector3(color.R, color.G, color.B),
                    exposureMultiplier,
                    tonemappingMode,
                    PresentEncoding.ManualSrgb,
                    WindowHdrState.Sdr,
                    tonemapping.AgxWhite,
                    tonemapping.AgxContrast
                );
                int offset = ((y * width) + x) * RGBA8_PIXEL_BYTES;
                pixels[offset] = ToByte(present.X);
                pixels[offset + 1] = ToByte(present.Y);
                pixels[offset + 2] = ToByte(present.Z);
                pixels[offset + 3] = OPAQUE_ALPHA;
            }
        }

        return pixels;
    }

    /// <summary>Creates the opaque-black result for a target that no frame wrote.</summary>
    /// <remarks>
    /// Vulkan cannot restore <c>UNDEFINED</c>, so the readback returns black instead of undefined
    /// memory.
    /// </remarks>
    [SuppressMessage(
        "Performance",
        "CA1814",
        Justification = "The public ReadColorsAsync contract requires a rectangular [y, x] Color array; a jagged array would change the API."
    )]
    internal static Color[,] CreateOpaqueBlackColors(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        Color[,] colors = new Color[height, width];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                colors[y, x] = Color.Black;
            }
        }

        return colors;
    }

    private static Color DecodePixel(ReadOnlySpan<byte> pixel, TextureFormat format) =>
        format switch
        {
            TextureFormat.Rgba16Float => new Color(
                (float)BitConverter.ToHalf(pixel[..2]),
                (float)BitConverter.ToHalf(pixel.Slice(2, 2)),
                (float)BitConverter.ToHalf(pixel.Slice(4, 2)),
                (float)BitConverter.ToHalf(pixel.Slice(6, 2))
            ),
            TextureFormat.Rgba8Unorm => new Color(
                pixel[0] / BYTE_SCALE,
                pixel[1] / BYTE_SCALE,
                pixel[2] / BYTE_SCALE,
                pixel[3] / BYTE_SCALE
            ),
            TextureFormat.Rgba8Srgb => new Color(
                AgxToneMap.SrgbDecodeChannel(pixel[0]),
                AgxToneMap.SrgbDecodeChannel(pixel[1]),
                AgxToneMap.SrgbDecodeChannel(pixel[2]),
                pixel[3] / BYTE_SCALE
            ),
            _ => throw new ArgumentOutOfRangeException(
                nameof(format),
                format,
                "The format is not a supported render texture color format."
            ),
        };

    private static int GetPixelBytes(TextureFormat format) =>
        format switch
        {
            TextureFormat.Rgba16Float => RGBA16F_PIXEL_BYTES,
            TextureFormat.Rgba8Unorm or TextureFormat.Rgba8Srgb => RGBA8_PIXEL_BYTES,
            _ => throw new ArgumentOutOfRangeException(
                nameof(format),
                format,
                "The format is not a supported render texture color format."
            ),
        };

    private static byte ToByte(float channel) =>
        (byte)
            Math.Clamp(
                (int)MathF.Round(channel * BYTE_SCALE, MidpointRounding.AwayFromZero),
                byte.MinValue,
                OPAQUE_ALPHA
            );
}
