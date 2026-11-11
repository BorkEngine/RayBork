using System.Buffers.Binary;
using RayBork.Internal;

namespace RayBork.Tests.Assets;

public sealed class PackFuzzTests {
    private const string DISPLAY_PATH = "fuzz-mutated.pck";
    private const int SEED = 20261111;
    private const int MUTATION_ITERATIONS = 400;
    private const int HEADER_MUTATION_ITERATIONS = 200;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SeededMutationsNeverEscapeTheDocumentedFailureSurface() {
        byte[] original = PackTestData.BuildFuzzPack();
        var random = new Random(SEED);

        for (int iteration = 0; iteration < MUTATION_ITERATIONS; iteration++) {
            byte[] mutated = Mutate(original, random, iteration);
            Exercise(mutated, iteration);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EveryTruncationIsControlled() {
        byte[] original = PackTestData.BuildFuzzPack();

        for (int length = 0; length < original.Length; length++) {
            Exercise(original[..length], length);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SeededHeaderFieldMutationsAreControlled() {
        byte[] original = PackTestData.BuildFuzzPack();
        int[] headerOffsets = [
            PackFormat.VERSION_OFFSET,
            PackFormat.FLAGS_OFFSET,
            PackFormat.ENTRY_COUNT_OFFSET,
            PackFormat.INDEX_LENGTH_OFFSET,
            PackFormat.INDEX_HASH_OFFSET,
            PackFormat.RESERVED_OFFSET,
        ];
        var random = new Random(SEED + 1);

        for (int iteration = 0; iteration < HEADER_MUTATION_ITERATIONS; iteration++) {
            byte[] mutated = (byte[])original.Clone();
            int offset = headerOffsets[random.Next(headerOffsets.Length)];
            BinaryPrimitives.WriteUInt32LittleEndian(
                mutated.AsSpan(offset),
                (uint)random.NextInt64(0, 1L << 32));
            Exercise(mutated, iteration);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SeededIndexOffsetMutationsAreControlled() {
        byte[] original = PackTestData.BuildFuzzPack();
        var random = new Random(SEED + 2);

        for (int iteration = 0; iteration < HEADER_MUTATION_ITERATIONS; iteration++) {
            byte[] mutated = (byte[])original.Clone();
            BinaryPrimitives.WriteUInt64LittleEndian(
                mutated.AsSpan(PackFormat.INDEX_OFFSET_OFFSET),
                (ulong)random.NextInt64());
            Exercise(mutated, iteration);
        }
    }

    private static byte[] Mutate(byte[] original, Random random, int iteration) {
        byte[] mutated = (byte[])original.Clone();
        int indexLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
            mutated.AsSpan(PackFormat.INDEX_LENGTH_OFFSET)));
        int indexStart = PackFormat.PREFIX_LENGTH;
        int payloadStart = indexStart + indexLength;

        switch (iteration % 7) {
            case 0:
                for (int i = 0; i < 1 + (iteration % 4); i++) {
                    int position = random.Next(mutated.Length);
                    mutated[position] ^= 0xFF;
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
                int field = random.Next(3);
                int offset = field switch {
                    0 => PackFormat.ENTRY_COUNT_OFFSET,
                    1 => PackFormat.INDEX_LENGTH_OFFSET,
                    _ => PackFormat.INDEX_HASH_OFFSET,
                };
                BinaryPrimitives.WriteUInt32LittleEndian(
                    mutated.AsSpan(offset),
                    (uint)random.NextInt64(0, 1L << 32));
                break;
            }
            case 5:
                mutated[random.Next(indexStart, Math.Max(indexStart, mutated.Length))] =
                    (byte)random.Next(256);
                break;
            default:
                if (payloadStart < mutated.Length) {
                    mutated[random.Next(payloadStart, mutated.Length)] = (byte)random.Next(256);
                }

                break;
        }

        return mutated;
    }

    private static void Exercise(byte[] pack, int iteration) {
        try {
            using PackArchive archive = PackArchive.Open(pack.AsMemory(), DISPLAY_PATH);
            foreach (PackEntry entry in archive.Entries) {
                byte[] payload = archive.ReadPayload(entry);
                Assert.Equal(entry.UncompressedSize, (uint)payload.Length);
            }
        } catch (AssetPackException) {
        } catch (AssetLoadException) {
        } catch (ArgumentException) {
        } catch (Exception exception) {
            Assert.Fail(
                $"Mutation {iteration} leaked {exception.GetType().FullName}: {exception.Message}");
        }
    }
}
