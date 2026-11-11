namespace RayBork.Internal;

/// <summary>
/// Chooses the effective MSAA sample count from the counts a device supports.
/// </summary>
/// <remarks>
/// The rule matches the documented renderer behavior: use the highest supported count that does not
/// exceed the request, and fall back to one sample when the request is below every supported count.
/// </remarks>
internal static class VulkanSampleCountSelection {
    /// <summary>
    /// Selects the effective sample count.
    /// </summary>
    /// <param name="requested">The requested sample count; it must be positive.</param>
    /// <param name="supported">The sample counts the device supports.</param>
    /// <returns>The effective sample count, which is always at least one.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="requested"/> is not positive.
    /// </exception>
    internal static int Select(int requested, IReadOnlyList<int> supported) {
        if (requested <= 0) {
            throw new ArgumentOutOfRangeException(
                nameof(requested),
                requested,
                "The requested sample count must be positive.");
        }

        ArgumentNullException.ThrowIfNull(supported);

        int selected = 1;
        foreach (int sampleCount in supported) {
            if (sampleCount > requested || sampleCount <= selected) {
                continue;
            }

            selected = sampleCount;
        }

        return selected;
    }
}
