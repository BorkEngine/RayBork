namespace RayBork.Internal;

/// <summary>
/// Chooses the number of swapchain images.
/// </summary>
/// <remarks>
/// Three images bound the renderer's run-ahead while keeping the swapchain fed; the surface's minimum
/// and maximum image counts always win.
/// </remarks>
internal static class VulkanSwapchainImageCount
{
    private const uint PREFERRED_IMAGE_COUNT = 3;

    /// <summary>Selects the image count, never below the surface minimum.</summary>
    internal static uint Select(uint minImageCount, uint maxImageCount)
    {
        uint count = Math.Max(minImageCount, PREFERRED_IMAGE_COUNT);
        if (maxImageCount != 0 && count > maxImageCount)
        {
            count = maxImageCount;
        }

        return Math.Max(count, minImageCount);
    }
}
