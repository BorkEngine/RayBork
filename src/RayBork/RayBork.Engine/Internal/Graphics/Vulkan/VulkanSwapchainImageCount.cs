namespace RayBork.Internal;

/// <summary>
/// Chooses the number of swapchain images.
/// </summary>
/// <remarks>
/// The policy is pure so it can be unit tested without a GPU. Three images bound the renderer's
/// run-ahead while keeping a mailbox or FIFO swapchain fed; the surface's minimum and maximum image
/// counts always win.
/// </remarks>
internal static class VulkanSwapchainImageCount {
    private const uint PREFERRED_IMAGE_COUNT = 3;

    /// <summary>
    /// Selects the swapchain image count.
    /// </summary>
    /// <param name="minImageCount">The minimum image count the surface requires.</param>
    /// <param name="maxImageCount">The maximum image count, or zero when the surface sets no limit.</param>
    /// <returns>The selected image count, never below <paramref name="minImageCount"/>.</returns>
    internal static uint Select(uint minImageCount, uint maxImageCount) {
        uint count = Math.Max(minImageCount, PREFERRED_IMAGE_COUNT);
        if (maxImageCount != 0 && count > maxImageCount) {
            count = maxImageCount;
        }

        return Math.Max(count, minImageCount);
    }
}
