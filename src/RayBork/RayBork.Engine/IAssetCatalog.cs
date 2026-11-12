namespace RayBork;

/// <summary>
/// The app's read-only view of the mounted asset packs.
/// </summary>
/// <remarks>
/// <para>
/// All members are thread-safe and may be called from any thread, including a user background
/// thread. Engine asset loads are coalesced by canonical path and asset type; custom data loads are
/// independent and return a fresh instance per call.
/// </para>
/// <para>
/// Asset paths are case-insensitive on input and canonicalized to lowercase. An optional leading
/// <c>/</c> is accepted, forward slashes are required, and the extension is omitted. The canonical
/// grammar is described in the API reference.
/// </para>
/// <para>
/// v1 has no runtime pack mount or unmount API; packs are fixed when the app is built. After the app
/// is disposed, every member throws <see cref="ObjectDisposedException"/>.
/// </para>
/// </remarks>
public interface IAssetCatalog {
    /// <summary>
    /// Loads an engine asset or a registered custom data type.
    /// </summary>
    /// <typeparam name="T">
    /// An engine asset type (<c>Mesh</c>, <c>Texture</c>, <c>Shader</c>) or a type registered with
    /// <c>RayBorkAppBuilder.WithAssetType&lt;T&gt;()</c>.
    /// </typeparam>
    /// <param name="path">The asset path. It is normalized and validated before the lookup.</param>
    /// <param name="cancellationToken">Cancels the caller's wait. The shared load may continue for other callers.</param>
    /// <returns>
    /// The shared engine asset, or a fresh caller-owned instance for registered custom data.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is not a valid asset path.</exception>
    /// <exception cref="AssetNotFoundException">The path is not in any mounted pack.</exception>
    /// <exception cref="AssetTypeMismatchException">The path exists with a different asset type.</exception>
    /// <exception cref="AssetPackException">A mounted pack is corrupt or cannot be read.</exception>
    /// <exception cref="AssetLoadException">The payload is corrupt or a GPU upload failed.</exception>
    /// <exception cref="ObjectDisposedException">The catalog or an engine asset was disposed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="NotSupportedException">
    /// <typeparamref name="T"/> is neither an engine asset type nor registered with
    /// <c>RayBorkAppBuilder.WithAssetType&lt;T&gt;()</c>.
    /// </exception>
    Task<T> Load<T>(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the decompressed payload bytes of an asset, whether it is typed or raw.
    /// </summary>
    /// <param name="path">The asset path. It is normalized and validated before the lookup.</param>
    /// <param name="cancellationToken">Cancels the caller's wait.</param>
    /// <returns>A fresh byte array for every call.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is not a valid asset path.</exception>
    /// <exception cref="AssetNotFoundException">The path is not in any mounted pack.</exception>
    /// <exception cref="AssetPackException">A mounted pack is corrupt or cannot be read.</exception>
    /// <exception cref="AssetLoadException">The payload is corrupt or exceeds the supported size.</exception>
    /// <exception cref="ObjectDisposedException">The catalog is disposed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<byte[]> LoadData(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests whether a mounted pack contains an asset path.
    /// </summary>
    /// <param name="path">The asset path. It is normalized and validated before the lookup.</param>
    /// <returns><see langword="true"/> when a visible entry has the exact canonical path.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is not a valid asset path.</exception>
    /// <exception cref="ObjectDisposedException">The catalog is disposed.</exception>
    bool Exists(string path);
}
