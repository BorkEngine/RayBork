using System.Numerics;
using System.Text;
using RayBork.Internal;

namespace RayBork;

/// <summary>
/// One parsed TTF or OTF face with an on-demand MSDF glyph cache.
/// </summary>
/// <remarks>
/// <para>
/// A font is a shared, reference-counted engine asset: <c>Load&lt;Font&gt;</c> parses the face and
/// returns the same object for a repeated load of the same path. It owns no GPU resource of its own.
/// Loading a font generates no glyph textures, so the object created by that load reports
/// <see cref="CachedGlyphCount"/> <c>0</c>; a repeated load returns the shared object, whose count
/// reflects the glyphs earlier draws already requested. The engine's <c>RayBork.Font</c> thread
/// generates and uploads one small MSDF texture per glyph when a draw first needs it. Drawing never
/// waits for that work: a glyph whose texture is not ready yet is skipped for that call and appears on
/// a later frame, while the pen still advances from the face metrics.
/// </para>
/// <para>
/// Every Unicode rune maps through the face's cmap. A rune the cmap does not map falls back to glyph id
/// zero (<c>.notdef</c>), so layout always advances by a real glyph and never drops the character.
/// There is no hinting and no shaping: Latin, Cyrillic, and Greek text maps codepoint-to-glyph
/// directly, and kerning comes from the face's <c>kern</c> or <c>GPOS</c> table.
/// </para>
/// <para>
/// <see cref="LineHeight"/>, <see cref="Ascent"/>, and <see cref="Descent"/> are in em units;
/// <see cref="Measure"/> and the text draw calls multiply them by the requested size.
/// <see cref="GlyphCount"/> counts the cmap-mapped glyphs and <see cref="CachedGlyphCount"/> counts the
/// ones with a generated MSDF texture. <see cref="TryGetGlyph"/> returns only cached glyphs.
/// <see cref="Measure"/> works from face metrics and kerning without generating a texture.
/// </para>
/// <para>
/// Font metrics and glyph acquisition are tick-thread operations. <see cref="Path"/> and
/// <see cref="IsDisposed"/> stay readable after disposal; every other member throws
/// <see cref="ObjectDisposedException"/> once the asset is disposed. Dispose every successful
/// <c>Load&lt;Font&gt;</c> exactly once.
/// </para>
/// </remarks>
public sealed class Font : IAsset {
    private readonly EngineAsset _asset;
    private readonly SfntFace _face;
    private readonly FontGlyphPipeline _pipeline;
    private readonly FontTextMetrics _metrics;
    private readonly int _glyphCount;

    /// <summary>
    /// Initializes a font from its committed lifetime, parsed face, and glyph pipeline.
    /// </summary>
    /// <param name="asset">The shared lifetime state acquired by the catalog.</param>
    /// <param name="face">The parsed face. The pipeline retains it for the font's lifetime.</param>
    /// <param name="pipeline">The font's glyph pipeline; the lifetime release disposes it.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    internal Font(EngineAsset asset, SfntFace face, FontGlyphPipeline pipeline) {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(face);
        ArgumentNullException.ThrowIfNull(pipeline);

        _asset = asset;
        _face = face;
        _pipeline = pipeline;
        _metrics = new FontTextMetrics(pipeline);
        _glyphCount = face.MappedGlyphCount;
    }

    /// <inheritdoc/>
    public string Path => _asset.Path;

    /// <summary>
    /// Gets the baseline-to-baseline distance in em units, including the face's line gap.
    /// </summary>
    /// <remarks>
    /// The value is <c>(ascender - descender + lineGap) / unitsPerEm</c> from the <c>hhea</c> and
    /// <c>head</c> tables. Multiply it by the requested text size for world units.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The font is disposed.</exception>
    public float LineHeight {
        get {
            ThrowIfDisposed();
            return _metrics.LineHeight;
        }
    }

    /// <summary>
    /// Gets the distance from the baseline to the top of the ascent in em units.
    /// </summary>
    /// <remarks>The value is <c>ascender / unitsPerEm</c>.</remarks>
    /// <exception cref="ObjectDisposedException">The font is disposed.</exception>
    public float Ascent {
        get {
            ThrowIfDisposed();
            return _metrics.Ascent;
        }
    }

    /// <summary>
    /// Gets the distance from the baseline to the bottom of the descent in em units.
    /// </summary>
    /// <remarks>The value is <c>-descender / unitsPerEm</c>, so it is positive for a normal face.</remarks>
    /// <exception cref="ObjectDisposedException">The font is disposed.</exception>
    public float Descent {
        get {
            ThrowIfDisposed();
            return _metrics.Descent;
        }
    }

    /// <summary>
    /// Gets the number of Unicode codepoints that the face's cmap maps to a real glyph.
    /// </summary>
    /// <remarks>
    /// It counts mapped codepoints, not distinct glyph ids: two codepoints that map to the same glyph
    /// both count. It is a property of the face, not of the generated cache, so the number does not
    /// change while the font is used; an unmapped rune falls back to <c>.notdef</c> instead of
    /// increasing it.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The font is disposed.</exception>
    public int GlyphCount {
        get {
            ThrowIfDisposed();
            return _glyphCount;
        }
    }

    /// <summary>
    /// Gets the number of glyphs with a generated MSDF texture cached by this font.
    /// </summary>
    /// <remarks>
    /// The object created by a font load reports <c>0</c>; a repeated load returns the shared object,
    /// whose count reflects the glyphs earlier draws already requested. The value grows as the font
    /// thread generates glyphs for draws and stays hard-capped by the per-font texture budget, so it can
    /// also fall when the cache evicts the least recently used glyph textures.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The font is disposed.</exception>
    public int CachedGlyphCount {
        get {
            ThrowIfDisposed();
            return _pipeline.CachedGlyphCount;
        }
    }

    /// <inheritdoc/>
    public bool IsDisposed => _asset.IsDisposed;

    /// <summary>
    /// Gets the internal retention state the engine keeps for this asset. The catalog owns the asset and
    /// exposes the lifetime only inside the engine assembly.
    /// </summary>
    internal EngineAsset Lifetime => _asset;

    /// <summary>
    /// Gets the layout metrics adapter the text layout engine reads.
    /// </summary>
    /// <remarks>
    /// The public <see cref="Font"/> deliberately does not implement the internal
    /// <see cref="ITextMetrics"/> interface, because the engine architecture policy rejects a public
    /// type whose implemented interfaces live in <c>RayBork.Internal</c>. This adapter is the bridge.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The font is disposed.</exception>
    internal ITextMetrics Metrics {
        get {
            ThrowIfDisposed();
            return _metrics;
        }
    }

    /// <summary>
    /// Tries to get one glyph whose MSDF texture is already generated.
    /// </summary>
    /// <param name="rune">The rune to look up.</param>
    /// <param name="glyph">The glyph data in em units when its texture is ready.</param>
    /// <returns>
    /// <see langword="true"/> when the mapped glyph has a ready texture; <see langword="false"/> when
    /// the glyph is still pending, is empty (a space), or failed.
    /// </returns>
    /// <remarks>
    /// An unmapped rune falls back to glyph id zero (<c>.notdef</c>). The lookup never generates a
    /// texture and never waits: call the draw-side glyph acquisition, which enqueues generation on a
    /// miss, and check again on a later frame. <see cref="Glyph.Rune"/> echoes the requested rune.
    /// </remarks>
    public bool TryGetGlyph(Rune rune, out Glyph glyph) {
        ThrowIfDisposed();
        int glyphId = SfntFace.NOTDEF_GLYPH_ID;
        _face.TryGetGlyphId(rune, out glyphId);
        if (!_pipeline.TryGetTexture(glyphId, out _)) {
            glyph = default;
            return false;
        }

        TextGlyphMetrics metrics = _pipeline.GetMetrics(glyphId);
        glyph = new Glyph(
            rune,
            new Vector2(metrics.AdvanceX, 0f),
            new Vector2(metrics.BearingX, metrics.BearingY),
            new Vector2(metrics.Width, metrics.Height));
        return true;
    }

    /// <summary>
    /// Measures one text block in world units at the requested size.
    /// </summary>
    /// <param name="text">The UTF-16 text to measure.</param>
    /// <param name="size">The em height in world units. It must be finite and positive.</param>
    /// <returns>
    /// The widest line advance including kerning and <c>lineCount * LineHeight * size</c> for the
    /// height. Empty text measures <see cref="Vector2.Zero"/>.
    /// </returns>
    /// <remarks>
    /// Measurement reads face metrics and kerning only: it ignores wrapping and a width limit, and it
    /// never generates a glyph texture. <c>'\n'</c> starts a new line; <c>'\r'</c> is ignored.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="size"/> is not finite and positive.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The font is disposed.</exception>
    public Vector2 Measure(ReadOnlySpan<char> text, float size) {
        ThrowIfDisposed();
        return TextLayoutEngine.Measure(_metrics, text, size);
    }

    /// <summary>
    /// Records the frame that the following glyph acquisitions belong to.
    /// </summary>
    /// <param name="frameIndex">The non-negative tick frame index.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="frameIndex"/> is negative.</exception>
    /// <exception cref="ObjectDisposedException">The font is disposed.</exception>
    /// <remarks>
    /// The draw path calls this once per camera before it records that camera's text, so the glyph
    /// cache clock follows the frame index. The pipeline keeps the highest value, so a repeated index
    /// never moves the clock backwards.
    /// </remarks>
    internal void BeginFrame(long frameIndex) {
        ThrowIfDisposed();
        _pipeline.BeginFrame(frameIndex);
    }

    /// <summary>
    /// Acquires the cached texture of one glyph for the current frame, or enqueues its generation.
    /// </summary>
    /// <param name="glyphId">The glyph identifier, in <c>[0, glyph count)</c>.</param>
    /// <param name="retention">The frame's retention set, which takes the texture's frame hold.</param>
    /// <returns>
    /// The texture when the glyph is ready, or <see langword="null"/> when the glyph is pending, empty,
    /// failed, or absent. A miss enqueues one generation request and never blocks.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="glyphId"/> is outside the face.</exception>
    /// <exception cref="ObjectDisposedException">The font is disposed.</exception>
    internal Texture? AcquireGlyphTexture(int glyphId, FrameAssetRetention retention) {
        ThrowIfDisposed();
        return _pipeline.Acquire(glyphId, retention);
    }

    /// <summary>
    /// Resolves a rune to its glyph identifier.
    /// </summary>
    /// <param name="rune">The rune to map.</param>
    /// <param name="glyphId">
    /// The mapped glyph identifier; glyph id zero (<c>.notdef</c>) when the cmap does not map the rune.
    /// </param>
    /// <returns><see langword="true"/> when the face maps the rune to a real glyph.</returns>
    /// <exception cref="ObjectDisposedException">The font is disposed.</exception>
    internal bool TryGetGlyphId(Rune rune, out int glyphId) {
        ThrowIfDisposed();
        bool mapped = _face.TryGetGlyphId(rune, out glyphId);
        if (!mapped) {
            glyphId = SfntFace.NOTDEF_GLYPH_ID;
        }

        return mapped;
    }

    /// <summary>
    /// Gets the horizontal kerning adjustment between two adjacent glyphs in em units.
    /// </summary>
    /// <param name="leftGlyphId">The left glyph identifier.</param>
    /// <param name="rightGlyphId">The right glyph identifier.</param>
    /// <returns>The adjustment, or <c>0</c> when the pair is not kerned.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A glyph identifier is outside the face.</exception>
    /// <exception cref="ObjectDisposedException">The font is disposed.</exception>
    internal float GetKerning(int leftGlyphId, int rightGlyphId) {
        ThrowIfDisposed();
        return _metrics.GetKerning(leftGlyphId, rightGlyphId);
    }

    /// <summary>
    /// Gets a task that completes when one glyph becomes ready, fails, or the pipeline shuts down.
    /// </summary>
    /// <param name="glyphId">The glyph identifier.</param>
    /// <returns>The pending settlement task, or a completed task when the glyph is already terminal.</returns>
    /// <remarks>
    /// Test hook. Call <see cref="AcquireGlyphTexture"/> first when the glyph is not generated yet; an
    /// absent glyph has no pending request, so its task is already complete. Inspect
    /// <see cref="TryGetGlyph"/> after the task completes to distinguish ready from failed.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="glyphId"/> is outside the face.</exception>
    /// <exception cref="ObjectDisposedException">The font is disposed.</exception>
    internal Task WhenGlyphSettledAsync(int glyphId) {
        ThrowIfDisposed();
        return _pipeline.WhenGlyphSettledAsync(glyphId);
    }

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">The font is already disposed.</exception>
    public void Dispose() {
        ObjectDisposedException.ThrowIf(_asset.IsDisposed, this);

        _asset.ReleaseOwnerReference();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_asset.IsDisposed, this);
}
