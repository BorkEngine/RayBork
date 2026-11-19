using RayBork.Bake.Baking;
using RayBork.Bake.Processors;
using RayBork.Bake.Toml;

namespace RayBork.Bake.Sources;

/// <summary>How a discovered source produces its entries.</summary>
internal enum BakeSourceKind
{
    /// <summary>A <c>[raybork]</c> custom-data TOML.</summary>
    Data,

    /// <summary>A non-TOML file owned by a processor.</summary>
    ProcessorFile,

    /// <summary>A TOML file whose processor-named table is the processor source.</summary>
    ProcessorToml,
}

/// <summary>A discovered source with its canonical paths and optional sidecar.</summary>
internal sealed class BakeSource
{
    /// <summary>Initializes a discovered source.</summary>
    internal BakeSource(
        BakePack pack,
        BakeSourceKind kind,
        string fullPath,
        string relativePath,
        string baseAssetPath,
        BakeProcessorRegistration? processor,
        string? matchedExtension,
        TomlDocument? document = null
    )
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseAssetPath);

        Pack = pack;
        Kind = kind;
        FullPath = fullPath;
        RelativePath = relativePath;
        BaseAssetPath = baseAssetPath;
        Processor = processor;
        MatchedExtension = matchedExtension;
        Document = document;
    }

    /// <summary>Gets the owning pack.</summary>
    internal BakePack Pack { get; }

    /// <summary>Gets the source kind.</summary>
    internal BakeSourceKind Kind { get; }

    /// <summary>Gets the absolute source path.</summary>
    internal string FullPath { get; }

    /// <summary>Gets the pack-relative source path with <c>/</c> separators.</summary>
    internal string RelativePath { get; }

    /// <summary>Gets the canonical base asset path.</summary>
    internal string BaseAssetPath { get; }

    /// <summary>Gets the processor, or <see langword="null"/> for a data source.</summary>
    internal BakeProcessorRegistration? Processor { get; }

    /// <summary>Gets the matched extension, or <c>toml</c> for a table source.</summary>
    internal string? MatchedExtension { get; }

    /// <summary>Gets the parsed source TOML for a data or table source.</summary>
    internal TomlDocument? Document { get; }

    /// <summary>Gets or sets the absolute sidecar path, when discovered.</summary>
    internal string? SidecarPath { get; set; }

    /// <summary>Gets or sets the pack-relative sidecar path, when discovered.</summary>
    internal string? SidecarRelativePath { get; set; }

    /// <summary>Gets or sets the parsed sidecar document, when discovered.</summary>
    internal TomlDocument? SidecarDocument { get; set; }

    /// <summary>Gets the stable source identifier for the incremental manifest.</summary>
    internal string SourceId => RelativePath;

    /// <summary>Gets the kind name used for incremental identity.</summary>
    internal string KindName =>
        Kind switch
        {
            BakeSourceKind.Data => "data",
            BakeSourceKind.ProcessorToml => "processor-toml",
            _ => "processor-file",
        };

    /// <summary>Gets the processor name for a processor source.</summary>
    internal string? ProcessorName => Processor?.Name;

    /// <summary>Gets the primary input files, including a discovered sidecar.</summary>
    internal IReadOnlyList<string> PrimaryInputs
    {
        get
        {
            if (
                SidecarPath is null
                || string.Equals(SidecarPath, FullPath, StringComparison.Ordinal)
            )
            {
                return [FullPath];
            }

            return [FullPath, SidecarPath];
        }
    }
}

/// <summary>The source list of one configured pack.</summary>
internal sealed class BakePackSources
{
    /// <summary>Initializes a pack source list.</summary>
    internal BakePackSources(BakePack pack, IReadOnlyList<BakeSource> sources)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(sources);
        Pack = pack;
        Sources = [.. sources];
    }

    /// <summary>Gets the configured pack.</summary>
    internal BakePack Pack { get; }

    /// <summary>Gets the sources in deterministic order.</summary>
    internal IReadOnlyList<BakeSource> Sources { get; }
}

/// <summary>The source lists for every configured pack.</summary>
internal sealed class BakeSourceSet
{
    /// <summary>Initializes a source set.</summary>
    internal BakeSourceSet(IReadOnlyList<BakePackSources> packs)
    {
        ArgumentNullException.ThrowIfNull(packs);
        Packs = [.. packs];
    }

    /// <summary>Gets the per-pack source lists in configuration order.</summary>
    internal IReadOnlyList<BakePackSources> Packs { get; }
}
