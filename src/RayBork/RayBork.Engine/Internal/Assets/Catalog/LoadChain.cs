namespace RayBork.Internal;

/// <summary>One link in the chain of nested asset loads that produced the current load.</summary>
/// <remarks>
/// A chain is immutable and read only on the loader thread. It rejects a request for the asset type
/// and path already in the chain, which would otherwise queue forever.
/// </remarks>
internal sealed class LoadChain
{
    private readonly LoadChain? _parent;
    private readonly Type _assetType;
    private readonly string _canonicalPath;

    /// <summary>Initializes one link.</summary>
    internal LoadChain(Type assetType, string canonicalPath, LoadChain? parent)
    {
        ArgumentNullException.ThrowIfNull(assetType);
        ArgumentNullException.ThrowIfNull(canonicalPath);

        _assetType = assetType;
        _canonicalPath = canonicalPath;
        _parent = parent;
    }

    /// <summary>Tests whether this chain or a parent already loads the asset type and path.</summary>
    internal bool Contains(Type assetType, string canonicalPath)
    {
        ArgumentNullException.ThrowIfNull(assetType);
        ArgumentNullException.ThrowIfNull(canonicalPath);

        for (LoadChain? link = this; link is not null; link = link._parent)
        {
            if (
                link._assetType == assetType
                && string.Equals(link._canonicalPath, canonicalPath, StringComparison.Ordinal)
            )
            {
                return true;
            }
        }

        return false;
    }
}
