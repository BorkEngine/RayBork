using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace RayBork.Internal;

/// <summary>
/// Converts the staging bytes of a render target's resolved color image to the two public readback
/// results: raw scene-linear colors and display-referred RGBA8 pixels.
/// </summary>
/// <remarks>
/// <para>
/// The readback copy returns tightly packed rows in top-left origin order, which is Vulkan's texel
/// order. <see cref="DecodeColors"/> maps that buffer to <c>[y, x]</c> with the first dimension as the
/// height, so row zero is the top row. It preserves the stored values, including HDR components above
/// one, and decodes an <c>R8G8B8A8_SRGB</c> image with the sRGB transfer function; alpha is linear for
/// every supported format.
/// </para>
/// <para>
/// <see cref="EncodePixels"/> is the display-referred screenshot path: it applies the same exposure and
/// AgX transform as the present shader through <see cref="AgxToneMap.Present"/>, uses the SDR
/// <see cref="PresentEncoding.ManualSrgb"/> encoding and <see cref="WindowHdrState.Sdr"/>, and quantizes
/// the clamped result to RGBA8. The present shader writes opaque output, so alpha is
/// <see cref="byte.MaxValue"/>. Every conversion is render-thread only; the caller supplies the frame's
/// tone-mapping snapshot and the startup mode.
/// </para>
/// </remarks>
internal static class RenderTextureReadbackConverter
{
    private const int RGBA8_PIXEL_BYTES = 4;
    private const int RGBA16F_PIXEL_BYTES = 8;
    private const float BYTE_SCALE = 255f;
    private const byte OPAQUE_ALPHA = byte.MaxValue;

    /// <summary>
    /// Decodes one resolved color image to raw scene-linear colors indexed <c>[y, x]</c>.
    /// </summary>
    /// <param name="pixels">The tightly packed top-left staging rows.</param>
    /// <param name="width">The image width in pixels.</param>
    /// <param name="height">The image height in pixels.</param>
    /// <param name="format">The target's color format.</param>
    /// <returns>The raw scene-linear colors, alpha included and HDR values preserved.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A dimension is not positive or the format is not a supported render target color format.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="pixels"/> does not contain exactly one tightly packed image.
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

    /// <summary>
    /// Converts raw scene-linear colors to display-referred RGBA8 pixels in row-major top-left order.
    /// </summary>
    /// <param name="colors">The raw scene-linear colors indexed <c>[y, x]</c>.</param>
    /// <param name="tonemapping">The tone-mapping snapshot of the frame that produced the content.</param>
    /// <param name="tonemappingMode">The app's startup tone-mapping mode.</param>
    /// <returns>The display-referred RGBA8 pixels, alpha opaque.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="colors"/> is <c>null</c>.</exception>
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

    /// <summary>
    /// Creates the defined default for a target whose resolved color image was never written.
    /// </summary>
    /// <remarks>
    /// Vulkan does not allow a layout transition back to <c>UNDEFINED</c>, and an image that no frame
    /// wrote has no resolvable content. The backend therefore returns opaque black instead of copying
    /// undefined GPU memory; the target's contents remain undefined for a later frame.
    /// </remarks>
    /// <param name="width">The image width in pixels.</param>
    /// <param name="height">The image height in pixels.</param>
    /// <returns>Opaque black colors indexed <c>[y, x]</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is not positive.</exception>
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
