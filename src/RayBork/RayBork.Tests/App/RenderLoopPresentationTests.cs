using Microsoft.Extensions.Logging.Abstractions;
using RayBork.Internal;

namespace RayBork.Tests;

/// <summary>
/// Unit tests for the render loop's presentation-completion mark: every render attempt, including a
/// dropped or failed frame, must release a FIFO waiter even when the frame was not presented.
/// </summary>
public sealed class RenderLoopPresentationTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task AFailedRenderAttemptMarksTheFrameFinished(bool failDuringPresent)
    {
        using Harness harness = new();
        if (failDuringPresent)
        {
            harness.Backend.PresentException = new InvalidOperationException("present failed");
        }
        else
        {
            harness.Backend.SubmitException = new InvalidOperationException("submit failed");
        }

        FrameLease lease = harness.PublishFrame();
        Assert.True(SpinWait.SpinUntil(() => harness.Loop.Failure is not null, _timeout));

        Task<bool> finished = Task.Run(() =>
            harness.Ring.WaitForPresentation(lease.FrameIndex, CancellationToken.None)
        );
        Assert.True(await finished.WaitAsync(_timeout, TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ADroppedFrameMarksTheFrameFinished()
    {
        using Harness harness = new();
        harness.Backend.SkipSubmitForTests = true;

        FrameLease lease = harness.PublishFrame();

        Task<bool> finished = Task.Run(() =>
            harness.Ring.WaitForPresentation(lease.FrameIndex, CancellationToken.None)
        );
        Assert.True(await finished.WaitAsync(_timeout, TestContext.Current.CancellationToken));
        Assert.Equal(0, harness.Ring.PresentationWaiterCount);
    }

    private sealed class Harness : IDisposable
    {
        private readonly GpuAssetBridge _bridge = new();
        private readonly RenderTextureService _renderTextures = new();

        internal Harness()
        {
            Loop = new RenderLoop(
                Ring,
                () => Backend,
                CreateOptions(),
                new RenderStatus(),
                _bridge,
                _renderTextures
            );
            Loop.Start();
        }

        internal FrameRing Ring { get; } = new();

        internal FakeRendererBackend Backend { get; } = new();

        internal RenderLoop Loop { get; }

        internal FrameLease PublishFrame()
        {
            Assert.True(Ring.TryAcquireWritable(out FrameLease lease));
            Ring.Publish(lease);
            return lease;
        }

        public void Dispose()
        {
            Ring.Complete();
            Assert.True(Loop.Thread.Join(_timeout));
            _renderTextures.Dispose();
        }
    }

    private sealed class StaticSurfaceProvider : IRendererSurfaceProvider
    {
        public IntPtr CreateSurface(IntPtr instance) => new(0x5EED);

        public void DestroySurface(IntPtr instance, IntPtr surface) { }
    }

    private static RendererBackendOptions CreateOptions() =>
        new()
        {
            Settings = new RenderSettings
            {
                PresentMode = PresentMode.Fifo,
                SampleCount = 4,
                Tonemapping = TonemappingMode.AgX,
                Exposure = 0f,
                ValidationEnabled = false,
            },
            InstanceExtensions = [],
            SurfaceProvider = new StaticSurfaceProvider(),
            WindowStateProvider = () =>
                new WindowSurfaceState(64, 64, false, true, WindowHdrState.Sdr),
            Logger = NullLogger.Instance,
            TeardownQueue = new MainThreadTeardownQueue(),
        };
}
