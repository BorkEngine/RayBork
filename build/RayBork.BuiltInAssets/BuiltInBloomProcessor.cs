using System.Text;
using PolyType;
using RayBork.Bake;
using RayBork.Bake.Shaders;
using RayBork.Internal;

namespace RayBork.BuiltInAssets;

/// <summary>
/// The tool-only processor that packages the committed bloom SPIR-V stages into the built-in pack.
/// </summary>
/// <remarks>
/// <para>
/// The <c>.bloom</c> marker is a small UTF-8 file containing <c>bloom</c>. The processor reads the
/// committed <c>BloomFullscreen.vert.spv</c> and the four fragment blobs from the engine shader
/// directory, registers them as tracked inputs, and emits the shared vertex payload plus four
/// <see cref="ShaderAssetDescriptor"/> assets. Each descriptor uses the <c>main</c> entry point because
/// these stages are compiled directly by <c>scripts/compile-shaders.ps1</c> with the shader ABI prologue
/// in <c>BloomAbi.glslinc</c>.
/// </para>
/// <para>
/// The processor is synchronous and keeps no per-source state. It reports every failure as
/// <c>RB4008</c>.
/// </para>
/// </remarks>
internal sealed class BuiltInBloomProcessor : IAssetProcessor
{
    private const string MARKER = "bloom";
    private const uint GPU_PAYLOAD_ALIGNMENT = 16;
    private const string FRAGMENT_PAYLOAD_SUFFIX = "-frag";

    private static readonly string[] _extensions = ["bloom"];

    private static readonly UTF8Encoding _strictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true
    );

    private readonly string _engineShadersDirectory;

    /// <summary>Initializes the built-in bloom processor.</summary>
    /// <param name="engineShadersDirectory">
    /// The directory that holds the committed bloom SPIR-V blobs and their GLSL sources.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="engineShadersDirectory"/> is empty.</exception>
    internal BuiltInBloomProcessor(string engineShadersDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(engineShadersDirectory);
        _engineShadersDirectory = Path.GetFullPath(engineShadersDirectory);
    }

    /// <inheritdoc/>
    public string Name => "builtin-bloom";

    /// <inheritdoc/>
    public IReadOnlyList<string> Extensions => _extensions;

    /// <inheritdoc/>
    public SidecarPolicy Policy => SidecarPolicy.None;

    /// <inheritdoc/>
    public ITypeShape? SidecarShape => null;

    /// <inheritdoc/>
    public void Process(AssetProcessorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!TryReadMarker(context, out string marker))
        {
            return;
        }

        if (!string.Equals(marker, MARKER, StringComparison.Ordinal))
        {
            context.Diagnostics.Error(
                ShaderDiagnosticCodes.SHADER_INVALID,
                $"The built-in bloom marker '{context.RelativePath}' contains '{marker}'; the marker "
                    + $"text must be '{MARKER}'."
            );
            return;
        }

        if (!TryReadSpirv(context, BloomShaderContract.VERTEX_SPIRV_FILE, out byte[] vertexSpirV))
        {
            return;
        }

        context.Outputs.AddData(
            ToAssetPath(BloomShaderContract.VERTEX_PAYLOAD_PATH),
            vertexSpirV,
            GPU_PAYLOAD_ALIGNMENT
        );

        if (
            !TryAddProgram(
                context,
                BloomShaderContract.PREFILTER_PATH,
                BloomShaderContract.PREFILTER_SPIRV_FILE,
                BloomShaderContract.PREFILTER_UNIFORMS,
                BloomShaderContract.PREFILTER_TEXTURES
            )
            || !TryAddProgram(
                context,
                BloomShaderContract.DOWNSAMPLE_PATH,
                BloomShaderContract.DOWNSAMPLE_SPIRV_FILE,
                BloomShaderContract.DOWNSAMPLE_UNIFORMS,
                BloomShaderContract.DOWNSAMPLE_TEXTURES
            )
            || !TryAddProgram(
                context,
                BloomShaderContract.UPSAMPLE_PATH,
                BloomShaderContract.UPSAMPLE_SPIRV_FILE,
                BloomShaderContract.UPSAMPLE_UNIFORMS,
                BloomShaderContract.UPSAMPLE_TEXTURES
            )
            || !TryAddProgram(
                context,
                BloomShaderContract.COMPOSITE_PATH,
                BloomShaderContract.COMPOSITE_SPIRV_FILE,
                BloomShaderContract.COMPOSITE_UNIFORMS,
                BloomShaderContract.COMPOSITE_TEXTURES
            )
        )
        {
            return;
        }
    }

    private bool TryAddProgram(
        AssetProcessorContext context,
        string path,
        string fragmentFile,
        ShaderUniformAsset[] uniforms,
        string[] textures
    )
    {
        if (!TryReadSpirv(context, fragmentFile, out byte[] fragmentSpirV))
        {
            return false;
        }

        string assetPath = ToAssetPath(path);
        string fragmentPayload = assetPath + FRAGMENT_PAYLOAD_SUFFIX;
        var descriptor = new ShaderAssetDescriptor
        {
            AbiVersion = ShaderAbiNames.ABI_VERSION,
            VertexPayload = ToAssetPath(BloomShaderContract.VERTEX_PAYLOAD_PATH),
            FragmentPayload = fragmentPayload,
            VertexEntry = BloomShaderContract.ENTRY_POINT,
            FragmentEntry = BloomShaderContract.ENTRY_POINT,
            Stages = ShaderStageMask.Vertex | ShaderStageMask.Fragment,
            DefaultRenderState = RenderState.Opaque,
            Uniforms = uniforms,
            Textures = textures,
        };

        context.Outputs.AddAsset(assetPath, descriptor);
        context.Outputs.AddData(fragmentPayload, fragmentSpirV, GPU_PAYLOAD_ALIGNMENT);
        return true;
    }

    private bool TryReadSpirv(AssetProcessorContext context, string fileName, out byte[] spirV)
    {
        spirV = [];
        string path = Path.Combine(_engineShadersDirectory, fileName);
        if (!File.Exists(path))
        {
            context.Diagnostics.Error(
                ShaderDiagnosticCodes.SHADER_INVALID,
                $"The built-in bloom marker '{context.RelativePath}' needs the committed SPIR-V "
                    + $"module '{fileName}' in '{_engineShadersDirectory}', but it is missing. Run "
                    + "scripts/compile-shaders.ps1 with the Vulkan SDK."
            );
            return false;
        }

        try
        {
            context.AddInput(path);
            spirV = File.ReadAllBytes(path);
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or ArgumentException
                        or NotSupportedException
            )
        {
            context.Diagnostics.Error(
                ShaderDiagnosticCodes.SHADER_INVALID,
                $"The built-in bloom marker '{context.RelativePath}' could not read or register "
                    + $"'{fileName}': {exception.Message}"
            );
            return false;
        }

        if (
            spirV.Length < 20
            || (spirV.Length % 4) != 0
            || spirV[0] != 0x03
            || spirV[1] != 0x02
            || spirV[2] != 0x23
            || spirV[3] != 0x07
        )
        {
            context.Diagnostics.Error(
                ShaderDiagnosticCodes.SHADER_INVALID,
                $"The committed bloom module '{fileName}' is not a non-empty SPIR-V blob."
            );
            return false;
        }

        return true;
    }

    private static string ToAssetPath(string runtimePath) => runtimePath.TrimStart('/');

    private static bool TryReadMarker(AssetProcessorContext context, out string marker)
    {
        marker = string.Empty;

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(context.SourcePath);
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or NotSupportedException
                        or ArgumentException
            )
        {
            context.Diagnostics.Error(
                ShaderDiagnosticCodes.SHADER_INVALID,
                $"Failed to read the built-in bloom marker '{context.RelativePath}': "
                    + exception.Message
            );
            return false;
        }

        ReadOnlySpan<byte> content = bytes;
        if (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF)
        {
            content = content[3..];
        }

        string text;
        try
        {
            text = _strictUtf8.GetString(content);
        }
        catch (DecoderFallbackException)
        {
            context.Diagnostics.Error(
                ShaderDiagnosticCodes.SHADER_INVALID,
                $"The built-in bloom marker '{context.RelativePath}' is not valid UTF-8."
            );
            return false;
        }

        marker = text.Trim();
        if (marker.Length == 0)
        {
            context.Diagnostics.Error(
                ShaderDiagnosticCodes.SHADER_INVALID,
                $"The built-in bloom marker '{context.RelativePath}' is empty; it must contain "
                    + $"'{MARKER}'."
            );
            return false;
        }

        return true;
    }
}
