namespace RayBork.Internal;

/// <summary>
/// A custom asset type registration backed by an <see cref="IAssetLoader{T}"/>.
/// </summary>
/// <remarks>
/// The registration is immutable. The loader it holds is stateless and thread-safe, so the catalog
/// can call it from its loader thread for every load of the type. The failure description is the
/// message fragment the catalog inserts when the loader fails with an exception that is not an asset
/// exception; custom data overrides it so a MessagePack failure keeps the custom-data wording.
/// </remarks>
/// <typeparam name="T">The registered custom asset type.</typeparam>
internal sealed class LoaderAssetTypeRegistration<T> : AssetTypeRegistration {
    /// <summary>
    /// Initializes a registration from a loader.
    /// </summary>
    /// <param name="loader">The loader that creates instances of <typeparamref name="T"/>.</param>
    /// <param name="failureDescription">
    /// The description inserted into a wrapped loader failure. The default is
    /// <c>"could not be loaded"</c>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="loader"/> or <paramref name="failureDescription"/> is <c>null</c>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="failureDescription"/> is empty or whitespace.</exception>
    internal LoaderAssetTypeRegistration(
        IAssetLoader<T> loader,
        string failureDescription = "could not be loaded")
        : base(typeof(T), GetEntryType(loader)) {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureDescription);

        Loader = loader;
        FailureDescription = failureDescription;
    }

    /// <summary>
    /// Gets the loader that creates instances of <typeparamref name="T"/>.
    /// </summary>
    internal IAssetLoader<T> Loader { get; }

    /// <summary>
    /// Gets the description the catalog inserts when it wraps a loader failure that is not an asset
    /// exception.
    /// </summary>
    internal string FailureDescription { get; }

    private static AssetEntryType GetEntryType(IAssetLoader<T> loader) {
        ArgumentNullException.ThrowIfNull(loader);
        return loader.EntryType;
    }
}
