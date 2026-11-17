using System.Buffers.Binary;
using RayBork.Internal;

namespace RayBork.Tests.Fonts;

/// <summary>
/// Tests the GPOS pair-positioning parser's script selection, lookup order, extension lookups,
/// validation failures, and the caps that bound hostile lookup and subtable counts.
/// </summary>
public sealed class GposKerningTests
{
    private const long ALLOCATION_BOUND = 16 * 1024 * 1024;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ScriptSelectionPrefersLatnThenDfltThenTheFirstScript()
    {
        Assert.Equal(-10, ScriptKerning(("cyrl", -30), ("DFLT", -20), ("latn", -10)));
        Assert.Equal(-20, ScriptKerning(("cyrl", -30), ("DFLT", -20)));
        Assert.Equal(-30, ScriptKerning(("cyrl", -30), ("grek", -40)));
        Assert.Equal(-10, ScriptKerning(("cyrl", -30), ("latn", -10)));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MultipleMatchingLookupsUseTheFirstLookup()
    {
        (SyntheticFontBuilder builder, int a, int v, int _, int __) = CreateGlyphBuilder();
        var gpos = new SyntheticGpos();
        gpos.AddLookup((a, v, -10));
        gpos.AddLookup((a, v, -20));
        gpos.AddFeature("kern", 0, 1);
        gpos.AddScript("latn", 0);
        SfntFace face = SfntFace.Parse(builder.WithGpos(gpos).Build());

        Assert.Equal(-10, face.GetKerning(a, v));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ExtensionLookupWithNonPairPositioningTypeIsIgnored()
    {
        (SyntheticFontBuilder builder, int a, int v, int _, int __) = CreateGlyphBuilder();
        var gpos = new SyntheticGpos
        {
            ExtensionLookup = true,
            ExtensionInnerLookupType = 4,
        }.AddPair(a, v, -55);
        SfntFace face = SfntFace.Parse(builder.WithGpos(gpos).Build());

        Assert.Equal(0, face.GetKerning(a, v));
    }

    [Theory]
    [InlineData(0x0100)]
    [InlineData(0x8000)]
    [InlineData(0xFFFF)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MalformedValueRecordFormatsAreRejected(int valueFormat)
    {
        (SyntheticFontBuilder builder, int a, int v, int _, int __) = CreateGlyphBuilder();
        var gpos = new SyntheticGpos { ValueFormat1 = valueFormat }.AddPair(a, v, -55);

        FontFormatException exception = Assert.Throws<FontFormatException>(() =>
            SfntFace.Parse(builder.WithGpos(gpos).Build())
        );

        Assert.Contains("value format", exception.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UnsortedCoverageIsRejected(int coverageFormat)
    {
        (SyntheticFontBuilder builder, int a, int v, int t, int o) = CreateGlyphBuilder();
        var gpos = new SyntheticGpos
        {
            PairPosFormat = 1,
            CoverageFormat = coverageFormat,
            ReverseCoverage = true,
        }
            .AddPair(a, v, -10)
            .AddPair(t, o, -20);

        FontFormatException exception = Assert.Throws<FontFormatException>(() =>
            SfntFace.Parse(builder.WithGpos(gpos).Build())
        );

        Assert.Contains("coverage", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UnsortedPairSetsAreRejected()
    {
        (SyntheticFontBuilder builder, int a, int v, int t, int _) = CreateGlyphBuilder();
        var gpos = new SyntheticGpos { PairPosFormat = 1, ReversePairSets = true }
            .AddPair(a, v, -10)
            .AddPair(a, t, -20);

        FontFormatException exception = Assert.Throws<FontFormatException>(() =>
            SfntFace.Parse(builder.WithGpos(gpos).Build())
        );

        Assert.Contains("not sorted by second glyph", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UnsortedClassDefinitionRangesAreRejected()
    {
        (SyntheticFontBuilder builder, int a, int v, int t, int o) = CreateGlyphBuilder();
        var gpos = new SyntheticGpos
        {
            PairPosFormat = 2,
            ClassDefFormat = 2,
            ReverseClassDefRanges = true,
        }
            .AddPair(a, v, -10)
            .AddPair(t, o, -20);

        FontFormatException exception = Assert.Throws<FontFormatException>(() =>
            SfntFace.Parse(builder.WithGpos(gpos).Build())
        );

        Assert.Contains("class definition", exception.Reason, StringComparison.Ordinal);
        Assert.Contains("overlapping or unsorted", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TooManyDistinctLookupsAreRejected()
    {
        (SyntheticFontBuilder builder, int _, int __, int ___, int ____) = CreateGlyphBuilder();
        var gpos = new SyntheticGpos();
        for (int index = 0; index < 65; index++)
        {
            gpos.AddLookup();
        }

        gpos.AddFeature("kern", [.. Enumerable.Range(0, 65)]);
        gpos.AddScript("latn", 0);

        AssertUnsupported(builder.WithGpos(gpos).Build(), "distinct lookups");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TooManyLookupReferencesAreRejected()
    {
        (SyntheticFontBuilder builder, int _, int __, int ___, int ____) = CreateGlyphBuilder();
        var gpos = new SyntheticGpos();
        gpos.AddLookup();
        gpos.AddFeature("kern", 0, 0);
        gpos.AddFeature("kern", Enumerable.Repeat(0, 65_535).ToArray());
        gpos.AddScript("latn", 0, 1);

        AssertUnsupported(builder.WithGpos(gpos).Build(), "lookup references");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AliasedLookupReferencesAreDeduplicated()
    {
        (SyntheticFontBuilder builder, int a, int v, int _, int __) = CreateGlyphBuilder();
        var gpos = new SyntheticGpos();
        gpos.AddLookup();
        gpos.AddFeature("kern", Enumerable.Repeat(0, 32_768).ToArray());
        gpos.AddScript("latn", 0);
        byte[] faceBytes = builder.WithGpos(gpos).Build();

        long before = GC.GetAllocatedBytesForCurrentThread();
        SfntFace face = SfntFace.Parse(faceBytes);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(
            allocated < ALLOCATION_BOUND,
            $"The aliased GPOS parse allocated {allocated} bytes."
        );
        Assert.Equal(0, face.GetKerning(a, v));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AliasedPairPositioningSubtablesAreDeduplicated()
    {
        (SyntheticFontBuilder builder, int a, int v, int _, int __) = CreateGlyphBuilder();
        var gpos = new SyntheticGpos { SubtablesPerLookup = 1000, AliasLookupSubtables = true };
        gpos.AddLookup((a, v, -10));
        gpos.AddFeature("kern", 0);
        gpos.AddScript("latn", 0);
        byte[] faceBytes = builder.WithGpos(gpos).Build();

        long before = GC.GetAllocatedBytesForCurrentThread();
        SfntFace face = SfntFace.Parse(faceBytes);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(
            allocated < ALLOCATION_BOUND,
            $"The aliased subtable parse allocated {allocated} bytes."
        );
        Assert.Equal(-10, face.GetKerning(a, v));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AliasedLookupListAndSubtableRecordsAreDeduplicated()
    {
        byte[] faceBytes = HostileFontFaces.CreateAliasedLookupListFace();

        long before = GC.GetAllocatedBytesForCurrentThread();
        SfntFace face = SfntFace.Parse(faceBytes);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(
            allocated < ALLOCATION_BOUND,
            $"The aliased lookup-list parse allocated {allocated} bytes."
        );
        Assert.Equal(-10, face.GetKerning(1, 2));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TooManyLookupSubtableRecordsAreRejected()
    {
        (SyntheticFontBuilder builder, int _, int __, int ___, int ____) = CreateGlyphBuilder();
        var gpos = new SyntheticGpos { AliasLookupSubtables = true };
        gpos.AddLookup(17);
        gpos.AddLookup(32_760);
        gpos.AddFeature("kern", 1, 0);
        gpos.AddScript("latn", 0);

        AssertUnsupported(builder.WithGpos(gpos).Build(), "subtable records");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void LookupSubtableRecordBudgetAllowsTheExactLimit()
    {
        (SyntheticFontBuilder builder, int a, int v, int _, int __) = CreateGlyphBuilder();
        var gpos = new SyntheticGpos { AliasLookupSubtables = true };
        gpos.AddLookup(17);
        gpos.AddLookup(32_751, (a, v, -10));
        gpos.AddFeature("kern", 1, 0);
        gpos.AddScript("latn", 0);
        SfntFace face = SfntFace.Parse(builder.WithGpos(gpos).Build());

        Assert.Equal(-10, face.GetKerning(a, v));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void LookupSubtableRecordBudgetRejectsOnePastTheLimit()
    {
        (SyntheticFontBuilder builder, int a, int v, int _, int __) = CreateGlyphBuilder();
        var gpos = new SyntheticGpos { AliasLookupSubtables = true };
        gpos.AddLookup(18);
        gpos.AddLookup(32_751, (a, v, -10));
        gpos.AddFeature("kern", 1, 0);
        gpos.AddScript("latn", 0);

        AssertUnsupported(builder.WithGpos(gpos).Build(), "subtable records");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CoverageEntryBudgetIsEnforced()
    {
        byte[] gpos = BuildBudgetGpos(5, CreateHugeCoverage(), CreateEmptyClassDef());
        byte[] faceBytes = CreateManyGlyphFace(gpos);

        AssertUnsupported(faceBytes, "262144");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ClassDefinitionEntryBudgetIsEnforced()
    {
        byte[] gpos = BuildBudgetGpos(3, CreateTinyCoverage(), CreateHugeClassDef());
        byte[] faceBytes = CreateManyGlyphFace(gpos);

        AssertUnsupported(faceBytes, "262144");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TooManyPairPositioningsAreRejected()
    {
        (SyntheticFontBuilder builder, int _, int __, int ___, int ____) = CreateGlyphBuilder();
        var gpos = new SyntheticGpos { SubtablesPerLookup = 200 };
        gpos.AddLookup();
        gpos.AddLookup();
        gpos.AddFeature("kern", 0, 1);
        gpos.AddScript("latn", 0);

        AssertUnsupported(builder.WithGpos(gpos).Build(), "distinct subtable offsets");
    }

    private static int ScriptKerning(params (string Tag, int XAdvance)[] variants)
    {
        (SyntheticFontBuilder builder, int a, int v, int _, int _) = CreateGlyphBuilder();
        var gpos = new SyntheticGpos();
        foreach (var variant in variants)
        {
            gpos.AddLookup((a, v, variant.XAdvance));
        }

        for (int index = 0; index < variants.Length; index++)
        {
            gpos.AddFeature("kern", index);
        }

        for (int index = 0; index < variants.Length; index++)
        {
            gpos.AddScript(variants[index].Tag, index);
        }

        SfntFace face = SfntFace.Parse(builder.WithGpos(gpos).Build());
        return face.GetKerning(a, v);
    }

    private static byte[] CreateManyGlyphFace(byte[] gpos)
    {
        var builder = SyntheticFontBuilder.Create();
        for (int index = 0; index < 65_534; index++)
        {
            builder.AddEmpty(500);
        }

        builder.WithCmap(4, (0x41, 1));
        builder.WithRawGpos(gpos);
        return builder.Build();
    }

    private static byte[] BuildBudgetGpos(int subtableCount, byte[] coverage, byte[] classDef)
    {
        const int scriptLength = 20;
        const int featureLength = 14;
        int lookupListOffset = 10 + scriptLength + featureLength;
        int lookupHeaderLength = 6 + (2 * subtableCount);
        int coverageOffset = lookupHeaderLength + (16 * subtableCount);
        int classDefOffset = coverageOffset + coverage.Length;
        int lookupLength = classDefOffset + classDef.Length;
        byte[] gpos = new byte[lookupListOffset + 4 + lookupLength];

        WriteUInt16(gpos, 0, 1);
        WriteUInt16(gpos, 2, 0);
        WriteUInt16(gpos, 4, 10);
        WriteUInt16(gpos, 6, 10 + scriptLength);
        WriteUInt16(gpos, 8, lookupListOffset);

        WriteUInt16(gpos, 10, 1);
        WriteTag(gpos, 12, "latn");
        WriteUInt16(gpos, 16, 8);
        WriteUInt16(gpos, 18, 4);
        WriteUInt16(gpos, 20, 0);
        WriteUInt16(gpos, 22, 0);
        WriteUInt16(gpos, 24, 0xFFFF);
        WriteUInt16(gpos, 26, 1);
        WriteUInt16(gpos, 28, 0);

        WriteUInt16(gpos, 30, 1);
        WriteTag(gpos, 32, "kern");
        WriteUInt16(gpos, 36, 8);
        WriteUInt16(gpos, 38, 0);
        WriteUInt16(gpos, 40, 1);
        WriteUInt16(gpos, 42, 0);

        WriteUInt16(gpos, lookupListOffset, 1);
        WriteUInt16(gpos, lookupListOffset + 2, 4);
        int lookup = lookupListOffset + 4;
        WriteUInt16(gpos, lookup, 2);
        WriteUInt16(gpos, lookup + 2, 0);
        WriteUInt16(gpos, lookup + 4, (ushort)subtableCount);
        int offsets = lookup + 6;
        int subtables = offsets + (2 * subtableCount);
        for (int index = 0; index < subtableCount; index++)
        {
            int subtable = subtables + (16 * index);
            WriteUInt16(gpos, offsets + (2 * index), (ushort)(subtable - lookup));
            WriteUInt16(gpos, subtable, 2);
            WriteUInt16(gpos, subtable + 2, (ushort)(lookup + coverageOffset - subtable));
            WriteUInt16(gpos, subtable + 4, 0);
            WriteUInt16(gpos, subtable + 6, 0);
            WriteUInt16(gpos, subtable + 8, (ushort)(lookup + classDefOffset - subtable));
            WriteUInt16(gpos, subtable + 10, (ushort)(lookup + classDefOffset - subtable));
            WriteUInt16(gpos, subtable + 12, 1);
            WriteUInt16(gpos, subtable + 14, 1);
        }

        coverage.CopyTo(gpos, lookup + coverageOffset);
        classDef.CopyTo(gpos, lookup + classDefOffset);
        return gpos;
    }

    private static byte[] CreateHugeCoverage()
    {
        byte[] coverage = new byte[10];
        WriteUInt16(coverage, 0, 2);
        WriteUInt16(coverage, 2, 1);
        WriteUInt16(coverage, 4, 0);
        WriteUInt16(coverage, 6, 0xFFFE);
        WriteUInt16(coverage, 8, 0);
        return coverage;
    }

    private static byte[] CreateTinyCoverage()
    {
        byte[] coverage = new byte[6];
        WriteUInt16(coverage, 0, 1);
        WriteUInt16(coverage, 2, 1);
        WriteUInt16(coverage, 4, 0);
        return coverage;
    }

    private static byte[] CreateEmptyClassDef()
    {
        byte[] classDef = new byte[4];
        WriteUInt16(classDef, 0, 2);
        WriteUInt16(classDef, 2, 0);
        return classDef;
    }

    private static byte[] CreateHugeClassDef()
    {
        byte[] classDef = new byte[10];
        WriteUInt16(classDef, 0, 2);
        WriteUInt16(classDef, 2, 1);
        WriteUInt16(classDef, 4, 0);
        WriteUInt16(classDef, 6, 0xFFFE);
        WriteUInt16(classDef, 8, 0);
        return classDef;
    }

    private static void WriteUInt16(byte[] target, int offset, int value) =>
        BinaryPrimitives.WriteUInt16BigEndian(target.AsSpan(offset), (ushort)value);

    private static void WriteTag(byte[] target, int offset, string tag)
    {
        target[offset] = (byte)tag[0];
        target[offset + 1] = (byte)tag[1];
        target[offset + 2] = (byte)tag[2];
        target[offset + 3] = (byte)tag[3];
    }

    private static void AssertUnsupported(byte[] faceBytes, string reason)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        FontUnsupportedException exception = Assert.Throws<FontUnsupportedException>(() =>
            SfntFace.Parse(faceBytes)
        );
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Contains(reason, exception.Reason, StringComparison.Ordinal);
        Assert.True(
            allocated < ALLOCATION_BOUND,
            $"The rejected GPOS parse allocated {allocated} bytes."
        );
    }

    private static (SyntheticFontBuilder Builder, int A, int V, int T, int O) CreateGlyphBuilder()
    {
        var builder = SyntheticFontBuilder.Create();
        int a = AddGlyph(builder);
        int v = AddGlyph(builder);
        int t = AddGlyph(builder);
        int o = AddGlyph(builder);
        builder.WithCmap(4, (0x41, a), (0x56, v), (0x54, t), (0x4F, o));
        return (builder, a, v, t, o);
    }

    private static int AddGlyph(SyntheticFontBuilder builder) =>
        builder.AddSimple(
            600,
            10,
            new SyntheticPoint(0, 0),
            new SyntheticPoint(500, 0),
            new SyntheticPoint(250, 700)
        );
}
