using RayBork.Bake.Diagnostics;
using RayBork.Bake.Incremental;
using RayBork.Bake.Paths;
using RayBork.Bake.Processors;
using RayBork.Bake.Sources;

namespace RayBork.Bake.Baking;

/// <summary>
/// The complete multi-pack bake pipeline: incremental reuse, data binding, processor execution,
/// validation, pack composition, atomic publication, and manifest recording.
/// </summary>
internal static class BakePipeline {
    /// <summary>Runs one bake or check.</summary>
    /// <param name="project">The validated project.</param>
    /// <param name="options">The invocation options.</param>
    /// <param name="sources">The discovered sources.</param>
    /// <param name="diagnostics">The diagnostic bag.</param>
    /// <returns>The invocation result.</returns>
    internal static BakeResult Run(
        BakeProject project,
        BakeOptions options,
        BakeSourceSet sources,
        BakeDiagnostics diagnostics) {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(diagnostics);

        IReadOnlyList<BakePackOutput> outputs;
        try {
            outputs = BakeOutputLayout.Resolve(
                project,
                BakeOutputLayout.ResolveRoot(options.OutputRoot));
        } catch (Exception exception) when (exception is ArgumentException or NotSupportedException
            or PathTooLongException) {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.CONFIGURATION_INVALID,
                $"The output root is invalid: {exception.Message}");
            return Failure(diagnostics);
        }

        if (diagnostics.HasErrors) {
            return Failure(diagnostics);
        }

        BakeIncrementalCache? cache = BakeIncrementalCache.TryCreate(project, options, sources);
        if (cache is { IsUpToDate: true }) {
            return UpToDate(project, outputs, cache, diagnostics);
        }

        var packEntries = new List<List<BakedEntry>>(outputs.Count);
        var packSources = new List<List<BakeManifestSource>>(outputs.Count);
        var packLayoutHashes = new List<string>(outputs.Count);

        for (int packIndex = 0; packIndex < outputs.Count; packIndex++) {
            BakePackOutput output = outputs[packIndex];
            BakePackSources discovered = sources.Packs[packIndex];
            var entries = new List<BakedEntry>();
            var sourceRecords = new List<BakeManifestSource>();

            foreach (BakeSource source in discovered.Sources) {
                if (cache is not null
                    && cache.TryGet(output.Pack, source, out CachedSource? cached)
                    && cached is not null) {
                    entries.AddRange(cached.Entries);
                    sourceRecords.Add(cached.Manifest);
                    continue;
                }

                if (source.Kind == BakeSourceKind.Data) {
                    if (!BakeDataBinder.TryBind(project, source, diagnostics, out BakedEntry? entry)
                        || entry is null) {
                        if (!diagnostics.HasErrors) {
                            AddSourceFailure(diagnostics, project, source, "the data source did not bind");
                        }

                        continue;
                    }

                    entries.Add(entry);
                    if (!TryAddSourceRecord(project, source, [entry], [], sourceRecords, diagnostics)) {
                        continue;
                    }

                    continue;
                }

                ProcessedSource? processed = BakeProcessorRunner.Run(project, source, diagnostics);
                if (processed is null) {
                    if (!diagnostics.HasErrors) {
                        AddSourceFailure(diagnostics, project, source, "the processor produced no result");
                    }

                    continue;
                }

                entries.AddRange(processed.Entries);
                if (!TryAddSourceRecord(
                    project,
                    source,
                    processed.Entries,
                    processed.AdditionalInputs,
                    sourceRecords,
                    diagnostics)) {
                    continue;
                }
            }

            if (!TryValidateEntries(project, output.Pack, entries, diagnostics)) {
                continue;
            }

            entries.Sort(static (left, right) => string.CompareOrdinal(left.Path, right.Path));
            packEntries.Add(entries);
            packSources.Add(sourceRecords);
            packLayoutHashes.Add(BakeLayoutHash.Compute(entries));
        }

        if (diagnostics.HasErrors
            || (options.WarningsAsErrors && diagnostics.HasWarnings)
            || packEntries.Count != outputs.Count) {
            return Failure(diagnostics);
        }

        if (options.Check) {
            return Success(project, outputs, packEntries, packWritten: false, diagnostics);
        }

        if (!BakePackWriter.TryWriteAll(outputs, packEntries, options.Mode, diagnostics)) {
            return Failure(diagnostics);
        }

        WriteManifest(
            project,
            options,
            outputs,
            packSources,
            packLayoutHashes,
            diagnostics);

        return Success(project, outputs, packEntries, packWritten: true, diagnostics);
    }

    private static bool TryAddSourceRecord(
        BakeProject project,
        BakeSource source,
        IReadOnlyList<BakedEntry> entries,
        IReadOnlyList<string> additionalInputs,
        List<BakeManifestSource> sourceRecords,
        BakeDiagnostics diagnostics) {
        try {
            sourceRecords.Add(CreateSourceRecord(project, source, entries, additionalInputs));
            return true;
        } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException) {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.INCREMENTAL_FAILED,
                $"The tracked inputs of '{source.RelativePath}' could not be hashed: "
                    + exception.Message,
                BakePath.ForDiagnostic(project.ProjectRoot, source.FullPath),
                assetPath: source.BaseAssetPath);
            return false;
        }
    }

    private static BakeManifestSource CreateSourceRecord(
        BakeProject project,
        BakeSource source,
        IReadOnlyList<BakedEntry> entries,
        IReadOnlyList<string> additionalInputs) {
        var inputs = new List<BakeManifestInput>();
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (string path in source.PrimaryInputs.Concat(additionalInputs)) {
            string display = BakePath.ForDiagnostic(project.ProjectRoot, path);
            if (!paths.Add(display)) {
                continue;
            }

            inputs.Add(new BakeManifestInput {
                Path = display,
                Hash = BakeHash.HashFile(path),
            });
        }

        inputs.Sort(static (left, right) => string.CompareOrdinal(left.Path, right.Path));
        var outputs = new List<BakeManifestOutput>(entries.Count);
        foreach (BakedEntry entry in entries) {
            outputs.Add(new BakeManifestOutput {
                Path = entry.Path,
                Type = (int)entry.Type,
                Alignment = entry.Alignment,
                Hash = BakeHash.HashBytes(entry.Payload),
            });
        }

        outputs.Sort(static (left, right) => string.CompareOrdinal(left.Path, right.Path));
        return new BakeManifestSource {
            Id = source.SourceId,
            Kind = source.KindName,
            Processor = source.ProcessorName,
            Inputs = inputs,
            Outputs = outputs,
        };
    }

    private static bool TryValidateEntries(
        BakeProject project,
        BakePack pack,
        IReadOnlyList<BakedEntry> entries,
        BakeDiagnostics diagnostics) {
        var byPath = new Dictionary<string, BakedEntry>(StringComparer.Ordinal);
        bool valid = true;
        foreach (BakedEntry entry in entries) {
            if (byPath.TryGetValue(entry.Path, out BakedEntry? existing)) {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.SOURCE_INVALID,
                    $"The asset path '{entry.Path}' is produced more than once in pack "
                        + $"'{pack.PackPath}' (sources '{existing.SourceId}' and '{entry.SourceId}').",
                    BakePath.ForDiagnostic(project.ProjectRoot, pack.Root),
                    assetPath: entry.Path);
                valid = false;
                continue;
            }

            byPath.Add(entry.Path, entry);
        }

        return valid;
    }

    private static void WriteManifest(
        BakeProject project,
        BakeOptions options,
        IReadOnlyList<BakePackOutput> outputs,
        IReadOnlyList<List<BakeManifestSource>> packSources,
        IReadOnlyList<string> layoutHashes,
        BakeDiagnostics diagnostics) {
        var manifest = new BakeManifest {
            Schema = BakeManifest.CURRENT_VERSION,
            ToolVersion = project.ToolVersion,
            ConfigurationHash = BakeConfigurationHash.Compute(project),
            Mode = options.Mode.ToName(),
            Packs = [],
        };

        for (int index = 0; index < outputs.Count; index++) {
            BakePackOutput output = outputs[index];
            string packHash;
            try {
                packHash = BakeHash.HashFile(output.Path);
            } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or ArgumentException or NotSupportedException) {
                diagnostics.Add(
                    BakeSeverity.Warning,
                    BakeDiagnosticCodes.INCREMENTAL_FAILED,
                    $"The pack '{output.Pack.PackPath}' was written, but its hash could not be "
                        + $"recorded: {exception.Message}. The next bake runs in full.",
                    output.Path);
                return;
            }

            manifest.Packs.Add(new BakeManifestPack {
                PackPath = output.Pack.PackPath,
                Root = output.Pack.RelativeRoot,
                LayoutHash = layoutHashes[index],
                PackHash = packHash,
                Sources = packSources[index],
            });
        }

        if (!BakeManifestStore.TryWrite(
            BakeManifestStore.ManifestPath(project.ProjectRoot),
            manifest)) {
            diagnostics.Add(
                BakeSeverity.Warning,
                BakeDiagnosticCodes.INCREMENTAL_FAILED,
                $"The packs were written, but the incremental manifest "
                    + $"'{BakeManifestStore.ManifestPath(project.ProjectRoot)}' could not be written. "
                    + "The next bake runs in full.");
        }
    }

    private static BakeResult UpToDate(
        BakeProject project,
        IReadOnlyList<BakePackOutput> outputs,
        BakeIncrementalCache cache,
        BakeDiagnostics diagnostics) {
        var packs = new List<BakedPackResult>(project.Packs.Count);
        var allEntries = new List<BakedEntry>();
        for (int index = 0; index < project.Packs.Count; index++) {
            BakePack pack = project.Packs[index];
            var entries = new List<BakedEntry>();
            foreach (BakeManifestSource source in cache.Manifest.Packs[index].Sources) {
                if (!cache.TryGet(pack, source.Id, out CachedSource? cached) || cached is null) {
                    diagnostics.Add(
                        BakeSeverity.Error,
                        BakeDiagnosticCodes.INCREMENTAL_FAILED,
                        $"The incremental manifest is inconsistent for source '{source.Id}' in "
                            + $"pack '{pack.PackPath}'.",
                        BakeManifestStore.ManifestPath(project.ProjectRoot));
                    return Failure(diagnostics);
                }

                entries.AddRange(cached.Entries);
            }

            entries.Sort(static (left, right) => string.CompareOrdinal(left.Path, right.Path));
            allEntries.AddRange(entries);
            packs.Add(new BakedPackResult(
                pack,
                outputs[index].Path,
                packWritten: false,
                entries));
        }

        allEntries.Sort(static (left, right) => string.CompareOrdinal(left.Path, right.Path));
        return new BakeResult(
            BakeStatus.UpToDate,
            packs,
            allEntries,
            diagnostics.GetSorted());
    }

    private static void AddSourceFailure(
        BakeDiagnostics diagnostics,
        BakeProject project,
        BakeSource source,
        string reason)
        => diagnostics.Add(
            BakeSeverity.Error,
            BakeDiagnosticCodes.SOURCE_INVALID,
            $"The source '{source.RelativePath}' could not be baked: {reason}.",
            BakePath.ForDiagnostic(project.ProjectRoot, source.FullPath),
            assetPath: source.BaseAssetPath);

    private static BakeResult Success(
        BakeProject project,
        IReadOnlyList<BakePackOutput> outputs,
        IReadOnlyList<List<BakedEntry>> entries,
        bool packWritten,
        BakeDiagnostics diagnostics) {
        var packs = new List<BakedPackResult>(outputs.Count);
        var allEntries = new List<BakedEntry>();
        for (int index = 0; index < outputs.Count; index++) {
            allEntries.AddRange(entries[index]);
            packs.Add(new BakedPackResult(
                outputs[index].Pack,
                outputs[index].Path,
                packWritten,
                entries[index]));
        }

        allEntries.Sort(static (left, right) => string.CompareOrdinal(left.Path, right.Path));
        return new BakeResult(BakeStatus.Success, packs, allEntries, diagnostics.GetSorted());
    }

    private static BakeResult Failure(BakeDiagnostics diagnostics)
        => new(
            BakeStatus.DiagnosticsError,
            [],
            [],
            diagnostics.GetSorted());
}
