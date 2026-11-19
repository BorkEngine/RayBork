using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>The physical-device facts for <see cref="VulkanPhysicalDeviceSelector"/>.</summary>
/// <remarks>
/// Capacity is the smaller of the per-stage and per-set update-after-bind limits. Missing
/// features are comma-separated; depth resolve must include
/// <c>VK_RESOLVE_MODE_SAMPLE_ZERO_BIT</c>.
/// </remarks>
internal readonly record struct VulkanPhysicalDeviceCandidate(
    int Index,
    string Name,
    uint ApiVersion,
    PhysicalDeviceType DeviceType,
    bool SupportsGraphicsQueue,
    bool SupportsPresentQueue,
    bool HasSwapchainExtension,
    uint BindlessTextureCapacity = VulkanDeviceCapabilities.TARGET_BINDLESS_TEXTURE_CAPACITY,
    string MissingBindlessFeatures = "",
    ResolveModeFlags SupportedDepthResolveModes = ResolveModeFlags.None
);
