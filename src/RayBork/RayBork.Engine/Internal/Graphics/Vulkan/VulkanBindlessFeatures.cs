using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>The Vulkan 1.2 descriptor-indexing subset the bindless table requires.</summary>
/// <remarks>
/// The device enables partially bound arrays, update-after-bind sampled images, and non-uniform
/// indexing of sampled-image arrays. Probing and enabling share this type; every member is pure.
/// </remarks>
internal static class VulkanBindlessFeatures
{
    /// <summary>The name of <c>descriptorBindingPartiallyBound</c>.</summary>
    internal const string PARTIALLY_BOUND = "descriptorBindingPartiallyBound";

    /// <summary>The name of <c>descriptorBindingSampledImageUpdateAfterBind</c>.</summary>
    internal const string SAMPLED_IMAGE_UPDATE_AFTER_BIND =
        "descriptorBindingSampledImageUpdateAfterBind";

    /// <summary>The name of <c>shaderSampledImageArrayNonUniformIndexing</c>.</summary>
    internal const string SAMPLED_IMAGE_ARRAY_NON_UNIFORM_INDEXING =
        "shaderSampledImageArrayNonUniformIndexing";

    /// <summary>Collects the required feature names the device does not advertise.</summary>
    /// <returns>The missing names, or an empty list when the subset is supported.</returns>
    internal static IReadOnlyList<string> GetMissingFeatures(
        in PhysicalDeviceVulkan12Features features
    )
    {
        List<string> missing = [];
        if (features.DescriptorBindingPartiallyBound.Value == 0)
        {
            missing.Add(PARTIALLY_BOUND);
        }

        if (features.DescriptorBindingSampledImageUpdateAfterBind.Value == 0)
        {
            missing.Add(SAMPLED_IMAGE_UPDATE_AFTER_BIND);
        }

        if (features.ShaderSampledImageArrayNonUniformIndexing.Value == 0)
        {
            missing.Add(SAMPLED_IMAGE_ARRAY_NON_UNIFORM_INDEXING);
        }

        return missing;
    }

    /// <summary>Queries a physical device's Vulkan 1.2 feature support.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="vulkan"/> is <c>null</c>.</exception>
    internal static unsafe PhysicalDeviceVulkan12Features Query(
        Vk vulkan,
        PhysicalDevice physicalDevice
    )
    {
        ArgumentNullException.ThrowIfNull(vulkan);

        PhysicalDeviceVulkan12Features features = new()
        {
            SType = StructureType.PhysicalDeviceVulkan12Features,
        };
        PhysicalDeviceFeatures2 features2 = new()
        {
            SType = StructureType.PhysicalDeviceFeatures2,
            PNext = &features,
        };
        vulkan.GetPhysicalDeviceFeatures2(physicalDevice, &features2);
        return features;
    }

    /// <summary>Queries how many update-after-bind sampled images a device supports.</summary>
    /// <returns>The smaller of the per-stage and per-set limits.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="vulkan"/> is <c>null</c>.</exception>
    internal static unsafe uint QueryTextureCapacity(Vk vulkan, PhysicalDevice physicalDevice)
    {
        ArgumentNullException.ThrowIfNull(vulkan);

        PhysicalDeviceVulkan12Properties properties = new()
        {
            SType = StructureType.PhysicalDeviceVulkan12Properties,
        };
        PhysicalDeviceProperties2 properties2 = new()
        {
            SType = StructureType.PhysicalDeviceProperties2,
            PNext = &properties,
        };
        vulkan.GetPhysicalDeviceProperties2(physicalDevice, &properties2);
        return GetTextureCapacity(properties);
    }

    /// <summary>Returns the smaller of the per-stage and per-set sampled-image limits.</summary>
    internal static uint GetTextureCapacity(in PhysicalDeviceVulkan12Properties properties) =>
        Math.Min(
            properties.MaxPerStageDescriptorUpdateAfterBindSampledImages,
            properties.MaxDescriptorSetUpdateAfterBindSampledImages
        );

    /// <summary>Enables the required subset on a device-creation feature chain.</summary>
    internal static void Enable(ref PhysicalDeviceVulkan12Features features)
    {
        features.DescriptorBindingPartiallyBound = true;
        features.DescriptorBindingSampledImageUpdateAfterBind = true;
        features.ShaderSampledImageArrayNonUniformIndexing = true;
    }
}
