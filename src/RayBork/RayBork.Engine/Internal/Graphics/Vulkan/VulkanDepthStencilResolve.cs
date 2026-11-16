using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// Probes and selects the Vulkan 1.2 depth/stencil resolve mode a depth-bearing render target uses.
/// </summary>
/// <remarks>
/// <para>
/// The renderer resolves a multisampled depth attachment into the single-sample depth image a shader
/// samples. Vulkan 1.2 exposes the supported modes through
/// <c>VkPhysicalDeviceDepthStencilResolveProperties</c>; the renderer prefers
/// <c>VK_RESOLVE_MODE_SAMPLE_ZERO_BIT</c> and falls back to <c>MIN</c> and then <c>MAX</c>. A device
/// that advertises no depth resolve mode is rejected instead of creating a depth view that can never
/// be sampled.
/// </para>
/// <para>
/// The engine has no stencil attachment, so only the depth mode is selected. Every member is pure and
/// safe on any thread.
/// </para>
/// </remarks>
internal static class VulkanDepthStencilResolve {
    /// <summary>The property the renderer reads to select the depth resolve mode.</summary>
    internal const string PROPERTY_NAME = "VkPhysicalDeviceDepthStencilResolveProperties.supportedDepthResolveModes";

    private static readonly ResolveModeFlags[] _depthModePreference = [
        ResolveModeFlags.SampleZeroBit,
        ResolveModeFlags.MinBit,
        ResolveModeFlags.MaxBit,
    ];

    /// <summary>
    /// Queries a physical device's depth/stencil resolve support.
    /// </summary>
    /// <param name="vulkan">The loaded Vulkan API.</param>
    /// <param name="physicalDevice">The physical device to probe.</param>
    /// <returns>The device's depth/stencil resolve properties.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="vulkan"/> is <c>null</c>.</exception>
    internal static unsafe PhysicalDeviceDepthStencilResolveProperties Query(
        Vk vulkan,
        PhysicalDevice physicalDevice) {
        ArgumentNullException.ThrowIfNull(vulkan);

        PhysicalDeviceDepthStencilResolveProperties properties = new() {
            SType = StructureType.PhysicalDeviceDepthStencilResolveProperties,
        };
        PhysicalDeviceProperties2 properties2 = new() {
            SType = StructureType.PhysicalDeviceProperties2,
            PNext = &properties,
        };
        vulkan.GetPhysicalDeviceProperties2(physicalDevice, &properties2);
        return properties;
    }

    /// <summary>
    /// Selects the depth resolve mode the renderer uses.
    /// </summary>
    /// <param name="supported">The modes the device advertises for depth.</param>
    /// <returns>
    /// <c>VK_RESOLVE_MODE_SAMPLE_ZERO_BIT</c> when supported, otherwise <c>MIN</c> or <c>MAX</c>, or
    /// <see cref="ResolveModeFlags.None"/> when the device advertises no usable depth mode.
    /// </returns>
    internal static ResolveModeFlags SelectDepthResolveMode(ResolveModeFlags supported) {
        foreach (ResolveModeFlags mode in _depthModePreference) {
            if ((supported & mode) != 0) {
                return mode;
            }
        }

        return ResolveModeFlags.None;
    }

    /// <summary>
    /// Describes a resolve-mode mask for a diagnostic.
    /// </summary>
    /// <param name="mode">The resolve-mode mask.</param>
    /// <returns>The Vulkan mode names, or <c>VK_RESOLVE_MODE_NONE</c>.</returns>
    internal static string Describe(ResolveModeFlags mode) {
        if (mode == ResolveModeFlags.None) {
            return "VK_RESOLVE_MODE_NONE";
        }

        List<string> names = [];
        if ((mode & ResolveModeFlags.SampleZeroBit) != 0) {
            names.Add("VK_RESOLVE_MODE_SAMPLE_ZERO_BIT");
        }

        if ((mode & ResolveModeFlags.AverageBit) != 0) {
            names.Add("VK_RESOLVE_MODE_AVERAGE_BIT");
        }

        if ((mode & ResolveModeFlags.MinBit) != 0) {
            names.Add("VK_RESOLVE_MODE_MIN_BIT");
        }

        if ((mode & ResolveModeFlags.MaxBit) != 0) {
            names.Add("VK_RESOLVE_MODE_MAX_BIT");
        }

        return string.Join(" | ", names);
    }

    /// <summary>
    /// Builds the diagnostic for a device that advertises no usable depth resolve mode.
    /// </summary>
    /// <param name="deviceName">The device name reported by Vulkan.</param>
    /// <param name="supported">The modes the device advertises for depth.</param>
    /// <returns>The diagnostic message.</returns>
    internal static string CreateMissingModeMessage(string deviceName, ResolveModeFlags supported) =>
        $"The Vulkan device '{deviceName}' supports no usable depth/stencil resolve mode: "
        + $"{PROPERTY_NAME} is {Describe(supported)} (0x{(uint)supported:X8}). A multisampled "
        + "depth-bearing render target needs VK_RESOLVE_MODE_SAMPLE_ZERO_BIT, "
        + "VK_RESOLVE_MODE_MIN_BIT, or VK_RESOLVE_MODE_MAX_BIT; the renderer does not create an "
        + "unsampleable depth view.";
}
