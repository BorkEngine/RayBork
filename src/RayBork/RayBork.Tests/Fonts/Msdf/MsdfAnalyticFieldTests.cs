using System.Numerics;
using RayBork.Internal;

namespace RayBork.Tests.Fonts.Msdf;

/// <summary>
/// Tests the RGBA8 distance field against analytically known shapes: the exact signed distance at
/// every texel centre, the 0.5 edge contract, the range and padding clamp, holes, the top-down row
/// direction, and the flattening tolerance of quadratic and cubic outlines. Texel centres are compared
/// in the same top-down pixel space that the layout exposes, so corners reconstruct the exact
/// nearest-edge distance too; the documented corner caveat is about bilinear filtering between texels,
/// which no test here samples.
/// </summary>
public sealed class MsdfAnalyticFieldTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AnAxisAlignedRectangleFieldMatchesTheExactSignedDistance() {
        MsdfGlyphLayout layout = MsdfGenerator.ComputeLayout(
            new FontBounds(0, 0, 1000, 1000),
            unitsPerEm: 1000);
        OutlinePath outline = MsdfTestShapes.Rectangle(0f, 0f, 1000f, 1000f);
        Vector2[] corners = [
            layout.ToPixel(new Vector2(0f, 0f)),
            layout.ToPixel(new Vector2(1000f, 0f)),
            layout.ToPixel(new Vector2(1000f, 1000f)),
            layout.ToPixel(new Vector2(0f, 1000f)),
        ];

        byte[] bitmap = MsdfGenerator.Generate(outline, layout);

        MsdfTestField.AssertNotConstant(bitmap);
        MsdfTestField.AssertMatchesContours(bitmap, layout, [corners]);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ARightTriangleFieldMatchesTheExactSignedDistance() {
        MsdfGlyphLayout layout = MsdfGenerator.ComputeLayout(
            new FontBounds(0, 0, 600, 600),
            unitsPerEm: 600);
        OutlinePath outline = MsdfTestShapes.RightTriangle(600f, 600f);
        Vector2[] corners = [
            layout.ToPixel(Vector2.Zero),
            layout.ToPixel(new Vector2(600f, 0f)),
            layout.ToPixel(new Vector2(0f, 600f)),
        ];

        byte[] bitmap = MsdfGenerator.Generate(outline, layout);

        MsdfTestField.AssertNotConstant(bitmap);
        MsdfTestField.AssertMatchesContours(bitmap, layout, [corners]);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ARegularPolygonFieldMatchesTheExactSignedDistance() {
        const int sides = 96;
        MsdfGlyphLayout layout = MsdfGenerator.ComputeLayout(
            new FontBounds(100, 100, 900, 900),
            unitsPerEm: 1000);
        OutlinePath outline = MsdfTestShapes.Polygon(sides, 500f, 500f, 400f);
        Vector2[] fontVertices = MsdfTestShapes.PolygonVertices(sides, 500f, 500f, 400f);
        Vector2[] pixelVertices = new Vector2[fontVertices.Length];
        for (int index = 0; index < fontVertices.Length; index++) {
            pixelVertices[index] = layout.ToPixel(fontVertices[index]);
        }

        byte[] bitmap = MsdfGenerator.Generate(outline, layout);

        MsdfTestField.AssertNotConstant(bitmap);
        MsdfTestField.AssertMatchesContours(bitmap, layout, [pixelVertices]);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TexelCentresOnTheOutlineReconstructToTheEdgeValue() {
        MsdfGlyphLayout layout = MsdfGenerator.ComputeLayout(
            new FontBounds(0, 0, 1000, 1000),
            unitsPerEm: 1000);
        float edgeFontX = (2.5f - layout.OriginX) / layout.Scale;
        OutlinePath outline = MsdfTestShapes.Rectangle(edgeFontX, 100f, 1000f, 900f);

        byte[] bitmap = MsdfGenerator.Generate(outline, layout);

        float tolerance = MsdfTestField.Tolerance(layout.Range);
        MsdfTestField.AssertTexelNear(bitmap, layout, 2, 16, 0f, tolerance);
        MsdfTestField.AssertTexelNear(bitmap, layout, 1, 16, -1f, tolerance);
        MsdfTestField.AssertTexelNear(bitmap, layout, 3, 16, 1f, tolerance);

        // A texel one texel outside the outline is already below 0.5 and one inside is above it.
        Assert.True(MsdfTestField.MedianAt(bitmap, layout, 1, 16) < 0.5f);
        Assert.True(MsdfTestField.MedianAt(bitmap, layout, 3, 16) > 0.5f);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TextureRowsRunFromTheOutlineMaximumYDownward() {
        MsdfGlyphLayout layout = MsdfGenerator.ComputeLayout(
            new FontBounds(0, 0, 1000, 1000),
            unitsPerEm: 1000);
        // The glyph is the lower 40% of its box, so in the top-down texture it must occupy the higher
        // row indices. This pins the engine's top-left UV convention against an accidental flip.
        OutlinePath outline = MsdfTestShapes.Rectangle(0f, 0f, 1000f, 400f);

        byte[] bitmap = MsdfGenerator.Generate(outline, layout);

        Assert.True(MsdfTestField.MedianAt(bitmap, layout, 16, 4) < 0.5f);
        Assert.True(MsdfTestField.MedianAt(bitmap, layout, 16, 26) > 0.5f);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ADeepInsideTexelSaturatesNearOneAndAFarOutsideTexelNearZero() {
        MsdfGlyphLayout layout = MsdfGenerator.ComputeLayout(
            new FontBounds(0, 0, 1000, 1000),
            unitsPerEm: 1000);
        float edgeFontX = (2.5f - layout.OriginX) / layout.Scale;
        OutlinePath outline = MsdfTestShapes.Rectangle(edgeFontX, 100f, 1000f, 900f);

        byte[] bitmap = MsdfGenerator.Generate(outline, layout);

        float insideMedian = MsdfTestField.MedianAt(bitmap, layout, 16, 16);
        float outsideMedian = MsdfTestField.MedianAt(bitmap, layout, 0, 0);
        Assert.True(MathF.Abs(insideMedian - 1f) <= MsdfTestField.BYTE_TOLERANCE);
        Assert.True(MathF.Abs(outsideMedian - 0f) <= MsdfTestField.BYTE_TOLERANCE);
        MsdfTestField.AssertTexelNear(
            bitmap,
            layout,
            16,
            16,
            layout.Range * 0.5f,
            MsdfTestField.Tolerance(layout.Range));
        MsdfTestField.AssertTexelNear(
            bitmap,
            layout,
            0,
            0,
            -layout.Range * 0.5f,
            MsdfTestField.Tolerance(layout.Range));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TheRangeScalesTheStoredEdgeDistance() {
        var bounds = new FontBounds(0, 0, 1000, 1000);
        MsdfGlyphLayout narrow = MsdfGenerator.ComputeLayout(bounds, unitsPerEm: 1000, range: 4);
        MsdfGlyphLayout wide = MsdfGenerator.ComputeLayout(bounds, unitsPerEm: 1000, range: 8);
        OutlinePath outline = MsdfTestShapes.Rectangle(0f, 0f, 1000f, 1000f);

        byte[] narrowBitmap = MsdfGenerator.Generate(outline, narrow);
        byte[] wideBitmap = MsdfGenerator.Generate(outline, wide);

        // The texel centre half a texel outside the left outline edge stores an eighth of the range at
        // range 4 and a sixteenth at range 8.
        MsdfTestField.AssertTexelNear(narrowBitmap, narrow, 1, 16, -0.5f, MsdfTestField.Tolerance(narrow.Range));
        MsdfTestField.AssertTexelNear(wideBitmap, wide, 1, 16, -0.5f, MsdfTestField.Tolerance(wide.Range));
        Assert.Equal(96f / 255f, MsdfTestField.MedianAt(narrowBitmap, narrow, 1, 16), 4);
        Assert.Equal(112f / 255f, MsdfTestField.MedianAt(wideBitmap, wide, 1, 16), 4);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void APaddingSmallerThanHalfTheRangeClipsTheFieldAtTheTextureEdge() {
        var bounds = new FontBounds(0, 0, 1000, 1000);
        MsdfGlyphLayout full = MsdfGenerator.ComputeLayout(bounds, unitsPerEm: 1000, range: 4);
        MsdfGlyphLayout clipped = MsdfGenerator.ComputeLayout(bounds, unitsPerEm: 1000, range: 8);
        OutlinePath outline = MsdfTestShapes.Rectangle(0f, 0f, 1000f, 1000f);

        byte[] fullBitmap = MsdfGenerator.Generate(outline, full);
        byte[] clippedBitmap = MsdfGenerator.Generate(outline, clipped);

        // With padding 2 the farthest outside texel centre is 2.12 texels from the outline corner.
        // At range 4 (padding equals half the range) the field saturates at the outside extreme; at
        // range 8 the texture edge clips the outside field before it reaches the range.
        Assert.Equal(0, fullBitmap[0]);
        Assert.InRange(clippedBitmap[0], 59, 61);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AnOutlineThatCoversTheWholeTextureSaturatesInside() {
        MsdfGlyphLayout layout = MsdfGenerator.ComputeLayout(
            new FontBounds(0, 0, 1000, 1000),
            unitsPerEm: 1000);

        // The layout intentionally belongs to a smaller box; every texel is far inside this outline.
        byte[] bitmap = MsdfGenerator.Generate(
            MsdfTestShapes.Rectangle(-5000f, -5000f, 5000f, 5000f),
            layout);

        for (int index = 0; index < bitmap.Length; index += 4) {
            Assert.Equal(255, bitmap[index]);
            Assert.Equal(255, bitmap[index + 1]);
            Assert.Equal(255, bitmap[index + 2]);
            Assert.Equal(255, bitmap[index + 3]);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AnOutlineFarOutsideTheTextureSaturatesOutside() {
        MsdfGlyphLayout layout = MsdfGenerator.ComputeLayout(
            new FontBounds(0, 0, 1000, 1000),
            unitsPerEm: 1000);

        // The layout intentionally belongs to a different outline; every texel is far outside it.
        byte[] bitmap = MsdfGenerator.Generate(
            MsdfTestShapes.Rectangle(5000f, 5000f, 5010f, 5010f),
            layout);

        MsdfTestField.AssertAllOutside(bitmap);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AHoleIsOutsideByTheNonZeroWinding() {
        MsdfGlyphLayout layout = MsdfGenerator.ComputeLayout(
            new FontBounds(0, 0, 1000, 1000),
            unitsPerEm: 1000);
        OutlinePath outline = MsdfTestShapes.RectangleWithHole(
            100f,
            100f,
            900f,
            900f,
            400f,
            400f,
            600f,
            600f);
        Vector2[] outer = [
            layout.ToPixel(new Vector2(100f, 100f)),
            layout.ToPixel(new Vector2(900f, 100f)),
            layout.ToPixel(new Vector2(900f, 900f)),
            layout.ToPixel(new Vector2(100f, 900f)),
        ];
        Vector2[] hole = [
            layout.ToPixel(new Vector2(400f, 400f)),
            layout.ToPixel(new Vector2(400f, 600f)),
            layout.ToPixel(new Vector2(600f, 600f)),
            layout.ToPixel(new Vector2(600f, 400f)),
        ];

        byte[] bitmap = MsdfGenerator.Generate(outline, layout);

        MsdfTestField.AssertMatchesContours(bitmap, layout, [outer, hole]);
        Assert.True(MsdfTestField.MedianAt(bitmap, layout, 15, 15) < 0.5f);
        Assert.True(MsdfTestField.MedianAt(bitmap, layout, 12, 15) > 0.5f);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AQuadraticOutlineStaysWithinTheFlatteningTolerance() {
        MsdfGlyphLayout layout = MsdfGenerator.ComputeLayout(
            new FontBounds(0, 0, 1000, 1000),
            unitsPerEm: 1000);
        OutlinePath outline = MsdfTestShapes.QuadraticDome(100f, 100f, 500f, 900f, 900f, 100f);

        byte[] bitmap = MsdfGenerator.Generate(outline, layout);

        Vector2[][] contours = MsdfTestShapes.SampleContours(outline, layout, samplesPerSegment: 1024);
        MsdfTestField.AssertNotConstant(bitmap);
        MsdfTestField.AssertMatchesContours(
            bitmap,
            layout,
            contours,
            extraTolerance: MsdfOutlineFlattener.FLATTEN_TOLERANCE_PX);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ACubicOutlineStaysWithinTheFlatteningTolerance() {
        MsdfGlyphLayout layout = MsdfGenerator.ComputeLayout(
            new FontBounds(0, 0, 1000, 1000),
            unitsPerEm: 1000);
        OutlinePath outline = MsdfTestShapes.CubicCapsule(200f, 800f, 500f, 300f);

        byte[] bitmap = MsdfGenerator.Generate(outline, layout);

        Vector2[][] contours = MsdfTestShapes.SampleContours(outline, layout, samplesPerSegment: 1024);
        MsdfTestField.AssertNotConstant(bitmap);
        MsdfTestField.AssertMatchesContours(
            bitmap,
            layout,
            contours,
            extraTolerance: MsdfOutlineFlattener.FLATTEN_TOLERANCE_PX);
    }
}
