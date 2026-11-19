using RayBork.Internal;

namespace RayBork;

/// <summary>The inputs for one custom asset load: path, token, payload, and catalog.</summary>
/// <remarks>
/// The context is immutable and valid only during the <see cref="IAssetLoader{T}.LoadAsync"/> call
/// that received it; its payload is shared and must not be mutated. Nested loads are queued on the
/// same <c>RayBork.Asset</c> loader thread, so a loader must not block waiting for one.
/// </remarks>
public sealed class AssetLoadContext
{
    private readonly AssetCatalog _catalog;
    private readonly string _canonicalPath;
    private readonly LoadChain? _chain;
    private readonly byte[] _payload;

    /// <summary>Initializes a context for one load.</summary>
    internal AssetLoadContext(
        AssetCatalog catalog,
        string canonicalPath,
        byte[] payload,
        LoadChain? chain,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(canonicalPath);
        ArgumentNullException.ThrowIfNull(payload);

        _catalog = catalog;
        _canonicalPath = canonicalPath;
        CancellationToken = cancellationToken;
        _payload = payload;
        _chain = chain;
    }

    /// <summary>Gets the asset path in public form, with a leading slash.</summary>
    public string Path => AssetPath.ToPublic(_canonicalPath);

    /// <summary>Gets the canonical path, without a leading slash.</summary>
    internal string CanonicalPath => _canonicalPath;

    /// <summary>Gets the token canceled when the catalog stops or the caller cancels.</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>Requests another asset as part of this load; do not block on it.</summary>
    /// <typeparam name="T">The requested asset type.</typeparam>
    /// <param name="path">The asset path, validated before the lookup.</param>
    /// <param name="cancellationToken">Overrides the context token when cancelable.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is invalid.</exception>
    /// <exception cref="AssetNotFoundException">The path is not in any mounted pack.</exception>
    /// <exception cref="AssetLoadException">The payload is corrupt or the load fails.</exception>
    public Task<T> LoadAsync<T>(string path, CancellationToken cancellationToken = default)
    {
        CancellationToken token = cancellationToken.CanBeCanceled
            ? cancellationToken
            : CancellationToken;
        return _catalog.LoadCore<T>(path, _chain, token);
    }

    /// <summary>Requests another entry's payload as part of this load; do not block on it.</summary>
    /// <param name="path">The asset path, validated before the lookup.</param>
    /// <param name="cancellationToken">Overrides the context token when cancelable.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is invalid.</exception>
    /// <exception cref="AssetNotFoundException">The path is not in any mounted pack.</exception>
    /// <exception cref="AssetLoadException">The payload is corrupt or too large.</exception>
    public Task<byte[]> LoadDataAsync(string path, CancellationToken cancellationToken = default)
    {
        CancellationToken token = cancellationToken.CanBeCanceled
            ? cancellationToken
            : CancellationToken;
        return _catalog.LoadData(path, token);
    }

    /// <summary>Tests whether another asset path is present in the mounted packs.</summary>
    /// <returns><see langword="true"/> when the canonical path resolves to a pack entry.</returns>
    public bool Exists(string path) => _catalog.Exists(path);

    /// <summary>Gets the already-read packed payload for the entry.</summary>
    /// <returns>The shared payload array; do not mutate it.</returns>
    public Task<byte[]> LoadPayloadAsync() => Task.FromResult(_payload);
}
