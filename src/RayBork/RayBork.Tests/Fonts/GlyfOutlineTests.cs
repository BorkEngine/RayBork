using System.Buffers.Binary;
using System.Numerics;
using RayBork.Internal;

namespace RayBork.Tests.Fonts;

/// <summary>
/// Tests TrueType glyf outline resolution: simple contours with quadratic runs, repeat-compressed
/// flags and implied points, composite transforms and nesting, the documented limits, empty glyphs,
/// and corrupt glyph data.
/// </summary>
public sealed class GlyfOutlineTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SimpleTriangleResolvesToClosedLineSegments() {
        var builder = SyntheticFontBuilder.Create();
        int glyph = builder.AddSimple(
            600,
            10,
            new SyntheticPoint(0, 0),
            new SyntheticPoint(500, 0),
            new SyntheticPoint(250, 700));
        SfntFace face = Parse(builder, glyph);

        FontGlyph resolved = face.GetGlyph(glyph);

        Assert.Equal(0, resolved.Bounds.XMin);
        Assert.Equal(0, resolved.Bounds.YMin);
        Assert.Equal(500, resolved.Bounds.XMax);
        Assert.Equal(700, resolved.Bounds.YMax);
        Assert.Equal(1, resolved.Outline.Contours.Length);
        OutlineContour contour = resolved.Outline.Contours.Span[0];
        Assert.Equal(new Vector2(0, 0), contour.Start);
        Assert.Equal(3, contour.Segments.Length);
        AssertSegment(contour.Segments.Span[0], OutlineSegmentKind.Line, default, default, new Vector2(500, 0));
        AssertSegment(contour.Segments.Span[1], OutlineSegmentKind.Line, default, default, new Vector2(250, 700));
        AssertSegment(contour.Segments.Span[2], OutlineSegmentKind.Line, default, default, new Vector2(0, 0));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SimpleQuadraticCurveResolvesWithAClosingLine() {
        var builder = SyntheticFontBuilder.Create();
        int glyph = builder.AddSimple(
            600,
            10,
            new SyntheticPoint(0, 0),
            new SyntheticPoint(300, 500, onCurve: false),
            new SyntheticPoint(600, 0));
        SfntFace face = Parse(builder, glyph);

        OutlineContour contour = face.GetGlyph(glyph).Outline.Contours.Span[0];

        Assert.Equal(new Vector2(0, 0), contour.Start);
        Assert.Equal(2, contour.Segments.Length);
        AssertSegment(
            contour.Segments.Span[0],
            OutlineSegmentKind.Quadratic,
            new Vector2(300, 500),
            default,
            new Vector2(600, 0));
        AssertSegment(contour.Segments.Span[1], OutlineSegmentKind.Line, default, default, new Vector2(0, 0));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ConsecutiveOffCurvePointsUseImpliedMidpoints() {
        var builder = SyntheticFontBuilder.Create();
        int glyph = builder.AddSimple(
            600,
            10,
            new SyntheticPoint(0, 0),
            new SyntheticPoint(200, 300, onCurve: false),
            new SyntheticPoint(400, -100, onCurve: false),
            new SyntheticPoint(600, 0));
        SfntFace face = Parse(builder, glyph);

        OutlineContour contour = face.GetGlyph(glyph).Outline.Contours.Span[0];

        Assert.Equal(new Vector2(0, 0), contour.Start);
        Assert.Equal(3, contour.Segments.Length);
        AssertSegment(
            contour.Segments.Span[0],
            OutlineSegmentKind.Quadratic,
            new Vector2(200, 300),
            default,
            new Vector2(300, 100));
        AssertSegment(
            contour.Segments.Span[1],
            OutlineSegmentKind.Quadratic,
            new Vector2(400, -100),
            default,
            new Vector2(600, 0));
        AssertSegment(contour.Segments.Span[2], OutlineSegmentKind.Line, default, default, new Vector2(0, 0));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AllOffCurveContourUsesAnImpliedStartPoint() {
        var builder = SyntheticFontBuilder.Create();
        int glyph = builder.AddSimple(
            600,
            10,
            new SyntheticPoint(0, 0, onCurve: false),
            new SyntheticPoint(300, 400, onCurve: false),
            new SyntheticPoint(600, 0, onCurve: false));
        SfntFace face = Parse(builder, glyph);

        OutlineContour contour = face.GetGlyph(glyph).Outline.Contours.Span[0];

        Assert.Equal(new Vector2(300, 0), contour.Start);
        Assert.Equal(3, contour.Segments.Length);
        AssertSegment(
            contour.Segments.Span[0],
            OutlineSegmentKind.Quadratic,
            new Vector2(0, 0),
            default,
            new Vector2(150, 200));
        AssertSegment(
            contour.Segments.Span[1],
            OutlineSegmentKind.Quadratic,
            new Vector2(300, 400),
            default,
            new Vector2(450, 200));
        AssertSegment(
            contour.Segments.Span[2],
            OutlineSegmentKind.Quadratic,
            new Vector2(600, 0),
            default,
            new Vector2(300, 0));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ContourWrapsToItsFirstOnCurvePoint() {
        var builder = SyntheticFontBuilder.Create();
        int glyph = builder.AddSimple(
            800,
            10,
            new SyntheticPoint(400, 0, onCurve: false),
            new SyntheticPoint(600, 0),
            new SyntheticPoint(600, 400),
            new SyntheticPoint(0, 400),
            new SyntheticPoint(0, 0));
        SfntFace face = Parse(builder, glyph);

        OutlineContour contour = face.GetGlyph(glyph).Outline.Contours.Span[0];

        Assert.Equal(new Vector2(600, 0), contour.Start);
        Assert.Equal(4, contour.Segments.Length);
        AssertSegment(contour.Segments.Span[0], OutlineSegmentKind.Line, default, default, new Vector2(600, 400));
        AssertSegment(contour.Segments.Span[1], OutlineSegmentKind.Line, default, default, new Vector2(0, 400));
        AssertSegment(contour.Segments.Span[2], OutlineSegmentKind.Line, default, default, new Vector2(0, 0));
        AssertSegment(
            contour.Segments.Span[3],
            OutlineSegmentKind.Quadratic,
            new Vector2(400, 0),
            default,
            new Vector2(600, 0));
        Assert.Equal(contour.Start, contour.Segments.Span[^1].End);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RepeatCompressedFlagsAreDecoded() {
        var builder = SyntheticFontBuilder.Create();
        SyntheticPoint[] points = new SyntheticPoint[10];
        for (int index = 0; index < points.Length; index++) {
            points[index] = new SyntheticPoint(index, index);
        }

        int glyph = builder.AddSimple(600, 0, points);
        SfntFace face = Parse(builder, glyph);

        OutlineContour contour = face.GetGlyph(glyph).Outline.Contours.Span[0];

        Assert.Equal(new Vector2(0, 0), contour.Start);
        Assert.Equal(points.Length, contour.Segments.Length);
        for (int index = 0; index < points.Length - 1; index++) {
            Assert.Equal(new Vector2(index + 1, index + 1), contour.Segments.Span[index].End);
        }

        Assert.Equal(contour.Start, contour.Segments.Span[^1].End);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EmptyGlyphResolvesToAnEmptyPath() {
        var builder = SyntheticFontBuilder.Create();
        int glyph = builder.AddEmpty(300);
        SfntFace face = Parse(builder, glyph);

        FontGlyph resolved = face.GetGlyph(glyph);

        Assert.True(resolved.IsEmpty);
        Assert.True(resolved.Outline.Contours.IsEmpty);
        Assert.Equal(0, resolved.Bounds.XMin);
        Assert.Equal(0, resolved.Bounds.YMin);
        Assert.Equal(0, resolved.Bounds.XMax);
        Assert.Equal(0, resolved.Bounds.YMax);
        Assert.Equal(300, face.GetAdvanceWidth(glyph));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ZeroContourGlyphWithHeaderBoundsHasZeroBounds() {
        var builder = SyntheticFontBuilder.Create();
        int glyph = AddTriangle(builder);
        byte[] faceBytes = builder.WithCmap(4, (0x41, glyph)).Build();
        BinaryPrimitives.WriteInt16BigEndian(
            faceBytes.AsSpan(GlyphStart(faceBytes, glyph)),
            0);
        SfntFace face = SfntFace.Parse(faceBytes);

        FontGlyph resolved = face.GetGlyph(glyph);

        Assert.True(resolved.IsEmpty);
        Assert.Equal(0, resolved.Bounds.XMin);
        Assert.Equal(0, resolved.Bounds.YMin);
        Assert.Equal(0, resolved.Bounds.XMax);
        Assert.Equal(0, resolved.Bounds.YMax);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CompositeOfEmptyGlyphsHasZeroBounds() {
        var builder = SyntheticFontBuilder.Create();
        int empty = builder.AddEmpty(300);
        int glyph = builder.AddComposite(
            700,
            10,
            new FontBounds(0, 0, 500, 700),
            SyntheticComponent.Translate(empty, 20, 30));
        SfntFace face = Parse(builder, glyph);

        FontGlyph resolved = face.GetGlyph(glyph);

        Assert.True(resolved.IsEmpty);
        Assert.Equal(0, resolved.Bounds.XMin);
        Assert.Equal(0, resolved.Bounds.YMin);
        Assert.Equal(0, resolved.Bounds.XMax);
        Assert.Equal(0, resolved.Bounds.YMax);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CompositeComponentGlyphOutsideTheFaceIsRejected() {
        var builder = SyntheticFontBuilder.Create();
        AddTriangle(builder);
        int glyph = builder.AddComposite(
            700,
            10,
            SyntheticComponent.Translate(100, 0, 0));
        SfntFace face = Parse(builder, glyph);

        FontTestData.AssertGlyphFailure(face, glyph, "references glyph 100");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CompositeGlyphAppliesComponentOffsets() {
        var builder = SyntheticFontBuilder.Create();
        int baseGlyph = AddTriangle(builder);
        int glyph = builder.AddComposite(
            700,
            10,
            new FontBounds(200, 300, 700, 1000),
            SyntheticComponent.Translate(baseGlyph, 200, 300));
        SfntFace face = Parse(builder, glyph);

        FontGlyph resolved = face.GetGlyph(glyph);

        Assert.Equal(200, resolved.Bounds.XMin);
        Assert.Equal(300, resolved.Bounds.YMin);
        Assert.Equal(700, resolved.Bounds.XMax);
        Assert.Equal(1000, resolved.Bounds.YMax);
        Assert.Equal(1, resolved.Outline.Contours.Length);
        OutlineContour contour = resolved.Outline.Contours.Span[0];
        Assert.Equal(new Vector2(200, 300), contour.Start);
        AssertSegment(contour.Segments.Span[0], OutlineSegmentKind.Line, default, default, new Vector2(700, 300));
        AssertSegment(contour.Segments.Span[1], OutlineSegmentKind.Line, default, default, new Vector2(450, 1000));
        AssertSegment(contour.Segments.Span[2], OutlineSegmentKind.Line, default, default, new Vector2(200, 300));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CompositeGlyphAppliesAUniformScale() {
        var builder = SyntheticFontBuilder.Create();
        int baseGlyph = AddTriangle(builder);
        int glyph = builder.AddComposite(
            700,
            10,
            SyntheticComponent.Scale(baseGlyph, 10, 20, 0.5f));
        SfntFace face = Parse(builder, glyph);

        OutlineContour contour = face.GetGlyph(glyph).Outline.Contours.Span[0];

        Assert.Equal(new Vector2(10, 20), contour.Start);
        AssertSegment(contour.Segments.Span[0], OutlineSegmentKind.Line, default, default, new Vector2(260, 20));
        AssertSegment(contour.Segments.Span[1], OutlineSegmentKind.Line, default, default, new Vector2(135, 370));
        AssertSegment(contour.Segments.Span[2], OutlineSegmentKind.Line, default, default, new Vector2(10, 20));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CompositeGlyphAppliesIndependentAxisScales() {
        var builder = SyntheticFontBuilder.Create();
        int baseGlyph = AddTriangle(builder);
        int glyph = builder.AddComposite(
            700,
            10,
            SyntheticComponent.ScaleXAndY(baseGlyph, 10, 20, 1.5f, 0.25f));
        SfntFace face = Parse(builder, glyph);

        OutlineContour contour = face.GetGlyph(glyph).Outline.Contours.Span[0];

        Assert.Equal(new Vector2(10, 20), contour.Start);
        AssertSegment(contour.Segments.Span[0], OutlineSegmentKind.Line, default, default, new Vector2(760, 20));
        AssertSegment(contour.Segments.Span[1], OutlineSegmentKind.Line, default, default, new Vector2(385, 195));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CompositeGlyphReadsEightBitComponentArguments() {
        var builder = SyntheticFontBuilder.Create();
        int baseGlyph = AddTriangle(builder);
        int glyph = builder.AddComposite(
            700,
            10,
            SyntheticComponent.ByteArguments(baseGlyph, 10, 20));
        SfntFace face = Parse(builder, glyph);

        OutlineContour contour = face.GetGlyph(glyph).Outline.Contours.Span[0];

        Assert.Equal(new Vector2(10, 20), contour.Start);
        AssertSegment(contour.Segments.Span[0], OutlineSegmentKind.Line, default, default, new Vector2(510, 20));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CompositeGlyphAppliesTwoByTwoTransform() {
        var builder = SyntheticFontBuilder.Create();
        int baseGlyph = AddTriangle(builder);
        int glyph = builder.AddComposite(
            700,
            10,
            SyntheticComponent.TwoByTwo(baseGlyph, 10, 20, 0f, 1f, -1f, 0f));
        SfntFace face = Parse(builder, glyph);

        OutlineContour contour = face.GetGlyph(glyph).Outline.Contours.Span[0];

        Assert.Equal(new Vector2(10, 20), contour.Start);
        AssertSegment(contour.Segments.Span[0], OutlineSegmentKind.Line, default, default, new Vector2(10, 520));
        AssertSegment(contour.Segments.Span[1], OutlineSegmentKind.Line, default, default, new Vector2(-690, 270));
        AssertSegment(contour.Segments.Span[2], OutlineSegmentKind.Line, default, default, new Vector2(10, 20));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CompositeGlyphScalesTheOffsetWhenFlagged() {
        var builder = SyntheticFontBuilder.Create();
        int baseGlyph = AddTriangle(builder);
        int glyph = builder.AddComposite(
            700,
            10,
            SyntheticComponent.TwoByTwoScaledOffsets(baseGlyph, 10, 0, 0f, 1f, -1f, 0f));
        SfntFace face = Parse(builder, glyph);

        OutlineContour contour = face.GetGlyph(glyph).Outline.Contours.Span[0];

        Assert.Equal(new Vector2(0, 10), contour.Start);
        Assert.Equal(new Vector2(0, 10), contour.Segments.Span[^1].End);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CompositeGlyphConcatenatesContoursInComponentOrder() {
        var builder = SyntheticFontBuilder.Create();
        int baseGlyph = AddTriangle(builder);
        int glyph = builder.AddComposite(
            700,
            10,
            SyntheticComponent.Translate(baseGlyph, 0, 0),
            SyntheticComponent.Translate(baseGlyph, 1000, 0));
        SfntFace face = Parse(builder, glyph);

        FontGlyph resolved = face.GetGlyph(glyph);

        Assert.Equal(2, resolved.Outline.Contours.Length);
        Assert.Equal(new Vector2(0, 0), resolved.Outline.Contours.Span[0].Start);
        Assert.Equal(new Vector2(1000, 0), resolved.Outline.Contours.Span[1].Start);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void NestedCompositesApplyEveryLevel() {
        var builder = SyntheticFontBuilder.Create();
        int baseGlyph = AddTriangle(builder);
        int first = builder.AddComposite(700, 10, SyntheticComponent.Translate(baseGlyph, 100, 0));
        int second = builder.AddComposite(700, 10, SyntheticComponent.Translate(first, 0, 50));
        SfntFace face = Parse(builder, second);

        OutlineContour contour = face.GetGlyph(second).Outline.Contours.Span[0];

        Assert.Equal(new Vector2(100, 50), contour.Start);
        Assert.Equal(new Vector2(600, 50), contour.Segments.Span[0].End);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CompositeDepthLimitAllowsSixteenLevels() {
        (SfntFace face, int[] chain) = CreateCompositeChain();
        int deepest = chain[15];

        FontGlyph resolved = face.GetGlyph(deepest);

        Assert.Equal(1, resolved.Outline.Contours.Length);
        Assert.Equal(new Vector2(160, 0), resolved.Outline.Contours.Span[0].Start);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CompositeDepthLimitRejectsSeventeenLevels() {
        (SfntFace face, int[] chain) = CreateCompositeChain();

        FontTestData.AssertGlyphFailure(face, chain[16], "nests more than 16 composite levels");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CompositeReferenceCycleIsRejected() {
        byte[] faceBytes = CreateCyclicComposite(out int cycleGlyph);
        SfntFace face = SfntFace.Parse(faceBytes);

        FontTestData.AssertGlyphFailure(face, cycleGlyph, "reference cycle");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CompositeSelfReferenceIsRejected() {
        var builder = SyntheticFontBuilder.Create();
        int baseGlyph = AddTriangle(builder);
        int glyph = builder.AddComposite(700, 10, SyntheticComponent.Translate(baseGlyph, 10, 0));
        byte[] faceBytes = builder.WithCmap(4, (0x41, glyph)).Build();
        WriteComponentGlyph(faceBytes, glyph, glyph);
        SfntFace face = SfntFace.Parse(faceBytes);

        FontTestData.AssertGlyphFailure(face, glyph, "reference cycle");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CompositePointMatchedArgumentsAreRejected() {
        var builder = SyntheticFontBuilder.Create();
        int baseGlyph = AddTriangle(builder);
        int glyph = builder.AddComposite(700, 10, SyntheticComponent.PointMatch(baseGlyph));
        SfntFace face = Parse(builder, glyph);

        FontTestData.AssertGlyphUnsupported(face, glyph, "point-matched component arguments");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SimpleGlyphAtThePointLimitResolves() {
        var builder = SyntheticFontBuilder.Create();
        int glyph = builder.AddSimple(600, 0, CreatePoints(GlyphOutlineReader.MAX_POINTS_PER_GLYPH));
        SfntFace face = Parse(builder, glyph);

        FontGlyph resolved = face.GetGlyph(glyph);

        Assert.Equal(1, resolved.Outline.Contours.Length);
        Assert.Equal(
            GlyphOutlineReader.MAX_POINTS_PER_GLYPH,
            resolved.Outline.Contours.Span[0].Segments.Length);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SimpleGlyphPastThePointLimitIsRejected() {
        var builder = SyntheticFontBuilder.Create();
        int glyph = builder.AddSimple(600, 0, CreatePoints(GlyphOutlineReader.MAX_POINTS_PER_GLYPH + 1));
        SfntFace face = Parse(builder, glyph);

        FontTestData.AssertGlyphFailure(face, glyph, "more than 20000 points");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CompositeTotalPointLimitIsRejected() {
        var builder = SyntheticFontBuilder.Create();
        int first = builder.AddSimple(600, 0, CreatePoints(10001));
        int second = builder.AddSimple(600, 0, CreatePoints(10001));
        int glyph = builder.AddComposite(
            700,
            10,
            SyntheticComponent.Translate(first, 0, 0),
            SyntheticComponent.Translate(second, 100, 0));
        SfntFace face = Parse(builder, glyph);

        FontTestData.AssertGlyphFailure(face, glyph, "more than 20000 points");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TruncatedGlyphDataIsRejected() {
        var builder = SyntheticFontBuilder.Create();
        int glyph = AddTriangle(builder);
        byte[] faceBytes = builder.WithCmap(4, (0x41, glyph)).Build();
        BinaryPrimitives.WriteUInt16BigEndian(
            faceBytes.AsSpan(GlyphStart(faceBytes, glyph) + 10),
            19_999);
        SfntFace face = SfntFace.Parse(faceBytes);

        FontTestData.AssertGlyphFailure(face, glyph, "truncated");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GlyphIdentifiersOutsideTheFaceAreRejected() {
        var builder = SyntheticFontBuilder.Create();
        int glyph = AddTriangle(builder);
        SfntFace face = Parse(builder, glyph);

        Assert.Throws<ArgumentOutOfRangeException>(() => face.GetGlyph(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => face.GetGlyph(face.GlyphCount));
    }

    private static SfntFace Parse(SyntheticFontBuilder builder, int glyph) =>
        SfntFace.Parse(builder.WithCmap(4, (0x41, glyph)).Build());

    private static int AddTriangle(SyntheticFontBuilder builder) => builder.AddSimple(
        600,
        10,
        new SyntheticPoint(0, 0),
        new SyntheticPoint(500, 0),
        new SyntheticPoint(250, 700));

    private static (SfntFace Face, int[] Chain) CreateCompositeChain() {
        var builder = SyntheticFontBuilder.Create();
        int baseGlyph = AddTriangle(builder);
        int[] chain = new int[17];
        int previous = baseGlyph;
        for (int level = 0; level < chain.Length; level++) {
            chain[level] = builder.AddComposite(700, 10, SyntheticComponent.Translate(previous, 10, 0));
            previous = chain[level];
        }

        return (Parse(builder, chain[16]), chain);
    }

    private static byte[] CreateCyclicComposite(out int cycleGlyph) {
        var builder = SyntheticFontBuilder.Create();
        int baseGlyph = AddTriangle(builder);
        int first = builder.AddComposite(700, 10, SyntheticComponent.Translate(baseGlyph, 10, 0));
        int second = builder.AddComposite(700, 10, SyntheticComponent.Translate(first, 20, 0));
        byte[] faceBytes = builder.WithCmap(4, (0x41, second)).Build();
        WriteComponentGlyph(faceBytes, first, second);
        cycleGlyph = second;
        return faceBytes;
    }

    private static void WriteComponentGlyph(byte[] faceBytes, int glyphId, int componentGlyph) {
        BinaryPrimitives.WriteUInt16BigEndian(
            faceBytes.AsSpan(GlyphStart(faceBytes, glyphId) + 12),
            (ushort)componentGlyph);
    }

    private static int GlyphStart(byte[] faceBytes, int glyphId) {
        SyntheticTableRecord loca = SyntheticFontBuilder.FindTable(faceBytes, "loca");
        SyntheticTableRecord glyf = SyntheticFontBuilder.FindTable(faceBytes, "glyf");
        SyntheticTableRecord head = SyntheticFontBuilder.FindTable(faceBytes, "head");
        int indexToLocFormat = BinaryPrimitives.ReadInt16BigEndian(faceBytes.AsSpan(head.Offset + 50));
        int offset = indexToLocFormat == 0
            ? BinaryPrimitives.ReadUInt16BigEndian(faceBytes.AsSpan(loca.Offset + (2 * glyphId))) * 2
            : (int)BinaryPrimitives.ReadUInt32BigEndian(faceBytes.AsSpan(loca.Offset + (4 * glyphId)));
        return glyf.Offset + offset;
    }

    private static SyntheticPoint[] CreatePoints(int count) {
        SyntheticPoint[] points = new SyntheticPoint[count];
        for (int index = 0; index < count; index++) {
            points[index] = new SyntheticPoint(index, 0);
        }

        return points;
    }

    private static void AssertSegment(
        OutlineSegment segment,
        OutlineSegmentKind kind,
        Vector2 control1,
        Vector2 control2,
        Vector2 end) {
        Assert.Equal(kind, segment.Kind);
        Assert.Equal(control1, segment.Control1);
        Assert.Equal(control2, segment.Control2);
        Assert.Equal(end, segment.End);
    }
}
