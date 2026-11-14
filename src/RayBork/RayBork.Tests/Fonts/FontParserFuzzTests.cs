using System.Buffers.Binary;
using System.Text;
using RayBork.Internal;

namespace RayBork.Tests.Fonts;

/// <summary>
/// Fuzzes the face parser with a fixed-seed deterministic mutation stream, every 1/64th truncation,
/// and random byte flips inside the table directory. Corrupt input must either parse or throw
/// <see cref="FontFormatException"/>; a single parse must stay within a generous allocation bound.
/// </summary>
public sealed class FontParserFuzzTests {
    private const int SEED = 20261114;
    private const int MUTATION_ITERATIONS = 3000;
    private const int DIRECTORY_ITERATIONS = 2000;
    private const int ALLOCATION_ITERATIONS = 200;
    private const long ALLOCATION_BOUND = 16 * 1024 * 1024;

    private static readonly Rune[] _runes = [
        new Rune('A'),
        new Rune('B'),
        new Rune('Z'),
        new Rune(' '),
        new Rune(0x1F600),
        new Rune(0x10FFFF),
    ];

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SeededMutationsNeverEscapeTheDocumentedFailureSurface() {
        byte[] original = CreateFuzzFace();
        var random = new Random(SEED);
        for (int iteration = 0; iteration < MUTATION_ITERATIONS; iteration++) {
            Exercise(Mutate(original, random, iteration), iteration);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EverySixtyFourthTruncationIsControlled() {
        byte[] original = CreateFuzzFace();
        for (int part = 0; part < 64; part++) {
            int length = (int)((long)original.Length * part / 64);
            Exercise(original.AsMemory(0, length), part);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EveryTableDirectoryByteFlipIsControlled() {
        byte[] original = CreateFuzzFace();
        int tableCount = BinaryPrimitives.ReadUInt16BigEndian(original.AsSpan(4));
        int directoryEnd = 12 + (tableCount * 16);
        var random = new Random(SEED + 1);
        for (int iteration = 0; iteration < DIRECTORY_ITERATIONS; iteration++) {
            byte[] mutated = (byte[])original.Clone();
            int position = random.Next(12, directoryEnd);
            mutated[position] ^= (byte)(1 << random.Next(8));
            Exercise(mutated, iteration);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RandomTableRecordFieldWritesAreControlled() {
        byte[] original = CreateFuzzFace();
        int tableCount = BinaryPrimitives.ReadUInt16BigEndian(original.AsSpan(4));
        var random = new Random(SEED + 2);
        for (int iteration = 0; iteration < DIRECTORY_ITERATIONS; iteration++) {
            byte[] mutated = (byte[])original.Clone();
            int record = random.Next(tableCount);
            int field = random.Next(2);
            int offset = 12 + (record * 16) + 8 + (field * 4);
            BinaryPrimitives.WriteUInt32BigEndian(
                mutated.AsSpan(offset),
                (uint)random.NextInt64(0, 1L << 32));
            Exercise(mutated, iteration);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SingleMutatedParseStaysWithinTheAllocationBound() {
        byte[] original = CreateFuzzFace();
        var random = new Random(SEED + 3);
        long maximum = 0;
        for (int iteration = 0; iteration < ALLOCATION_ITERATIONS; iteration++) {
            byte[] mutated = Mutate(original, random, iteration);
            long before = GC.GetAllocatedBytesForCurrentThread();
            try {
                SfntFace.Parse(mutated);
            } catch (FontFormatException) {
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            maximum = Math.Max(maximum, allocated);
        }

        Assert.True(
            maximum < ALLOCATION_BOUND,
            $"A mutated parse allocated {maximum} bytes; the bound is {ALLOCATION_BOUND}.");
    }

    private static byte[] CreateFuzzFace() {
        var builder = SyntheticFontBuilder.Create();
        int glyph = builder.AddSimpleContours(
            600,
            20,
            [
                new SyntheticPoint(0, 0),
                new SyntheticPoint(500, 0),
                new SyntheticPoint(250, 700),
            ],
            [
                new SyntheticPoint(50, 100),
                new SyntheticPoint(200, 300, onCurve: false),
                new SyntheticPoint(350, 100),
            ]);
        int empty = builder.AddEmpty(300);
        int composite = builder.AddComposite(
            700,
            10,
            new FontBounds(0, 0, 600, 700),
            SyntheticComponent.Translate(glyph, 100, 0),
            SyntheticComponent.TwoByTwo(glyph, 20, 20, 0.5f, 0f, 0f, 0.5f));
        builder.WithCmapRecord(3, 1, 4, (0x41, glyph), (0x42, empty));
        builder.WithCmapRecord(3, 10, 12, (0x41, glyph), (0x42, empty), (0x1F600, composite));
        builder.WithKernPairs((glyph, composite, -40), (glyph, empty, 25));
        builder.WithGpos(new SyntheticGpos().AddPair(composite, glyph, -60));
        return builder.Build();
    }

    private static byte[] Mutate(byte[] original, Random random, int iteration) {
        byte[] mutated = (byte[])original.Clone();
        switch (iteration % 8) {
            case 0:
                for (int index = 0; index < 1 + (iteration % 4); index++) {
                    mutated[random.Next(mutated.Length)] ^= (byte)(1 << random.Next(8));
                }

                break;
            case 1:
                return mutated[..random.Next(0, mutated.Length)];
            case 2: {
                int start = random.Next(mutated.Length);
                int length = Math.Min(mutated.Length - start, random.Next(1, 16));
                mutated.AsSpan(start, length).Clear();
                break;
            }

            case 3: {
                int start = random.Next(mutated.Length);
                int length = Math.Min(mutated.Length - start, random.Next(1, 16));
                mutated.AsSpan(start, length).Fill(0xFF);
                break;
            }

            case 4: {
                int tableCount = BinaryPrimitives.ReadUInt16BigEndian(mutated.AsSpan(4));
                if (tableCount > 0) {
                    int record = random.Next(tableCount);
                    int field = random.Next(2);
                    int offset = 12 + (record * 16) + 8 + (field * 4);
                    BinaryPrimitives.WriteUInt32BigEndian(
                        mutated.AsSpan(offset),
                        (uint)random.NextInt64(0, 1L << 32));
                }

                break;
            }

            case 5: {
                int offset = random.Next(4, mutated.Length - 4);
                BinaryPrimitives.WriteUInt32BigEndian(
                    mutated.AsSpan(offset),
                    (uint)random.NextInt64(0, 1L << 32));
                break;
            }

            case 6: {
                int offset = random.Next(0, mutated.Length / 2) * 2;
                BinaryPrimitives.WriteUInt16BigEndian(
                    mutated.AsSpan(offset),
                    (ushort)random.Next(0x10000));
                break;
            }

            default: {
                int tableCount = BinaryPrimitives.ReadUInt16BigEndian(mutated.AsSpan(4));
                int directoryEnd = Math.Min(mutated.Length, 12 + (tableCount * 16));
                if (directoryEnd > 12) {
                    mutated[random.Next(12, directoryEnd)] ^= (byte)random.Next(256);
                }

                break;
            }
        }

        return mutated;
    }

    private static void Exercise(ReadOnlyMemory<byte> faceBytes, int iteration) {
        try {
            SfntFace face = SfntFace.Parse(faceBytes);
            Assert.InRange(face.UnitsPerEm, SfntFace.MIN_UNITS_PER_EM, SfntFace.MAX_UNITS_PER_EM);
            Assert.InRange(face.GlyphCount, 1, 0xFFFF);
            Assert.InRange(face.MappedGlyphCount, 0, 0x11_0000);
            Assert.Contains(face.CmapFormat, new[] { 0, 4, 6, 12 });

            int glyphLimit = Math.Min(face.GlyphCount, 16);
            for (int glyph = 0; glyph < glyphLimit; glyph++) {
                _ = face.GetAdvanceWidth(glyph);
                _ = face.GetLeftSideBearing(glyph);
                if (face.OutlineKind == SfntOutlineKind.TrueType) {
                    _ = face.GetGlyph(glyph);
                }
            }

            foreach (Rune rune in _runes) {
                if (face.TryGetGlyphId(rune, out int glyphId)) {
                    Assert.InRange(glyphId, 1, face.GlyphCount - 1);
                }
            }

            int kerningLimit = Math.Min(face.GlyphCount, 8);
            for (int left = 0; left < kerningLimit; left++) {
                for (int right = 0; right < kerningLimit; right++) {
                    _ = face.GetKerning(left, right);
                }
            }
        } catch (FontFormatException) {
        } catch (Exception exception) {
            Assert.Fail(
                $"Mutation {iteration} leaked {exception.GetType().FullName}: {exception.Message}");
        }
    }
}
