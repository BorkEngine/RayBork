using System.Text;
using PolyType;
using RayBork.Bake;
using RayBork.Bake.Shaders;
using RayBork.Internal;

namespace RayBork.BuiltInAssets;

/// <summary>
/// The tool-only processor that packages the committed SSAO SPIR-V variants into the built-in pack.
/// </summary>
/// <remarks>
/// <para>
/// The <c>.ssao</c> marker is a small UTF-8 file containing <c>ssao</c>. The processor reads the
/// committed <c>SsaoFullscreen.vert.spv</c> and the seven fragment blobs from the engine shader
/// directory, registers them as tracked inputs, and emits the shared vertex payload plus seven
/// <see cref="ShaderAssetDescriptor"/> assets. Each descriptor uses the <c>main</c> entry point
/// because these stages are compiled directly by <c>scripts/compile-shaders.ps1</c> with the shader
/// ABI prologue in <c>SsaoAbi.glslinc</c>.
/// </para>
/// <para>
/// The processor is synchronous and keeps no per-source state. It reports every failure as
/// <c>RB4008</c>.
/// </para>
/// </remarks>
internal sealed class BuiltInSsaoProcessor : IAssetProcessor {
    private const string MARKER = "ssao";
    private const uint GPU_PAYLOAD_ALIGNMENT = 16;
    private const string FRAGMENT_PAYLOAD_SUFFIX = "-frag";

    private static readonly string[] _extensions = ["ssao"];

    private static readonly UTF8Encoding _strictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly string _engineShadersDirectory;

    /// <summary>Initializes the built-in SSAO processor.</summary>
    /// <param name="engineShadersDirectory">
    /// The directory that holds the committed SSAO SPIR-V blobs and their GLSL sources.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="engineShadersDirectory"/> is empty.</exception>
    internal BuiltInSsaoProcessor(string engineShadersDirectory) {
        ArgumentException.ThrowIfNullOrWhiteSpace(engineShadersDirectory);
        _engineShadersDirectory = Path.GetFullPath(engineShadersDirectory);
    }

    /// <inheritdoc/>
    public string Name => "builtin-ssao";

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

        if (!string.Equals(marker, MARKER, StringComparison.Ordinal)) {
            context.Diagnostics.Error(
                ShaderDiagnosticCodes.SHADER_INVALID,
                $"The built-in SSAO marker '{context.RelativePath}' contains '{marker}'; the marker "
                    + $"text must be '{MARKER}'.");
            return;
        }

        if (!TryReadSpirv(context, SsaoShaderContract.VERTEX_SPIRV_FILE, out byte[] vertexSpirV)) {
            return;
        }

        context.Outputs.AddData(
            ToAssetPath(SsaoShaderContract.VERTEX_PAYLOAD_PATH),
            vertexSpirV,
            GPU_PAYLOAD_ALIGNMENT);

        if (!TryAddProgram(
                context,
                SsaoShaderContract.AO_8_PATH,
                SsaoShaderContract.AO_8_SPIRV_FILE,
                SsaoShaderContract.AO_UNIFORMS,
                SsaoShaderContract.AO_TEXTURES)
            || !TryAddProgram(
                context,
                SsaoShaderContract.AO_16_PATH,
                SsaoShaderContract.AO_16_SPIRV_FILE,
                SsaoShaderContract.AO_UNIFORMS,
                SsaoShaderContract.AO_TEXTURES)
            || !TryAddProgram(
                context,
                SsaoShaderContract.AO_64_PATH,
                SsaoShaderContract.AO_64_SPIRV_FILE,
                SsaoShaderContract.AO_UNIFORMS,
                SsaoShaderContract.AO_TEXTURES)
            || !TryAddProgram(
                context,
                SsaoShaderContract.DENOISE_4_PATH,
                SsaoShaderContract.DENOISE_4_SPIRV_FILE,
                SsaoShaderContract.DENOISE_UNIFORMS,
                SsaoShaderContract.DENOISE_TEXTURES)
            || !TryAddProgram(
                context,
                SsaoShaderContract.DENOISE_8_PATH,
                SsaoShaderContract.DENOISE_8_SPIRV_FILE,
                SsaoShaderContract.DENOISE_UNIFORMS,
                SsaoShaderContract.DENOISE_TEXTURES)
            || !TryAddProgram(
                context,
                SsaoShaderContract.DENOISE_16_PATH,
                SsaoShaderContract.DENOISE_16_SPIRV_FILE,
                SsaoShaderContract.DENOISE_UNIFORMS,
                SsaoShaderContract.DENOISE_TEXTURES)
            || !TryAddProgram(
                context,
                SsaoShaderContract.COMPOSITE_PATH,
                SsaoShaderContract.COMPOSITE_SPIRV_FILE,
                SsaoShaderContract.COMPOSITE_UNIFORMS,
                SsaoShaderContract.COMPOSITE_TEXTURES)) {
            return;
        }
    }

    private bool TryAddProgram(
        AssetProcessorContext context,
        string path,
        string fragmentFile,
        ShaderUniformAsset[] uniforms,
        string[] textures) {
        if (!TryReadSpirv(context, fragmentFile, out byte[] fragmentSpirV)) {
            return false;
        }

        string assetPath = ToAssetPath(path);
        string fragmentPayload = assetPath + FRAGMENT_PAYLOAD_SUFFIX;
        var descriptor = new ShaderAssetDescriptor {
            AbiVersion = ShaderAbiNames.ABI_VERSION,
            VertexPayload = ToAssetPath(SsaoShaderContract.VERTEX_PAYLOAD_PATH),
            FragmentPayload = fragmentPayload,
            VertexEntry = SsaoShaderContract.ENTRY_POINT,
            FragmentEntry = SsaoShaderContract.ENTRY_POINT,
            Stages = ShaderStageMask.Vertex | ShaderStageMask.Fragment,
            DefaultRenderState = RenderState.Opaque,
            Uniforms = uniforms,
            Textures = textures,
        };

        context.Outputs.AddAsset(assetPath, descriptor);
        context.Outputs.AddData(fragmentPayload, fragmentSpirV, GPU_PAYLOAD_ALIGNMENT);
        return true;
    }

    private bool TryReadSpirv(AssetProcessorContext context, string fileName, out byte[] spirV) {
        spirV = [];
        string path = Path.Combine(_engineShadersDirectory, fileName);
        if (!File.Exists(path)) {
            context.Diagnostics.Error(
                ShaderDiagnosticCodes.SHADER_INVALID,
                $"The built-in SSAO marker '{context.RelativePath}' needs the committed SPIR-V "
                    + $"module '{fileName}' in '{_engineShadersDirectory}', but it is missing. Run "
                    + "scripts/compile-shaders.ps1 with the Vulkan SDK.");
            return false;
        }

        try {
            context.AddInput(path);
            spirV = File.ReadAllBytes(path);
        } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException) {
            context.Diagnostics.Error(
                ShaderDiagnosticCodes.SHADER_INVALID,
                $"The built-in SSAO marker '{context.RelativePath}' could not read or register "
                    + $"'{fileName}': {exception.Message}");
            return false;
        }

        if (spirV.Length < 20
            || (spirV.Length % 4) != 0
            || spirV[0] != 0x03
            || spirV[1] != 0x02
            || spirV[2] != 0x23
            || spirV[3] != 0x07) {
            context.Diagnostics.Error(
                ShaderDiagnosticCodes.SHADER_INVALID,
                $"The committed SSAO module '{fileName}' is not a non-empty SPIR-V blob.");
            return false;
        }

        return true;
    }

    private static string ToAssetPath(string runtimePath) => runtimePath.TrimStart('/');

    private static bool TryReadMarker(AssetProcessorContext context, out string marker) {
        marker = string.Empty;

        byte[] bytes;
        try {
            bytes = File.ReadAllBytes(context.SourcePath);
        } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException) {
            context.Diagnostics.Error(
                ShaderDiagnosticCodes.SHADER_INVALID,
                $"Failed to read the built-in SSAO marker '{context.RelativePath}': "
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
                $"The built-in SSAO marker '{context.RelativePath}' is not valid UTF-8.");
            return false;
        }

        marker = text.Trim();
        if (marker.Length == 0) {
            context.Diagnostics.Error(
                ShaderDiagnosticCodes.SHADER_INVALID,
                $"The built-in SSAO marker '{context.RelativePath}' is empty; it must contain "
                    + $"'{MARKER}'.");
            return false;
        }

        return true;
    }
}
