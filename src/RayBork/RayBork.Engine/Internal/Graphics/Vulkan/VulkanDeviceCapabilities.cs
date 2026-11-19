using System.Globalization;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// An immutable snapshot of the selected device's limits, features, and queue families.
/// </summary>
/// <remarks>
/// Probed once with the logical device and re-probed only when the device is recreated; the
/// swapchain-dependent data is re-queried when the swapchain is recreated. The bindless capacity
/// is fixed at 2048 entries and the depth resolve mode is always sample-zero; device selection
/// rejects a device that cannot provide either.
/// </remarks>
internal sealed class VulkanDeviceCapabilities
{
    /// <summary>
    /// The bindless texture capacity: 2048 combined image samplers, matching the shader ABI.
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
        ResolveModeFlags depthResolveMode,
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
        VulkanSwapchainSupport swapchain
    )
    {
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
        DepthResolveMode = depthResolveMode;
        DescriptorIndexing = descriptorIndexing;
        MaxPerStageDescriptorUpdateAfterBindSampledImages =
            maxPerStageDescriptorUpdateAfterBindSampledImages;
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

    /// <summary>Gets the device name reported by Vulkan.</summary>
    internal string DeviceName { get; }

    /// <summary>Gets the packed Vulkan API version the device supports.</summary>
    internal uint ApiVersion { get; }

    /// <summary>Gets the PCI vendor identifier reported by Vulkan.</summary>
    internal uint VendorId { get; }

    /// <summary>Gets the PCI device identifier reported by Vulkan.</summary>
    internal uint DeviceId { get; }

    /// <summary>Gets the Vulkan device type.</summary>
    internal PhysicalDeviceType DeviceType { get; }

    /// <summary>Gets the queue-family index used for graphics and compute work.</summary>
    internal uint GraphicsQueueFamily { get; }

    /// <summary>Gets the queue-family index used for presentation.</summary>
    internal uint PresentQueueFamily { get; }

    /// <summary>Gets the maximum framebuffer width in pixels.</summary>
    internal uint MaxFramebufferWidth { get; }

    /// <summary>Gets the maximum framebuffer height in pixels.</summary>
    internal uint MaxFramebufferHeight { get; }

    /// <summary>
    /// Gets the supported MSAA sample counts in ascending order; the list always contains one.
    /// </summary>
    internal IReadOnlyList<int> SupportedSampleCounts => _supportedSampleCounts;

    /// <summary>
    /// Gets the depth resolve mode, always sample-zero; a device without it is rejected.
    /// </summary>
    internal ResolveModeFlags DepthResolveMode { get; }

    /// <summary>Gets whether the bindless descriptor-indexing feature subset is enabled.</summary>
    internal bool DescriptorIndexing { get; }

    /// <summary>Gets the maximum update-after-bind sampled images per shader stage.</summary>
    internal uint MaxPerStageDescriptorUpdateAfterBindSampledImages { get; }

    /// <summary>Gets the maximum update-after-bind sampled images per descriptor set.</summary>
    internal uint MaxDescriptorSetUpdateAfterBindSampledImages { get; }

    /// <summary>Gets the maximum update-after-bind samplers per shader stage.</summary>
    internal uint MaxPerStageDescriptorUpdateAfterBindSamplers { get; }

    /// <summary>Gets the maximum byte range of one storage buffer.</summary>
    internal uint MaxStorageBufferRange { get; }

    /// <summary>Gets the maximum size in bytes of one device memory allocation.</summary>
    internal ulong MaxMemoryAllocationSize { get; }

    /// <summary>
    /// Gets the bindless capacity, always the fixed shader-array capacity of 2048 entries.
    /// </summary>
    internal uint BindlessTextureCapacity { get; }

    /// <summary>Gets whether anisotropic sampling is supported and enabled.</summary>
    internal bool SamplerAnisotropy { get; }

    /// <summary>Gets the maximum sampler anisotropy, meaningful only when supported.</summary>
    internal float MaxSamplerAnisotropy { get; }

    /// <summary>Gets whether BC block-compressed texture formats are supported.</summary>
    internal bool TextureCompressionBc { get; }

    /// <summary>
    /// Gets whether non-solid fill is supported and enabled, as wireframe draws require.
    /// </summary>
    internal bool FillModeNonSolid { get; }

    /// <summary>Gets whether the swapchain color-space extension was enabled.</summary>
    internal bool SwapchainColorSpaceExtensionEnabled { get; }

    /// <summary>Gets the format and color-space pairs the window surface advertises.</summary>
    internal IReadOnlyList<VulkanSurfaceFormat> SurfaceFormats => Swapchain.Formats;

    /// <summary>Gets whether the surface advertises a half-float scRGB HDR pair.</summary>
    internal bool SupportsHdrFloatFormat => Swapchain.SupportsHdrFloatFormat;

    /// <summary>Gets whether the surface advertises extended sRGB linear color space.</summary>
    internal bool SupportsExtendedSrgbLinear => Swapchain.SupportsExtendedSrgbLinear;

    /// <summary>Gets the swapchain support snapshot captured for the window surface.</summary>
    internal VulkanSwapchainSupport Swapchain { get; }

    /// <summary>
    /// Probes the selected physical device and creates the capabilities snapshot.
    /// </summary>
    /// <remarks>
    /// Requires the sampled-image capacity to cover 2048 entries and sample-zero depth resolve.
    /// </remarks>
    internal static unsafe VulkanDeviceCapabilities Query(
        Vk vulkan,
        PhysicalDevice physicalDevice,
        uint graphicsQueueFamily,
        uint presentQueueFamily,
        bool swapchainColorSpaceExtensionEnabled,
        VulkanSwapchainSupport swapchain
    )
    {
        ArgumentNullException.ThrowIfNull(vulkan);
        ArgumentNullException.ThrowIfNull(swapchain);

        PhysicalDeviceProperties properties = vulkan.GetPhysicalDeviceProperties(physicalDevice);
        PhysicalDeviceFeatures features = vulkan.GetPhysicalDeviceFeatures(physicalDevice);
        PhysicalDeviceVulkan12Features vulkan12Features = VulkanBindlessFeatures.Query(
            vulkan,
            physicalDevice
        );
        (
            PhysicalDeviceVulkan12Properties vulkan12Properties,
            PhysicalDeviceVulkan11Properties vulkan11Properties
        ) = QueryProperties2(vulkan, physicalDevice);

        string deviceName = Marshal.PtrToStringUTF8((IntPtr)properties.DeviceName) ?? string.Empty;
        bool descriptorIndexing =
            VulkanBindlessFeatures.GetMissingFeatures(vulkan12Features).Count == 0;
        int[] supportedSampleCounts = SelectSupportedSampleCounts(
            properties.Limits.FramebufferColorSampleCounts,
            properties.Limits.FramebufferDepthSampleCounts
        );
        PhysicalDeviceDepthStencilResolveProperties resolveProperties =
            VulkanDepthStencilResolve.Query(vulkan, physicalDevice);
        ResolveModeFlags depthResolveMode = VulkanDepthStencilResolve.SelectDepthResolveMode(
            resolveProperties.SupportedDepthResolveModes
        );
        if (depthResolveMode == ResolveModeFlags.None)
        {
            throw new GraphicsException(
                VulkanDepthStencilResolve.CreateMissingModeMessage(
                    deviceName,
                    resolveProperties.SupportedDepthResolveModes
                ),
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null
            );
        }

        uint sampledImageCapacity = VulkanBindlessFeatures.GetTextureCapacity(vulkan12Properties);
        if (sampledImageCapacity < TARGET_BINDLESS_TEXTURE_CAPACITY)
        {
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
                null
            );
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
            depthResolveMode,
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
            swapchain
        );
    }

    private static unsafe (
        PhysicalDeviceVulkan12Properties Vulkan12,
        PhysicalDeviceVulkan11Properties Vulkan11
    ) QueryProperties2(Vk vulkan, PhysicalDevice physicalDevice)
    {
        PhysicalDeviceVulkan12Properties vulkan12Properties = new()
        {
            SType = StructureType.PhysicalDeviceVulkan12Properties,
        };
        PhysicalDeviceVulkan11Properties vulkan11Properties = new()
        {
            SType = StructureType.PhysicalDeviceVulkan11Properties,
            PNext = &vulkan12Properties,
        };
        PhysicalDeviceProperties2 properties2 = new()
        {
            SType = StructureType.PhysicalDeviceProperties2,
            PNext = &vulkan11Properties,
        };
        vulkan.GetPhysicalDeviceProperties2(physicalDevice, &properties2);
        return (vulkan12Properties, vulkan11Properties);
    }

    private static int[] SelectSupportedSampleCounts(
        SampleCountFlags colorSampleCounts,
        SampleCountFlags depthSampleCounts
    )
    {
        SampleCountFlags intersection = colorSampleCounts & depthSampleCounts;
        List<int> supported = [];
        foreach (int sampleCount in _sampleCounts)
        {
            if ((intersection & ToSampleCountFlag(sampleCount)) != 0)
            {
                supported.Add(sampleCount);
            }
        }

        if (supported.Count == 0 || supported[0] != 1)
        {
            supported.Insert(0, 1);
        }

        return [.. supported];
    }

    private static SampleCountFlags ToSampleCountFlag(int sampleCount) =>
        sampleCount switch
        {
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
