using RayBork.Internal;

namespace RayBork.Tests;

public sealed class RenderStatusTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DefaultStatusIsTheIdleSnapshot() {
        RenderStatus status = new();

        EngineInfo info = status.Info;

        Assert.Equal("RayBork", info.Name);
        Assert.Equal(EngineVersion.Current, info.Version);
        Assert.Equal(string.Empty, info.RendererName);
        Assert.Equal(string.Empty, info.GpuName);
        Assert.Equal(0u, info.VulkanApiVersion);
        Assert.Equal(string.Empty, info.SwapchainFormat);
        Assert.False(info.HdrOutput);
        Assert.Equal(0, info.SampleCount);
        Assert.Equal(PresentMode.Mailbox, info.PresentMode);
        Assert.False(info.ValidationEnabled);
        Assert.Equal(default, status.Stats);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SetInfoAndSetStatsRoundTrip() {
        RenderStatus status = new();
        EngineInfo info = CreateEngineInfo();
        RenderStats stats = CreateRenderStats();

        status.SetInfo(info);
        status.SetStats(stats);

        Assert.Equal(info, status.Info);
        Assert.Equal(stats, status.Stats);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WorkerThreadWriteIsVisibleAfterJoin() {
        RenderStatus status = new();
        EngineInfo expectedInfo = CreateEngineInfo();
        RenderStats expectedStats = CreateRenderStats();

        Thread writer = new(() => {
            status.SetInfo(expectedInfo);
            status.SetStats(expectedStats);
        });

        writer.Start();

        Assert.True(writer.Join(AppTestHost.TIMEOUT));
        Assert.Equal(expectedInfo, status.Info);
        Assert.Equal(expectedStats, status.Stats);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EngineContextReadsTheAppStatusHolderOnTheNextTick() {
        EngineInfo expectedInfo = CreateEngineInfo();
        RenderStats expectedStats = CreateRenderStats();
        using ManualResetEventSlim firstTickStarted = new(false);
        using ManualResetEventSlim releaseFirstTick = new(false);

        EngineInfo capturedInfo = default;
        RenderStats capturedStats = default;
        int tickCount = 0;

        using AppTestHost host = new(new ScriptedGame());
        // The render loop owns Stats once frames complete; hold the value under test steady so the
        // tick thread observes the same snapshot the test wrote to the app status holder.
        host.FakeRenderer.Stats = expectedStats;
        host.FakeRenderer.PublishFrameStats = false;
        host.Game.TickHandler = context => {
            tickCount++;
            if (tickCount == 1) {
                firstTickStarted.Set();
                releaseFirstTick.Wait(AppTestHost.TIMEOUT);
                return;
            }

            capturedInfo = context.Info;
            capturedStats = context.Stats;
            context.RequestExit();
        };

        Exception? runFailure = null;
        Thread runThread = new(() => {
            try {
                host.App.Run();
            } catch (Exception exception) {
                runFailure = exception;
            }
        });

        runThread.Start();
        try {
            Assert.True(firstTickStarted.Wait(AppTestHost.TIMEOUT, TestContext.Current.CancellationToken));

            host.App.RenderStatus.SetInfo(expectedInfo);
            host.App.RenderStatus.SetStats(expectedStats);

            releaseFirstTick.Set();
            Assert.True(runThread.Join(AppTestHost.TIMEOUT));
            Assert.Null(runFailure);
        } finally {
            releaseFirstTick.Set();
            runThread.Join(AppTestHost.TIMEOUT);
        }

        Assert.Equal(expectedInfo, capturedInfo);
        Assert.Equal(expectedStats, capturedStats);
    }

    private static EngineInfo CreateEngineInfo() => new(
        "RayBork",
        new Version(0, 1, 0, 0),
        "Vulkan 1.2",
        "Test GPU",
        4202496u,
        "B8G8R8A8_SRGB",
        false,
        4,
        PresentMode.Mailbox,
        true);

    private static RenderStats CreateRenderStats() => new(
        42L,
        7,
        9,
        1200,
        3,
        2,
        5,
        4,
        false,
        1.5,
        2.5,
        2,
        1);
}
