using Microsoft.Extensions.Logging;
using RayBork.Internal;

namespace RayBork.Tests.Fonts.Glyph;

/// <summary>
/// Tests the app-lifetime font thread: the pause gate, the idle hook, start and stop joins, stop from
/// the font thread, cancellation of pending and in-flight work, and deduplicated concurrent requests.
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
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task PauseHoldsQueuedWorkAndResumeDrainsIt() {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateManyGlyphFace(3, out int firstGlyphId);
        FontGlyphPipeline pipeline = harness.CreatePipeline(face);
        harness.Service.Pause();

        Assert.Null(pipeline.Acquire(firstGlyphId, 1));
        Assert.Null(pipeline.Acquire(firstGlyphId + 1, 1));
        Assert.Null(pipeline.Acquire(firstGlyphId + 2, 1));
        Task idle = harness.Service.WhenIdleAsync();
        Assert.False(idle.IsCompleted);
        Assert.Equal(0, harness.Factory.CreateCount);

        harness.Service.Resume();
        await idle.WaitAsync(GlyphPipelineTestHarness.TIMEOUT, TestContext.Current.CancellationToken);

        Assert.Equal(3, harness.Factory.CreateCount);
        Assert.True(pipeline.IsGlyphReady(firstGlyphId));
        Assert.True(pipeline.IsGlyphReady(firstGlyphId + 1));
        Assert.True(pipeline.IsGlyphReady(firstGlyphId + 2));
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

        Task[] requests = new Task[8];
        for (int index = 0; index < requests.Length; index++) {
            requests[index] = Task.Run(
                () => pipeline.Acquire(glyphId, 1),
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
        Assert.Null(pipeline.Acquire(firstGlyphId, 1));
        Assert.Null(pipeline.Acquire(firstGlyphId + 1, 1));
        Assert.Null(pipeline.Acquire(firstGlyphId + 2, 1));
        Thread thread = Assert.IsType<Thread>(harness.Service.GenerationThread);
        Assert.True(thread.IsAlive);

        harness.Service.Stop();

        Assert.False(thread.IsAlive);
        Assert.False(harness.Service.IsRunning);
        Assert.Equal(0, harness.Factory.CreateCount);
        Assert.True(harness.Service.WhenIdleAsync().IsCompleted);
        Assert.True(pipeline.WhenGlyphReadyAsync(firstGlyphId).IsCompleted);
        Assert.False(pipeline.IsGlyphReady(firstGlyphId));
        Assert.Equal(0, pipeline.CachedGlyphCount);

        harness.Service.Stop();
        Assert.False(harness.Service.IsRunning);
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

        Assert.Null(pipeline.Acquire(glyphId, 1));
        await harness.Factory.WhenCreateStartedAsync()
            .WaitAsync(GlyphPipelineTestHarness.TIMEOUT, TestContext.Current.CancellationToken);
        Thread thread = Assert.IsType<Thread>(harness.Service.GenerationThread);

        harness.Service.Stop();

        Assert.False(thread.IsAlive);
        Assert.True(pipeline.WhenGlyphReadyAsync(glyphId).IsCompleted);
        Assert.False(pipeline.IsGlyphReady(glyphId));
        Assert.Equal(0, pipeline.CachedGlyphCount);
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

        harness.Service.Stop();

        Assert.Throws<ObjectDisposedException>(() => harness.Service.Start());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EnqueueAfterStopIsRejectedAndFailsTheGlyph() {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateSquareFace(out int glyphId);
        FontGlyphPipeline pipeline = harness.CreatePipeline(face);
        harness.Service.Stop();

        Assert.False(harness.Service.Enqueue(pipeline, glyphId));
        Assert.Null(pipeline.Acquire(glyphId, 1));
        Assert.False(pipeline.IsGlyphReady(glyphId));
        Assert.True(pipeline.WhenGlyphReadyAsync(glyphId).IsCompleted);
        Assert.Equal(0, harness.Factory.CreateCount);
    }
}
