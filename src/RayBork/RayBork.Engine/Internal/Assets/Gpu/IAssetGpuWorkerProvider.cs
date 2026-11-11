namespace RayBork.Internal;

/// <summary>
/// Implemented by a renderer backend that can create a GPU asset worker for the current device.
/// </summary>
/// <remarks>
/// The render loop asks for the worker on the render thread immediately after the backend initializes
/// successfully and before it publishes readiness, so no asset upload can be queued without a worker.
/// A backend that does not implement this interface leaves the bridge without a worker; uploads then
/// fail with <see cref="GraphicsException"/> instead of waiting forever. The returned worker and the
/// device share a lifetime: the backend recreates the worker's device only through
/// <see cref="IAssetGpuWorker.Restore"/>.
/// </remarks>
internal interface IAssetGpuWorkerProvider {
    /// <summary>
    /// Creates the asset GPU worker for the backend's current device.
    /// </summary>
    /// <returns>The worker. It must not be <c>null</c>.</returns>
    IAssetGpuWorker CreateAssetGpuWorker();
}
