using RayBork.Bake.Baking;
using RayBork.Bake.Paths;
using RayBork.Bake.Sources;
using RayBork.Internal;

namespace RayBork.Bake.Incremental;

/// <summary>
/// The loaded per-source incremental state for one invocation.
/// </summary>
/// <remarks>
/// A cache is created only when the manifest schema, tool version, configuration hash, mode, pack
/// list, and physical pack hashes all match. Any mismatch disables reuse and runs a full bake. The
/// caller checks <see cref="IsUpToDate"/> before processing; otherwise each source with a valid hit
/// can reuse its previous entries and only the misses run.
/// </remarks>
internal sealed class BakeIncrementalCache {
    private readonly Dictionary<(int PackIndex, string SourceId), CachedSource> _sources;

    private BakeIncrementalCache(
        BakeManifest manifest,
        Dictionary<(int PackIndex, string SourceId), CachedSource> sources,
        bool isUpToDate) {
        Manifest = manifest;
        _sources = sources;
        IsUpToDate = isUpToDate;
    }

    /// <summary>Gets the previous manifest.</summary>
    internal BakeManifest Manifest { get; }

    /// <summary>Gets a value indicating every current source and pack is unchanged.</summary>
    internal bool IsUpToDate { get; }

    /// <summary>Loads a compatible cache and prepares every reusable source.</summary>
    /// <param name="project">The validated project.</param>
    /// <param name="options">The invocation options.</param>
    /// <param name="sources">The current discovered sources.</param>
    /// <returns>The cache, or <see langword="null"/> when a full bake is required.</returns>
    internal static BakeIncrementalCache? TryCreate(
        BakeProject project,
        BakeOptions options,
        BakeSourceSet sources) {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(sources);

        if (options.Check || options.Force) {
            return null;
        }

        BakeManifest? manifest = BakeManifestStore.TryRead(
            BakeManifestStore.ManifestPath(project.ProjectRoot));
        if (manifest is null
            || manifest.Schema != BakeManifest.CURRENT_VERSION
            || !string.Equals(manifest.ToolVersion, project.ToolVersion, StringComparison.Ordinal)
            || !string.Equals(
                manifest.ConfigurationHash,
                BakeConfigurationHash.Compute(project),
                StringComparison.Ordinal)
            || !string.Equals(manifest.Mode, options.Mode.ToName(), StringComparison.Ordinal)
            || manifest.Packs.Count != project.Packs.Count) {
            return null;
        }

        string outputRoot;
        IReadOnlyList<BakePackOutput> outputs;
        try {
            outputRoot = BakeOutputLayout.ResolveRoot(options.OutputRoot);
            outputs = BakeOutputLayout.Resolve(project, outputRoot);
        } catch (Exception exception) when (exception is ArgumentException or NotSupportedException
            or PathTooLongException) {
            return null;
        }

        for (int index = 0; index < project.Packs.Count; index++) {
            BakeManifestPack previous = manifest.Packs[index];
            BakePack pack = project.Packs[index];
            if (!string.Equals(previous.PackPath, pack.PackPath, StringComparison.Ordinal)
                || !string.Equals(previous.Root, pack.RelativeRoot, StringComparison.Ordinal)) {
                return null;
            }

            if (!File.Exists(outputs[index].Path)
                || !string.Equals(
                    previous.PackHash,
                    BakeHash.HashFile(outputs[index].Path),
                    StringComparison.Ordinal)) {
                return null;
            }
        }

        var previousSources = new Dictionary<(int, string), BakeManifestSource>();
        foreach (int index in Enumerable.Range(0, manifest.Packs.Count)) {
            foreach (BakeManifestSource source in manifest.Packs[index].Sources) {
                previousSources[(index, source.Id)] = source;
            }
        }

        var reusable = new Dictionary<(int PackIndex, string SourceId), CachedSource>();
        foreach (BakePackSources packSources in sources.Packs) {
            BakeManifestPack previousPack = manifest.Packs[packSources.Pack.Index];
            using PackArchive? archive = TryOpenPack(outputs[packSources.Pack.Index].Path);
            if (archive is null) {
                return null;
            }

            foreach (BakeSource source in packSources.Sources) {
                if (!previousSources.TryGetValue(
                    (packSources.Pack.Index, source.SourceId),
                    out BakeManifestSource? previousSource)) {
                    continue;
                }

                if (!TryMatchSource(
                    project,
                    source,
                    previousSource,
                    out IReadOnlyList<BakeManifestInput>? inputs)) {
                    continue;
                }

                if (!TryReadEntries(archive, source, previousSource, out IReadOnlyList<BakedEntry>? entries)) {
                    return null;
                }

                reusable[(packSources.Pack.Index, source.SourceId)] = new CachedSource(
                    previousSource,
                    [.. inputs!],
                    [.. entries!]);
            }
        }

        bool upToDate = true;
        foreach (BakePackSources packSources in sources.Packs) {
            BakeManifestPack previousPack = manifest.Packs[packSources.Pack.Index];
            if (previousPack.Sources.Count != packSources.Sources.Count) {
                upToDate = false;
                break;
            }

            foreach (BakeSource source in packSources.Sources) {
                if (!reusable.ContainsKey((packSources.Pack.Index, source.SourceId))) {
                    upToDate = false;
                    break;
                }
            }

            if (!upToDate) {
                break;
            }
        }

        return new BakeIncrementalCache(manifest, reusable, upToDate);
    }

    /// <summary>Tries to reuse the entries of one source.</summary>
    /// <param name="pack">The source's pack.</param>
    /// <param name="source">The source.</param>
    /// <param name="cached">The cached source record when unchanged.</param>
    /// <returns><see langword="true"/> when the source can be reused.</returns>
    internal bool TryGet(
        BakePack pack,
        BakeSource source,
        out CachedSource? cached) {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(source);
        return _sources.TryGetValue((pack.Index, source.SourceId), out cached);
    }

    /// <summary>Tries to reuse the entries of one source by identifier.</summary>
    /// <param name="pack">The source's pack.</param>
    /// <param name="sourceId">The stable source identifier.</param>
    /// <param name="cached">The cached source record when unchanged.</param>
    /// <returns><see langword="true"/> when the source can be reused.</returns>
    internal bool TryGet(
        BakePack pack,
        string sourceId,
        out CachedSource? cached) {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        return _sources.TryGetValue((pack.Index, sourceId), out cached);
    }

    private static PackArchive? TryOpenPack(string path) {
        try {
            return PackArchive.Open(path);
        } catch (Exception exception) when (exception is not OutOfMemoryException) {
            return null;
        }
    }

    private static bool TryMatchSource(
        BakeProject project,
        BakeSource source,
        BakeManifestSource previous,
        out IReadOnlyList<BakeManifestInput>? inputs) {
        inputs = null;
        if (!string.Equals(previous.Kind, source.KindName, StringComparison.Ordinal)
            || !string.Equals(previous.Processor, source.ProcessorName, StringComparison.Ordinal)) {
            return false;
        }

        var paths = new HashSet<string>(
            previous.Inputs.Select(input => input.Path),
            StringComparer.Ordinal);
        foreach (string primary in source.PrimaryInputs) {
            paths.Add(BakePath.ForDiagnostic(project.ProjectRoot, primary));
        }

        var candidates = new List<BakeManifestInput>(paths.Count);
        foreach (string path in paths) {
            string fullPath;
            try {
                fullPath = Path.IsPathRooted(path)
                    ? path
                    : Path.GetFullPath(Path.Combine(
                        project.ProjectRoot,
                        path.Replace('/', Path.DirectorySeparatorChar)));
                candidates.Add(new BakeManifestInput {
                    Path = path,
                    Hash = BakeHash.HashFile(fullPath),
                });
            } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or ArgumentException or NotSupportedException) {
                return false;
            }
        }

        candidates.Sort(static (left, right) => string.CompareOrdinal(left.Path, right.Path));
        BakeManifestInput[] expected = [.. previous.Inputs
            .OrderBy(input => input.Path, StringComparer.Ordinal)];
        if (candidates.Count != expected.Length) {
            return false;
        }

        for (int index = 0; index < expected.Length; index++) {
            if (!string.Equals(candidates[index].Path, expected[index].Path, StringComparison.Ordinal)
                || !string.Equals(candidates[index].Hash, expected[index].Hash, StringComparison.Ordinal)) {
                return false;
            }
        }

        inputs = candidates;
        return true;
    }

    private static bool TryReadEntries(
        PackArchive archive,
        BakeSource source,
        BakeManifestSource previous,
        out IReadOnlyList<BakedEntry>? entries) {
        entries = null;
        var values = new List<BakedEntry>(previous.Outputs.Count);
        foreach (BakeManifestOutput output in previous.Outputs) {
            if (output.Type is < 0 or > (int)AssetEntryType.Data
                || !archive.TryGetEntry(output.Path, out PackEntry? entry)
                || (int)entry.Type != output.Type
                || entry.Alignment != output.Alignment) {
                return false;
            }

            byte[] payload;
            try {
                payload = archive.ReadPayload(entry);
            } catch (Exception exception) when (exception is not OutOfMemoryException) {
                return false;
            }

            if (!string.Equals(
                BakeHash.HashBytes(payload),
                output.Hash,
                StringComparison.Ordinal)) {
                return false;
            }

            values.Add(new BakedEntry(
                output.Path,
                (AssetEntryType)output.Type,
                payload,
                output.Alignment,
                source.SourceId));
        }

        entries = values;
        return true;
    }
}

/// <summary>The reusable state of one unchanged source.</summary>
internal sealed class CachedSource {
    /// <summary>Initializes a cached source.</summary>
    /// <param name="manifest">The previous source record.</param>
    /// <param name="inputs">The verified input hashes.</param>
    /// <param name="entries">The payloads read from the previous pack.</param>
    internal CachedSource(
        BakeManifestSource manifest,
        IReadOnlyList<BakeManifestInput> inputs,
        IReadOnlyList<BakedEntry> entries) {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(entries);

        Manifest = manifest;
        Inputs = [.. inputs];
        Entries = [.. entries];
    }

    /// <summary>Gets the previous manifest source record.</summary>
    internal BakeManifestSource Manifest { get; }

    /// <summary>Gets the verified input hashes.</summary>
    internal IReadOnlyList<BakeManifestInput> Inputs { get; }

    /// <summary>Gets the reusable entries.</summary>
    internal IReadOnlyList<BakedEntry> Entries { get; }
}
