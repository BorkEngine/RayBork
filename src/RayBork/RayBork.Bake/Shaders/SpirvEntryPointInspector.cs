using System.Buffers.Binary;
using System.Text;

namespace RayBork.Bake.Shaders;

/// <summary>The SPIR-V execution model of one shader stage.</summary>
internal enum SpirvExecutionModel : uint {
    /// <summary>The <c>Vertex</c> execution model.</summary>
    Vertex = 0,

    /// <summary>The <c>Fragment</c> execution model.</summary>
    Fragment = 4,
}

/// <summary>
/// Validates a compiled SPIR-V module and finds a stage entry point in its <c>OpEntryPoint</c>
/// instructions.
/// </summary>
/// <remarks>
/// <para>
/// The bake never passes an unchecked shaderc result to the pack. The inspector verifies the SPIR-V
/// magic number, the 1.x version word, the word-count/length invariant, and every instruction's word
/// count before it reads an <c>OpEntryPoint</c> (<c>15</c>) instruction, so a truncated or hand-built
/// module becomes a shader diagnostic instead of an out-of-range read.
/// </para>
/// <para>
/// M3 fixes the entry points to <c>main_vertex</c> and <c>main_fragment</c>; the inspector enforces
/// both the name and the execution model (Vertex for the vertex stage, Fragment for the fragment
/// stage).
/// </para>
/// </remarks>
internal static class SpirvEntryPointInspector {
    private const uint SPIRV_MAGIC = 0x07230203;
    private const int HEADER_WORD_COUNT = 5;
    private const int OP_ENTRY_POINT = 15;
    private const int ENTRY_POINT_FIXED_WORD_COUNT = 3;
    private const uint SUPPORTED_MAJOR_VERSION = 1;
    private const uint MAX_SUPPORTED_MINOR_VERSION = 6;

    /// <summary>Validates a module and finds the requested stage entry point.</summary>
    /// <param name="module">The complete SPIR-V module bytes.</param>
    /// <param name="entryPoint">The required entry point name.</param>
    /// <param name="executionModel">The required execution model.</param>
    /// <param name="message">The failure reason when validation failed, otherwise empty.</param>
    /// <returns><see langword="true"/> when the module declares the entry point for the stage.</returns>
    internal static bool TryValidate(
        ReadOnlySpan<byte> module,
        string entryPoint,
        SpirvExecutionModel executionModel,
        out string message) {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryPoint);

        message = string.Empty;
        if (module.Length < HEADER_WORD_COUNT * sizeof(uint)) {
            message = "the compiled module is too short to contain a SPIR-V header.";
            return false;
        }

        if (module.Length % sizeof(uint) != 0) {
            message = $"the compiled module is {module.Length} bytes, which is not a multiple of 4.";
            return false;
        }

        var words = new uint[module.Length / sizeof(uint)];
        for (int word = 0; word < words.Length; word++) {
            words[word] = BinaryPrimitives.ReadUInt32LittleEndian(
                module.Slice(word * sizeof(uint), sizeof(uint)));
        }

        if (words[0] != SPIRV_MAGIC) {
            message = $"the compiled module has the wrong SPIR-V magic 0x{words[0]:X8}.";
            return false;
        }

        uint version = words[1];
        uint major = version >> 16;
        uint minor = (version >> 8) & 0xFF;
        if (major != SUPPORTED_MAJOR_VERSION || minor > MAX_SUPPORTED_MINOR_VERSION) {
            message = $"the compiled module declares the unsupported SPIR-V version "
                + $"{major}.{minor}.";
            return false;
        }

        for (int index = HEADER_WORD_COUNT; index < words.Length;) {
            uint instruction = words[index];
            int wordCount = checked((int)(instruction >> 16));
            int opcode = checked((int)(instruction & 0xFFFF));
            if (wordCount == 0) {
                message = $"the compiled module has a zero-length instruction at word {index}.";
                return false;
            }

            if (index + wordCount > words.Length) {
                message = $"the compiled module has an instruction at word {index} that overruns "
                    + "the module.";
                return false;
            }

            if (opcode == OP_ENTRY_POINT) {
                if (wordCount < ENTRY_POINT_FIXED_WORD_COUNT + 1) {
                    message = $"the compiled module has a malformed OpEntryPoint at word {index}.";
                    return false;
                }

                uint model = words[index + 1];
                string name = ReadEntryPointName(
                    words,
                    index + ENTRY_POINT_FIXED_WORD_COUNT,
                    wordCount - ENTRY_POINT_FIXED_WORD_COUNT);
                if (string.Equals(name, entryPoint, StringComparison.Ordinal)) {
                    if (model == (uint)executionModel) {
                        return true;
                    }

                    message = $"the entry point '{entryPoint}' declares the execution model "
                        + $"{DescribeModel(model)}; the {DescribeModel((uint)executionModel)} stage "
                        + "requires the matching model.";
                    return false;
                }
            }

            index += wordCount;
        }

        message = $"the compiled module declares no OpEntryPoint named '{entryPoint}' for the "
            + $"{DescribeModel((uint)executionModel)} stage.";
        return false;
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

    private static string DescribeModel(uint model)
        => model switch {
            (uint)SpirvExecutionModel.Vertex => "Vertex",
            (uint)SpirvExecutionModel.Fragment => "Fragment",
            _ => $"execution model {model}",
        };
}
