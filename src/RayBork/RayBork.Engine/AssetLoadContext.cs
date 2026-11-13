using RayBork.Internal;

namespace RayBork;

/// <summary>
/// The inputs for one custom asset load: the canonical path, the combined catalog and caller
/// cancellation token, and the already-read packed payload.
/// </summary>
/// <remarks>
/// The context is immutable and thread-safe. <see cref="LoadPayloadAsync"/> returns the same array on
/// every call; the loader must treat the payload as read-only and must not mutate it.
/// </remarks>
internal sealed class AssetLoadContext {
    private readonly string _canonicalPath;
    private readonly byte[] _payload;

    /// <summary>
    /// Initializes a context for one load.
    /// </summary>
    /// <param name="canonicalPath">The canonical path, without a leading slash.</param>
    /// <param name="cancellationToken">
    /// The token canceled when either the catalog stops or the caller cancels.
    /// </param>
    /// <param name="payload">The complete payload bytes read from the pack entry.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    internal AssetLoadContext(string canonicalPath, CancellationToken cancellationToken, byte[] payload) {
        ArgumentNullException.ThrowIfNull(canonicalPath);
        ArgumentNullException.ThrowIfNull(payload);

        _canonicalPath = canonicalPath;
        CancellationToken = cancellationToken;
        _payload = payload;
    }

    /// <summary>
    /// Gets the asset path in public form, with a leading slash.
    /// </summary>
    internal string Path => AssetPath.ToPublic(_canonicalPath);

    /// <summary>
    /// Gets the token canceled when the catalog stops or the caller cancels.
    /// </summary>
    internal CancellationToken CancellationToken { get; }

    /// <summary>
    /// Gets the complete packed payload for the entry.
    /// </summary>
    /// <returns>A completed task holding the already-read payload array.</returns>
    internal Task<byte[]> LoadPayloadAsync() => Task.FromResult(_payload);
}
