namespace RayBork.Bake.Baking;

/// <summary>One configured pack after path resolution.</summary>
internal sealed class BakePack {
    /// <summary>Initializes a resolved pack.</summary>
    /// <param name="index">The configuration order index.</param>
    /// <param name="root">The absolute asset root.</param>
    /// <param name="relativeRoot">The project-relative asset root with <c>/</c> separators.</param>
    /// <param name="packPath">The output-root-relative pack path with <c>/</c> separators.</param>
    internal BakePack(int index, string root, string relativeRoot, string packPath) {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(packPath);

        Index = index;
        Root = root;
        RelativeRoot = relativeRoot;
        PackPath = packPath;
    }

    /// <summary>Gets the configuration order index; zero is the main pack.</summary>
    internal int Index { get; }

    /// <summary>Gets the absolute asset root.</summary>
    internal string Root { get; }

    /// <summary>Gets the project-relative asset root with <c>/</c> separators.</summary>
    internal string RelativeRoot { get; }

    /// <summary>Gets the output-root-relative pack path with <c>/</c> separators.</summary>
    internal string PackPath { get; }

    /// <summary>Gets a value indicating whether this is the main pack.</summary>
    internal bool IsMain => Index == 0;
}
