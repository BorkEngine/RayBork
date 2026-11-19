namespace RayBork.Internal;

/// <summary>
/// The immutable CPU-side snapshot of one engine asset's GPU upload.
/// </summary>
/// <remarks>
/// The bridge retains it for the GPU resource's lifetime so a device recovery can recreate the
/// resource. Payload memory is handed over, not copied; <see cref="Path"/> is diagnostic only.
/// </remarks>
internal abstract class AssetGpuUpload
{
    private protected AssetGpuUpload(AssetGpuUploadKind kind, string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        Kind = kind;
        Path = AssetPath.Normalize(path);
    }

    /// <summary>Gets the kind of GPU resource this upload creates.</summary>
    internal AssetGpuUploadKind Kind { get; }

    /// <summary>Gets the canonical asset path without a leading slash; diagnostic only.</summary>
    internal string Path { get; }

    /// <summary>Gets the approximate payload bytes retained for the upload budget.</summary>
    internal virtual long EstimatedBytes => 0;
}
