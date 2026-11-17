using RayBork.Internal;

namespace RayBork;

/// <summary>
/// One texture: an immutable GPU image with its size, format, and sampler state.
/// </summary>
/// <remarks>
/// <para>
/// A texture is either a packed texture loaded from a pack or one procedural texture built by a
/// <see cref="TextureBuilder"/>. A packed image is created from the KTX2 payload during the load; a
/// procedural image is created from the builder's raw uncompressed mip levels. Format selection,
/// sRGB, and sampler settings are fixed at bake or build time and cannot be changed at run time. The
/// GPU image keeps its bindless slot alive until every frame that references it completes.
/// </para>
/// <para>
/// The texture does not expose CPU pixels: a loaded texture never retained them, and a procedural
/// texture's pixels stay owned by its builder. <see cref="Path"/> and <see cref="IsDisposed"/> stay
/// readable after disposal. Every other member throws <see cref="ObjectDisposedException"/> once the
/// asset is disposed. A <see cref="RenderTexture"/>'s color and depth views are the exception to
/// immutability: resizing the target recreates the GPU image and updates the same view's
/// <see cref="Width"/> and <see cref="Height"/> in place. Format, sampler, path, and owner stay fixed,
/// and directly disposing the view still throws.
/// </para>
/// </remarks>
public sealed class Texture : IAsset
{
    private readonly ITextureOwner _owner;
    private readonly int _width;
    private readonly int _height;
    private readonly int _mipLevels;
    private readonly TextureFormat _format;
    private readonly bool _isSrgb;
    private readonly SamplerState _sampler;
    private readonly bool _isProcedural;
    private readonly bool _isRenderTargetView;

    /// <summary>
    /// Initializes a texture from its uploaded lifetime state and descriptor.
    /// </summary>
    /// <param name="asset">The shared lifetime state acquired by the catalog or the builder.</param>
    /// <param name="descriptor">The packed or synthetic texture descriptor.</param>
    /// <param name="isProcedural">Whether the texture was built by a <see cref="TextureBuilder"/>.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    internal Texture(
        EngineAsset asset,
        TextureAssetDescriptor descriptor,
        bool isProcedural = false
    )
        : this((ITextureOwner)asset, descriptor, isProcedural, isRenderTargetView: false) { }

    /// <summary>
    /// Initializes a texture over a non-asset lifetime.
    /// </summary>
    /// <param name="owner">The lifetime state that owns the GPU view.</param>
    /// <param name="descriptor">The synthetic texture descriptor.</param>
    /// <param name="isProcedural">Whether the texture was built by a <see cref="TextureBuilder"/>.</param>
    /// <param name="isRenderTargetView">
    /// Whether the texture is a render target's color or depth view, which the owning
    /// <see cref="RenderTexture"/> creates and disposes.
    /// </param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    internal Texture(
        ITextureOwner owner,
        TextureAssetDescriptor descriptor,
        bool isProcedural,
        bool isRenderTargetView
    )
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(descriptor);

        _owner = owner;
        _width = checked((int)descriptor.Width);
        _height = checked((int)descriptor.Height);
        _mipLevels = checked((int)descriptor.MipLevels);
        _format = descriptor.Format;
        _isSrgb = descriptor.IsSrgb;
        _sampler = new SamplerState(descriptor.Filter, descriptor.Wrap, descriptor.MaxAnisotropy);
        _isProcedural = isProcedural;
        _isRenderTargetView = isRenderTargetView;
    }

    /// <inheritdoc/>
    public string Path => _owner.Path;

    /// <summary>
    /// Gets whether the texture was built by a <see cref="TextureBuilder"/> instead of loaded from a
    /// pack. A procedural texture has a reserved synthetic <see cref="Path"/> under
    /// <c>/_procedural/texture/</c>.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The texture is disposed.</exception>
    public bool IsProcedural
    {
        get
        {
            ThrowIfDisposed();
            return _isProcedural;
        }
    }

    /// <summary>
    /// Gets the base-level width in pixels. A render target view reports the current target width, which
    /// changes in place when the owning <see cref="RenderTexture"/> is resized.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The texture is disposed.</exception>
    public int Width
    {
        get
        {
            ThrowIfDisposed();
            return _owner is RenderTextureViewLifetime view ? view.Width : _width;
        }
    }

    /// <summary>
    /// Gets the base-level height in pixels. A render target view reports the current target height, which
    /// changes in place when the owning <see cref="RenderTexture"/> is resized.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The texture is disposed.</exception>
    public int Height
    {
        get
        {
            ThrowIfDisposed();
            return _owner is RenderTextureViewLifetime view ? view.Height : _height;
        }
    }

    /// <summary>
    /// Gets the number of mip levels in the uploaded image.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The texture is disposed.</exception>
    public int MipLevels
    {
        get
        {
            ThrowIfDisposed();
            return _mipLevels;
        }
    }

    /// <summary>
    /// Gets the GPU format of the texture.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The texture is disposed.</exception>
    public TextureFormat Format
    {
        get
        {
            ThrowIfDisposed();
            return _format;
        }
    }

    /// <summary>
    /// Gets whether the samples are sRGB-encoded. Data textures such as normal and mask maps are
    /// linear and return <see langword="false"/>.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The texture is disposed.</exception>
    public bool IsSrgb
    {
        get
        {
            ThrowIfDisposed();
            return _isSrgb;
        }
    }

    /// <summary>
    /// Gets the sampler state of the texture.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The texture is disposed.</exception>
    public SamplerState Sampler
    {
        get
        {
            ThrowIfDisposed();
            return _sampler;
        }
    }

    /// <inheritdoc/>
    public bool IsDisposed => _owner.IsDisposed;

    /// <summary>
    /// Gets the retention target the draw recorder keeps alive for the frame that references this
    /// texture, or the render target lifetime when the texture is a render target view.
    /// </summary>
    internal IAssetRetentionTarget RetentionTarget => _owner;

    /// <summary>
    /// Gets the engine-asset lifetime. Only an asset texture has one; a render target view is owned by
    /// its <see cref="RenderTexture"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The texture is a render target view.</exception>
    internal EngineAsset Lifetime =>
        _owner.EngineAsset
        ?? throw new InvalidOperationException(
            "A render target view has no engine-asset lifetime; its RenderTexture owns it."
        );

    /// <summary>
    /// Gets the bindless binding resource the renderer samples, or <see langword="null"/> when the GPU
    /// resource does not exist yet.
    /// </summary>
    internal ITextureBindingResource? BindingResource => _owner.BindingResource;

    /// <summary>
    /// Gets whether this texture is a render target's color or depth view.
    /// </summary>
    internal bool IsRenderTargetView => _isRenderTargetView;

    /// <summary>
    /// Gets the render target id when this texture is a render target view, or <c>0</c> otherwise.
    /// </summary>
    internal long RenderTargetId => _owner is RenderTextureViewLifetime view ? view.TargetId : 0;

    /// <summary>
    /// Retires the view and releases the owner reference the render texture held on it when the
    /// owning render texture is disposed. A non-view texture is unaffected.
    /// </summary>
    internal void RetireRenderTargetView()
    {
        if (_owner is RenderTextureViewLifetime view)
        {
            view.Retire();
            view.ReleaseOwnerReference();
        }
    }

    /// <summary>
    /// Adds one owner reference to the texture's lifetime.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The texture is disposed.</exception>
    internal void AddOwnerReference() => _owner.AddOwnerReference();

    /// <summary>
    /// Releases one owner reference from the texture's lifetime.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The owner count is already zero.</exception>
    internal void ReleaseOwnerReference() => _owner.ReleaseOwnerReference();

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">
    /// The texture is a render target's color or depth view; dispose the owning <see cref="RenderTexture"/>
    /// instead.
    /// </exception>
    /// <exception cref="ObjectDisposedException">Every reference was already released.</exception>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1065",
        Justification = "Disposing a render target view is a documented usage error; throwing a clear InvalidOperationException tells the caller to dispose the owning RenderTexture instead of silently leaking the target."
    )]
    public void Dispose()
    {
        if (_isRenderTargetView)
        {
            throw new InvalidOperationException(
                "A render target view is owned by its RenderTexture; dispose the RenderTexture "
                    + "instead of the view."
            );
        }

        ObjectDisposedException.ThrowIf(_owner.IsDisposed, this);

        _owner.ReleaseOwnerReference();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_owner.IsDisposed, this);
}
