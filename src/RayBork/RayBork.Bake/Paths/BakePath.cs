namespace RayBork.Bake.Paths;

/// <summary>
/// Path helpers shared by discovery, configuration validation, and diagnostics.
/// </summary>
internal static class BakePath {
    private const string TOML_SUFFIX = ".toml";

    /// <summary>Tests whether a path is inside a root on a directory-separator boundary.</summary>
    /// <param name="root">The root directory.</param>
    /// <param name="path">The candidate path.</param>
    /// <returns><see langword="true"/> when the candidate is the root or is contained by it.</returns>
    internal static bool IsUnderRoot(string root, string path) {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string fullPath = Path.GetFullPath(path);
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (string.Equals(fullRoot, fullPath, comparison)) {
            return true;
        }

        string prefix = fullRoot + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(prefix, comparison);
    }

    /// <summary>Converts an absolute path to a project-relative display path with <c>/</c> separators.</summary>
    /// <param name="projectRoot">The absolute project root.</param>
    /// <param name="fullPath">The absolute file path.</param>
    /// <returns>The relative path when under the project, otherwise the absolute path.</returns>
    internal static string ForDiagnostic(string projectRoot, string fullPath) {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

        if (!IsUnderRoot(projectRoot, fullPath)) {
            return ToSlashes(fullPath);
        }

        string relative = Path.GetRelativePath(Path.GetFullPath(projectRoot), Path.GetFullPath(fullPath));
        return ToSlashes(relative);
    }

    /// <summary>Normalizes separators to <c>/</c>.</summary>
    /// <param name="path">The path.</param>
    /// <returns>The normalized path.</returns>
    internal static string ToSlashes(string path)
        => path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

    /// <summary>
    /// Validates a relative configuration path and returns it with <c>/</c> separators.
    /// </summary>
    /// <param name="value">The configured path.</param>
    /// <param name="normalized">The normalized relative path when valid.</param>
    /// <param name="reason">The failure reason when invalid.</param>
    /// <returns><see langword="true"/> when the path is relative and traversal-safe.</returns>
    internal static bool TryNormalizeRelative(
        string value,
        out string normalized,
        out string? reason) {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(value)) {
            reason = "the path is empty";
            return false;
        }

        if (Path.IsPathRooted(value)) {
            reason = "the path is rooted";
            return false;
        }

        string candidate = value.Replace('\\', '/');
        foreach (string segment in candidate.Split('/')) {
            if (segment.Length == 0) {
                reason = "the path contains an empty segment";
                return false;
            }

            if (segment is "." or "..") {
                reason = $"the path contains a '{segment}' segment";
                return false;
            }
        }

        normalized = candidate;
        reason = null;
        return true;
    }

    /// <summary>
    /// Resolves a relative path under a root and verifies containment.
    /// </summary>
    /// <param name="root">The absolute root.</param>
    /// <param name="relativePath">The safe relative path.</param>
    /// <param name="fullPath">The resolved absolute path.</param>
    /// <returns><see langword="true"/> when the path is contained by the root.</returns>
    internal static bool TryResolveUnderRoot(string root, string relativePath, out string fullPath) {
        fullPath = string.Empty;
        try {
            string candidate = Path.GetFullPath(
                Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsUnderRoot(root, candidate)) {
                return false;
            }

            fullPath = candidate;
            return true;
        } catch (Exception exception) when (exception is ArgumentException or NotSupportedException
            or PathTooLongException) {
            return false;
        }
    }

    /// <summary>
    /// Maps a source-relative path to a canonical asset path by removing one matched extension.
    /// </summary>
    /// <param name="relativeSourcePath">The pack-root-relative source path.</param>
    /// <param name="matchedExtension">The normalized extension without a leading dot, or an empty string.</param>
    /// <param name="assetPath">The canonical base path when successful.</param>
    /// <param name="reason">The failure reason when unsuccessful.</param>
    /// <returns><see langword="true"/> when a canonical path was produced.</returns>
    internal static bool TryToCanonicalBase(
        string relativeSourcePath,
        string matchedExtension,
        out string assetPath,
        out string? reason) {
        assetPath = string.Empty;

        if (string.IsNullOrWhiteSpace(relativeSourcePath)
            || Path.IsPathRooted(relativeSourcePath)) {
            reason = "the source path is empty or rooted";
            return false;
        }

        string normalized = relativeSourcePath.Replace('\\', '/').ToLowerInvariant();
        string withoutSuffix = normalized;
        if (matchedExtension.Length > 0) {
            string suffix = "." + matchedExtension.ToLowerInvariant();
            if (!normalized.EndsWith(suffix, StringComparison.Ordinal)) {
                reason = $"the path does not end with the matched extension '{suffix}'";
                return false;
            }

            withoutSuffix = normalized[..^suffix.Length];
        }

        if (!RayBork.Internal.AssetPath.TryValidateCanonical(withoutSuffix, out reason)) {
            return false;
        }

        assetPath = withoutSuffix;
        return true;
    }

    /// <summary>Gets the final extension of a file name without the dot, or an empty string.</summary>
    /// <param name="relativePath">The relative file path.</param>
    /// <returns>The lowercase extension, or an empty string.</returns>
    internal static string GetFinalExtension(string relativePath) {
        int slash = relativePath.LastIndexOf('/');
        int dot = relativePath.LastIndexOf('.');
        if (dot <= slash || dot == relativePath.Length - 1) {
            return string.Empty;
        }

        return relativePath[(dot + 1)..].ToLowerInvariant();
    }

    /// <summary>Gets the pack-relative path with its final extension removed.</summary>
    /// <param name="relativePath">The relative file path.</param>
    /// <returns>The path without its final extension.</returns>
    internal static string RemoveFinalExtension(string relativePath) {
        int slash = relativePath.LastIndexOf('/');
        int dot = relativePath.LastIndexOf('.');
        return dot <= slash ? relativePath : relativePath[..dot];
    }

    /// <summary>Gets the pack-relative path with one matched extension removed.</summary>
    /// <param name="relativePath">The relative file path.</param>
    /// <param name="matchedExtension">The matched extension, which may be compound.</param>
    /// <returns>The path without the matched extension.</returns>
    internal static string RemoveMatchedExtension(string relativePath, string matchedExtension) {
        if (matchedExtension.Length == 0) {
            return relativePath;
        }

        string suffix = "." + matchedExtension;
        return relativePath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? relativePath[..^suffix.Length]
            : RemoveFinalExtension(relativePath);
    }

    /// <summary>Tests whether a file name ends in the TOML suffix, case-insensitively.</summary>
    /// <param name="relativePath">The relative file path.</param>
    /// <returns><see langword="true"/> for a <c>.toml</c> path.</returns>
    internal static bool IsToml(string relativePath)
        => relativePath.EndsWith(TOML_SUFFIX, StringComparison.OrdinalIgnoreCase);

    /// <summary>Enumerates regular files under a root in ordinal relative-path order.</summary>
    /// <param name="root">The absolute pack root.</param>
    /// <returns>The absolute file paths, sorted ordinal by relative path.</returns>
    internal static IReadOnlyList<string> EnumerateFilesSorted(string root) {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (!Directory.Exists(root)) {
            return [];
        }

        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(root));

        while (pending.Count > 0) {
            string directory = pending.Pop();
            string[] entries;
            try {
                entries = Directory.GetFileSystemEntries(directory);
            } catch (Exception exception) when (IsEnumerationFailure(exception)) {
                continue;
            }

            foreach (string entry in entries) {
                FileAttributes attributes;
                try {
                    attributes = File.GetAttributes(entry);
                } catch (Exception exception) when (IsEnumerationFailure(exception)) {
                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0) {
                    continue;
                }

                if ((attributes & FileAttributes.Directory) != 0) {
                    pending.Push(entry);
                } else {
                    files.Add(entry);
                }
            }
        }

        files.Sort((left, right) => string.CompareOrdinal(
            Path.GetRelativePath(root, left),
            Path.GetRelativePath(root, right)));
        return files;
    }

    private static bool IsEnumerationFailure(Exception exception)
        => exception is IOException or UnauthorizedAccessException or NotSupportedException
            or ArgumentException;
}
