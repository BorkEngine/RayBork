using RayBork.Bake.Baking;
using RayBork.Bake.Paths;

namespace RayBork.Bake.Incremental;

/// <summary>Resolves pack outputs under an invocation output root.</summary>
internal static class BakeOutputLayout {
    /// <summary>Resolves an output root to an absolute path.</summary>
    /// <param name="outputRoot">The root path.</param>
    /// <returns>The absolute root.</returns>
    /// <exception cref="ArgumentException">The path is empty or invalid.</exception>
    internal static string ResolveRoot(string outputRoot) {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        return Path.GetFullPath(outputRoot);
    }

    /// <summary>Resolves one pack path under the output root.</summary>
    /// <param name="outputRoot">The absolute output root.</param>
    /// <param name="packPath">The safe relative pack path.</param>
    /// <returns>The absolute pack target.</returns>
    /// <exception cref="ArgumentException">The path escapes the output root.</exception>
    internal static string ResolvePackPath(string outputRoot, string packPath) {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(packPath);

        if (!BakePath.TryNormalizeRelative(packPath, out string normalized, out string? reason)) {
            throw new ArgumentException($"Invalid pack path '{packPath}': {reason}.", nameof(packPath));
        }

        if (!BakePath.TryResolveUnderRoot(outputRoot, normalized, out string fullPath)) {
            throw new ArgumentException($"The pack path '{packPath}' escapes the output root.", nameof(packPath));
        }

        return fullPath;
    }

    /// <summary>Resolves every configured pack output.</summary>
    /// <param name="project">The project.</param>
    /// <param name="outputRoot">The absolute output root.</param>
    /// <returns>The pack outputs in configuration order.</returns>
    internal static IReadOnlyList<BakePackOutput> Resolve(BakeProject project, string outputRoot) {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);

        var outputs = new List<BakePackOutput>(project.Packs.Count);
        foreach (BakePack pack in project.Packs) {
            outputs.Add(new BakePackOutput(pack, ResolvePackPath(outputRoot, pack.PackPath)));
        }

        return outputs;
    }
}

/// <summary>One configured pack and its absolute output path.</summary>
/// <param name="Pack">The configured pack.</param>
/// <param name="Path">The absolute pack path.</param>
internal sealed record BakePackOutput(BakePack Pack, string Path);
