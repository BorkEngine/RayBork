namespace RayBork.Internal;

/// <summary>
/// The CPU-only <see cref="GpuAssetResource"/> behind a loaded <see cref="Model"/>.
/// </summary>
/// <remarks>
/// A model owns no images, buffers, samplers, or shader modules; its parts are separate engine assets
/// with their own resources. The resource exists because every <see cref="EngineAsset"/> owns one, and
/// its handle is always <c>0</c>. The catalog never queues it on the <see cref="GpuAssetBridge"/>.
/// </remarks>
internal sealed class ModelAssetResource : GpuAssetResource {
    /// <summary>
    /// Initializes the CPU-only model resource.
    /// </summary>
    /// <param name="upload">The marker upload that describes the model path.</param>
    /// <exception cref="ArgumentNullException"><paramref name="upload"/> is <c>null</c>.</exception>
    internal ModelAssetResource(ModelGpuUpload upload)
        : base(upload, handle: 0) {
    }
}
