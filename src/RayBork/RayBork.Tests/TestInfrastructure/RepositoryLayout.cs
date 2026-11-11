namespace RayBork.Tests;

internal static class RepositoryLayout {
    private const string ROOT_MARKER_FILE = "AGENTS.md";
    private const string SOLUTION_RELATIVE_PATH = "src/RayBork/RayBork.slnx";

    private static readonly Lazy<string> _root = new(FindRoot);

    public static string Root => _root.Value;

    public static string PathOf(params string[] relativeSegments) {
        ArgumentNullException.ThrowIfNull(relativeSegments);
        if (relativeSegments.Length == 0) {
            return Root;
        }

        string[] segments = new string[relativeSegments.Length + 1];
        segments[0] = Root;
        relativeSegments.CopyTo(segments, 1);
        return Path.Combine(segments);
    }

    private static string FindRoot() {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null) {
            bool hasRootMarker = File.Exists(Path.Combine(directory.FullName, ROOT_MARKER_FILE));
            bool hasSolution = File.Exists(Path.Combine(directory.FullName, SOLUTION_RELATIVE_PATH));
            if (hasRootMarker && hasSolution) {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not find the repository root by walking up from '{AppContext.BaseDirectory}'. " +
            $"The root must contain both '{ROOT_MARKER_FILE}' and '{SOLUTION_RELATIVE_PATH}'.");
    }
}
