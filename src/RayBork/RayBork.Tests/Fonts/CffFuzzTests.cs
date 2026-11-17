using System.Buffers.Binary;
using System.Text;
using RayBork.Internal;

namespace RayBork.Tests.Fonts;

/// <summary>
/// Fuzzes the CFF 1.0 reader with fixed-seed mutations, every 1/64th truncation, exhaustive per-byte
/// corruption of the CFF table, and an allocation bound. A mutated face must either parse or raise
/// <see cref="FontFormatException"/> (malformed) or <see cref="FontUnsupportedException"/> (valid but
/// outside a documented limit); no framework exception may escape, and one parse must stay bounded.
/// </summary>
public sealed class CffFuzzTests
{
    private const int SEED = 20261114;
    private const int MUTATION_ITERATIONS = 2000;
    private const int ALLOCATION_ITERATIONS = 200;
    private const long ALLOCATION_BOUND = 16 * 1024 * 1024;
    private const int CORRUPTION_PATTERNS = 3;

    private static readonly (int Codepoint, int GlyphId)[] _mappings =
    [
        (0x41, 1),
        (0x42, 2),
        (0x43, 3),
        (0x44, 4),
        (0x45, 5),
    ];

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SeededMutationsNeverEscapeTheDocumentedFailureSurface()
    {
        var random = new Random(SEED);
        int iteration = 0;
        foreach (byte[] original in CreateFaces())
        {
            for (int mutation = 0; mutation < MUTATION_ITERATIONS; mutation++)
            {
                Exercise(Mutate(original, random, iteration), iteration++);
            }
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EverySixtyFourthTruncationIsControlled()
    {
        int iteration = 0;
        foreach (byte[] original in CreateFaces())
        {
            for (int part = 0; part < 64; part++)
            {
                int length = (int)((long)original.Length * part / 64);
                Exercise(original.AsMemory(0, length), iteration++);
            }
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EveryCffTableByteCorruptionIsControlled()
    {
        // Every CFF structure — header, INDEXes, Top/Private/Font DICTs, charset, charstrings,
        // subroutines, and FDSelect — lies inside this range, so corrupting each byte exercises all
        // of them. The charstring bytes additionally exercise the Type 2 interpreter.
        int iteration = 0;
        foreach (byte[] original in CreateFaces())
        {
            (int offset, int length) = FindCffTable(original);
            for (int relative = 0; relative < length; relative++)
            {
                for (int pattern = 0; pattern < CORRUPTION_PATTERNS; pattern++)
                {
                    byte[] mutated = (byte[])original.Clone();
                    mutated[offset + relative] = pattern switch
                    {
                        0 => 0x00,
                        1 => 0x7F,
                        _ => 0xFF,
                    };
                    Exercise(mutated, iteration++);
                }
            }
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ACorruptCffParseStaysWithinTheAllocationBound()
    {
        byte[][] faces = CreateFaces();
        var random = new Random(SEED + 1);
        long maximum = 0;
        int iteration = 0;
        for (int index = 0; index < ALLOCATION_ITERATIONS; index++)
        {
            byte[] original = faces[index % faces.Length];
            byte[] mutated = Mutate(original, random, iteration++);
            long before = GC.GetAllocatedBytesForCurrentThread();
            try
            {
                SfntFace face = SfntFace.Parse(mutated);
                foreach (int glyphId in SampleGlyphIds(face.GlyphCount))
                {
                    _ = face.GetGlyph(glyphId);
                }
            }
            catch (FontFormatException) { }
            catch (FontUnsupportedException) { }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            maximum = Math.Max(maximum, allocated);
        }

        Assert.True(
            maximum < ALLOCATION_BOUND,
            $"A mutated CFF parse allocated {maximum} bytes; the bound is {ALLOCATION_BOUND}."
        );
    }

    private static byte[][] CreateFaces() =>
        [CreateLatinFace(), CreateCidFace(), CreateSharedFontDictFace()];

    private static byte[] CreateSharedFontDictFace()
    {
        byte[][] subrs = new byte[128][];
        for (int index = 0; index < subrs.Length; index++)
        {
            subrs[index] = SyntheticCffCharStrings.Return();
        }

        subrs[0] = SyntheticCffCharStrings.Concat(
            SyntheticCffCharStrings.RMoveTo(1, 2),
            SyntheticCffCharStrings.RLineTo(3, 4),
            SyntheticCffCharStrings.Return()
        );
        SyntheticCff cff = SyntheticCff.Create();
        cff.AddGlyph(SyntheticCffCharStrings.EndChar());
        cff.AsCid();
        int first = cff.AddFontDict(subrs);
        for (int index = 1; index < 32; index++)
        {
            cff.AddSharedFontDict(first);
        }

        cff.WithFdSelectFormat3((0, 0), (1, 0), (2, 1));
        cff.AddGlyph(
            SyntheticCffCharStrings.Concat(
                SyntheticCffCharStrings.CallSubr(-107),
                SyntheticCffCharStrings.EndChar()
            )
        );
        cff.AddGlyph(
            SyntheticCffCharStrings.Concat(
                SyntheticCffCharStrings.CallSubr(-107),
                SyntheticCffCharStrings.EndChar()
            )
        );
        return cff.BuildFace((0x41, 1), (0x42, 2));
    }

    private static byte[] CreateLatinFace()
    {
        SyntheticCff cff = SyntheticCff.Create();
        cff.AddGlyph(SyntheticCffCharStrings.EndChar());
        cff.AddGlyph(
            SyntheticCffCharStrings.Concat(
                SyntheticCffCharStrings.RMoveTo(10, 20),
                SyntheticCffCharStrings.RRCurveTo(100, 200, -50, -100, 20, 300),
                SyntheticCffCharStrings.HLineTo(-50, 40),
                SyntheticCffCharStrings.EndChar()
            )
        );
        cff.AddGlyph(
            SyntheticCffCharStrings.Concat(
                SyntheticCffCharStrings.HStemHm(0, 20, 0, 30),
                SyntheticCffCharStrings.VStemHm(0, 10),
                SyntheticCffCharStrings.HintMask([0x07]),
                SyntheticCffCharStrings.RMoveTo(0, 0),
                SyntheticCffCharStrings.RLineTo(50, 0),
                SyntheticCffCharStrings.CntrMask([0x03]),
                SyntheticCffCharStrings.RLineTo(-50, 0),
                SyntheticCffCharStrings.EndChar()
            )
        );
        cff.AddGlyph(
            SyntheticCffCharStrings.Concat(
                SyntheticCffCharStrings.CallSubr(-107),
                SyntheticCffCharStrings.CallGSubr(-107),
                SyntheticCffCharStrings.EndChar()
            )
        );
        cff.AddGlyph(
            SyntheticCffCharStrings.Concat(
                SyntheticCffCharStrings.RMoveTo(0, 0),
                SyntheticCffCharStrings.HFlex(10, 20, 30, 40, 50, 60, 70),
                SyntheticCffCharStrings.VHCurveTo(10, 20, 30, 40, 50),
                SyntheticCffCharStrings.EndChar()
            )
        );
        cff.AddGlyph(
            SyntheticCffCharStrings.Concat(
                SyntheticCffCharStrings.RMoveTo(100, 50),
                SyntheticCffCharStrings.RLineTo(20, 0),
                SyntheticCffCharStrings.EndChar()
            )
        );
        cff.WithPrivateWidths(500, 400);
        cff.WithLocalSubrs(
            SyntheticCffCharStrings.Concat(
                SyntheticCffCharStrings.RMoveTo(5, 6),
                SyntheticCffCharStrings.RLineTo(20, 0),
                SyntheticCffCharStrings.Return()
            )
        );
        cff.WithGlobalSubrs(
            SyntheticCffCharStrings.Concat(
                SyntheticCffCharStrings.RLineTo(0, 30),
                SyntheticCffCharStrings.Return()
            )
        );
        cff.WithCharsetFormat(1, 34, 35, 36, 37, 38);
        cff.WithFontMatrix(0.0012, 0, 0, 0.0012, 0, 0);
        return cff.BuildFace(_mappings);
    }

    private static byte[] CreateCidFace()
    {
        SyntheticCff cff = SyntheticCff.Create();
        cff.AddGlyph(SyntheticCffCharStrings.EndChar());
        cff.AsCid();
        cff.AddFontDict(
            SyntheticCffCharStrings.Concat(
                SyntheticCffCharStrings.RMoveTo(10, 10),
                SyntheticCffCharStrings.RLineTo(20, 0),
                SyntheticCffCharStrings.Return()
            )
        );
        cff.AddFontDict(
            SyntheticCffCharStrings.Concat(
                SyntheticCffCharStrings.RMoveTo(300, 40),
                SyntheticCffCharStrings.RLineTo(10, 0),
                SyntheticCffCharStrings.Return()
            )
        );
        cff.WithFdSelectFormat3((0, 0), (2, 0), (4, 1));
        cff.AddGlyph(
            SyntheticCffCharStrings.Concat(
                SyntheticCffCharStrings.CallSubr(-107),
                SyntheticCffCharStrings.EndChar()
            )
        );
        cff.AddGlyph(
            SyntheticCffCharStrings.Concat(
                SyntheticCffCharStrings.HStem(0, 10),
                SyntheticCffCharStrings.CallSubr(-107),
                SyntheticCffCharStrings.EndChar()
            )
        );
        cff.AddGlyph(
            SyntheticCffCharStrings.Concat(
                SyntheticCffCharStrings.CallSubr(-107),
                SyntheticCffCharStrings.EndChar()
            )
        );
        cff.AddGlyph(
            SyntheticCffCharStrings.Concat(
                SyntheticCffCharStrings.CallSubr(-107),
                SyntheticCffCharStrings.EndChar()
            )
        );
        cff.AddGlyph(
            SyntheticCffCharStrings.Concat(
                SyntheticCffCharStrings.RMoveTo(1, 2),
                SyntheticCffCharStrings.RLineTo(3, 4),
                SyntheticCffCharStrings.EndChar()
            )
        );
        return cff.BuildFace(_mappings);
    }

    private static byte[] Mutate(byte[] original, Random random, int iteration)
    {
        byte[] mutated = (byte[])original.Clone();
        switch (iteration % 8)
        {
            case 0:
                for (int index = 0; index < 1 + (iteration % 6); index++)
                {
                    mutated[random.Next(mutated.Length)] ^= (byte)(1 << random.Next(8));
                }

                break;
            case 1:
                return mutated[..random.Next(0, mutated.Length)];
            case 2:
            {
                int start = random.Next(mutated.Length);
                int length = Math.Min(mutated.Length - start, random.Next(1, 16));
                mutated.AsSpan(start, length).Clear();
                break;
            }

            case 3:
            {
                int start = random.Next(mutated.Length);
                int length = Math.Min(mutated.Length - start, random.Next(1, 16));
                mutated.AsSpan(start, length).Fill(0xFF);
                break;
            }

            case 4:
            {
                int offset = random.Next(0, mutated.Length - 4);
                BinaryPrimitives.WriteUInt32BigEndian(
                    mutated.AsSpan(offset),
                    (uint)random.NextInt64(0, 1L << 32)
                );
                break;
            }

            case 5:
            {
                int offset = random.Next(0, mutated.Length - 2);
                BinaryPrimitives.WriteUInt16BigEndian(
                    mutated.AsSpan(offset),
                    (ushort)random.Next(0x10000)
                );
                break;
            }

            case 6:
            {
                int offset = random.Next(0, mutated.Length - 1);
                mutated[offset] = (byte)random.Next(256);
                mutated[offset + 1] = (byte)random.Next(256);
                break;
            }

            default:
            {
                (int offset, int length) = FindCffTable(mutated);
                if (length > 0)
                {
                    mutated[offset + random.Next(length)] ^= (byte)(1 << random.Next(8));
                }

                break;
            }
        }

        return mutated;
    }

    private static void Exercise(ReadOnlyMemory<byte> faceBytes, int iteration)
    {
        try
        {
            SfntFace face = SfntFace.Parse(faceBytes);
            Assert.InRange(face.UnitsPerEm, SfntFace.MIN_UNITS_PER_EM, SfntFace.MAX_UNITS_PER_EM);
            Assert.InRange(face.GlyphCount, 1, 0xFFFF);
            Assert.Contains(face.OutlineKind, new[] { SfntOutlineKind.Cff, SfntOutlineKind.Cff2 });

            if (face.OutlineKind == SfntOutlineKind.Cff2)
            {
                FontUnsupportedException exception = Assert.Throws<FontUnsupportedException>(() =>
                    face.GetGlyph(0)
                );
                Assert.Contains("CFF2", exception.Reason, StringComparison.Ordinal);
                return;
            }

            foreach (int glyphId in SampleGlyphIds(face.GlyphCount))
            {
                _ = face.GetAdvanceWidth(glyphId);
                _ = face.GetLeftSideBearing(glyphId);
                _ = face.GetGlyph(glyphId);
            }

            if (face.TryGetGlyphId(new Rune('A'), out int mapped))
            {
                if (face.GlyphCount > 1)
                {
                    Assert.InRange(mapped, 1, face.GlyphCount - 1);
                }

                _ = face.GetGlyph(mapped);
            }
        }
        catch (FontFormatException) { }
        catch (FontUnsupportedException) { }
        catch (Exception exception)
        {
            Assert.Fail(
                $"Mutation {iteration} leaked {exception.GetType().FullName}: {exception.Message}"
            );
        }
    }

    private static (int Offset, int Length) FindCffTable(ReadOnlySpan<byte> face)
    {
        int tableCount = BinaryPrimitives.ReadUInt16BigEndian(face[4..]);
        for (int index = 0; index < tableCount; index++)
        {
            int record = 12 + (index * 16);
            if (Encoding.ASCII.GetString(face.Slice(record, 4)) == "CFF ")
            {
                return (
                    (int)BinaryPrimitives.ReadUInt32BigEndian(face[(record + 8)..]),
                    (int)BinaryPrimitives.ReadUInt32BigEndian(face[(record + 12)..])
                );
            }
        }

        return (0, 0);
    }

    private static int[] SampleGlyphIds(int glyphCount)
    {
        if (glyphCount <= 0)
        {
            return [];
        }

        if (glyphCount <= 8)
        {
            return [.. Enumerable.Range(0, glyphCount)];
        }

        return [0, 1, glyphCount / 2, glyphCount - 2, glyphCount - 1];
    }
}
