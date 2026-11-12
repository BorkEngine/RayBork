namespace RayBork.Internal;

/// <summary>
/// The shared lifetime and frame-retention state behind one loaded engine asset.
/// </summary>
/// <remarks>
/// <para>
/// The catalog owns one instance per cached <c>(canonical path, engine asset type)</c> key and hands
/// it to the public <see cref="Mesh"/>, <see cref="Texture"/>, or <see cref="Shader"/> wrapper. Owner
/// references are acquired by the catalog for every successful load and released by the public
/// <c>Dispose</c>. Frame holds are acquired by the M4 draw recorder and released by the frame ring
/// after the consuming frame completes.
/// </para>
/// <para>
/// The owner count, frame-hold count, disposed flag, and destruction decision are guarded by one
/// internal lock, so a cache-hit retain can never interleave with the transition to zero owners. An
/// owner reference or a frame hold added after disposal throws <see cref="ObjectDisposedException"/>.
/// The asset becomes disposed exactly when the owner count reaches zero; GPU destruction is claimed
/// exactly once and only after both the owner count and the frame-hold count are zero. The catalog
/// callback and the bridge destruction request run outside the lock, so a waiter or a public
/// <c>Dispose</c> never blocks the render thread on asset bookkeeping.
/// </para>
/// </remarks>
internal sealed class EngineAsset : IAssetRetentionTarget {
    private readonly Lock _gate = new();
    private readonly GpuAssetResource _resource;
    private readonly Action<GpuAssetResource> _queueDestruction;
    private readonly Action _onOwnersReleased;
    private int _ownerCount;
    private int _frameHoldCount;
    private bool _disposed;
    private bool _destructionQueued;

    /// <summary>
    /// Initializes the lifetime state for one uploaded asset.
    /// </summary>
    /// <param name="publicPath">The canonical path with a leading slash.</param>
    /// <param name="resource">The GPU resource created for the asset.</param>
    /// <param name="queueDestruction">
    /// Queues the resource for destruction on the render thread. It is called exactly once, after the
    /// last owner reference and frame hold are gone.
    /// </param>
    /// <param name="onOwnersReleased">
    /// Removes the asset from the catalog cache when the last owner reference is released. It is
    /// called exactly once and must not throw.
    /// </param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="publicPath"/> is not canonical.</exception>
    internal EngineAsset(
        string publicPath,
        GpuAssetResource resource,
        Action<GpuAssetResource> queueDestruction,
        Action onOwnersReleased) {
        ArgumentNullException.ThrowIfNull(publicPath);
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(queueDestruction);
        ArgumentNullException.ThrowIfNull(onOwnersReleased);

        Path = AssetPath.ToPublic(AssetPath.Normalize(publicPath));
        _resource = resource;
        _queueDestruction = queueDestruction;
        _onOwnersReleased = onOwnersReleased;
    }

    /// <summary>
    /// Gets the canonical public path with a leading slash. It stays readable after disposal.
    /// </summary>
    internal string Path { get; }

    /// <summary>
    /// Gets the GPU resource behind the asset. It is valid until the destruction request is queued.
    /// </summary>
    internal GpuAssetResource Resource => _resource;

    /// <summary>
    /// Gets whether the asset was disposed. It stays readable after disposal.
    /// </summary>
    internal bool IsDisposed {
        get {
            lock (_gate) {
                return _disposed;
            }
        }
    }

    /// <summary>
    /// Adds one owner reference for a successful load.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The asset is already disposed.</exception>
    internal void AddOwnerReference() {
        lock (_gate) {
            if (_disposed) {
                throw new ObjectDisposedException(nameof(EngineAsset));
            }

            _ownerCount++;
        }
    }

    /// <summary>
    /// Releases one owner reference. The transition to zero disposes the asset and removes it from the
    /// catalog cache; GPU destruction waits for every frame hold.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The owner count is already zero.</exception>
    internal void ReleaseOwnerReference() {
        bool notifyOwnersReleased;
        bool queueDestruction;
        lock (_gate) {
            if (_disposed || _ownerCount <= 0) {
                throw new ObjectDisposedException(nameof(EngineAsset));
            }

            _ownerCount--;
            notifyOwnersReleased = _ownerCount == 0;
            if (notifyOwnersReleased) {
                _disposed = true;
            }

            queueDestruction = notifyOwnersReleased && TryClaimDestructionLocked();
        }

        if (notifyOwnersReleased) {
            NotifyOwnersReleased();
        }

        if (queueDestruction) {
            _queueDestruction(_resource);
        }
    }

    /// <summary>
    /// Disposes the asset without requiring a matching owner release. The catalog uses it during
    /// shutdown so a game that leaked a reference cannot keep a GPU resource alive past the app.
    /// </summary>
    internal void ForceDispose() {
        bool queueDestruction;
        lock (_gate) {
            if (_disposed) {
                return;
            }

            _disposed = true;
            _ownerCount = 0;
            queueDestruction = TryClaimDestructionLocked();
        }

        NotifyOwnersReleased();

        if (queueDestruction) {
            _queueDestruction(_resource);
        }
    }

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">The asset is already disposed.</exception>
    public void AddFrameHold() {
        lock (_gate) {
            if (_disposed) {
                throw new ObjectDisposedException(nameof(EngineAsset));
            }

            _frameHoldCount++;
        }
    }

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">The frame-hold count is already zero.</exception>
    public void ReleaseFrameHold() {
        bool queueDestruction;
        lock (_gate) {
            if (_frameHoldCount <= 0) {
                throw new InvalidOperationException(
                    "The engine asset frame-hold count is already zero; every frame hold is released exactly once.");
            }

            _frameHoldCount--;
            queueDestruction = TryClaimDestructionLocked();
        }

        if (queueDestruction) {
            _queueDestruction(_resource);
        }
    }

    private bool TryClaimDestructionLocked() {
        if (!_disposed || _ownerCount != 0 || _frameHoldCount != 0 || _destructionQueued) {
            return false;
        }

        _destructionQueued = true;
        return true;
    }

    private void NotifyOwnersReleased() {
        try {
            _onOwnersReleased();
        } catch (Exception) {
            // The callback only removes a catalog cache entry. Shutdown may already have cleared it,
            // and a failure here must not leak the GPU resource.
        }
    }
}
