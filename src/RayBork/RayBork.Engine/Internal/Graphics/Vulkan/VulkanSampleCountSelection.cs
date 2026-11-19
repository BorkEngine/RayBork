namespace RayBork.Internal;

/// <summary>Chooses the effective MSAA sample count from the supported counts.</summary>
/// <remarks>
/// The result is the highest supported count not above the request, or one sample when every
/// supported count exceeds the request.
/// </remarks>
internal static class VulkanSampleCountSelection
{
    /// <summary>Selects the effective sample count, which is always at least one.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="requested"/> is not positive.
    /// </exception>
    internal static int Select(int requested, IReadOnlyList<int> supported)
    {
        if (requested <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requested),
                requested,
                "The requested sample count must be positive."
            );
        }

        ArgumentNullException.ThrowIfNull(supported);

        int selected = 1;
        foreach (int sampleCount in supported)
        {
            if (sampleCount > requested || sampleCount <= selected)
            {
                continue;
            }

            selected = sampleCount;
        }

        return selected;
    }
}
