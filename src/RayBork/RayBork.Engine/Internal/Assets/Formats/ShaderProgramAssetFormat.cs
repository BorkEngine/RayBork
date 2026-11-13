namespace RayBork.Internal;

/// <summary>
/// The built-in packed shader-program format: it decodes the descriptor, reads both SPIR-V payloads,
/// validates them, and builds the program upload.
/// </summary>
/// <remarks>
/// The format is stateless, so one shared instance serves every program load on the loader thread. The
/// runtime never parses GLSL or SPIR-V; it only checks that each module is a non-empty, 4-byte aligned
/// blob.
/// </remarks>
internal sealed class ShaderProgramAssetFormat : IEngineAssetFormat<ShaderProgram> {
    /// <summary>
    /// The shared format instance.
    /// </summary>
    internal static readonly ShaderProgramAssetFormat Instance = new();

    private ShaderProgramAssetFormat() {
    }

    /// <inheritdoc/>
    public AssetEntryType EntryType => AssetEntryType.Shader;

    /// <inheritdoc/>
    public DecodedAsset<ShaderProgram> Decode(AssetFormatContext context) {
        ArgumentNullException.ThrowIfNull(context);

        ShaderAssetDescriptor descriptor = AssetSerialization.Deserialize<ShaderAssetDescriptor>(
            context.DescriptorPayload);
        byte[] vertexPayload = context.ReadRawPayload(
            descriptor.VertexPayload,
            "vertex SPIR-V");
        byte[] fragmentPayload = context.ReadRawPayload(
            descriptor.FragmentPayload,
            "fragment SPIR-V");
        ValidateShader(descriptor, vertexPayload, fragmentPayload, context.CanonicalPath);
        ShaderAssetDescriptor captured = descriptor;
        return new DecodedAsset<ShaderProgram>(
            new ShaderGpuUpload(context.CanonicalPath, descriptor, vertexPayload, fragmentPayload),
            lifetime => new ShaderProgram(lifetime, captured));
    }

    private static void ValidateShader(
        ShaderAssetDescriptor descriptor,
        byte[] vertexPayload,
        byte[] fragmentPayload,
        string canonical) {
        const ShaderStageMask requiredStages = ShaderStageMask.Vertex | ShaderStageMask.Fragment;
        if (descriptor.Stages != requiredStages) {
            throw AssetLoadErrors.CreateLoadFailure(
                canonical,
                $"the shader descriptor stages 0x{(int)descriptor.Stages:x} do not describe a complete "
                    + "vertex+fragment program");
        }

        if (string.IsNullOrWhiteSpace(descriptor.VertexEntry)
            || string.IsNullOrWhiteSpace(descriptor.FragmentEntry)) {
            throw AssetLoadErrors.CreateLoadFailure(canonical, "the shader descriptor must name both entry points");
        }

        if (!IsSpirvModule(vertexPayload) || !IsSpirvModule(fragmentPayload)) {
            throw AssetLoadErrors.CreateLoadFailure(
                canonical,
                "the shader SPIR-V payloads must be non-empty, 4-byte aligned modules");
        }

        if (descriptor.Uniforms is null || descriptor.Textures is null) {
            throw AssetLoadErrors.CreateLoadFailure(canonical, "the shader descriptor metadata arrays must not be null");
        }

        HashSet<string> textureNames = new(StringComparer.Ordinal);
        foreach (string? texture in descriptor.Textures) {
            if (string.IsNullOrWhiteSpace(texture)) {
                throw AssetLoadErrors.CreateLoadFailure(canonical, "the shader descriptor has an empty texture name");
            }

            if (!textureNames.Add(texture)) {
                throw AssetLoadErrors.CreateLoadFailure(
                    canonical,
                    $"the shader descriptor repeats the texture name '{texture}'");
            }
        }

        foreach (ShaderUniformAsset? uniform in descriptor.Uniforms) {
            if (uniform is null
                || string.IsNullOrWhiteSpace(uniform.Name)
                || !Enum.IsDefined(uniform.Type)
                || uniform.Default is null) {
                throw AssetLoadErrors.CreateLoadFailure(canonical, "the shader descriptor has invalid uniform metadata");
            }
        }
    }

    private static bool IsSpirvModule(byte[] payload)
        => payload.Length >= 20 && payload.Length % sizeof(uint) == 0;
}
