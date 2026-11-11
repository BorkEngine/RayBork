using System.Globalization;
using System.Text;
using RayBork.Bake;

namespace RayBork.BuiltInAssets;

/// <summary>
/// Build-time generator for the embedded <c>raybork-builtin.pck</c>.
/// </summary>
/// <remarks>
/// <para>
/// The tool reuses the ordinary bake pipeline, the default processors, and the shared GLSL compiler on
/// the checked-in built-in source tree, then writes the pack as an AOT/trim-safe C# byte array that
/// <c>RayBork.Engine</c> compiles into its assembly. It is JIT and build-only: no runtime project
/// references it and neither the tool nor its packages enter a runtime project's output or publish
/// graph.
/// </para>
/// <para>
/// The pack is always written into a unique temporary output directory, so the checked-in source tree
/// is never used as an output location. The bake's incremental state under the source root is removed
/// after the run so a clean clone stays clean; the temporary directory is removed on every exit path.
/// </para>
/// </remarks>
internal static class Program {
    private const string ASSETS_DIRECTORY_NAME = "Assets";
    private const string PACK_FILE_NAME = "raybork-builtin.pck";
    private const string GENERATED_NAMESPACE = "RayBork.Internal";
    private const string ENGINE_VERTEX_SOURCE = "Present.vert.glsl";
    private const string ENGINE_FRAGMENT_SOURCE = "Present.frag.glsl";
    private const int BYTES_PER_LINE = 12;

    private static int Main(string[] args) {
        try {
            return Run(args);
        } catch (Exception exception) when (exception is not OutOfMemoryException) {
            Console.Error.WriteLine("RayBork.BuiltInAssets: " + exception.Message);
            return 1;
        }
    }

    private static int Run(string[] args) {
        if (Array.IndexOf(args, "--help") >= 0 || Array.IndexOf(args, "-h") >= 0) {
            PrintUsage(Console.Out);
            return 0;
        }

        if (!TryParse(args, out string output, out string sourceRoot, out string engineShaders,
                out int usageExitCode)) {
            return usageExitCode;
        }

        if (!TryGetFullPath(output, out string fullOutput, out string outputProblem)) {
            Console.Error.WriteLine(
                $"RayBork.BuiltInAssets: the output path '{output}' is not a valid path: "
                    + outputProblem);
            return 1;
        }

        if (!TryGetFullPath(sourceRoot, out string fullSourceRoot, out string sourceProblem)) {
            Console.Error.WriteLine(
                $"RayBork.BuiltInAssets: the source root '{sourceRoot}' is not a valid path: "
                    + sourceProblem);
            return 1;
        }

        if (!Directory.Exists(Path.Combine(fullSourceRoot, ASSETS_DIRECTORY_NAME))) {
            Console.Error.WriteLine(
                $"RayBork.BuiltInAssets: the source root '{fullSourceRoot}' has no "
                    + $"'{ASSETS_DIRECTORY_NAME}' directory.");
            return 1;
        }

        if (!TryGetFullPath(engineShaders, out string fullEngineShaders, out string shaderProblem)) {
            Console.Error.WriteLine(
                $"RayBork.BuiltInAssets: the engine shaders directory '{engineShaders}' is not a "
                    + $"valid path: {shaderProblem}");
            return 1;
        }

        if (!Directory.Exists(fullEngineShaders)
            || !File.Exists(Path.Combine(fullEngineShaders, ENGINE_VERTEX_SOURCE))
            || !File.Exists(Path.Combine(fullEngineShaders, ENGINE_FRAGMENT_SOURCE))) {
            Console.Error.WriteLine(
                $"RayBork.BuiltInAssets: the engine shaders directory '{fullEngineShaders}' has no "
                    + $"'{ENGINE_VERTEX_SOURCE}' and '{ENGINE_FRAGMENT_SOURCE}' pair.");
            return 1;
        }

        string temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            $"raybork-builtin-{Guid.NewGuid():N}");

        try {
            int bakeExitCode = Bake(fullSourceRoot, fullEngineShaders, temporaryRoot);
            if (bakeExitCode != 0) {
                Console.Error.WriteLine(
                    $"RayBork.BuiltInAssets: the built-in pack bake failed with exit code "
                        + $"{bakeExitCode.ToString(CultureInfo.InvariantCulture)}.");
                return 1;
            }

            string packPath = Path.Combine(temporaryRoot, PACK_FILE_NAME);
            if (!File.Exists(packPath)) {
                Console.Error.WriteLine(
                    "RayBork.BuiltInAssets: the built-in pack bake reported success but "
                        + $"'{packPath}' was not written.");
                return 1;
            }

            byte[] pack = File.ReadAllBytes(packPath);
            byte[] generated = Generate(pack);
            WriteIfChanged(fullOutput, generated);
            Console.WriteLine(
                $"RayBork.BuiltInAssets: generated '{fullOutput}' from {pack.Length} pack bytes.");
            return 0;
        } finally {
            TryDeleteDirectory(temporaryRoot);
            DeleteIncrementalState(fullSourceRoot);
        }
    }

    private static int Bake(string sourceRoot, string engineShaders, string temporaryRoot) {
        BakeApplication application = new BakeApplicationBuilder()
            .WithProjectRoot(sourceRoot)
            .WithPack(PACK_FILE_NAME, ASSETS_DIRECTORY_NAME)
            .UseDefaultProcessors()
            .AddProcessor(new BuiltInTextureProcessor())
            .AddProcessor(new BuiltInPresentProcessor(engineShaders))
            .Build();

        string[] arguments =
        [
            "bake",
            "--project-root",
            sourceRoot,
            "--output-root",
            temporaryRoot,
            "--mode",
            "debug",
            "--force",
        ];

        return application.RunAsync(arguments).GetAwaiter().GetResult();
    }

    private static byte[] Generate(byte[] pack) {
        ArgumentNullException.ThrowIfNull(pack);

        var builder = new StringBuilder((pack.Length * 6) + 256);
        builder.Append("// <auto-generated/>\n");
        builder.Append("// Generated by RayBork.BuiltInAssets from the built-in asset sources. Do not edit.\n");
        builder.Append("namespace ").Append(GENERATED_NAMESPACE).Append(";\n");
        builder.Append('\n');
        builder.Append("internal static class BuiltInPack {\n");
        builder.Append("    internal static byte[] Bytes => _bytes;\n");
        builder.Append('\n');
        builder.Append("    private static readonly byte[] _bytes = [\n");
        for (int index = 0; index < pack.Length; index++) {
            if (index % BYTES_PER_LINE == 0) {
                builder.Append("        ");
            }

            builder.Append("0x").Append(pack[index].ToString("X2", CultureInfo.InvariantCulture));
            if (index != pack.Length - 1) {
                builder.Append(", ");
            }

            if (index % BYTES_PER_LINE == BYTES_PER_LINE - 1 || index == pack.Length - 1) {
                builder.Append('\n');
            }
        }

        builder.Append("    ];\n");
        builder.Append("}\n");
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(builder.ToString());
    }

    private static void WriteIfChanged(string output, byte[] generated) {
        if (File.Exists(output)) {
            try {
                if (File.ReadAllBytes(output).AsSpan().SequenceEqual(generated)) {
                    // The pack bytes did not change, but a declared input did. Keep the output newer
                    // than every input so the incremental target does not run the generator on every
                    // following build; the content stays byte-identical.
                    File.SetLastWriteTimeUtc(output, DateTime.UtcNow);
                    return;
                }
            } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or NotSupportedException or ArgumentException) {
                // Fall through and rewrite the generated output.
                _ = exception;
            }
        }

        string? directory = Path.GetDirectoryName(output);
        if (!string.IsNullOrEmpty(directory)) {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(output, generated);
    }

    private static bool TryParse(
        string[] args,
        out string output,
        out string sourceRoot,
        out string engineShaders,
        out int exitCode) {
        output = string.Empty;
        sourceRoot = string.Empty;
        engineShaders = string.Empty;
        exitCode = 0;

        for (int index = 0; index < args.Length; index++) {
            switch (args[index]) {
                case "--output":
                    if (!TryTakeValue(args, ref index, "--output", out output)) {
                        exitCode = 2;
                        return false;
                    }

                    break;

                case "--source-root":
                    if (!TryTakeValue(args, ref index, "--source-root", out sourceRoot)) {
                        exitCode = 2;
                        return false;
                    }

                    break;

                case "--engine-shaders":
                    if (!TryTakeValue(args, ref index, "--engine-shaders", out engineShaders)) {
                        exitCode = 2;
                        return false;
                    }

                    break;

                default:
                    Console.Error.WriteLine(
                        $"RayBork.BuiltInAssets: unknown argument '{args[index]}'.");
                    PrintUsage(Console.Error);
                    exitCode = 2;
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(output)) {
            Console.Error.WriteLine("RayBork.BuiltInAssets: --output <file.cs> is required.");
            PrintUsage(Console.Error);
            exitCode = 2;
            return false;
        }

        if (string.IsNullOrWhiteSpace(sourceRoot)) {
            Console.Error.WriteLine("RayBork.BuiltInAssets: --source-root <dir> is required.");
            PrintUsage(Console.Error);
            exitCode = 2;
            return false;
        }

        if (string.IsNullOrWhiteSpace(engineShaders)) {
            Console.Error.WriteLine("RayBork.BuiltInAssets: --engine-shaders <dir> is required.");
            PrintUsage(Console.Error);
            exitCode = 2;
            return false;
        }

        return true;
    }

    private static bool TryTakeValue(
        string[] args,
        ref int index,
        string option,
        out string value) {
        value = string.Empty;
        if (index + 1 >= args.Length) {
            Console.Error.WriteLine($"RayBork.BuiltInAssets: '{option}' requires a value.");
            return false;
        }

        value = args[++index];
        if (string.IsNullOrWhiteSpace(value)) {
            Console.Error.WriteLine($"RayBork.BuiltInAssets: '{option}' requires a non-empty value.");
            return false;
        }

        return true;
    }

    private static bool TryGetFullPath(string path, out string fullPath, out string problem) {
        fullPath = string.Empty;
        problem = string.Empty;
        try {
            fullPath = Path.GetFullPath(path);
            return true;
        } catch (Exception exception) when (exception is ArgumentException or NotSupportedException
            or PathTooLongException) {
            problem = exception.Message;
            return false;
        }
    }

    private static void DeleteIncrementalState(string sourceRoot) {
        // The bake records a manifest under <project root>/obj even for --force runs. This tool owns
        // the built-in source root and always bakes into a temporary output, so that state cannot be
        // reused; remove it to keep the checked-in tree free of generated files.
        TryDeleteDirectory(Path.Combine(sourceRoot, "obj", "raybork", "bake"));
        TryDeleteEmptyDirectory(Path.Combine(sourceRoot, "obj", "raybork"));
        TryDeleteEmptyDirectory(Path.Combine(sourceRoot, "obj"));
    }

    private static void TryDeleteDirectory(string path) {
        try {
            if (Directory.Exists(path)) {
                Directory.Delete(path, recursive: true);
            }
        } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException) {
            Console.Error.WriteLine(
                $"RayBork.BuiltInAssets: could not remove '{path}': {exception.Message}");
        }
    }

    private static void TryDeleteEmptyDirectory(string path) {
        try {
            if (Directory.Exists(path) && Directory.GetFileSystemEntries(path).Length == 0) {
                Directory.Delete(path);
            }
        } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException) {
            Console.Error.WriteLine(
                $"RayBork.BuiltInAssets: could not remove '{path}': {exception.Message}");
        }
    }

    private static void PrintUsage(TextWriter writer) {
        writer.WriteLine(
            "Usage: RayBork.BuiltInAssets --output <file.cs> --source-root <dir> "
                + "--engine-shaders <dir>");
        writer.WriteLine("  --output          generated C# file that embeds raybork-builtin.pck");
        writer.WriteLine("  --source-root     built-in asset project root that contains Assets");
        writer.WriteLine("  --engine-shaders  directory of the engine-owned Present GLSL sources");
    }
}
