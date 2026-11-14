using System.Numerics;
using RayBork.Internal;

namespace RayBork.Tests.Fonts.Msdf;

/// <summary>
/// Tests the MSDF generation entry point: determinism, empty and degenerate inputs, argument
/// validation, the documented work limits, and a real glyph outline resolved from a synthetic TTF
/// face.
/// </summary>
public sealed class MsdfGeneratorTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TheGenerationIsByteIdenticalAcrossCalls() {
        MsdfGlyphLayout layout = MsdfGenerator.ComputeLayout(
            new FontBounds(100, 100, 900, 900),
            unitsPerEm: 1000);
        OutlinePath outline = MsdfTestShapes.Polygon(96, 500f, 500f, 400f);

        byte[] first = MsdfGenerator.Generate(outline, layout);
        byte[] second = MsdfGenerator.Generate(outline, layout);

        Assert.True(
            first.AsSpan().SequenceEqual(second),
            "Two generations of the same outline produced different bytes.");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AnEmptyOutlineAndAnEmptyLayoutProduceAnEmptyArray() {
        byte[] bitmap = MsdfGenerator.Generate(OutlinePath.Empty, MsdfGlyphLayout.Empty);

        Assert.Empty(bitmap);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AnEmptyOutlineWithANonEmptyLayoutProducesTheOutsideField() {
        MsdfGlyphLayout layout = MsdfGenerator.ComputeLayout(
            new FontBounds(0, 0, 1000, 1000),
            unitsPerEm: 1000);

        byte[] bitmap = MsdfGenerator.Generate(OutlinePath.Empty, layout);

        Assert.Equal(layout.TextureWidth * layout.TextureHeight * 4, bitmap.Length);
        MsdfTestField.AssertAllOutside(bitmap);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AnOutlineWithoutEdgesProducesTheOutsideField() {
        MsdfGlyphLayout layout = MsdfGenerator.ComputeLayout(
            new FontBounds(0, 0, 1000, 1000),
            unitsPerEm: 1000);
        OutlinePath outline = MsdfTestShapes.ContourWithoutEdges(500f, 500f);

        byte[] bitmap = MsdfGenerator.Generate(outline, layout);

        Assert.Equal(layout.TextureWidth * layout.TextureHeight * 4, bitmap.Length);
        MsdfTestField.AssertAllOutside(bitmap);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void InvalidArgumentsAreRejected() {
        OutlinePath outline = MsdfTestShapes.Rectangle(0f, 0f, 1000f, 1000f);

        Assert.Throws<ArgumentNullException>(
            () => MsdfGenerator.Generate(null!, MsdfGlyphLayout.Empty));
        Assert.Equal(
            "layout",
            Assert.Throws<ArgumentException>(
                () => MsdfGenerator.Generate(outline, MsdfGlyphLayout.Empty)).ParamName);
        Assert.Equal(
            "layout",
            Assert.Throws<ArgumentException>(
                () => MsdfGenerator.Generate(
                    outline,
                    new MsdfGlyphLayout(4, 4, 0f, 4, 0f, 0f, 0f, 0f, 0f, 0f))).ParamName);
        Assert.Equal(
            "layout",
            Assert.Throws<ArgumentException>(
                () => MsdfGenerator.Generate(
                    outline,
                    new MsdfGlyphLayout(4, 4, 1f, 0, 0f, 0f, 0f, 0f, 0f, 0f))).ParamName);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AnOversizedTextureIsRejectedBeforeItIsAllocated() {
        MsdfGlyphLayout layout = MsdfGenerator.ComputeLayout(
            new FontBounds(0, 0, 1000, 1000),
            unitsPerEm: 1000,
            pixelSize: 4096,
            padding: 0);
        OutlinePath outline = MsdfTestShapes.Rectangle(0f, 0f, 1000f, 1000f);

        MsdfLimitException exception = Assert.Throws<MsdfLimitException>(
            () => MsdfGenerator.Generate(outline, layout));

        Assert.Contains("texels", exception.Reason, StringComparison.Ordinal);
        Assert.Contains(MsdfGenerator.MAX_TEXELS.ToString(), exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AnOutlineAboveTheEdgeLimitIsRejected() {
        MsdfGlyphLayout layout = MsdfGenerator.ComputeLayout(
            new FontBounds(0, 0, 1000, 1000),
            unitsPerEm: 1000);
        OutlineSegment[] segments = new OutlineSegment[300];
        for (int index = 0; index < segments.Length; index++) {
            segments[index] = OutlineSegment.Cubic(
                new Vector2(300_000f, 0f),
                new Vector2(0f, 300_000f),
                Vector2.Zero);
        }

        OutlinePath outline = MsdfTestShapes.CreatePath(
            new OutlineContour(Vector2.Zero, segments));

        MsdfLimitException exception = Assert.Throws<MsdfLimitException>(
            () => MsdfGenerator.Generate(outline, layout));

        Assert.Contains("edges", exception.Reason, StringComparison.Ordinal);
        Assert.Contains(
            MsdfGenerator.MAX_FLATTENED_EDGES.ToString(),
            exception.Reason,
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AnOutlineAboveTheEdgeTexelPairLimitIsRejected() {
        MsdfGlyphLayout layout = MsdfGenerator.ComputeLayout(
            new FontBounds(0, 0, 1000, 1000),
            unitsPerEm: 1000,
            pixelSize: 64,
            padding: 0);
        OutlinePath outline = MsdfTestShapes.Polygon(2100, 500f, 500f, 400f);

        MsdfLimitException exception = Assert.Throws<MsdfLimitException>(
            () => MsdfGenerator.Generate(outline, layout));

        Assert.Contains("edge-texel pairs", exception.Reason, StringComparison.Ordinal);
        Assert.Contains(
            MsdfGenerator.MAX_EDGE_TEXEL_PAIRS.ToString(),
            exception.Reason,
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ARealGlyphOutlineFromASyntheticFaceGeneratesAValidField() {
        var builder = SyntheticFontBuilder.Create();
        int glyph = builder.AddSimple(
            600,
            0,
            new SyntheticPoint(150, 0),
            new SyntheticPoint(350, 0),
            new SyntheticPoint(500, 150),
            new SyntheticPoint(500, 350),
            new SyntheticPoint(350, 500),
            new SyntheticPoint(150, 500),
            new SyntheticPoint(0, 350),
            new SyntheticPoint(0, 150));
        SfntFace face = SfntFace.Parse(builder.WithCmap(4, (0x41, glyph)).Build());
        FontGlyph resolved = face.GetGlyph(glyph);

        MsdfGlyphLayout layout = MsdfGenerator.ComputeLayout(resolved.Bounds, face.UnitsPerEm);
        byte[] bitmap = MsdfGenerator.Generate(resolved.Outline, layout);

        Assert.Equal(0, resolved.Bounds.XMin);
        Assert.Equal(0, resolved.Bounds.YMin);
        Assert.Equal(500, resolved.Bounds.XMax);
        Assert.Equal(500, resolved.Bounds.YMax);
        Assert.False(layout.IsEmpty);
        Assert.Equal(32, layout.TextureWidth);
        Assert.Equal(32, layout.TextureHeight);
        Assert.Equal(layout.TextureWidth * layout.TextureHeight * 4, bitmap.Length);
        MsdfTestField.AssertOpaque(bitmap);
        MsdfTestField.AssertNotConstant(bitmap);

        OutlineContour contour = resolved.Outline.Contours.Span[0];
        ReadOnlySpan<OutlineSegment> segments = contour.Segments.Span;
        Assert.Equal(8, segments.Length);
        Vector2[] pixelVertices = new Vector2[8];
        pixelVertices[0] = layout.ToPixel(contour.Start);
        for (int index = 1; index < pixelVertices.Length; index++) {
            Assert.Equal(OutlineSegmentKind.Line, segments[index - 1].Kind);
            pixelVertices[index] = layout.ToPixel(segments[index - 1].End);
        }

        MsdfTestField.AssertMatchesContours(bitmap, layout, [pixelVertices]);
    }
}
