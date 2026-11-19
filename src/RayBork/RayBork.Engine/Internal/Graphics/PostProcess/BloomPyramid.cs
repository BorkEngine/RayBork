using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// Builds the half-resolution bloom pyramid and clamps every extent to at least one pixel.
/// </summary>
/// <remarks>
/// Levels halve from half the camera target to 1/32 across at most <see cref="MAX_LEVELS"/>, and a
/// repeated extent stops the pyramid, so small targets run fewer passes.
/// </remarks>
internal static class BloomPyramid
{
    /// <summary>The maximum number of downsample levels in the pyramid.</summary>
    internal const int MAX_LEVELS = 5;

    /// <summary>The maximum pass count: prefilter, four downsamples, four upsamples, and composite.</summary>
    internal const int MAX_PASSES = MAX_LEVELS * 2;

    /// <summary>Fills the span with pyramid extents from finest to coarsest and returns their count.</summary>
    /// <returns>The number of levels written, from one to <see cref="MAX_LEVELS"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The width or height is not positive.</exception>
    /// <exception cref="ArgumentException">The span is too short.</exception>
    internal static int BuildLevels(int width, int height, Span<Extent2D> levels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (levels.Length < MAX_LEVELS)
        {
            throw new ArgumentException(
                $"The pyramid level destination must hold at least {MAX_LEVELS} extents; it holds "
                    + $"{levels.Length}.",
                nameof(levels)
            );
        }

        int count = 0;
        uint currentWidth = (uint)width;
        uint currentHeight = (uint)height;
        for (int level = 0; level < MAX_LEVELS; level++)
        {
            currentWidth = Math.Max(1u, currentWidth / 2u);
            currentHeight = Math.Max(1u, currentHeight / 2u);
            if (
                count > 0
                && levels[count - 1].Width == currentWidth
                && levels[count - 1].Height == currentHeight
            )
            {
                break;
            }

            levels[count++] = new Extent2D { Width = currentWidth, Height = currentHeight };
        }

        return count;
    }
}
