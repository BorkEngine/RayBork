using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// Selects and allocates the Vulkan device memory behind one uploaded asset resource.
/// </summary>
/// <remarks>
/// The worker asks for device-local memory for images and mesh buffers and host-visible coherent
/// memory for staging. The memory type must satisfy the resource's <c>memoryTypeBits</c>; a device that
/// offers no matching type is an upload failure (<c>RB1010</c>), never a fallback to a different
/// resource. Uploads run on the render thread, so no member is thread-safe.
/// </remarks>
internal static unsafe class VulkanAssetMemory {
    /// <summary>
    /// Allocates device memory for one image or buffer.
    /// </summary>
    /// <param name="device">The device that owns the allocation.</param>
    /// <param name="memoryProperties">The physical device's memory heap snapshot.</param>
    /// <param name="requirements">The resource's memory requirements.</param>
    /// <param name="requiredProperties">The memory properties the resource needs.</param>
    /// <param name="canonicalPath">The canonical asset path, for diagnostics.</param>
    /// <param name="description">The resource description, for diagnostics.</param>
    /// <returns>The allocated memory.</returns>
    /// <exception cref="AssetLoadException">
    /// No memory type matches or the allocation failed. The diagnostic code is <c>RB1010</c>.
    /// </exception>
    internal static DeviceMemory Allocate(
        VulkanDevice device,
        PhysicalDeviceMemoryProperties memoryProperties,
        MemoryRequirements requirements,
        MemoryPropertyFlags requiredProperties,
        string canonicalPath,
        string description) => Allocate(
            device,
            memoryProperties,
            requirements.Size,
            requirements.MemoryTypeBits,
            requiredProperties,
            canonicalPath,
            description);

    /// <summary>
    /// Allocates device memory for a combined resource such as a mesh's vertex and index buffers.
    /// </summary>
    /// <param name="device">The device that owns the allocation.</param>
    /// <param name="memoryProperties">The physical device's memory heap snapshot.</param>
    /// <param name="size">The allocation size in bytes.</param>
    /// <param name="memoryTypeBits">The memory types the resource accepts.</param>
    /// <param name="requiredProperties">The memory properties the resource needs.</param>
    /// <param name="canonicalPath">The canonical asset path, for diagnostics.</param>
    /// <param name="description">The resource description, for diagnostics.</param>
    /// <returns>The allocated memory.</returns>
    /// <exception cref="AssetLoadException">
    /// No memory type matches or the allocation failed. The diagnostic code is <c>RB1010</c>.
    /// </exception>
    internal static DeviceMemory Allocate(
        VulkanDevice device,
        PhysicalDeviceMemoryProperties memoryProperties,
        ulong size,
        uint memoryTypeBits,
        MemoryPropertyFlags requiredProperties,
        string canonicalPath,
        string description) {
        uint memoryTypeIndex = FindMemoryType(
            memoryTypeBits,
            requiredProperties,
            memoryProperties,
            canonicalPath,
            description);
        MemoryAllocateInfo allocateInfo = new() {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = size,
            MemoryTypeIndex = memoryTypeIndex,
        };
        Result result = device.Vulkan.AllocateMemory(device.Device, &allocateInfo, null, out DeviceMemory memory);
        VulkanAssetErrors.ThrowIfFailed(result, canonicalPath, $"vkAllocateMemory for the {description}");
        return memory;
    }

    /// <summary>
    /// Finds a memory type that satisfies a resource's type bits and required properties.
    /// </summary>
    /// <param name="memoryTypeBits">The memory types the resource accepts.</param>
    /// <param name="requiredProperties">The memory properties the resource needs.</param>
    /// <param name="memoryProperties">The physical device's memory heap snapshot.</param>
    /// <param name="canonicalPath">The canonical asset path, for diagnostics.</param>
    /// <param name="description">The resource description, for diagnostics.</param>
    /// <returns>The memory type index.</returns>
    /// <exception cref="AssetLoadException">
    /// No memory type satisfies the request. The diagnostic code is <c>RB1010</c>.
    /// </exception>
    internal static uint FindMemoryType(
        uint memoryTypeBits,
        MemoryPropertyFlags requiredProperties,
        PhysicalDeviceMemoryProperties memoryProperties,
        string canonicalPath,
        string description) {
        for (uint index = 0; index < memoryProperties.MemoryTypeCount; index++) {
            bool typeSupported = (memoryTypeBits & (1u << (int)index)) != 0;
            bool propertiesMatch = (memoryProperties.MemoryTypes[(int)index].PropertyFlags & requiredProperties) == requiredProperties;
            if (typeSupported && propertiesMatch) {
                return index;
            }
        }

        throw VulkanAssetErrors.UploadFailed(
            canonicalPath,
            $"no Vulkan memory type satisfies {requiredProperties} for the {description} (type bits 0x{memoryTypeBits:X8}).");
    }
}
