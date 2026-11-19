using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// Chooses the swapchain image extent from the surface capabilities and the window's render size.
/// </summary>
/// <remarks>
/// A defined current extent fixes the size; otherwise the render size is clamped to the supported
/// range. The result is never smaller than one by one.
/// </remarks>
internal static class VulkanSwapchainExtentSelection
{
    private const uint UNDEFINED_EXTENT = uint.MaxValue;

    /// <summary>Selects the extent, which is always at least one by one pixel.</summary>
    internal static Extent2D Select(
        SurfaceCapabilitiesKHR capabilities,
        int renderWidth,
        int renderHeight
    )
    {
        if (capabilities.CurrentExtent.Width != UNDEFINED_EXTENT)
        {
            return capabilities.CurrentExtent;
        }

        uint minimumWidth = Math.Max(1u, capabilities.MinImageExtent.Width);
        uint minimumHeight = Math.Max(1u, capabilities.MinImageExtent.Height);
        if (renderWidth <= 0 || renderHeight <= 0)
        {
            return new Extent2D { Width = minimumWidth, Height = minimumHeight };
        }

        uint maximumWidth =
            capabilities.MaxImageExtent.Width == 0
                ? uint.MaxValue
                : Math.Max(minimumWidth, capabilities.MaxImageExtent.Width);
        uint maximumHeight =
            capabilities.MaxImageExtent.Height == 0
                ? uint.MaxValue
                : Math.Max(minimumHeight, capabilities.MaxImageExtent.Height);
        return new Extent2D
        {
            Width = Math.Clamp((uint)renderWidth, minimumWidth, maximumWidth),
            Height = Math.Clamp((uint)renderHeight, minimumHeight, maximumHeight),
        };
    }
}
