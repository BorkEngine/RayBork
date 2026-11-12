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
/// readback tests use. <see cref="Upload"/> stands in for the render loop and pumps the bridge until
/// the deferred upload finished; tests that exercise a single poll call the worker's
/// <c>BeginUpload</c> and <c>TryComplete</c> directly. Dispose closes the bridge, which cancels any
/// pending upload and destroys every live resource before the backend is disposed.
/// </remarks>
internal sealed class AssetGpuUploadHarness : IDisposable {
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);

    private readonly GpuAssetBridge _bridge = new();

    /// <summary>
    /// Creates the harness for an initialized backend.
    /// </summary>
    /// <param name="backend">The initialized test backend.</param>
    internal AssetGpuUploadHarness(Vulkan12Backend backend) {
        ArgumentNullException.ThrowIfNull(backend);

        Backend = backend;
        Worker = Assert.IsType<VulkanAssetWorker>(backend.CreateAssetGpuWorker());
        _bridge.AttachWorker(Worker);
    }

    /// <summary>
    /// Gets the backend the worker resolves its device from.
    /// </summary>
    internal Vulkan12Backend Backend { get; }

    /// <summary>
    /// Gets the asset GPU worker attached to the bridge.
    /// </summary>
    internal VulkanAssetWorker Worker { get; }

    /// <summary>
    /// Gets the bridge.
    /// </summary>
    internal GpuAssetBridge Bridge => _bridge;

    /// <summary>
    /// Gets the timeout the harness uses while waiting for a GPU upload.
    /// </summary>
    internal static TimeSpan Timeout => _timeout;

    /// <summary>
    /// Queues an upload, pumps the bridge like the render loop, and returns the created resource.
    /// </summary>
    /// <param name="upload">The upload to create.</param>
    /// <returns>The created resource.</returns>
    /// <exception cref="AssetLoadException">The upload or the resource creation failed.</exception>
    internal GpuAssetResource Upload(AssetGpuUpload upload) {
        Task<GpuAssetResource> task = _bridge.UploadAsync(upload, CancellationToken.None);
        bool completed = SpinWait.SpinUntil(
            () => {
                _bridge.ProcessPending(Worker);
                return task.IsCompleted;
            },
            _timeout);
        Assert.True(completed, "The GPU upload did not complete.");
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
    /// Processes the bridge once, as the render loop does on every loop turn. Builder tests that start
    /// a procedural build poll the bridge with this method.
    /// </summary>
    internal void Pump() => _bridge.ProcessPending(Worker);

    /// <summary>
    /// Pumps the bridge until <paramref name="task"/> completes and returns its result.
    /// </summary>
    /// <typeparam name="T">The task result type.</typeparam>
    /// <param name="task">The pending build or upload task.</param>
    /// <returns>The completed result.</returns>
    /// <exception cref="AssetLoadException">The upload or the build failed.</exception>
    internal T WaitFor<T>(Task<T> task) {
        ArgumentNullException.ThrowIfNull(task);

        bool completed = SpinWait.SpinUntil(
            () => {
                _bridge.ProcessPending(Worker);
                return task.IsCompleted;
            },
            _timeout);
        Assert.True(completed, "The GPU upload did not complete.");
        return task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Recreates every live resource on the backend's current device.
    /// </summary>
    internal void Restore() => _bridge.Restore(Worker);

    /// <inheritdoc />
    public void Dispose() => _bridge.Close(null);
}
