using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using RayBork.Internal;

namespace RayBork.Tests.Fonts.Glyph;

/// <summary>
/// Tests the bounded glyph texture cache: misses, hits, empty glyphs, eviction order, the production
/// budget, failure without retry, concurrent use, and the exact generator bitmap.
/// </summary>
public sealed class FontGlyphPipelineCacheTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task AMissEnqueuesOnceAndAHitReturnsTheCachedTexture() {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateSquareFace(out int glyphId);
        FontGlyphPipeline pipeline = harness.CreatePipeline(face);

        harness.Service.Pause();
        Assert.Null(pipeline.Acquire(glyphId, 1));
        Assert.Null(pipeline.Acquire(glyphId, 1));
        Assert.Equal(0, harness.Factory.CreateCount);
        Assert.False(pipeline.IsGlyphReady(glyphId));
        Assert.False(harness.Service.WhenIdleAsync().IsCompleted);

        harness.Service.Resume();
        await pipeline.WhenGlyphReadyAsync(glyphId)
            .WaitAsync(GlyphPipelineTestHarness.TIMEOUT, TestContext.Current.CancellationToken);

        Assert.True(pipeline.IsGlyphReady(glyphId));
        Assert.Equal(1, harness.Factory.CreateCount);
        Assert.Equal(1, pipeline.CachedGlyphCount);
        Assert.True(pipeline.TryGetTexture(glyphId, out Texture? peek));
        Texture? acquired = pipeline.Acquire(glyphId, 2);
        Assert.NotNull(acquired);
        Assert.Same(peek, acquired);
        Assert.Equal(1, harness.Factory.CreateCount);
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
        Assert.Null(pipeline.Acquire(glyphId, 2));
        Assert.False(pipeline.TryGetTexture(glyphId, out _));
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
    public async Task AnEntryUsedAtTheInsertingStampIsNotEvicted() {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateManyGlyphFace(2, out int firstGlyphId);
        FontGlyphPipeline pipeline = harness.CreatePipeline(
            face,
            new FontGlyphSettings(face.UnitsPerEm, 8, 4, 2),
            textureBudget: 1);

        Texture first = await GlyphPipelineTestHarness.GenerateAsync(pipeline, firstGlyphId, 1);
        Texture second = await GlyphPipelineTestHarness.GenerateAsync(pipeline, firstGlyphId + 1, 1);

        Assert.Equal(2, pipeline.CachedGlyphCount);
        Assert.False(first.IsDisposed);
        Assert.False(second.IsDisposed);
        Assert.True(pipeline.IsGlyphReady(firstGlyphId));
        Assert.True(pipeline.IsGlyphReady(firstGlyphId + 1));
        Assert.Empty(harness.Factory.Destroyed);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task AHitRecordsTheUseStamp() {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateManyGlyphFace(2, out int firstGlyphId);
        FontGlyphPipeline pipeline = harness.CreatePipeline(
            face,
            new FontGlyphSettings(face.UnitsPerEm, 8, 4, 2),
            textureBudget: 1);
        Texture first = await GlyphPipelineTestHarness.GenerateAsync(pipeline, firstGlyphId, 1);

        Assert.Same(first, pipeline.Acquire(firstGlyphId, 2));

        await GlyphPipelineTestHarness.GenerateAsync(pipeline, firstGlyphId + 1, 2);

        Assert.Equal(2, pipeline.CachedGlyphCount);
        Assert.True(pipeline.IsGlyphReady(firstGlyphId));
        Assert.False(first.IsDisposed);
        Assert.Empty(harness.Factory.Destroyed);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task TemporaryOverflowRecoversWhenTheStampAdvances() {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateManyGlyphFace(3, out int firstGlyphId);
        FontGlyphPipeline pipeline = harness.CreatePipeline(
            face,
            new FontGlyphSettings(face.UnitsPerEm, 8, 4, 2),
            textureBudget: 1);

        Texture first = await GlyphPipelineTestHarness.GenerateAsync(pipeline, firstGlyphId, 1);
        Texture second = await GlyphPipelineTestHarness.GenerateAsync(pipeline, firstGlyphId + 1, 1);
        Assert.Equal(2, pipeline.CachedGlyphCount);

        Texture third = await GlyphPipelineTestHarness.GenerateAsync(pipeline, firstGlyphId + 2, 2);

        Assert.Equal(1, pipeline.CachedGlyphCount);
        Assert.True(first.IsDisposed);
        Assert.True(second.IsDisposed);
        Assert.False(third.IsDisposed);
        Assert.True(pipeline.IsGlyphReady(firstGlyphId + 2));
        Assert.Equal(2, harness.Factory.Destroyed.Count);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task TheCacheHonoursTheProductionTextureBudget() {
        Assert.Equal(512, FontGlyphPipeline.MAX_CACHED_TEXTURES);

        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateManyGlyphFace(
            FontGlyphPipeline.MAX_CACHED_TEXTURES + 1,
            out int firstGlyphId);
        FontGlyphPipeline pipeline = harness.CreatePipeline(
            face,
            new FontGlyphSettings(face.UnitsPerEm, 8, 4, 2));

        int glyphCount = FontGlyphPipeline.MAX_CACHED_TEXTURES + 1;
        for (int index = 0; index < glyphCount; index++) {
            await GlyphPipelineTestHarness.GenerateAsync(pipeline, firstGlyphId + index, index + 1);
        }

        Assert.Equal(FontGlyphPipeline.MAX_CACHED_TEXTURES, pipeline.CachedGlyphCount);
        Assert.False(pipeline.IsGlyphReady(firstGlyphId));
        for (int index = 1; index < glyphCount; index++) {
            Assert.True(pipeline.IsGlyphReady(firstGlyphId + index));
        }

        Assert.Equal(glyphCount, harness.Factory.CreateCount);
        Assert.Single(harness.Factory.Destroyed);
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
        Assert.Null(pipeline.Acquire(glyphId, 2));
        await harness.Service.WhenIdleAsync()
            .WaitAsync(GlyphPipelineTestHarness.TIMEOUT, TestContext.Current.CancellationToken);
        Assert.Equal(1, harness.Factory.CreateCount);
        Assert.Single(GenerationWarnings(harness));

        Assert.Null(pipeline.Acquire(glyphId, 3));
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
        Assert.Null(pipeline.Acquire(glyphId, 2));
        await harness.Service.WhenIdleAsync()
            .WaitAsync(GlyphPipelineTestHarness.TIMEOUT, TestContext.Current.CancellationToken);
        Assert.Equal(0, harness.Factory.CreateCount);
        Assert.Single(GenerationWarnings(harness));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task AcquireDoesNotBlockWhileTheServiceIsPaused() {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateSquareFace(out int glyphId);
        FontGlyphPipeline pipeline = harness.CreatePipeline(face);
        harness.Service.Pause();

        Texture? missed = await Task.Run(
                () => pipeline.Acquire(glyphId, 1),
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
        Assert.Null(pipeline.Acquire(firstGlyphId, 3));
        Assert.True(pipeline.WhenGlyphReadyAsync(firstGlyphId).IsCompleted);
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
                        Texture? texture = pipeline.Acquire(glyphId, iteration + 1);
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
            Texture? cached = pipeline.Acquire(glyphId, 200);
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
}
