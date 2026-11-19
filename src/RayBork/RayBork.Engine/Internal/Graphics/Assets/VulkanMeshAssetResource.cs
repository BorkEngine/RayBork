using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace RayBork.Internal;

/// <summary>
/// The Vulkan objects behind one uploaded mesh: device-local vertex and index buffers sharing one
/// memory allocation.
/// </summary>
/// <remarks>
/// Created by <see cref="VulkanAssetWorker.BeginUpload"/> and recreated by
/// <see cref="IAssetGpuWorker.Restore"/>; the index buffer starts at the vertex requirement's
/// alignment, and both buffers are destroyed together with the memory.
/// </remarks>
internal sealed class VulkanMeshAssetResource : VulkanAssetResource
{
    /// <summary>Initializes a mesh resource.</summary>
    internal VulkanMeshAssetResource(
        MeshGpuUpload upload,
        Buffer vertexBuffer,
        Buffer indexBuffer,
        DeviceMemory memory,
        ulong vertexBufferSize,
        ulong indexBufferSize,
        int deviceGeneration
    )
        : base(upload, unchecked((long)vertexBuffer.Handle), deviceGeneration)
    {
        VertexBuffer = vertexBuffer;
        IndexBuffer = indexBuffer;
        Memory = memory;
        VertexBufferSize = vertexBufferSize;
        IndexBufferSize = indexBufferSize;
    }

    /// <summary>Gets the vertex buffer.</summary>
    internal Buffer VertexBuffer { get; private set; }

    /// <summary>Gets the 32-bit index buffer.</summary>
    internal Buffer IndexBuffer { get; private set; }

    /// <summary>Gets the device memory that backs both buffers.</summary>
    internal DeviceMemory Memory { get; private set; }

    /// <summary>Gets the vertex buffer size in bytes.</summary>
    internal ulong VertexBufferSize { get; private set; }

    /// <summary>Gets the index buffer size in bytes.</summary>
    internal ulong IndexBufferSize { get; private set; }

    /// <summary>Adopts the handles of a mesh recreated after a device recovery.</summary>
    internal override void AdoptRestored(GpuAssetResource restored)
    {
        if (ReferenceEquals(this, restored))
        {
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
