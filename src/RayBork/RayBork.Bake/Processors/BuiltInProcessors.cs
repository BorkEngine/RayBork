using RayBork.Bake.Baking;

namespace RayBork.Bake.Processors;

/// <summary>
/// The extension point for the built-in processor set.
/// </summary>
/// <remarks>
/// The M3 default set is the raw fallback only. The texture, model, shader, and font processor task
/// extends this method; keeping the registration here means that task never edits the builder or the
/// discovery pipeline. <see cref="CONFIGURATION_MARKER"/> is part of the incremental configuration
/// hash so a built-in processor change invalidates cached sources.
/// </remarks>
internal static class BuiltInProcessors {
    /// <summary>Identifies the current built-in processor implementation.</summary>
    internal const string CONFIGURATION_MARKER = "raybork-bake/m3/raw-1";

    /// <summary>Adds the built-in processors to a definition.</summary>
    /// <param name="definition">The definition being configured.</param>
    internal static void AddTo(BakeProjectDefinition definition) {
        ArgumentNullException.ThrowIfNull(definition);
        definition.AddProcessor(new RawAssetProcessor());
    }
}
