namespace RayBork.Internal;

/// <summary>A custom asset type registration backed by an <see cref="IAssetLoader{T}"/>.</summary>
/// <remarks>
/// The registration is immutable and its loader is stateless and thread-safe, so the catalog may call
/// it from the loader thread. The failure description is inserted when a loader fails with an
/// exception that is not an asset exception.
/// </remarks>
internal sealed class LoaderAssetTypeRegistration<T> : AssetTypeRegistration
{
    /// <summary>Initializes a registration from a loader.</summary>
    internal LoaderAssetTypeRegistration(
        IAssetLoader<T> loader,
        string failureDescription = "could not be loaded"
    )
        : base(typeof(T), GetEntryType(loader))
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureDescription);

        Loader = loader;
        FailureDescription = failureDescription;
    }

    /// <summary>Gets the loader that creates instances of the registered type.</summary>
    internal IAssetLoader<T> Loader { get; }

    /// <summary>Gets the message fragment inserted when a loader failure is wrapped.</summary>
    internal string FailureDescription { get; }

    private static AssetEntryType GetEntryType(IAssetLoader<T> loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        return loader.EntryType;
    }
}
