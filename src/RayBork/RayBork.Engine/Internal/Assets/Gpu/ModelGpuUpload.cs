namespace RayBork.Internal;

/// <summary>
/// The CPU-only upload description for one model asset. A model owns no GPU handles of its own: its
/// part meshes and shaders are loaded and uploaded independently, so this upload is never queued on
/// the <see cref="GpuAssetBridge"/>.
/// </summary>
internal sealed class ModelGpuUpload : AssetGpuUpload {
    /// <summary>
    /// Initializes the model upload marker.
    /// </summary>
    /// <param name="path">The canonical model path without a leading slash.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <c>null</c>.</exception>
    internal ModelGpuUpload(string path)
        : base(AssetGpuUploadKind.Model, path) {
    }
}
