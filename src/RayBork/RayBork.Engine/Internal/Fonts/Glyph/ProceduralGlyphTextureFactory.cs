namespace RayBork.Internal;

/// <summary>Uploads generated MSDF glyphs through the procedural GPU bridge.</summary>
/// <remarks>
/// One linear, clamped, single-level RGBA8 texture per glyph, sampled whole with <c>0..1</c>
/// UVs; the MSDF shader antialiases from derivatives, not mips. The caller owns the texture,
/// and the stop token flows to <see cref="TextureBuilder.BuildAsync"/>.
/// </remarks>
internal sealed class ProceduralGlyphTextureFactory : IGlyphTextureFactory
{
    private readonly ProceduralAssetService _assets;

    /// <summary>Initializes the factory over the procedural asset service.</summary>
    /// <param name="assets">The app-lifetime procedural asset service.</param>
    /// <exception cref="ArgumentNullException">The service is <c>null</c>.</exception>
    internal ProceduralGlyphTextureFactory(ProceduralAssetService assets)
    {
        ArgumentNullException.ThrowIfNull(assets);

        _assets = assets;
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentException">The bitmap length does not match the size.</exception>
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
