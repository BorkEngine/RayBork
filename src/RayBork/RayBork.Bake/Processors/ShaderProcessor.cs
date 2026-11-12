using System.Globalization;
using System.Text;
using RayBork.Bake.Paths;
using RayBork.Bake.Shaders;
using RayBork.Bake.Toml;
using RayBork.Internal;
using Silk.NET.Shaderc;

namespace RayBork.Bake.Processors;

/// <summary>
/// The built-in shader processor: compiles one <c>[shader]</c> TOML source's GLSL vertex and fragment
/// stages and emits a <see cref="ShaderAssetDescriptor"/> plus the two SPIR-V payloads.
/// </summary>
/// <remarks>
/// <para>
/// The TOML source is the processor source and its own sidecar. It names two GLSL files relative to
/// itself; the paths must resolve inside the source's directory subtree. Both files are registered
/// through <see cref="AssetProcessorContext.AddInput"/> as tracked inputs and are read as strict
/// UTF-8. The processor builds the instance layout from the sidecar uniforms and textures, validates
/// each user stage against the generated ABI, and inserts the M4 prologue after the stage's
/// <c>#version</c> directive (prepending <c>#version 450</c> when the source has none).
/// </para>
/// <para>
/// shaderc ignores GLSL entry point names, so the generated wrapper is compiled with the entry point
/// <c>main</c>; the compiled <c>OpEntryPoint</c> is then renamed to the fixed <c>main_vertex</c> or
/// <c>main_fragment</c> name and validated with <see cref="SpirvEntryPointInspector"/>. A validation,
/// compile, entry-point, or metadata failure is <c>RB4008</c> and emits no outputs; unknown keys and
/// wrong types are the framework's <c>RB4002</c> binding errors.
/// </para>
/// <para>
/// The processor is synchronous, runs on the bake thread, and keeps no per-source state, matching the
/// processor contract.
/// </para>
/// </remarks>
internal sealed class ShaderProcessor : AssetProcessor<ShaderSidecar> {
    private const string VERTEX_PAYLOAD_SUFFIX = "-vert";
    private const string FRAGMENT_PAYLOAD_SUFFIX = "-frag";
    private const int MAX_TEXTURE_BINDINGS = 16;
    private const int RECOMMENDED_INSTANCE_RECORD_SIZE = 4 * 1024;
    private const uint GPU_PAYLOAD_ALIGNMENT = 16;

    private static readonly string[] _extensions = ["shader.toml"];

    private static readonly UTF8Encoding _strictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    /// <inheritdoc/>
    /// <remarks>
    /// A shader source is discovered by its root <c>[shader]</c> table; the compound extension is the
    /// processor's registered identity for diagnostics and configuration hashing.
    /// </remarks>
    public override string Name => "shader";

    /// <inheritdoc/>
    public override IReadOnlyList<string> Extensions => _extensions;

    /// <inheritdoc/>
    public override SidecarPolicy Policy => SidecarPolicy.Required;

    /// <inheritdoc/>
    protected override ShaderSidecar CreateDefaultSidecar(AssetProcessorContext context) {
        ArgumentNullException.ThrowIfNull(context);
        return new ShaderSidecar {
            Vertex = string.Empty,
            Fragment = string.Empty,
        };
    }

    /// <inheritdoc/>
    protected override void Process(AssetProcessorContext context, ShaderSidecar sidecar) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sidecar);

        string vertexEntry = sidecar.VertexEntry ?? ShaderEntryPoints.VERTEX;
        if (!string.Equals(vertexEntry, ShaderEntryPoints.VERTEX, StringComparison.Ordinal)) {
            context.Diagnostics.Error(
                ShaderDiagnosticCodes.SHADER_INVALID,
                $"Shader '{context.RelativePath}' declares the vertex entry point "
                    + $"'{vertexEntry}'; the v1 vertex entry point is fixed to "
                    + $"'{ShaderEntryPoints.VERTEX}'.");
            return;
        }

        string fragmentEntry = sidecar.FragmentEntry ?? ShaderEntryPoints.FRAGMENT;
        if (!string.Equals(fragmentEntry, ShaderEntryPoints.FRAGMENT, StringComparison.Ordinal)) {
            context.Diagnostics.Error(
                ShaderDiagnosticCodes.SHADER_INVALID,
                $"Shader '{context.RelativePath}' declares the fragment entry point "
                    + $"'{fragmentEntry}'; the v1 fragment entry point is fixed to "
                    + $"'{ShaderEntryPoints.FRAGMENT}'.");
            return;
        }

        if (!TryResolveStagePath(context, sidecar.Vertex, "vertex", out string vertexPath)
            || !TryResolveStagePath(context, sidecar.Fragment, "fragment", out string fragmentPath)
            || !TryReadStageSource(context, vertexPath, "vertex", out string vertexSource)
            || !TryReadStageSource(context, fragmentPath, "fragment", out string fragmentSource)) {
            return;
        }

        if (!TryPackUniforms(context, sidecar.Uniforms, out ShaderUniformAsset[] uniforms)
            || !TryPackTextures(context, sidecar.Textures, out string[] textures)
            || !TryResolveRenderState(context, sidecar.RenderState, out RenderState renderState)) {
            return;
        }

        ShaderAbiLayout layout;
        try {
            layout = new ShaderAbiLayout(uniforms, textures);
        } catch (InvalidOperationException exception) {
            context.Diagnostics.Error(
                ShaderDiagnosticCodes.SHADER_INVALID,
                $"Shader '{context.RelativePath}' cannot be laid out: {exception.Message}");
            return;
        }

        if (layout.InstanceRecordSize > RECOMMENDED_INSTANCE_RECORD_SIZE) {
            context.Diagnostics.Warning(
                ShaderDiagnosticCodes.SHADER_INVALID,
                $"Shader '{context.RelativePath}' has a per-instance record of "
                    + $"{layout.InstanceRecordSize} bytes; records above "
                    + $"{RECOMMENDED_INSTANCE_RECORD_SIZE} bytes may exceed practical storage-buffer "
                    + "range or batch limits.");
        }

        if (!TryCompileStage(
                context,
                vertexSource,
                vertexPath,
                "vertex",
                ShaderKind.GlslVertexShader,
                ShaderStageMask.Vertex,
                vertexEntry,
                SpirvExecutionModel.Vertex,
                layout,
                out byte[] vertexSpirV)
            || !TryCompileStage(
                context,
                fragmentSource,
                fragmentPath,
                "fragment",
                ShaderKind.GlslFragmentShader,
                ShaderStageMask.Fragment,
                fragmentEntry,
                SpirvExecutionModel.Fragment,
                layout,
                out byte[] fragmentSpirV)) {
            return;
        }

        string vertexPayload = context.BaseAssetPath + VERTEX_PAYLOAD_SUFFIX;
        string fragmentPayload = context.BaseAssetPath + FRAGMENT_PAYLOAD_SUFFIX;
        var descriptor = new ShaderAssetDescriptor {
            VertexPayload = vertexPayload,
            FragmentPayload = fragmentPayload,
            VertexEntry = vertexEntry,
            FragmentEntry = fragmentEntry,
            Stages = ShaderStageMask.Vertex | ShaderStageMask.Fragment,
            DefaultRenderState = renderState,
            Uniforms = uniforms,
            Textures = textures,
        };

        context.Outputs.AddAsset(context.BaseAssetPath, descriptor);
        context.Outputs.AddData(vertexPayload, vertexSpirV, GPU_PAYLOAD_ALIGNMENT);
        context.Outputs.AddData(fragmentPayload, fragmentSpirV, GPU_PAYLOAD_ALIGNMENT);
    }

    private static bool TryResolveStagePath(
        AssetProcessorContext context,
        string? declared,
        string stage,
        out string fullPath) {
        fullPath = string.Empty;

        if (string.IsNullOrWhiteSpace(declared)) {
            context.Diagnostics.Error(
                ShaderDiagnosticCodes.SHADER_INVALID,
                $"Shader '{context.RelativePath}' declares no {stage} GLSL path.");
            return false;
        }

        if (Path.IsPathRooted(declared)) {
            context.Diagnostics.Error(
                ShaderDiagnosticCodes.SHADER_INVALID,
                $"Shader '{context.RelativePath}' declares the {stage} GLSL path '{declared}'; an "
                    + "v1 stage path must be relative to the shader TOML and stay inside its "
                    + "directory subtree.");
            return false;
        }

        string sourceDirectory = Path.GetDirectoryName(context.SourcePath)
            ?? Directory.GetCurrentDirectory();
        string candidate;
        try {
            candidate = Path.GetFullPath(Path.Combine(sourceDirectory, declared));
        } catch (Exception exception) when (exception is ArgumentException or NotSupportedException
            or PathTooLongException) {
            context.Diagnostics.Error(
                ShaderDiagnosticCodes.SHADER_INVALID,
                $"Shader '{context.RelativePath}' declares the invalid {stage} GLSL path "
                    + $"'{declared}': {exception.Message}");
            return false;
        }

        if (!BakePath.IsUnderRoot(sourceDirectory, candidate)) {
            context.Diagnostics.Error(
                ShaderDiagnosticCodes.SHADER_INVALID,
                $"Shader '{context.RelativePath}' declares the {stage} GLSL path '{declared}', "
                    + "which resolves outside the shader source's directory subtree; v1 stage files "
                    + "must stay inside it.");
            return false;
        }

        if (!File.Exists(candidate)) {
            context.Diagnostics.Error(
                ShaderDiagnosticCodes.SHADER_INVALID,
                $"Shader '{context.RelativePath}' declares the {stage} GLSL path '{declared}', "
                    + "but that file does not exist.");
            return false;
        }

        try {
            context.AddInput(candidate);
        } catch (Exception exception) when (exception is not OutOfMemoryException) {
            context.Diagnostics.Error(
                ShaderDiagnosticCodes.SHADER_INVALID,
                $"Shader '{context.RelativePath}' could not register the {stage} GLSL path "
                    + $"'{declared}' as a tracked input: {exception.Message}");
            return false;
        }

        fullPath = candidate;
        return true;
    }

    private static bool TryReadStageSource(
        AssetProcessorContext context,
        string fullPath,
        string stage,
        out string source) {
        source = string.Empty;

        byte[] bytes;
        try {
            bytes = File.ReadAllBytes(fullPath);
        } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException) {
            context.Diagnostics.Error(
                ShaderDiagnosticCodes.SHADER_INVALID,
                $"Failed to read the {stage} GLSL source '{Path.GetFileName(fullPath)}' of shader "
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
                $"The {stage} GLSL source '{Path.GetFileName(fullPath)}' of shader "
                    + $"'{context.RelativePath}' is not valid UTF-8.");
            return false;
        }
    }

    private static bool TryCompileStage(
        AssetProcessorContext context,
        string source,
        string fullPath,
        string stage,
        ShaderKind kind,
        ShaderStageMask stageMask,
        string entryPoint,
        SpirvExecutionModel executionModel,
        ShaderAbiLayout layout,
        out byte[] spirV) {
        spirV = [];
        string fileName = Path.GetFileName(fullPath);

        if (!ShaderPrologueGenerator.TryBuild(
                source,
                layout,
                stageMask,
                out string wrappedSource,
                out string problem)) {
            context.Diagnostics.Error(
                ShaderDiagnosticCodes.SHADER_INVALID,
                $"The {stage} stage '{fileName}' of shader '{context.RelativePath}' is not valid for "
                    + $"the generated M4 shader ABI: {problem}");
            return false;
        }

        if (!GlslCompiler.TryCompile(
                wrappedSource,
                fileName,
                kind,
                ShaderAbiNames.GLSL_ENTRY_POINT,
                out spirV,
                out string message)) {
            context.Diagnostics.Error(
                ShaderDiagnosticCodes.SHADER_INVALID,
                $"Failed to compile the {stage} stage '{fileName}' of shader "
                    + $"'{context.RelativePath}': {message}");
            return false;
        }

        if (!SpirvEntryPointRewriter.TryRename(
                spirV,
                executionModel,
                ShaderAbiNames.GLSL_ENTRY_POINT,
                entryPoint,
                out byte[] renamed,
                out string renameMessage)) {
            context.Diagnostics.Error(
                ShaderDiagnosticCodes.SHADER_INVALID,
                $"The {stage} stage '{fileName}' of shader '{context.RelativePath}' cannot use entry "
                    + $"point '{entryPoint}': {renameMessage}");
            return false;
        }

        spirV = renamed;
        if (!SpirvEntryPointInspector.TryValidate(spirV, entryPoint, executionModel, out string inspectorProblem)) {
            context.Diagnostics.Error(
                ShaderDiagnosticCodes.SHADER_INVALID,
                $"The {stage} stage '{fileName}' of shader '{context.RelativePath}' is not a valid "
                    + $"SPIR-V module for entry point '{entryPoint}': {inspectorProblem}");
            return false;
        }

        return true;
    }

    private static bool TryPackUniforms(
        AssetProcessorContext context,
        ShaderUniformSidecar[]? declared,
        out ShaderUniformAsset[] uniforms) {
        if (declared is null || declared.Length == 0) {
            uniforms = [];
            return true;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var packed = new List<ShaderUniformAsset>(declared.Length);
        foreach (ShaderUniformSidecar declaration in declared) {
            if (declaration is null || string.IsNullOrWhiteSpace(declaration.Name)) {
                context.Diagnostics.Error(
                    ShaderDiagnosticCodes.SHADER_INVALID,
                    $"Shader '{context.RelativePath}' declares a uniform without a name.");
                uniforms = [];
                return false;
            }

            string name = declaration.Name;
            if (!names.Add(name)) {
                context.Diagnostics.Error(
                    ShaderDiagnosticCodes.SHADER_INVALID,
                    $"Shader '{context.RelativePath}' declares the uniform '{name}' more than once.");
                uniforms = [];
                return false;
            }

            if (!TryMapUniformType(declaration.Type, out ShaderUniformType type, out int componentCount)) {
                context.Diagnostics.Error(
                    ShaderDiagnosticCodes.SHADER_INVALID,
                    $"The uniform '{name}' of shader '{context.RelativePath}' declares the "
                        + $"unsupported type '{declaration.Type}'; expected one of float, int, uint, "
                        + "bool, vec2, vec3, vec4, or mat4.");
                uniforms = [];
                return false;
            }

            if (!TryPackDefault(
                    context,
                    name,
                    declaration.Type,
                    type,
                    componentCount,
                    declaration.Default,
                    out float[] defaultValue)) {
                uniforms = [];
                return false;
            }

            packed.Add(new ShaderUniformAsset {
                Name = name,
                Type = type,
                Default = defaultValue,
            });
        }

        uniforms = [.. packed];
        return true;
    }

    private static bool TryPackTextures(
        AssetProcessorContext context,
        ShaderTextureSidecar[]? declared,
        out string[] textures) {
        if (declared is null || declared.Length == 0) {
            textures = [];
            return true;
        }

        if (declared.Length > MAX_TEXTURE_BINDINGS) {
            context.Diagnostics.Error(
                ShaderDiagnosticCodes.SHADER_INVALID,
                $"Shader '{context.RelativePath}' declares {declared.Length} texture bindings; at "
                    + $"most {MAX_TEXTURE_BINDINGS} are supported.");
            textures = [];
            return false;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var packed = new List<string>(declared.Length);
        foreach (ShaderTextureSidecar declaration in declared) {
            if (declaration is null || string.IsNullOrWhiteSpace(declaration.Name)) {
                context.Diagnostics.Error(
                    ShaderDiagnosticCodes.SHADER_INVALID,
                    $"Shader '{context.RelativePath}' declares a texture binding without a name.");
                textures = [];
                return false;
            }

            if (!names.Add(declaration.Name)) {
                context.Diagnostics.Error(
                    ShaderDiagnosticCodes.SHADER_INVALID,
                    $"Shader '{context.RelativePath}' declares the texture binding "
                        + $"'{declaration.Name}' more than once.");
                textures = [];
                return false;
            }

            packed.Add(declaration.Name);
        }

        textures = [.. packed];
        return true;
    }

    private static bool TryResolveRenderState(
        AssetProcessorContext context,
        ShaderRenderStateSidecar? declared,
        out RenderState renderState) {
        renderState = RenderState.Opaque;
        if (declared is null) {
            return true;
        }

        if (declared.Blend is { } blend) {
            if (!TryParseBlend(blend, out BlendMode blendMode)) {
                context.Diagnostics.Error(
                    ShaderDiagnosticCodes.SHADER_INVALID,
                    $"Shader '{context.RelativePath}' declares the unsupported blend token "
                        + $"'{blend}'; expected one of opaque, alpha, additive, or multiply.");
                return false;
            }

            renderState = renderState with { Blend = blendMode };
        }

        if (declared.Cull is { } cull) {
            if (!TryParseCull(cull, out CullMode cullMode)) {
                context.Diagnostics.Error(
                    ShaderDiagnosticCodes.SHADER_INVALID,
                    $"Shader '{context.RelativePath}' declares the unsupported cull token "
                        + $"'{cull}'; expected one of none, front, or back.");
                return false;
            }

            renderState = renderState with { Cull = cullMode };
        }

        if (declared.DepthTest is { } depthTest) {
            if (!TryParseDepthTest(depthTest, out DepthTest depthTestMode)) {
                context.Diagnostics.Error(
                    ShaderDiagnosticCodes.SHADER_INVALID,
                    $"Shader '{context.RelativePath}' declares the unsupported depth-test token "
                        + $"'{depthTest}'; expected one of disabled, greater, greater_equal, less, "
                        + "less_or_equal, or always.");
                return false;
            }

            renderState = renderState with { DepthTest = depthTestMode };
        }

        if (declared.DepthWrite is bool depthWrite) {
            renderState = renderState with { DepthWrite = depthWrite };
        }

        return true;
    }

    private static bool TryMapUniformType(
        string declaredType,
        out ShaderUniformType type,
        out int componentCount) {
        switch (declaredType) {
            case "float":
                type = ShaderUniformType.Float;
                componentCount = 1;
                return true;

            case "int":
                type = ShaderUniformType.Int;
                componentCount = 1;
                return true;

            case "uint":
                type = ShaderUniformType.UInt;
                componentCount = 1;
                return true;

            case "bool":
                type = ShaderUniformType.Bool;
                componentCount = 1;
                return true;

            case "vec2":
                type = ShaderUniformType.Vector2;
                componentCount = 2;
                return true;

            case "vec3":
                type = ShaderUniformType.Vector3;
                componentCount = 3;
                return true;

            case "vec4":
                type = ShaderUniformType.Vector4;
                componentCount = 4;
                return true;

            case "mat4":
                type = ShaderUniformType.Matrix4x4;
                componentCount = 16;
                return true;

            default:
                type = default;
                componentCount = 0;
                return false;
        }
    }

    private static bool TryPackDefault(
        AssetProcessorContext context,
        string name,
        string declaredType,
        ShaderUniformType type,
        int componentCount,
        TomlNumberOrArray? declared,
        out float[] defaultValue) {
        defaultValue = [];
        if (declared is null) {
            return true;
        }

        IReadOnlyList<double> components = declared.Values;
        if (components.Count != componentCount) {
            context.Diagnostics.Error(
                ShaderDiagnosticCodes.SHADER_INVALID,
                $"The uniform '{name}' of shader '{context.RelativePath}' has a default with "
                    + $"{components.Count} component(s); type '{declaredType}' requires "
                    + $"{componentCount}.");
            return false;
        }

        var packed = new float[componentCount];
        for (int index = 0; index < componentCount; index++) {
            double component = components[index];
            if (!double.IsFinite(component)) {
                context.Diagnostics.Error(
                    ShaderDiagnosticCodes.SHADER_INVALID,
                    $"The uniform '{name}' of shader '{context.RelativePath}' has the non-finite "
                        + $"default component {index}.");
                defaultValue = [];
                return false;
            }

            string? problem = ValidateDefaultComponent(type, component);
            if (problem is not null) {
                context.Diagnostics.Error(
                    ShaderDiagnosticCodes.SHADER_INVALID,
                    $"The uniform '{name}' of shader '{context.RelativePath}' has the default "
                        + $"component {index} = "
                        + $"{component.ToString(CultureInfo.InvariantCulture)}: {problem}");
                defaultValue = [];
                return false;
            }

            float value = (float)component;
            if (!float.IsFinite(value)) {
                context.Diagnostics.Error(
                    ShaderDiagnosticCodes.SHADER_INVALID,
                    $"The uniform '{name}' of shader '{context.RelativePath}' has a default "
                        + $"component {index} that is not finite as a 32-bit float.");
                defaultValue = [];
                return false;
            }

            packed[index] = value;
        }

        defaultValue = packed;
        return true;
    }

    private static string? ValidateDefaultComponent(ShaderUniformType type, double component) {
        switch (type) {
            case ShaderUniformType.Int
                when component != Math.Truncate(component)
                    || component < int.MinValue
                    || component > int.MaxValue:
                return "an int default must be a 32-bit signed integer.";

            case ShaderUniformType.UInt
                when component != Math.Truncate(component)
                    || component < 0
                    || component > uint.MaxValue:
                return "a uint default must be a 32-bit unsigned integer.";

            case ShaderUniformType.Bool when component != 0d && component != 1d:
                return "a bool default must be 0 or 1.";

            default:
                return null;
        }
    }

    private static bool TryParseBlend(string token, out BlendMode blend)
        => token switch {
            "opaque" => Set(BlendMode.Opaque, out blend),
            "alpha" => Set(BlendMode.Alpha, out blend),
            "additive" => Set(BlendMode.Additive, out blend),
            "multiply" => Set(BlendMode.Multiply, out blend),
            _ => Set(default(BlendMode), out blend, valid: false),
        };

    private static bool TryParseCull(string token, out CullMode cull)
        => token switch {
            "none" => Set(CullMode.None, out cull),
            "front" => Set(CullMode.Front, out cull),
            "back" => Set(CullMode.Back, out cull),
            _ => Set(default(CullMode), out cull, valid: false),
        };

    private static bool TryParseDepthTest(string token, out DepthTest depthTest)
        => token switch {
            "disabled" => Set(DepthTest.Disabled, out depthTest),
            "greater" => Set(DepthTest.Greater, out depthTest),
            "greater_equal" => Set(DepthTest.GreaterOrEqual, out depthTest),
            "less" => Set(DepthTest.Less, out depthTest),
            "less_or_equal" => Set(DepthTest.LessOrEqual, out depthTest),
            "always" => Set(DepthTest.Always, out depthTest),
            _ => Set(default(DepthTest), out depthTest, valid: false),
        };

    private static bool Set<T>(T value, out T result, bool valid = true) {
        result = value;
        return valid;
    }
}
