using Microsoft.Extensions.Logging;
using RayBork.Internal;

namespace RayBork.Tests.Fonts.Glyph;

/// <summary>
/// Tests the app-lifetime font thread: the pause gate, the idle hook, start and stop joins, start
/// ordering, the enqueue result, stop from the font thread, concurrent stops, cancellation of pending
/// and in-flight work, and deduplicated concurrent requests.
/// </summary>
public sealed class GlyphGenerationServiceTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TheServiceStartsOneNamedBackgroundThread() {
        using GlyphPipelineTestHarness harness = new();

        Assert.True(harness.Service.IsRunning);
        Thread thread = Assert.IsType<Thread>(harness.Service.GenerationThread);
        Assert.Equal(GlyphGenerationService.THREAD_NAME, thread.Name);
        Assert.True(thread.IsBackground);
        Assert.True(thread.IsAlive);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task PauseHoldsQueuedWorkAndResumeDrainsIt() {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateManyGlyphFace(3, out int firstGlyphId);
        FontGlyphPipeline pipeline = harness.CreatePipeline(face);
        harness.Service.Pause();
        pipeline.BeginFrame(1);
        FrameAssetRetention retention = new();

        Assert.Null(pipeline.Acquire(firstGlyphId, retention));
        Assert.Null(pipeline.Acquire(firstGlyphId + 1, retention));
        Assert.Null(pipeline.Acquire(firstGlyphId + 2, retention));
        Task idle = harness.Service.WhenIdleAsync();
        Assert.False(idle.IsCompleted);
        Assert.Equal(0, harness.Factory.CreateCount);

        harness.Service.Resume();
        await idle.WaitAsync(GlyphPipelineTestHarness.TIMEOUT, TestContext.Current.CancellationToken);

        Assert.Equal(3, harness.Factory.CreateCount);
        Assert.True(pipeline.IsGlyphReady(firstGlyphId));
        Assert.True(pipeline.IsGlyphReady(firstGlyphId + 1));
        Assert.True(pipeline.IsGlyphReady(firstGlyphId + 2));
        retention.Release();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AnIdleServiceCompletesWhenIdleImmediately() {
        using GlyphPipelineTestHarness harness = new();

        Assert.True(harness.Service.WhenIdleAsync().IsCompleted);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ConcurrentRequestsForOneGlyphGenerateItOnce() {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateSquareFace(out int glyphId);
        FontGlyphPipeline pipeline = harness.CreatePipeline(face);
        pipeline.BeginFrame(1);

        Task[] requests = new Task[8];
        for (int index = 0; index < requests.Length; index++) {
            requests[index] = Task.Run(
                () => {
                    FrameAssetRetention retention = new();
                    pipeline.Acquire(glyphId, retention);
                    retention.Release();
                },
                TestContext.Current.CancellationToken);
        }

        await Task.WhenAll(requests)
            .WaitAsync(GlyphPipelineTestHarness.TIMEOUT, TestContext.Current.CancellationToken);
        await harness.Service.WhenIdleAsync()
            .WaitAsync(GlyphPipelineTestHarness.TIMEOUT, TestContext.Current.CancellationToken);

        Assert.Equal(1, harness.Factory.CreateCount);
        Assert.True(pipeline.IsGlyphReady(glyphId));
        Assert.Equal(1, pipeline.CachedGlyphCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task StopJoinsTheThreadAndCancelsPendingWork() {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateManyGlyphFace(3, out int firstGlyphId);
        FontGlyphPipeline pipeline = harness.CreatePipeline(face);
        harness.Service.Pause();
        pipeline.BeginFrame(1);
        FrameAssetRetention retention = new();
        Assert.Null(pipeline.Acquire(firstGlyphId, retention));
        Assert.Null(pipeline.Acquire(firstGlyphId + 1, retention));
        Assert.Null(pipeline.Acquire(firstGlyphId + 2, retention));
        Thread thread = Assert.IsType<Thread>(harness.Service.GenerationThread);
        Assert.True(thread.IsAlive);

        GlyphPipelineTestHarness.StopService(harness.Service);

        Assert.False(thread.IsAlive);
        Assert.False(harness.Service.IsRunning);
        Assert.Equal(0, harness.Factory.CreateCount);
        Assert.True(harness.Service.WhenIdleAsync().IsCompleted);
        Assert.True(pipeline.WhenGlyphSettledAsync(firstGlyphId).IsCompleted);
        Assert.False(pipeline.IsGlyphReady(firstGlyphId));
        Assert.Equal(0, pipeline.CachedGlyphCount);
        Assert.DoesNotContain(
            harness.LoggerFactory.Entries,
            entry => entry.Level == LogLevel.Warning);

        GlyphPipelineTestHarness.StopService(harness.Service);
        Assert.False(harness.Service.IsRunning);
        retention.Release();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ARejectedNotStartedRequestIsLoggedOnceAndStartOrderingIsEnforced() {
        TrackingLoggerFactory loggerFactory = new();
        FakeGlyphTextureFactory factory = new();
        GlyphGenerationService service = new(loggerFactory);
        SfntFace face = GlyphTestFonts.CreateManyGlyphFace(3, out int firstGlyphId);
        FontGlyphPipeline pipeline = new(
            face,
            FontGlyphSettings.CreateDefault(face.UnitsPerEm),
            factory,
            service,
            loggerFactory.CreateLogger(GlyphGenerationService.LOGGER_CATEGORY));
        try {
            pipeline.BeginFrame(1);
            FrameAssetRetention retention = new();

            Assert.Equal(
                GlyphEnqueueResult.RejectedNotStarted,
                service.Enqueue(pipeline, firstGlyphId + 2));
            Assert.Null(pipeline.Acquire(firstGlyphId, retention));
            Assert.Null(pipeline.Acquire(firstGlyphId + 1, retention));
            Assert.False(pipeline.IsGlyphReady(firstGlyphId));
            Assert.False(pipeline.IsGlyphReady(firstGlyphId + 1));
            Assert.True(pipeline.WhenGlyphSettledAsync(firstGlyphId).IsCompleted);
            Assert.True(pipeline.WhenGlyphSettledAsync(firstGlyphId + 1).IsCompleted);
            Assert.Single(NotStartedWarnings(loggerFactory));

            Assert.Null(pipeline.Acquire(firstGlyphId, retention));
            Assert.Single(NotStartedWarnings(loggerFactory));
            Assert.Equal(0, factory.CreateCount);

            service.Start();
            await GlyphPipelineTestHarness.GenerateAsync(pipeline, firstGlyphId + 2, 1);

            Assert.True(pipeline.IsGlyphReady(firstGlyphId + 2));
            Assert.Equal(1, factory.CreateCount);
            retention.Release();
        } finally {
            GlyphPipelineTestHarness.StopService(service);
            pipeline.Dispose();
            loggerFactory.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task EnqueueDistinguishesQueuedAlreadyPendingAndRejected() {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateManyGlyphFace(2, out int firstGlyphId);
        FontGlyphPipeline pipeline = harness.CreatePipeline(face);
        harness.Service.Pause();
        pipeline.BeginFrame(1);

        Assert.Equal(
            GlyphEnqueueResult.Queued,
            harness.Service.Enqueue(pipeline, firstGlyphId));
        Assert.Equal(
            GlyphEnqueueResult.AlreadyPending,
            harness.Service.Enqueue(pipeline, firstGlyphId));
        Assert.Equal(0, harness.Factory.CreateCount);

        harness.Service.Resume();
        await harness.Service.WhenIdleAsync()
            .WaitAsync(GlyphPipelineTestHarness.TIMEOUT, TestContext.Current.CancellationToken);
        Assert.Equal(1, harness.Factory.CreateCount);

        GlyphPipelineTestHarness.StopService(harness.Service);
        Assert.Equal(
            GlyphEnqueueResult.RejectedStopping,
            harness.Service.Enqueue(pipeline, firstGlyphId + 1));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EnqueueAfterPipelineDisposeIsRejected() {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateSquareFace(out int glyphId);
        FontGlyphPipeline pipeline = harness.CreatePipeline(face);
        pipeline.Dispose();

        Assert.Equal(
            GlyphEnqueueResult.RejectedStopping,
            harness.Service.Enqueue(pipeline, glyphId));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task StopCancelsAnInFlightUploadAndCompletesItsWaiter() {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateSquareFace(out int glyphId);
        FontGlyphPipeline pipeline = harness.CreatePipeline(face);
        harness.Factory.Handler = (_, _, _, cancellationToken) => {
            TaskCompletionSource<Texture> upload =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() => upload.TrySetCanceled(cancellationToken));
            return upload.Task;
        };
        pipeline.BeginFrame(1);
        FrameAssetRetention retention = new();
        Assert.Null(pipeline.Acquire(glyphId, retention));
        await harness.Factory.WhenCreateStartedAsync()
            .WaitAsync(GlyphPipelineTestHarness.TIMEOUT, TestContext.Current.CancellationToken);
        Thread thread = Assert.IsType<Thread>(harness.Service.GenerationThread);

        await Task.Run(harness.Service.Stop, TestContext.Current.CancellationToken)
            .WaitAsync(GlyphPipelineTestHarness.TIMEOUT, TestContext.Current.CancellationToken);

        Assert.False(thread.IsAlive);
        Assert.True(pipeline.WhenGlyphSettledAsync(glyphId).IsCompleted);
        Assert.False(pipeline.IsGlyphReady(glyphId));
        Assert.Equal(0, pipeline.CachedGlyphCount);
        Assert.DoesNotContain(
            harness.LoggerFactory.Entries,
            entry => entry.Level == LogLevel.Warning);
        retention.Release();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task SecondConcurrentStopWaitsForTheFirstJoin() {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateSquareFace(out int glyphId);
        FontGlyphPipeline pipeline = harness.CreatePipeline(face);
        TaskCompletionSource<Texture> upload =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource stopObserved =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Factory.Handler = (_, _, _, cancellationToken) => {
            cancellationToken.Register(() => stopObserved.TrySetResult());
            return upload.Task;
        };
        pipeline.BeginFrame(1);
        FrameAssetRetention retention = new();
        Assert.Null(pipeline.Acquire(glyphId, retention));
        await harness.Factory.WhenCreateStartedAsync()
            .WaitAsync(GlyphPipelineTestHarness.TIMEOUT, TestContext.Current.CancellationToken);
        Thread thread = Assert.IsType<Thread>(harness.Service.GenerationThread);

        Task first = Task.Run(harness.Service.Stop, TestContext.Current.CancellationToken);
        await stopObserved.Task.WaitAsync(
            GlyphPipelineTestHarness.TIMEOUT,
            TestContext.Current.CancellationToken);
        Task second = Task.Run(harness.Service.Stop, TestContext.Current.CancellationToken);

        bool entered = SpinWait.SpinUntil(
            () => harness.Service.StopWaiterCount == 1,
            GlyphPipelineTestHarness.TIMEOUT);
        Assert.True(entered, "The second Stop did not enter the wait for the first stop.");
        Assert.False(second.IsCompleted);
        upload.TrySetCanceled(TestContext.Current.CancellationToken);
        await Task.WhenAll(first, second)
            .WaitAsync(GlyphPipelineTestHarness.TIMEOUT, TestContext.Current.CancellationToken);

        Assert.False(thread.IsAlive);
        Assert.False(harness.Service.IsRunning);
        retention.Release();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task EnqueueRacingStopSettlesEveryGlyph() {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateManyGlyphFace(4, out int firstGlyphId);
        FontGlyphPipeline pipeline = harness.CreatePipeline(face);
        int[] glyphIds = [.. Enumerable.Range(0, 4).Select(index => firstGlyphId + index)];
        pipeline.BeginFrame(1);
        using ManualResetEventSlim start = new(false);
        Task[] workers = new Task[4];
        for (int worker = 0; worker < workers.Length; worker++) {
            int workerIndex = worker;
            workers[worker] = Task.Run(
                () => {
                    start.Wait(TestContext.Current.CancellationToken);
                    for (int iteration = 0; iteration < 100; iteration++) {
                        FrameAssetRetention retention = new();
                        pipeline.Acquire(glyphIds[(workerIndex + iteration) % glyphIds.Length], retention);
                        retention.Release();
                    }
                },
                TestContext.Current.CancellationToken);
        }

        Task stopper = Task.Run(
            () => {
                start.Wait(TestContext.Current.CancellationToken);
                harness.Service.Stop();
            },
            TestContext.Current.CancellationToken);
        start.Set();

        await Task.WhenAll([.. workers, stopper])
            .WaitAsync(GlyphPipelineTestHarness.TIMEOUT, TestContext.Current.CancellationToken);

        Assert.False(harness.Service.IsRunning);
        foreach (int glyphId in glyphIds) {
            Assert.True(pipeline.WhenGlyphSettledAsync(glyphId).IsCompleted);
        }

        pipeline.Dispose();
        Assert.Equal(harness.Factory.CreateCount, harness.Factory.Destroyed.Count);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task StopFromTheFontThreadThrowsInsteadOfDeadlocking() {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateSquareFace(out int glyphId);
        FontGlyphPipeline pipeline = harness.CreatePipeline(face);
        Exception? captured = null;
        harness.Factory.Handler = (width, height, _, _) => {
            try {
                harness.Service.Stop();
            } catch (Exception exception) {
                captured = exception;
            }

            return Task.FromResult(harness.Factory.CreateTexture(width, height));
        };

        await GlyphPipelineTestHarness.RequestAndWaitAsync(pipeline, glyphId, 1);

        Assert.IsType<InvalidOperationException>(captured);
        Assert.True(harness.Service.IsRunning);
        Assert.True(pipeline.IsGlyphReady(glyphId));
        Assert.Equal(1, harness.Factory.CreateCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void StartTwiceAndStartAfterStopAreRejected() {
        using GlyphPipelineTestHarness harness = new();

        Assert.Throws<InvalidOperationException>(() => harness.Service.Start());

        GlyphPipelineTestHarness.StopService(harness.Service);

        Assert.Throws<ObjectDisposedException>(() => harness.Service.Start());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EnqueueAfterStopIsRejectedAndFailsTheGlyph() {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateManyGlyphFace(2, out int firstGlyphId);
        FontGlyphPipeline pipeline = harness.CreatePipeline(face);
        pipeline.BeginFrame(1);
        GlyphPipelineTestHarness.StopService(harness.Service);

        Assert.Equal(
            GlyphEnqueueResult.RejectedStopping,
            harness.Service.Enqueue(pipeline, firstGlyphId));
        Assert.Null(GlyphPipelineTestHarness.AcquireOnce(pipeline, firstGlyphId));
        Assert.Null(GlyphPipelineTestHarness.AcquireOnce(pipeline, firstGlyphId + 1));
        Assert.False(pipeline.IsGlyphReady(firstGlyphId));
        Assert.False(pipeline.IsGlyphReady(firstGlyphId + 1));
        Assert.True(pipeline.WhenGlyphSettledAsync(firstGlyphId).IsCompleted);
        Assert.True(pipeline.WhenGlyphSettledAsync(firstGlyphId + 1).IsCompleted);
        Assert.Equal(0, harness.Factory.CreateCount);
        Assert.Single(StoppingWarnings(harness));
        Assert.Empty(NotStartedWarnings(harness));
    }

    private static IReadOnlyList<TrackedLogEntry> NotStartedWarnings(TrackingLoggerFactory factory) => [
        .. factory.Entries.Where(
            entry => entry.Level == LogLevel.Warning
                && entry.Message.Contains("never started", StringComparison.Ordinal)),
    ];

    private static IReadOnlyList<TrackedLogEntry> NotStartedWarnings(GlyphPipelineTestHarness harness) =>
        NotStartedWarnings(harness.LoggerFactory);

    private static IReadOnlyList<TrackedLogEntry> StoppingWarnings(GlyphPipelineTestHarness harness) => [
        .. harness.LoggerFactory.Entries.Where(
            entry => entry.Level == LogLevel.Warning
                && entry.Message.Contains("is stopping", StringComparison.Ordinal)),
    ];
}
