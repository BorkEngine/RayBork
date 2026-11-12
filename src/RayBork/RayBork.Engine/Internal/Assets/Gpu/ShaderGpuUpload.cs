namespace RayBork.Internal;

/// <summary>
/// The GPU upload for one internal shader program: the packed descriptor plus the vertex and fragment
/// SPIR-V.
/// </summary>
/// <remarks>
/// The runtime never parses GLSL or SPIR-V. The render thread creates one shader module per stage from
/// the committed SPIR-V payloads and keeps the bake sidecar metadata for pipeline creation. The
/// catalog caches the uploaded program; public <see cref="Shader"/> values are materials over it.
/// </remarks>
internal sealed class ShaderGpuUpload : AssetGpuUpload {
    /// <summary>
    /// Initializes a shader upload.
    /// </summary>
    /// <param name="path">The canonical asset path without a leading slash.</param>
    /// <param name="descriptor">The packed shader descriptor.</param>
    /// <param name="vertexSpirv">The vertex-stage SPIR-V module.</param>
    /// <param name="fragmentSpirv">The fragment-stage SPIR-V module.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    internal ShaderGpuUpload(
        string path,
        ShaderAssetDescriptor descriptor,
        ReadOnlyMemory<byte> vertexSpirv,
        ReadOnlyMemory<byte> fragmentSpirv)
        : base(AssetGpuUploadKind.Shader, path) {
        ArgumentNullException.ThrowIfNull(descriptor);

        Shader = descriptor;
        VertexSpirv = vertexSpirv;
        FragmentSpirv = fragmentSpirv;
    }

    /// <summary>
    /// Gets the packed shader descriptor that carries the entry points, stages, and default state.
    /// </summary>
    internal ShaderAssetDescriptor Shader { get; }

    /// <summary>
    /// Gets the vertex-stage SPIR-V module.
    /// </summary>
    internal ReadOnlyMemory<byte> VertexSpirv { get; }

    /// <summary>
    /// Gets the fragment-stage SPIR-V module.
    /// </summary>
    internal ReadOnlyMemory<byte> FragmentSpirv { get; }

    /// <inheritdoc/>
    internal override long EstimatedBytes => VertexSpirv.Length + (long)FragmentSpirv.Length;
}
