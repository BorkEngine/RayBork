using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using RayBork.Internal;

namespace RayBork.Tests.Fonts;

/// <summary>
/// Tests the sfnt container parse: the accepted magic values, the validated table directory, the
/// required tables, metric consistency, CFF acceptance, determinism, and thread-safe reads.
/// </summary>
public sealed class SfntFaceContainerTests {
    private const uint TRUETYPE_MAGIC = 0x0001_0000;
    private const uint TTC_MAGIC = 0x7474_6366;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ParsesAValidTrueTypeFace() {
        var builder = SyntheticFontBuilder.Create()
            .WithUnitsPerEm(2048)
            .WithVerticalMetrics(1600, -500, 100);
        int glyph = builder.AddSimple(
            900,
            40,
            new SyntheticPoint(0, 0),
            new SyntheticPoint(600, 0),
            new SyntheticPoint(300, 1400));
        byte[] faceBytes = builder.WithCmap(4, (0x41, glyph)).Build();

        SfntFace face = SfntFace.Parse(faceBytes);

        Assert.Equal(2048, face.UnitsPerEm);
        Assert.Equal(1600, face.Ascender);
        Assert.Equal(-500, face.Descender);
        Assert.Equal(100, face.LineGap);
        Assert.Equal(2, face.GlyphCount);
        Assert.Equal(2, face.NumberOfHMetrics);
        Assert.Equal(SfntOutlineKind.TrueType, face.OutlineKind);
        Assert.Equal(1, face.MappedGlyphCount);
        Assert.Equal(0, SfntFace.NOTDEF_GLYPH_ID);
        Assert.True(face.CffTable.IsEmpty);
        Assert.True(face.Cff2Table.IsEmpty);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ExposesTheValidatedTableDirectory() {
        byte[] faceBytes = CreateDefaultFace();
        SfntFace face = SfntFace.Parse(faceBytes);

        SyntheticTableRecord headRecord = SyntheticFontBuilder.FindTable(faceBytes, "head");
        Assert.True(face.TryGetTable(FontTableTag.Head, out FontTable head));
        Assert.Equal(headRecord.Offset, head.Offset);
        Assert.Equal(headRecord.Length, head.Length);
        Assert.Equal(faceBytes.AsSpan(head.Offset, head.Length).ToArray(), head.Data.ToArray());
        Assert.False(face.TryGetTable(FontTableTag.Kern, out _));
        Assert.False(face.TryGetTable(FontTableTag.Gpos, out _));

        string[] tags = ["head", "maxp", "hhea", "hmtx", "cmap", "loca", "glyf"];
        foreach (string tag in tags) {
            Assert.True(face.TryGetTable(FontTableTag.FromAscii(TagBytes(tag)), out _));
        }
    }

    private static byte[] TagBytes(string tag) => Encoding.ASCII.GetBytes(tag);

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AcceptsTheTrueMagic() {
        byte[] face = CreateDefaultFace();
        BinaryPrimitives.WriteUInt32BigEndian(face, 0x7472_7565);

        SfntFace parsed = SfntFace.Parse(face);

        Assert.Equal(2, parsed.GlyphCount);
        Assert.Equal(SfntOutlineKind.TrueType, parsed.OutlineKind);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsUnsupportedMagic() {
        byte[] face = CreateDefaultFace();
        BinaryPrimitives.WriteUInt32BigEndian(face, 0x1234_5678);

        FontTestData.AssertFaceFailure(face, "is not a supported TrueType");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsTrueTypeCollection() {
        byte[] face = CreateDefaultFace();
        BinaryPrimitives.WriteUInt32BigEndian(face, TTC_MAGIC);

        FontTestData.AssertFaceFailure(face, "TrueType Collection");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsTruncatedOffsetTable() {
        FontTestData.AssertFaceFailure(CreateDefaultFace()[..8], "12-byte sfnt offset table");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsTruncatedTableDirectory() {
        byte[] face = CreateDefaultFace();
        int tableCount = BinaryPrimitives.ReadUInt16BigEndian(face.AsSpan(4));

        FontTestData.AssertFaceFailure(
            face[..(12 + (16 * (tableCount - 1)) + 5)],
            "table directory extends past");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsTableOutsideTheFile() {
        byte[] face = CreateDefaultFace();
        SyntheticTableRecord record = SyntheticFontBuilder.FindTable(face, "glyf");
        BinaryPrimitives.WriteUInt32BigEndian(face.AsSpan(record.RecordOffset + 12), 0x0FFF_FFFF);

        FontTestData.AssertFaceFailure(face, "extends past the");
    }

    [Theory]
    [InlineData("head")]
    [InlineData("maxp")]
    [InlineData("hhea")]
    [InlineData("hmtx")]
    [InlineData("cmap")]
    [InlineData("loca")]
    [InlineData("glyf")]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsMissingRequiredTable(string tag) {
        byte[] face = CreateDefaultBuilder().WithoutTable(tag).Build();

        FontTestData.AssertFaceFailure(face, $"missing the required '{tag}' table");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsZeroGlyphCount() {
        byte[] face = CreateDefaultFace();
        SyntheticTableRecord maxp = SyntheticFontBuilder.FindTable(face, "maxp");
        BinaryPrimitives.WriteUInt16BigEndian(face.AsSpan(maxp.Offset + 4), 0);

        FontTestData.AssertFaceFailure(face, "declares zero glyphs");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(16385)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsUnitsPerEmOutsideTheSupportedRange(int unitsPerEm) {
        byte[] face = CreateDefaultBuilder().WithUnitsPerEm(unitsPerEm).Build();

        FontTestData.AssertFaceFailure(face, "unitsPerEm");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsUnsupportedIndexToLocFormat() {
        byte[] face = CreateDefaultBuilder().WithHeadIndexToLocFormat(2).Build();

        FontTestData.AssertFaceFailure(face, "indexToLocFormat");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsMaxpGlyphCountBeyondLoca() {
        byte[] face = CreateDefaultFace();
        SyntheticTableRecord maxp = SyntheticFontBuilder.FindTable(face, "maxp");
        int glyphCount = BinaryPrimitives.ReadUInt16BigEndian(face.AsSpan(maxp.Offset + 4));
        BinaryPrimitives.WriteUInt16BigEndian(face.AsSpan(maxp.Offset + 4), (ushort)(glyphCount + 1));

        SyntheticTableRecord hmtx = SyntheticFontBuilder.FindTable(face, "hmtx");
        BinaryPrimitives.WriteUInt32BigEndian(
            face.AsSpan(hmtx.RecordOffset + 12),
            (uint)(hmtx.Length + 8));

        FontTestData.AssertFaceFailure(face, "'loca' table");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AcceptsACffFaceStructurally() {
        byte[] cffData = [1, 0, 4, 1, 0, 0, 0, 0];
        var builder = SyntheticFontBuilder.Create();
        int glyph = builder.AddSimple(
            700,
            20,
            new SyntheticPoint(0, 0),
            new SyntheticPoint(500, 0));
        byte[] faceBytes = builder
            .UseCff(cffData)
            .WithCmap(4, (0x41, glyph))
            .Build();

        SfntFace face = SfntFace.Parse(faceBytes);

        Assert.Equal(SfntOutlineKind.Cff, face.OutlineKind);
        Assert.Equal(cffData, face.CffTable.ToArray());
        Assert.True(face.Cff2Table.IsEmpty);
        Assert.False(face.TryGetTable(FontTableTag.Glyf, out _));
        Assert.False(face.TryGetTable(FontTableTag.Loca, out _));
        Assert.Equal(2, face.GlyphCount);
        Assert.Equal(700, face.GetAdvanceWidth(glyph));
        Assert.False(face.TryGetGlyphId(new Rune('B'), out _));
        FontUnsupportedException cff = Assert.Throws<FontUnsupportedException>(() => face.GetGlyph(glyph));
        Assert.Contains("CFF", cff.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AcceptsACff2FaceStructurally() {
        byte[] cff2Data = [1, 0, 4, 1];
        byte[] faceBytes = SyntheticFontBuilder.Create()
            .UseCff2(cff2Data)
            .WithCmap(4, (0x41, 0))
            .Build();

        SfntFace face = SfntFace.Parse(faceBytes);

        Assert.Equal(SfntOutlineKind.Cff2, face.OutlineKind);
        Assert.Equal(cff2Data, face.Cff2Table.ToArray());
        Assert.True(face.CffTable.IsEmpty);
        FontUnsupportedException cff2 = Assert.Throws<FontUnsupportedException>(() => face.GetGlyph(0));
        Assert.Contains("CFF2", cff2.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsACffFaceWithoutACffTable() {
        byte[] face = SyntheticFontBuilder.Create()
            .UseCff([1, 0, 4, 1])
            .WithCmap(4, (0x41, 0))
            .WithoutTable("CFF ")
            .Build();

        FontTestData.AssertFaceFailure(face, "missing the required 'CFF ' or 'CFF2' table");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ParsesShortAndLongLocaFacesIdentically() {
        SfntFace shortFace = SfntFace.Parse(CreateLocaFace(0));
        SfntFace longFace = SfntFace.Parse(CreateLocaFace(1));

        Assert.Equal(shortFace.GlyphCount, longFace.GlyphCount);
        for (int glyphId = 0; glyphId < shortFace.GlyphCount; glyphId++) {
            FontGlyph expected = shortFace.GetGlyph(glyphId);
            FontGlyph actual = longFace.GetGlyph(glyphId);
            Assert.Equal(expected.Bounds.XMin, actual.Bounds.XMin);
            Assert.Equal(expected.Bounds.YMin, actual.Bounds.YMin);
            Assert.Equal(expected.Bounds.XMax, actual.Bounds.XMax);
            Assert.Equal(expected.Bounds.YMax, actual.Bounds.YMax);
            Assert.Equal(expected.Outline.Contours.Length, actual.Outline.Contours.Length);
            for (int index = 0; index < expected.Outline.Contours.Length; index++) {
                Assert.Equal(
                    expected.Outline.Contours.Span[index].Start,
                    actual.Outline.Contours.Span[index].Start);
            }
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ParsingTheSameBytesTwiceIsDeterministic() {
        byte[] faceBytes = CreateRichFace();

        AssertSameFace(SfntFace.Parse(faceBytes), SfntFace.Parse(faceBytes));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ConcurrentReadsReturnTheSameValues() {
        byte[] faceBytes = CreateRichFace();
        SfntFace face = SfntFace.Parse(faceBytes);
        int glyph = 1;
        int left = 1;
        int right = 3;
        int expectedAdvance = face.GetAdvanceWidth(glyph);
        int expectedKerning = face.GetKerning(left, right);
        Vector2 expectedStart = face.GetGlyph(glyph).Outline.Contours.Span[0].Start;

        Parallel.For(0, 64, _ => {
            for (int iteration = 0; iteration < 50; iteration++) {
                Assert.Equal(expectedAdvance, face.GetAdvanceWidth(glyph));
                Assert.Equal(expectedKerning, face.GetKerning(left, right));
                Assert.Equal(expectedStart, face.GetGlyph(glyph).Outline.Contours.Span[0].Start);
                Assert.True(face.TryGetGlyphId(new Rune(0x41), out int mapped));
                Assert.Equal(glyph, mapped);
            }
        });
    }

    private static byte[] CreateDefaultFace() => CreateDefaultBuilder().Build();

    private static SyntheticFontBuilder CreateDefaultBuilder() {
        var builder = SyntheticFontBuilder.Create();
        int glyph = builder.AddSimple(
            600,
            10,
            new SyntheticPoint(0, 0),
            new SyntheticPoint(500, 0),
            new SyntheticPoint(250, 700));
        return builder.WithCmap(4, (0x41, glyph));
    }

    private static byte[] CreateLocaFace(int locaFormat) {
        var builder = SyntheticFontBuilder.Create().WithLocaFormat(locaFormat);
        int first = builder.AddSimple(
            600,
            10,
            new SyntheticPoint(0, 0),
            new SyntheticPoint(500, 0),
            new SyntheticPoint(250, 700));
        int second = builder.AddSimpleContours(
            700,
            20,
            [
                new SyntheticPoint(0, 0),
                new SyntheticPoint(300, 500, onCurve: false),
                new SyntheticPoint(600, 0),
            ],
            [
                new SyntheticPoint(100, 100),
                new SyntheticPoint(200, 100),
                new SyntheticPoint(200, 200),
            ]);
        builder.AddEmpty(300);
        return builder.WithCmap(4, (0x41, first), (0x42, second)).Build();
    }

    private static byte[] CreateRichFace() {
        var builder = SyntheticFontBuilder.Create();
        int triangle = builder.AddSimple(
            600,
            10,
            new SyntheticPoint(0, 0),
            new SyntheticPoint(500, 0),
            new SyntheticPoint(250, 700));
        int curve = builder.AddSimpleContours(
            700,
            20,
            [
                new SyntheticPoint(0, 0),
                new SyntheticPoint(300, 500, onCurve: false),
                new SyntheticPoint(600, 0),
            ],
            [
                new SyntheticPoint(100, 100),
                new SyntheticPoint(200, 100),
                new SyntheticPoint(200, 200),
            ]);
        int composite = builder.AddComposite(
            800,
            30,
            new FontBounds(0, 0, 700, 700),
            SyntheticComponent.Translate(triangle, 100, 50),
            SyntheticComponent.TwoByTwo(curve, 20, 30, 0.5f, 0f, 0f, 0.5f));
        builder.AddEmpty(250);
        builder.WithCmapRecord(3, 1, 4, (0x41, triangle), (0x42, curve), (0x43, composite));
        builder.WithCmapRecord(
            3,
            10,
            12,
            (0x41, triangle),
            (0x42, curve),
            (0x43, composite),
            (0x1F600, composite));
        builder.WithKernPairs((triangle, curve, -40), (curve, composite, 25));
        builder.WithGpos(new SyntheticGpos().AddPair(composite, triangle, -60));
        return builder.Build();
    }

    private static void AssertSameFace(SfntFace expected, SfntFace actual) {
        Assert.Equal(expected.UnitsPerEm, actual.UnitsPerEm);
        Assert.Equal(expected.Ascender, actual.Ascender);
        Assert.Equal(expected.Descender, actual.Descender);
        Assert.Equal(expected.LineGap, actual.LineGap);
        Assert.Equal(expected.GlyphCount, actual.GlyphCount);
        Assert.Equal(expected.NumberOfHMetrics, actual.NumberOfHMetrics);
        Assert.Equal(expected.MappedGlyphCount, actual.MappedGlyphCount);
        Assert.Equal(expected.OutlineKind, actual.OutlineKind);
        Assert.Equal(expected.CmapFormat, actual.CmapFormat);
        string[] tags = ["head", "maxp", "hhea", "hmtx", "cmap", "loca", "glyf", "kern", "GPOS"];
        foreach (string tag in tags) {
            bool expectedPresent = expected.TryGetTable(FontTableTag.FromAscii(TagBytes(tag)), out FontTable expectedTable);
            bool actualPresent = actual.TryGetTable(FontTableTag.FromAscii(TagBytes(tag)), out FontTable actualTable);
            Assert.Equal(expectedPresent, actualPresent);
            if (expectedPresent) {
                Assert.Equal(expectedTable.Tag, actualTable.Tag);
                Assert.Equal(expectedTable.Offset, actualTable.Offset);
                Assert.Equal(expectedTable.Length, actualTable.Length);
            }
        }

        for (int glyphId = 0; glyphId < expected.GlyphCount; glyphId++) {
            Assert.Equal(expected.GetAdvanceWidth(glyphId), actual.GetAdvanceWidth(glyphId));
            Assert.Equal(expected.GetLeftSideBearing(glyphId), actual.GetLeftSideBearing(glyphId));
            AssertSameGlyph(expected.GetGlyph(glyphId), actual.GetGlyph(glyphId));
        }

        foreach (Rune rune in Runes()) {
            bool expectedMapped = expected.TryGetGlyphId(rune, out int expectedGlyph);
            bool actualMapped = actual.TryGetGlyphId(rune, out int actualGlyph);
            Assert.Equal(expectedMapped, actualMapped);
            Assert.Equal(expectedGlyph, actualGlyph);
        }

        for (int left = 0; left < expected.GlyphCount; left++) {
            for (int right = 0; right < expected.GlyphCount; right++) {
                Assert.Equal(expected.GetKerning(left, right), actual.GetKerning(left, right));
            }
        }
    }

    private static void AssertSameGlyph(FontGlyph expected, FontGlyph actual) {
        Assert.Equal(expected.GlyphId, actual.GlyphId);
        Assert.Equal(expected.Bounds.XMin, actual.Bounds.XMin);
        Assert.Equal(expected.Bounds.YMin, actual.Bounds.YMin);
        Assert.Equal(expected.Bounds.XMax, actual.Bounds.XMax);
        Assert.Equal(expected.Bounds.YMax, actual.Bounds.YMax);
        Assert.Equal(expected.Outline.Contours.Length, actual.Outline.Contours.Length);
        for (int index = 0; index < expected.Outline.Contours.Length; index++) {
            OutlineContour expectedContour = expected.Outline.Contours.Span[index];
            OutlineContour actualContour = actual.Outline.Contours.Span[index];
            Assert.Equal(expectedContour.Start, actualContour.Start);
            Assert.Equal(expectedContour.Segments.Length, actualContour.Segments.Length);
            for (int segment = 0; segment < expectedContour.Segments.Length; segment++) {
                OutlineSegment expectedSegment = expectedContour.Segments.Span[segment];
                OutlineSegment actualSegment = actualContour.Segments.Span[segment];
                Assert.Equal(expectedSegment.Kind, actualSegment.Kind);
                Assert.Equal(expectedSegment.Control1, actualSegment.Control1);
                Assert.Equal(expectedSegment.Control2, actualSegment.Control2);
                Assert.Equal(expectedSegment.End, actualSegment.End);
            }
        }
    }

    private static Rune[] Runes() => [
        new Rune('A'),
        new Rune('B'),
        new Rune('C'),
        new Rune(' '),
        new Rune(0x1F600),
        new Rune(0x10FFFF),
    ];
}
