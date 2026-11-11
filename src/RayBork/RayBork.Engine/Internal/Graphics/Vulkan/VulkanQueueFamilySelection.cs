namespace RayBork.Internal;

/// <summary>
/// Chooses the graphics and presentation queue families for the logical device.
/// </summary>
/// <remarks>
/// Selection is pure so it can be unit tested without a GPU. When one family supports both
/// operations, the device creates one queue for both; otherwise it creates one queue in the lowest
/// graphics family and one in the lowest presentation family.
/// </remarks>
internal static class VulkanQueueFamilySelection {
    /// <summary>
    /// Selects the graphics and presentation queue-family indices.
    /// </summary>
    /// <param name="families">The queue-family facts, in Vulkan order.</param>
    /// <returns>The selected graphics and presentation queue-family indices.</returns>
    /// <exception cref="GraphicsException">
    /// No family supports graphics or presentation. The message names the missing capability; the
    /// diagnostic code is <c>RB3001</c>.
    /// </exception>
    internal static (uint Graphics, uint Present) Select(IReadOnlyList<VulkanQueueFamilyInfo> families) {
        ArgumentNullException.ThrowIfNull(families);

        VulkanQueueFamilyInfo? combined = null;
        VulkanQueueFamilyInfo? graphics = null;
        VulkanQueueFamilyInfo? present = null;
        foreach (VulkanQueueFamilyInfo family in families) {
            if (family.SupportsGraphics && family.SupportsPresent) {
                if (combined is null || family.Index < combined.Value.Index) {
                    combined = family;
                }
            }

            if (family.SupportsGraphics && (graphics is null || family.Index < graphics.Value.Index)) {
                graphics = family;
            }

            if (family.SupportsPresent && (present is null || family.Index < present.Value.Index)) {
                present = family;
            }
        }

        if (combined is not null) {
            return (combined.Value.Index, combined.Value.Index);
        }

        if (graphics is null || present is null) {
            string missing = (graphics is null, present is null) switch {
                (true, true) => "graphics and presentation queue families",
                (true, false) => "graphics queue family",
                _ => "presentation queue family",
            };
            throw new GraphicsException(
                $"The Vulkan device has no {missing} for the window surface.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null);
        }

        return (graphics.Value.Index, present.Value.Index);
    }
}
