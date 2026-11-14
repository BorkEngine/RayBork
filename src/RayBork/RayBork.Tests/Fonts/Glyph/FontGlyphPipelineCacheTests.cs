using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using RayBork.Internal;

namespace RayBork.Tests.Fonts.Glyph;

/// <summary>
/// Tests the hard-capped glyph texture cache: misses, hits and frame holds, empty glyphs, LRU
/// eviction, the production budget, failure without retry, concurrent use, and the exact generator
/// bitmap.
/// </summary>
public sealed class FontGlyphPipelineCacheTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task AMissEnqueuesOnceAndAHitAddsOneFrameHold() {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateSquareFace(out int glyphId);
        FontGlyphPipeline pipeline = harness.CreatePipeline(face);

        harness.Service.Pause();
        pipeline.BeginFrame(1);
        FrameAssetRetention probe = new();
        Assert.Null(pipeline.Acquire(glyphId, probe));
        Assert.Null(pipeline.Acquire(glyphId, probe));
        Assert.Equal(0, harness.Factory.CreateCount);
        Assert.False(pipeline.IsGlyphReady(glyphId));
        Assert.False(harness.Service.WhenIdleAsync().IsCompleted);

        harness.Service.Resume();
        await pipeline.WhenGlyphSettledAsync(glyphId)
            .WaitAsync(GlyphPipelineTestHarness.TIMEOUT, TestContext.Current.CancellationToken);

        FrameAssetRetention recording = new();
        Texture? texture = pipeline.Acquire(glyphId, recording);
        Assert.NotNull(texture);
        Assert.Equal(1, recording.Count);
        Assert.Same(texture, pipeline.Acquire(glyphId, recording));
        Assert.Equal(1, recording.Count);
        Assert.Equal(1, harness.Factory.CreateCount);
        Assert.Equal(1, pipeline.CachedGlyphCount);

        recording.Release();
        probe.Release();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task AnEmptyGlyphBecomesReadyWithoutATexture() {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateEmptyGlyphFace(out int glyphId);
        FontGlyphPipeline pipeline = harness.CreatePipeline(face);

        await GlyphPipelineTestHarness.RequestAndWaitAsync(pipeline, glyphId, 1);

        Assert.True(pipeline.IsGlyphReady(glyphId));
        Assert.Equal(0, pipeline.CachedGlyphCount);
        Assert.Equal(0, harness.Factory.CreateCount);
        Assert.Null(GlyphPipelineTestHarness.AcquireOnce(pipeline, glyphId));
        Assert.False(pipeline.TryGetTexture(glyphId, out _));
        Assert.True(pipeline.WhenGlyphSettledAsync(glyphId).IsCompleted);
        TextGlyphMetrics metrics = pipeline.GetMetrics(glyphId);
        Assert.Equal(0.25f, metrics.AdvanceX);
        Assert.Equal(0f, metrics.Width);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task EvictionRemovesTheLeastRecentlyUsedTexture() {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateManyGlyphFace(4, out int firstGlyphId);
        FontGlyphPipeline pipeline = harness.CreatePipeline(
            face,
            new FontGlyphSettings(face.UnitsPerEm, 8, 4, 2),
            textureBudget: 2);

        Texture first = await GlyphPipelineTestHarness.GenerateAsync(pipeline, firstGlyphId, 1);
        Texture second = await GlyphPipelineTestHarness.GenerateAsync(pipeline, firstGlyphId + 1, 2);
        Assert.Equal(2, pipeline.CachedGlyphCount);
        Assert.False(first.IsDisposed);
        Assert.False(second.IsDisposed);

        Texture third = await GlyphPipelineTestHarness.GenerateAsync(pipeline, firstGlyphId + 2, 3);

        Assert.Equal(2, pipeline.CachedGlyphCount);
        Assert.False(pipeline.IsGlyphReady(firstGlyphId));
        Assert.True(pipeline.IsGlyphReady(firstGlyphId + 1));
        Assert.True(pipeline.IsGlyphReady(firstGlyphId + 2));
        Assert.True(first.IsDisposed);
        Assert.False(second.IsDisposed);
        Assert.False(third.IsDisposed);
        Assert.Single(
            harness.Factory.Destroyed,
            resource => ReferenceEquals(resource, first.Lifetime.Resource));

        Texture fourth = await GlyphPipelineTestHarness.GenerateAsync(pipeline, firstGlyphId + 3, 4);

        Assert.Equal(2, pipeline.CachedGlyphCount);
        Assert.False(pipeline.IsGlyphReady(firstGlyphId + 1));
        Assert.True(pipeline.IsGlyphReady(firstGlyphId + 2));
        Assert.True(pipeline.IsGlyphReady(firstGlyphId + 3));
        Assert.True(second.IsDisposed);
        Assert.False(third.IsDisposed);
        Assert.False(fourth.IsDisposed);
        Assert.Single(
            harness.Factory.Destroyed,
            resource => ReferenceEquals(resource, second.Lifetime.Resource));
        Assert.Equal(2, harness.Factory.Destroyed.Count);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task AHitRefreshesTheLruOrder() {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateManyGlyphFace(3, out int firstGlyphId);
        FontGlyphPipeline pipeline = harness.CreatePipeline(
            face,
            new FontGlyphSettings(face.UnitsPerEm, 8, 4, 2),
            textureBudget: 2);
        await GlyphPipelineTestHarness.GenerateAsync(pipeline, firstGlyphId, 1);
        await GlyphPipelineTestHarness.GenerateAsync(pipeline, firstGlyphId + 1, 1);

        pipeline.BeginFrame(2);
        FrameAssetRetention recording = new();
        Assert.NotNull(pipeline.Acquire(firstGlyphId, recording));
        recording.Release();

        await GlyphPipelineTestHarness.GenerateAsync(pipeline, firstGlyphId + 2, 2);

        Assert.True(pipeline.IsGlyphReady(firstGlyphId));
        Assert.False(pipeline.IsGlyphReady(firstGlyphId + 1));
        Assert.True(pipeline.IsGlyphReady(firstGlyphId + 2));
        Assert.Equal(2, pipeline.CachedGlyphCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task TheCacheNeverExceedsTheProductionTextureBudget() {
        Assert.Equal(512, FontGlyphPipeline.MAX_CACHED_TEXTURES);

        using GlyphPipelineTestHarness harness = new();
        int budget = FontGlyphPipeline.MAX_CACHED_TEXTURES;
        SfntFace face = GlyphTestFonts.CreateManyGlyphFace(budget + 1, out int firstGlyphId);
        FontGlyphPipeline pipeline = harness.CreatePipeline(
            face,
            new FontGlyphSettings(face.UnitsPerEm, 8, 4, 2));

        for (int index = 0; index < budget + 1; index++) {
            await GlyphPipelineTestHarness.GenerateAsync(pipeline, firstGlyphId + index, index + 1);
            Assert.True(
                pipeline.CachedGlyphCount <= budget,
                $"The cache held {pipeline.CachedGlyphCount} textures after the first pass.");
        }

        for (int index = 0; index < budget + 1; index++) {
            await GlyphPipelineTestHarness.GenerateAsync(pipeline, firstGlyphId + index, 1000 + index);
            Assert.True(
                pipeline.CachedGlyphCount <= budget,
                $"The cache held {pipeline.CachedGlyphCount} textures after the second pass.");
        }

        Assert.Equal(budget, pipeline.CachedGlyphCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task TheFrameHoldKeepsAnEvictedTextureAlive() {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateManyGlyphFace(2, out int firstGlyphId);
        FontGlyphPipeline pipeline = harness.CreatePipeline(
            face,
            new FontGlyphSettings(face.UnitsPerEm, 8, 4, 2),
            textureBudget: 1);
        Texture first = await GlyphPipelineTestHarness.GenerateAsync(pipeline, firstGlyphId, 1);

        pipeline.BeginFrame(2);
        FrameAssetRetention recording = new();
        Assert.Same(first, pipeline.Acquire(firstGlyphId, recording));
        Assert.Equal(1, recording.Count);
        Assert.Same(first, pipeline.Acquire(firstGlyphId, recording));
        Assert.Equal(1, recording.Count);

        Texture second = await GlyphPipelineTestHarness.GenerateAsync(pipeline, firstGlyphId + 1, 2);

        Assert.Equal(1, pipeline.CachedGlyphCount);
        Assert.True(first.IsDisposed);
        Assert.False(second.IsDisposed);
        Assert.DoesNotContain(
            harness.Factory.Destroyed,
            resource => ReferenceEquals(resource, first.Lifetime.Resource));

        recording.Release();

        Assert.Single(
            harness.Factory.Destroyed,
            resource => ReferenceEquals(resource, first.Lifetime.Resource));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task MixedFrameBatchesKeepTheCacheAtTheBudget() {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateManyGlyphFace(4, out int firstGlyphId);
        FontGlyphPipeline pipeline = harness.CreatePipeline(
            face,
            new FontGlyphSettings(face.UnitsPerEm, 8, 4, 2),
            textureBudget: 2);
        Texture first = await GlyphPipelineTestHarness.GenerateAsync(pipeline, firstGlyphId, 5);
        Texture second = await GlyphPipelineTestHarness.GenerateAsync(pipeline, firstGlyphId + 1, 5);
        pipeline.BeginFrame(6);
        FrameAssetRetention recording = new();
        Assert.Same(first, pipeline.Acquire(firstGlyphId, recording));
        Assert.Same(second, pipeline.Acquire(firstGlyphId + 1, recording));

        await GlyphPipelineTestHarness.GenerateAsync(pipeline, firstGlyphId + 2, 6);
        await GlyphPipelineTestHarness.GenerateAsync(pipeline, firstGlyphId + 3, 6);

        Assert.Equal(2, pipeline.CachedGlyphCount);
        Assert.True(first.IsDisposed);
        Assert.True(second.IsDisposed);
        Assert.DoesNotContain(
            harness.Factory.Destroyed,
            resource => ReferenceEquals(resource, first.Lifetime.Resource));
        Assert.DoesNotContain(
            harness.Factory.Destroyed,
            resource => ReferenceEquals(resource, second.Lifetime.Resource));

        recording.Release();

        Assert.Single(
            harness.Factory.Destroyed,
            resource => ReferenceEquals(resource, first.Lifetime.Resource));
        Assert.Single(
            harness.Factory.Destroyed,
            resource => ReferenceEquals(resource, second.Lifetime.Resource));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task AnUploadFailureMarksTheGlyphFailedWithoutRetry() {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateSquareFace(out int glyphId);
        FontGlyphPipeline pipeline = harness.CreatePipeline(face);
        harness.Factory.UploadException = new InvalidOperationException("the upload failed");

        await GlyphPipelineTestHarness.RequestAndWaitAsync(pipeline, glyphId, 1);

        Assert.False(pipeline.IsGlyphReady(glyphId));
        Assert.False(pipeline.TryGetTexture(glyphId, out _));
        Assert.Null(GlyphPipelineTestHarness.AcquireOnce(pipeline, glyphId));
        await harness.Service.WhenIdleAsync()
            .WaitAsync(GlyphPipelineTestHarness.TIMEOUT, TestContext.Current.CancellationToken);
        Assert.Equal(1, harness.Factory.CreateCount);
        Assert.Single(GenerationWarnings(harness));

        Assert.Null(GlyphPipelineTestHarness.AcquireOnce(pipeline, glyphId));
        await harness.Service.WhenIdleAsync()
            .WaitAsync(GlyphPipelineTestHarness.TIMEOUT, TestContext.Current.CancellationToken);
        Assert.Equal(1, harness.Factory.CreateCount);
        Assert.Single(GenerationWarnings(harness));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task AGenerationFailureMarksTheGlyphFailedWithoutRetry() {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateUnsupportedCompositeFace(out int glyphId);
        FontGlyphPipeline pipeline = harness.CreatePipeline(face);

        await GlyphPipelineTestHarness.RequestAndWaitAsync(pipeline, glyphId, 1);

        Assert.False(pipeline.IsGlyphReady(glyphId));
        Assert.Equal(0, harness.Factory.CreateCount);
        Assert.Single(GenerationWarnings(harness));
        Assert.Null(GlyphPipelineTestHarness.AcquireOnce(pipeline, glyphId));
        await harness.Service.WhenIdleAsync()
            .WaitAsync(GlyphPipelineTestHarness.TIMEOUT, TestContext.Current.CancellationToken);
        Assert.Equal(0, harness.Factory.CreateCount);
        Assert.Single(GenerationWarnings(harness));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task AMeasuredFailureIsLoggedOnceAcrossMetricsAndGeneration() {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateUnsupportedCompositeFace(out int glyphId);
        FontGlyphPipeline pipeline = harness.CreatePipeline(face);

        pipeline.GetMetrics(glyphId);
        await GlyphPipelineTestHarness.RequestAndWaitAsync(pipeline, glyphId, 1);

        Assert.Single(MetricWarnings(harness));
        Assert.Empty(GenerationWarnings(harness));
        Assert.Single(harness.LoggerFactory.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task AcquireDoesNotBlockWhileTheServiceIsPaused() {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateSquareFace(out int glyphId);
        FontGlyphPipeline pipeline = harness.CreatePipeline(face);
        harness.Service.Pause();
        pipeline.BeginFrame(1);
        FrameAssetRetention retention = new();

        Texture? missed = await Task.Run(
                () => pipeline.Acquire(glyphId, retention),
                TestContext.Current.CancellationToken)
            .WaitAsync(GlyphPipelineTestHarness.TIMEOUT, TestContext.Current.CancellationToken);

        Assert.Null(missed);
        Assert.Equal(0, harness.Factory.CreateCount);
        Assert.False(pipeline.IsGlyphReady(glyphId));

        harness.Service.Resume();
        Texture texture = await GlyphPipelineTestHarness.GenerateAsync(pipeline, glyphId, 1);

        Assert.Equal(0.6f, pipeline.GetMetrics(glyphId).AdvanceX);
        Assert.Equal(32, texture.Width);
        Assert.Equal(1, harness.Factory.CreateCount);
        retention.Release();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task AcquireAndGetMetricsDoNotBlockWhileTheFontThreadIsUploading() {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateSquareFace(out int glyphId);
        FontGlyphPipeline pipeline = harness.CreatePipeline(face);
        TaskCompletionSource<Texture> upload =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Factory.Handler = (_, _, _, _) => upload.Task;
        pipeline.BeginFrame(1);
        FrameAssetRetention retention = new();
        Assert.Null(pipeline.Acquire(glyphId, retention));
        await harness.Factory.WhenCreateStartedAsync()
            .WaitAsync(GlyphPipelineTestHarness.TIMEOUT, TestContext.Current.CancellationToken);

        Texture? repeated = await Task.Run(
                () => pipeline.Acquire(glyphId, retention),
                TestContext.Current.CancellationToken)
            .WaitAsync(GlyphPipelineTestHarness.TIMEOUT, TestContext.Current.CancellationToken);
        TextGlyphMetrics metrics = await Task.Run(
                () => pipeline.GetMetrics(glyphId),
                TestContext.Current.CancellationToken)
            .WaitAsync(GlyphPipelineTestHarness.TIMEOUT, TestContext.Current.CancellationToken);

        Assert.Null(repeated);
        Assert.Equal(0.6f, metrics.AdvanceX);
        upload.TrySetResult(harness.Factory.CreateTexture(16, 16));
        await pipeline.WhenGlyphSettledAsync(glyphId)
            .WaitAsync(GlyphPipelineTestHarness.TIMEOUT, TestContext.Current.CancellationToken);
        Assert.True(pipeline.IsGlyphReady(glyphId));
        retention.Release();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ServiceStopThenPipelineDisposeReleasesEveryTexture() {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateManyGlyphFace(2, out int firstGlyphId);
        FontGlyphPipeline pipeline = harness.CreatePipeline(face);
        Texture first = await GlyphPipelineTestHarness.GenerateAsync(pipeline, firstGlyphId, 1);
        Texture second = await GlyphPipelineTestHarness.GenerateAsync(pipeline, firstGlyphId + 1, 2);

        harness.Service.Stop();
        pipeline.Dispose();
        pipeline.Dispose();

        Assert.False(harness.Service.IsRunning);
        Assert.True(first.IsDisposed);
        Assert.True(second.IsDisposed);
        Assert.Equal(0, pipeline.CachedGlyphCount);
        Assert.False(pipeline.IsGlyphReady(firstGlyphId));
        Assert.Null(GlyphPipelineTestHarness.AcquireOnce(pipeline, firstGlyphId));
        Assert.True(pipeline.WhenGlyphSettledAsync(firstGlyphId).IsCompleted);
        Assert.Throws<ObjectDisposedException>(() => pipeline.GetMetrics(firstGlyphId));
        Assert.Equal(2, harness.Factory.Destroyed.Count);
        Assert.Single(
            harness.Factory.Destroyed,
            resource => ReferenceEquals(resource, first.Lifetime.Resource));
        Assert.Single(
            harness.Factory.Destroyed,
            resource => ReferenceEquals(resource, second.Lifetime.Resource));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task MetricsSurviveAnEviction() {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateManyGlyphFace(2, out int firstGlyphId);
        FontGlyphPipeline pipeline = harness.CreatePipeline(
            face,
            new FontGlyphSettings(face.UnitsPerEm, 8, 4, 2),
            textureBudget: 1);
        TextGlyphMetrics expected = pipeline.GetMetrics(firstGlyphId);
        await GlyphPipelineTestHarness.GenerateAsync(pipeline, firstGlyphId, 1);
        await GlyphPipelineTestHarness.GenerateAsync(pipeline, firstGlyphId + 1, 2);

        Assert.False(pipeline.IsGlyphReady(firstGlyphId));
        Assert.Equal(expected, pipeline.GetMetrics(firstGlyphId));
        Assert.Empty(MetricWarnings(harness));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ALateTextureForADisposedPipelineIsDisposed() {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateSquareFace(out int glyphId);
        FontGlyphPipeline pipeline = harness.CreatePipeline(face);
        pipeline.Dispose();
        Texture late = harness.Factory.CreateTexture(8, 8);

        pipeline.Insert(glyphId, late);

        Assert.True(late.IsDisposed);
        Assert.Single(
            harness.Factory.Destroyed,
            resource => ReferenceEquals(resource, late.Lifetime.Resource));
        Assert.Equal(0, pipeline.CachedGlyphCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ADuplicateTextureForOneGlyphIsDisposed() {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateSquareFace(out int glyphId);
        FontGlyphPipeline pipeline = harness.CreatePipeline(face);
        Texture first = harness.Factory.CreateTexture(8, 8);
        Texture duplicate = harness.Factory.CreateTexture(8, 8);

        pipeline.Insert(glyphId, first);
        pipeline.Insert(glyphId, duplicate);

        Assert.False(first.IsDisposed);
        Assert.True(duplicate.IsDisposed);
        Assert.True(pipeline.TryGetTexture(glyphId, out Texture? cached));
        Assert.Same(first, cached);
        Assert.Equal(1, pipeline.CachedGlyphCount);
        Assert.Single(
            harness.Factory.Destroyed,
            resource => ReferenceEquals(resource, duplicate.Lifetime.Resource));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DisposingAPipelineWhileAnUploadIsInFlightReleasesTheLateTexture() {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateSquareFace(out int glyphId);
        FontGlyphPipeline pipeline = harness.CreatePipeline(face);
        TaskCompletionSource<Texture> upload =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Factory.Handler = (_, _, _, _) => upload.Task;
        pipeline.BeginFrame(1);
        FrameAssetRetention retention = new();
        Assert.Null(pipeline.Acquire(glyphId, retention));
        await harness.Factory.WhenCreateStartedAsync()
            .WaitAsync(GlyphPipelineTestHarness.TIMEOUT, TestContext.Current.CancellationToken);

        pipeline.Dispose();
        Texture late = harness.Factory.CreateTexture(16, 16);
        upload.TrySetResult(late);
        await harness.Service.WhenIdleAsync()
            .WaitAsync(GlyphPipelineTestHarness.TIMEOUT, TestContext.Current.CancellationToken);

        Assert.True(late.IsDisposed);
        Assert.Single(
            harness.Factory.Destroyed,
            resource => ReferenceEquals(resource, late.Lifetime.Resource));
        Assert.Equal(0, pipeline.CachedGlyphCount);
        Assert.True(pipeline.WhenGlyphSettledAsync(glyphId).IsCompleted);
        retention.Release();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task UnregisterKeepsAnotherPipelinesQueuedRequests() {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateManyGlyphFace(2, out int firstGlyphId);
        FontGlyphPipeline first = harness.CreatePipeline(face);
        FontGlyphPipeline second = harness.CreatePipeline(face);
        harness.Service.Pause();
        first.BeginFrame(1);
        second.BeginFrame(1);
        FrameAssetRetention retention = new();
        Assert.Null(first.Acquire(firstGlyphId, retention));
        Assert.Null(second.Acquire(firstGlyphId + 1, retention));

        first.Dispose();
        harness.Service.Resume();
        await harness.Service.WhenIdleAsync()
            .WaitAsync(GlyphPipelineTestHarness.TIMEOUT, TestContext.Current.CancellationToken);

        Assert.False(first.IsGlyphReady(firstGlyphId));
        Assert.True(second.IsGlyphReady(firstGlyphId + 1));
        Assert.Equal(1, harness.Factory.CreateCount);
        retention.Release();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task WhenGlyphSettledIsCompleteForReadyAndEmptyGlyphs() {
        using GlyphPipelineTestHarness harness = new();
        SfntFace squareFace = GlyphTestFonts.CreateSquareFace(out int squareGlyphId);
        FontGlyphPipeline squarePipeline = harness.CreatePipeline(squareFace);
        SfntFace emptyFace = GlyphTestFonts.CreateEmptyGlyphFace(out int emptyGlyphId);
        FontGlyphPipeline emptyPipeline = harness.CreatePipeline(emptyFace);

        await GlyphPipelineTestHarness.GenerateAsync(squarePipeline, squareGlyphId, 1);
        await GlyphPipelineTestHarness.RequestAndWaitAsync(emptyPipeline, emptyGlyphId, 1);

        Assert.True(squarePipeline.WhenGlyphSettledAsync(squareGlyphId).IsCompleted);
        Assert.True(emptyPipeline.WhenGlyphSettledAsync(emptyGlyphId).IsCompleted);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task TryGetTextureIsFalseForAPendingOrFailedGlyph() {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateManyGlyphFace(2, out int firstGlyphId);
        FontGlyphPipeline pipeline = harness.CreatePipeline(face);
        harness.Service.Pause();
        pipeline.BeginFrame(1);
        FrameAssetRetention retention = new();
        Assert.Null(pipeline.Acquire(firstGlyphId, retention));
        Assert.False(pipeline.TryGetTexture(firstGlyphId, out _));
        harness.Service.Resume();
        harness.Factory.UploadException = new InvalidOperationException("the upload failed");
        await GlyphPipelineTestHarness.RequestAndWaitAsync(pipeline, firstGlyphId + 1, 1);

        Assert.False(pipeline.IsGlyphReady(firstGlyphId + 1));
        Assert.False(pipeline.TryGetTexture(firstGlyphId + 1, out _));
        retention.Release();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task TheFactoryReceivesTheExactGeneratorBitmap() {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateSquareFace(out int glyphId);
        FontGlyphSettings settings = FontGlyphSettings.CreateDefault(face.UnitsPerEm);
        FontGlyphPipeline pipeline = harness.CreatePipeline(face, settings);
        FontGlyph glyph = face.GetGlyph(glyphId);
        MsdfGlyphLayout layout = MsdfGenerator.ComputeLayout(
            glyph.Bounds,
            settings.UnitsPerEm,
            settings.MsdfPixelSize,
            settings.MsdfRange,
            settings.MsdfPadding);
        byte[] expected = MsdfGenerator.Generate(glyph.Outline, layout);

        Texture texture = await GlyphPipelineTestHarness.GenerateAsync(pipeline, glyphId, 1);

        GlyphTextureCreation creation = Assert.Single(harness.Factory.Creations);
        Assert.Equal(layout.TextureWidth, creation.Width);
        Assert.Equal(layout.TextureHeight, creation.Height);
        Assert.True(
            expected.AsSpan().SequenceEqual(creation.Rgba),
            "The factory received a bitmap that differs from the direct generator output.");
        Assert.Equal(layout.TextureWidth, texture.Width);
        Assert.Equal(layout.TextureHeight, texture.Height);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task GenerationIsByteIdenticalAcrossTwoPipelines() {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateSquareFace(out int glyphId);
        FontGlyphPipeline first = harness.CreatePipeline(face);
        FontGlyphPipeline second = harness.CreatePipeline(face);

        await GlyphPipelineTestHarness.GenerateAsync(first, glyphId, 1);
        await GlyphPipelineTestHarness.GenerateAsync(second, glyphId, 2);

        IReadOnlyList<GlyphTextureCreation> creations = harness.Factory.Creations;
        Assert.Equal(2, creations.Count);
        Assert.Equal(creations[0].Width, creations[1].Width);
        Assert.Equal(creations[0].Height, creations[1].Height);
        Assert.True(
            creations[0].Rgba.AsSpan().SequenceEqual(creations[1].Rgba),
            "Two generations of the same glyph produced different bytes.");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ConcurrentAcquireAndGetMetricsStayConsistent() {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateManyGlyphFace(6, out int firstGlyphId);
        FontGlyphPipeline pipeline = harness.CreatePipeline(face);
        pipeline.BeginFrame(1);
        int[] glyphIds = [.. Enumerable.Range(0, 6).Select(index => firstGlyphId + index)];
        Dictionary<int, TextGlyphMetrics> expected = glyphIds
            .ToDictionary(glyphId => glyphId, glyphId => pipeline.GetMetrics(glyphId));
        ConcurrentDictionary<int, Texture> textures = new();

        Task[] workers = new Task[4];
        for (int worker = 0; worker < workers.Length; worker++) {
            int workerIndex = worker;
            workers[worker] = Task.Run(
                () => {
                    for (int iteration = 0; iteration < 100; iteration++) {
                        int glyphId = glyphIds[(workerIndex + iteration) % glyphIds.Length];
                        Assert.Equal(expected[glyphId], pipeline.GetMetrics(glyphId));
                        FrameAssetRetention retention = new();
                        Texture? texture = pipeline.Acquire(glyphId, retention);
                        retention.Release();
                        if (texture is not null) {
                            textures.TryAdd(glyphId, texture);
                        }
                    }
                },
                TestContext.Current.CancellationToken);
        }

        await Task.WhenAll(workers)
            .WaitAsync(GlyphPipelineTestHarness.TIMEOUT, TestContext.Current.CancellationToken);
        await harness.Service.WhenIdleAsync()
            .WaitAsync(GlyphPipelineTestHarness.TIMEOUT, TestContext.Current.CancellationToken);

        Assert.All(glyphIds, glyphId => Assert.True(pipeline.IsGlyphReady(glyphId)));
        Assert.Equal(glyphIds.Length, pipeline.CachedGlyphCount);
        Assert.Equal(glyphIds.Length, harness.Factory.CreateCount);
        foreach (int glyphId in glyphIds) {
            Texture? cached = GlyphPipelineTestHarness.AcquireOnce(pipeline, glyphId);
            Assert.NotNull(cached);
            if (textures.TryGetValue(glyphId, out Texture? concurrent)) {
                Assert.Same(concurrent, cached);
            }
        }
    }

    private static IReadOnlyList<TrackedLogEntry> GenerationWarnings(GlyphPipelineTestHarness harness) => [
        .. harness.LoggerFactory.Entries.Where(
            entry => entry.Level == LogLevel.Warning
                && entry.Message.Contains("could not be generated", StringComparison.Ordinal)),
    ];

    private static IReadOnlyList<TrackedLogEntry> MetricWarnings(GlyphPipelineTestHarness harness) => [
        .. harness.LoggerFactory.Entries.Where(
            entry => entry.Level == LogLevel.Warning
                && entry.Message.Contains("metrics of glyph", StringComparison.Ordinal)),
    ];
}
