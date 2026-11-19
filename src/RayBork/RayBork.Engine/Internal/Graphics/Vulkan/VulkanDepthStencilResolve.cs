using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// Selects and probes the Vulkan 1.2 depth/stencil resolve mode a depth-bearing render target uses.
/// </summary>
/// <remarks>
/// A multisampled depth attachment resolves into the sampled single-sample depth image with
/// <c>VK_RESOLVE_MODE_SAMPLE_ZERO_BIT</c>, the only mode that leaves the stored reverse-Z value unchanged;
/// a device without it is rejected. <see cref="Query"/> reads the physical device and is render-thread
/// only; the selection and diagnostic helpers are pure.
/// </remarks>
internal static class VulkanDepthStencilResolve
{
    /// <summary>The property the renderer reads to select the depth resolve mode.</summary>
    internal const string PROPERTY_NAME =
        "VkPhysicalDeviceDepthStencilResolveProperties.supportedDepthResolveModes";

    /// <summary>The depth resolve mode the renderer requires.</summary>
    internal const string MODE_NAME = "VK_RESOLVE_MODE_SAMPLE_ZERO_BIT";

    /// <summary>Queries a physical device's depth/stencil resolve support.</summary>
    internal static unsafe PhysicalDeviceDepthStencilResolveProperties Query(
        Vk vulkan,
        PhysicalDevice physicalDevice
    )
    {
        ArgumentNullException.ThrowIfNull(vulkan);

        PhysicalDeviceDepthStencilResolveProperties properties = new()
        {
            SType = StructureType.PhysicalDeviceDepthStencilResolveProperties,
        };
        PhysicalDeviceProperties2 properties2 = new()
        {
            SType = StructureType.PhysicalDeviceProperties2,
            PNext = &properties,
        };
        vulkan.GetPhysicalDeviceProperties2(physicalDevice, &properties2);
        return properties;
    }

    /// <summary>
    /// Selects sample-zero depth resolve, or <see cref="ResolveModeFlags.None"/> when it is unsupported.
    /// </summary>
    internal static ResolveModeFlags SelectDepthResolveMode(ResolveModeFlags supported) =>
        (supported & ResolveModeFlags.SampleZeroBit) != 0
            ? ResolveModeFlags.SampleZeroBit
            : ResolveModeFlags.None;

    /// <summary>Describes a resolve-mode mask for a diagnostic.</summary>
    /// <returns>The Vulkan mode names, or <c>VK_RESOLVE_MODE_NONE</c>.</returns>
    internal static string Describe(ResolveModeFlags mode)
    {
        if (mode == ResolveModeFlags.None)
        {
            return "VK_RESOLVE_MODE_NONE";
        }

        List<string> names = [];
        if ((mode & ResolveModeFlags.SampleZeroBit) != 0)
        {
            names.Add("VK_RESOLVE_MODE_SAMPLE_ZERO_BIT");
        }

        if ((mode & ResolveModeFlags.AverageBit) != 0)
        {
            names.Add("VK_RESOLVE_MODE_AVERAGE_BIT");
        }

        if ((mode & ResolveModeFlags.MinBit) != 0)
        {
            names.Add("VK_RESOLVE_MODE_MIN_BIT");
        }

        if ((mode & ResolveModeFlags.MaxBit) != 0)
        {
            names.Add("VK_RESOLVE_MODE_MAX_BIT");
        }

        return string.Join(" | ", names);
    }

    /// <summary>Builds the diagnostic for a device without a usable depth resolve mode.</summary>
    internal static string CreateMissingModeMessage(
        string deviceName,
        ResolveModeFlags supported
    ) =>
        $"The Vulkan device '{deviceName}' supports no usable depth/stencil resolve mode: "
        + $"{PROPERTY_NAME} is {Describe(supported)} (0x{(uint)supported:X8}). A multisampled "
        + $"depth-bearing render target needs {MODE_NAME}; the renderer does not create an "
        + "unsampleable depth view or fall back to a min/max resolve that would change the stored "
        + "reverse-Z value.";
}
