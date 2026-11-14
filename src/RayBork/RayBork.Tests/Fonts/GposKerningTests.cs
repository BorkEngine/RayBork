using RayBork.Internal;

namespace RayBork.Tests.Fonts;

/// <summary>
/// Tests the GPOS pair-positioning parser's script selection, lookup order, extension lookups,
/// validation failures, and the caps that bound hostile lookup and subtable counts.
/// </summary>
public sealed class GposKerningTests {
    private const long ALLOCATION_BOUND = 16 * 1024 * 1024;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ScriptSelectionPrefersLatnThenDfltThenTheFirstScript() {
        Assert.Equal(
            -10,
            ScriptKerning(("cyrl", -30), ("DFLT", -20), ("latn", -10)));
        Assert.Equal(
            -20,
            ScriptKerning(("cyrl", -30), ("DFLT", -20)));
        Assert.Equal(
            -30,
            ScriptKerning(("cyrl", -30), ("grek", -40)));
        Assert.Equal(
            -10,
            ScriptKerning(("cyrl", -30), ("latn", -10)));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MultipleMatchingLookupsUseTheFirstLookup() {
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
    public void ExtensionLookupWithNonPairPositioningTypeIsIgnored() {
        (SyntheticFontBuilder builder, int a, int v, int _, int __) = CreateGlyphBuilder();
        var gpos = new SyntheticGpos {
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
    public void MalformedValueRecordFormatsAreRejected(int valueFormat) {
        (SyntheticFontBuilder builder, int a, int v, int _, int __) = CreateGlyphBuilder();
        var gpos = new SyntheticGpos { ValueFormat1 = valueFormat }.AddPair(a, v, -55);

        FontFormatException exception = Assert.Throws<FontFormatException>(
            () => SfntFace.Parse(builder.WithGpos(gpos).Build()));

        Assert.Contains("value format", exception.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UnsortedCoverageIsRejected(int coverageFormat) {
        (SyntheticFontBuilder builder, int a, int v, int t, int o) = CreateGlyphBuilder();
        var gpos = new SyntheticGpos {
            PairPosFormat = 1,
            CoverageFormat = coverageFormat,
            ReverseCoverage = true,
        }
            .AddPair(a, v, -10)
            .AddPair(t, o, -20);

        FontFormatException exception = Assert.Throws<FontFormatException>(
            () => SfntFace.Parse(builder.WithGpos(gpos).Build()));

        Assert.Contains("coverage", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UnsortedPairSetsAreRejected() {
        (SyntheticFontBuilder builder, int a, int v, int t, int _) = CreateGlyphBuilder();
        var gpos = new SyntheticGpos {
            PairPosFormat = 1,
            ReversePairSets = true,
        }
            .AddPair(a, v, -10)
            .AddPair(a, t, -20);

        FontFormatException exception = Assert.Throws<FontFormatException>(
            () => SfntFace.Parse(builder.WithGpos(gpos).Build()));

        Assert.Contains("not sorted by second glyph", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UnsortedClassDefinitionRangesAreRejected() {
        (SyntheticFontBuilder builder, int a, int v, int t, int o) = CreateGlyphBuilder();
        var gpos = new SyntheticGpos {
            PairPosFormat = 2,
            ClassDefFormat = 2,
            ReverseClassDefRanges = true,
        }
            .AddPair(a, v, -10)
            .AddPair(t, o, -20);

        FontFormatException exception = Assert.Throws<FontFormatException>(
            () => SfntFace.Parse(builder.WithGpos(gpos).Build()));

        Assert.Contains("class definition", exception.Reason, StringComparison.Ordinal);
        Assert.Contains("overlapping or unsorted", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TooManyDistinctLookupsAreRejected() {
        (SyntheticFontBuilder builder, int _, int __, int ___, int ____) = CreateGlyphBuilder();
        var gpos = new SyntheticGpos();
        for (int index = 0; index < 65; index++) {
            gpos.AddLookup();
        }

        gpos.AddFeature("kern", [.. Enumerable.Range(0, 65)]);
        gpos.AddScript("latn", 0);

        AssertControlledFailure(builder.WithGpos(gpos).Build(), "distinct lookups");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TooManyLookupReferencesAreRejected() {
        (SyntheticFontBuilder builder, int _, int __, int ___, int ____) = CreateGlyphBuilder();
        var gpos = new SyntheticGpos();
        gpos.AddLookup();
        gpos.AddFeature("kern", 0, 0);
        gpos.AddFeature("kern", Enumerable.Repeat(0, 65_535).ToArray());
        gpos.AddScript("latn", 0, 1);

        AssertControlledFailure(builder.WithGpos(gpos).Build(), "lookup references");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AliasedLookupReferencesAreDeduplicated() {
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
            $"The aliased GPOS parse allocated {allocated} bytes.");
        Assert.Equal(0, face.GetKerning(a, v));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AliasedPairPositioningSubtablesAreDeduplicated() {
        (SyntheticFontBuilder builder, int a, int v, int _, int __) = CreateGlyphBuilder();
        var gpos = new SyntheticGpos {
            SubtablesPerLookup = 1000,
            AliasLookupSubtables = true,
        };
        gpos.AddLookup((a, v, -10));
        gpos.AddFeature("kern", 0);
        gpos.AddScript("latn", 0);
        byte[] faceBytes = builder.WithGpos(gpos).Build();

        long before = GC.GetAllocatedBytesForCurrentThread();
        SfntFace face = SfntFace.Parse(faceBytes);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(
            allocated < ALLOCATION_BOUND,
            $"The aliased subtable parse allocated {allocated} bytes.");
        Assert.Equal(-10, face.GetKerning(a, v));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TooManyPairPositioningsAreRejected() {
        (SyntheticFontBuilder builder, int _, int __, int ___, int ____) = CreateGlyphBuilder();
        var gpos = new SyntheticGpos { SubtablesPerLookup = 200 };
        gpos.AddLookup();
        gpos.AddLookup();
        gpos.AddFeature("kern", 0, 1);
        gpos.AddScript("latn", 0);

        AssertControlledFailure(builder.WithGpos(gpos).Build(), "distinct subtable offsets");
    }

    private static int ScriptKerning(params (string Tag, int XAdvance)[] variants) {
        (SyntheticFontBuilder builder, int a, int v, int _, int _) = CreateGlyphBuilder();
        var gpos = new SyntheticGpos();
        foreach (var variant in variants) {
            gpos.AddLookup((a, v, variant.XAdvance));
        }

        for (int index = 0; index < variants.Length; index++) {
            gpos.AddFeature("kern", index);
        }

        for (int index = 0; index < variants.Length; index++) {
            gpos.AddScript(variants[index].Tag, index);
        }

        SfntFace face = SfntFace.Parse(builder.WithGpos(gpos).Build());
        return face.GetKerning(a, v);
    }

    private static void AssertControlledFailure(byte[] faceBytes, string reason) {
        long before = GC.GetAllocatedBytesForCurrentThread();
        FontFormatException exception = Assert.Throws<FontFormatException>(
            () => SfntFace.Parse(faceBytes));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Contains(reason, exception.Reason, StringComparison.Ordinal);
        Assert.True(
            allocated < ALLOCATION_BOUND,
            $"The rejected GPOS parse allocated {allocated} bytes.");
    }

    private static (SyntheticFontBuilder Builder, int A, int V, int T, int O) CreateGlyphBuilder() {
        var builder = SyntheticFontBuilder.Create();
        int a = AddGlyph(builder);
        int v = AddGlyph(builder);
        int t = AddGlyph(builder);
        int o = AddGlyph(builder);
        builder.WithCmap(4, (0x41, a), (0x56, v), (0x54, t), (0x4F, o));
        return (builder, a, v, t, o);
    }

    private static int AddGlyph(SyntheticFontBuilder builder) => builder.AddSimple(
        600,
        10,
        new SyntheticPoint(0, 0),
        new SyntheticPoint(500, 0),
        new SyntheticPoint(250, 700));
}
