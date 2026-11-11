using System.Text.Encodings.Web;
using System.Text.Json;

namespace RayBork.Bake.Incremental;

/// <summary>Reads and atomically writes the incremental manifest.</summary>
internal static class BakeManifestStore {
    private static readonly JsonSerializerOptions _options = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Gets the incremental state directory under a project root.</summary>
    /// <param name="projectRoot">The absolute project root.</param>
    /// <returns>The <c>obj/raybork/bake</c> directory.</returns>
    internal static string Directory(string projectRoot) {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        return Path.Combine(projectRoot, "obj", "raybork", "bake");
    }

    /// <summary>Gets the manifest path under a project root.</summary>
    /// <param name="projectRoot">The absolute project root.</param>
    /// <returns>The manifest path.</returns>
    internal static string ManifestPath(string projectRoot)
        => Path.Combine(Directory(projectRoot), "manifest.json");

    /// <summary>Tries to read and validate a manifest.</summary>
    /// <param name="path">The manifest path.</param>
    /// <returns>The manifest, or <see langword="null"/> when it is missing or unusable.</returns>
    internal static BakeManifest? TryRead(string path) {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path)) {
            return null;
        }

        try {
            byte[] bytes = File.ReadAllBytes(path);
            BakeManifest? manifest = JsonSerializer.Deserialize<BakeManifest>(bytes, _options);
            return IsStructurallyValid(manifest) ? manifest : null;
        } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or JsonException or NotSupportedException or ArgumentException) {
            return null;
        }
    }

    /// <summary>Tries to write a manifest atomically.</summary>
    /// <param name="path">The manifest path.</param>
    /// <param name="manifest">The manifest.</param>
    /// <returns><see langword="true"/> when the target now holds the manifest.</returns>
    internal static bool TryWrite(string path, BakeManifest manifest) {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(manifest);

        string? directory = Path.GetDirectoryName(path);
        string? temporary = null;
        try {
            if (!string.IsNullOrEmpty(directory)) {
                System.IO.Directory.CreateDirectory(directory);
            }

            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, _options);
            temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, overwrite: true);
            temporary = null;
            return true;
        } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException) {
            return false;
        } finally {
            if (temporary is not null) {
                try {
                    File.Delete(temporary);
                } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
                    // Best effort cleanup.
                }
            }
        }
    }

    private static bool IsStructurallyValid(BakeManifest? manifest) {
        if (manifest is null
            || manifest.Schema != BakeManifest.CURRENT_VERSION
            || string.IsNullOrWhiteSpace(manifest.ToolVersion)
            || string.IsNullOrWhiteSpace(manifest.ConfigurationHash)
            || string.IsNullOrWhiteSpace(manifest.Mode)
            || manifest.Packs is null) {
            return false;
        }

        var packPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (BakeManifestPack pack in manifest.Packs) {
            if (pack is null
                || string.IsNullOrWhiteSpace(pack.PackPath)
                || string.IsNullOrWhiteSpace(pack.Root)
                || string.IsNullOrWhiteSpace(pack.LayoutHash)
                || string.IsNullOrWhiteSpace(pack.PackHash)
                || pack.Sources is null
                || !packPaths.Add(pack.PackPath)) {
                return false;
            }

            var sourceIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (BakeManifestSource source in pack.Sources) {
                if (source is null
                    || string.IsNullOrWhiteSpace(source.Id)
                    || string.IsNullOrWhiteSpace(source.Kind)
                    || source.Inputs is null
                    || source.Outputs is null
                    || !sourceIds.Add(source.Id)) {
                    return false;
                }

                foreach (BakeManifestInput input in source.Inputs) {
                    if (input is null
                        || string.IsNullOrWhiteSpace(input.Path)
                        || string.IsNullOrWhiteSpace(input.Hash)) {
                        return false;
                    }
                }

                foreach (BakeManifestOutput output in source.Outputs) {
                    if (output is null
                        || string.IsNullOrWhiteSpace(output.Path)
                        || string.IsNullOrWhiteSpace(output.Hash)
                        || output.Alignment is not 1 and not 16 and not 64 and not 256) {
                        return false;
                    }
                }
            }
        }

        return true;
    }
}
