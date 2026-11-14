using System.Buffers.Binary;
using RayBork.Internal;

namespace RayBork.Tests.Fonts;

/// <summary>
/// Tests the legacy <c>kern</c> table and GPOS pair positioning: format 0 pairs, PairPos formats 1
/// and 2, coverage and class definition formats, extension lookups, value record layout, the
/// <c>kern</c> feature filter, and the documented GPOS-over-<c>kern</c> precedence.
/// </summary>
public sealed class KerningTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void KernTableFormat0PairsAreRead() {
        (SyntheticFontBuilder builder, int a, int v, int t, int o) = CreateKerningBuilder();
        byte[] faceBytes = builder
            .WithKernPairs((a, v, -80), (v, a, -70), (a, t, 50))
            .Build();
        SfntFace face = SfntFace.Parse(faceBytes);

        Assert.Equal(-80, face.GetKerning(a, v));
        Assert.Equal(-70, face.GetKerning(v, a));
        Assert.Equal(50, face.GetKerning(a, t));
        Assert.Equal(0, face.GetKerning(a, o));
        Assert.Equal(0, face.GetKerning(o, o));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MissingKerningTablesReturnZero() {
        (SyntheticFontBuilder builder, int a, int v, int _, int __) = CreateKerningBuilder();
        SfntFace face = SfntFace.Parse(builder.Build());

        Assert.Equal(0, face.GetKerning(a, v));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GposPairPos1GlyphPairsAreRead() {
        (SyntheticFontBuilder builder, int a, int v, int t, int o) = CreateKerningBuilder();
        var gpos = new SyntheticGpos { PairPosFormat = 1, CoverageFormat = 1 }
            .AddPair(a, v, -90)
            .AddPair(a, t, 45)
            .AddPair(v, a, -60);
        SfntFace face = SfntFace.Parse(builder.WithGpos(gpos).Build());

        Assert.Equal(-90, face.GetKerning(a, v));
        Assert.Equal(45, face.GetKerning(a, t));
        Assert.Equal(-60, face.GetKerning(v, a));
        Assert.Equal(0, face.GetKerning(a, o));
        Assert.Equal(0, face.GetKerning(o, o));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GposPairPos1SkipsOptionalValueRecordFields() {
        (SyntheticFontBuilder builder, int a, int v, int t, int o) = CreateKerningBuilder();
        var gpos = new SyntheticGpos {
            PairPosFormat = 1,
            CoverageFormat = 2,
            ValueFormat1 = 0x001D,
            ValueFormat2 = 0x001C,
        }
            .AddPair(a, v, -90, secondXAdvance: 333)
            .AddPair(a, t, 45, secondXAdvance: -444)
            .AddPair(v, o, -21, secondXAdvance: 555);
        SfntFace face = SfntFace.Parse(builder.WithGpos(gpos).Build());

        Assert.Equal(-90, face.GetKerning(a, v));
        Assert.Equal(45, face.GetKerning(a, t));
        Assert.Equal(-21, face.GetKerning(v, o));
        Assert.Equal(0, face.GetKerning(a, o));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GposPairPos2ClassPairsAreRead() {
        (SyntheticFontBuilder builder, int a, int v, int t, int o) = CreateKerningBuilder();
        var gpos = new SyntheticGpos {
            PairPosFormat = 2,
            CoverageFormat = 1,
            ClassDefFormat = 1,
        }
            .AddPair(a, v, -80)
            .AddPair(a, t, -20)
            .AddPair(v, o, 30)
            .AddPair(t, a, 10);
        SfntFace face = SfntFace.Parse(builder.WithGpos(gpos).Build());

        Assert.Equal(-80, face.GetKerning(a, v));
        Assert.Equal(-20, face.GetKerning(a, t));
        Assert.Equal(30, face.GetKerning(v, o));
        Assert.Equal(10, face.GetKerning(t, a));
        Assert.Equal(0, face.GetKerning(o, o));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GposPairPos2ClassDefinitionFormat2IsRead() {
        (SyntheticFontBuilder builder, int a, int v, int t, int o) = CreateKerningBuilder();
        var gpos = new SyntheticGpos {
            PairPosFormat = 2,
            CoverageFormat = 2,
            ClassDefFormat = 2,
            ValueFormat1 = 0x0005,
        }
            .AddPair(a, v, -80)
            .AddPair(v, t, -35)
            .AddPair(t, o, 15);
        SfntFace face = SfntFace.Parse(builder.WithGpos(gpos).Build());

        Assert.Equal(-80, face.GetKerning(a, v));
        Assert.Equal(-35, face.GetKerning(v, t));
        Assert.Equal(15, face.GetKerning(t, o));
        Assert.Equal(0, face.GetKerning(o, v));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GposExtensionLookupIsRead() {
        (SyntheticFontBuilder builder, int a, int v, int _, int __) = CreateKerningBuilder();
        var gpos = new SyntheticGpos {
            PairPosFormat = 2,
            ExtensionLookup = true,
        }
            .AddPair(a, v, -77);
        SfntFace face = SfntFace.Parse(builder.WithGpos(gpos).Build());

        Assert.Equal(-77, face.GetKerning(a, v));
    }

    [Theory]
    [InlineData("latn")]
    [InlineData("DFLT")]
    [InlineData("cyrl")]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GposScriptSelectionHonorsLatnThenDfltThenTheFirstScript(string scriptTag) {
        (SyntheticFontBuilder builder, int a, int v, int _, int __) = CreateKerningBuilder();
        var gpos = new SyntheticGpos { ScriptTag = scriptTag }.AddPair(a, v, -66);
        SfntFace face = SfntFace.Parse(builder.WithGpos(gpos).Build());

        Assert.Equal(-66, face.GetKerning(a, v));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GposWinsOverTheKernTable() {
        (SyntheticFontBuilder builder, int a, int v, int t, int o) = CreateKerningBuilder();
        var gpos = new SyntheticGpos().AddPair(a, v, -80);
        SfntFace face = SfntFace.Parse(
            builder
                .WithKernPairs((a, v, 100), (t, o, 50))
                .WithGpos(gpos)
                .Build());

        Assert.Equal(-80, face.GetKerning(a, v));
        Assert.Equal(0, face.GetKerning(t, o));
        Assert.Equal(0, face.GetKerning(a, t));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void KernTableIsUsedWhenGposHasNoKernFeature() {
        (SyntheticFontBuilder builder, int a, int v, int _, int __) = CreateKerningBuilder();
        var gpos = new SyntheticGpos { FeatureTag = "liga" }.AddPair(a, v, -55);
        SfntFace face = SfntFace.Parse(
            builder
                .WithKernPairs((a, v, -80))
                .WithGpos(gpos)
                .Build());

        Assert.Equal(-80, face.GetKerning(a, v));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GposWithoutKernFeatureAndNoKernTableReturnsZero() {
        (SyntheticFontBuilder builder, int a, int v, int _, int __) = CreateKerningBuilder();
        var gpos = new SyntheticGpos { FeatureTag = "liga" }.AddPair(a, v, -55);
        SfntFace face = SfntFace.Parse(builder.WithGpos(gpos).Build());

        Assert.Equal(0, face.GetKerning(a, v));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GposPairWithoutAnXAdvanceContributesZero() {
        (SyntheticFontBuilder builder, int a, int v, int _, int __) = CreateKerningBuilder();
        var gpos = new SyntheticGpos { ValueFormat1 = 0x0001 }.AddPair(a, v, -55);
        SfntFace face = SfntFace.Parse(builder.WithGpos(gpos).Build());

        Assert.Equal(0, face.GetKerning(a, v));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void KerningGlyphIdentifiersOutsideTheFaceAreRejected() {
        (SyntheticFontBuilder builder, int a, int v, int _, int __) = CreateKerningBuilder();
        SfntFace face = SfntFace.Parse(
            builder.WithKernPairs((a, v, -10)).Build());

        Assert.Throws<ArgumentOutOfRangeException>(() => face.GetKerning(-1, v));
        Assert.Throws<ArgumentOutOfRangeException>(() => face.GetKerning(a, face.GlyphCount));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void KernSubtablesFilterUnsupportedVersionAndCoverage() {
        (SyntheticFontBuilder builder, int a, int v, int t, int o) = CreateKerningBuilder();
        byte[] kern = SyntheticKern.Build(
            new SyntheticKernSubtable(1, 0x0001, (a, v, 111)),
            new SyntheticKernSubtable(0, 0x0101, (t, o, 222)),
            new SyntheticKernSubtable(0, 0x0000, (a, o, 333)),
            new SyntheticKernSubtable(0, 0x0001, (a, v, -40)));
        SfntFace face = SfntFace.Parse(builder.WithRawKern(kern).Build());

        Assert.Equal(-40, face.GetKerning(a, v));
        Assert.Equal(0, face.GetKerning(t, o));
        Assert.Equal(0, face.GetKerning(a, o));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void KernDuplicatePairsTakeTheLastSubtable() {
        (SyntheticFontBuilder builder, int a, int v, int _, int __) = CreateKerningBuilder();
        byte[] kern = SyntheticKern.Build(
            new SyntheticKernSubtable(0, 0x0001, (a, v, -10)),
            new SyntheticKernSubtable(0, 0x0001, (a, v, 50)));
        SfntFace face = SfntFace.Parse(builder.WithRawKern(kern).Build());

        Assert.Equal(50, face.GetKerning(a, v));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void KernPairCountBeyondTheCapIsUnsupported() {
        (SyntheticFontBuilder builder, int _, int __, int ___, int ____) = CreateKerningBuilder();
        byte[] kern = BuildOversizedKernTable();
        byte[] faceBytes = builder.WithRawKern(kern).Build();

        long before = GC.GetAllocatedBytesForCurrentThread();
        FontUnsupportedException exception = Assert.Throws<FontUnsupportedException>(
            () => SfntFace.Parse(faceBytes));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Contains("kern", exception.Reason, StringComparison.Ordinal);
        Assert.Contains("1000000", exception.Reason, StringComparison.Ordinal);
        Assert.True(allocated < 16 * 1024 * 1024, $"The rejected kern parse allocated {allocated} bytes.");
    }

    private static byte[] BuildOversizedKernTable() {
        const int subtableCount = 92;
        const int pairCount = 10_920;
        int subtableLength = 14 + (6 * pairCount);
        byte[] kern = new byte[4 + (subtableCount * subtableLength)];
        BinaryPrimitives.WriteUInt16BigEndian(kern, 0);
        BinaryPrimitives.WriteUInt16BigEndian(kern.AsSpan(2), (ushort)subtableCount);
        int position = 4;
        for (int index = 0; index < subtableCount; index++) {
            BinaryPrimitives.WriteUInt16BigEndian(kern.AsSpan(position), 0);
            BinaryPrimitives.WriteUInt16BigEndian(kern.AsSpan(position + 2), (ushort)subtableLength);
            BinaryPrimitives.WriteUInt16BigEndian(kern.AsSpan(position + 4), 0x0001);
            BinaryPrimitives.WriteUInt16BigEndian(kern.AsSpan(position + 6), (ushort)pairCount);
            position += subtableLength;
        }

        return kern;
    }

    private static (SyntheticFontBuilder Builder, int A, int V, int T, int O) CreateKerningBuilder() {
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
