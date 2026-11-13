namespace RayBork.Internal;

/// <summary>
/// One link in the chain of nested asset loads that produced the current load.
/// </summary>
/// <remarks>
/// The catalog records the asset type and canonical path of every value load that can request another
/// asset. A loader that asks for the asset type and path it is currently loading would create an
/// endless chain, so the catalog rejects that request instead of queueing it forever. A chain is
/// immutable and is read only on the loader thread.
/// </remarks>
internal sealed class LoadChain {
    private readonly LoadChain? _parent;
    private readonly Type _assetType;
    private readonly string _canonicalPath;

    /// <summary>
    /// Initializes one link.
    /// </summary>
    /// <param name="assetType">The CLR asset type being loaded.</param>
    /// <param name="canonicalPath">The canonical path being loaded.</param>
    /// <param name="parent">The chain that requested this load, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    internal LoadChain(Type assetType, string canonicalPath, LoadChain? parent) {
        ArgumentNullException.ThrowIfNull(assetType);
        ArgumentNullException.ThrowIfNull(canonicalPath);

        _assetType = assetType;
        _canonicalPath = canonicalPath;
        _parent = parent;
    }

    /// <summary>
    /// Tests whether this chain or a parent already loads the asset type and canonical path.
    /// </summary>
    /// <param name="assetType">The CLR asset type to test.</param>
    /// <param name="canonicalPath">The canonical path to test.</param>
    /// <returns><see langword="true"/> when the load is already in the chain.</returns>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    internal bool Contains(Type assetType, string canonicalPath) {
        ArgumentNullException.ThrowIfNull(assetType);
        ArgumentNullException.ThrowIfNull(canonicalPath);

        for (LoadChain? link = this; link is not null; link = link._parent) {
            if (link._assetType == assetType
                && string.Equals(link._canonicalPath, canonicalPath, StringComparison.Ordinal)) {
                return true;
            }
        }

        return false;
    }
}
