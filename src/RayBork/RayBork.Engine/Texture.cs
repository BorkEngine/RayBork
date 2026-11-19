using RayBork.Internal;

namespace RayBork;

/// <summary>
/// One immutable GPU image with its size, format, and sampler state.
/// </summary>
/// <remarks>
/// A texture is either loaded from a pack or built by a <see cref="TextureBuilder"/>, with format,
/// sRGB, and sampler fixed at creation; after disposal <see cref="Path"/> and
/// <see cref="IsDisposed"/> stay readable while other members throw
/// <see cref="ObjectDisposedException"/>. Render target views resize in place and cannot be
/// disposed directly.
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

    /// <summary>Initializes a texture from its uploaded lifetime state and descriptor.</summary>
    internal Texture(
        EngineAsset asset,
        TextureAssetDescriptor descriptor,
        bool isProcedural = false
    )
        : this((ITextureOwner)asset, descriptor, isProcedural, isRenderTargetView: false) { }

    /// <summary>Initializes a render target view over a non-asset lifetime.</summary>
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

    /// <summary>Gets whether the texture was built by a <see cref="TextureBuilder"/>.</summary>
    public bool IsProcedural
    {
        get
        {
            ThrowIfDisposed();
            return _isProcedural;
        }
    }

    /// <summary>
    /// Gets the base-level width in pixels; a render target view reports the live target width.
    /// </summary>
    public int Width
    {
        get
        {
            ThrowIfDisposed();
            return _owner is RenderTextureViewLifetime view ? view.Width : _width;
        }
    }

    /// <summary>
    /// Gets the base-level height in pixels; a render target view reports the live target height.
    /// </summary>
    public int Height
    {
        get
        {
            ThrowIfDisposed();
            return _owner is RenderTextureViewLifetime view ? view.Height : _height;
        }
    }

    /// <summary>Gets the number of mip levels.</summary>
    public int MipLevels
    {
        get
        {
            ThrowIfDisposed();
            return _mipLevels;
        }
    }

    /// <summary>Gets the GPU format.</summary>
    public TextureFormat Format
    {
        get
        {
            ThrowIfDisposed();
            return _format;
        }
    }

    /// <summary>Gets whether the samples are sRGB-encoded; data textures are linear.</summary>
    public bool IsSrgb
    {
        get
        {
            ThrowIfDisposed();
            return _isSrgb;
        }
    }

    /// <summary>Gets the sampler state.</summary>
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
    /// Gets the retention target the draw recorder keeps alive for the frames that reference it.
    /// </summary>
    internal IAssetRetentionTarget RetentionTarget => _owner;

    /// <summary>Gets the engine-asset lifetime; a render target view has none.</summary>
    /// <exception cref="InvalidOperationException">The texture is a render target view.</exception>
    internal EngineAsset Lifetime =>
        _owner.EngineAsset
        ?? throw new InvalidOperationException(
            "A render target view has no engine-asset lifetime; its RenderTexture owns it."
        );

    /// <summary>
    /// Gets the bindless binding resource, or <see langword="null"/> when the GPU resource does
    /// not exist.
    /// </summary>
    internal ITextureBindingResource? BindingResource => _owner.BindingResource;

    /// <summary>Gets whether this texture is a render target view.</summary>
    internal bool IsRenderTargetView => _isRenderTargetView;

    /// <summary>Gets the render target id for a view, or <c>0</c> otherwise.</summary>
    internal long RenderTargetId => _owner is RenderTextureViewLifetime view ? view.TargetId : 0;

    /// <summary>
    /// Retires a render target view and releases the owner reference the render texture held on it.
    /// </summary>
    internal void RetireRenderTargetView()
    {
        if (_owner is RenderTextureViewLifetime view)
        {
            view.Retire();
            view.ReleaseOwnerReference();
        }
    }

    /// <summary>Adds one owner reference to the texture's lifetime.</summary>
    internal void AddOwnerReference() => _owner.AddOwnerReference();

    /// <summary>Releases one owner reference; the count must not already be zero.</summary>
    internal void ReleaseOwnerReference() => _owner.ReleaseOwnerReference();

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">
    /// The texture is a render target view; dispose the owning <see cref="RenderTexture"/> instead.
    /// </exception>
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
