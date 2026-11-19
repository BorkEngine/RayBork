namespace RayBork.Internal;

/// <summary>Chooses the graphics and presentation queue families for the device.</summary>
/// <remarks>
/// A family supporting both operations provides both queues; otherwise the lowest graphics
/// family and the lowest presentation family are used.
/// </remarks>
internal static class VulkanQueueFamilySelection
{
    /// <summary>Selects the graphics and presentation queue-family indices.</summary>
    /// <exception cref="GraphicsException">
    /// No family supports graphics or presentation; the message names the missing capability and
    /// the code is <c>RB3001</c>.
    /// </exception>
    internal static (uint Graphics, uint Present) Select(
        IReadOnlyList<VulkanQueueFamilyInfo> families
    )
    {
        ArgumentNullException.ThrowIfNull(families);

        VulkanQueueFamilyInfo? combined = null;
        VulkanQueueFamilyInfo? graphics = null;
        VulkanQueueFamilyInfo? present = null;
        foreach (VulkanQueueFamilyInfo family in families)
        {
            if (family.SupportsGraphics && family.SupportsPresent)
            {
                if (combined is null || family.Index < combined.Value.Index)
                {
                    combined = family;
                }
            }

            if (
                family.SupportsGraphics && (graphics is null || family.Index < graphics.Value.Index)
            )
            {
                graphics = family;
            }

            if (family.SupportsPresent && (present is null || family.Index < present.Value.Index))
            {
                present = family;
            }
        }

        if (combined is not null)
        {
            return (combined.Value.Index, combined.Value.Index);
        }

        if (graphics is null || present is null)
        {
            string missing = (graphics is null, present is null) switch
            {
                (true, true) => "graphics and presentation queue families",
                (true, false) => "graphics queue family",
                _ => "presentation queue family",
            };
            throw new GraphicsException(
                $"The Vulkan device has no {missing} for the window surface.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null
            );
        }

        return (graphics.Value.Index, present.Value.Index);
    }
}
