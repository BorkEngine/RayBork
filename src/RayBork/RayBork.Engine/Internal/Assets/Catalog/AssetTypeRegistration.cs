namespace RayBork.Internal;

/// <summary>
/// One custom asset type registered with <see cref="RayBork.RayBorkAppBuilder.WithAssetType{T}"/>.
/// </summary>
/// <remarks>
/// The registration binds the registered CLR type to the pack entry type and the loader that produce
/// instances of it. The bake must register the same type independently; the runtime never scans
/// assemblies. Registrations are immutable, so they are safe to share across threads.
/// </remarks>
internal abstract class AssetTypeRegistration {
    /// <summary>
    /// Initializes a registration for one asset type.
    /// </summary>
    /// <param name="assetType">The registered CLR type.</param>
    /// <param name="entryType">The pack entry type the registration accepts.</param>
    /// <exception cref="ArgumentNullException"><paramref name="assetType"/> is <c>null</c>.</exception>
    private protected AssetTypeRegistration(Type assetType, AssetEntryType entryType) {
        ArgumentNullException.ThrowIfNull(assetType);

        AssetType = assetType;
        EntryType = entryType;
    }

    /// <summary>
    /// Gets the registered CLR type.
    /// </summary>
    internal Type AssetType { get; }

    /// <summary>
    /// Gets the pack entry type the registration accepts.
    /// </summary>
    internal AssetEntryType EntryType { get; }
}
