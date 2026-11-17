namespace RayBork.Internal;

/// <summary>
/// The lifetime of one render target view: the public <see cref="Texture"/> wrapper's owner and
/// retention state for the resolved color or depth image.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="RenderTexture"/> owns exactly one reference. A <see cref="Shader"/> binding acquires an
/// owner reference exactly like it does for an asset texture, and the recorded frame adds a frame hold,
/// which this lifetime forwards to the target so the target images stay alive. The view itself has an
/// empty <see cref="Path"/> because it is not a pack asset.
/// </para>
/// <para>
/// The owning <see cref="RenderTexture"/> retires the view when it is disposed, so every later access
/// throws <see cref="ObjectDisposedException"/> even when a shader still holds a reference. Disposing the
/// public <see cref="Texture"/> directly is invalid and is rejected by <see cref="Texture.Dispose"/>.
/// This type never creates or destroys GPU objects: the target resource owns the image view and the
/// bindless slot. It does own the view's current dimensions: a resize updates them together with the
/// target description on the render thread before it completes the request, and the public
/// <see cref="Texture.Width"/>/<see cref="Texture.Height"/> read them, so the view and the target never
/// report a split state.
/// </para>
/// </remarks>
internal sealed class RenderTextureViewLifetime : ITextureOwner {
    private readonly Lock _gate = new();
    private readonly RenderTextureTargetLifetime _target;
    private ITextureBindingResource? _resource;
    private int _ownerCount = 1;
    private int _width;
    private int _height;
    private bool _retired;

    /// <summary>
    /// Initializes the view lifetime with one owner reference, owned by the <see cref="RenderTexture"/>.
    /// </summary>
    /// <param name="target">The target lifetime that owns the image view and the bindless slot.</param>
    /// <param name="resource">The bindless view resource, or <see langword="null"/> before allocation.</param>
    /// <param name="width">The initial view width in pixels.</param>
    /// <param name="height">The initial view height in pixels.</param>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is not positive.</exception>
    internal RenderTextureViewLifetime(
        RenderTextureTargetLifetime target,
        ITextureBindingResource? resource,
        int width,
        int height) {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        _target = target;
        _resource = resource;
        _width = width;
        _height = height;
    }

    /// <summary>Gets the target id; <c>0</c> for a texture that is not a render target view.</summary>
    internal long TargetId => _target.TargetId;

    /// <summary>Gets the current view width in pixels.</summary>
    internal int Width => Volatile.Read(ref _width);

    /// <summary>Gets the current view height in pixels.</summary>
    internal int Height => Volatile.Read(ref _height);

    /// <summary>
    /// Replaces the view dimensions after the renderer recreated the target's image at a new size.
    /// Render-thread only; the resize completes its request only after this update, so a tick thread that
    /// observes the completed resize sees the new dimensions.
    /// </summary>
    /// <param name="width">The new width in pixels.</param>
    /// <param name="height">The new height in pixels.</param>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is not positive.</exception>
    internal void SetDimensions(int width, int height) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Volatile.Write(ref _width, width);
        Volatile.Write(ref _height, height);
    }

    /// <inheritdoc/>
    public string Path => string.Empty;

    /// <inheritdoc/>
    public bool IsDisposed {
        get {
            lock (_gate) {
                return _retired || _ownerCount <= 0;
            }
        }
    }

    /// <inheritdoc/>
    public EngineAsset? EngineAsset => null;

    /// <inheritdoc/>
    public ITextureBindingResource? BindingResource => Volatile.Read(ref _resource);

    /// <summary>
    /// Replaces the bindless view resource after a device recreation. Render-thread only.
    /// </summary>
    /// <param name="resource">The recreated view resource.</param>
    internal void SetBindingResource(ITextureBindingResource resource) {
        ArgumentNullException.ThrowIfNull(resource);
        Volatile.Write(ref _resource, resource);
    }

    /// <summary>
    /// Retires the view when the owning <see cref="RenderTexture"/> is disposed. Every later access
    /// throws <see cref="ObjectDisposedException"/>. Idempotent.
    /// </summary>
    internal void Retire() {
        lock (_gate) {
            _retired = true;
        }
    }

    /// <inheritdoc/>
    public void AddOwnerReference() {
        lock (_gate) {
            ObjectDisposedException.ThrowIf(IsDisposedLocked(), this);

            _ownerCount++;
        }
    }

    /// <inheritdoc/>
    public void ReleaseOwnerReference() {
        lock (_gate) {
            if (_ownerCount <= 0) {
                throw new ObjectDisposedException(
                    nameof(RenderTextureViewLifetime),
                    "The render target view owner count is already zero.");
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
