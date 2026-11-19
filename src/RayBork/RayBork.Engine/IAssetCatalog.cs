namespace RayBork;

/// <summary>The app's read-only view of the mounted asset packs.</summary>
/// <remarks>
/// All members are thread-safe. Engine assets are cached by canonical path and type while custom
/// loads return a fresh instance; paths are case-insensitive, slash-delimited, and extension-free,
/// and every member throws <see cref="ObjectDisposedException"/> after disposal.
/// </remarks>
public interface IAssetCatalog
{
    /// <summary>Loads a built-in engine asset or a registered custom asset.</summary>
    /// <typeparam name="T">The requested asset type, built in or registered.</typeparam>
    /// <param name="path">The asset path, validated before the lookup.</param>
    /// <param name="cancellationToken">Cancels the caller's wait; a shared load may continue.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is invalid.</exception>
    /// <exception cref="AssetNotFoundException">The path is not in any mounted pack.</exception>
    /// <exception cref="AssetTypeMismatchException">The path has a different asset type.</exception>
    /// <exception cref="AssetLoadException">The payload is corrupt or an upload failed.</exception>
    /// <exception cref="NotSupportedException"><typeparamref name="T"/> is not registered.</exception>
    Task<T> Load<T>(string path, CancellationToken cancellationToken = default);

    /// <summary>Loads the decompressed payload bytes of a typed or raw asset.</summary>
    /// <returns>A fresh byte array for every call.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is invalid.</exception>
    /// <exception cref="AssetNotFoundException">The path is not in any mounted pack.</exception>
    /// <exception cref="AssetLoadException">The payload is corrupt or too large.</exception>
    Task<byte[]> LoadData(string path, CancellationToken cancellationToken = default);

    /// <summary>Tests whether a mounted pack contains an asset path.</summary>
    /// <returns><see langword="true"/> when a visible entry has the exact canonical path.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is invalid.</exception>
    bool Exists(string path);
}
