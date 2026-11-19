namespace RayBork.Internal;

/// <summary>
/// One custom asset type registered with <see cref="RayBork.RayBorkAppBuilder.WithAssetType{T}"/>.
/// </summary>
/// <remarks>
/// A registration binds the CLR type to the pack entry type and the loader that creates it; it is
/// immutable and safe to share across threads. The bake registers the same type independently.
/// </remarks>
internal abstract class AssetTypeRegistration
{
    /// <summary>Initializes a registration for one asset type.</summary>
    private protected AssetTypeRegistration(Type assetType, AssetEntryType entryType)
    {
        ArgumentNullException.ThrowIfNull(assetType);

        AssetType = assetType;
        EntryType = entryType;
    }

    /// <summary>Gets the registered CLR type.</summary>
    internal Type AssetType { get; }

    /// <summary>Gets the pack entry type the registration accepts.</summary>
    internal AssetEntryType EntryType { get; }
}
