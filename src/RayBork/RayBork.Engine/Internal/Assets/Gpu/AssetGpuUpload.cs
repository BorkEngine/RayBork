namespace RayBork.Internal;

/// <summary>
/// The immutable CPU-side snapshot of one engine asset's GPU upload.
/// </summary>
/// <remarks>
/// <para>
/// The asset loader builds the upload after it decodes the packed descriptor and reads the sibling
/// payload entries; the render thread consumes it through <see cref="GpuAssetBridge"/>. The bridge
/// retains the upload for the lifetime of the GPU resource so a device recovery can recreate it.
/// </para>
/// <para>
/// Payload memory is handed over, not copied: the loader must not mutate or recycle the arrays while
/// the resource is live. <see cref="Path"/> is the canonical asset path without a leading slash and
/// is used only for diagnostics; the constructor accepts the public form with one optional leading
/// slash and normalizes it.
/// </para>
/// </remarks>
internal abstract class AssetGpuUpload {
    private protected AssetGpuUpload(AssetGpuUploadKind kind, string path) {
        ArgumentNullException.ThrowIfNull(path);

        Kind = kind;
        Path = AssetPath.Normalize(path);
    }

    /// <summary>
    /// Gets the kind of GPU resource this upload creates.
    /// </summary>
    internal AssetGpuUploadKind Kind { get; }

    /// <summary>
    /// Gets the canonical asset path without a leading slash. It is diagnostic metadata only.
    /// </summary>
    internal string Path { get; }
}
