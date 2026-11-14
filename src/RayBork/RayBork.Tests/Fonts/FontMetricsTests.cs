using System.Buffers.Binary;
using RayBork.Internal;

namespace RayBork.Tests.Fonts;

/// <summary>
/// Tests the validated <c>head</c>/<c>maxp</c>/<c>hhea</c> metrics and the <c>hmtx</c> advance and
/// side-bearing reads, including the <c>numberOfHMetrics</c> boundary, compact trailing bearings,
/// signed values, and the rejected inconsistencies.
/// </summary>
public sealed class FontMetricsTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ReadsHeadHheaAndMaxpMetrics() {
        var builder = SyntheticFontBuilder.Create()
            .WithUnitsPerEm(2048)
            .WithVerticalMetrics(1500, -450, 120);
        int glyph = builder.AddSimple(600, 10, new SyntheticPoint(0, 0), new SyntheticPoint(500, 0));
        SfntFace face = SfntFace.Parse(builder.WithCmap(4, (0x41, glyph)).Build());

        Assert.Equal(2048, face.UnitsPerEm);
        Assert.Equal(1500, face.Ascender);
        Assert.Equal(-450, face.Descender);
        Assert.Equal(120, face.LineGap);
        Assert.Equal(2, face.GlyphCount);
        Assert.Equal(2, face.NumberOfHMetrics);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AdvancesAndBearingsCrossTheLongMetricsBoundary() {
        var builder = SyntheticFontBuilder.Create();
        int notdef = 0;
        int first = AddGlyph(builder, 600, 40);
        int second = AddGlyph(builder, 65500, -300);
        int third = AddGlyph(builder, 700, -20);
        int fourth = AddGlyph(builder, 800, 10);
        int fifth = AddGlyph(builder, 900, 30);
        builder.WithNumberOfHMetrics(3);
        SfntFace face = SfntFace.Parse(builder.WithCmap(4, (0x41, first)).Build());

        Assert.Equal(3, face.NumberOfHMetrics);
        Assert.Equal(500, face.GetAdvanceWidth(notdef));
        Assert.Equal(600, face.GetAdvanceWidth(first));
        Assert.Equal(65500, face.GetAdvanceWidth(second));
        Assert.Equal(65500, face.GetAdvanceWidth(third));
        Assert.Equal(65500, face.GetAdvanceWidth(fourth));
        Assert.Equal(65500, face.GetAdvanceWidth(fifth));
        Assert.Equal(0, face.GetLeftSideBearing(notdef));
        Assert.Equal(40, face.GetLeftSideBearing(first));
        Assert.Equal(-300, face.GetLeftSideBearing(second));
        Assert.Equal(-20, face.GetLeftSideBearing(third));
        Assert.Equal(10, face.GetLeftSideBearing(fourth));
        Assert.Equal(30, face.GetLeftSideBearing(fifth));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TrailingGlyphsInheritTheLastAdvance() {
        var builder = SyntheticFontBuilder.Create();
        int first = AddGlyph(builder, 640, 0);
        int second = AddGlyph(builder, 500, 20);
        int third = AddGlyph(builder, 700, 30);
        builder.WithNumberOfHMetrics(2);
        SfntFace face = SfntFace.Parse(builder.WithCmap(4, (0x41, first)).Build());

        Assert.Equal(640, face.GetAdvanceWidth(first));
        Assert.Equal(640, face.GetAdvanceWidth(second));
        Assert.Equal(640, face.GetAdvanceWidth(third));
        Assert.Equal(20, face.GetLeftSideBearing(second));
        Assert.Equal(30, face.GetLeftSideBearing(third));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsZeroLongHorizontalMetrics() {
        byte[] faceBytes = CreateMetricFace();
        SyntheticTableRecord hhea = SyntheticFontBuilder.FindTable(faceBytes, "hhea");
        BinaryPrimitives.WriteUInt16BigEndian(faceBytes.AsSpan(hhea.Offset + 34), 0);

        FontTestData.AssertFaceFailure(faceBytes, "zero long horizontal metrics");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsLongMetricsBeyondTheGlyphCount() {
        byte[] faceBytes = CreateMetricFace();
        SyntheticTableRecord hhea = SyntheticFontBuilder.FindTable(faceBytes, "hhea");
        SyntheticTableRecord maxp = SyntheticFontBuilder.FindTable(faceBytes, "maxp");
        int glyphCount = BinaryPrimitives.ReadUInt16BigEndian(faceBytes.AsSpan(maxp.Offset + 4));
        BinaryPrimitives.WriteUInt16BigEndian(
            faceBytes.AsSpan(hhea.Offset + 34),
            (ushort)(glyphCount + 1));

        FontTestData.AssertFaceFailure(faceBytes, "long horizontal metrics but 'maxp'");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsTruncatedHorizontalMetrics() {
        byte[] faceBytes = CreateMetricFace();
        SyntheticTableRecord hmtx = SyntheticFontBuilder.FindTable(faceBytes, "hmtx");
        BinaryPrimitives.WriteUInt32BigEndian(faceBytes.AsSpan(hmtx.RecordOffset + 12), 6);

        FontTestData.AssertFaceFailure(faceBytes, "'hmtx' table is");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsGlyphIdentifiersOutsideTheFace() {
        SfntFace face = SfntFace.Parse(CreateMetricFace());

        Assert.Throws<ArgumentOutOfRangeException>(() => face.GetAdvanceWidth(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => face.GetAdvanceWidth(face.GlyphCount));
        Assert.Throws<ArgumentOutOfRangeException>(() => face.GetLeftSideBearing(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => face.GetLeftSideBearing(face.GlyphCount));
    }

    private static int AddGlyph(SyntheticFontBuilder builder, int advance, int leftSideBearing) =>
        builder.AddSimple(
            advance,
            leftSideBearing,
            new SyntheticPoint(0, 0),
            new SyntheticPoint(400, 0),
            new SyntheticPoint(200, 500));

    private static byte[] CreateMetricFace() {
        var builder = SyntheticFontBuilder.Create();
        int first = AddGlyph(builder, 600, 40);
        AddGlyph(builder, 700, -20);
        return builder.WithCmap(4, (0x41, first)).Build();
    }
}
