using System.Text;
using RayBork.Internal;
using RayBork.Tests.Assets.Catalog;
using RayBork.Tests.Fonts.Glyph;

namespace RayBork.Tests.Rendering;

/// <summary>Owns a camera frame host, the built-in primitive assets, and the font test harness.</summary>
/// <remarks>
/// Assets come from the real built-in pack; fonts are real <see cref="Font"/> instances over synthetic
/// faces, disposed before the glyph harness stops.
/// </remarks>
internal sealed class TextDrawTestHarness : IDisposable
{
    private const string FONT_PATH = "fonts/test-text";

    private readonly CameraTestHost _host;
    private readonly CatalogTestHarness _catalog;
    private readonly PrimitiveAssets _primitives;
    private readonly GlyphPipelineTestHarness _glyphs;
    private readonly List<Font> _fonts = [];
    private bool _disposed;

    private TextDrawTestHarness(
        CameraTestHost host,
        CatalogTestHarness catalog,
        PrimitiveAssets primitives,
        GlyphPipelineTestHarness glyphs
    )
    {
        _host = host;
        _catalog = catalog;
        _primitives = primitives;
        _glyphs = glyphs;
        // The app attaches the primitives before IGame.Setup; the text draws read the MSDF material
        // and the engine quad through the service.
        _host.Service.AttachPrimitives(primitives);
    }

    /// <summary>Gets the frame host whose service records the draws.</summary>
    internal CameraTestHost Host => _host;

    /// <summary>Gets the loaded engine primitive assets.</summary>
    internal PrimitiveAssets Primitives => _primitives;

    /// <summary>Gets the fake glyph texture factory the fonts use.</summary>
    internal FakeGlyphTextureFactory Factory => _glyphs.Factory;

    /// <summary>Creates the frame host and loads the engine primitive assets.</summary>
    /// <returns>The initialized harness.</returns>
    internal static async Task<TextDrawTestHarness> CreateAsync()
    {
        CameraTestHost host = new();
        CatalogTestHarness? catalog = null;
        PrimitiveAssets? primitives = null;
        GlyphPipelineTestHarness? glyphs = null;
        try
        {
            catalog = new CatalogTestHarness(builtInPack: BuiltInAssetPack.Bytes.ToArray());
            primitives = await PrimitiveAssets.LoadAsync(catalog.Catalog);
            glyphs = new GlyphPipelineTestHarness();
            return new TextDrawTestHarness(host, catalog, primitives, glyphs);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            glyphs?.Dispose();
            primitives?.Dispose();
            catalog?.Dispose();
            host.Dispose();
            throw;
        }
    }

    /// <summary>Creates a real font over one synthetic face and registers it for disposal.</summary>
    /// <param name="face">The parsed face.</param>
    /// <returns>The font, owned by the harness.</returns>
    internal Font CreateFont(SfntFace face)
    {
        ArgumentNullException.ThrowIfNull(face);
        FontGlyphPipeline pipeline = _glyphs.CreatePipeline(face);
        EngineAsset asset = new(
            FONT_PATH,
            resource: null,
            static _ => { },
            static () => { },
            pipeline.Dispose
        );
        asset.AddOwnerReference();
        Font font = new(asset, face, pipeline);
        _fonts.Add(font);
        return font;
    }

    /// <summary>Requests every glyph of the text and waits until each request is terminal.</summary>
    /// <param name="font">The font to generate glyphs for.</param>
    /// <param name="text">The text whose glyphs the test needs.</param>
    /// <param name="frameIndex">The frame index to begin the font's cache clock at.</param>
    internal static async Task SettleGlyphsAsync(Font font, string text, long frameIndex = 1)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(text);

        font.BeginFrame(frameIndex);
        foreach (Rune rune in text.EnumerateRunes())
        {
            font.TryGetGlyphId(rune, out int glyphId);
            FrameAssetRetention probe = new();
            Texture? texture;
            try
            {
                texture = font.AcquireGlyphTexture(glyphId, probe);
            }
            finally
            {
                probe.Release();
            }

            if (texture is null)
            {
                await font.WhenGlyphSettledAsync(glyphId)
                    .WaitAsync(
                        GlyphPipelineTestHarness.TIMEOUT,
                        TestContext.Current.CancellationToken
                    );
            }
        }
    }

    /// <summary>Gets a generated glyph's cached texture without adding a frame hold.</summary>
    /// <returns>The cached texture, or <see langword="null"/> when the glyph is empty or not ready.</returns>
    internal static Texture? GetGlyphTexture(Font font, char value)
    {
        ArgumentNullException.ThrowIfNull(font);
        font.TryGetGlyphId(new Rune(value), out int glyphId);
        FrameAssetRetention probe = new();
        try
        {
            return font.AcquireGlyphTexture(glyphId, probe);
        }
        finally
        {
            probe.Release();
        }
    }

    /// <summary>Gets a ready glyph's padding-inclusive metrics in em units.</summary>
    /// <returns>The glyph metrics.</returns>
    internal static Glyph GetGlyph(Font font, char value)
    {
        ArgumentNullException.ThrowIfNull(font);
        Assert.True(
            font.TryGetGlyph(new Rune(value), out Glyph glyph),
            $"The glyph '{value}' is not ready; generate it before asserting its layout."
        );
        return glyph;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (Font font in _fonts)
        {
            if (!font.IsDisposed)
            {
                font.Dispose();
            }
        }

        _glyphs.Dispose();
        _primitives.Dispose();
        _catalog.Dispose();
        _host.Dispose();
    }
}
