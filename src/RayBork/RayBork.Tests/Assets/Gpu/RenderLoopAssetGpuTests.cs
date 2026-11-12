using Microsoft.Extensions.Logging.Abstractions;
using RayBork.Internal;

namespace RayBork.Tests.Assets.Gpu;

public sealed class RenderLoopAssetGpuTests {
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task RenderLoopRestoresAssetsWhenTheDeviceGenerationChanges() {
        using Harness harness = new();
        harness.PublishFrame();
        harness.WaitForFrameSubmit();

        Task<GpuAssetResource> upload = harness.Bridge.UploadAsync(
            CreateTextureUpload("/textures/test"),
            CancellationToken.None);
        GpuAssetResource resource = await upload.WaitAsync(_timeout, TestContext.Current.CancellationToken);
        Assert.Equal(1, harness.Worker.UploadCallCount);
        Assert.Equal(1, harness.Bridge.LiveResourceCount);
        long uploadedHandle = resource.Handle;

        harness.SimulateDeviceRecreation();
        harness.PublishFrame();
        Assert.True(harness.Worker.Restored.Wait(_timeout, TestContext.Current.CancellationToken));

        Assert.Null(harness.Loop.Failure);
        Assert.Equal(1, harness.Worker.RestoreCallCount);
        Assert.NotEqual(uploadedHandle, resource.Handle);
        Assert.Equal(1, harness.Bridge.LiveResourceCount);

        harness.Stop();

        Assert.Equal(1, harness.Backend.DisposeCallCount);
        GpuAssetResource destroyed = Assert.Single(harness.Worker.DestroyCalls);
        Assert.Same(resource, destroyed);
        Assert.True(destroyed.IsDestroyed);
        Assert.True(harness.Bridge.IsClosed);
        Assert.Equal(0, harness.Bridge.LiveResourceCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task PresentationOnlyRecoveryDoesNotRestoreAssets() {
        using Harness harness = new();
        harness.PublishFrame();
        harness.WaitForFrameSubmit();

        Task<GpuAssetResource> upload = harness.Bridge.UploadAsync(
            CreateTextureUpload("/textures/test"),
            CancellationToken.None);
        await upload.WaitAsync(_timeout, TestContext.Current.CancellationToken);

        harness.SetStatsRecoveryCount(1);
        harness.PublishFrame();
        harness.WaitForSecondPresent();

        Assert.Null(harness.Loop.Failure);
        Assert.Equal(0, harness.Worker.RestoreCallCount);
        Assert.Equal(1, harness.Worker.UploadCallCount);
        Assert.Equal(1, harness.Bridge.LiveResourceCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task FrameIsPresentedWhileAnUploadIsInFlightAndCompletionIsPolledWithoutAFrame() {
        using Harness harness = new();
        harness.PublishFrame();
        harness.WaitForFrameSubmit();

        harness.Worker.DeferUploads = true;
        Task<GpuAssetResource> uploadTask = harness.Bridge.UploadAsync(
            CreateTextureUpload("/textures/test"),
            CancellationToken.None);
        FakeGpuAssetUploadOperation upload = harness.Worker.WaitForUpload(_timeout);

        harness.PublishFrame();
        harness.WaitForSecondPresent();

        Assert.False(uploadTask.IsCompleted);
        Assert.Equal(2, harness.PresentCount);
        Assert.True(harness.Bridge.HasPendingWork);

        // No frame is published after this point. The loop observes the finished copy through the
        // bounded wait it uses while the bridge has pending work.
        upload.Complete();
        GpuAssetResource resource = await uploadTask.WaitAsync(_timeout, TestContext.Current.CancellationToken);

        Assert.Same(upload.Upload, resource.Upload);
        Assert.Equal(1, harness.Worker.UploadCallCount);
        Assert.False(harness.Bridge.HasPendingWork);
        Assert.Null(harness.Loop.Failure);
    }

    private static RendererBackendOptions CreateOptions() => new() {
        Settings = new RenderSettings {
            PresentMode = PresentMode.Mailbox,
            SampleCount = 4,
            Tonemapping = TonemappingMode.AgX,
            Exposure = 0f,
            ValidationEnabled = false,
            ClearColor = Color.White,
        },
        InstanceExtensions = [],
        SurfaceProvider = new StaticSurfaceProvider(),
        WindowStateProvider = () => new WindowSurfaceState(64, 64, false, true, WindowHdrState.Sdr),
        Logger = NullLogger.Instance,
        TeardownQueue = new MainThreadTeardownQueue(),
    };

    private static TextureGpuUpload CreateTextureUpload(string path) => new(
        path,
        new TextureAssetDescriptor {
            Payload = "payloads/ktx2",
            Width = 1,
            Height = 1,
            MipLevels = 1,
            Format = TextureFormat.Rgba8Srgb,
            IsSrgb = true,
            Filter = TextureFilter.Linear,
            Wrap = TextureWrap.Repeat,
            MaxAnisotropy = 1f,
        },
        new byte[] { 1, 2, 3, 4 });

    /// <summary>
    /// One render loop, fake backend, and bridge wired the same way the app wires them, with events
    /// for deterministic frame synchronization.
    /// </summary>
    private sealed class Harness : IDisposable {
        private readonly ManualResetEventSlim _frameSubmitted = new(false);
        private readonly ManualResetEventSlim _secondFramePresented = new(false);

        private int _presentCount;
        private int _statsRecoveryCount;
        private int _stopped;

        internal Harness() {
            Backend = new FakeRendererBackend {
                AssetGpuWorker = Worker,
                PublishFrameStats = false,
            };
            Backend.OnSubmit = _ => {
                Backend.Stats = new RenderStats(
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    4,
                    false,
                    0,
                    0,
                    0,
                    Volatile.Read(ref _statsRecoveryCount));
                _frameSubmitted.Set();
            };
            Backend.OnPresent = () => {
                if (Interlocked.Increment(ref _presentCount) >= 2) {
                    _secondFramePresented.Set();
                }
            };

            Loop = new RenderLoop(Ring, () => Backend, CreateOptions(), new RenderStatus(), Bridge);
            Loop.Start();
        }

        internal FakeRendererBackend Backend { get; }

        internal FakeAssetGpuWorker Worker { get; } = new();

        internal GpuAssetBridge Bridge { get; } = new();

        internal FrameRing Ring { get; } = new();

        internal RenderLoop Loop { get; }

        internal int PresentCount => Volatile.Read(ref _presentCount);

        internal void PublishFrame() {
            Assert.True(Ring.TryAcquireWritable(out FrameLease lease));
            Ring.Publish(lease);
        }

        internal void WaitForFrameSubmit() => Assert.True(
            _frameSubmitted.Wait(_timeout, TestContext.Current.CancellationToken));

        internal void WaitForSecondPresent() => Assert.True(
            _secondFramePresented.Wait(_timeout, TestContext.Current.CancellationToken));

        internal void SetStatsRecoveryCount(int value) => Volatile.Write(ref _statsRecoveryCount, value);

        internal void SimulateDeviceRecreation() => Backend.SimulateDeviceRecreationForTests();

        internal void Stop() {
            if (Interlocked.Exchange(ref _stopped, 1) != 0) {
                return;
            }

            Ring.Complete();
            Assert.True(Loop.Thread.Join(_timeout));
            Assert.Null(Loop.Failure);
        }

        public void Dispose() {
            Stop();
            _frameSubmitted.Dispose();
            _secondFramePresented.Dispose();
        }
    }

    private sealed class StaticSurfaceProvider : IRendererSurfaceProvider {
        public IntPtr CreateSurface(IntPtr instance) => new(0x5EED);

        public void DestroySurface(IntPtr instance, IntPtr surface) {
        }
    }
}
