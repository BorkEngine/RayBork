using System.Buffers.Binary;
using System.Text;
using RayBork.Internal;

namespace RayBork.Tests.Fonts;

/// <summary>
/// Tests the cmap subtable selection and lookup for formats 0, 4, 6, and 12, including the
/// deterministic preference order, supplementary planes, unmapped codepoints, <c>.notdef</c>, the
/// mapped-glyph count, and allocation-free lookups.
/// </summary>
public sealed class CmapTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void Format0MapsByteCodepoints() {
        (SyntheticFontBuilder builder, int first, int second) = CreateMappableBuilder();
        byte[] faceBytes = builder.WithCmap(0, (0x41, first), (0x42, second)).Build();
        SfntFace face = SfntFace.Parse(faceBytes);

        Assert.Equal(0, face.CmapFormat);
        AssertMapped(face, 'A', first);
        AssertMapped(face, 'B', second);
        AssertUnmapped(face, 'C');
        AssertUnmapped(face, 0x100);
        Assert.Equal(2, face.MappedGlyphCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void Format4MapsBmpCodepoints() {
        (SyntheticFontBuilder builder, int first, int second) = CreateMappableBuilder();
        byte[] faceBytes = builder
            .WithCmap(4, (0x20, first), (0x41, second), (0x42, second))
            .Build();
        SfntFace face = SfntFace.Parse(faceBytes);

        Assert.Equal(4, face.CmapFormat);
        AssertMapped(face, ' ', first);
        AssertMapped(face, 'A', second);
        AssertMapped(face, 'B', second);
        AssertUnmapped(face, 'C');
        AssertUnmapped(face, 0xFFFE);
        AssertUnmapped(face, 0x1F600);
        Assert.Equal(3, face.MappedGlyphCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void Format6MapsContiguousRanges() {
        (SyntheticFontBuilder builder, int first, int second) = CreateMappableBuilder();
        byte[] faceBytes = builder
            .WithCmap(6, (0x30, first), (0x31, second), (0x33, first))
            .Build();
        SfntFace face = SfntFace.Parse(faceBytes);

        Assert.Equal(6, face.CmapFormat);
        AssertMapped(face, '0', first);
        AssertMapped(face, '1', second);
        AssertMapped(face, '3', first);
        AssertUnmapped(face, '2');
        AssertUnmapped(face, '4');
        Assert.Equal(3, face.MappedGlyphCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void Format12MapsSupplementaryPlanes() {
        (SyntheticFontBuilder builder, int first, int second) = CreateMappableBuilder();
        byte[] faceBytes = builder
            .WithCmap(12, (0x41, first), (0x1F600, second), (0x10FFFF, first))
            .Build();
        SfntFace face = SfntFace.Parse(faceBytes);

        Assert.Equal(12, face.CmapFormat);
        AssertMapped(face, 'A', first);
        AssertMapped(face, 0x1F600, second);
        AssertMapped(face, 0x10FFFF, first);
        AssertUnmapped(face, 0x1F601);
        AssertUnmapped(face, 0x10FFFE);
        Assert.Equal(3, face.MappedGlyphCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void Format4MapsConsecutiveRuns() {
        (SyntheticFontBuilder builder, int first, int second) = CreateMappableBuilder();
        int third = builder.AddSimple(
            800,
            30,
            new SyntheticPoint(0, 0),
            new SyntheticPoint(700, 0),
            new SyntheticPoint(350, 900));
        byte[] faceBytes = builder
            .WithCmap(4, (0x41, first), (0x42, second), (0x43, third), (0x60, first))
            .Build();
        SfntFace face = SfntFace.Parse(faceBytes);

        Assert.Equal(4, face.CmapFormat);
        AssertMapped(face, 'A', first);
        AssertMapped(face, 'B', second);
        AssertMapped(face, 'C', third);
        AssertMapped(face, '`', first);
        AssertUnmapped(face, 'D');
        Assert.Equal(4, face.MappedGlyphCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void Format12GroupsCrossTheBmpBoundary() {
        (SyntheticFontBuilder builder, int first, int second) = CreateMappableBuilder();
        int third = builder.AddSimple(
            800,
            30,
            new SyntheticPoint(0, 0),
            new SyntheticPoint(700, 0),
            new SyntheticPoint(350, 900));
        byte[] faceBytes = builder
            .WithCmap(
                12,
                (0xFFFD, first),
                (0xFFFE, second),
                (0xFFFF, third),
                (0x10000, first),
                (0x10001, second))
            .Build();
        SfntFace face = SfntFace.Parse(faceBytes);

        Assert.Equal(12, face.CmapFormat);
        AssertMapped(face, 0xFFFD, first);
        AssertMapped(face, 0xFFFE, second);
        AssertMapped(face, 0xFFFF, third);
        AssertMapped(face, 0x10000, first);
        AssertMapped(face, 0x10001, second);
        AssertUnmapped(face, 0xFFFC);
        AssertUnmapped(face, 0x10002);
        Assert.Equal(5, face.MappedGlyphCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void Format12MappedGlyphCountExcludesSurrogates() {
        (SyntheticFontBuilder builder, int first, int second) = CreateMappableBuilder();
        int third = builder.AddSimple(
            800,
            30,
            new SyntheticPoint(0, 0),
            new SyntheticPoint(700, 0),
            new SyntheticPoint(350, 900));
        byte[] faceBytes = builder
            .WithCmap(12, (0xD7FF, first), (0xD800, second), (0xE000, third))
            .Build();
        SfntFace face = SfntFace.Parse(faceBytes);

        Assert.Equal(2, face.MappedGlyphCount);
        AssertMapped(face, 0xD7FF, first);
        AssertMapped(face, 0xE000, third);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PrefersFormat12OverFormat4() {
        (SyntheticFontBuilder builder, int first, int second) = CreateMappableBuilder();
        byte[] faceBytes = builder
            .WithCmapRecord(3, 1, 4, (0x41, first))
            .WithCmapRecord(3, 10, 12, (0x41, second))
            .Build();
        SfntFace face = SfntFace.Parse(faceBytes);

        Assert.Equal(12, face.CmapFormat);
        AssertMapped(face, 'A', second);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PrefersFormat4OverFormat6() {
        (SyntheticFontBuilder builder, int first, int second) = CreateMappableBuilder();
        byte[] faceBytes = builder
            .WithCmapRecord(3, 1, 6, (0x41, first))
            .WithCmapRecord(3, 1, 4, (0x41, second))
            .Build();
        SfntFace face = SfntFace.Parse(faceBytes);

        Assert.Equal(4, face.CmapFormat);
        AssertMapped(face, 'A', second);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PrefersFormat6OverFormat0() {
        (SyntheticFontBuilder builder, int first, int second) = CreateMappableBuilder();
        byte[] faceBytes = builder
            .WithCmapRecord(0, 3, 0, (0x41, first))
            .WithCmapRecord(3, 1, 6, (0x41, second))
            .Build();
        SfntFace face = SfntFace.Parse(faceBytes);

        Assert.Equal(6, face.CmapFormat);
        AssertMapped(face, 'A', second);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void FallsBackToALegacySubtable() {
        (SyntheticFontBuilder builder, int first, int _) = CreateMappableBuilder();
        byte[] faceBytes = builder
            .WithCmapRecord(1, 0, 0, (0x41, first))
            .Build();
        SfntFace face = SfntFace.Parse(faceBytes);

        Assert.Equal(0, face.CmapFormat);
        AssertMapped(face, 'A', first);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsAFaceWithoutAUsableSubtable() {
        byte[] cmap = [
            0, 0,
            0, 1,
            0, 3,
            0, 1,
            0, 0, 0, 12,
            0, 2,
        ];
        byte[] face = CreateMappableBuilder().Builder.WithRawCmap(cmap).Build();

        FontTestData.AssertFaceFailure(face, "no supported subtable");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MappedGlyphCountExcludesGlyphZeroAndOutOfRangeIds() {
        (SyntheticFontBuilder builder, int first, int _) = CreateMappableBuilder();
        byte[] faceBytes = builder
            .WithCmap(4, (0x41, 0), (0x42, first), (0x43, 0x7FFF))
            .Build();
        SfntFace face = SfntFace.Parse(faceBytes);

        Assert.Equal(1, face.MappedGlyphCount);
        AssertUnmapped(face, 'A');
        AssertMapped(face, 'B', first);
        AssertUnmapped(face, 'C');
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GlyphZeroIsNotdef() {
        (SyntheticFontBuilder builder, int first, int _) = CreateMappableBuilder();
        byte[] faceBytes = builder.WithCmap(4, (0x41, first)).Build();
        SfntFace face = SfntFace.Parse(faceBytes);

        Assert.Equal(0, SfntFace.NOTDEF_GLYPH_ID);
        Assert.False(face.TryGetGlyphId(new Rune('Z'), out int glyphId));
        Assert.Equal(0, glyphId);
        FontGlyph notdef = face.GetGlyph(SfntFace.NOTDEF_GLYPH_ID);
        Assert.False(notdef.IsEmpty);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void LookupsAllocateNothing() {
        (SyntheticFontBuilder builder, int first, int second) = CreateMappableBuilder();
        byte[] faceBytes = builder
            .WithCmap(12, (0x41, first), (0x1F600, second), (0x10FFFF, first))
            .Build();
        SfntFace face = SfntFace.Parse(faceBytes);

        int mapped = 0;
        for (int iteration = 0; iteration < 100; iteration++) {
            if (face.TryGetGlyphId(new Rune(0x1F600), out int glyph)) {
                mapped += glyph;
            }
        }

        Assert.NotEqual(0, mapped);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 10_000; iteration++) {
            face.TryGetGlyphId(new Rune(0x41 + (iteration % 4)), out _);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void Format4IndirectGlyphArrayMapsCodepoints() {
        (SyntheticFontBuilder builder, int first, int second) = CreateMappableBuilder();
        int third = AddGlyph(builder, 800);
        byte[] faceBytes = builder
            .WithCmapIndirectGlyphArray((0x41, first), (0x42, second), (0x43, third))
            .Build();
        SfntFace face = SfntFace.Parse(faceBytes);

        Assert.Equal(4, face.CmapFormat);
        AssertMapped(face, 'A', first);
        AssertMapped(face, 'B', second);
        AssertMapped(face, 'C', third);
        AssertUnmapped(face, 'D');
        Assert.Equal(3, face.MappedGlyphCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void Format4IndirectGlyphArrayOutsideTheSubtableIsUnmapped() {
        (SyntheticFontBuilder builder, int first, int second) = CreateMappableBuilder();
        byte[] faceBytes = builder
            .WithCmapIndirectGlyphArray((0x41, first), (0x42, second))
            .Build();
        SyntheticTableRecord cmap = SyntheticFontBuilder.FindTable(faceBytes, "cmap");
        BinaryPrimitives.WriteUInt16BigEndian(
            faceBytes.AsSpan(cmap.Offset + 12 + 22),
            0xFFFF);

        SfntFace face = SfntFace.Parse(faceBytes);

        Assert.Equal(0, face.MappedGlyphCount);
        AssertUnmapped(face, 'A');
        AssertUnmapped(face, 'B');
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void Format4MappedGlyphCountExcludesSurrogates() {
        var builder = SyntheticFontBuilder.Create();
        int first = AddGlyph(builder, 600);
        int second = AddGlyph(builder, 700);
        int third = AddGlyph(builder, 800);
        byte[] faceBytes = builder
            .WithCmap(4, (0xD7FF, first), (0xD800, second), (0xD801, third))
            .Build();

        SfntFace face = SfntFace.Parse(faceBytes);

        Assert.Equal(1, face.MappedGlyphCount);
        AssertMapped(face, 0xD7FF, first);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void Format4IndirectMappedGlyphCountExcludesSurrogates() {
        var builder = SyntheticFontBuilder.Create();
        int first = AddGlyph(builder, 600);
        int second = AddGlyph(builder, 700);
        int third = AddGlyph(builder, 800);
        byte[] faceBytes = builder
            .WithCmapIndirectGlyphArray((0xD7FF, first), (0xD800, second), (0xD801, third))
            .Build();

        SfntFace face = SfntFace.Parse(faceBytes);

        Assert.Equal(1, face.MappedGlyphCount);
        AssertMapped(face, 0xD7FF, first);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void Format6MappedGlyphCountExcludesSurrogates() {
        var builder = SyntheticFontBuilder.Create();
        int[] glyphs = new int[5];
        for (int index = 0; index < glyphs.Length; index++) {
            glyphs[index] = AddGlyph(builder, 600 + index);
        }

        byte[] faceBytes = builder
            .WithCmap(
                6,
                (0xD7FE, glyphs[0]),
                (0xD7FF, glyphs[1]),
                (0xD800, glyphs[2]),
                (0xD801, glyphs[3]),
                (0xE000, glyphs[4]))
            .Build();

        SfntFace face = SfntFace.Parse(faceBytes);

        Assert.Equal(3, face.MappedGlyphCount);
        AssertMapped(face, 0xD7FE, glyphs[0]);
        AssertMapped(face, 0xE000, glyphs[4]);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void Format12GlyphIdentifierOverflowIsRejected() {
        (SyntheticFontBuilder builder, int _, int _) = CreateMappableBuilder();
        byte[] faceBytes = builder
            .WithRawCmap(BuildRawFormat12(0x41, 0xFFFF_FFFEu))
            .Build();

        FontFormatException exception = FontTestData.AssertFaceFailure(faceBytes, "format 12");
        Assert.Contains("maximum glyph identifier", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void Format12GlyphIdentifierPastTheGlyphCountIsUnmapped() {
        (SyntheticFontBuilder builder, int _, int _) = CreateMappableBuilder();
        byte[] faceBytes = builder
            .WithRawCmap(BuildRawFormat12(0x41, 100_000))
            .Build();

        SfntFace face = SfntFace.Parse(faceBytes);

        Assert.Equal(0, face.MappedGlyphCount);
        AssertUnmapped(face, 'A');
    }

    [Theory]
    [InlineData(0u, false, 0)]
    [InlineData(2u, true, 2)]
    [InlineData(3u, false, 0)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void Format12GlyphIdentifierBoundariesAreEnforced(
        uint startGlyph,
        bool expectedMapped,
        int expectedGlyph) {
        (SyntheticFontBuilder builder, int _, int _) = CreateMappableBuilder();
        byte[] faceBytes = builder
            .WithRawCmap(BuildRawFormat12(0x41, startGlyph))
            .Build();

        SfntFace face = SfntFace.Parse(faceBytes);

        Assert.Equal(expectedMapped, face.TryGetGlyphId(new Rune('A'), out int glyphId));
        Assert.Equal(expectedGlyph, glyphId);
        Assert.Equal(expectedMapped ? 1 : 0, face.MappedGlyphCount);
    }

    private static byte[] BuildRawFormat12(int codepoint, uint startGlyph) {
        byte[] cmap = new byte[40];
        BinaryPrimitives.WriteUInt16BigEndian(cmap, 0);
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(4), 3);
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(6), 10);
        BinaryPrimitives.WriteUInt32BigEndian(cmap.AsSpan(8), 12);
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(12), 12);
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(14), 0);
        BinaryPrimitives.WriteUInt32BigEndian(cmap.AsSpan(16), 28);
        BinaryPrimitives.WriteUInt32BigEndian(cmap.AsSpan(20), 0);
        BinaryPrimitives.WriteUInt32BigEndian(cmap.AsSpan(24), 1);
        BinaryPrimitives.WriteUInt32BigEndian(cmap.AsSpan(28), (uint)codepoint);
        BinaryPrimitives.WriteUInt32BigEndian(cmap.AsSpan(32), (uint)codepoint);
        BinaryPrimitives.WriteUInt32BigEndian(cmap.AsSpan(36), startGlyph);
        return cmap;
    }

    private static int AddGlyph(SyntheticFontBuilder builder, int advance) => builder.AddSimple(
        advance,
        10,
        new SyntheticPoint(0, 0),
        new SyntheticPoint(500, 0),
        new SyntheticPoint(250, 700));

    private static (SyntheticFontBuilder Builder, int First, int Second) CreateMappableBuilder() {
        var builder = SyntheticFontBuilder.Create();
        int first = builder.AddSimple(
            600,
            10,
            new SyntheticPoint(0, 0),
            new SyntheticPoint(500, 0),
            new SyntheticPoint(250, 700));
        int second = builder.AddSimple(
            700,
            20,
            new SyntheticPoint(0, 0),
            new SyntheticPoint(600, 0),
            new SyntheticPoint(300, 800));
        return (builder, first, second);
    }

    private static void AssertMapped(SfntFace face, int codepoint, int expectedGlyph) {
        Assert.True(face.TryGetGlyphId(new Rune(codepoint), out int glyphId));
        Assert.Equal(expectedGlyph, glyphId);
    }

    private static void AssertMapped(SfntFace face, char character, int expectedGlyph) =>
        AssertMapped(face, (int)character, expectedGlyph);

    private static void AssertUnmapped(SfntFace face, int codepoint) {
        Assert.False(face.TryGetGlyphId(new Rune(codepoint), out int glyphId));
        Assert.Equal(0, glyphId);
    }

    private static void AssertUnmapped(SfntFace face, char character) =>
        AssertUnmapped(face, (int)character);
}
