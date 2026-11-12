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
/// asset is disposed.
/// </para>
/// </remarks>
public sealed class Texture : IAsset {
    private readonly EngineAsset _asset;
    private readonly int _width;
    private readonly int _height;
    private readonly int _mipLevels;
    private readonly TextureFormat _format;
    private readonly bool _isSrgb;
    private readonly SamplerState _sampler;
    private readonly bool _isProcedural;

    /// <summary>
    /// Initializes a texture from its uploaded lifetime state and descriptor.
    /// </summary>
    /// <param name="asset">The shared lifetime state acquired by the catalog or the builder.</param>
    /// <param name="descriptor">The packed or synthetic texture descriptor.</param>
    /// <param name="isProcedural">Whether the texture was built by a <see cref="TextureBuilder"/>.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    internal Texture(EngineAsset asset, TextureAssetDescriptor descriptor, bool isProcedural = false) {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(descriptor);

        _asset = asset;
        _width = checked((int)descriptor.Width);
        _height = checked((int)descriptor.Height);
        _mipLevels = checked((int)descriptor.MipLevels);
        _format = descriptor.Format;
        _isSrgb = descriptor.IsSrgb;
        _sampler = new SamplerState(descriptor.Filter, descriptor.Wrap, descriptor.MaxAnisotropy);
        _isProcedural = isProcedural;
    }

    /// <inheritdoc/>
    public string Path => _asset.Path;

    /// <summary>
    /// Gets whether the texture was built by a <see cref="TextureBuilder"/> instead of loaded from a
    /// pack. A procedural texture has a reserved synthetic <see cref="Path"/> under
    /// <c>/_procedural/texture/</c>.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The texture is disposed.</exception>
    public bool IsProcedural {
        get {
            ThrowIfDisposed();
            return _isProcedural;
        }
    }

    /// <summary>
    /// Gets the base-level width in pixels.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The texture is disposed.</exception>
    public int Width {
        get {
            ThrowIfDisposed();
            return _width;
        }
    }

    /// <summary>
    /// Gets the base-level height in pixels.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The texture is disposed.</exception>
    public int Height {
        get {
            ThrowIfDisposed();
            return _height;
        }
    }

    /// <summary>
    /// Gets the number of mip levels in the uploaded image.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The texture is disposed.</exception>
    public int MipLevels {
        get {
            ThrowIfDisposed();
            return _mipLevels;
        }
    }

    /// <summary>
    /// Gets the GPU format of the texture.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The texture is disposed.</exception>
    public TextureFormat Format {
        get {
            ThrowIfDisposed();
            return _format;
        }
    }

    /// <summary>
    /// Gets whether the samples are sRGB-encoded. Data textures such as normal and mask maps are
    /// linear and return <see langword="false"/>.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The texture is disposed.</exception>
    public bool IsSrgb {
        get {
            ThrowIfDisposed();
            return _isSrgb;
        }
    }

    /// <summary>
    /// Gets the sampler state of the texture.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The texture is disposed.</exception>
    public SamplerState Sampler {
        get {
            ThrowIfDisposed();
            return _sampler;
        }
    }

    /// <inheritdoc/>
    public bool IsDisposed => _asset.IsDisposed;

    /// <summary>
    /// Gets the internal retention state the M4 draw recorder will retain per frame. The catalog or the
    /// builder owns the asset and exposes the lifetime only inside the engine assembly.
    /// </summary>
    internal EngineAsset Lifetime => _asset;

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">Every reference was already released.</exception>
    public void Dispose() {
        if (_asset.IsDisposed) {
            throw new ObjectDisposedException(nameof(Texture));
        }

        _asset.ReleaseOwnerReference();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_asset.IsDisposed, this);
}
