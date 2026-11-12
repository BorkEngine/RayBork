using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace RayBork.Internal;

/// <summary>
/// The Vulkan objects behind one uploaded mesh: the device-local vertex and index buffers and the one
/// memory allocation that backs both.
/// </summary>
/// <remarks>
/// Created on the render thread by <see cref="VulkanAssetWorker.BeginUpload"/> and recreated by
/// <see cref="IAssetGpuWorker.Restore"/> after a device recovery. The vertex buffer is the primary
/// handle. Topology is descriptor metadata, not a buffer property, so it is not stored here. The two
/// buffers share one allocation: the index buffer starts at the vertex requirement's alignment. The
/// buffers and the memory are destroyed together by <see cref="VulkanAssetWorker.Destroy"/>.
/// </remarks>
internal sealed class VulkanMeshAssetResource : VulkanAssetResource {
    /// <summary>
    /// Initializes a mesh resource.
    /// </summary>
    /// <param name="upload">The retained mesh upload description.</param>
    /// <param name="vertexBuffer">The vertex buffer.</param>
    /// <param name="indexBuffer">The 32-bit index buffer.</param>
    /// <param name="memory">The device memory that backs both buffers.</param>
    /// <param name="vertexBufferSize">The vertex buffer size in bytes.</param>
    /// <param name="indexBufferSize">The index buffer size in bytes.</param>
    /// <param name="deviceGeneration">The logical-device generation that owns the handles.</param>
    internal VulkanMeshAssetResource(
        MeshGpuUpload upload,
        Buffer vertexBuffer,
        Buffer indexBuffer,
        DeviceMemory memory,
        ulong vertexBufferSize,
        ulong indexBufferSize,
        int deviceGeneration)
        : base(upload, unchecked((long)vertexBuffer.Handle), deviceGeneration) {
        VertexBuffer = vertexBuffer;
        IndexBuffer = indexBuffer;
        Memory = memory;
        VertexBufferSize = vertexBufferSize;
        IndexBufferSize = indexBufferSize;
    }

    /// <summary>
    /// Gets the vertex buffer.
    /// </summary>
    internal Buffer VertexBuffer { get; private set; }

    /// <summary>
    /// Gets the 32-bit index buffer.
    /// </summary>
    internal Buffer IndexBuffer { get; private set; }

    /// <summary>
    /// Gets the device memory that backs both buffers.
    /// </summary>
    internal DeviceMemory Memory { get; private set; }

    /// <summary>
    /// Gets the vertex buffer size in bytes.
    /// </summary>
    internal ulong VertexBufferSize { get; private set; }

    /// <summary>
    /// Gets the index buffer size in bytes.
    /// </summary>
    internal ulong IndexBufferSize { get; private set; }

    /// <summary>
    /// Adopts the handles of a mesh recreated after a device recovery.
    /// </summary>
    /// <param name="restored">The recreated mesh resource.</param>
    internal override void AdoptRestored(GpuAssetResource restored) {
        if (ReferenceEquals(this, restored)) {
            return;
        }

        VulkanMeshAssetResource mesh = CastRestored<VulkanMeshAssetResource>(restored);
        VertexBuffer = mesh.VertexBuffer;
        IndexBuffer = mesh.IndexBuffer;
        Memory = mesh.Memory;
        VertexBufferSize = mesh.VertexBufferSize;
        IndexBufferSize = mesh.IndexBufferSize;
        AdoptGeneration(mesh);
        ResetRelease();
        PublishHandle(mesh.Handle);
    }
}
