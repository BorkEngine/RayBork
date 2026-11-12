using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// Selects the swapchain image usage flags the renderer requests from the surface capabilities.
/// </summary>
/// <remarks>
/// The present pass needs color-attachment usage. The presented-pixel readback used by the HDR and SDR
/// present probes also needs transfer-source usage; the swapchain must request it at creation time
/// because a swapchain image's usage is fixed when it is created. A surface that does not advertise
/// transfer-source usage is still usable for presentation, but the diagnostic readback is unavailable
/// and the GPU tests skip the probe with that reason.
/// </remarks>
internal static class VulkanSwapchainImageUsage {
    /// <summary>
    /// Selects the requested usage flags and reports whether presented-pixel readback is available.
    /// </summary>
    /// <param name="supported">The surface's supported usage flags.</param>
    /// <returns>The usage flags to request and whether transfer-source readback is available.</returns>
    internal static (ImageUsageFlags Usage, bool SupportsPresentReadback) Select(ImageUsageFlags supported) {
        bool supportsReadback = (supported & ImageUsageFlags.TransferSrcBit) != 0;
        ImageUsageFlags usage = ImageUsageFlags.ColorAttachmentBit;
        if (supportsReadback) {
            usage |= ImageUsageFlags.TransferSrcBit;
        }

        return (usage, supportsReadback);
    }
}
