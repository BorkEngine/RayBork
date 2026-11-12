using System.Text;
using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// Chooses the physical device the renderer uses.
/// </summary>
/// <remarks>
/// Selection is pure so it can be unit tested without a GPU. A device qualifies only when it
/// supports Vulkan 1.2 or newer, graphics and presentation queues, <c>VK_KHR_swapchain</c>, and the
/// M4 descriptor-indexing subset the bindless texture table needs. A device without the bindless
/// subset is not suitable; the renderer never falls back to one texture per draw.
/// </remarks>
internal static class VulkanPhysicalDeviceSelector {
    private const uint REQUIRED_API_VERSION = (1u << 22) | (2u << 12);

    /// <summary>
    /// Determines whether a device satisfies every M4 requirement.
    /// </summary>
    /// <param name="candidate">The device facts.</param>
    /// <returns><see langword="true"/> when the device is suitable.</returns>
    internal static bool IsSuitable(in VulkanPhysicalDeviceCandidate candidate) =>
        candidate.ApiVersion >= REQUIRED_API_VERSION
        && candidate.SupportsGraphicsQueue
        && candidate.SupportsPresentQueue
        && candidate.HasSwapchainExtension
        && string.IsNullOrEmpty(candidate.MissingBindlessFeatures);

    /// <summary>
    /// Selects the preferred suitable device and returns its index in the loader's device array.
    /// </summary>
    /// <remarks>
    /// A discrete GPU is preferred over an integrated GPU, then a virtual GPU, then a CPU device,
    /// then any other device. Equal device types are ordered by the higher API version and then by
    /// the lower index so the result is deterministic.
    /// </remarks>
    /// <param name="candidates">The facts for every physical device, in loader order.</param>
    /// <returns>The index of the selected device in the loader's physical-device array.</returns>
    /// <exception cref="GraphicsException">
    /// No candidate is suitable. The message contains one line per rejected candidate naming the
    /// rejection reasons; the diagnostic code is <c>RB3001</c>.
    /// </exception>
    internal static int Select(IReadOnlyList<VulkanPhysicalDeviceCandidate> candidates) {
        ArgumentNullException.ThrowIfNull(candidates);

        int bestIndex = -1;
        int bestRank = int.MaxValue;
        uint bestApiVersion = 0;
        StringBuilder rejected = new();
        foreach (VulkanPhysicalDeviceCandidate candidate in candidates) {
            if (!IsSuitable(candidate)) {
                rejected.AppendLine(DescribeRejection(candidate));
                continue;
            }

            int rank = GetRank(candidate.DeviceType);
            bool isBetter = bestIndex < 0
                || rank < bestRank
                || (rank == bestRank && candidate.ApiVersion > bestApiVersion)
                || (rank == bestRank && candidate.ApiVersion == bestApiVersion && candidate.Index < bestIndex);
            if (!isBetter) {
                continue;
            }

            bestIndex = candidate.Index;
            bestRank = rank;
            bestApiVersion = candidate.ApiVersion;
        }

        if (bestIndex >= 0) {
            return bestIndex;
        }

        string detail = rejected.Length > 0
            ? " No candidate qualifies:" + Environment.NewLine + rejected
            : " The Vulkan loader reported no physical-device facts.";
        throw new GraphicsException(
            "No suitable Vulkan 1.2 physical device was found." + detail,
            DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
            null);
    }

    private static string DescribeRejection(in VulkanPhysicalDeviceCandidate candidate) {
        List<string> reasons = [];
        if (candidate.ApiVersion < REQUIRED_API_VERSION) {
            reasons.Add($"API {VulkanVersion.Format(candidate.ApiVersion)} is older than Vulkan 1.2");
        }

        if (!candidate.SupportsGraphicsQueue) {
            reasons.Add("no graphics queue family");
        }

        if (!candidate.SupportsPresentQueue) {
            reasons.Add("no presentation queue family");
        }

        if (!candidate.HasSwapchainExtension) {
            reasons.Add("the VK_KHR_swapchain extension is missing");
        }

        if (!string.IsNullOrEmpty(candidate.MissingBindlessFeatures)) {
            reasons.Add($"descriptor-indexing features missing: {candidate.MissingBindlessFeatures}");
        }

        return $"- [{candidate.Index}] {candidate.Name}: {string.Join("; ", reasons)}.";
    }

    private static int GetRank(PhysicalDeviceType deviceType) => deviceType switch {
        PhysicalDeviceType.DiscreteGpu => 0,
        PhysicalDeviceType.IntegratedGpu => 1,
        PhysicalDeviceType.VirtualGpu => 2,
        PhysicalDeviceType.Cpu => 3,
        _ => 4,
    };
}
