namespace RayBork.Internal;

/// <summary>
/// The GPU upload for one internal shader program: the packed descriptor plus the vertex and fragment
/// SPIR-V.
/// </summary>
/// <remarks>
/// Modules are created per stage on the render thread; the runtime never parses GLSL or SPIR-V.
/// </remarks>
internal sealed class ShaderGpuUpload : AssetGpuUpload
{
    /// <summary>Initializes a shader upload.</summary>
    internal ShaderGpuUpload(
        string path,
        ShaderAssetDescriptor descriptor,
        ReadOnlyMemory<byte> vertexSpirv,
        ReadOnlyMemory<byte> fragmentSpirv
    )
        : base(AssetGpuUploadKind.Shader, path)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        Shader = descriptor;
        VertexSpirv = vertexSpirv;
        FragmentSpirv = fragmentSpirv;
    }

    /// <summary>Gets the packed shader descriptor with entry points, stages, and defaults.</summary>
    internal ShaderAssetDescriptor Shader { get; }

    /// <summary>Gets the vertex-stage SPIR-V module.</summary>
    internal ReadOnlyMemory<byte> VertexSpirv { get; }

    /// <summary>Gets the fragment-stage SPIR-V module.</summary>
    internal ReadOnlyMemory<byte> FragmentSpirv { get; }

    /// <inheritdoc/>
    internal override long EstimatedBytes => VertexSpirv.Length + (long)FragmentSpirv.Length;
}
