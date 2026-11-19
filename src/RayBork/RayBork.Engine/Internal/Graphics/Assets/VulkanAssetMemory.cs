using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>Selects and allocates device memory for one uploaded asset resource.</summary>
/// <remarks>
/// Resources ask for device-local memory and staging for host-visible coherent memory; no matching
/// type is an upload failure (<c>RB1010</c>), never a fallback. Render-thread only.
/// </remarks>
internal static unsafe class VulkanAssetMemory
{
    /// <summary>Allocates device memory for one image or buffer.</summary>
    /// <exception cref="AssetLoadException">
    /// No memory type matches or the allocation failed (<c>RB1010</c>).
    /// </exception>
    internal static DeviceMemory Allocate(
        VulkanDevice device,
        PhysicalDeviceMemoryProperties memoryProperties,
        MemoryRequirements requirements,
        MemoryPropertyFlags requiredProperties,
        string canonicalPath,
        string description
    ) =>
        Allocate(
            device,
            memoryProperties,
            requirements.Size,
            requirements.MemoryTypeBits,
            requiredProperties,
            canonicalPath,
            description
        );

    /// <summary>Allocates one block for several related buffers.</summary>
    /// <exception cref="AssetLoadException">
    /// No memory type matches or the allocation failed (<c>RB1010</c>).
    /// </exception>
    internal static DeviceMemory Allocate(
        VulkanDevice device,
        PhysicalDeviceMemoryProperties memoryProperties,
        ulong size,
        uint memoryTypeBits,
        MemoryPropertyFlags requiredProperties,
        string canonicalPath,
        string description
    )
    {
        uint memoryTypeIndex = FindMemoryType(
            memoryTypeBits,
            requiredProperties,
            memoryProperties,
            canonicalPath,
            description
        );
        MemoryAllocateInfo allocateInfo = new()
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = size,
            MemoryTypeIndex = memoryTypeIndex,
        };
        Result result = device.Vulkan.AllocateMemory(
            device.Device,
            &allocateInfo,
            null,
            out DeviceMemory memory
        );
        VulkanAssetErrors.ThrowIfFailed(
            result,
            canonicalPath,
            $"vkAllocateMemory for the {description}"
        );
        return memory;
    }

    /// <summary>Finds a memory type that satisfies the type bits and required properties.</summary>
    /// <exception cref="AssetLoadException">
    /// No memory type satisfies the request (<c>RB1010</c>).
    /// </exception>
    internal static uint FindMemoryType(
        uint memoryTypeBits,
        MemoryPropertyFlags requiredProperties,
        PhysicalDeviceMemoryProperties memoryProperties,
        string canonicalPath,
        string description
    )
    {
        for (uint index = 0; index < memoryProperties.MemoryTypeCount; index++)
        {
            bool typeSupported = (memoryTypeBits & (1u << (int)index)) != 0;
            bool propertiesMatch =
                (memoryProperties.MemoryTypes[(int)index].PropertyFlags & requiredProperties)
                == requiredProperties;
            if (typeSupported && propertiesMatch)
            {
                return index;
            }
        }

        throw VulkanAssetErrors.UploadFailed(
            canonicalPath,
            $"no Vulkan memory type satisfies {requiredProperties} for the {description} (type bits 0x{memoryTypeBits:X8})."
        );
    }
}
