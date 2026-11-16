namespace RayBork.Internal;

/// <summary>
/// The lifetime of one render target's sampled color view: the public <see cref="Texture"/> wrapper's
/// owner and retention state.
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
/// bindless slot.
/// </para>
/// </remarks>
internal sealed class RenderTextureViewLifetime : ITextureOwner {
    private readonly Lock _gate = new();
    private readonly RenderTextureTargetLifetime _target;
    private ITextureBindingResource? _resource;
    private int _ownerCount = 1;
    private bool _retired;

    /// <summary>
    /// Initializes the view lifetime with one owner reference, owned by the <see cref="RenderTexture"/>.
    /// </summary>
    /// <param name="target">The target lifetime that owns the image view and the bindless slot.</param>
    /// <param name="resource">The bindless view resource, or <see langword="null"/> before allocation.</param>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is <c>null</c>.</exception>
    internal RenderTextureViewLifetime(
        RenderTextureTargetLifetime target,
        ITextureBindingResource? resource) {
        ArgumentNullException.ThrowIfNull(target);
        _target = target;
        _resource = resource;
    }


    /// <summary>Gets the target id; <c>0</c> for a texture that is not a render target view.</summary>
    internal long TargetId => _target.TargetId;

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
                    "The render target color view owner count is already zero.");
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
