using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// The Vulkan 1.2 descriptor-indexing subset the M4 bindless texture table requires.
/// </summary>
/// <remarks>
/// <para>
/// Every geometry shader samples <c>RayBorkTextures[]</c> with a non-uniform instance index and the
/// renderer writes a slot while frames may be in flight. The logical device therefore enables exactly
/// these four core features and no others: runtime descriptor arrays, partially bound arrays,
/// update-after-bind for sampled images, and non-uniform indexing of sampled-image arrays. Update-after-bind
/// for uniform and storage buffers, variable descriptor counts, and descriptor binding for other
/// descriptor types stay disabled.
/// </para>
/// <para>
/// The support probe and the device-creation enable path share this type so a device cannot be selected
/// without the features the backend later enables. Every member is pure and safe on any thread.
/// </para>
/// </remarks>
internal static class VulkanBindlessFeatures {
    /// <summary>The human-readable name of <c>runtimeDescriptorArray</c>.</summary>
    internal const string RUNTIME_DESCRIPTOR_ARRAY = "runtimeDescriptorArray";

    /// <summary>The human-readable name of <c>descriptorBindingPartiallyBound</c>.</summary>
    internal const string PARTIALLY_BOUND = "descriptorBindingPartiallyBound";

    /// <summary>The human-readable name of <c>descriptorBindingSampledImageUpdateAfterBind</c>.</summary>
    internal const string SAMPLED_IMAGE_UPDATE_AFTER_BIND = "descriptorBindingSampledImageUpdateAfterBind";

    /// <summary>The human-readable name of <c>shaderSampledImageArrayNonUniformIndexing</c>.</summary>
    internal const string SAMPLED_IMAGE_ARRAY_NON_UNIFORM_INDEXING = "shaderSampledImageArrayNonUniformIndexing";

    /// <summary>
    /// Collects the required feature names the physical device does not advertise.
    /// </summary>
    /// <param name="features">The device's Vulkan 1.2 feature support snapshot.</param>
    /// <returns>The missing feature names, or an empty list when the subset is supported.</returns>
    internal static IReadOnlyList<string> GetMissingFeatures(in PhysicalDeviceVulkan12Features features) {
        List<string> missing = [];
        if (features.RuntimeDescriptorArray.Value == 0) {
            missing.Add(RUNTIME_DESCRIPTOR_ARRAY);
        }

        if (features.DescriptorBindingPartiallyBound.Value == 0) {
            missing.Add(PARTIALLY_BOUND);
        }

        if (features.DescriptorBindingSampledImageUpdateAfterBind.Value == 0) {
            missing.Add(SAMPLED_IMAGE_UPDATE_AFTER_BIND);
        }

        if (features.ShaderSampledImageArrayNonUniformIndexing.Value == 0) {
            missing.Add(SAMPLED_IMAGE_ARRAY_NON_UNIFORM_INDEXING);
        }

        return missing;
    }

    /// <summary>
    /// Queries a physical device's Vulkan 1.2 feature support.
    /// </summary>
    /// <param name="vulkan">The loaded Vulkan API.</param>
    /// <param name="physicalDevice">The physical device to probe.</param>
    /// <returns>The device's Vulkan 1.2 feature support snapshot.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="vulkan"/> is <c>null</c>.</exception>
    internal static unsafe PhysicalDeviceVulkan12Features Query(Vk vulkan, PhysicalDevice physicalDevice) {
        ArgumentNullException.ThrowIfNull(vulkan);

        PhysicalDeviceVulkan12Features features = new() {
            SType = StructureType.PhysicalDeviceVulkan12Features,
        };
        PhysicalDeviceFeatures2 features2 = new() {
            SType = StructureType.PhysicalDeviceFeatures2,
            PNext = &features,
        };
        vulkan.GetPhysicalDeviceFeatures2(physicalDevice, &features2);
        return features;
    }

    /// <summary>
    /// Enables the required subset on a device-creation feature chain.
    /// </summary>
    /// <param name="features">The feature structure to fill.</param>
    internal static void Enable(ref PhysicalDeviceVulkan12Features features) {
        features.RuntimeDescriptorArray = true;
        features.DescriptorBindingPartiallyBound = true;
        features.DescriptorBindingSampledImageUpdateAfterBind = true;
        features.ShaderSampledImageArrayNonUniformIndexing = true;
    }
}
