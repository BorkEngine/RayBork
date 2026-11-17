using System.Buffers.Binary;

namespace RayBork.Tests.Fonts;

/// <summary>
/// Builds the structured hostile faces the parser allocation and fuzz tests use: aliased GPOS
/// references and subtables, a composite-fanout fixture, cap-crossing lookup counts, dense cmap and
/// kern tables, and huge declared counts over short payloads. None of these bytes come from a real
/// font.
/// </summary>
internal static class HostileFontFaces
{
    internal static IReadOnlyList<(string Name, byte[] Face)> All() =>
        [
            ("aliased-gpos", CreateAliasedGposFace()),
            ("aliased-lookup-list", CreateAliasedLookupListFace()),
            ("aliased-positionings", CreateAliasedPositioningFace()),
            ("composite-fanout", CreateCompositeFanoutFace()),
            ("too-many-lookups", CreateTooManyLookupFace()),
            ("dense-cmap", CreateDenseCmapFace()),
            ("dense-kern", CreateDenseKernFace()),
            ("huge-cmap-count", CreateHugeDeclaredCmapFace()),
            ("huge-kern-count", CreateHugeDeclaredKernFace()),
            ("huge-gpos-lookup-count", CreateHugeGposLookupCountFace()),
        ];

    internal static byte[] CreateAliasedGposFace()
    {
        (SyntheticFontBuilder builder, int _, int __) = CreateBaseFace();
        var gpos = new SyntheticGpos();
        gpos.AddLookup();
        gpos.AddFeature("kern", Enumerable.Repeat(0, 32_768).ToArray());
        gpos.AddScript("latn", 0);
        return builder.WithGpos(gpos).Build();
    }

    /// <summary>
    /// Corpus coverage for the pre-hardening aliasing baseline: 64 lookup-list entries all pointing
    /// at one lookup, that lookup holding 8,192 aliased subtable records. Before the absolute-offset
    /// dedupe this multiplied into roughly 524,000 positionings; the hardened parser resolves one
    /// lookup and one positioning. Cap-crossing tests are the current regression tests.
    /// </summary>
    internal static byte[] CreateAliasedLookupListFace()
    {
        (SyntheticFontBuilder builder, int a, int v) = CreateBaseFace();
        var gpos = new SyntheticGpos
        {
            SubtablesPerLookup = 8192,
            AliasLookupSubtables = true,
            AliasLookupListEntries = true,
        };
        for (int index = 0; index < 64; index++)
        {
            gpos.AddLookup((a, v, -10));
        }

        gpos.AddFeature("kern", [.. Enumerable.Range(0, 64)]);
        gpos.AddScript("latn", 0);
        return builder.WithGpos(gpos).Build();
    }

    /// <summary>Fifteen levels of eight-way composites over an empty leaf glyph.</summary>
    internal static byte[] CreateCompositeFanoutFace()
    {
        var builder = SyntheticFontBuilder.Create();
        int glyph = builder.AddEmpty(300);
        for (int level = 0; level < 15; level++)
        {
            SyntheticComponent[] components = new SyntheticComponent[8];
            for (int component = 0; component < components.Length; component++)
            {
                components[component] = SyntheticComponent.Translate(glyph, component * 5, 0);
            }

            glyph = builder.AddComposite(700, 10, components);
        }

        return builder.WithCmap(4, (0x41, glyph)).Build();
    }

    internal static byte[] CreateAliasedPositioningFace()
    {
        (SyntheticFontBuilder builder, int a, int v) = CreateBaseFace();
        var gpos = new SyntheticGpos { SubtablesPerLookup = 1000, AliasLookupSubtables = true };
        gpos.AddLookup((a, v, -10));
        gpos.AddFeature("kern", 0);
        gpos.AddScript("latn", 0);
        return builder.WithGpos(gpos).Build();
    }

    internal static byte[] CreateTooManyLookupFace()
    {
        (SyntheticFontBuilder builder, int _, int __) = CreateBaseFace();
        var gpos = new SyntheticGpos();
        for (int index = 0; index < 65; index++)
        {
            gpos.AddLookup();
        }

        gpos.AddFeature("kern", [.. Enumerable.Range(0, 65)]);
        gpos.AddScript("latn", 0);
        return builder.WithGpos(gpos).Build();
    }

    internal static byte[] CreateDenseCmapFace()
    {
        var builder = SyntheticFontBuilder.Create();
        int[] glyphs = new int[4];
        for (int index = 0; index < glyphs.Length; index++)
        {
            glyphs[index] = AddTriangle(builder);
        }

        (int Codepoint, int GlyphId)[] mappings = new (int, int)[4096];
        for (int index = 0; index < mappings.Length; index++)
        {
            mappings[index] = (0x20 + index, glyphs[index % glyphs.Length]);
        }

        return builder.WithCmap(12, mappings).Build();
    }

    private static byte[] CreateDenseKernFace()
    {
        var builder = SyntheticFontBuilder.Create();
        int[] glyphs = new int[64];
        for (int index = 0; index < glyphs.Length; index++)
        {
            glyphs[index] = builder.AddEmpty(500);
        }

        (int Left, int Right, int Value)[] pairs = new (int, int, int)[4096];
        int pair = 0;
        for (int left = 0; left < glyphs.Length && pair < pairs.Length; left += 8)
        {
            for (int right = 0; right < glyphs.Length && pair < pairs.Length; right += 8)
            {
                pairs[pair++] = (glyphs[left], glyphs[right], -((left + right) % 64));
            }
        }

        while (pair < pairs.Length)
        {
            pairs[pair] = (
                glyphs[pair % glyphs.Length],
                glyphs[(pair * 7) % glyphs.Length],
                pair % 32
            );
            pair++;
        }

        builder.WithKernPairs(pairs);
        builder.WithCmap(4, (0x41, glyphs[0]));
        return builder.Build();
    }

    private static byte[] CreateHugeDeclaredCmapFace()
    {
        byte[] cmap = new byte[28];
        BinaryPrimitives.WriteUInt16BigEndian(cmap, 0);
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(4), 3);
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(6), 10);
        BinaryPrimitives.WriteUInt32BigEndian(cmap.AsSpan(8), 12);
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(12), 12);
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(14), 0);
        BinaryPrimitives.WriteUInt32BigEndian(cmap.AsSpan(16), 16);
        BinaryPrimitives.WriteUInt32BigEndian(cmap.AsSpan(20), 0);
        BinaryPrimitives.WriteUInt32BigEndian(cmap.AsSpan(24), 0x00FF_FFFF);
        (SyntheticFontBuilder builder, int _, int __) = CreateBaseFace();
        return builder.WithRawCmap(cmap).Build();
    }

    private static byte[] CreateHugeDeclaredKernFace()
    {
        byte[] kern = new byte[24];
        BinaryPrimitives.WriteUInt16BigEndian(kern, 0);
        BinaryPrimitives.WriteUInt16BigEndian(kern.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(kern.AsSpan(4), 0);
        BinaryPrimitives.WriteUInt16BigEndian(kern.AsSpan(6), 20);
        BinaryPrimitives.WriteUInt16BigEndian(kern.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt16BigEndian(kern.AsSpan(10), 0xFFFF);
        (SyntheticFontBuilder builder, int _, int __) = CreateBaseFace();
        return builder.WithRawKern(kern).Build();
    }

    private static byte[] CreateHugeGposLookupCountFace()
    {
        (SyntheticFontBuilder builder, int a, int v) = CreateBaseFace();
        var gpos = new SyntheticGpos().AddPair(a, v, -10);
        byte[] faceBytes = builder.WithGpos(gpos).Build();
        SyntheticTableRecord gposRecord = SyntheticFontBuilder.FindTable(faceBytes, "GPOS");
        int lookupListOffset = BinaryPrimitives.ReadUInt16BigEndian(
            faceBytes.AsSpan(gposRecord.Offset + 8)
        );
        BinaryPrimitives.WriteUInt16BigEndian(
            faceBytes.AsSpan(gposRecord.Offset + lookupListOffset),
            0xFFFF
        );
        return faceBytes;
    }

    private static (SyntheticFontBuilder Builder, int A, int V) CreateBaseFace()
    {
        var builder = SyntheticFontBuilder.Create();
        int a = AddTriangle(builder);
        int v = AddTriangle(builder);
        builder.WithCmap(4, (0x41, a), (0x56, v));
        return (builder, a, v);
    }

    private static int AddTriangle(SyntheticFontBuilder builder) =>
        builder.AddSimple(
            600,
            10,
            new SyntheticPoint(0, 0),
            new SyntheticPoint(500, 0),
            new SyntheticPoint(250, 700)
        );
}
