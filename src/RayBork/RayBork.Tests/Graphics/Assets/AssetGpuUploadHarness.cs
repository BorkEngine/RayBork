using RayBork.Internal;

namespace RayBork.Tests.Graphics.Assets;

/// <summary>
/// Wires the real <see cref="VulkanAssetWorker"/> into a <see cref="GpuAssetBridge"/> the same way the
/// render loop does, so the GPU tests exercise the bridge handoff rather than calling the worker
/// directly.
/// </summary>
/// <remarks>
/// The bridge and worker are render-thread objects, but the tests call them on the test thread after
/// the backend initialized; that is the same single-threaded pattern the other GPU recovery and
/// readback tests use. Dispose closes the bridge, which destroys every live resource before the
/// backend is disposed.
/// </remarks>
internal sealed class AssetGpuUploadHarness : IDisposable {
    private readonly GpuAssetBridge _bridge = new();

    /// <summary>
    /// Creates the harness for an initialized backend.
    /// </summary>
    /// <param name="backend">The initialized test backend.</param>
    internal AssetGpuUploadHarness(Vulkan12Backend backend) {
        ArgumentNullException.ThrowIfNull(backend);

        Backend = backend;
        Worker = backend.CreateAssetGpuWorker();
        _bridge.AttachWorker(Worker);
    }

    /// <summary>
    /// Gets the backend the worker resolves its device from.
    /// </summary>
    internal Vulkan12Backend Backend { get; }

    /// <summary>
    /// Gets the asset GPU worker attached to the bridge.
    /// </summary>
    internal IAssetGpuWorker Worker { get; }

    /// <summary>
    /// Gets the bridge.
    /// </summary>
    internal GpuAssetBridge Bridge => _bridge;

    /// <summary>
    /// Queues an upload, drains the bridge, and returns the created resource.
    /// </summary>
    /// <param name="upload">The upload to create.</param>
    /// <returns>The created resource.</returns>
    /// <exception cref="AssetLoadException">The upload or the resource creation failed.</exception>
    internal GpuAssetResource Upload(AssetGpuUpload upload) {
        Task<GpuAssetResource> task = _bridge.UploadAsync(upload, CancellationToken.None);
        _bridge.ProcessPending(Worker);
        return task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Queues a destruction, drains the bridge, and waits for the worker to destroy or skip it.
    /// </summary>
    /// <param name="resource">The resource to destroy.</param>
    internal void Destroy(GpuAssetResource resource) {
        _bridge.Destroy(resource);
        _bridge.ProcessPending(Worker);
    }

    /// <summary>
    /// Recreates every live resource on the backend's current device.
    /// </summary>
    internal void Restore() => _bridge.Restore(Worker);

    /// <inheritdoc />
    public void Dispose() => _bridge.Close(null);
}
