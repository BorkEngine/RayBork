using System.Text;
using PolyType;
using RayBork.Bake;
using RayBork.Bake.Shaders;
using RayBork.Internal;
using Silk.NET.Shaderc;

namespace RayBork.BuiltInAssets;

/// <summary>
/// The tool-only processor that compiles the engine-owned present stages into the built-in pack's
/// shader entry.
/// </summary>
/// <remarks>
/// <para>
/// A <c>.present</c> source is a small UTF-8 text marker (<c>present</c>). The processor locates
/// <c>Present.vert.glsl</c> and <c>Present.frag.glsl</c> in the engine shader directory, compiles both
/// through the shared <see cref="GlslCompiler"/> with the <c>main</c> entry point, validates each
/// module with <see cref="SpirvEntryPointInspector"/>, and emits a
/// <see cref="ShaderAssetDescriptor"/> at the source's base path with the two SPIR-V payloads at
/// <c>&lt;base&gt;-vert</c> and <c>&lt;base&gt;-frag</c>.
/// </para>
/// <para>
/// The runtime present pipeline still consumes the committed embedded SPIR-V blobs directly; this
/// entry proves that the built-in pack is produced by the same processor and bake pipeline as game
/// content, and M4 can switch a render path to the pack entry without changing the present ABI. The
/// two GLSL sources are registered as tracked inputs. The processor is synchronous, keeps no
/// per-source state, and reports every failure as <c>RB4008</c>.
/// </para>
/// </remarks>
internal sealed class BuiltInPresentProcessor : IAssetProcessor {
    private const string VERTEX_SOURCE = "Present.vert.glsl";
    private const string FRAGMENT_SOURCE = "Present.frag.glsl";
    private const string PRESENT_MARKER = "present";
    private const string ENTRY_POINT = "main";
    private const string VERTEX_PAYLOAD_SUFFIX = "-vert";
    private const string FRAGMENT_PAYLOAD_SUFFIX = "-frag";
    private const uint GPU_PAYLOAD_ALIGNMENT = 16;

    private static readonly string[] _extensions = ["present"];

    private static readonly UTF8Encoding _strictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly string _engineShadersDirectory;

    /// <summary>Initializes the built-in present processor.</summary>
    /// <param name="engineShadersDirectory">
    /// The directory that holds the engine-owned <c>Present.vert.glsl</c> and
    /// <c>Present.frag.glsl</c> sources.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="engineShadersDirectory"/> is empty.</exception>
    internal BuiltInPresentProcessor(string engineShadersDirectory) {
        ArgumentException.ThrowIfNullOrWhiteSpace(engineShadersDirectory);
        _engineShadersDirectory = Path.GetFullPath(engineShadersDirectory);
    }

    /// <inheritdoc/>
    public string Name => "builtin-present";

    /// <inheritdoc/>
    public IReadOnlyList<string> Extensions => _extensions;

    /// <inheritdoc/>
    public SidecarPolicy Policy => SidecarPolicy.None;

    /// <inheritdoc/>
    public ITypeShape? SidecarShape => null;

    /// <inheritdoc/>
    public void Process(AssetProcessorContext context) {
        ArgumentNullException.ThrowIfNull(context);

        if (!TryReadMarker(context, out string marker)) {
            return;
        }

        if (!string.Equals(marker, PRESENT_MARKER, StringComparison.Ordinal)) {
            context.Diagnostics.Error(
                ShaderDiagnosticCodes.SHADER_INVALID,
                $"The built-in present marker '{context.RelativePath}' contains '{marker}'; the "
                    + $"marker text must be '{PRESENT_MARKER}'.");
            return;
        }

        string vertexPath = Path.Combine(_engineShadersDirectory, VERTEX_SOURCE);
        string fragmentPath = Path.Combine(_engineShadersDirectory, FRAGMENT_SOURCE);
        if (!File.Exists(vertexPath) || !File.Exists(fragmentPath)) {
            context.Diagnostics.Error(
                ShaderDiagnosticCodes.SHADER_INVALID,
                $"The built-in present marker '{context.RelativePath}' needs '{VERTEX_SOURCE}' and "
                    + $"'{FRAGMENT_SOURCE}' in '{_engineShadersDirectory}', but the pair is missing.");
            return;
        }

        if (!TryRegisterInput(context, vertexPath) || !TryRegisterInput(context, fragmentPath)) {
            return;
        }

        if (!TryReadStageSource(context, vertexPath, "vertex", out string vertexSource)
            || !TryReadStageSource(context, fragmentPath, "fragment", out string fragmentSource)) {
            return;
        }

        if (!TryCompileStage(
                context,
                vertexSource,
                vertexPath,
                "vertex",
                ShaderKind.GlslVertexShader,
                SpirvExecutionModel.Vertex,
                out byte[] vertexSpirV)
            || !TryCompileStage(
                context,
                fragmentSource,
                fragmentPath,
                "fragment",
                ShaderKind.GlslFragmentShader,
                SpirvExecutionModel.Fragment,
                out byte[] fragmentSpirV)) {
            return;
        }

        string vertexPayload = context.BaseAssetPath + VERTEX_PAYLOAD_SUFFIX;
        string fragmentPayload = context.BaseAssetPath + FRAGMENT_PAYLOAD_SUFFIX;
        var descriptor = new ShaderAssetDescriptor {
            VertexPayload = vertexPayload,
            FragmentPayload = fragmentPayload,
            VertexEntry = ENTRY_POINT,
            FragmentEntry = ENTRY_POINT,
            Stages = ShaderStageMask.Vertex | ShaderStageMask.Fragment,
            DefaultRenderState = RenderState.Opaque,
            Uniforms = [],
            Textures = [],
        };

        context.Outputs.AddAsset(context.BaseAssetPath, descriptor);
        context.Outputs.AddData(vertexPayload, vertexSpirV, GPU_PAYLOAD_ALIGNMENT);
        context.Outputs.AddData(fragmentPayload, fragmentSpirV, GPU_PAYLOAD_ALIGNMENT);
    }

    private static bool TryReadMarker(AssetProcessorContext context, out string marker) {
        marker = string.Empty;

        byte[] bytes;
        try {
            bytes = File.ReadAllBytes(context.SourcePath);
        } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException) {
            context.Diagnostics.Error(
                ShaderDiagnosticCodes.SHADER_INVALID,
                $"Failed to read the built-in present marker '{context.RelativePath}': "
                    + exception.Message);
            return false;
        }

        ReadOnlySpan<byte> content = bytes;
        if (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF) {
            content = content[3..];
        }

        string text;
        try {
            text = _strictUtf8.GetString(content);
        } catch (DecoderFallbackException) {
            context.Diagnostics.Error(
                ShaderDiagnosticCodes.SHADER_INVALID,
                $"The built-in present marker '{context.RelativePath}' is not valid UTF-8.");
            return false;
        }

        marker = text.Trim();
        if (marker.Length == 0) {
            context.Diagnostics.Error(
                ShaderDiagnosticCodes.SHADER_INVALID,
                $"The built-in present marker '{context.RelativePath}' is empty; it must contain "
                    + $"'{PRESENT_MARKER}'.");
            return false;
        }

        return true;
    }

    private static bool TryRegisterInput(AssetProcessorContext context, string path) {
        try {
            context.AddInput(path);
            return true;
        } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException) {
            context.Diagnostics.Error(
                ShaderDiagnosticCodes.SHADER_INVALID,
                $"The built-in present marker '{context.RelativePath}' could not register "
                    + $"'{Path.GetFileName(path)}' as a tracked input: {exception.Message}");
            return false;
        }
    }

    private static bool TryReadStageSource(
        AssetProcessorContext context,
        string path,
        string stage,
        out string source) {
        source = string.Empty;
        string fileName = Path.GetFileName(path);

        byte[] bytes;
        try {
            bytes = File.ReadAllBytes(path);
        } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException) {
            context.Diagnostics.Error(
                ShaderDiagnosticCodes.SHADER_INVALID,
                $"Failed to read the engine-owned present {stage} shader '{fileName}' of "
                    + $"'{context.RelativePath}': {exception.Message}");
            return false;
        }

        ReadOnlySpan<byte> content = bytes;
        if (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF) {
            content = content[3..];
        }

        try {
            source = _strictUtf8.GetString(content);
            return true;
        } catch (DecoderFallbackException) {
            context.Diagnostics.Error(
                ShaderDiagnosticCodes.SHADER_INVALID,
                $"The engine-owned present {stage} shader '{fileName}' of "
                    + $"'{context.RelativePath}' is not valid UTF-8.");
            return false;
        }
    }

    private static bool TryCompileStage(
        AssetProcessorContext context,
        string source,
        string path,
        string stage,
        ShaderKind kind,
        SpirvExecutionModel executionModel,
        out byte[] spirV) {
        spirV = [];
        string fileName = Path.GetFileName(path);

        if (!GlslCompiler.TryCompile(
                source,
                fileName,
                kind,
                ENTRY_POINT,
                out spirV,
                out string message)) {
            context.Diagnostics.Error(
                ShaderDiagnosticCodes.SHADER_INVALID,
                $"Failed to compile the engine-owned present {stage} shader '{fileName}' of "
                    + $"'{context.RelativePath}': {message}");
            return false;
        }

        if (!SpirvEntryPointInspector.TryValidate(
                spirV,
                ENTRY_POINT,
                executionModel,
                out string problem)) {
            context.Diagnostics.Error(
                ShaderDiagnosticCodes.SHADER_INVALID,
                $"The engine-owned present {stage} shader '{fileName}' of "
                    + $"'{context.RelativePath}' is not a valid SPIR-V module for entry point "
                    + $"'{ENTRY_POINT}': {problem}");
            return false;
        }

        return true;
    }
}
