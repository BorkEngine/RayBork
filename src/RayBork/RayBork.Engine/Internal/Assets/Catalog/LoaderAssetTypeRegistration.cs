namespace RayBork.Internal;

/// <summary>
/// A custom asset type registration backed by an <see cref="IAssetLoader{T}"/>.
/// </summary>
/// <remarks>
/// The registration is immutable. The loader it holds is stateless and thread-safe, so the catalog
/// can call it from its loader thread for every load of the type.
/// </remarks>
/// <typeparam name="T">The registered custom asset type.</typeparam>
internal sealed class LoaderAssetTypeRegistration<T> : AssetTypeRegistration {
    /// <summary>
    /// Initializes a registration from a loader.
    /// </summary>
    /// <param name="loader">The loader that creates instances of <typeparamref name="T"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="loader"/> is <c>null</c>.</exception>
    internal LoaderAssetTypeRegistration(IAssetLoader<T> loader)
        : base(typeof(T), GetEntryType(loader)) {
        Loader = loader;
    }

    /// <summary>
    /// Gets the loader that creates instances of <typeparamref name="T"/>.
    /// </summary>
    internal IAssetLoader<T> Loader { get; }

    private static AssetEntryType GetEntryType(IAssetLoader<T> loader) {
        ArgumentNullException.ThrowIfNull(loader);
        return loader.EntryType;
    }
}
