namespace RayBork.Internal;

/// <summary>
/// The engine-owned bloom shader assets and the shader-ABI metadata every pass declares.
/// </summary>
/// <remarks>
/// The built-in asset generator reads the committed SPIR-V files and emits these shader assets into the
/// built-in pack. The runtime resolves the same paths through <see cref="BloomAssets"/> before
/// <c>IGame.Setup</c> and never compiles or loads a shader on the render thread. The stage entries are
/// <c>main</c> because these modules are compiled directly by <c>scripts/compile-shaders.ps1</c> rather
/// than through the bake's entry-point rewriter. The uniform and texture declarations here must match
/// the instance structs in <c>BloomAbi.glslinc</c> and therefore <see cref="ShaderAbiLayout"/>.
/// </remarks>
internal static class BloomShaderContract {
    internal const string VERTEX_SPIRV_FILE = "BloomFullscreen.vert.spv";
    internal const string VERTEX_PAYLOAD_PATH = "/shaders/bloom/fullscreen-vert";
    internal const string ENTRY_POINT = "main";

    internal const string PREFILTER_PATH = "/shaders/bloom/prefilter";
    internal const string DOWNSAMPLE_PATH = "/shaders/bloom/downsample";
    internal const string UPSAMPLE_PATH = "/shaders/bloom/upsample";
    internal const string COMPOSITE_PATH = "/shaders/bloom/composite";

    internal const string PREFILTER_SPIRV_FILE = "BloomPrefilter.frag.spv";
    internal const string DOWNSAMPLE_SPIRV_FILE = "BloomDownsample.frag.spv";
    internal const string UPSAMPLE_SPIRV_FILE = "BloomUpsample.frag.spv";
    internal const string COMPOSITE_SPIRV_FILE = "BloomComposite.frag.spv";

    internal const string SOURCE_TEXTURE = "bloomSource";
    internal const string COARSE_TEXTURE = "bloomCoarse";
    internal const string FINE_TEXTURE = "bloomFine";
    internal const string SOURCE_COLOR_TEXTURE = "sourceColor";
    internal const string BLOOM_TEXTURE = "bloomTexture";

    internal static readonly ShaderUniformAsset[] PREFILTER_UNIFORMS = [
        Uniform("uThreshold", ShaderUniformType.Float),
        Uniform("uSoftKnee", ShaderUniformType.Float),
    ];

    internal static readonly ShaderUniformAsset[] DOWNSAMPLE_UNIFORMS = [];

    internal static readonly ShaderUniformAsset[] UPSAMPLE_UNIFORMS = [
        Uniform("uScatter", ShaderUniformType.Float),
    ];

    internal static readonly ShaderUniformAsset[] COMPOSITE_UNIFORMS = [
        Uniform("uTint", ShaderUniformType.Vector4),
        Uniform("uIntensity", ShaderUniformType.Float),
    ];

    internal static readonly string[] PREFILTER_TEXTURES = [SOURCE_TEXTURE];

    internal static readonly string[] DOWNSAMPLE_TEXTURES = [SOURCE_TEXTURE];

    internal static readonly string[] UPSAMPLE_TEXTURES = [COARSE_TEXTURE, FINE_TEXTURE];

    internal static readonly string[] COMPOSITE_TEXTURES = [SOURCE_COLOR_TEXTURE, BLOOM_TEXTURE];

    private static ShaderUniformAsset Uniform(string name, ShaderUniformType type) => new() {
        Name = name,
        Type = type,
        Default = [],
    };
}
