namespace RayBork.Internal;

/// <summary>
/// The queue-family facts used by <see cref="VulkanQueueFamilySelection"/>.
/// </summary>
/// <param name="Index">The queue-family index reported by Vulkan.</param>
/// <param name="SupportsGraphics">Whether the family reports <c>VK_QUEUE_GRAPHICS_BIT</c>.</param>
/// <param name="SupportsPresent">Whether the family supports presentation to the target surface.</param>
/// <param name="QueueCount">The number of queues in the family.</param>
internal readonly record struct VulkanQueueFamilyInfo(
    uint Index,
    bool SupportsGraphics,
    bool SupportsPresent,
    uint QueueCount);
