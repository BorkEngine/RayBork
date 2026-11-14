using System.Text;
using RayBork.Internal;
using RayBork.Tests.Fonts.Glyph;

namespace RayBork.Tests.Fonts;

/// <summary>
/// Tests the public <see cref="Font"/> asset surface over the real pipeline and a fake glyph texture
/// factory: em-unit metrics, measurement, on-demand glyph acquisition, the <c>.notdef</c> fallback, and
/// the disposal contract.
/// </summary>
public sealed class FontTests {
    private static readonly TimeSpan _timeout = GlyphPipelineTestHarness.TIMEOUT;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void FaceMetricsAreInEmUnits() {
        using FontTestHarness harness = new(CreateKernedSquareFace(out int glyphId));
        Font font = harness.Font;

        Assert.Equal(1f, font.LineHeight);
        Assert.Equal(0.8f, font.Ascent);
        Assert.Equal(0.2f, font.Descent);
        Assert.Equal(1, font.GlyphCount);
        Assert.Equal(0, font.CachedGlyphCount);
        AssertNear(-0.1f, font.GetKerning(glyphId, glyphId));
        AssertNear(0f, font.GetKerning(SfntFace.NOTDEF_GLYPH_ID, SfntFace.NOTDEF_GLYPH_ID));

        ITextMetrics metrics = font.Metrics;
        Assert.Same(metrics, font.Metrics);
        Assert.Equal(font.LineHeight, metrics.LineHeight);
        Assert.Equal(font.Ascent, metrics.Ascent);
        Assert.True(metrics.TryGetGlyphMetrics(new Rune('A'), out TextGlyphMetrics mapped));
        Assert.Equal(glyphId, mapped.GlyphId);
        AssertNear(0.6f, mapped.AdvanceX);
        Assert.True(metrics.TryGetGlyphMetrics(new Rune(0x4E00), out TextGlyphMetrics fallback));
        Assert.Equal(SfntFace.NOTDEF_GLYPH_ID, fallback.GlyphId);
        AssertNear(0.5f, fallback.AdvanceX);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MeasureAppliesSizeKerningAndTheNotdefFallback() {
        using FontTestHarness harness = new(CreateKernedSquareFace(out _));
        Font font = harness.Font;

        AssertVectorNear(new System.Numerics.Vector2(6f, 10f), font.Measure("A", 10f));
        AssertVectorNear(new System.Numerics.Vector2(11f, 10f), font.Measure("AA", 10f));
        AssertVectorNear(new System.Numerics.Vector2(6f, 20f), font.Measure("A\nA", 10f));
        AssertVectorNear(new System.Numerics.Vector2(5f, 10f), font.Measure("\u4E00", 10f));
        AssertVectorNear(System.Numerics.Vector2.Zero, font.Measure("", 10f));
        Assert.Throws<ArgumentOutOfRangeException>(() => font.Measure("A", 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => font.Measure("A", float.NaN));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task TryGetGlyphIsFalseUntilTheGlyphTextureIsGenerated() {
        using FontTestHarness harness = new(GlyphTestFonts.CreateSquareFace(out int glyphId));
        Font font = harness.Font;

        Assert.False(font.TryGetGlyph(new Rune('A'), out RayBork.Glyph missing));
        Assert.Equal<RayBork.Glyph>(default, missing);

        Assert.Null(AcquireGlyph(font, glyphId, frameIndex: 1));
        await font.WhenGlyphSettledAsync(glyphId)
            .WaitAsync(_timeout, TestContext.Current.CancellationToken);

        Assert.Equal(1, font.CachedGlyphCount);
        Assert.True(font.TryGetGlyph(new Rune('A'), out RayBork.Glyph glyph));
        Assert.Equal(new Rune('A'), glyph.Rune);
        AssertNear(0.6f, glyph.Advance.X);
        AssertNear(0f, glyph.Advance.Y);
        AssertNear(0f, glyph.Uv0.X);
        AssertNear(0f, glyph.Uv0.Y);
        AssertNear(1f, glyph.Uv1.X);
        AssertNear(1f, glyph.Uv1.Y);
        Assert.True(glyph.Size.X > 0f);
        Assert.True(glyph.Size.Y > 0f);

        Assert.True(font.Metrics.TryGetGlyphMetrics(new Rune('A'), out TextGlyphMetrics metrics));
        AssertNear(metrics.BearingX, glyph.Bearing.X);
        AssertNear(metrics.BearingY, glyph.Bearing.Y);
        AssertNear(metrics.Width, glyph.Size.X);
        AssertNear(metrics.Height, glyph.Size.Y);

        // A cached glyph needs no second generation.
        Assert.NotNull(AcquireGlyph(font, glyphId, frameIndex: 2));
        Assert.Equal(1, harness.Factory.CreateCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task AnUnmappedRuneFallsBackToTheNotdefGlyph() {
        using FontTestHarness harness = new(GlyphTestFonts.CreateSquareFace(out _));
        Font font = harness.Font;
        Rune unmapped = new(0x4E00);

        Assert.False(font.TryGetGlyphId(unmapped, out int glyphId));
        Assert.Equal(SfntFace.NOTDEF_GLYPH_ID, glyphId);
        Assert.False(font.TryGetGlyph(unmapped, out _));

        Assert.Null(AcquireGlyph(font, SfntFace.NOTDEF_GLYPH_ID, frameIndex: 1));
        await font.WhenGlyphSettledAsync(SfntFace.NOTDEF_GLYPH_ID)
            .WaitAsync(_timeout, TestContext.Current.CancellationToken);

        Assert.True(font.TryGetGlyph(unmapped, out RayBork.Glyph glyph));
        Assert.Equal(unmapped, glyph.Rune);
        AssertNear(0.5f, glyph.Advance.X);
        Assert.False(font.TryGetGlyph(new Rune('A'), out _));
        Assert.Equal(1, font.GlyphCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task AnEmptyGlyphAdvancesThePenWithoutATexture() {
        using FontTestHarness harness = new(GlyphTestFonts.CreateEmptyGlyphFace(out int glyphId));
        Font font = harness.Font;

        Assert.Null(AcquireGlyph(font, glyphId, frameIndex: 1));
        await font.WhenGlyphSettledAsync(glyphId)
            .WaitAsync(_timeout, TestContext.Current.CancellationToken);

        Assert.False(font.TryGetGlyph(new Rune(' '), out _));
        Assert.Equal(0, font.CachedGlyphCount);
        Assert.Equal(0, harness.Factory.CreateCount);
        AssertVectorNear(new System.Numerics.Vector2(0.25f, 1f), font.Measure(" ", 1f));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DisposeKeepsPathAndStateReadableAndRejectsEveryOtherMember() {
        using FontTestHarness harness = new(GlyphTestFonts.CreateSquareFace(out int glyphId));
        Font font = harness.Font;
        string path = font.Path;

        font.Dispose();

        Assert.True(font.IsDisposed);
        Assert.Equal(path, font.Path);
        Assert.Throws<ObjectDisposedException>(() => _ = font.LineHeight);
        Assert.Throws<ObjectDisposedException>(() => _ = font.Ascent);
        Assert.Throws<ObjectDisposedException>(() => _ = font.Descent);
        Assert.Throws<ObjectDisposedException>(() => _ = font.GlyphCount);
        Assert.Throws<ObjectDisposedException>(() => _ = font.CachedGlyphCount);
        Assert.Throws<ObjectDisposedException>(() => font.TryGetGlyph(new Rune('A'), out _));
        Assert.Throws<ObjectDisposedException>(() => font.Measure("A", 1f));
        Assert.Throws<ObjectDisposedException>(() => _ = font.Metrics);
        Assert.Throws<ObjectDisposedException>(() => font.BeginFrame(1));
        Assert.Throws<ObjectDisposedException>(() => { _ = font.AcquireGlyphTexture(glyphId, new FrameAssetRetention()); });
        Assert.Throws<ObjectDisposedException>(() => font.TryGetGlyphId(new Rune('A'), out _));
        Assert.Throws<ObjectDisposedException>(() => font.GetKerning(glyphId, glyphId));
        Assert.Throws<ObjectDisposedException>(() => { _ = font.WhenGlyphSettledAsync(glyphId); });
        Assert.Throws<ObjectDisposedException>(font.Dispose);
    }

    private static Texture? AcquireGlyph(Font font, int glyphId, long frameIndex) {
        font.BeginFrame(frameIndex);
        FrameAssetRetention probe = new();
        try {
            return font.AcquireGlyphTexture(glyphId, probe);
        } finally {
            probe.Release();
        }
    }

    /// <summary>
    /// Builds a face like <see cref="GlyphTestFonts.CreateSquareFace"/> plus one kern pair of -100 units
    /// for the mapped glyph followed by itself.
    /// </summary>
    private static SfntFace CreateKernedSquareFace(out int glyphId) {
        SyntheticFontBuilder builder = SyntheticFontBuilder.Create();
        glyphId = builder.AddSimple(
            600,
            0,
            new SyntheticPoint(0, 0),
            new SyntheticPoint(500, 0),
            new SyntheticPoint(500, 500),
            new SyntheticPoint(0, 500));
        return SfntFace.Parse(
            builder
                .WithCmap(4, (0x41, glyphId))
                .WithKernPairs((glyphId, glyphId, -100))
                .Build());
    }

    private static void AssertNear(float expected, float actual, float tolerance = 1e-5f)
        => Assert.True(
            MathF.Abs(expected - actual) <= tolerance,
            $"Expected {expected} within {tolerance} but found {actual}.");

    private static void AssertVectorNear(System.Numerics.Vector2 expected, System.Numerics.Vector2 actual)
        => Assert.True(
            MathF.Abs(expected.X - actual.X) <= 1e-5f && MathF.Abs(expected.Y - actual.Y) <= 1e-5f,
            $"Expected {expected} but found {actual}.");

    /// <summary>
    /// One parsed face, its pipeline, and a <see cref="Font"/> over a CPU-only <see cref="EngineAsset"/>.
    /// The fake texture factory and app-lifetime generation service come from the glyph test harness.
    /// </summary>
    private sealed class FontTestHarness : IDisposable {
        private readonly GlyphPipelineTestHarness _glyphs;

        internal FontTestHarness(SfntFace face) {
            ArgumentNullException.ThrowIfNull(face);

            _glyphs = new GlyphPipelineTestHarness();
            Factory = _glyphs.Factory;
            FontGlyphPipeline pipeline = _glyphs.CreatePipeline(face);
            EngineAsset asset = new(
                "fonts/test-face",
                resource: null,
                static _ => { },
                static () => { },
                pipeline.Dispose);
            asset.AddOwnerReference();
            Font = new Font(asset, face, pipeline);
        }

        internal Font Font { get; }

        internal FakeGlyphTextureFactory Factory { get; }

        public void Dispose() {
            // Release the font's owner reference even when a test forgot to dispose it, so the harness
            // never leaks the lifetime. Disposing the pipeline again through the glyph harness is a
            // no-op, and the glyph harness stops the generation service before it disposes its
            // pipelines.
            if (!Font.IsDisposed) {
                Font.Dispose();
            }

            _glyphs.Dispose();
        }
    }
}
