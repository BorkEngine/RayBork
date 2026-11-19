namespace RayBork.Internal;

/// <summary>
/// The owner and frame-hold state of one render target view: the resolved color or depth image.
/// </summary>
/// <remarks>
/// Disposal retires the view, so later access throws <see cref="ObjectDisposedException"/>. Frame
/// holds forward to the target, and a resize updates the dimensions before the request completes.
/// </remarks>
internal sealed class RenderTextureViewLifetime : ITextureOwner
{
    private readonly Lock _gate = new();
    private readonly RenderTextureTargetLifetime _target;
    private ITextureBindingResource? _resource;
    private int _ownerCount = 1;
    private int _width;
    private int _height;
    private bool _retired;

    /// <summary>Initializes the view with one owner reference and an optional resource.</summary>
    internal RenderTextureViewLifetime(
        RenderTextureTargetLifetime target,
        ITextureBindingResource? resource,
        int width,
        int height
    )
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        _target = target;
        _resource = resource;
        _width = width;
        _height = height;
    }

    /// <summary>Gets the owning target id.</summary>
    internal long TargetId => _target.TargetId;

    /// <summary>Gets the current view width in pixels.</summary>
    internal int Width => Volatile.Read(ref _width);

    /// <summary>Gets the current view height in pixels.</summary>
    internal int Height => Volatile.Read(ref _height);

    /// <summary>Replaces the view dimensions after a resize; render-thread only.</summary>
    internal void SetDimensions(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Volatile.Write(ref _width, width);
        Volatile.Write(ref _height, height);
    }

    /// <inheritdoc/>
    public string Path => string.Empty;

    /// <inheritdoc/>
    public bool IsDisposed
    {
        get
        {
            lock (_gate)
            {
                return _retired || _ownerCount <= 0;
            }
        }
    }

    /// <inheritdoc/>
    public EngineAsset? EngineAsset => null;

    /// <inheritdoc/>
    public ITextureBindingResource? BindingResource => Volatile.Read(ref _resource);

    /// <summary>Replaces the view resource after device recreation; render-thread only.</summary>
    internal void SetBindingResource(ITextureBindingResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        Volatile.Write(ref _resource, resource);
    }

    /// <summary>Retires the view when its owner is disposed; idempotent.</summary>
    internal void Retire()
    {
        lock (_gate)
        {
            _retired = true;
        }
    }

    /// <inheritdoc/>
    public void AddOwnerReference()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(IsDisposedLocked(), this);

            _ownerCount++;
        }
    }

    /// <inheritdoc/>
    public void ReleaseOwnerReference()
    {
        lock (_gate)
        {
            if (_ownerCount <= 0)
            {
                throw new ObjectDisposedException(
                    nameof(RenderTextureViewLifetime),
                    "The render target view owner count is already zero."
                );
            }

            // A material that still holds this view must be able to release its reference after the
            // owning RenderTexture was disposed; retirement makes the view unusable without trapping
            // the delayed internal release.
            _ownerCount--;
        }
    }

    /// <inheritdoc/>
    public void AddFrameHold() => _target.AddFrameHold();

    /// <inheritdoc/>
    public void ReleaseFrameHold() => _target.ReleaseFrameHold();

    private bool IsDisposedLocked() => _retired || _ownerCount <= 0;
}
