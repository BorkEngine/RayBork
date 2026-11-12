using RayBork.Bake.Baking;
using RayBork.Bake.Diagnostics;
using RayBork.Bake.Paths;
using RayBork.Bake.Processors;
using RayBork.Bake.Toml;
using Tomlyn.Model;

namespace RayBork.Bake.Sources;

/// <summary>
/// Discovers and classifies every file under every configured pack root.
/// </summary>
/// <remarks>
/// <para>
/// Files are walked in ordinal pack-relative path order; reparse points are skipped. A non-TOML file
/// is owned by the processor whose registered extension is the longest matching suffix. A TOML file
/// with a root <c>[raybork]</c> table is a custom-data source. A TOML file next to a processor source
/// with the same base path is that source's sidecar. A TOML file whose root table names a processor
/// and which has no sibling processor source is a table-named processor source. Everything else is an
/// error.
/// </para>
/// <para>
/// A string value inside a table-named processor source that resolves to an existing file under the
/// pack root marks that file as a tracked input for discovery purposes. Such a file is not a source
/// itself; this is how a shader TOML's <c>vertex</c> and <c>fragment</c> GLSL files stay dependencies
/// rather than assets. The processor still registers them through
/// <see cref="AssetProcessorContext.AddInput"/>.
/// </para>
/// <para>
/// Sidecars are only associated with processors that declare a sidecar policy. A sidecar is never
/// created or rewritten. Duplicate canonical paths are reported immediately and fail the pack.
/// </para>
/// </remarks>
internal static class BakeSourceDiscovery {
    /// <summary>Discovers the sources of every configured pack.</summary>
    /// <param name="project">The validated project.</param>
    /// <param name="diagnostics">The diagnostic bag.</param>
    /// <returns>The source lists.</returns>
    internal static BakeSourceSet Discover(BakeProject project, BakeDiagnostics diagnostics) {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var packs = new List<BakePackSources>(project.Packs.Count);
        foreach (BakePack pack in project.Packs) {
            packs.Add(DiscoverPack(project, pack, diagnostics));
        }

        return new BakeSourceSet(packs);
    }

    private static BakePackSources DiscoverPack(
        BakeProject project,
        BakePack pack,
        BakeDiagnostics diagnostics) {
        var sources = new List<BakeSource>();
        var byBasePath = new Dictionary<string, BakeSource>(StringComparer.Ordinal);
        var fileStems = new Dictionary<string, BakeSource>(StringComparer.OrdinalIgnoreCase);
        var tomlPaths = new List<(string FullPath, string RelativePath)>();
        var otherPaths = new List<(string FullPath, string RelativePath)>();

        foreach (string fullPath in BakePath.EnumerateFilesSorted(pack.Root)) {
            if (!TryMakeRelative(pack.Root, fullPath, out string relativePath)) {
                continue;
            }

            bool isPlainToml = BakePath.IsToml(relativePath)
                && !(project.ProcessorRegistry.TryMatch(relativePath, out BakeProcessorMatch tomlMatch)
                    && tomlMatch.Extension.Contains('.', StringComparison.Ordinal));
            if (isPlainToml) {
                tomlPaths.Add((fullPath, relativePath));
            } else {
                otherPaths.Add((fullPath, relativePath));
            }
        }

        var tomlFiles = new List<TomlFile>(tomlPaths.Count);
        foreach ((string fullPath, string relativePath) in tomlPaths) {
            TomlFile? toml = ReadTomlFile(project, fullPath, relativePath, diagnostics);
            if (toml is not null) {
                tomlFiles.Add(toml);
            }
        }

        Dictionary<string, OwnerCandidate> owners = BuildOwners(project, otherPaths);
        var dataTomls = new List<TomlFile>();
        var sidecarTomls = new List<TomlFile>();
        var tableTomls = new List<(TomlFile Toml, BakeProcessorRegistration Processor)>();
        var trackedInputs = new HashSet<string>(PathComparer);

        foreach (TomlFile toml in tomlFiles) {
            TomlTable root = toml.Document.Root;
            string diagnosticPath = DiagnosticPath(project, toml.FullPath);

            if (root.TryGetValue("raybork", out object? metadata) && metadata is TomlTable) {
                dataTomls.Add(toml);
                continue;
            }

            string stem = BakePath.RemoveFinalExtension(toml.RelativePath);
            if (owners.TryGetValue(stem, out OwnerCandidate? owner)) {
                if (owner.Processor.Policy == SidecarPolicy.None) {
                    diagnostics.Add(
                        BakeSeverity.Error,
                        BakeDiagnosticCodes.TOML_INVALID,
                        $"The TOML file '{toml.RelativePath}' is next to '{owner.RelativePath}', whose "
                            + $"processor '{owner.Processor.Name}' declares no sidecar.",
                        diagnosticPath,
                        assetPath: owner.BaseAssetPath);
                } else {
                    sidecarTomls.Add(toml);
                }

                continue;
            }

            List<(TomlTable Table, BakeProcessorRegistration Processor)> namedTables =
                FindNamedTables(root, project.Processors);
            if (namedTables.Count == 1) {
                tableTomls.Add((toml, namedTables[0].Processor));
                CollectTrackedInputs(
                    namedTables[0].Table,
                    Path.GetDirectoryName(toml.FullPath) ?? pack.Root,
                    pack.Root,
                    trackedInputs);
                continue;
            }

            if (namedTables.Count > 1) {
                string names = string.Join(
                    ", ",
                    namedTables.Select(static entry => $"'{entry.Processor.Name}'"));
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.TOML_INVALID,
                    $"The TOML file '{toml.RelativePath}' declares more than one processor table: "
                        + $"{names}. A TOML file may be a source or sidecar for exactly one processor.",
                    diagnosticPath);
                continue;
            }

            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.TOML_INVALID,
                $"The TOML file '{toml.RelativePath}' has no root '[raybork]' table, no matching "
                    + "sibling source, and no table naming a registered processor. It is an orphan "
                    + "sidecar.",
                diagnosticPath);
        }

        foreach ((string fullPath, string relativePath) in otherPaths) {
            if (trackedInputs.Contains(fullPath)) {
                continue;
            }

            ClassifyNonToml(project, pack, fullPath, relativePath, sources, byBasePath, fileStems, diagnostics);
        }

        foreach (TomlFile toml in dataTomls) {
            AddDataToml(project, pack, toml, sources, byBasePath, diagnostics);
        }

        foreach (TomlFile toml in sidecarTomls) {
            string stem = BakePath.RemoveFinalExtension(toml.RelativePath);
            if (!fileStems.TryGetValue(stem, out BakeSource? sibling)) {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.TOML_INVALID,
                    $"The sidecar '{toml.RelativePath}' has no matching source file.",
                    DiagnosticPath(project, toml.FullPath));
                continue;
            }

            if (sibling.SidecarPath is not null) {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.TOML_INVALID,
                    $"The source '{sibling.RelativePath}' has more than one sidecar: "
                        + $"'{sibling.SidecarRelativePath}' and '{toml.RelativePath}'.",
                    DiagnosticPath(project, toml.FullPath),
                    assetPath: sibling.BaseAssetPath);
                continue;
            }

            sibling.SidecarPath = toml.FullPath;
            sibling.SidecarRelativePath = toml.RelativePath;
            sibling.SidecarDocument = toml.Document;
        }

        foreach ((TomlFile toml, BakeProcessorRegistration processor) in tableTomls) {
            AddTableToml(project, pack, toml, processor, sources, byBasePath, diagnostics);
        }

        sources.Sort(static (left, right) => string.CompareOrdinal(left.RelativePath, right.RelativePath));
        return new BakePackSources(pack, sources);
    }

    private static Dictionary<string, OwnerCandidate> BuildOwners(
        BakeProject project,
        IReadOnlyList<(string FullPath, string RelativePath)> files) {
        var owners = new Dictionary<string, OwnerCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach ((string fullPath, string relativePath) in files) {
            if (!project.ProcessorRegistry.TryMatch(relativePath, out BakeProcessorMatch match)) {
                continue;
            }

            if (!BakePath.TryToCanonicalBase(
                relativePath,
                match.Extension,
                out string baseAssetPath,
                out _)) {
                continue;
            }

            owners.TryAdd(
                BakePath.RemoveMatchedExtension(relativePath, match.Extension),
                new OwnerCandidate(relativePath, baseAssetPath, match.Processor));
        }

        return owners;
    }

    private static void ClassifyNonToml(
        BakeProject project,
        BakePack pack,
        string fullPath,
        string relativePath,
        List<BakeSource> sources,
        Dictionary<string, BakeSource> byBasePath,
        Dictionary<string, BakeSource> fileStems,
        BakeDiagnostics diagnostics) {
        if (project.ProcessorRegistry.TryMatch(relativePath, out BakeProcessorMatch match)) {
            if (!BakePath.TryToCanonicalBase(
                relativePath,
                match.Extension,
                out string baseAssetPath,
                out string? reason)) {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.SOURCE_INVALID,
                    $"The processor source '{relativePath}' does not map to a canonical base asset "
                        + $"path: {reason}. The processor '{match.Processor.Name}' owns the "
                        + $"'{match.Extension}' extension.",
                    DiagnosticPath(project, fullPath));
                return;
            }

            var source = new BakeSource(
                pack,
                BakeSourceKind.ProcessorFile,
                fullPath,
                relativePath,
                baseAssetPath,
                match.Processor,
                match.Extension);
            AddSource(
                pack,
                source,
                sources,
                byBasePath,
                fileStems,
                addFileStem: match.Processor.Policy != SidecarPolicy.None,
                project,
                diagnostics);
            return;
        }

        BakeProcessorRegistration? fallback = project.ProcessorRegistry.Fallback;
        if (fallback is null) {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.SOURCE_INVALID,
                $"The source '{relativePath}' in pack '{pack.PackPath}' is not claimed by any "
                    + "processor, and no raw fallback processor is registered.",
                DiagnosticPath(project, fullPath));
            return;
        }

        string extension = BakePath.GetFinalExtension(relativePath);
        if (!BakePath.TryToCanonicalBase(
            relativePath,
            extension,
            out string rawBasePath,
            out string? rawReason)) {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.SOURCE_INVALID,
                $"The raw source '{relativePath}' does not map to a canonical asset path: {rawReason}.",
                DiagnosticPath(project, fullPath));
            return;
        }

        var rawSource = new BakeSource(
            pack,
            BakeSourceKind.ProcessorFile,
            fullPath,
            relativePath,
            rawBasePath,
            fallback,
            extension);
        AddSource(
            pack,
            rawSource,
            sources,
            byBasePath,
            fileStems,
            addFileStem: false,
            project,
            diagnostics);
    }

    private static void AddDataToml(
        BakeProject project,
        BakePack pack,
        TomlFile toml,
        List<BakeSource> sources,
        Dictionary<string, BakeSource> byBasePath,
        BakeDiagnostics diagnostics) {
        if (!BakePath.TryToCanonicalBase(
            toml.RelativePath,
            "toml",
            out string assetPath,
            out string? reason)) {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.SOURCE_INVALID,
                $"The data TOML '{toml.RelativePath}' does not map to a canonical asset path: "
                    + $"{reason}. A data TOML name must not contain an extension or dot.",
                DiagnosticPath(project, toml.FullPath));
            return;
        }

        var source = new BakeSource(
            pack,
            BakeSourceKind.Data,
            toml.FullPath,
            toml.RelativePath,
            assetPath,
            processor: null,
            matchedExtension: null,
            toml.Document);
        AddSource(
            pack,
            source,
            sources,
            byBasePath,
            new Dictionary<string, BakeSource>(StringComparer.OrdinalIgnoreCase),
            addFileStem: false,
            project,
            diagnostics);
    }

    private static void AddTableToml(
        BakeProject project,
        BakePack pack,
        TomlFile toml,
        BakeProcessorRegistration processor,
        List<BakeSource> sources,
        Dictionary<string, BakeSource> byBasePath,
        BakeDiagnostics diagnostics) {
        if (!BakePath.TryToCanonicalBase(
            toml.RelativePath,
            "toml",
            out string assetPath,
            out string? reason)) {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.SOURCE_INVALID,
                $"The processor TOML '{toml.RelativePath}' does not map to a canonical asset "
                    + $"path: {reason}.",
                DiagnosticPath(project, toml.FullPath));
            return;
        }

        TomlDocument document = toml.Document;
        var source = new BakeSource(
            pack,
            BakeSourceKind.ProcessorToml,
            toml.FullPath,
            toml.RelativePath,
            assetPath,
            processor,
            matchedExtension: "toml",
            document) {
            SidecarPath = toml.FullPath,
            SidecarRelativePath = toml.RelativePath,
            SidecarDocument = document,
        };
        AddSource(
            pack,
            source,
            sources,
            byBasePath,
            new Dictionary<string, BakeSource>(StringComparer.OrdinalIgnoreCase),
            addFileStem: false,
            project,
            diagnostics);
    }

    private static List<(TomlTable Table, BakeProcessorRegistration Processor)> FindNamedTables(
        TomlTable root,
        IReadOnlyList<BakeProcessorRegistration> processors) {
        var matches = new List<(TomlTable, BakeProcessorRegistration)>();
        foreach (string key in root.Keys) {
            if (root[key] is not TomlTable table) {
                continue;
            }

            foreach (BakeProcessorRegistration processor in processors) {
                if (string.Equals(key, processor.Name, StringComparison.Ordinal)) {
                    matches.Add((table, processor));
                    break;
                }
            }
        }

        return matches;
    }

    private static void CollectTrackedInputs(
        object? value,
        string sourceDirectory,
        string packRoot,
        HashSet<string> trackedInputs) {
        switch (value) {
            case string text:
                if (string.IsNullOrWhiteSpace(text) || Path.IsPathRooted(text)) {
                    return;
                }

                string candidate;
                try {
                    candidate = Path.GetFullPath(Path.Combine(sourceDirectory, text));
                } catch (Exception exception) when (exception is ArgumentException or NotSupportedException
                    or PathTooLongException) {
                    return;
                }

                if (BakePath.IsUnderRoot(packRoot, candidate) && File.Exists(candidate)) {
                    trackedInputs.Add(candidate);
                }

                return;

            case TomlTable table:
                foreach (object item in table.Values) {
                    CollectTrackedInputs(item, sourceDirectory, packRoot, trackedInputs);
                }

                return;

            case TomlArray array:
                foreach (object? item in array) {
                    CollectTrackedInputs(item, sourceDirectory, packRoot, trackedInputs);
                }

                return;

            case TomlTableArray tableArray:
                foreach (TomlTable table in tableArray) {
                    CollectTrackedInputs(table, sourceDirectory, packRoot, trackedInputs);
                }

                return;
        }
    }

    private static void AddSource(
        BakePack pack,
        BakeSource source,
        List<BakeSource> sources,
        Dictionary<string, BakeSource> byBasePath,
        Dictionary<string, BakeSource> fileStems,
        bool addFileStem,
        BakeProject project,
        BakeDiagnostics diagnostics) {
        if (byBasePath.TryGetValue(source.BaseAssetPath, out BakeSource? existing)) {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.SOURCE_INVALID,
                $"The sources '{existing.RelativePath}' and '{source.RelativePath}' in pack "
                    + $"'{pack.PackPath}' both map to the canonical asset path "
                    + $"'{source.BaseAssetPath}'.",
                DiagnosticPath(project, source.FullPath),
                assetPath: source.BaseAssetPath);
            return;
        }

        byBasePath.Add(source.BaseAssetPath, source);
        if (addFileStem && source.MatchedExtension is not null) {
            fileStems.TryAdd(
                BakePath.RemoveMatchedExtension(source.RelativePath, source.MatchedExtension),
                source);
        }

        sources.Add(source);
    }

    private static TomlFile? ReadTomlFile(
        BakeProject project,
        string fullPath,
        string relativePath,
        BakeDiagnostics diagnostics) {
        string diagnosticPath = DiagnosticPath(project, fullPath);
        if (!BakeSourceText.TryRead(fullPath, diagnosticPath, diagnostics, out string text)) {
            return null;
        }

        if (!TomlDocument.TryParse(text, diagnosticPath, diagnostics, out TomlDocument? document)
            || document is null) {
            return null;
        }

        return new TomlFile(fullPath, relativePath, document);
    }

    private static bool TryMakeRelative(string root, string fullPath, out string relativePath) {
        relativePath = string.Empty;
        if (!BakePath.IsUnderRoot(root, fullPath)) {
            return false;
        }

        relativePath = BakePath.ToSlashes(Path.GetRelativePath(Path.GetFullPath(root), fullPath));
        return relativePath.Length > 0 && relativePath != ".";
    }

    private static string DiagnosticPath(BakeProject project, string fullPath)
        => BakePath.ForDiagnostic(project.ProjectRoot, fullPath);

    private static StringComparer PathComparer
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private sealed record TomlFile(string FullPath, string RelativePath, TomlDocument Document);

    private sealed record OwnerCandidate(
        string RelativePath,
        string BaseAssetPath,
        BakeProcessorRegistration Processor);
}
