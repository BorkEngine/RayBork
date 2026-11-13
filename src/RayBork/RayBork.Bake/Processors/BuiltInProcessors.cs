using RayBork.Bake.Baking;

namespace RayBork.Bake.Processors;

/// <summary>
/// The extension point for the built-in processor set.
/// </summary>
/// <remarks>
/// The M3 default set is the texture, shader, model, and font processors followed by the raw fallback.
/// Registration order is significant: a specific extension wins over the raw fallback.
/// <see cref="CONFIGURATION_MARKER"/> is part of the incremental configuration hash so a built-in
/// processor change invalidates cached sources.
/// </remarks>
internal static class BuiltInProcessors {
    /// <summary>Identifies the current built-in processor implementation.</summary>
    internal const string CONFIGURATION_MARKER = "raybork-bake/m5/texture-shader-model-font-collision-1";

    /// <summary>Adds the built-in processors to a definition.</summary>
    /// <param name="definition">The definition being configured.</param>
    internal static void AddTo(BakeProjectDefinition definition) {
        ArgumentNullException.ThrowIfNull(definition);

        definition.AddProcessor(new TextureProcessor(
            () => definition.TextureFilter,
            () => definition.TextureWrap));
        definition.AddProcessor(new ShaderProcessor());
        definition.AddProcessor(new ModelProcessor());
        definition.AddProcessor(new FontProcessor());
        definition.AddProcessor(new RawAssetProcessor());
    }
}
