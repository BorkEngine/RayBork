using RayBork.Internal;
using RayBork.Tests.Assets.Gpu;

namespace RayBork.Tests.Assets.Procedural;

/// <summary>
/// Wires a <see cref="ProceduralAssetService"/> to a <see cref="GpuAssetBridge"/> and the fake GPU
/// worker, with a pump thread standing in for the render loop.
/// </summary>
/// <remarks>
/// The pump waits on the bridge signal and the worker's progress signal instead of polling, so a
/// deferred upload test never depends on scheduling. <see cref="Dispose"/> stops the service before
/// the pump so a failed build cannot start new work, then closes the bridge and joins the thread.
/// </remarks>
internal sealed class ProceduralTestHarness : IDisposable {
    private readonly ManualResetEventSlim _pumpStop = new(false);
    private readonly Thread _pumpThread;

    internal ProceduralTestHarness() {
        Bridge = new GpuAssetBridge();
        Worker = new FakeAssetGpuWorker();
        Bridge.AttachWorker(Worker);
        Service = new ProceduralAssetService(Bridge);
        _pumpThread = new Thread(PumpLoop) {
            IsBackground = true,
            Name = "RayBork.Tests.ProceduralGpuPump",
        };
        _pumpThread.Start();
    }

    /// <summary>Gets the bridge the service queues uploads on.</summary>
    internal GpuAssetBridge Bridge { get; }

    /// <summary>Gets the recording fake worker attached to the bridge.</summary>
    internal FakeAssetGpuWorker Worker { get; }

    /// <summary>Gets the service under test.</summary>
    internal ProceduralAssetService Service { get; }

    /// <inheritdoc />
    public void Dispose() {
        Service.Stop();
        _pumpStop.Set();
        _pumpThread.Join();
        Bridge.Close(null);
        _pumpStop.Dispose();
    }

    private void PumpLoop() {
        WaitHandle[] handles = [Bridge.Signal, _pumpStop.WaitHandle, Worker.Progress];
        while (true) {
            int index = WaitHandle.WaitAny(handles);
            if (index == 1) {
                return;
            }

            Bridge.ProcessPending(Worker);
        }
    }
}
