using RayBork.Internal;

namespace RayBork;

/// <summary>
/// Loads one registered custom asset type from a packed entry.
/// </summary>
/// <remarks>
/// Implementations are stateless and thread-safe: the catalog invokes <see cref="LoadAsync"/> on its
/// loader thread for every load, and concurrent loads may share one loader instance. A loader must
/// not mutate the payload array supplied by the <see cref="AssetLoadContext"/>.
/// </remarks>
/// <typeparam name="T">The custom asset type produced by the loader.</typeparam>
internal interface IAssetLoader<T> {
    /// <summary>
    /// Gets the pack entry type that this loader accepts.
    /// </summary>
    AssetEntryType EntryType { get; }

    /// <summary>
    /// Loads one asset from the supplied context.
    /// </summary>
    /// <param name="context">The canonical path, cancellation token, and packed payload for the entry.</param>
    /// <returns>A new caller-owned instance.</returns>
    Task<T> LoadAsync(AssetLoadContext context);
}
