using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace RayBork.Internal;

/// <summary>
/// The per-frame-in-flight GPU resources of the geometry path: one host-visible frame uniform
/// buffer, one growable host-visible instance storage buffer, and one descriptor set per slot.
/// </summary>
/// <remarks>
/// <para>
/// The renderer waits for a slot's fence before it records into that slot, so the slot's buffers and
/// descriptor set are never referenced by an in-flight frame when they grow. The uniform buffer holds
/// one 256-byte <see cref="ShaderAbiFrame"/> block; the instance buffer holds fixed-stride
/// <c>RayBorkInstance</c> records indexed by <c>gl_InstanceIndex</c>. Both buffers are mapped once and
/// stay mapped for their lifetime. The frame block is static rather than dynamically offset because
/// the geometry set layout also carries the update-after-bind texture array, and Vulkan forbids
/// dynamic uniform buffers in a set layout that has an update-after-bind binding.
/// </para>
/// <para>
/// The instance buffer is bounded by the smaller of the device's storage-buffer range and
/// <see cref="MAX_INSTANCE_BYTES"/>; a frame that needs more is a fatal <c>RB3001</c> failure rather
/// than a dropped draw. The buffers, memory, and sets are destroyed before the descriptor state and
/// the device. Every member is render-thread only and the call is not thread-safe.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanFrameResources : IDisposable {
    /// <summary>The number of frames the renderer may have in flight.</summary>
    internal const int FRAMES_IN_FLIGHT = 3;

    /// <summary>The byte size of one frame uniform block.</summary>
    internal const int FRAME_BLOCK_SIZE = ShaderAbiFrame.BLOCK_SIZE;

    /// <summary>The upper bound the renderer places on the instance storage buffer (64 MiB).</summary>
    internal const ulong MAX_INSTANCE_BYTES = 64UL * 1024 * 1024;

    private const ulong INITIAL_INSTANCE_BYTES = 64UL * 1024;
    private const ulong INSTANCE_GROWTH_FACTOR = 2;
    private const ulong FRAME_BLOCK_BYTES = FRAME_BLOCK_SIZE;

    private readonly Vk _vulkan;
    private readonly Device _device;
    private readonly PhysicalDevice _physicalDevice;
    private readonly VulkanDescriptorState _descriptors;
    private readonly FrameSlot[] _slots;
    private readonly ulong _maxInstanceBytes;
    private readonly DescriptorSet[] _descriptorSets;
    private bool _disposed;

    private VulkanFrameResources(
        Vk vulkan,
        Device device,
        PhysicalDevice physicalDevice,
        VulkanDescriptorState descriptors,
        ulong maxInstanceBytes) {
        _vulkan = vulkan;
        _device = device;
        _physicalDevice = physicalDevice;
        _descriptors = descriptors;
        _maxInstanceBytes = maxInstanceBytes;
        _slots = new FrameSlot[FRAMES_IN_FLIGHT];
        _descriptorSets = new DescriptorSet[FRAMES_IN_FLIGHT];
    }

    /// <summary>
    /// Gets the frame descriptor sets in slot order.
    /// </summary>
    internal DescriptorSet[] DescriptorSets => _descriptorSets;

    /// <summary>
    /// Gets the instance storage-buffer bound in bytes.
    /// </summary>
    internal ulong MaxInstanceBytes => _maxInstanceBytes;

    /// <summary>
    /// Creates the per-slot buffers, memory, and descriptor sets.
    /// </summary>
    /// <param name="device">The device that owns the resources.</param>
    /// <param name="descriptors">The shared descriptor state.</param>
    /// <returns>The created frame resources.</returns>
    /// <exception cref="GraphicsException">
    /// A Vulkan object could not be created. The diagnostic code is <c>RB3001</c>.
    /// </exception>
    internal static VulkanFrameResources Create(VulkanDevice device, VulkanDescriptorState descriptors) {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(descriptors);

        ulong maxInstanceBytes = Math.Min(
            (ulong)device.Capabilities.MaxStorageBufferRange,
            MAX_INSTANCE_BYTES);
        VulkanFrameResources resources = new(
            device.Vulkan,
            device.Device,
            device.PhysicalDevice,
            descriptors,
            maxInstanceBytes);
        try {
            resources.CreateSlots();
            return resources;
        } catch {
            resources.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Ensures one frame slot can hold the frame's instance records and writes the slot's buffer
    /// descriptors when the instance buffer grows.
    /// </summary>
    /// <remarks>
    /// The caller has already waited for the slot's fence, so recreating the instance buffer and
    /// rewriting the slot's descriptor set cannot disturb an in-flight frame that references them.
    /// The frame uniform block is fixed at <see cref="FRAME_BLOCK_SIZE"/> bytes because the geometry
    /// set layout also carries the update-after-bind texture array, which forbids dynamic uniform
    /// buffers in the same set.
    /// </remarks>
    /// <param name="slotIndex">The frame slot index.</param>
    /// <param name="instanceBytes">
    /// The instance-record bytes the frame needs, including any per-batch alignment.
    /// </param>
    /// <param name="frameIndex">The frame index, for fatal diagnostics.</param>
    /// <exception cref="GraphicsException">
    /// The frame needs more instance bytes than the storage-buffer bound. The diagnostic code is
    /// <c>RB3001</c>.
    /// </exception>
    internal void EnsureSlot(int slotIndex, ulong instanceBytes, long frameIndex) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        FrameSlot slot = _slots[slotIndex];
        if (instanceBytes > _maxInstanceBytes) {
            throw new GraphicsException(
                $"Frame {frameIndex} needs {instanceBytes} instance-record bytes, which exceeds the "
                    + $"{_maxInstanceBytes}-byte instance storage-buffer bound (the device's "
                    + "maxStorageBufferRange capped at 64 MiB). Reduce the frame's instance count or "
                    + "texture and uniform payload.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null);
        }

        ulong requiredInstanceBytes = Math.Max(instanceBytes, Math.Min(INITIAL_INSTANCE_BYTES, _maxInstanceBytes));
        if (requiredInstanceBytes > slot.InstanceBufferBytes) {
            ulong capacity = Math.Max(requiredInstanceBytes, slot.InstanceBufferBytes * INSTANCE_GROWTH_FACTOR);
            capacity = Math.Min(capacity, _maxInstanceBytes);
            RecreateInstanceBuffer(slot, capacity);
            _descriptors.WriteInstanceStorageBuffer(slot.DescriptorSet, slot.InstanceBuffer, capacity);
        }
    }

    /// <summary>
    /// Gets the slot's 256-byte frame uniform block in the mapped buffer.
    /// </summary>
    /// <param name="slotIndex">The frame slot index.</param>
    /// <returns>The writable block.</returns>
    internal Span<byte> GetFrameBlockSpan(int slotIndex) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new Span<byte>(_slots[slotIndex].FrameMapped, FRAME_BLOCK_SIZE);
    }

    /// <summary>
    /// Gets a writable range of the slot's mapped instance buffer.
    /// </summary>
    /// <param name="slotIndex">The frame slot index.</param>
    /// <param name="byteOffset">The byte offset of the range.</param>
    /// <param name="byteLength">The range length in bytes.</param>
    /// <returns>The writable range.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The range lies outside the slot's buffer.</exception>
    internal Span<byte> GetInstanceSpan(int slotIndex, ulong byteOffset, ulong byteLength) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        FrameSlot slot = _slots[slotIndex];
        if (byteOffset + byteLength > slot.InstanceBufferBytes) {
            throw new ArgumentOutOfRangeException(
                nameof(byteOffset),
                byteOffset,
                $"The instance range ends outside the {slot.InstanceBufferBytes}-byte frame buffer.");
        }

        return new Span<byte>(slot.InstanceMapped + byteOffset, checked((int)byteLength));
    }

    /// <summary>
    /// Gets the slot's frame descriptor set.
    /// </summary>
    /// <param name="slotIndex">The frame slot index.</param>
    /// <returns>The descriptor set.</returns>
    internal DescriptorSet GetDescriptorSet(int slotIndex) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _slots[slotIndex].DescriptorSet;
    }

    /// <summary>
    /// Unmaps and destroys every buffer, memory allocation, and descriptor set. The call is
    /// idempotent and must run before the descriptor state and the logical device are destroyed.
    /// </summary>
    public void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;
        foreach (FrameSlot slot in _slots) {
            if (slot is null) {
                continue;
            }

            DestroyBuffer(ref slot.FrameBuffer, ref slot.FrameMemory, ref slot.FrameMapped);
            DestroyBuffer(ref slot.InstanceBuffer, ref slot.InstanceMemory, ref slot.InstanceMapped);
        }
    }

    private void CreateSlots() {
        for (int index = 0; index < _slots.Length; index++) {
            FrameSlot slot = new();
            _slots[index] = slot;
            slot.DescriptorSet = _descriptors.AllocateSet();
            _descriptorSets[index] = slot.DescriptorSet;
            RecreateFrameBuffer(slot, FRAME_BLOCK_BYTES);
            _descriptors.WriteFrameUniformBuffer(slot.DescriptorSet, slot.FrameBuffer, FRAME_BLOCK_SIZE);
            RecreateInstanceBuffer(slot, Math.Min(INITIAL_INSTANCE_BYTES, _maxInstanceBytes));
            _descriptors.WriteInstanceStorageBuffer(
                slot.DescriptorSet,
                slot.InstanceBuffer,
                slot.InstanceBufferBytes);
        }
    }

    private void RecreateFrameBuffer(FrameSlot slot, ulong capacity) {
        DestroyBuffer(ref slot.FrameBuffer, ref slot.FrameMemory, ref slot.FrameMapped);
        CreateMappedBuffer(
            capacity,
            BufferUsageFlags.UniformBufferBit,
            out slot.FrameBuffer,
            out slot.FrameMemory,
            out slot.FrameMapped);
        slot.FrameBufferBytes = capacity;
    }

    private void RecreateInstanceBuffer(FrameSlot slot, ulong capacity) {
        DestroyBuffer(ref slot.InstanceBuffer, ref slot.InstanceMemory, ref slot.InstanceMapped);
        CreateMappedBuffer(
            capacity,
            BufferUsageFlags.StorageBufferBit,
            out slot.InstanceBuffer,
            out slot.InstanceMemory,
            out slot.InstanceMapped);
        slot.InstanceBufferBytes = capacity;
    }

    private void CreateMappedBuffer(
        ulong size,
        BufferUsageFlags usage,
        out Buffer buffer,
        out DeviceMemory memory,
        out byte* mapped) {
        buffer = default;
        memory = default;
        mapped = null;
        try {
            BufferCreateInfo createInfo = new() {
                SType = StructureType.BufferCreateInfo,
                Size = size,
                Usage = usage,
                SharingMode = SharingMode.Exclusive,
            };
            Result createResult = _vulkan.CreateBuffer(_device, &createInfo, null, out buffer);
            if (createResult != Result.Success || buffer.Handle == 0) {
                throw new GraphicsException(
                    $"The Vulkan frame buffer of {size} byte(s) could not be created: {createResult}.",
                    DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                    null);
            }

            MemoryRequirements requirements = _vulkan.GetBufferMemoryRequirements(_device, buffer);
            PhysicalDeviceMemoryProperties memoryProperties =
                _vulkan.GetPhysicalDeviceMemoryProperties(_physicalDevice);
            MemoryAllocateInfo allocateInfo = new() {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = requirements.Size,
                MemoryTypeIndex = FindHostVisibleMemoryType(requirements.MemoryTypeBits, memoryProperties),
            };
            Result allocateResult = _vulkan.AllocateMemory(_device, &allocateInfo, null, out memory);
            if (allocateResult != Result.Success || memory.Handle == 0) {
                throw new GraphicsException(
                    $"The Vulkan frame buffer memory of {size} byte(s) could not be allocated: {allocateResult}.",
                    DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                    null);
            }

            Result bindResult = _vulkan.BindBufferMemory(_device, buffer, memory, 0);
            if (bindResult != Result.Success) {
                throw new GraphicsException(
                    $"The Vulkan frame buffer memory could not be bound: {bindResult}.",
                    DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                    null);
            }

            void* pointer = null;
            Result mapResult = _vulkan.MapMemory(_device, memory, 0, requirements.Size, MemoryMapFlags.None, &pointer);
            if (mapResult != Result.Success || pointer is null) {
                throw new GraphicsException(
                    $"The Vulkan frame buffer memory could not be mapped: {mapResult}.",
                    DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                    null);
            }

            mapped = (byte*)pointer;
        } catch {
            DestroyBuffer(ref buffer, ref memory, ref mapped);
            throw;
        }
    }

    private void DestroyBuffer(ref Buffer buffer, ref DeviceMemory memory, ref byte* mapped) {
        if (mapped is not null) {
            _vulkan.UnmapMemory(_device, memory);
            mapped = null;
        }

        if (buffer.Handle != 0) {
            _vulkan.DestroyBuffer(_device, buffer, null);
            buffer = default;
        }

        if (memory.Handle != 0) {
            _vulkan.FreeMemory(_device, memory, null);
            memory = default;
        }
    }

    private static uint FindHostVisibleMemoryType(
        uint typeBits,
        PhysicalDeviceMemoryProperties memoryProperties) {
        const MemoryPropertyFlags required =
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit;
        for (uint index = 0; index < memoryProperties.MemoryTypeCount; index++) {
            bool typeSupported = (typeBits & (1u << (int)index)) != 0;
            bool propertiesMatch =
                (memoryProperties.MemoryTypes[(int)index].PropertyFlags & required) == required;
            if (typeSupported && propertiesMatch) {
                return index;
            }
        }

        throw new GraphicsException(
            $"No host-visible coherent Vulkan memory type is available for the frame buffers "
                + $"(type bits 0x{typeBits:X8}).",
            DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
            null);
    }

    private sealed class FrameSlot {
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
