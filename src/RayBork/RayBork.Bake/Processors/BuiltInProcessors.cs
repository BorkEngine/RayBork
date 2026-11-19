using RayBork.Bake.Baking;

namespace RayBork.Bake.Processors;

/// <summary>The built-in processor set, ordered so a specific extension beats the raw
/// fallback.</summary>
/// <remarks><see cref="CONFIGURATION_MARKER"/> is part of the configuration hash.</remarks>
internal static class BuiltInProcessors
{
    /// <summary>Identifies the current built-in processor implementation.</summary>
    internal const string CONFIGURATION_MARKER =
        "raybork-bake/m5/texture-shader-model-font-collision-1-shader-abi-2";

    /// <summary>Adds the built-in processors to a definition.</summary>
    internal static void AddTo(BakeProjectDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        definition.AddProcessor(
            new TextureProcessor(() => definition.TextureFilter, () => definition.TextureWrap)
        );
        definition.AddProcessor(new ShaderProcessor());
        definition.AddProcessor(new ModelProcessor());
        definition.AddProcessor(new FontProcessor());
        definition.AddProcessor(new RawAssetProcessor());
    }
}
