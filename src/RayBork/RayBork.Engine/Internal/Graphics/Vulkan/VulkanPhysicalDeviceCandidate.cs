using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// The physical-device facts used by <see cref="VulkanPhysicalDeviceSelector"/>.
/// </summary>
/// <param name="Index">The index of the device in the loader's physical-device array.</param>
/// <param name="Name">The device name reported by <c>vkGetPhysicalDeviceProperties</c>.</param>
/// <param name="ApiVersion">The packed Vulkan API version supported by the device.</param>
/// <param name="DeviceType">The device type reported by Vulkan.</param>
/// <param name="SupportsGraphicsQueue">Whether at least one queue family supports graphics.</param>
/// <param name="SupportsPresentQueue">
/// Whether at least one queue family supports presentation to the target surface.
/// </param>
/// <param name="HasSwapchainExtension">Whether the device advertises <c>VK_KHR_swapchain</c>.</param>
/// <param name="MissingBindlessFeatures">
/// The M4 descriptor-indexing feature names the device does not advertise, separated by commas, or an
/// empty string when the bindless subset is supported.
/// </param>
internal readonly record struct VulkanPhysicalDeviceCandidate(
    int Index,
    string Name,
    uint ApiVersion,
    PhysicalDeviceType DeviceType,
    bool SupportsGraphicsQueue,
    bool SupportsPresentQueue,
    bool HasSwapchainExtension,
    string MissingBindlessFeatures = "");
