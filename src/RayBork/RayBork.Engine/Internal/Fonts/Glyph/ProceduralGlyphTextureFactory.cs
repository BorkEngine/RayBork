namespace RayBork.Internal;

/// <summary>
/// The production <see cref="IGlyphTextureFactory"/>: it uploads one generated MSDF bitmap as a
/// procedural, linear, single-level RGBA8 texture through the catalog's procedural GPU bridge.
/// </summary>
/// <remarks>
/// <para>
/// Each glyph is one small texture sampled whole with <c>0..1</c> UVs, so there is no atlas and no
/// sampler trick. The texture is created with linear filtering and clamped wrapping and no mip chain:
/// the MSDF fragment shader derives its antialiasing band from screen-space derivatives, not from
/// mips.
/// </para>
/// <para>
/// The generation service calls <see cref="CreateAsync"/> on the <c>RayBork.Font</c> thread. The
/// returned task completes when the render thread finished the GPU copy through the app-lifetime
/// <see cref="ProceduralAssetService"/>, and the cancellation token is passed straight to
/// <see cref="TextureBuilder.BuildAsync"/> so stopping the service cancels an in-flight upload. The
/// returned texture is owned by the caller, which is the font's glyph cache.
/// </para>
/// </remarks>
internal sealed class ProceduralGlyphTextureFactory : IGlyphTextureFactory
{
    private readonly ProceduralAssetService _assets;

    /// <summary>
    /// Initializes the factory over the app-lifetime procedural asset service.
    /// </summary>
    /// <param name="assets">The procedural asset service owned by the asset catalog.</param>
    /// <exception cref="ArgumentNullException"><paramref name="assets"/> is <c>null</c>.</exception>
    internal ProceduralGlyphTextureFactory(ProceduralAssetService assets)
    {
        ArgumentNullException.ThrowIfNull(assets);

        _assets = assets;
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentException">The bitmap length does not match the texture size.</exception>
    /// <exception cref="ObjectDisposedException">The app has stopped.</exception>
    public async Task<Texture> CreateAsync(
        int width,
        int height,
        ReadOnlyMemory<byte> rgba,
        CancellationToken cancellationToken
    )
    {
        TextureBuilder builder = _assets.CreateTextureBuilder(
            width,
            height,
            TextureFormat.Rgba8Unorm,
            generateMips: false,
            sampler: new SamplerState(TextureFilter.Linear, TextureWrap.Clamp)
        );
        builder.SetPixels(rgba.Span);
        return await builder.BuildAsync(cancellationToken).ConfigureAwait(false);
    }
}
