using RayBork.Internal;

namespace RayBork.Tests.Graphics.Assets;

/// <summary>
/// Wires the real <see cref="VulkanAssetWorker"/> into a <see cref="GpuAssetBridge"/> the same way
/// the render loop does, so GPU tests exercise the bridge handoff.
/// </summary>
/// <remarks>
/// Tests call the render-thread objects on the test thread, like the other GPU tests. Dispose
/// closes the bridge, cancelling a pending upload and destroying live resources.
/// </remarks>
internal sealed class AssetGpuUploadHarness : IDisposable
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);

    private readonly GpuAssetBridge _bridge = new();

    /// <summary>Creates the harness for an initialized backend.</summary>
    internal AssetGpuUploadHarness(Vulkan12Backend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);

        Backend = backend;
        Worker = Assert.IsType<VulkanAssetWorker>(backend.CreateAssetGpuWorker());
        _bridge.AttachWorker(Worker);
    }

    /// <summary>Gets the backend the worker resolves its device from.</summary>
    internal Vulkan12Backend Backend { get; }

    /// <summary>Gets the asset GPU worker attached to the bridge.</summary>
    internal VulkanAssetWorker Worker { get; }

    /// <summary>Gets the bridge.</summary>
    internal GpuAssetBridge Bridge => _bridge;

    /// <summary>Gets the timeout the harness uses while waiting for a GPU upload.</summary>
    internal static TimeSpan Timeout => _timeout;

    /// <summary>Queues an upload and pumps the bridge like the render loop does.</summary>
    /// <exception cref="AssetLoadException">The upload or the resource creation failed.</exception>
    internal GpuAssetResource Upload(AssetGpuUpload upload)
    {
        Task<GpuAssetResource> task = _bridge.UploadAsync(upload, CancellationToken.None);
        bool completed = SpinWait.SpinUntil(
            () =>
            {
                _bridge.ProcessPending(Worker);
                return task.IsCompleted;
            },
            _timeout
        );
        Assert.True(completed, "The GPU upload did not complete.");
        return task.GetAwaiter().GetResult();
    }

    /// <summary>Queues a destruction and drains the bridge.</summary>
    internal void Destroy(GpuAssetResource resource)
    {
        _bridge.Destroy(resource);
        _bridge.ProcessPending(Worker);
    }

    /// <summary>Processes the bridge once, as the render loop does on every turn.</summary>
    internal void Pump() => _bridge.ProcessPending(Worker);

    /// <summary>Pumps the bridge until the task completes and returns its result.</summary>
    /// <exception cref="AssetLoadException">The upload or the build failed.</exception>
    internal T WaitFor<T>(Task<T> task)
    {
        ArgumentNullException.ThrowIfNull(task);

        bool completed = SpinWait.SpinUntil(
            () =>
            {
                _bridge.ProcessPending(Worker);
                return task.IsCompleted;
            },
            _timeout
        );
        Assert.True(completed, "The GPU upload did not complete.");
        return task.GetAwaiter().GetResult();
    }

    /// <summary>Recreates every live resource on the backend's current device.</summary>
    internal void Restore() => _bridge.Restore(Worker);

    /// <inheritdoc />
    public void Dispose() => _bridge.Close(null);
}
