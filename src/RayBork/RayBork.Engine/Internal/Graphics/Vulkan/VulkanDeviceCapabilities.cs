using System.Globalization;
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
/// The descriptor-indexing flags and update-after-bind limits describe the M4 bindless texture
/// table. <see cref="BindlessTextureCapacity"/> is always
/// <see cref="TARGET_BINDLESS_TEXTURE_CAPACITY"/>: the shader ABI declares
/// <c>RayBorkTextures[2048]</c>, so the descriptor set layout must use exactly that count and the
/// capacity is never reduced. Device selection rejects a device without the feature subset or with
/// update-after-bind sampled-image limits below the capacity.
/// </para>
/// </remarks>
internal sealed class VulkanDeviceCapabilities {
    /// <summary>
    /// The renderer's bindless texture capacity: 2048 combined image samplers, matching the fixed
    /// <c>RayBorkTextures[2048]</c> shader array. A device whose update-after-bind sampled-image
    /// limits are below this value is rejected during device selection.
    /// </summary>
    internal const uint TARGET_BINDLESS_TEXTURE_CAPACITY = ShaderAbiNames.TEXTURE_CAPACITY;

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
        uint maxPerStageDescriptorUpdateAfterBindSampledImages,
        uint maxDescriptorSetUpdateAfterBindSampledImages,
        uint maxPerStageDescriptorUpdateAfterBindSamplers,
        uint maxStorageBufferRange,
        ulong maxMemoryAllocationSize,
        uint bindlessTextureCapacity,
        bool samplerAnisotropy,
        float maxSamplerAnisotropy,
        bool textureCompressionBc,
        bool fillModeNonSolid,
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
        MaxPerStageDescriptorUpdateAfterBindSampledImages = maxPerStageDescriptorUpdateAfterBindSampledImages;
        MaxDescriptorSetUpdateAfterBindSampledImages = maxDescriptorSetUpdateAfterBindSampledImages;
        MaxPerStageDescriptorUpdateAfterBindSamplers = maxPerStageDescriptorUpdateAfterBindSamplers;
        MaxStorageBufferRange = maxStorageBufferRange;
        MaxMemoryAllocationSize = maxMemoryAllocationSize;
        BindlessTextureCapacity = bindlessTextureCapacity;
        SamplerAnisotropy = samplerAnisotropy;
        MaxSamplerAnisotropy = maxSamplerAnisotropy;
        TextureCompressionBc = textureCompressionBc;
        FillModeNonSolid = fillModeNonSolid;
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
    /// Gets whether the device supports and the renderer enables the M4 descriptor-indexing subset:
    /// partially bound arrays, sampled-image update-after-bind, and non-uniform indexing of
    /// sampled-image arrays.
    /// </summary>
    internal bool DescriptorIndexing { get; }

    /// <summary>
    /// Gets the maximum update-after-bind sampled images available to one shader stage.
    /// </summary>
    internal uint MaxPerStageDescriptorUpdateAfterBindSampledImages { get; }

    /// <summary>
    /// Gets the maximum update-after-bind sampled images available to one descriptor set.
    /// </summary>
    internal uint MaxDescriptorSetUpdateAfterBindSampledImages { get; }

    /// <summary>
    /// Gets the maximum update-after-bind samplers available to one shader stage.
    /// </summary>
    internal uint MaxPerStageDescriptorUpdateAfterBindSamplers { get; }

    /// <summary>
    /// Gets the maximum byte range of one storage buffer.
    /// </summary>
    internal uint MaxStorageBufferRange { get; }

    /// <summary>
    /// Gets the maximum size in bytes of one device memory allocation.
    /// </summary>
    internal ulong MaxMemoryAllocationSize { get; }

    /// <summary>
    /// Gets the bindless texture table capacity the backend creates. It is always
    /// <see cref="TARGET_BINDLESS_TEXTURE_CAPACITY"/>, which matches the fixed shader array; device
    /// selection guarantees the device's update-after-bind sampled-image limits cover it.
    /// </summary>
    internal uint BindlessTextureCapacity { get; }

    /// <summary>
    /// Gets whether the device supports anisotropic sampling. The renderer enables the feature when
    /// it is supported.
    /// </summary>
    internal bool SamplerAnisotropy { get; }

    /// <summary>
    /// Gets the maximum sampler anisotropy the device supports. It is meaningful only when
    /// <see cref="SamplerAnisotropy"/> is <see langword="true"/>.
    /// </summary>
    internal float MaxSamplerAnisotropy { get; }

    /// <summary>
    /// Gets whether the device supports the BC block-compressed formats. A packed BC7 or BC5 texture
    /// requires this capability; a device without it rejects the upload with <c>RB1010</c>.
    /// </summary>
    internal bool TextureCompressionBc { get; }

    /// <summary>
    /// Gets whether the device supports the core <c>fillModeNonSolid</c> feature and the renderer
    /// enables it. Wireframe model draws need it for <c>VK_POLYGON_MODE_LINE</c> pipelines; a device
    /// without it reports an unsupported wireframe draw instead of rendering nothing.
    /// </summary>
    internal bool FillModeNonSolid { get; }

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
    /// Runs on the render thread after the logical device is created. The descriptor-indexing subset
    /// is reported as enabled only when the device advertises every required feature; device
    /// selection already guarantees that for a created device.
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
        PhysicalDeviceVulkan12Features vulkan12Features = VulkanBindlessFeatures.Query(vulkan, physicalDevice);
        (PhysicalDeviceVulkan12Properties vulkan12Properties, PhysicalDeviceVulkan11Properties vulkan11Properties) =
            QueryProperties2(vulkan, physicalDevice);

        string deviceName = Marshal.PtrToStringUTF8((IntPtr)properties.DeviceName) ?? string.Empty;
        bool descriptorIndexing = VulkanBindlessFeatures.GetMissingFeatures(vulkan12Features).Count == 0;
        int[] supportedSampleCounts = SelectSupportedSampleCounts(
            properties.Limits.FramebufferColorSampleCounts,
            properties.Limits.FramebufferDepthSampleCounts);
        uint sampledImageCapacity = VulkanBindlessFeatures.GetTextureCapacity(vulkan12Properties);
        if (sampledImageCapacity < TARGET_BINDLESS_TEXTURE_CAPACITY) {
            throw new GraphicsException(
                $"The Vulkan device '{deviceName}' supports "
                + $"{sampledImageCapacity.ToString(CultureInfo.InvariantCulture)} update-after-bind "
                + "sampled image(s), which is below the M4 bindless texture capacity of "
                + $"{TARGET_BINDLESS_TEXTURE_CAPACITY.ToString(CultureInfo.InvariantCulture)}. "
                + "The shader ABI declares RayBorkTextures["
                + $"{TARGET_BINDLESS_TEXTURE_CAPACITY.ToString(CultureInfo.InvariantCulture)}] and the "
                + "descriptor set layout must declare the same count; the renderer does not reduce "
                + "the capacity.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null);
        }

        uint capacity = TARGET_BINDLESS_TEXTURE_CAPACITY;

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
            vulkan12Properties.MaxPerStageDescriptorUpdateAfterBindSampledImages,
            vulkan12Properties.MaxDescriptorSetUpdateAfterBindSampledImages,
            vulkan12Properties.MaxPerStageDescriptorUpdateAfterBindSamplers,
            properties.Limits.MaxStorageBufferRange,
            vulkan11Properties.MaxMemoryAllocationSize,
            capacity,
            features.SamplerAnisotropy.Value != 0,
            properties.Limits.MaxSamplerAnisotropy,
            features.TextureCompressionBC.Value != 0,
            features.FillModeNonSolid.Value != 0,
            swapchainColorSpaceExtensionEnabled,
            swapchain);
    }

    private static unsafe (PhysicalDeviceVulkan12Properties Vulkan12, PhysicalDeviceVulkan11Properties Vulkan11)
        QueryProperties2(Vk vulkan, PhysicalDevice physicalDevice) {
        PhysicalDeviceVulkan12Properties vulkan12Properties = new() {
            SType = StructureType.PhysicalDeviceVulkan12Properties,
        };
        PhysicalDeviceVulkan11Properties vulkan11Properties = new() {
            SType = StructureType.PhysicalDeviceVulkan11Properties,
            PNext = &vulkan12Properties,
        };
        PhysicalDeviceProperties2 properties2 = new() {
            SType = StructureType.PhysicalDeviceProperties2,
            PNext = &vulkan11Properties,
        };
        vulkan.GetPhysicalDeviceProperties2(physicalDevice, &properties2);
        return (vulkan12Properties, vulkan11Properties);
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
