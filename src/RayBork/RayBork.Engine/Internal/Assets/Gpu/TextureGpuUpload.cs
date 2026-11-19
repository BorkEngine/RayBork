namespace RayBork.Internal;

/// <summary>
/// The GPU upload for one texture asset: the packed descriptor plus the complete KTX2 2.0 file.
/// </summary>
/// <remarks>
/// The bytes are a full KTX2 file with its level index; no source image format is parsed.
/// </remarks>
internal sealed class TextureGpuUpload : AssetGpuUpload
{
    /// <summary>Initializes a texture upload.</summary>
    internal TextureGpuUpload(
        string path,
        TextureAssetDescriptor descriptor,
        ReadOnlyMemory<byte> ktx2Data
    )
        : base(AssetGpuUploadKind.Texture, path)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        Texture = descriptor;
        Ktx2Data = ktx2Data;
    }

    /// <summary>Gets the packed texture descriptor with format, mip count, and sampler.</summary>
    internal TextureAssetDescriptor Texture { get; }

    /// <summary>Gets the complete KTX2 2.0 payload.</summary>
    internal ReadOnlyMemory<byte> Ktx2Data { get; }

    /// <inheritdoc/>
    internal override long EstimatedBytes => Ktx2Data.Length;
}
