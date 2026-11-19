using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// Selects the swapchain image usage flags the renderer requests from the surface capabilities.
/// </summary>
/// <remarks>
/// Presentation needs color-attachment usage; transfer-source usage is added only when the surface
/// supports it, which enables the presented-pixel readback probes.
/// </remarks>
internal static class VulkanSwapchainImageUsage
{
    /// <summary>Selects the usage flags and reports whether presented-pixel readback is available.</summary>
    internal static (ImageUsageFlags Usage, bool SupportsPresentReadback) Select(
        ImageUsageFlags supported
    )
    {
        bool supportsReadback = (supported & ImageUsageFlags.TransferSrcBit) != 0;
        ImageUsageFlags usage = ImageUsageFlags.ColorAttachmentBit;
        if (supportsReadback)
        {
            usage |= ImageUsageFlags.TransferSrcBit;
        }

        return (usage, supportsReadback);
    }
}
