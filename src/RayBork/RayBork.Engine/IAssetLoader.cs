namespace RayBork;

/// <summary>Loads one registered custom asset type from a packed entry.</summary>
/// <remarks>
/// The catalog calls <see cref="LoadAsync"/> on the <c>RayBork.Asset</c> loader thread for every
/// load of the registered type and never caches the result, so each call returns a fresh
/// caller-owned value. Loaders must be thread-safe, must not block or call SDL, Vulkan, or
/// tick-thread APIs, and must hand off or release the assets they acquire.
/// </remarks>
/// <typeparam name="T">The custom asset type produced by the loader.</typeparam>
public interface IAssetLoader<T>
{
    /// <summary>Gets the pack entry type this loader accepts; a mismatch fails before load.</summary>
    AssetEntryType EntryType { get; }

    /// <summary>
    /// Loads one asset from the context; a <see langword="null"/> result is a load failure.
    /// </summary>
    Task<T> LoadAsync(AssetLoadContext context);
}
