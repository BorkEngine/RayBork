namespace RayBork.Internal;

/// <summary>The queue-family facts used by <see cref="VulkanQueueFamilySelection"/>.</summary>
/// <remarks>Presentation support refers to the target surface.</remarks>
internal readonly record struct VulkanQueueFamilyInfo(
    uint Index,
    bool SupportsGraphics,
    bool SupportsPresent,
    uint QueueCount
);
