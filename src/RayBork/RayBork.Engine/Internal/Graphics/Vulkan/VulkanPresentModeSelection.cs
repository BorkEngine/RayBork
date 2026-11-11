using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// The native present mode selected for the swapchain and its public projection.
/// </summary>
/// <remarks>
/// <see cref="Public"/> is the effective mode reported by <see cref="EngineInfo.PresentMode"/>, and
/// <see cref="FellBack"/> records that the requested mode was unavailable so the caller can warn.
/// </remarks>
/// <param name="Native">The selected Vulkan present mode.</param>
/// <param name="Public">The selected public present mode.</param>
/// <param name="FellBack">Whether the requested mode was unavailable and a fallback was used.</param>
internal readonly record struct VulkanPresentModeChoice(PresentModeKHR Native, PresentMode Public, bool FellBack);

/// <summary>
/// Chooses the swapchain present mode from the modes the window surface advertises.
/// </summary>
/// <remarks>
/// The policy is pure so it can be unit tested without a GPU. Mailbox and FIFO fall back to each
/// other because every surface must advertise at least FIFO and a window without both cannot present
/// without tearing; immediate mode never falls back because tearing is only used when it was
/// explicitly requested.
/// </remarks>
internal static class VulkanPresentModeSelection {
    /// <summary>
    /// Selects the effective present mode.
    /// </summary>
    /// <param name="available">The present modes the surface advertises.</param>
    /// <param name="requested">The present mode the app requested.</param>
    /// <returns>The native mode, the public mode, and whether a fallback was used.</returns>
    /// <exception cref="GraphicsException">
    /// The surface advertises neither FIFO nor mailbox for a queued request, or the requested
    /// immediate mode is unavailable. The message names the requested and supported modes and the
    /// diagnostic code is <c>RB3001</c>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="requested"/> is not a defined <see cref="PresentMode"/> value.
    /// </exception>
    internal static VulkanPresentModeChoice Select(IReadOnlyList<PresentModeKHR> available, PresentMode requested) {
        ArgumentNullException.ThrowIfNull(available);
        bool supportsMailbox = Contains(available, PresentModeKHR.MailboxKhr);
        bool supportsFifo = Contains(available, PresentModeKHR.FifoKhr);
        return requested switch {
            PresentMode.Mailbox when supportsMailbox =>
                new VulkanPresentModeChoice(PresentModeKHR.MailboxKhr, PresentMode.Mailbox, false),
            PresentMode.Mailbox when supportsFifo =>
                new VulkanPresentModeChoice(PresentModeKHR.FifoKhr, PresentMode.Fifo, true),
            PresentMode.Mailbox => throw MissingQueuedMode(requested, available),
            PresentMode.Fifo when supportsFifo =>
                new VulkanPresentModeChoice(PresentModeKHR.FifoKhr, PresentMode.Fifo, false),
            PresentMode.Fifo when supportsMailbox =>
                new VulkanPresentModeChoice(PresentModeKHR.MailboxKhr, PresentMode.Mailbox, true),
            PresentMode.Fifo => throw MissingQueuedMode(requested, available),
            PresentMode.Immediate when Contains(available, PresentModeKHR.ImmediateKhr) =>
                new VulkanPresentModeChoice(PresentModeKHR.ImmediateKhr, PresentMode.Immediate, false),
            PresentMode.Immediate => throw new GraphicsException(
                $"The window surface does not support the requested Immediate present mode. Supported modes: {Describe(available)}.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null),
            _ => throw new ArgumentOutOfRangeException(
                nameof(requested),
                requested,
                "The present mode is not a defined value."),
        };
    }

    private static GraphicsException MissingQueuedMode(PresentMode requested, IReadOnlyList<PresentModeKHR> available) => new(
        $"The window surface does not support the requested {requested} present mode and does not advertise the mandatory FIFO present mode. Supported modes: {Describe(available)}.",
        DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
        null);

    private static bool Contains(IReadOnlyList<PresentModeKHR> available, PresentModeKHR mode) {
        for (int index = 0; index < available.Count; index++) {
            if (available[index] == mode) {
                return true;
            }
        }

        return false;
    }

    private static string Describe(IReadOnlyList<PresentModeKHR> available) {
        if (available.Count == 0) {
            return "none";
        }

        List<string> descriptions = new(available.Count);
        foreach (PresentModeKHR mode in available) {
            descriptions.Add(mode.ToString());
        }

        return string.Join(", ", descriptions);
    }
}
