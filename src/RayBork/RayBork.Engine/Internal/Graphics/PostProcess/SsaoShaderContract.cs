namespace RayBork.Internal;

/// <summary>
/// The engine-owned SSAO shader assets and the shader-ABI metadata every variant declares.
/// </summary>
/// <remarks>
/// The built-in asset generator reads the committed SPIR-V files and emits these shader assets into
/// the built-in pack. The runtime resolves the same paths through <see cref="SsaoAssets"/> before
/// <c>IGame.Setup</c> and never compiles or loads a shader on the render thread. The stage
/// entries are <c>main</c> because these modules are compiled directly by
/// <c>scripts/compile-shaders.ps1</c> rather than through the bake's entry-point rewriter.
/// </remarks>
internal static class SsaoShaderContract {
    internal const string VERTEX_SPIRV_FILE = "SsaoFullscreen.vert.spv";
    internal const string VERTEX_PAYLOAD_PATH = "/shaders/ssao/fullscreen-vert";
    internal const string ENTRY_POINT = "main";

    internal const string AO_8_PATH = "/shaders/ssao/ao-8";
    internal const string AO_16_PATH = "/shaders/ssao/ao-16";
    internal const string AO_64_PATH = "/shaders/ssao/ao-64";
    internal const string DENOISE_4_PATH = "/shaders/ssao/denoise-4";
    internal const string DENOISE_8_PATH = "/shaders/ssao/denoise-8";
    internal const string DENOISE_16_PATH = "/shaders/ssao/denoise-16";
    internal const string COMPOSITE_PATH = "/shaders/ssao/composite";
    internal const string BLUE_NOISE_PATH = "/textures/blue-noise";
    internal const string AO_8_SPIRV_FILE = "SsaoAo8.frag.spv";
    internal const string AO_16_SPIRV_FILE = "SsaoAo16.frag.spv";
    internal const string AO_64_SPIRV_FILE = "SsaoAo64.frag.spv";
    internal const string DENOISE_4_SPIRV_FILE = "SsaoDenoise4.frag.spv";
    internal const string DENOISE_8_SPIRV_FILE = "SsaoDenoise8.frag.spv";
    internal const string DENOISE_16_SPIRV_FILE = "SsaoDenoise16.frag.spv";
    internal const string COMPOSITE_SPIRV_FILE = "SsaoComposite.frag.spv";

    internal const string SCENE_DEPTH_TEXTURE = "sceneDepth";
    internal const string BLUE_NOISE_TEXTURE = "blueNoise";
    internal const string AO_TEXTURE = "aoTexture";
    internal const string SOURCE_COLOR_TEXTURE = "sourceColor";

    internal static readonly ShaderUniformAsset[] AO_UNIFORMS = [
        Uniform("uProjectionInverse", ShaderUniformType.Matrix4x4),
        Uniform("uRadius", ShaderUniformType.Float),
        Uniform("uDistanceFalloff", ShaderUniformType.Float),
        Uniform("uBiasOffset", ShaderUniformType.Float),
        Uniform("uBiasMultiplier", ShaderUniformType.Float),
        Uniform("uScreenSpaceRadius", ShaderUniformType.Bool),
    ];

    internal static readonly ShaderUniformAsset[] DENOISE_UNIFORMS = [
        Uniform("uProjectionInverse", ShaderUniformType.Matrix4x4),
        Uniform("uDenoiseRadius", ShaderUniformType.Float),
        Uniform("uRadius", ShaderUniformType.Float),
        Uniform("uDistanceFalloff", ShaderUniformType.Float),
        Uniform("uScreenSpaceRadius", ShaderUniformType.Bool),
        Uniform("uIteration", ShaderUniformType.UInt),
    ];

    internal static readonly ShaderUniformAsset[] COMPOSITE_UNIFORMS = [
        Uniform("uAoColor", ShaderUniformType.Vector4),
        Uniform("uIntensity", ShaderUniformType.Float),
    ];

    internal static readonly string[] AO_TEXTURES = [SCENE_DEPTH_TEXTURE, BLUE_NOISE_TEXTURE];

    internal static readonly string[] DENOISE_TEXTURES = [
        AO_TEXTURE,
        SCENE_DEPTH_TEXTURE,
        BLUE_NOISE_TEXTURE,
    ];

    internal static readonly string[] COMPOSITE_TEXTURES = [SOURCE_COLOR_TEXTURE, AO_TEXTURE];

    private static ShaderUniformAsset Uniform(string name, ShaderUniformType type) => new() {
        Name = name,
        Type = type,
        Default = [],
    };
}
