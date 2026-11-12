namespace RayBork.Internal;

/// <summary>
/// The GPU upload for one mesh asset: the packed descriptor plus the vertex and index payload.
/// </summary>
/// <remarks>
/// The payload is the fixed-layout vertex stream followed by 32-bit indices, as produced by the bake.
/// The render thread stages it into one vertex buffer and one index buffer; the M3 runtime never
/// parses glTF or OBJ.
/// </remarks>
internal sealed class MeshGpuUpload : AssetGpuUpload {
    /// <summary>
    /// Initializes a mesh upload.
    /// </summary>
    /// <param name="path">The canonical asset path without a leading slash.</param>
    /// <param name="descriptor">The packed mesh descriptor.</param>
    /// <param name="vertexAndIndexData">The vertex stream followed by the 32-bit indices.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    internal MeshGpuUpload(string path, MeshAssetDescriptor descriptor, ReadOnlyMemory<byte> vertexAndIndexData)
        : base(AssetGpuUploadKind.Mesh, path) {
        ArgumentNullException.ThrowIfNull(descriptor);

        Mesh = descriptor;
        VertexAndIndexData = vertexAndIndexData;
    }

    /// <summary>
    /// Gets the packed mesh descriptor that selects the vertex and index counts and topology.
    /// </summary>
    internal MeshAssetDescriptor Mesh { get; }

    /// <summary>
    /// Gets the vertex stream followed by the 32-bit indices.
    /// </summary>
    internal ReadOnlyMemory<byte> VertexAndIndexData { get; }

    /// <inheritdoc/>
    internal override long EstimatedBytes => VertexAndIndexData.Length;
}
