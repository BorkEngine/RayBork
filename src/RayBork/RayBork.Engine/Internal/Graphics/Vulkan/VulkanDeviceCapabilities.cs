using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// An immutable snapshot of the selected device's limits, features, and queue families.
/// </summary>
/// <remarks>
/// <para>
/// The render thread probes this snapshot once when the logical device is created and re-probes it
/// only when the device is recreated. The swapchain-dependent data in <see cref="Swapchain"/> is
/// re-queried when the swapchain is recreated; the rest of the snapshot stays valid for the
/// logical device lifetime.
/// </para>
/// <para>
/// No live Vulkan handle or capability query crosses the renderer seam; this value feeds
/// <see cref="EngineInfo"/> and later renderer tasks.
/// </para>
/// </remarks>
internal sealed class VulkanDeviceCapabilities {
    private static readonly int[] _sampleCounts = [1, 2, 4, 8, 16, 32, 64];

    private readonly int[] _supportedSampleCounts;

    private VulkanDeviceCapabilities(
        string deviceName,
        uint apiVersion,
        uint vendorId,
        uint deviceId,
        PhysicalDeviceType deviceType,
        uint graphicsQueueFamily,
        uint presentQueueFamily,
        uint maxFramebufferWidth,
        uint maxFramebufferHeight,
        int[] supportedSampleCounts,
        bool descriptorIndexing,
        uint maxPerStageDescriptorSampledImages,
        bool samplerAnisotropy,
        bool textureCompressionBc,
        bool swapchainColorSpaceExtensionEnabled,
        VulkanSwapchainSupport swapchain) {
        DeviceName = deviceName;
        ApiVersion = apiVersion;
        VendorId = vendorId;
        DeviceId = deviceId;
        DeviceType = deviceType;
        GraphicsQueueFamily = graphicsQueueFamily;
        PresentQueueFamily = presentQueueFamily;
        MaxFramebufferWidth = maxFramebufferWidth;
        MaxFramebufferHeight = maxFramebufferHeight;
        _supportedSampleCounts = supportedSampleCounts;
        DescriptorIndexing = descriptorIndexing;
        MaxPerStageDescriptorSampledImages = maxPerStageDescriptorSampledImages;
        SamplerAnisotropy = samplerAnisotropy;
        TextureCompressionBc = textureCompressionBc;
        SwapchainColorSpaceExtensionEnabled = swapchainColorSpaceExtensionEnabled;
        Swapchain = swapchain;
    }

    /// <summary>
    /// Gets the device name reported by Vulkan.
    /// </summary>
    internal string DeviceName { get; }

    /// <summary>
    /// Gets the packed Vulkan API version supported by the device.
    /// </summary>
    internal uint ApiVersion { get; }

    /// <summary>
    /// Gets the PCI vendor identifier reported by Vulkan.
    /// </summary>
    internal uint VendorId { get; }

    /// <summary>
    /// Gets the PCI device identifier reported by Vulkan.
    /// </summary>
    internal uint DeviceId { get; }

    /// <summary>
    /// Gets the Vulkan device type.
    /// </summary>
    internal PhysicalDeviceType DeviceType { get; }

    /// <summary>
    /// Gets the queue-family index used for graphics and compute work.
    /// </summary>
    internal uint GraphicsQueueFamily { get; }

    /// <summary>
    /// Gets the queue-family index used for presentation.
    /// </summary>
    internal uint PresentQueueFamily { get; }

    /// <summary>
    /// Gets the maximum framebuffer width in pixels.
    /// </summary>
    internal uint MaxFramebufferWidth { get; }

    /// <summary>
    /// Gets the maximum framebuffer height in pixels.
    /// </summary>
    internal uint MaxFramebufferHeight { get; }

    /// <summary>
    /// Gets the supported MSAA sample counts in ascending order. The list always contains one.
    /// </summary>
    internal IReadOnlyList<int> SupportedSampleCounts => _supportedSampleCounts;

    /// <summary>
    /// Gets whether the device supports the descriptor-indexing subset the M2 bindless texture table
    /// needs. M2 probes this capability but does not require it.
    /// </summary>
    internal bool DescriptorIndexing { get; }

    /// <summary>
    /// Gets the maximum number of sampled images available to one shader stage.
    /// </summary>
    internal uint MaxPerStageDescriptorSampledImages { get; }

    /// <summary>
    /// Gets whether the device supports anisotropic sampling.
    /// </summary>
    internal bool SamplerAnisotropy { get; }

    /// <summary>
    /// Gets whether the device supports the BC block-compressed formats. A packed BC7 or BC5 texture
    /// requires this capability; a device without it rejects the upload with <c>RB1010</c>.
    /// </summary>
    internal bool TextureCompressionBc { get; }

    /// <summary>
    /// Gets whether <c>VK_EXT_swapchain_colorspace</c> was enabled on the instance.
    /// </summary>
    internal bool SwapchainColorSpaceExtensionEnabled { get; }

    /// <summary>
    /// Gets the surface format and color-space pairs advertised by the window surface.
    /// </summary>
    internal IReadOnlyList<VulkanSurfaceFormat> SurfaceFormats => Swapchain.Formats;

    /// <summary>
    /// Gets whether the surface advertises a half-float scRGB pair that can carry HDR values.
    /// </summary>
    internal bool SupportsHdrFloatFormat => Swapchain.SupportsHdrFloatFormat;

    /// <summary>
    /// Gets whether the surface advertises the extended sRGB linear (scRGB) color space for a
    /// half-float format.
    /// </summary>
    internal bool SupportsExtendedSrgbLinear => Swapchain.SupportsExtendedSrgbLinear;

    /// <summary>
    /// Gets the swapchain support snapshot captured for the window surface.
    /// </summary>
    internal VulkanSwapchainSupport Swapchain { get; }

    /// <summary>
    /// Probes the selected physical device and creates the capabilities snapshot.
    /// </summary>
    /// <remarks>
    /// Runs on the render thread after the logical device is created. M2 enables no optional device
    /// features; feature support is probed for later tasks and for reporting.
    /// </remarks>
    /// <param name="vulkan">The loaded Vulkan API.</param>
    /// <param name="physicalDevice">The selected physical device.</param>
    /// <param name="graphicsQueueFamily">The selected graphics queue family.</param>
    /// <param name="presentQueueFamily">The selected presentation queue family.</param>
    /// <param name="swapchainColorSpaceExtensionEnabled">
    /// Whether <c>VK_EXT_swapchain_colorspace</c> was enabled on the instance.
    /// </param>
    /// <param name="swapchain">The swapchain support snapshot for the window surface.</param>
    /// <returns>The capabilities snapshot.</returns>
    internal static unsafe VulkanDeviceCapabilities Query(
        Vk vulkan,
        PhysicalDevice physicalDevice,
        uint graphicsQueueFamily,
        uint presentQueueFamily,
        bool swapchainColorSpaceExtensionEnabled,
        VulkanSwapchainSupport swapchain) {
        ArgumentNullException.ThrowIfNull(vulkan);
        ArgumentNullException.ThrowIfNull(swapchain);

        PhysicalDeviceProperties properties = vulkan.GetPhysicalDeviceProperties(physicalDevice);
        PhysicalDeviceFeatures features = vulkan.GetPhysicalDeviceFeatures(physicalDevice);

        PhysicalDeviceVulkan12Features vulkan12Features = new() {
            SType = StructureType.PhysicalDeviceVulkan12Features,
        };
        PhysicalDeviceFeatures2 features2 = new() {
            SType = StructureType.PhysicalDeviceFeatures2,
            PNext = &vulkan12Features,
        };
        vulkan.GetPhysicalDeviceFeatures2(physicalDevice, &features2);

        string deviceName = Marshal.PtrToStringUTF8((IntPtr)properties.DeviceName) ?? string.Empty;
        bool descriptorIndexing = vulkan12Features.RuntimeDescriptorArray.Value != 0
            && vulkan12Features.DescriptorBindingPartiallyBound.Value != 0
            && vulkan12Features.DescriptorBindingSampledImageUpdateAfterBind.Value != 0;
        int[] supportedSampleCounts = SelectSupportedSampleCounts(
            properties.Limits.FramebufferColorSampleCounts,
            properties.Limits.FramebufferDepthSampleCounts);

        return new VulkanDeviceCapabilities(
            deviceName,
            properties.ApiVersion,
            properties.VendorID,
            properties.DeviceID,
            properties.DeviceType,
            graphicsQueueFamily,
            presentQueueFamily,
            properties.Limits.MaxFramebufferWidth,
            properties.Limits.MaxFramebufferHeight,
            supportedSampleCounts,
            descriptorIndexing,
            properties.Limits.MaxPerStageDescriptorSampledImages,
            features.SamplerAnisotropy.Value != 0,
            features.TextureCompressionBC.Value != 0,
            swapchainColorSpaceExtensionEnabled,
            swapchain);
    }

    private static int[] SelectSupportedSampleCounts(
        SampleCountFlags colorSampleCounts,
        SampleCountFlags depthSampleCounts) {
        SampleCountFlags intersection = colorSampleCounts & depthSampleCounts;
        List<int> supported = [];
        foreach (int sampleCount in _sampleCounts) {
            if ((intersection & ToSampleCountFlag(sampleCount)) != 0) {
                supported.Add(sampleCount);
            }
        }

        if (supported.Count == 0 || supported[0] != 1) {
            supported.Insert(0, 1);
        }

        return [.. supported];
    }

    private static SampleCountFlags ToSampleCountFlag(int sampleCount) => sampleCount switch {
        1 => SampleCountFlags.Count1Bit,
        2 => SampleCountFlags.Count2Bit,
        4 => SampleCountFlags.Count4Bit,
        8 => SampleCountFlags.Count8Bit,
        16 => SampleCountFlags.Count16Bit,
        32 => SampleCountFlags.Count32Bit,
        64 => SampleCountFlags.Count64Bit,
        _ => SampleCountFlags.None,
    };
}
