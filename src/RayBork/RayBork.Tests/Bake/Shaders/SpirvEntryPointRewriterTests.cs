using System.Buffers.Binary;
using System.Text;
using RayBork.Bake.Shaders;

namespace RayBork.Tests.Bake.Shaders;

public sealed class SpirvEntryPointRewriterTests {
    private const uint SPIRV_MAGIC = 0x07230203;
    private const uint SPIRV_VERSION = 0x00010500;
    private const uint OP_ENTRY_POINT = 15;
    private const uint OP_CAPABILITY = 17;
    private const uint OP_TYPE_VOID = 19;
    private const uint FUNCTION_ID = 7;
    private const int HEADER_WORD_COUNT = 5;
    private const int ENTRY_POINT_FIXED_WORD_COUNT = 3;

    private static readonly uint[] _interfaceIds = [11, 12, 13];

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RenamingKeepsANonEmptyEntryPointInterfaceList() {
        byte[] module = CreateModule();

        Assert.True(
            SpirvEntryPointRewriter.TryRename(
                module,
                SpirvExecutionModel.Vertex,
                "main",
                "main_vertex",
                out byte[] renamed,
                out string message),
            message);

        // "main" is two words and "main_vertex" is three, so the instruction grows by one word.
        Assert.Equal(module.Length + sizeof(uint), renamed.Length);
        AssertInstructionStreamConsumesExactlyTheModule(renamed);

        uint[] words = ToWords(renamed);
        int entryPointWord = FindEntryPoint(words);
        int wordCount = InstructionWordCount(words[entryPointWord]);
        int nameWordCount = EntryPointNameWordCount("main_vertex");
        Assert.Equal(
            ENTRY_POINT_FIXED_WORD_COUNT + nameWordCount + _interfaceIds.Length,
            wordCount);
        Assert.Equal(
            "main_vertex",
            ReadEntryPointName(words, entryPointWord + ENTRY_POINT_FIXED_WORD_COUNT, nameWordCount));

        int interfaceWord = entryPointWord + ENTRY_POINT_FIXED_WORD_COUNT + nameWordCount;
        uint[] interfaceIds = words[interfaceWord..(entryPointWord + wordCount)];
        Assert.Equal(_interfaceIds.Length, interfaceIds.Length);
        for (int index = 0; index < _interfaceIds.Length; index++) {
            Assert.Equal(_interfaceIds[index], interfaceIds[index]);
        }
    }

    private static byte[] CreateModule() {
        uint[] nameWords = EntryPointNameWords("main");
        var words = new List<uint> {
            SPIRV_MAGIC,
            SPIRV_VERSION,
            0,
            32,
            0,
            ((uint)(ENTRY_POINT_FIXED_WORD_COUNT + nameWords.Length + _interfaceIds.Length) << 16)
                | OP_ENTRY_POINT,
            (uint)SpirvExecutionModel.Vertex,
            FUNCTION_ID,
        };

        words.AddRange(nameWords);
        words.AddRange(_interfaceIds);
        words.Add((2u << 16) | OP_CAPABILITY);
        words.Add(1u);
        words.Add((2u << 16) | OP_TYPE_VOID);
        words.Add(1u);
        return ToBytes(words);
    }

    private static void AssertInstructionStreamConsumesExactlyTheModule(ReadOnlySpan<byte> module) {
        uint[] words = ToWords(module);
        int index = HEADER_WORD_COUNT;
        while (index < words.Length) {
            int wordCount = InstructionWordCount(words[index]);
            Assert.InRange(wordCount, 1, words.Length - index);
            index += wordCount;
        }

        Assert.Equal(words.Length, index);
    }

    private static int FindEntryPoint(uint[] words) {
        for (int index = HEADER_WORD_COUNT; index < words.Length;) {
            int wordCount = InstructionWordCount(words[index]);
            Assert.InRange(wordCount, 1, words.Length - index);
            if (InstructionOpcode(words[index]) == OP_ENTRY_POINT) {
                return index;
            }

            index += wordCount;
        }

        throw new InvalidOperationException("The test module has no OpEntryPoint instruction.");
    }

    private static string ReadEntryPointName(uint[] words, int firstWord, int wordCount) {
        var bytes = new byte[wordCount * sizeof(uint)];
        for (int offset = 0; offset < bytes.Length; offset++) {
            bytes[offset] = (byte)(words[firstWord + (offset / sizeof(uint))]
                >> ((offset % sizeof(uint)) * 8));
        }

        int length = Array.IndexOf(bytes, (byte)0);
        return Encoding.UTF8.GetString(bytes, 0, length < 0 ? bytes.Length : length);
    }

    private static uint[] EntryPointNameWords(string name) {
        byte[] bytes = Encoding.UTF8.GetBytes(name);
        var words = new uint[EntryPointNameWordCount(name)];
        for (int offset = 0; offset < bytes.Length; offset++) {
            words[offset / sizeof(uint)] |= (uint)bytes[offset] << ((offset % sizeof(uint)) * 8);
        }

        return words;
    }

    private static int EntryPointNameWordCount(string name)
        => (Encoding.UTF8.GetByteCount(name) + 1 + (sizeof(uint) - 1)) / sizeof(uint);

    private static int InstructionWordCount(uint instruction) => checked((int)(instruction >> 16));

    private static int InstructionOpcode(uint instruction) => checked((int)(instruction & 0xFFFF));

    private static uint[] ToWords(ReadOnlySpan<byte> module) {
        var words = new uint[module.Length / sizeof(uint)];
        for (int word = 0; word < words.Length; word++) {
            words[word] = BinaryPrimitives.ReadUInt32LittleEndian(
                module.Slice(word * sizeof(uint), sizeof(uint)));
        }

        return words;
    }

    private static byte[] ToBytes(IReadOnlyList<uint> words) {
        var bytes = new byte[words.Count * sizeof(uint)];
        for (int word = 0; word < words.Count; word++) {
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(word * sizeof(uint), sizeof(uint)),
                words[word]);
        }

        return bytes;
    }
}
