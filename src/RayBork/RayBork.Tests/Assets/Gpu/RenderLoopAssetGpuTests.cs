using Microsoft.Extensions.Logging.Abstractions;
using RayBork.Internal;

namespace RayBork.Tests.Assets.Gpu;

public sealed class RenderLoopAssetGpuTests {
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task RenderLoopServicesUploadsRestoresAfterRecoveryAndClosesTheBridge() {
        RendererBackendOptions options = CreateOptions();
        FakeRendererBackend backend = new();
        FakeAssetGpuWorker worker = new();
        backend.AssetGpuWorker = worker;
        GpuAssetBridge bridge = new();
        FrameRing ring = new();
        RenderStatus status = new();
        RenderLoop loop = new(ring, () => backend, options, status, bridge);
        ManualResetEventSlim frameSubmitted = new(false);
        int recoveryCount = 0;
        backend.PublishFrameStats = false;
        backend.OnSubmit = _ => {
            backend.Stats = new RenderStats(
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
                Volatile.Read(ref recoveryCount));
            frameSubmitted.Set();
        };

        loop.Start();
        GpuAssetResource? resource = null;
        try {
            Assert.True(ring.TryAcquireWritable(out FrameLease first));
            ring.Publish(first);
            Assert.True(frameSubmitted.Wait(_timeout, TestContext.Current.CancellationToken));

            Task<GpuAssetResource> upload = bridge.UploadAsync(CreateTextureUpload("/textures/test"), CancellationToken.None);
            resource = await upload.WaitAsync(_timeout, TestContext.Current.CancellationToken);
            Assert.Equal(1, worker.UploadCallCount);
            Assert.Equal(1, bridge.LiveResourceCount);
            long uploadedHandle = resource.Handle;

            Volatile.Write(ref recoveryCount, 1);
            Assert.True(ring.TryAcquireWritable(out FrameLease second));
            ring.Publish(second);
            Assert.True(worker.Restored.Wait(_timeout, TestContext.Current.CancellationToken));

            Assert.Null(loop.Failure);
            Assert.Equal(1, worker.RestoreCallCount);
            Assert.NotEqual(uploadedHandle, resource.Handle);
            Assert.Equal(1, bridge.LiveResourceCount);
        } finally {
            ring.Complete();
            Assert.True(loop.Thread.Join(_timeout));
        }

        Assert.Null(loop.Failure);
        Assert.Equal(1, backend.DisposeCallCount);
        GpuAssetResource destroyed = Assert.Single(worker.DestroyCalls);
        Assert.Same(resource, destroyed);
        Assert.True(destroyed.IsDestroyed);
        Assert.True(bridge.IsClosed);
        Assert.Equal(0, bridge.LiveResourceCount);
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

    private sealed class StaticSurfaceProvider : IRendererSurfaceProvider {
        public IntPtr CreateSurface(IntPtr instance) => new(0x5EED);

        public void DestroySurface(IntPtr instance, IntPtr surface) {
        }
    }
}
