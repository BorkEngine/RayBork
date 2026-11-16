using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace RayBork.Internal;

/// <summary>
/// The backend-owned fullscreen quad the custom post-process pass instances: four vertices in the fixed
/// 88-byte engine vertex layout and six 32-bit indices.
/// </summary>
/// <remarks>
/// <para>
/// The positions span <c>[-0.5, 0.5]</c> in the local XY plane and the UVs span <c>[0, 1]</c> with
/// <c>(0, 0)</c> at the image top-left. The contract vertex stage maps it with
/// <c>vUv = aUv; gl_Position = vec4(aPosition.xy * 2.0, 0.0, 1.0);</c>, so a pass preserves the
/// top-left image orientation that the resolved color and depth images and the present and store-back
/// passes use.
/// </para>
/// <para>
/// The buffers are a small host-visible coherent allocation owned by the backend, not a public asset.
/// They are created with the device and destroyed with it; render-thread only.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanFullscreenQuad : IDisposable {
    private const uint INDEX_COUNT = 6;

    private readonly Vk _vulkan;
    private readonly Device _device;
    private Buffer _vertexBuffer;
    private DeviceMemory _vertexMemory;
    private byte* _vertexMapped;
    private Buffer _indexBuffer;
    private DeviceMemory _indexMemory;
    private byte* _indexMapped;
    private bool _disposed;

    private VulkanFullscreenQuad(Vk vulkan, Device device) {
        _vulkan = vulkan;
        _device = device;
    }

    /// <summary>Gets the vertex buffer in the fixed engine vertex layout.</summary>
    internal Buffer VertexBuffer => _vertexBuffer;

    /// <summary>Gets the 32-bit index buffer.</summary>
    internal Buffer IndexBuffer => _indexBuffer;

    /// <summary>Gets the indexed triangle count: six indices, two triangles.</summary>
    internal static uint IndexCount => INDEX_COUNT;

    /// <summary>
    /// Creates the vertex and index buffers and fills them with the fullscreen quad.
    /// </summary>
    /// <param name="device">The device that owns the buffers.</param>
    /// <returns>The created quad.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is <c>null</c>.</exception>
    /// <exception cref="GraphicsException">A buffer or its memory could not be created.</exception>
    internal static VulkanFullscreenQuad Create(VulkanDevice device) {
        ArgumentNullException.ThrowIfNull(device);

        VulkanFullscreenQuad quad = new(device.Vulkan, device.Device);
        try {
            quad.CreateBuffers(device);
            return quad;
        } catch {
            quad.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Unmaps and destroys the vertex and index buffers. The call is idempotent and must run before the
    /// device is destroyed.
    /// </summary>
    public void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;
        DestroyBuffer(ref _indexBuffer, ref _indexMemory, ref _indexMapped);
        DestroyBuffer(ref _vertexBuffer, ref _vertexMemory, ref _vertexMapped);
    }

    private void CreateBuffers(VulkanDevice device) {
        // Position (-0.5, -0.5) is clip-space (-1, -1), the framebuffer top-left, so its UV is (0, 0).
        Vertex[] vertices = [
            new(new Vector3(-0.5f, -0.5f, 0f), normal: Vector3.UnitZ, uv: new Vector2(0f, 0f)),
            new(new Vector3(0.5f, -0.5f, 0f), normal: Vector3.UnitZ, uv: new Vector2(1f, 0f)),
            new(new Vector3(0.5f, 0.5f, 0f), normal: Vector3.UnitZ, uv: new Vector2(1f, 1f)),
            new(new Vector3(-0.5f, 0.5f, 0f), normal: Vector3.UnitZ, uv: new Vector2(0f, 1f)),
        ];
        uint[] indices = [0u, 1u, 2u, 0u, 2u, 3u];
        ReadOnlySpan<byte> vertexBytes = MemoryMarshal.AsBytes(vertices.AsSpan());
        ReadOnlySpan<byte> indexBytes = MemoryMarshal.AsBytes(indices.AsSpan());
        _vertexBuffer = CreateMappedBuffer(
            device,
            (ulong)vertexBytes.Length,
            BufferUsageFlags.VertexBufferBit,
            out _vertexMemory,
            out _vertexMapped);
        vertexBytes.CopyTo(new Span<byte>(_vertexMapped, vertexBytes.Length));
        _indexBuffer = CreateMappedBuffer(
            device,
            (ulong)indexBytes.Length,
            BufferUsageFlags.IndexBufferBit,
            out _indexMemory,
            out _indexMapped);
        indexBytes.CopyTo(new Span<byte>(_indexMapped, indexBytes.Length));
    }

    private static Buffer CreateMappedBuffer(
        VulkanDevice device,
        ulong size,
        BufferUsageFlags usage,
        out DeviceMemory memory,
        out byte* mapped) {
        Vk vulkan = device.Vulkan;
        Buffer buffer = default;
        memory = default;
        mapped = null;
        try {
            BufferCreateInfo createInfo = new() {
                SType = StructureType.BufferCreateInfo,
                Size = size,
                Usage = usage,
                SharingMode = SharingMode.Exclusive,
            };
            Result createResult = vulkan.CreateBuffer(device.Device, &createInfo, null, out buffer);
            if (createResult != Result.Success || buffer.Handle == 0) {
                throw new GraphicsException(
                    $"The post-process fullscreen quad buffer of {size} byte(s) could not be created: {createResult}.",
                    DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                    null);
            }

            MemoryRequirements requirements = vulkan.GetBufferMemoryRequirements(device.Device, buffer);
            PhysicalDeviceMemoryProperties memoryProperties =
                vulkan.GetPhysicalDeviceMemoryProperties(device.PhysicalDevice);
            MemoryAllocateInfo allocateInfo = new() {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = requirements.Size,
                MemoryTypeIndex = FindHostVisibleMemoryType(requirements.MemoryTypeBits, memoryProperties),
            };
            Result allocateResult = vulkan.AllocateMemory(device.Device, &allocateInfo, null, out memory);
            if (allocateResult != Result.Success || memory.Handle == 0) {
                throw new GraphicsException(
                    $"The post-process fullscreen quad buffer memory of {size} byte(s) could not be allocated: {allocateResult}.",
                    DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                    null);
            }

            Result bindResult = vulkan.BindBufferMemory(device.Device, buffer, memory, 0);
            if (bindResult != Result.Success) {
                throw new GraphicsException(
                    $"The post-process fullscreen quad buffer memory could not be bound: {bindResult}.",
                    DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                    null);
            }

            void* pointer = null;
            Result mapResult = vulkan.MapMemory(device.Device, memory, 0, requirements.Size, MemoryMapFlags.None, &pointer);
            if (mapResult != Result.Success || pointer is null) {
                throw new GraphicsException(
                    $"The post-process fullscreen quad buffer memory could not be mapped: {mapResult}.",
                    DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                    null);
            }

            mapped = (byte*)pointer;
            return buffer;
        } catch {
            if (mapped is not null) {
                vulkan.UnmapMemory(device.Device, memory);
                mapped = null;
            }

            if (buffer.Handle != 0) {
                vulkan.DestroyBuffer(device.Device, buffer, null);
            }

            if (memory.Handle != 0) {
                vulkan.FreeMemory(device.Device, memory, null);
                memory = default;
            }

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
            $"No host-visible coherent Vulkan memory type is available for the post-process fullscreen "
                + $"quad (type bits 0x{typeBits:X8}).",
            DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
            null);
    }
}
