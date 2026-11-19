using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// An immutable snapshot of what the window surface supports for a swapchain.
/// </summary>
/// <remarks>
/// A surface is usable only when it advertises at least one format and one present mode.
/// </remarks>
internal sealed class VulkanSwapchainSupport
{
    private readonly VulkanSurfaceFormat[] _formats;
    private readonly PresentModeKHR[] _presentModes;

    private VulkanSwapchainSupport(
        SurfaceCapabilitiesKHR capabilities,
        VulkanSurfaceFormat[] formats,
        PresentModeKHR[] presentModes
    )
    {
        Capabilities = capabilities;
        _formats = formats;
        _presentModes = presentModes;
    }

    /// <summary>Gets the surface capabilities, including extent and image count limits.</summary>
    internal SurfaceCapabilitiesKHR Capabilities { get; }

    /// <summary>Gets the surface format and color-space pairs available for the swapchain.</summary>
    internal IReadOnlyList<VulkanSurfaceFormat> Formats => _formats;

    /// <summary>Gets the present modes available for the swapchain.</summary>
    internal IReadOnlyList<PresentModeKHR> PresentModes => _presentModes;

    /// <summary>Gets whether the surface can support a swapchain.</summary>
    internal bool IsUsable => _formats.Length > 0 && _presentModes.Length > 0;

    /// <summary>Gets whether the surface advertises the format with any color space.</summary>
    internal bool HasFormat(Format format) => Array.Exists(_formats, pair => pair.Format == format);

    /// <summary>Gets whether the surface advertises the exact format and color-space pair.</summary>
    internal bool HasFormat(Format format, ColorSpaceKHR colorSpace) =>
        Array.Exists(_formats, pair => pair.Format == format && pair.ColorSpace == colorSpace);

    /// <summary>Gets whether the surface advertises a half-float pair that can carry HDR.</summary>
    internal bool SupportsHdrFloatFormat =>
        HasFormat(Format.R16G16B16A16Sfloat, ColorSpaceKHR.SpaceExtendedSrgbLinearExt)
        || HasFormat(Format.R16G16B16A16Sfloat, ColorSpaceKHR.SpaceSrgbNonlinearKhr);

    /// <summary>Gets whether the surface advertises scRGB linear for a half-float format.</summary>
    internal bool SupportsExtendedSrgbLinear =>
        HasFormat(Format.R16G16B16A16Sfloat, ColorSpaceKHR.SpaceExtendedSrgbLinearExt);

    /// <summary>Queries the swapchain support of a surface on the render thread.</summary>
    internal static VulkanSwapchainSupport Query(
        Vk vulkan,
        PhysicalDevice device,
        SurfaceKHR surface
    )
    {
        ArgumentNullException.ThrowIfNull(vulkan);

        SurfaceCapabilitiesKHR capabilities = VulkanNativeFunctions.GetSurfaceCapabilities(
            vulkan,
            device,
            surface
        );
        SurfaceFormatKHR[] nativeFormats = VulkanNativeFunctions.GetSurfaceFormats(
            vulkan,
            device,
            surface
        );
        PresentModeKHR[] presentModes = VulkanNativeFunctions.GetSurfacePresentModes(
            vulkan,
            device,
            surface
        );
        VulkanSurfaceFormat[] formats = new VulkanSurfaceFormat[nativeFormats.Length];
        for (int index = 0; index < nativeFormats.Length; index++)
        {
            formats[index] = new VulkanSurfaceFormat(
                nativeFormats[index].Format,
                nativeFormats[index].ColorSpace
            );
        }

        return new VulkanSwapchainSupport(capabilities, formats, presentModes);
    }
}
