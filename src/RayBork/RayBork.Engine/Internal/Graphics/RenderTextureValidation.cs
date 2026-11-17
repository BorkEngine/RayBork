namespace RayBork.Internal;

/// <summary>
/// Validates a <see cref="RenderTextureDescription"/> and selects its effective sample count.
/// </summary>
/// <remarks>
/// The limits and fallback rule match the procedural texture path: the bake's <c>16384</c> maximum
/// dimension is the public limit, and the sample count uses
/// <see cref="VulkanSampleCountSelection.Select"/>. The render thread additionally checks the device's
/// <c>MaxImageDimension2D</c> before it allocates the images.
/// </remarks>
internal static class RenderTextureValidation
{
    /// <summary>The largest accepted target width or height in pixels. It matches the texture bake limit.</summary>
    internal const int MAX_DIMENSION = 16384;

    /// <summary>
    /// Validates the description before any request is queued.
    /// </summary>
    /// <param name="description">The description to validate.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A dimension is not positive or exceeds <see cref="MAX_DIMENSION"/>, or the sample count is not
    /// <c>1</c>, <c>2</c>, <c>4</c>, or <c>8</c>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The color format is not <see cref="TextureFormat.Rgba16Float"/>,
    /// <see cref="TextureFormat.Rgba8Unorm"/>, or <see cref="TextureFormat.Rgba8Srgb"/>.
    /// </exception>
    internal static void Validate(RenderTextureDescription description) =>
        Validate(
            description.Width,
            description.Height,
            description.ColorFormat,
            description.SampleCount
        );

    /// <summary>
    /// Validates the description components.
    /// </summary>
    /// <param name="width">The target width in pixels.</param>
    /// <param name="height">The target height in pixels.</param>
    /// <param name="colorFormat">The target color format.</param>
    /// <param name="sampleCount">The requested MSAA sample count.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A dimension is not positive or exceeds <see cref="MAX_DIMENSION"/>, or the sample count is not
    /// <c>1</c>, <c>2</c>, <c>4</c>, or <c>8</c>.
    /// </exception>
    /// <exception cref="ArgumentException">The color format is not a supported render-target format.</exception>
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

    /// <summary>
    /// Validates that a render target width and height are positive and within the public limit.
    /// </summary>
    /// <param name="width">The target width in pixels.</param>
    /// <param name="height">The target height in pixels.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A dimension is not positive or exceeds <see cref="MAX_DIMENSION"/>.
    /// </exception>
    internal static void ValidateDimensions(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, MAX_DIMENSION);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, MAX_DIMENSION);
    }

    /// <summary>
    /// Gets whether a format is a supported render-target color format.
    /// </summary>
    /// <param name="format">The color format.</param>
    /// <returns><see langword="true"/> for the three uncompressed RGBA render-target formats.</returns>
    internal static bool IsSupportedColorFormat(TextureFormat format) =>
        format is TextureFormat.Rgba16Float or TextureFormat.Rgba8Unorm or TextureFormat.Rgba8Srgb;

    /// <summary>
    /// Gets whether a render-target color format stores linear HDR values.
    /// </summary>
    /// <param name="format">The color format.</param>
    /// <returns><see langword="true"/> for <see cref="TextureFormat.Rgba16Float"/>.</returns>
    internal static bool IsHdr(TextureFormat format) => format == TextureFormat.Rgba16Float;

    /// <summary>
    /// Selects the effective sample count with the documented fallback rule.
    /// </summary>
    /// <param name="requested">The requested sample count.</param>
    /// <param name="supported">The sample counts the device supports.</param>
    /// <returns>The highest supported count that does not exceed the request, or <c>1</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="requested"/> is not positive.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="supported"/> is <c>null</c>.</exception>
    internal static int SelectSampleCount(int requested, IReadOnlyList<int> supported) =>
        VulkanSampleCountSelection.Select(requested, supported);
}
