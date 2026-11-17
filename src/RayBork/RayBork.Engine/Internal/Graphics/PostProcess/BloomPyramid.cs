using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// Builds the half-resolution pyramid of the built-in bloom effect and clamps every extent to at least
/// one pixel.
/// </summary>
/// <remarks>
/// <para>
/// The first level is half the camera target and the following levels halve it again, for at most
/// <see cref="MAX_LEVELS"/> levels (half, quarter, eighth, 1/16, and 1/32). A target small enough that
/// the next halving no longer shrinks the extent stops the pyramid there, so a level that would repeat
/// the previous extent is skipped instead of re-blurring the same one-pixel image. The effect therefore
/// runs fewer passes on a small target and never rents more than the per-extent three-target set.
/// </para>
/// <para>
/// The method is pure and allocation-free beyond the caller's span, so the snapshot can cache its level
/// array and a unit test can assert the extents without a GPU.
/// </para>
/// </remarks>
internal static class BloomPyramid
{
    /// <summary>The maximum number of downsample levels in the pyramid.</summary>
    internal const int MAX_LEVELS = 5;

    /// <summary>
    /// The maximum number of recorded passes: the prefilter, one downsample per level after the first,
    /// one upsample per level after the first, and the composite.
    /// </summary>
    internal const int MAX_PASSES = MAX_LEVELS * 2;

    /// <summary>
    /// Fills <paramref name="levels"/> with the pyramid extents from finest to coarsest and returns how
    /// many levels the target uses.
    /// </summary>
    /// <param name="width">The camera target width in pixels; positive.</param>
    /// <param name="height">The camera target height in pixels; positive.</param>
    /// <param name="levels">The destination span; it must hold at least <see cref="MAX_LEVELS"/> entries.</param>
    /// <returns>The number of levels written, from one to <see cref="MAX_LEVELS"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> or <paramref name="height"/> is not positive.</exception>
    /// <exception cref="ArgumentException"><paramref name="levels"/> is too short.</exception>
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
