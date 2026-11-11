using RayBork.Bake.Processors;

namespace RayBork.Bake.Baking;

/// <summary>
/// The validated, immutable configuration of one bake.
/// </summary>
internal sealed class BakeProject {
    /// <summary>Initializes a validated project.</summary>
    /// <param name="projectRoot">The absolute project root.</param>
    /// <param name="packs">The ordered packs; the first is the main pack.</param>
    /// <param name="textureFilter">The default texture filter.</param>
    /// <param name="textureWrap">The default texture wrap mode.</param>
    /// <param name="assetTypes">The registered data types keyed by full name.</param>
    /// <param name="processors">The registered processor registrations in order.</param>
    /// <param name="processorRegistry">The immutable extension registry.</param>
    /// <param name="toolVersion">The tool version recorded in the incremental manifest.</param>
    internal BakeProject(
        string projectRoot,
        IReadOnlyList<BakePack> packs,
        RayBork.TextureFilter textureFilter,
        RayBork.TextureWrap textureWrap,
        IReadOnlyDictionary<string, BakeAssetTypeRegistration> assetTypes,
        IReadOnlyList<BakeProcessorRegistration> processors,
        BakeProcessorRegistry processorRegistry,
        string toolVersion) {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentNullException.ThrowIfNull(packs);
        ArgumentNullException.ThrowIfNull(assetTypes);
        ArgumentNullException.ThrowIfNull(processors);
        ArgumentNullException.ThrowIfNull(processorRegistry);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolVersion);
        if (packs.Count == 0) {
            throw new ArgumentException("A project must configure at least one pack.", nameof(packs));
        }

        ProjectRoot = projectRoot;
        Packs = [.. packs];
        TextureFilter = textureFilter;
        TextureWrap = textureWrap;
        AssetTypes = new Dictionary<string, BakeAssetTypeRegistration>(
            assetTypes,
            StringComparer.Ordinal);
        Processors = [.. processors];
        ProcessorRegistry = processorRegistry;
        ToolVersion = toolVersion;
    }

    /// <summary>Gets the absolute project root.</summary>
    internal string ProjectRoot { get; }

    /// <summary>Gets the ordered packs; the first is the main pack.</summary>
    internal IReadOnlyList<BakePack> Packs { get; }

    /// <summary>Gets the main pack.</summary>
    internal BakePack MainPack => Packs[0];

    /// <summary>Gets the default texture filter.</summary>
    internal RayBork.TextureFilter TextureFilter { get; }

    /// <summary>Gets the default texture wrap mode.</summary>
    internal RayBork.TextureWrap TextureWrap { get; }

    /// <summary>Gets the registered data types keyed by full name.</summary>
    internal IReadOnlyDictionary<string, BakeAssetTypeRegistration> AssetTypes { get; }

    /// <summary>Gets the processor registrations in order.</summary>
    internal IReadOnlyList<BakeProcessorRegistration> Processors { get; }

    /// <summary>Gets the immutable extension registry.</summary>
    internal BakeProcessorRegistry ProcessorRegistry { get; }

    /// <summary>Gets the tool version recorded in the incremental manifest.</summary>
    internal string ToolVersion { get; }
}
