using System.Diagnostics.CodeAnalysis;

namespace RayBork.Internal;

/// <summary>The shared lifetime and frame-retention state behind one loaded engine asset.</summary>
/// <remarks>
/// Owner references come from successful loads and frame holds from the draw recorder; the asset
/// disposes once both reach zero. A CPU-only asset such as <see cref="Font"/> owns no GPU resource.
/// </remarks>
internal sealed class EngineAsset : ITextureOwner
{
    private readonly Lock _gate = new();
    private readonly GpuAssetResource? _resource;
    private readonly Action<GpuAssetResource> _queueDestruction;
    private readonly Action _onOwnersReleased;
    private readonly Action? _release;
    private int _ownerCount;
    private int _frameHoldCount;
    private bool _disposed;
    private bool _destructionQueued;

    /// <summary>Initializes the lifetime state for one asset.</summary>
    /// <param name="publicPath">The canonical path with a leading slash.</param>
    /// <param name="resource">The GPU resource, or <see langword="null"/> when CPU-only.</param>
    /// <param name="queueDestruction">
    /// Queues the resource for destruction on the render thread, exactly once at teardown.
    /// </param>
    /// <param name="onOwnersReleased">Removes the asset from the cache; must not throw.</param>
    /// <param name="release">
    /// The CPU-only teardown to run exactly once, or <see langword="null"/>.
    /// </param>
    internal EngineAsset(
        string publicPath,
        GpuAssetResource? resource,
        Action<GpuAssetResource> queueDestruction,
        Action onOwnersReleased,
        Action? release = null
    )
    {
        ArgumentNullException.ThrowIfNull(publicPath);
        ArgumentNullException.ThrowIfNull(queueDestruction);
        ArgumentNullException.ThrowIfNull(onOwnersReleased);

        Path = AssetPath.ToPublic(AssetPath.Normalize(publicPath));
        _resource = resource;
        _queueDestruction = queueDestruction;
        _onOwnersReleased = onOwnersReleased;
        _release = release;
    }

    /// <summary>Gets the canonical public path, readable after disposal.</summary>
    internal string Path { get; }

    /// <summary>Gets the GPU resource, or <see langword="null"/> for a CPU-only asset.</summary>
    internal GpuAssetResource? Resource => _resource;

    /// <inheritdoc/>
    EngineAsset? ITextureOwner.EngineAsset => this;

    /// <inheritdoc/>
    ITextureBindingResource? ITextureOwner.BindingResource => _resource as ITextureBindingResource;

    /// <inheritdoc/>
    string ITextureOwner.Path => Path;

    /// <inheritdoc/>
    bool ITextureOwner.IsDisposed => IsDisposed;

    /// <inheritdoc/>
    void ITextureOwner.AddOwnerReference() => AddOwnerReference();

    /// <inheritdoc/>
    void ITextureOwner.ReleaseOwnerReference() => ReleaseOwnerReference();

    /// <summary>Gets whether the asset is disposed.</summary>
    internal bool IsDisposed
    {
        get
        {
            lock (_gate)
            {
                return _disposed;
            }
        }
    }

    /// <summary>Adds one owner reference for a successful load.</summary>
    /// <exception cref="ObjectDisposedException">The asset is already disposed.</exception>
    internal void AddOwnerReference()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            _ownerCount++;
        }
    }

    /// <summary>Releases one owner reference and disposes the asset when it reaches zero.</summary>
    /// <exception cref="ObjectDisposedException">The owner count is already zero.</exception>
    internal void ReleaseOwnerReference()
    {
        bool notifyOwnersReleased;
        bool destroy;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed || _ownerCount <= 0, this);

            _ownerCount--;
            notifyOwnersReleased = _ownerCount == 0;
            if (notifyOwnersReleased)
            {
                _disposed = true;
            }

            destroy = notifyOwnersReleased && TryClaimDestructionLocked();
        }

        if (notifyOwnersReleased)
        {
            NotifyOwnersReleased();
        }

        if (destroy)
        {
            Destroy();
        }
    }

    /// <summary>Disposes the asset without a matching owner release, for shutdown.</summary>
    internal void ForceDispose()
    {
        bool destroy;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _ownerCount = 0;
            destroy = TryClaimDestructionLocked();
        }

        NotifyOwnersReleased();

        if (destroy)
        {
            Destroy();
        }
    }

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">The asset is already disposed.</exception>
    public void AddFrameHold()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            _frameHoldCount++;
        }
    }

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">
    /// The frame-hold count is already zero.
    /// </exception>
    public void ReleaseFrameHold()
    {
        bool destroy;
        lock (_gate)
        {
            if (_frameHoldCount <= 0)
            {
                throw new InvalidOperationException(
                    "The engine asset frame-hold count is already zero; every frame hold is released exactly once."
                );
            }

            _frameHoldCount--;
            destroy = TryClaimDestructionLocked();
        }

        if (destroy)
        {
            Destroy();
        }
    }

    private bool TryClaimDestructionLocked()
    {
        if (!_disposed || _ownerCount != 0 || _frameHoldCount != 0 || _destructionQueued)
        {
            return false;
        }

        _destructionQueued = true;
        return true;
    }

    private void Destroy()
    {
        try
        {
            _release?.Invoke();
        }
        finally
        {
            if (_resource is not null)
            {
                _queueDestruction(_resource);
            }
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "The catalog cache-removal callback runs during teardown, when the entry may already be gone; it must not mask the GPU destruction that follows."
    )]
    private void NotifyOwnersReleased()
    {
        try
        {
            _onOwnersReleased();
        }
        catch (Exception)
        {
            // The callback only removes a catalog cache entry. Shutdown may already have cleared it,
            // and a failure here must not leak the GPU resource.
        }
    }
}
