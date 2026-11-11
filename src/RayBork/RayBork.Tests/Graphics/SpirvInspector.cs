using System.Buffers.Binary;
using System.Text;

namespace RayBork.Tests;

/// <summary>
/// A minimal reader for the SPIR-V header and <c>OpEntryPoint</c> instructions. The shader asset
/// tests use it to assert the compiled modules without a Vulkan driver or a SPIR-V reflection
/// package.
/// </summary>
internal static class SpirvInspector {
    public const uint MAGIC_NUMBER = 0x07230203;
    public const ushort OP_ENTRY_POINT = 15;
    public const uint EXECUTION_MODEL_VERTEX = 0;
    public const uint EXECUTION_MODEL_FRAGMENT = 4;

    private const int HEADER_WORD_COUNT = 5;
    private const int ENTRY_POINT_MINIMUM_WORD_COUNT = 4;

    public static uint ReadWord(ReadOnlySpan<byte> module, int wordIndex) {
        ArgumentOutOfRangeException.ThrowIfNegative(wordIndex);
        if ((wordIndex + 1) * sizeof(uint) > module.Length) {
            throw new ArgumentOutOfRangeException(
                nameof(wordIndex),
                wordIndex,
                "The word index is outside the SPIR-V module.");
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(module.Slice(wordIndex * sizeof(uint), sizeof(uint)));
    }

    public static IReadOnlyList<SpirvEntryPoint> ReadEntryPoints(ReadOnlySpan<byte> module) {
        List<SpirvEntryPoint> entryPoints = [];
        int wordCount = module.Length / sizeof(uint);
        int wordIndex = HEADER_WORD_COUNT;
        while (wordIndex < wordCount) {
            uint instruction = ReadWord(module, wordIndex);
            int instructionWordCount = (int)(instruction >> 16);
            ushort opcode = (ushort)(instruction & 0xFFFF);
            if (instructionWordCount < 1 || wordIndex + instructionWordCount > wordCount) {
                throw new InvalidOperationException($"Malformed SPIR-V instruction at word {wordIndex}.");
            }

            if (opcode == OP_ENTRY_POINT) {
                entryPoints.Add(ReadEntryPoint(module, wordIndex, instructionWordCount));
            }

            wordIndex += instructionWordCount;
        }

        return entryPoints;
    }

    private static SpirvEntryPoint ReadEntryPoint(ReadOnlySpan<byte> module, int instructionIndex, int instructionWordCount) {
        if (instructionWordCount < ENTRY_POINT_MINIMUM_WORD_COUNT) {
            throw new InvalidOperationException($"OpEntryPoint at word {instructionIndex} is too short.");
        }

        uint executionModel = ReadWord(module, instructionIndex + 1);
        uint entryPointId = ReadWord(module, instructionIndex + 2);
        string name = ReadLiteralString(module, instructionIndex + 3, instructionIndex + instructionWordCount);
        return new SpirvEntryPoint(executionModel, entryPointId, name);
    }

    private static string ReadLiteralString(ReadOnlySpan<byte> module, int firstWordIndex, int endWordIndex) {
        StringBuilder builder = new();
        for (int wordIndex = firstWordIndex; wordIndex < endWordIndex; wordIndex++) {
            uint word = ReadWord(module, wordIndex);
            for (int shift = 0; shift < 32; shift += 8) {
                byte value = (byte)((word >> shift) & 0xFF);
                if (value == 0) {
                    return builder.ToString();
                }

                builder.Append((char)value);
            }
        }

        throw new InvalidOperationException("OpEntryPoint has no NUL-terminated name.");
    }
}
