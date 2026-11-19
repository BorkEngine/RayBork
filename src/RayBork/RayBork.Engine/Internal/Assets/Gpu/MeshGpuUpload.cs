namespace RayBork.Internal;

/// <summary>
/// The GPU upload for one mesh asset: the packed descriptor plus the vertex and index payload.
/// </summary>
/// <remarks>
/// The payload is the fixed-layout vertex stream followed by 32-bit indices, staged into one vertex
/// buffer and one index buffer.
/// </remarks>
internal sealed class MeshGpuUpload : AssetGpuUpload
{
    /// <summary>Initializes a mesh upload.</summary>
    internal MeshGpuUpload(
        string path,
        MeshAssetDescriptor descriptor,
        ReadOnlyMemory<byte> vertexAndIndexData
    )
        : base(AssetGpuUploadKind.Mesh, path)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        Mesh = descriptor;
        VertexAndIndexData = vertexAndIndexData;
    }

    /// <summary>Gets the packed mesh descriptor with counts and topology.</summary>
    internal MeshAssetDescriptor Mesh { get; }

    /// <summary>Gets the vertex stream followed by the 32-bit indices.</summary>
    internal ReadOnlyMemory<byte> VertexAndIndexData { get; }

    /// <inheritdoc/>
    internal override long EstimatedBytes => VertexAndIndexData.Length;
}
