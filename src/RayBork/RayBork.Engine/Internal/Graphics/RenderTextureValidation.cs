namespace RayBork.Internal;

/// <summary>Validates render target descriptions and selects the sample count.</summary>
/// <remarks>
/// Dimensions are limited to <see cref="MAX_DIMENSION"/>, and the render thread also checks the
/// device's <c>MaxImageDimension2D</c>. Sample counts fall back through
/// <see cref="VulkanSampleCountSelection.Select"/>.
/// </remarks>
internal static class RenderTextureValidation
{
    /// <summary>The largest accepted target dimension in pixels.</summary>
    internal const int MAX_DIMENSION = 16384;

    /// <summary>Validates a description before any request is queued.</summary>
    internal static void Validate(RenderTextureDescription description) =>
        Validate(
            description.Width,
            description.Height,
            description.ColorFormat,
            description.SampleCount
        );

    /// <summary>Validates dimensions, color format, and sample count together.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A dimension is not positive or exceeds <see cref="MAX_DIMENSION"/>, or the sample count is not
    /// <c>1</c>, <c>2</c>, <c>4</c>, or <c>8</c>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The color format is not <c>Rgba16Float</c>, <c>Rgba8Unorm</c>, or <c>Rgba8Srgb</c>.
    /// </exception>
    internal static void Validate(int width, int height, TextureFormat colorFormat, int sampleCount)
    {
        ValidateDimensions(width, height);
        if (!IsSupportedColorFormat(colorFormat))
        {
            throw new ArgumentException(
                $"The render texture color format '{colorFormat}' is not supported; use "
                    + "Rgba16Float, Rgba8Unorm, or Rgba8Srgb. The packed block-compressed formats and "
                    + "Depth32Float are not render-target color formats.",
                nameof(colorFormat)
            );
        }

        if (sampleCount is not (1 or 2 or 4 or 8))
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleCount),
                sampleCount,
                "The render texture sample count must be 1, 2, 4, or 8."
            );
        }
    }

    /// <summary>Validates that dimensions are positive and at most <see cref="MAX_DIMENSION"/>.</summary>
    internal static void ValidateDimensions(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, MAX_DIMENSION);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, MAX_DIMENSION);
    }

    /// <summary>Gets whether a format is a supported render-target color format.</summary>
    internal static bool IsSupportedColorFormat(TextureFormat format) =>
        format is TextureFormat.Rgba16Float or TextureFormat.Rgba8Unorm or TextureFormat.Rgba8Srgb;

    /// <summary>Gets whether a format stores linear HDR values.</summary>
    internal static bool IsHdr(TextureFormat format) => format == TextureFormat.Rgba16Float;

    /// <summary>Selects the highest supported count not above the request, or <c>1</c>.</summary>
    internal static int SelectSampleCount(int requested, IReadOnlyList<int> supported) =>
        VulkanSampleCountSelection.Select(requested, supported);
}
