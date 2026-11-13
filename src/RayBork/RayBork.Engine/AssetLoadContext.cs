using RayBork.Internal;

namespace RayBork;

/// <summary>
/// The inputs for one custom asset load: the canonical path, the combined catalog and caller
/// cancellation token, the already-read packed payload, and the catalog that can serve nested loads.
/// </summary>
/// <remarks>
/// <para>
/// The context is immutable and thread-safe. <see cref="LoadPayloadAsync"/> returns the same array on
/// every call; the loader must treat the payload as read-only and must not mutate it.
/// </para>
/// <para>
/// The context is valid only during the <see cref="IAssetLoader{T}.LoadAsync"/> call that received
/// it. A loader that needs another asset calls <see cref="LoadAsync{T}"/>, <see cref="LoadDataAsync"/>,
/// or <see cref="Exists"/>. A nested load is queued on the same <c>RayBork.Asset</c> loader thread as
/// the current load, so the loader must not block waiting for it. The load chain rejects a loader that
/// requests the asset type and path it is currently loading instead of queueing the request forever.
/// </para>
/// </remarks>
public sealed class AssetLoadContext {
    private readonly AssetCatalog _catalog;
    private readonly string _canonicalPath;
    private readonly LoadChain? _chain;
    private readonly byte[] _payload;

    /// <summary>
    /// Initializes a context for one load.
    /// </summary>
    /// <param name="catalog">The catalog that dispatched the load.</param>
    /// <param name="canonicalPath">The canonical path, without a leading slash.</param>
    /// <param name="cancellationToken">
    /// The token canceled when either the catalog stops or the caller cancels.
    /// </param>
    /// <param name="payload">The complete payload bytes read from the pack entry.</param>
    /// <param name="chain">
    /// The chain of loads that requested this load, or <see langword="null"/> for a top-level load.
    /// </param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    internal AssetLoadContext(
        AssetCatalog catalog,
        string canonicalPath,
        CancellationToken cancellationToken,
        byte[] payload,
        LoadChain? chain) {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(canonicalPath);
        ArgumentNullException.ThrowIfNull(payload);

        _catalog = catalog;
        _canonicalPath = canonicalPath;
        CancellationToken = cancellationToken;
        _payload = payload;
        _chain = chain;
    }

    /// <summary>
    /// Gets the asset path in public form, with a leading slash.
    /// </summary>
    public string Path => AssetPath.ToPublic(_canonicalPath);

    /// <summary>
    /// Gets the canonical path, without a leading slash.
    /// </summary>
    internal string CanonicalPath => _canonicalPath;

    /// <summary>
    /// Gets the token canceled when the catalog stops or the caller cancels.
    /// </summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// Requests another asset as part of the current load.
    /// </summary>
    /// <typeparam name="T">The requested asset type.</typeparam>
    /// <param name="path">The asset path.</param>
    /// <param name="cancellationToken">
    /// An optional token for this nested load; the context token is used when it cannot be canceled.
    /// </param>
    /// <returns>The nested load. The loader must not block waiting for it.</returns>
    public Task<T> LoadAsync<T>(string path, CancellationToken cancellationToken = default) {
        CancellationToken token = cancellationToken.CanBeCanceled ? cancellationToken : CancellationToken;
        return _catalog.LoadCore<T>(path, token, _chain);
    }

    /// <summary>
    /// Requests the packed payload of another entry as part of the current load.
    /// </summary>
    /// <param name="path">The asset path.</param>
    /// <param name="cancellationToken">
    /// An optional token for this nested load; the context token is used when it cannot be canceled.
    /// </param>
    /// <returns>The nested payload load. The loader must not block waiting for it.</returns>
    public Task<byte[]> LoadDataAsync(string path, CancellationToken cancellationToken = default) {
        CancellationToken token = cancellationToken.CanBeCanceled ? cancellationToken : CancellationToken;
        return _catalog.LoadData(path, token);
    }

    /// <summary>
    /// Tests whether another asset path is present in the mounted packs.
    /// </summary>
    /// <param name="path">The asset path.</param>
    /// <returns><see langword="true"/> when the canonical path resolves to a pack entry.</returns>
    public bool Exists(string path) => _catalog.Exists(path);

    /// <summary>
    /// Gets the complete packed payload for the entry.
    /// </summary>
    /// <returns>A completed task holding the already-read payload array.</returns>
    public Task<byte[]> LoadPayloadAsync() => Task.FromResult(_payload);
}
