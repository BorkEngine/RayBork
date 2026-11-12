namespace RayBork.Internal;

/// <summary>
/// The GPU upload for one texture asset: the packed descriptor plus the complete KTX2 2.0 file.
/// </summary>
/// <remarks>
/// The render thread creates the image, view, and sampler from the descriptor and the KTX2 bytes.
/// The bytes are a full KTX2 file, including its level index; the worker never parses a source image
/// format.
/// </remarks>
internal sealed class TextureGpuUpload : AssetGpuUpload {
    /// <summary>
    /// Initializes a texture upload.
    /// </summary>
    /// <param name="path">The canonical asset path without a leading slash.</param>
    /// <param name="descriptor">The packed texture descriptor.</param>
    /// <param name="ktx2Data">The complete KTX2 2.0 payload.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    internal TextureGpuUpload(string path, TextureAssetDescriptor descriptor, ReadOnlyMemory<byte> ktx2Data)
        : base(AssetGpuUploadKind.Texture, path) {
        ArgumentNullException.ThrowIfNull(descriptor);

        Texture = descriptor;
        Ktx2Data = ktx2Data;
    }

    /// <summary>
    /// Gets the packed texture descriptor that selects the format, mip count, and sampler state.
    /// </summary>
    internal TextureAssetDescriptor Texture { get; }

    /// <summary>
    /// Gets the complete KTX2 2.0 payload.
    /// </summary>
    internal ReadOnlyMemory<byte> Ktx2Data { get; }

    /// <inheritdoc/>
    internal override long EstimatedBytes => Ktx2Data.Length;
}
