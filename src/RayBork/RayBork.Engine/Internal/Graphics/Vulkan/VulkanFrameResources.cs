using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace RayBork.Internal;

/// <summary>
/// The per-frame-in-flight geometry resources: a frame storage buffer, an instance storage buffer, and
/// one descriptor set per frame slot.
/// </summary>
/// <remarks>
/// A slot's buffers and descriptors are rewritten only after its fence is waited, so growth never disturbs
/// an in-flight frame; both buffers stay mapped for their lifetime. Each buffer is bounded by the smaller
/// of the device storage-buffer range and 64 MiB (instances) or 16 MiB (frame blocks).
/// </remarks>
internal sealed unsafe class VulkanFrameResources : IDisposable
{
    /// <summary>The number of frames the renderer may have in flight.</summary>
    internal const int FRAMES_IN_FLIGHT = 3;

    /// <summary>The byte size of one frame block.</summary>
    internal const int FRAME_BLOCK_SIZE = ShaderAbiFrame.BLOCK_SIZE;

    /// <summary>The upper bound the renderer places on the instance storage buffer (64 MiB).</summary>
    internal const ulong MAX_INSTANCE_BYTES = 64UL * 1024 * 1024;

    /// <summary>The upper bound the renderer places on the frame storage buffer (16 MiB).</summary>
    internal const ulong MAX_FRAME_BYTES = 16UL * 1024 * 1024;

    private const ulong INITIAL_INSTANCE_BYTES = 64UL * 1024;
    private const ulong INSTANCE_GROWTH_FACTOR = 2;
    private const ulong INITIAL_FRAME_BYTES = FRAME_BLOCK_SIZE;
    private const ulong FRAME_GROWTH_FACTOR = 2;

    private readonly Vk _vulkan;
    private readonly Device _device;
    private readonly PhysicalDevice _physicalDevice;
    private readonly VulkanDescriptorState _descriptors;
    private readonly FrameSlot[] _slots;
    private readonly ulong _maxInstanceBytes;
    private readonly ulong _maxFrameBytes;
    private readonly DescriptorSet[] _descriptorSets;
    private bool _disposed;

    private VulkanFrameResources(
        Vk vulkan,
        Device device,
        PhysicalDevice physicalDevice,
        VulkanDescriptorState descriptors,
        ulong maxInstanceBytes,
        ulong maxFrameBytes
    )
    {
        _vulkan = vulkan;
        _device = device;
        _physicalDevice = physicalDevice;
        _descriptors = descriptors;
        _maxInstanceBytes = maxInstanceBytes;
        _maxFrameBytes = maxFrameBytes;
        _slots = new FrameSlot[FRAMES_IN_FLIGHT];
        _descriptorSets = new DescriptorSet[FRAMES_IN_FLIGHT];
    }

    /// <summary>Gets the frame descriptor sets in slot order.</summary>
    internal DescriptorSet[] DescriptorSets => _descriptorSets;

    /// <summary>Gets the instance storage-buffer bound in bytes.</summary>
    internal ulong MaxInstanceBytes => _maxInstanceBytes;

    /// <summary>Creates the per-slot buffers, memory, and descriptor sets.</summary>
    internal static VulkanFrameResources Create(
        VulkanDevice device,
        VulkanDescriptorState descriptors
    )
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(descriptors);

        ulong maxInstanceBytes = Math.Min(
            (ulong)device.Capabilities.MaxStorageBufferRange,
            MAX_INSTANCE_BYTES
        );
        ulong maxFrameBytes = Math.Min(
            (ulong)device.Capabilities.MaxStorageBufferRange,
            MAX_FRAME_BYTES
        );
        VulkanFrameResources resources = new(
            device.Vulkan,
            device.Device,
            device.PhysicalDevice,
            descriptors,
            maxInstanceBytes,
            maxFrameBytes
        );
        try
        {
            resources.CreateSlots();
            return resources;
        }
        catch
        {
            resources.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Ensures one slot's frame storage buffer holds a 256-byte block for every camera pass, growing it and
    /// rewriting the slot's descriptor when needed.
    /// </summary>
    /// <remarks>
    /// The caller has already waited for the slot's fence, and a pass index is a push constant because the
    /// set layout also carries the update-after-bind texture array.
    /// </remarks>
    internal void EnsureFrameCapacity(int slotIndex, int passCount, long frameIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(passCount);
        FrameSlot slot = _slots[slotIndex];
        ulong requiredFrameBytes = checked((ulong)passCount * FRAME_BLOCK_SIZE);
        if (requiredFrameBytes > _maxFrameBytes)
        {
            throw new GraphicsException(
                $"Frame {frameIndex} records {passCount} camera pass(es), which needs "
                    + $"{requiredFrameBytes} frame-block bytes and exceeds the "
                    + $"{_maxFrameBytes}-byte frame storage-buffer bound (the device's "
                    + "maxStorageBufferRange capped at 16 MiB). Reduce the frame's camera pass count.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null
            );
        }

        ulong requiredCapacity = Math.Max(
            requiredFrameBytes,
            Math.Min(INITIAL_FRAME_BYTES, _maxFrameBytes)
        );
        if (requiredCapacity > slot.FrameBufferBytes)
        {
            ulong capacity = Math.Max(
                requiredCapacity,
                slot.FrameBufferBytes * FRAME_GROWTH_FACTOR
            );
            capacity = Math.Min(capacity, _maxFrameBytes);
            RecreateFrameBuffer(slot, capacity);
            _descriptors.WriteFrameStorageBuffer(slot.DescriptorSet, slot.FrameBuffer, capacity);
        }
    }

    /// <summary>
    /// Ensures one slot holds the frame's instance records, including any per-batch alignment, and rewrites
    /// its instance descriptor when the buffer grows.
    /// </summary>
    internal void EnsureSlot(int slotIndex, ulong instanceBytes, long frameIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        FrameSlot slot = _slots[slotIndex];
        if (instanceBytes > _maxInstanceBytes)
        {
            throw new GraphicsException(
                $"Frame {frameIndex} needs {instanceBytes} instance-record bytes, which exceeds the "
                    + $"{_maxInstanceBytes}-byte instance storage-buffer bound (the device's "
                    + "maxStorageBufferRange capped at 64 MiB). Reduce the frame's instance count or "
                    + "texture and uniform payload.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null
            );
        }

        ulong requiredInstanceBytes = Math.Max(
            instanceBytes,
            Math.Min(INITIAL_INSTANCE_BYTES, _maxInstanceBytes)
        );
        if (requiredInstanceBytes > slot.InstanceBufferBytes)
        {
            ulong capacity = Math.Max(
                requiredInstanceBytes,
                slot.InstanceBufferBytes * INSTANCE_GROWTH_FACTOR
            );
            capacity = Math.Min(capacity, _maxInstanceBytes);
            RecreateInstanceBuffer(slot, capacity);
            _descriptors.WriteInstanceStorageBuffer(
                slot.DescriptorSet,
                slot.InstanceBuffer,
                capacity
            );
        }
    }

    /// <summary>Gets one camera pass's 256-byte frame block in the slot's mapped buffer.</summary>
    internal Span<byte> GetFrameBlockSpan(int slotIndex, int passIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(passIndex);
        FrameSlot slot = _slots[slotIndex];
        long byteOffset = (long)passIndex * FRAME_BLOCK_SIZE;
        if (byteOffset + FRAME_BLOCK_SIZE > (long)slot.FrameBufferBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(passIndex),
                passIndex,
                $"The frame block ends outside the {slot.FrameBufferBytes}-byte frame buffer."
            );
        }

        return new Span<byte>(slot.FrameMapped + byteOffset, FRAME_BLOCK_SIZE);
    }

    /// <summary>Gets a writable range of the slot's mapped instance buffer.</summary>
    internal Span<byte> GetInstanceSpan(int slotIndex, ulong byteOffset, ulong byteLength)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        FrameSlot slot = _slots[slotIndex];
        if (byteOffset + byteLength > slot.InstanceBufferBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(byteOffset),
                byteOffset,
                $"The instance range ends outside the {slot.InstanceBufferBytes}-byte frame buffer."
            );
        }

        return new Span<byte>(slot.InstanceMapped + byteOffset, checked((int)byteLength));
    }

    /// <summary>Gets the slot's frame descriptor set.</summary>
    internal DescriptorSet GetDescriptorSet(int slotIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _slots[slotIndex].DescriptorSet;
    }

    /// <summary>
    /// Unmaps and destroys every slot buffer and memory allocation. The call is idempotent and must run
    /// before the descriptor state and the logical device are destroyed.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (FrameSlot slot in _slots)
        {
            if (slot is null)
            {
                continue;
            }

            DestroyBuffer(ref slot.FrameBuffer, ref slot.FrameMemory, ref slot.FrameMapped);
            DestroyBuffer(
                ref slot.InstanceBuffer,
                ref slot.InstanceMemory,
                ref slot.InstanceMapped
            );
        }
    }

    private void CreateSlots()
    {
        for (int index = 0; index < _slots.Length; index++)
        {
            FrameSlot slot = new();
            _slots[index] = slot;
            slot.DescriptorSet = _descriptors.AllocateSet();
            _descriptorSets[index] = slot.DescriptorSet;
            RecreateFrameBuffer(slot, Math.Min(INITIAL_FRAME_BYTES, _maxFrameBytes));
            _descriptors.WriteFrameStorageBuffer(
                slot.DescriptorSet,
                slot.FrameBuffer,
                slot.FrameBufferBytes
            );
            RecreateInstanceBuffer(slot, Math.Min(INITIAL_INSTANCE_BYTES, _maxInstanceBytes));
            _descriptors.WriteInstanceStorageBuffer(
                slot.DescriptorSet,
                slot.InstanceBuffer,
                slot.InstanceBufferBytes
            );
        }
    }

    private void RecreateFrameBuffer(FrameSlot slot, ulong capacity)
    {
        DestroyBuffer(ref slot.FrameBuffer, ref slot.FrameMemory, ref slot.FrameMapped);
        CreateMappedBuffer(
            capacity,
            BufferUsageFlags.StorageBufferBit,
            out slot.FrameBuffer,
            out slot.FrameMemory,
            out slot.FrameMapped
        );
        slot.FrameBufferBytes = capacity;
    }

    private void RecreateInstanceBuffer(FrameSlot slot, ulong capacity)
    {
        DestroyBuffer(ref slot.InstanceBuffer, ref slot.InstanceMemory, ref slot.InstanceMapped);
        CreateMappedBuffer(
            capacity,
            BufferUsageFlags.StorageBufferBit,
            out slot.InstanceBuffer,
            out slot.InstanceMemory,
            out slot.InstanceMapped
        );
        slot.InstanceBufferBytes = capacity;
    }

    private void CreateMappedBuffer(
        ulong size,
        BufferUsageFlags usage,
        out Buffer buffer,
        out DeviceMemory memory,
        out byte* mapped
    )
    {
        buffer = default;
        memory = default;
        mapped = null;
        try
        {
            BufferCreateInfo createInfo = new()
            {
                SType = StructureType.BufferCreateInfo,
                Size = size,
                Usage = usage,
                SharingMode = SharingMode.Exclusive,
            };
            Result createResult = _vulkan.CreateBuffer(_device, &createInfo, null, out buffer);
            if (createResult != Result.Success || buffer.Handle == 0)
            {
                throw new GraphicsException(
                    $"The Vulkan frame buffer of {size} byte(s) could not be created: {createResult}.",
                    DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                    null
                );
            }

            MemoryRequirements requirements = _vulkan.GetBufferMemoryRequirements(_device, buffer);
            PhysicalDeviceMemoryProperties memoryProperties =
                _vulkan.GetPhysicalDeviceMemoryProperties(_physicalDevice);
            MemoryAllocateInfo allocateInfo = new()
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = requirements.Size,
                MemoryTypeIndex = FindHostVisibleMemoryType(
                    requirements.MemoryTypeBits,
                    memoryProperties
                ),
            };
            Result allocateResult = _vulkan.AllocateMemory(
                _device,
                &allocateInfo,
                null,
                out memory
            );
            if (allocateResult != Result.Success || memory.Handle == 0)
            {
                throw new GraphicsException(
                    $"The Vulkan frame buffer memory of {size} byte(s) could not be allocated: {allocateResult}.",
                    DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                    null
                );
            }

            Result bindResult = _vulkan.BindBufferMemory(_device, buffer, memory, 0);
            if (bindResult != Result.Success)
            {
                throw new GraphicsException(
                    $"The Vulkan frame buffer memory could not be bound: {bindResult}.",
                    DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                    null
                );
            }

            void* pointer = null;
            Result mapResult = _vulkan.MapMemory(
                _device,
                memory,
                0,
                requirements.Size,
                MemoryMapFlags.None,
                &pointer
            );
            if (mapResult != Result.Success || pointer is null)
            {
                throw new GraphicsException(
                    $"The Vulkan frame buffer memory could not be mapped: {mapResult}.",
                    DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                    null
                );
            }

            mapped = (byte*)pointer;
        }
        catch
        {
            DestroyBuffer(ref buffer, ref memory, ref mapped);
            throw;
        }
    }

    private void DestroyBuffer(ref Buffer buffer, ref DeviceMemory memory, ref byte* mapped)
    {
        if (mapped is not null)
        {
            _vulkan.UnmapMemory(_device, memory);
            mapped = null;
        }

        if (buffer.Handle != 0)
        {
            _vulkan.DestroyBuffer(_device, buffer, null);
            buffer = default;
        }

        if (memory.Handle != 0)
        {
            _vulkan.FreeMemory(_device, memory, null);
            memory = default;
        }
    }

    private static uint FindHostVisibleMemoryType(
        uint typeBits,
        PhysicalDeviceMemoryProperties memoryProperties
    )
    {
        const MemoryPropertyFlags required =
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit;
        for (uint index = 0; index < memoryProperties.MemoryTypeCount; index++)
        {
            bool typeSupported = (typeBits & (1u << (int)index)) != 0;
            bool propertiesMatch =
                (memoryProperties.MemoryTypes[(int)index].PropertyFlags & required) == required;
            if (typeSupported && propertiesMatch)
            {
                return index;
            }
        }

        throw new GraphicsException(
            $"No host-visible coherent Vulkan memory type is available for the frame buffers "
                + $"(type bits 0x{typeBits:X8}).",
            DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
            null
        );
    }

    private sealed class FrameSlot
    {
        internal Buffer FrameBuffer;
        internal DeviceMemory FrameMemory;
        internal byte* FrameMapped;
        internal ulong FrameBufferBytes;
        internal Buffer InstanceBuffer;
        internal DeviceMemory InstanceMemory;
        internal byte* InstanceMapped;
        internal ulong InstanceBufferBytes;
        internal DescriptorSet DescriptorSet;
    }
}
