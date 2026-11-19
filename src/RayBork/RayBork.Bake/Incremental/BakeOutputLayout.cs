using RayBork.Bake.Baking;
using RayBork.Bake.Paths;

namespace RayBork.Bake.Incremental;

/// <summary>Resolves pack outputs under an invocation output root.</summary>
internal static class BakeOutputLayout
{
    /// <summary>Resolves an output root to an absolute path.</summary>
    internal static string ResolveRoot(string outputRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        return Path.GetFullPath(outputRoot);
    }

    /// <summary>Resolves one pack under the output root, rejecting escapes.</summary>
    internal static string ResolvePackPath(string outputRoot, string packPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(packPath);

        if (!BakePath.TryNormalizeRelative(packPath, out string normalized, out string? reason))
        {
            throw new ArgumentException(
                $"Invalid pack path '{packPath}': {reason}.",
                nameof(packPath)
            );
        }

        if (!BakePath.TryResolveUnderRoot(outputRoot, normalized, out string fullPath))
        {
            throw new ArgumentException(
                $"The pack path '{packPath}' escapes the output root.",
                nameof(packPath)
            );
        }

        return fullPath;
    }

    /// <summary>Resolves every configured pack output.</summary>
    internal static IReadOnlyList<BakePackOutput> Resolve(BakeProject project, string outputRoot)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);

        var outputs = new List<BakePackOutput>(project.Packs.Count);
        foreach (BakePack pack in project.Packs)
        {
            outputs.Add(new BakePackOutput(pack, ResolvePackPath(outputRoot, pack.PackPath)));
        }

        return outputs;
    }
}

/// <summary>One configured pack and its absolute output path.</summary>
internal sealed record BakePackOutput(BakePack Pack, string Path);
