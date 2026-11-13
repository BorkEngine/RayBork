using RayBork.Bake.Diagnostics;

namespace RayBork.Bake.Baking;

/// <summary>Classification of one bake invocation.</summary>
internal enum BakeStatus {
    /// <summary>Every pack validated and was written, or the check succeeded.</summary>
    Success,

    /// <summary>The incremental manifest matched; nothing was written.</summary>
    UpToDate,

    /// <summary>Source, binding, processor, validation, or pack diagnostics failed the bake.</summary>
    DiagnosticsError,

    /// <summary>The builder, project, or output configuration is invalid.</summary>
    ConfigurationError,
}

/// <summary>The outcome of one configured pack.</summary>
internal sealed class BakedPackResult {
    /// <summary>Initializes a pack result.</summary>
    /// <param name="pack">The configured pack.</param>
    /// <param name="packPath">The absolute output path.</param>
    /// <param name="packWritten">Whether the pack was written.</param>
    /// <param name="entries">The pack entries in ordinal path order.</param>
    internal BakedPackResult(
        BakePack pack,
        string packPath,
        bool packWritten,
        IReadOnlyList<BakedEntry> entries) {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentException.ThrowIfNullOrWhiteSpace(packPath);
        ArgumentNullException.ThrowIfNull(entries);

        Pack = pack;
        PackPath = packPath;
        PackWritten = packWritten;
        Entries = [.. entries];
        AssetCount = entries.Count(static entry => entry.Type != AssetEntryType.Raw);
        RawCount = entries.Count - AssetCount;
    }

    /// <summary>Gets the configured pack.</summary>
    internal BakePack Pack { get; }

    /// <summary>Gets the absolute output path.</summary>
    internal string PackPath { get; }

    /// <summary>Gets a value indicating whether the pack was written.</summary>
    internal bool PackWritten { get; }

    /// <summary>Gets the entries in ordinal path order.</summary>
    internal IReadOnlyList<BakedEntry> Entries { get; }

    /// <summary>Gets the shapeable asset entry count.</summary>
    internal int AssetCount { get; }

    /// <summary>Gets the raw payload entry count.</summary>
    internal int RawCount { get; }
}

/// <summary>The outcome of one bake invocation.</summary>
internal sealed class BakeResult {
    /// <summary>Initializes a result.</summary>
    /// <param name="status">The outcome classification.</param>
    /// <param name="packs">The per-pack results.</param>
    /// <param name="entries">The aggregate entries in ordinal path order.</param>
    /// <param name="diagnostics">The sorted diagnostics.</param>
    internal BakeResult(
        BakeStatus status,
        IReadOnlyList<BakedPackResult> packs,
        IReadOnlyList<BakedEntry> entries,
        IReadOnlyList<BakeDiagnostic> diagnostics) {
        ArgumentNullException.ThrowIfNull(packs);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(diagnostics);

        Status = status;
        Packs = [.. packs];
        Entries = [.. entries];
        Diagnostics = [.. diagnostics];
        AssetCount = entries.Count(static entry => entry.Type != AssetEntryType.Raw);
        RawCount = entries.Count - AssetCount;
    }

    /// <summary>Gets the outcome classification.</summary>
    internal BakeStatus Status { get; }

    /// <summary>Gets the per-pack results.</summary>
    internal IReadOnlyList<BakedPackResult> Packs { get; }

    /// <summary>Gets the aggregate entries in ordinal path order.</summary>
    internal IReadOnlyList<BakedEntry> Entries { get; }

    /// <summary>Gets the shapeable asset entry count.</summary>
    internal int AssetCount { get; }

    /// <summary>Gets the raw payload entry count.</summary>
    internal int RawCount { get; }

    /// <summary>Gets the sorted diagnostics.</summary>
    internal IReadOnlyList<BakeDiagnostic> Diagnostics { get; }
}
