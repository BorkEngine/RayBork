using RayBork.Internal;

namespace RayBork.Tests.Fonts;

/// <summary>
/// Tests the CFF 1.0 container, INDEX, DICT, charset, FDArray/FDSelect, and Top/Private DICT parsing.
/// </summary>
public sealed class CffReaderTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ParsesAnEmptyIndex() {
        byte[] table = [0, 0];

        CffIndex index = CffIndex.Parse(table, 0, "the synthetic INDEX", out int end);

        Assert.True(index.IsEmpty);
        Assert.Equal(0, index.Count);
        Assert.Equal(2, end);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ParsesEveryIndexOffsetSize(int offSize) {
        byte[] table = BuildRawIndex(offSize, [1, 3, 3], [0xAA, 0xBB]);

        CffIndex index = CffIndex.Parse(table, 0, "the synthetic INDEX", out int end);

        Assert.Equal(2, index.Count);
        Assert.Equal(new byte[] { 0xAA, 0xBB }, index.Get(0).ToArray());
        Assert.Equal([], index.Get(1).ToArray());
        Assert.Equal(table.Length, end);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsAnIndexOffsetSizeOutsideOneToFour(int offSize) {
        byte[] table = [0, 1, (byte)offSize, 1, 1];

        FontFormatException exception = Assert.Throws<FontFormatException>(
            () => CffIndex.Parse(table, 0, "the synthetic INDEX", out _));
        Assert.Contains("offset size", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsAnIndexWhoseFirstOffsetIsNotOne() {
        byte[] table = BuildRawIndex(1, [2, 3], [0xAA, 0xBB]);

        FontFormatException exception = Assert.Throws<FontFormatException>(
            () => CffIndex.Parse(table, 0, "the synthetic INDEX", out _));
        Assert.Contains("first offset", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsAnIndexWithDecreasingOffsets() {
        byte[] table = BuildRawIndex(1, [1, 2, 1, 3], [0xAA, 0xBB]);

        FontFormatException exception = Assert.Throws<FontFormatException>(
            () => CffIndex.Parse(table, 0, "the synthetic INDEX", out _));
        Assert.Contains("decreasing", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsAnIndexWhoseObjectDataIsPastTheTable() {
        byte[] table = BuildRawIndex(1, [1, 5], [0xAA, 0xBB]);

        FontFormatException exception = Assert.Throws<FontFormatException>(
            () => CffIndex.Parse(table, 0, "the synthetic INDEX", out _));
        Assert.Contains("object data", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsATruncatedIndexOffsetArray() {
        byte[] table = [0, 2, 4, 0, 0, 0, 1];

        FontFormatException exception = Assert.Throws<FontFormatException>(
            () => CffIndex.Parse(table, 0, "the synthetic INDEX", out _));
        Assert.Contains("truncated", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsATruncatedEmptyIndex() {
        FontFormatException exception = Assert.Throws<FontFormatException>(
            () => CffIndex.Parse(new byte[] { 0 }, 0, "the synthetic INDEX", out _));
        Assert.Contains("truncated", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ReadsEveryDictIntegerEncoding() {
        byte[] dict = [
            32, // -107
            139, // 0
            246, // 107
            247, 0, // 108
            250, 255, // 1131
            251, 0, // -108
            254, 255, // -1131
            28, 0x80, 0x00, // -32768
            28, 0x7F, 0xFF, // 32767
            29, 0x80, 0x00, 0x00, 0x00, // int.MinValue
            29, 0x7F, 0xFF, 0xFF, 0xFF, // int.MaxValue
            0, // version operator
        ];

        IReadOnlyList<(int Operator, double[] Operands)> entries = ReadDict(dict);

        (int operatorCode, double[] operands) = Assert.Single(entries);
        Assert.Equal(0, operatorCode);
        Assert.Equal(
            [-107, 0, 107, 108, 1131, -108, -1131, -32768, 32767, int.MinValue, int.MaxValue],
            operands);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ReadsEveryDictRealEncoding() {
        byte[] dict = [
            30, 0x1A, 0x5F, // 1.5
            30, 0xE2, 0xA2, 0x5F, // -2.25
            30, 0x0A, 0x00, 0x1F, // 0.001
            30, 0x0A, 0x14, 0x05, 0x41, 0xC3, 0xFF, // 0.140541E-3
            30, 0x1B, 0x2F, // 1E2
            30, 0xAF, // "."
            30, 0xFF, // empty real
            0
        ];

        (_, double[] operands) = Assert.Single(ReadDict(dict));

        Assert.Equal(7, operands.Length);
        Assert.Equal(1.5, operands[0], 12);
        Assert.Equal(-2.25, operands[1], 12);
        Assert.Equal(0.001, operands[2], 12);
        Assert.Equal(0.000140541, operands[3], 15);
        Assert.Equal(100, operands[4], 12);
        Assert.Equal(0, operands[5], 12);
        Assert.Equal(0, operands[6], 12);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ReadsDictEscapeOperators() {
        byte[] dict = [139, 12, 7, 12, 0];

        IReadOnlyList<(int Operator, double[] Operands)> entries = ReadDict(dict);

        Assert.Equal(2, entries.Count);
        Assert.Equal(CffDictReader.ESCAPE_OPERATOR_FLAG | 7, entries[0].Operator);
        Assert.Equal([0d], entries[0].Operands);
        Assert.Equal(CffDictReader.ESCAPE_OPERATOR_FLAG, entries[1].Operator);
        Assert.Empty(entries[1].Operands);
    }

    [Theory]
    [InlineData(22)]
    [InlineData(23)]
    [InlineData(24)]
    [InlineData(25)]
    [InlineData(26)]
    [InlineData(27)]
    [InlineData(31)]
    [InlineData(255)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsReservedDictBytes(int reserved) {
        FontFormatException exception = Assert.Throws<FontFormatException>(
            () => ReadDict([(byte)reserved]));
        Assert.Contains("reserved", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsReservedRealNibbles() {
        FontFormatException exception = Assert.Throws<FontFormatException>(
            () => ReadDict([30, 0xDF]));
        Assert.Contains("reserved nibble", exception.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(new byte[] { 30, 0x12 })]
    [InlineData(new byte[] { 30, 0x1B, 0xFF })]
    [InlineData(new byte[] { 30, 0xBE, 0x5F })]
    [InlineData(new byte[] { 30, 0xB1, 0xE5, 0xFF })]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsMalformedRealEncodings(byte[] dict) {
        Assert.Throws<FontFormatException>(() => ReadDict(dict));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsRealOperandsOutsideTheDoubleRange() {
        byte[] dict = [30, 0x1B, 0x10, 0x00, 0x0F, 0];

        FontFormatException exception = Assert.Throws<FontFormatException>(() => ReadDict(dict));
        Assert.Contains("double range", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsDictOperandStackOverflow() {
        byte[] dict = new byte[49];
        dict.AsSpan().Fill(139);
        byte[] withOperator = [.. dict, 0];

        FontFormatException exception = Assert.Throws<FontFormatException>(
            () => ReadDict(withOperator));
        Assert.Contains("48", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsTrailingDictOperands() {
        FontFormatException exception = Assert.Throws<FontFormatException>(() => ReadDict([139]));
        Assert.Contains("no operator", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsAnEscapeByteAtTheEndOfADict() {
        FontFormatException exception = Assert.Throws<FontFormatException>(() => ReadDict([12]));
        Assert.Contains("escape byte", exception.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ReadsEveryCharsetFormat(int format) {
        int[] sids = [34, 35, 36, 40, 41, 42];
        SyntheticCff builder = CreateGlyphTable(sids.Length + 1).WithCharsetFormat(format, sids);
        byte[] table = builder.Build();

        CffCharset charset = CffCharset.Parse(
            table,
            sids.Length + 1,
            builder.CharsetOffset,
            "the synthetic charset");

        Assert.Equal(sids.Length + 1, charset.Count);
        Assert.Equal(0, charset.GetSid(0));
        for (int index = 0; index < sids.Length; index++) {
            Assert.Equal(sids[index], charset.GetSid(index + 1));
        }

        Assert.True(charset.TryGetGlyphId(40, out int glyphId));
        Assert.Equal(4, glyphId);
        Assert.False(charset.TryGetGlyphId(999, out _));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ReadsThePredefinedIsoAdobeCharset() {
        CffCharset charset = CffCharset.Parse(
            new byte[] { 1, 0, 4, 4 },
            3,
            0,
            "the synthetic charset");

        Assert.Equal(3, charset.Count);
        Assert.Equal(0, charset.GetSid(0));
        Assert.Equal(1, charset.GetSid(1));
        Assert.Equal(2, charset.GetSid(2));
    }

    [Theory]
    [InlineData(1, "Expert")]
    [InlineData(2, "ExpertSubset")]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsPredefinedExpertCharsets(int offset, string name) {
        FontUnsupportedException exception = Assert.Throws<FontUnsupportedException>(
            () => CffCharset.Parse(new byte[] { 1, 0, 4, 4 }, 3, offset, "the synthetic charset"));
        Assert.Contains(name, exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsAnIsoAdobeCharsetLargerThanItsGlyphCount() {
        FontFormatException exception = Assert.Throws<FontFormatException>(
            () => CffCharset.Parse(new byte[] { 1, 0, 4, 4 }, 230, 0, "the synthetic charset"));
        Assert.Contains("ISOAdobe", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsAnUnknownCharsetFormat() {
        SyntheticCff builder = CreateGlyphTable(3).WithRawCharset([3, 0, 0]);

        FontTestData.AssertFaceFailure(builder.BuildFace(), "charset format 3");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsATruncatedCharset() {
        SyntheticCff builder = CreateGlyphTable(3).WithRawCharset([0, 0, 34]);

        FontTestData.AssertFaceFailure(builder.BuildFace(), "charset");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsACharsetRangePastTheLastGlyph() {
        byte[] charset = [1, 0, 34, 4];

        SyntheticCff builder = CreateGlyphTable(3).WithRawCharset(charset);

        FontTestData.AssertFaceFailure(builder.BuildFace(), "range");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ReadsFdSelectFormatZero() {
        SyntheticCff builder = CreateCidTable(3, fontDictCount: 2)
            .WithFdSelectFormat0(0, 1, 1);
        byte[] table = builder.Build();

        CffFdSelect select = CffFdSelect.Parse(
            table,
            3,
            2,
            builder.FdSelectOffset,
            "the synthetic FDSelect");

        Assert.Equal(0, select.GetFontDictIndex(0));
        Assert.Equal(1, select.GetFontDictIndex(1));
        Assert.Equal(1, select.GetFontDictIndex(2));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ReadsFdSelectFormatThreeRanges() {
        SyntheticCff builder = CreateCidTable(4, fontDictCount: 2)
            .WithFdSelectFormat3((0, 0), (2, 1));
        byte[] table = builder.Build();

        CffFdSelect select = CffFdSelect.Parse(
            table,
            4,
            2,
            builder.FdSelectOffset,
            "the synthetic FDSelect");

        Assert.Equal(0, select.GetFontDictIndex(0));
        Assert.Equal(0, select.GetFontDictIndex(1));
        Assert.Equal(1, select.GetFontDictIndex(2));
        Assert.Equal(1, select.GetFontDictIndex(3));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsAnFdSelectIndexPastTheFdArray() {
        SyntheticCff builder = CreateCidTable(3, fontDictCount: 2)
            .WithFdSelectFormat0(0, 2, 1);
        byte[] table = builder.Build();

        FontFormatException exception = Assert.Throws<FontFormatException>(
            () => CffFdSelect.Parse(table, 3, 2, builder.FdSelectOffset, "the synthetic FDSelect"));
        Assert.Contains("FDArray", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsFdSelectRangesThatDoNotIncrease() {
        SyntheticCff builder = CreateCidTable(3, fontDictCount: 2)
            .WithFdSelectFormat3((0, 0), (0, 1));
        byte[] table = builder.Build();

        FontFormatException exception = Assert.Throws<FontFormatException>(
            () => CffFdSelect.Parse(table, 3, 2, builder.FdSelectOffset, "the synthetic FDSelect"));
        Assert.Contains("non-increasing", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsFdSelectRangesThatDoNotEndAtTheGlyphCount() {
        SyntheticCff builder = CreateCidTable(3, fontDictCount: 2)
            .WithRawFdSelect([3, 0, 1, 0, 0, 0, 0, 2]);
        byte[] table = builder.Build();

        FontFormatException exception = Assert.Throws<FontFormatException>(
            () => CffFdSelect.Parse(table, 3, 2, builder.FdSelectOffset, "the synthetic FDSelect"));
        Assert.Contains("ends its ranges", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsFdSelectFormatFour() {
        SyntheticCff builder = CreateCidTable(3, fontDictCount: 1)
            .WithRawFdSelect([4, 0, 0]);
        byte[] table = builder.Build();

        FontUnsupportedException exception = Assert.Throws<FontUnsupportedException>(
            () => CffFdSelect.Parse(table, 3, 1, builder.FdSelectOffset, "the synthetic FDSelect"));
        Assert.Contains("format 4", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsAnUnknownFdSelectFormat() {
        SyntheticCff builder = CreateCidTable(3, fontDictCount: 1)
            .WithRawFdSelect([2, 0, 0]);
        byte[] table = builder.Build();

        FontFormatException exception = Assert.Throws<FontFormatException>(
            () => CffFdSelect.Parse(table, 3, 1, builder.FdSelectOffset, "the synthetic FDSelect"));
        Assert.Contains("FDSelect format 2", exception.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 107)]
    [InlineData(107, 107)]
    [InlineData(1239, 107)]
    [InlineData(1240, 1131)]
    [InlineData(33899, 1131)]
    [InlineData(33900, 32768)]
    [InlineData(65535, 32768)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UsesTheDocumentedSubroutineBias(int subroutineCount, int expected) {
        Assert.Equal(expected, CffType2Interpreter.GetSubroutineBias(subroutineCount));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ParsesFacesWithUnknownTopAndPrivateDictKeys() {
        SyntheticCff builder = CreateGlyphTable(2)
            .WithTopDictExtra(SyntheticCff.DictKey(5, -614, -295, 2159, 958))
            .WithTopDictExtra(SyntheticCff.DictEscapeKey(2, 0))
            .WithPrivateDictExtra(SyntheticCff.DictKey(6, -12, 12));

        SfntFace face = SfntFace.Parse(builder.BuildFace());

        Assert.Equal(SfntOutlineKind.Cff, face.OutlineKind);
        Assert.True(face.GetGlyph(1).IsEmpty);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ParsesFacesWithPrivateWidthsAndLocalSubrs() {
        SyntheticCff builder = CreateGlyphTable(2)
            .WithPrivateWidths(500, 400)
            .WithLocalSubrs(SyntheticCffCharStrings.RLineTo(10, 0), SyntheticCffCharStrings.Return());
        int glyph = builder.AddGlyph(SyntheticCffCharStrings.Concat(
            SyntheticCffCharStrings.RMoveTo(0, 0),
            SyntheticCffCharStrings.CallSubr(-107),
            SyntheticCffCharStrings.EndChar()));

        SfntFace face = SfntFace.Parse(builder.BuildFace());

        FontGlyph resolved = face.GetGlyph(glyph);
        Assert.Equal(1, resolved.Outline.Contours.Length);
        Assert.Equal(2, resolved.Outline.Contours.Span[0].Segments.Length);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void IgnoresTheTopDictEncoding() {
        foreach (int encoding in new[] { 0, 1 }) {
            SyntheticCff builder = CreateGlyphTable(2).WithEncoding(encoding);

            SfntFace face = SfntFace.Parse(builder.BuildFace());

            Assert.Equal(SfntOutlineKind.Cff, face.OutlineKind);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsAnEncodingOffsetPastTheTable() {
        SyntheticCff builder = CreateGlyphTable(2)
            .WithTopDictExtra(SyntheticCff.DictKey(16, 100000));

        FontTestData.AssertFaceFailure(builder.BuildFace(), "Encoding offset");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsACharsetOffsetPastTheTable() {
        SyntheticCff builder = CreateGlyphTable(2);
        builder.WithTopDictExtra(SyntheticCff.DictKey(15, 100000));

        FontTestData.AssertFaceFailure(builder.BuildFace(), "charset");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsACharStringsOffsetPastTheTable() {
        SyntheticCff builder = CreateGlyphTable(2);
        builder.WithTopDictExtra(SyntheticCff.DictKey(17, 100000));

        FontTestData.AssertFaceFailure(builder.BuildFace(), "CFF");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsAPrivateDictPastTheTable() {
        SyntheticCff builder = CreateGlyphTable(2);
        builder.WithTopDictExtra(SyntheticCff.DictKey(18, 8, 100000));

        FontTestData.AssertFaceFailure(builder.BuildFace(), "Private DICT");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsAPrivateDictSizePastTheTable() {
        SyntheticCff builder = CreateGlyphTable(2);
        builder.WithTopDictExtra(SyntheticCff.DictKey(18, 100000, 4));

        FontTestData.AssertFaceFailure(builder.BuildFace(), "Private DICT");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsALocalSubrsOffsetPastTheTable() {
        SyntheticCff builder = CreateGlyphTable(2)
            .WithPrivateDictExtra(SyntheticCff.DictKey(19, 100000));

        FontTestData.AssertFaceFailure(builder.BuildFace(), "Subrs");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsMissingCharStringsOperator() {
        byte[] topDict = SyntheticCff.DictKey(15, 0);

        AssertCffFailure(BuildMinimalFace(topDict), "CharStrings");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsAnEmptyTopDictIndex() {
        byte[] table = BuildTable(
            CffHeader(),
            BuildNameIndex(),
            [0, 0],
            [0, 0],
            [0, 0]);

        AssertCffFailure(table, "Top DICT INDEX");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsAnEmptyNameIndex() {
        byte[] table = BuildTable(CffHeader(), [0, 0], BuildTopDictIndex([]), [0, 0], [0, 0]);

        AssertCffFailure(table, "Name INDEX");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsAMultipleNameIndex() {
        byte[] names = BuildRawIndex(1, [1, 3, 4], [(byte)'A', (byte)'B']);
        byte[] table = BuildTable(CffHeader(), names, BuildTopDictIndex([]), [0, 0], [0, 0]);

        AssertCffFailure(table, "Name INDEX");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsACharStringsCountMismatch() {
        SyntheticCff cff = CreateGlyphTable(2);
        byte[] cffData = cff.Build();
        SyntheticFontBuilder builder = SyntheticFontBuilder.Create();
        while (builder.GlyphCount < 3) {
            builder.AddEmpty(500);
        }

        byte[] face = builder.UseCff(cffData).WithCmap(4, (0x41, 1)).Build();

        FontTestData.AssertFaceFailure(face, "CharStrings INDEX");
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 1, 0, 4 })]
    [InlineData(new byte[] { 0, 0, 4, 4 })]
    [InlineData(new byte[] { 3, 0, 4, 4 })]
    [InlineData(new byte[] { 1, 0, 3, 4 })]
    [InlineData(new byte[] { 1, 0, 4, 0 })]
    [InlineData(new byte[] { 1, 0, 4, 5 })]
    [InlineData(new byte[] { 1, 0, 8, 4 })]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsMalformedCffHeaders(byte[] table) {
        AssertCffFailure(table, "CFF");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsACff2TableDirectly() {
        FontUnsupportedException exception = Assert.Throws<FontUnsupportedException>(
            () => CffFont.Parse(new byte[] { 2, 0, 5, 0 }, 1, 1000));
        Assert.Contains("CFF version 2.0", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsAnUnknownCffMajorVersion() {
        FontFormatException exception = Assert.Throws<FontFormatException>(
            () => CffFont.Parse(new byte[] { 3, 0, 4, 4 }, 1, 1000));
        Assert.Contains("3.0", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsAnOutOfRangeGlyphIdentifier() {
        SfntFace face = SfntFace.Parse(CreateGlyphTable(2).BuildFace());

        Assert.Throws<ArgumentOutOfRangeException>(() => face.GetGlyph(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => face.GetGlyph(-1));
    }

    private static void AssertCffFailure(byte[] table, string reason) {
        FontFormatException exception = Assert.Throws<FontFormatException>(
            () => CffFont.Parse(table, 1, 1000));
        Assert.Contains(reason, exception.Reason, StringComparison.Ordinal);
    }

    private static SyntheticCff CreateGlyphTable(int glyphCount) {
        SyntheticCff builder = SyntheticCff.Create();
        for (int glyph = 0; glyph < glyphCount; glyph++) {
            builder.AddGlyph(SyntheticCffCharStrings.EndChar());
        }

        return builder;
    }

    private static SyntheticCff CreateCidTable(int glyphCount, int fontDictCount) {
        SyntheticCff builder = CreateGlyphTable(glyphCount).AsCid();
        for (int fontDict = 0; fontDict < fontDictCount; fontDict++) {
            builder.AddFontDict();
        }

        return builder;
    }

    private static IReadOnlyList<(int Operator, double[] Operands)> ReadDict(byte[] data) {
        CffDictReader reader = new(data, "the synthetic DICT");
        List<(int Operator, double[] Operands)> entries = [];
        while (reader.TryRead(out int operatorCode, out int operandCount)) {
            double[] operands = new double[operandCount];
            for (int index = 0; index < operandCount; index++) {
                operands[index] = reader.Operand(index);
            }

            entries.Add((operatorCode, operands));
        }

        return entries;
    }

    private static byte[] BuildRawIndex(int offSize, int[] offsets, byte[] data) {
        List<byte> index = [];
        WriteUInt16(index, offsets.Length - 1);
        index.Add((byte)offSize);
        foreach (int offset in offsets) {
            for (int shift = (offSize - 1) * 8; shift >= 0; shift -= 8) {
                index.Add((byte)(offset >> shift));
            }
        }

        index.AddRange(data);
        return [.. index];
    }

    private static byte[] CffHeader() => [1, 0, 4, 4];

    private static byte[] BuildNameIndex() => BuildRawIndex(1, [1, 2], [(byte)'S']);

    private static byte[] BuildTopDictIndex(byte[] topDict) =>
        BuildRawIndex(1, [1, 1 + topDict.Length], topDict);

    private static byte[] BuildMinimalFace(byte[] topDict) =>
        BuildTable(CffHeader(), BuildNameIndex(), BuildTopDictIndex(topDict), [0, 0], [0, 0]);

    private static byte[] BuildTable(
        byte[] header,
        byte[] nameIndex,
        byte[] topDictIndex,
        byte[] stringIndex,
        byte[] globalSubrIndex) => [
            .. header,
            .. nameIndex,
            .. topDictIndex,
            .. stringIndex,
            .. globalSubrIndex,
        ];

    private static void WriteUInt16(List<byte> target, int value) {
        target.Add((byte)(value >> 8));
        target.Add((byte)value);
    }
}
