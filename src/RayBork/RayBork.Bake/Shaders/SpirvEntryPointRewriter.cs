using System.Buffers.Binary;
using System.Text;

namespace RayBork.Bake.Shaders;

/// <summary>
/// Rewrites the name of one SPIR-V <c>OpEntryPoint</c> instruction.
/// </summary>
/// <remarks>
/// <para>
/// shaderc ignores the entry point name for GLSL input and always compiles the source's <c>main</c>
/// function. The shared compiler therefore renames the authoring entry point to <c>main</c> with a
/// preprocessor macro before compilation and uses this helper to restore the requested name in the
/// compiled module. The result is a module whose <c>OpEntryPoint</c> name is the fixed M3 entry point,
/// which is what Vulkan pipeline creation consumes.
/// </para>
/// <para>
/// The rewrite keeps the module's instruction stream valid: it replaces the target instruction with a
/// new one whose word count covers the new null-terminated name and copies the interface list
/// unchanged. The inspector validates the rewritten module before it reaches the pack.
/// </para>
/// </remarks>
internal static class SpirvEntryPointRewriter {
    private const uint SPIRV_MAGIC = 0x07230203;
    private const int HEADER_WORD_COUNT = 5;
    private const int OP_ENTRY_POINT = 15;
    private const int ENTRY_POINT_FIXED_WORD_COUNT = 3;

    /// <summary>Renames the module's entry point for one stage.</summary>
    /// <param name="module">The compiled SPIR-V module bytes.</param>
    /// <param name="executionModel">The execution model whose entry point is renamed.</param>
    /// <param name="expectedName">The compiled entry point name.</param>
    /// <param name="requestedName">The authoring entry point name to store.</param>
    /// <param name="renamed">The rewritten module bytes when the rename succeeded.</param>
    /// <param name="message">The failure reason when the rename did not happen.</param>
    /// <returns><see langword="true"/> when the entry point was renamed.</returns>
    internal static bool TryRename(
        ReadOnlySpan<byte> module,
        SpirvExecutionModel executionModel,
        string expectedName,
        string requestedName,
        out byte[] renamed,
        out string message) {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedName);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedName);

        renamed = [];
        message = string.Empty;

        if (module.Length < HEADER_WORD_COUNT * sizeof(uint) || module.Length % sizeof(uint) != 0) {
            message = "the compiled module has no complete SPIR-V header.";
            return false;
        }

        var words = new uint[module.Length / sizeof(uint)];
        for (int word = 0; word < words.Length; word++) {
            words[word] = BinaryPrimitives.ReadUInt32LittleEndian(
                module.Slice(word * sizeof(uint), sizeof(uint)));
        }

        if (words[0] != SPIRV_MAGIC) {
            message = "the compiled module has the wrong SPIR-V magic.";
            return false;
        }

        var output = new List<uint>(words.Length + 8);
        for (int index = 0; index < HEADER_WORD_COUNT; index++) {
            output.Add(words[index]);
        }

        bool renamedEntryPoint = false;
        for (int index = HEADER_WORD_COUNT; index < words.Length;) {
            uint instruction = words[index];
            int wordCount = checked((int)(instruction >> 16));
            int opcode = checked((int)(instruction & 0xFFFF));
            if (wordCount == 0 || index + wordCount > words.Length) {
                message = "the compiled module has a malformed instruction stream.";
                return false;
            }

            if (opcode == OP_ENTRY_POINT
                && !renamedEntryPoint
                && wordCount >= ENTRY_POINT_FIXED_WORD_COUNT + 1
                && words[index + 1] == (uint)executionModel) {
                string name = ReadEntryPointName(
                    words,
                    index + ENTRY_POINT_FIXED_WORD_COUNT,
                    wordCount - ENTRY_POINT_FIXED_WORD_COUNT);
                if (string.Equals(name, expectedName, StringComparison.Ordinal)) {
                    WriteEntryPointInstruction(
                        output,
                        instruction,
                        words,
                        index,
                        wordCount,
                        requestedName);
                    renamedEntryPoint = true;
                    index += wordCount;
                    continue;
                }
            }

            for (int offset = 0; offset < wordCount; offset++) {
                output.Add(words[index + offset]);
            }

            index += wordCount;
        }

        if (!renamedEntryPoint) {
            message = $"the compiled module has no '{expectedName}' entry point for the "
                + $"{executionModel} stage.";
            return false;
        }

        var bytes = new byte[output.Count * sizeof(uint)];
        for (int word = 0; word < output.Count; word++) {
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(word * sizeof(uint), sizeof(uint)),
                output[word]);
        }

        renamed = bytes;
        return true;
    }

    private static void WriteEntryPointInstruction(
        List<uint> output,
        uint instruction,
        uint[] words,
        int index,
        int wordCount,
        string requestedName) {
        int oldNameWordCount = EntryPointNameWordCount(
            words,
            index + ENTRY_POINT_FIXED_WORD_COUNT,
            wordCount - ENTRY_POINT_FIXED_WORD_COUNT);
        byte[] nameBytes = Encoding.UTF8.GetBytes(requestedName);
        int newNameWordCount = (nameBytes.Length + 1 + (sizeof(uint) - 1)) / sizeof(uint);
        int newWordCount = ENTRY_POINT_FIXED_WORD_COUNT + newNameWordCount;

        output.Add(((uint)newWordCount << 16) | (instruction & 0xFFFF));
        output.Add(words[index + 1]);
        output.Add(words[index + 2]);

        var nameWords = new uint[newNameWordCount];
        for (int offset = 0; offset < nameBytes.Length; offset++) {
            nameWords[offset / 4] |= (uint)nameBytes[offset] << ((offset % 4) * 8);
        }

        output.AddRange(nameWords);

        int interfaceStart = index + ENTRY_POINT_FIXED_WORD_COUNT + oldNameWordCount;
        for (int interfaceWord = interfaceStart; interfaceWord < index + wordCount; interfaceWord++) {
            output.Add(words[interfaceWord]);
        }
    }

    private static int EntryPointNameWordCount(uint[] words, int firstWord, int availableWords) {
        int byteCount = availableWords * sizeof(uint);
        for (int offset = 0; offset < byteCount; offset++) {
            uint word = words[firstWord + (offset / 4)];
            if (((word >> ((offset % 4) * 8)) & 0xFF) == 0) {
                return (offset + 1 + (sizeof(uint) - 1)) / sizeof(uint);
            }
        }

        return availableWords;
    }

    private static string ReadEntryPointName(uint[] words, int firstWord, int wordCount) {
        int byteCount = wordCount * sizeof(uint);
        var bytes = new byte[byteCount];
        for (int offset = 0; offset < byteCount; offset++) {
            uint word = words[firstWord + (offset / 4)];
            bytes[offset] = (byte)(word >> ((offset % 4) * 8));
        }

        int length = Array.IndexOf(bytes, (byte)0);
        if (length < 0) {
            length = bytes.Length;
        }

        return Encoding.UTF8.GetString(bytes, 0, length);
    }
}
