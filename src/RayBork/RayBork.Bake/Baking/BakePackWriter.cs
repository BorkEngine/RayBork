using RayBork.Bake.Baking;
using RayBork.Bake.Diagnostics;
using RayBork.Bake.Incremental;
using RayBork.Internal;

namespace RayBork.Bake.Baking;

/// <summary>
/// Stages every pack in a unique temporary file next to its target, then replaces the targets.
/// </summary>
/// <remarks>
/// Staging all packs before any target is touched means a discovery, binding, validation, processor,
/// or pack-layout failure leaves every previous target untouched. During publication an existing
/// target is moved to a backup before the staged file takes its place; a failure restores every prior
/// target. A process crash inside the publication window cannot be rolled back.
/// </remarks>
internal static class BakePackWriter {
    /// <summary>Composes and publishes every pack.</summary>
    /// <param name="outputs">The configured outputs in order.</param>
    /// <param name="entries">The entry list per pack.</param>
    /// <param name="mode">The compression mode.</param>
    /// <param name="diagnostics">The diagnostic bag.</param>
    /// <returns><see langword="true"/> when every pack was replaced.</returns>
    internal static bool TryWriteAll(
        IReadOnlyList<BakePackOutput> outputs,
        IReadOnlyList<IReadOnlyList<BakedEntry>> entries,
        BakeMode mode,
        BakeDiagnostics diagnostics) {
        ArgumentNullException.ThrowIfNull(outputs);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(diagnostics);
        if (outputs.Count != entries.Count) {
            throw new ArgumentException("Every pack output must have exactly one entry list.", nameof(entries));
        }

        var staged = new List<StagedPack>(outputs.Count);
        foreach (BakePackOutput output in outputs) {
            StagedPack? pack = Stage(output, entries[output.Pack.Index], mode, diagnostics);
            if (pack is null) {
                DeleteAll(staged);
                return false;
            }

            staged.Add(pack);
        }

        if (!Publish(staged, diagnostics)) {
            DeleteAll(staged);
            return false;
        }

        return true;
    }

    private static StagedPack? Stage(
        BakePackOutput output,
        IReadOnlyList<BakedEntry> entries,
        BakeMode mode,
        BakeDiagnostics diagnostics) {
        string? temporary = null;
        try {
            string? directory = Path.GetDirectoryName(output.Path);
            if (!string.IsNullOrEmpty(directory)) {
                Directory.CreateDirectory(directory);
            }

            temporary = output.Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var builder = new PackBuilder(
                output.Pack.PackPath,
                mode.ToCompressionPolicy());
            foreach (BakedEntry entry in entries.OrderBy(entry => entry.Path, StringComparer.Ordinal)) {
                builder.Add(entry.Path, entry.Type, entry.Payload, entry.Alignment);
            }

            using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                FileOptions.SequentialScan)) {
                builder.WriteTo(stream);
                stream.Flush(flushToDisk: true);
            }

            return new StagedPack(output.Path, temporary);
        } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or InvalidOperationException) {
            if (temporary is not null) {
                TryDelete(temporary);
            }

            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.PACK_WRITE_FAILED,
                $"The pack '{output.Pack.PackPath}' could not be written: {exception.Message}",
                output.Path);
            return null;
        }
    }

    private static bool Publish(IReadOnlyList<StagedPack> staged, BakeDiagnostics diagnostics) {
        var published = new List<(StagedPack Pack, string? Backup)>(staged.Count);
        foreach (StagedPack pack in staged) {
            string? backup = null;
            try {
                if (File.Exists(pack.Target)) {
                    backup = pack.Target + "." + Guid.NewGuid().ToString("N") + ".bak";
                    File.Move(pack.Target, backup);
                }

                File.Move(pack.Temporary, pack.Target);
                published.Add((pack, backup));
            } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or ArgumentException or NotSupportedException) {
                if (backup is not null) {
                    TryMove(backup, pack.Target);
                }

                RollBack(published);
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.PACK_WRITE_FAILED,
                    $"The pack '{pack.Target}' could not be replaced atomically: {exception.Message}",
                    pack.Target);
                return false;
            }
        }

        foreach ((_, string? backup) in published) {
            if (backup is not null) {
                TryDelete(backup);
            }
        }

        return true;
    }

    private static void RollBack(IReadOnlyList<(StagedPack Pack, string? Backup)> published) {
        for (int index = published.Count - 1; index >= 0; index--) {
            (StagedPack pack, string? backup) = published[index];
            TryDelete(pack.Target);
            if (backup is not null) {
                TryMove(backup, pack.Target);
            }
        }
    }

    private static void DeleteAll(IReadOnlyList<StagedPack> staged) {
        foreach (StagedPack pack in staged) {
            TryDelete(pack.Temporary);
        }
    }

    private static void TryDelete(string path) {
        try {
            if (File.Exists(path)) {
                File.Delete(path);
            }
        } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
            // Best effort cleanup.
        }
    }

    private static void TryMove(string source, string destination) {
        try {
            File.Move(source, destination, overwrite: true);
        } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
            // Best effort rollback.
        }
    }

    private sealed record StagedPack(string Target, string Temporary);
}
