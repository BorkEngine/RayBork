using System.Text;
using Silk.NET.Shaderc;

namespace RayBork.Bake.Shaders;

/// <summary>The reusable GLSL-to-SPIR-V compiler built on Silk.NET.Shaderc.</summary>
/// <remarks>
/// Compiles use GLSL input, a Vulkan 1.2 target, performance optimization, and warnings as errors.
/// A requested entry point is renamed to <c>main</c> by macro and restored in <c>OpEntryPoint</c>.
/// </remarks>
internal static unsafe class GlslCompiler
{
    private const string GLSL_ENTRY_POINT = "main";

    /// <summary>The process-wide shaderc API, kept loaded for the process lifetime.</summary>
    private static readonly Lazy<Shaderc> _shadercApi = new(static () => Shaderc.GetApi());

    /// <summary>Compiles one GLSL source into a SPIR-V module.</summary>
    /// <remarks>
    /// A rejected source returns the shaderc diagnostics in <paramref name="message"/>; an environment
    /// failure is reported the same way.
    /// </remarks>
    /// <returns><see langword="true"/> when the source compiled.</returns>
    internal static bool TryCompile(
        string source,
        string fileName,
        ShaderKind kind,
        string entryPoint,
        out byte[] spirV,
        out string message
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryPoint);

        spirV = [];
        message = string.Empty;

        SpirvExecutionModel? executionModel =
            entryPoint == GLSL_ENTRY_POINT
                ? null
                : kind switch
                {
                    ShaderKind.GlslVertexShader => SpirvExecutionModel.Vertex,
                    ShaderKind.GlslFragmentShader => SpirvExecutionModel.Fragment,
                    _ => null,
                };
        bool renamesEntryPoint = executionModel is not null;
        if (entryPoint != GLSL_ENTRY_POINT && executionModel is null)
        {
            message =
                $"The entry point '{entryPoint}' is supported only for GLSL vertex and "
                + "fragment shaders.";
            return false;
        }

        if (renamesEntryPoint && !IsIdentifier(entryPoint))
        {
            message = $"The entry point '{entryPoint}' is not a valid GLSL identifier.";
            return false;
        }

        if (renamesEntryPoint && !ContainsIdentifier(source, entryPoint))
        {
            message = $"The shader source does not declare the entry point '{entryPoint}'.";
            return false;
        }

        Shaderc? shaderc = null;
        Compiler* compiler = null;
        CompileOptions* options = null;
        CompilationResult* result = null;
        try
        {
            shaderc = _shadercApi.Value;
            compiler = shaderc.CompilerInitialize();
            options = shaderc.CompileOptionsInitialize();
            if (compiler is null || options is null)
            {
                message = "Shaderc could not create a compiler.";
                return false;
            }

            shaderc.CompileOptionsSetSourceLanguage(options, SourceLanguage.Glsl);
            shaderc.CompileOptionsSetTargetEnv(
                options,
                TargetEnv.Vulkan,
                (uint)EnvVersion.Vulkan12
            );
            shaderc.CompileOptionsSetOptimizationLevel(options, OptimizationLevel.Performance);
            shaderc.CompileOptionsSetWarningsAsErrors(options);
            if (renamesEntryPoint)
            {
                shaderc.CompileOptionsAddMacroDefinition(
                    options,
                    entryPoint,
                    (nuint)Encoding.UTF8.GetByteCount(entryPoint),
                    GLSL_ENTRY_POINT,
                    (nuint)GLSL_ENTRY_POINT.Length
                );
            }

            result = shaderc.CompileIntoSpv(
                compiler,
                source,
                (nuint)Encoding.UTF8.GetByteCount(source),
                kind,
                fileName,
                entryPoint,
                options
            );
            if (result is null)
            {
                message = $"Shaderc returned no result for '{fileName}'.";
                return false;
            }

            CompilationStatus status = shaderc.ResultGetCompilationStatus(result);
            string diagnostics = shaderc.ResultGetErrorMessageS(result) ?? string.Empty;
            if (status != CompilationStatus.Success)
            {
                message = string.IsNullOrWhiteSpace(diagnostics)
                    ? $"Shader compilation failed for '{fileName}' with {status}."
                    : diagnostics.Trim();
                return false;
            }

            nuint length = shaderc.ResultGetLength(result);
            byte* bytes = shaderc.ResultGetBytes(result);
            if (bytes is null || length == 0)
            {
                message = $"Shader '{fileName}' produced no SPIR-V.";
                return false;
            }

            spirV = new ReadOnlySpan<byte>(bytes, checked((int)length)).ToArray();
            if (renamesEntryPoint && executionModel is { } model)
            {
                if (
                    !SpirvEntryPointRewriter.TryRename(
                        spirV,
                        model,
                        GLSL_ENTRY_POINT,
                        entryPoint,
                        out byte[] rewritten,
                        out string renameMessage
                    )
                )
                {
                    message = renameMessage;
                    return false;
                }

                spirV = rewritten;
            }

            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            message = $"Shaderc failed to compile '{fileName}': {exception.Message}";
            return false;
        }
        finally
        {
            if (shaderc is not null)
            {
                if (result is not null)
                {
                    shaderc.ResultRelease(result);
                }

                if (options is not null)
                {
                    shaderc.CompileOptionsRelease(options);
                }

                if (compiler is not null)
                {
                    shaderc.CompilerRelease(compiler);
                }
            }
        }
    }

    private static bool ContainsIdentifier(string source, string identifier)
    {
        int index = 0;
        while (index <= source.Length - identifier.Length)
        {
            int found = source.IndexOf(identifier, index, StringComparison.Ordinal);
            if (found < 0)
            {
                return false;
            }

            int after = found + identifier.Length;
            bool startsAtBoundary = found == 0 || !IsIdentifierPart(source[found - 1]);
            bool endsAtBoundary = after == source.Length || !IsIdentifierPart(source[after]);
            if (startsAtBoundary && endsAtBoundary)
            {
                return true;
            }

            index = found + 1;
        }

        return false;
    }

    private static bool IsIdentifier(string value)
    {
        if (value.Length == 0 || char.IsAsciiDigit(value[0]))
        {
            return false;
        }

        foreach (char character in value)
        {
            if (!IsIdentifierPart(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsIdentifierPart(char character) =>
        character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_';
}
