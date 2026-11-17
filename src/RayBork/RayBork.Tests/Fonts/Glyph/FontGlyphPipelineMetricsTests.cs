using Microsoft.Extensions.Logging;
using RayBork.Internal;

namespace RayBork.Tests.Fonts.Glyph;

/// <summary>
/// Tests the per-glyph metrics path: a mapped glyph, an empty glyph, and the unsupported, malformed,
/// or over-budget outlines that fall back to the face advance with a zero quad.
/// </summary>
public sealed class FontGlyphPipelineMetricsTests
{
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AMappedGlyphUsesTheFaceAdvanceAndTheMsdfLayout()
    {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateSquareFace(out int glyphId);
        FontGlyphSettings settings = FontGlyphSettings.CreateDefault(face.UnitsPerEm);
        FontGlyphPipeline pipeline = harness.CreatePipeline(face, settings);
        MsdfGlyphLayout layout = MsdfGenerator.ComputeLayout(
            face.GetGlyph(glyphId).Bounds,
            settings.UnitsPerEm,
            settings.MsdfPixelSize,
            settings.MsdfRange,
            settings.MsdfPadding
        );

        TextGlyphMetrics metrics = pipeline.GetMetrics(glyphId);

        Assert.Equal(glyphId, metrics.GlyphId);
        Assert.Equal(0.6f, metrics.AdvanceX);
        Assert.Equal(layout.BearingXEm, metrics.BearingX);
        Assert.Equal(layout.BearingYEm, metrics.BearingY);
        Assert.Equal(layout.WidthEm, metrics.Width);
        Assert.Equal(layout.HeightEm, metrics.Height);
        Assert.Equal(32, layout.TextureWidth);
        Assert.Equal(32, layout.TextureHeight);
        Assert.Equal(metrics, pipeline.GetMetrics(glyphId));
        Assert.False(pipeline.IsGlyphReady(glyphId));
        Assert.Equal(0, pipeline.CachedGlyphCount);
        Assert.Equal(0, harness.Factory.CreateCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AnEmptyGlyphHasTheAdvanceOnly()
    {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateEmptyGlyphFace(out int glyphId);
        FontGlyphPipeline pipeline = harness.CreatePipeline(face);

        TextGlyphMetrics metrics = pipeline.GetMetrics(glyphId);

        Assert.Equal(glyphId, metrics.GlyphId);
        Assert.Equal(0.25f, metrics.AdvanceX);
        Assert.Equal(0f, metrics.BearingX);
        Assert.Equal(0f, metrics.BearingY);
        Assert.Equal(0f, metrics.Width);
        Assert.Equal(0f, metrics.Height);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AnUnsupportedOutlineFallsBackAndLogsOnce()
    {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateUnsupportedCompositeFace(out int glyphId);
        FontGlyphPipeline pipeline = harness.CreatePipeline(face);

        TextGlyphMetrics first = pipeline.GetMetrics(glyphId);
        TextGlyphMetrics second = pipeline.GetMetrics(glyphId);

        Assert.Equal(first, second);
        Assert.Equal(0.5f, first.AdvanceX);
        Assert.Equal(0f, first.BearingX);
        Assert.Equal(0f, first.BearingY);
        Assert.Equal(0f, first.Width);
        Assert.Equal(0f, first.Height);
        Assert.Single(MetricWarnings(harness));
        Assert.Contains(
            "point-matched",
            MetricWarnings(harness)[0].Message,
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AnInvertedStoredBoxFallsBackAndLogsOnce()
    {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateInvertedBoundsFace(out int glyphId);
        FontGlyphPipeline pipeline = harness.CreatePipeline(face);

        TextGlyphMetrics first = pipeline.GetMetrics(glyphId);
        TextGlyphMetrics second = pipeline.GetMetrics(glyphId);

        Assert.Equal(first, second);
        Assert.Equal(0.5f, first.AdvanceX);
        Assert.Equal(0f, first.Width);
        Assert.Equal(0f, first.Height);
        Assert.Single(MetricWarnings(harness));
        Assert.Contains("inverted", MetricWarnings(harness)[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ATextureAboveTheTexelBudgetFallsBackAndLogsOnce()
    {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateSquareFace(out int glyphId);
        FontGlyphPipeline pipeline = harness.CreatePipeline(
            face,
            new FontGlyphSettings(1000, 4096, 4, 0)
        );

        TextGlyphMetrics first = pipeline.GetMetrics(glyphId);
        TextGlyphMetrics second = pipeline.GetMetrics(glyphId);

        Assert.Equal(first, second);
        Assert.Equal(0.6f, first.AdvanceX);
        Assert.Equal(0f, first.Width);
        Assert.Equal(0f, first.Height);
        Assert.Single(MetricWarnings(harness));
        Assert.Contains("texels", MetricWarnings(harness)[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ConcurrentMetricsResolutionLogsOneWarning()
    {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateUnsupportedCompositeFace(out int glyphId);
        FontGlyphPipeline pipeline = harness.CreatePipeline(face);
        TextGlyphMetrics[] results = new TextGlyphMetrics[8];

        Task[] workers = new Task[results.Length];
        for (int index = 0; index < workers.Length; index++)
        {
            int workerIndex = index;
            workers[index] = Task.Run(
                () => results[workerIndex] = pipeline.GetMetrics(glyphId),
                TestContext.Current.CancellationToken
            );
        }

        await Task.WhenAll(workers)
            .WaitAsync(GlyphPipelineTestHarness.TIMEOUT, TestContext.Current.CancellationToken);

        Assert.All(results, result => Assert.Equal(results[0], result));
        Assert.Single(MetricWarnings(harness));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GlyphIdsAndArgumentsOutsideTheContractAreRejected()
    {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateSquareFace(out int glyphId);
        FontGlyphPipeline pipeline = harness.CreatePipeline(face);
        FrameAssetRetention retention = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => pipeline.GetMetrics(face.GlyphCount));
        Assert.Throws<ArgumentOutOfRangeException>(() => pipeline.GetMetrics(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            pipeline.Acquire(face.GlyphCount, retention)
        );
        Assert.Throws<ArgumentNullException>(() => pipeline.Acquire(glyphId, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => pipeline.TryGetTexture(-1, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => pipeline.IsGlyphReady(face.GlyphCount));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = pipeline.WhenGlyphSettledAsync(face.GlyphCount);
        });
        Assert.Throws<ArgumentOutOfRangeException>(() => pipeline.BeginFrame(-1));
        retention.Release();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AMismatchedUnitsPerEmIsRejected()
    {
        using GlyphPipelineTestHarness harness = new();
        SfntFace face = GlyphTestFonts.CreateSquareFace(out int _);
        FontGlyphSettings settings = FontGlyphSettings.CreateDefault(face.UnitsPerEm + 1);

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            harness.CreatePipeline(face, settings)
        );

        Assert.Contains("units per em", exception.Message, StringComparison.Ordinal);
    }

    private static IReadOnlyList<TrackedLogEntry> MetricWarnings(
        GlyphPipelineTestHarness harness
    ) =>
        [
            .. harness.LoggerFactory.Entries.Where(entry =>
                entry.Level == LogLevel.Warning
                && entry.Message.Contains("metrics of glyph", StringComparison.Ordinal)
            ),
        ];
}
