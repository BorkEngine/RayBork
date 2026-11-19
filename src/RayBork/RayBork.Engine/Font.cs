using System.Numerics;
using System.Text;
using RayBork.Internal;

namespace RayBork;

/// <summary>One parsed TTF or OTF face with an on-demand MSDF glyph cache.</summary>
/// <remarks>
/// A font is a shared, reference-counted asset that owns its glyph pipeline and MSDF cache; dispose
/// every successful <c>Load&lt;Font&gt;</c> exactly once. Metrics are in em units, and access is
/// tick-thread only except <see cref="Path"/> and <see cref="IsDisposed"/>.
/// </remarks>
public sealed class Font : IAsset
{
    private readonly EngineAsset _asset;
    private readonly SfntFace _face;
    private readonly FontGlyphPipeline _pipeline;
    private readonly FontTextMetrics _metrics;
    private readonly int _glyphCount;

    /// <summary>Initializes a font from its committed lifetime, parsed face, and glyph pipeline.</summary>
    internal Font(EngineAsset asset, SfntFace face, FontGlyphPipeline pipeline)
    {
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
    public float LineHeight
    {
        get
        {
            ThrowIfDisposed();
            return _metrics.LineHeight;
        }
    }

    /// <summary>Gets the distance from the baseline to the top of the ascent in em units.</summary>
    public float Ascent
    {
        get
        {
            ThrowIfDisposed();
            return _metrics.Ascent;
        }
    }

    /// <summary>
    /// Gets the positive distance from the baseline to the descent bottom in em units.
    /// </summary>
    public float Descent
    {
        get
        {
            ThrowIfDisposed();
            return _metrics.Descent;
        }
    }

    /// <summary>Gets the number of mapped Unicode codepoints, not distinct glyph ids.</summary>
    public int GlyphCount
    {
        get
        {
            ThrowIfDisposed();
            return _glyphCount;
        }
    }

    /// <summary>Gets the number of glyphs with a cached MSDF texture.</summary>
    /// <remarks>
    /// The count grows with draws and falls on eviction, capped by the per-font texture budget.
    /// </remarks>
    public int CachedGlyphCount
    {
        get
        {
            ThrowIfDisposed();
            return _pipeline.CachedGlyphCount;
        }
    }

    /// <inheritdoc/>
    public bool IsDisposed => _asset.IsDisposed;

    /// <summary>Gets the internal retention state the catalog owns for this asset.</summary>
    internal EngineAsset Lifetime => _asset;

    /// <summary>Gets the layout metrics adapter the text layout engine reads.</summary>
    internal ITextMetrics Metrics
    {
        get
        {
            ThrowIfDisposed();
            return _metrics;
        }
    }

    /// <summary>Tries to get one glyph whose MSDF texture is already generated.</summary>
    /// <param name="rune">The rune to look up.</param>
    /// <param name="glyph">The glyph data in em units when its texture is ready.</param>
    /// <returns>
    /// <see langword="true"/> when the mapped glyph has a ready texture; pending, empty, and failed
    /// glyphs return <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// An unmapped rune falls back to <c>.notdef</c>, and the lookup never generates a texture or
    /// waits.
    /// </remarks>
    public bool TryGetGlyph(Rune rune, out Glyph glyph)
    {
        ThrowIfDisposed();
        int glyphId = SfntFace.NOTDEF_GLYPH_ID;
        _face.TryGetGlyphId(rune, out glyphId);
        if (!_pipeline.TryGetTexture(glyphId, out _))
        {
            glyph = default;
            return false;
        }

        TextGlyphMetrics metrics = _pipeline.GetMetrics(glyphId);
        glyph = new Glyph(
            rune,
            new Vector2(metrics.AdvanceX, 0f),
            new Vector2(metrics.BearingX, metrics.BearingY),
            new Vector2(metrics.Width, metrics.Height)
        );
        return true;
    }

    /// <summary>
    /// Measures one text block in world units at the requested size; the size must be finite and
    /// positive.
    /// </summary>
    /// <returns>
    /// The widest line advance including kerning plus <c>lineCount * LineHeight * size</c>; empty text
    /// measures <see cref="Vector2.Zero"/>.
    /// </returns>
    /// <remarks>
    /// Reads face metrics and kerning only, with no wrapping or width limit. <c>'\n'</c> starts a new
    /// line; <c>'\r'</c> is ignored.
    /// </remarks>
    public Vector2 Measure(ReadOnlySpan<char> text, float size)
    {
        ThrowIfDisposed();
        return TextLayoutEngine.Measure(_metrics, text, size);
    }

    /// <summary>Records the frame index for the following glyph acquisitions.</summary>
    /// <param name="frameIndex">The non-negative tick frame index.</param>
    /// <remarks>
    /// The draw path calls it once per camera; the pipeline keeps the highest index, so a repeated or
    /// stale index is ignored.
    /// </remarks>
    internal void BeginFrame(long frameIndex)
    {
        ThrowIfDisposed();
        _pipeline.BeginFrame(frameIndex);
    }

    /// <summary>
    /// Acquires the cached texture of one glyph for the current frame, or enqueues its generation.
    /// </summary>
    /// <param name="glyphId">The glyph identifier, in <c>[0, glyph count)</c>.</param>
    /// <param name="retention">The frame's retention set, which receives the texture's frame hold.</param>
    /// <returns>
    /// The cached texture when ready; <see langword="null"/> when pending, empty, failed, or absent.
    /// A miss enqueues one request and never blocks.
    /// </returns>
    internal Texture? AcquireGlyphTexture(int glyphId, FrameAssetRetention retention)
    {
        ThrowIfDisposed();
        return _pipeline.Acquire(glyphId, retention);
    }

    /// <summary>Resolves a rune to its glyph identifier, or zero (<c>.notdef</c>) when unmapped.</summary>
    /// <returns><see langword="true"/> when the face maps the rune to a real glyph.</returns>
    internal bool TryGetGlyphId(Rune rune, out int glyphId)
    {
        ThrowIfDisposed();
        bool mapped = _face.TryGetGlyphId(rune, out glyphId);
        if (!mapped)
        {
            glyphId = SfntFace.NOTDEF_GLYPH_ID;
        }

        return mapped;
    }

    /// <summary>
    /// Gets the horizontal kerning adjustment between adjacent glyphs in em units, or zero when the
    /// pair is not kerned.
    /// </summary>
    internal float GetKerning(int leftGlyphId, int rightGlyphId)
    {
        ThrowIfDisposed();
        return _metrics.GetKerning(leftGlyphId, rightGlyphId);
    }

    /// <summary>
    /// Gets a task that completes when one glyph becomes ready, fails, or the pipeline shuts down.
    /// </summary>
    /// <param name="glyphId">The glyph identifier.</param>
    /// <returns>
    /// The pending settlement task, or a completed task when the glyph is already terminal or absent.
    /// </returns>
    /// <remarks>Test hook; call <see cref="AcquireGlyphTexture"/> first for an ungenerated glyph.</remarks>
    internal Task WhenGlyphSettledAsync(int glyphId)
    {
        ThrowIfDisposed();
        return _pipeline.WhenGlyphSettledAsync(glyphId);
    }

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">The font is already disposed.</exception>
    public void Dispose()
    {
        ObjectDisposedException.ThrowIf(_asset.IsDisposed, this);

        _asset.ReleaseOwnerReference();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_asset.IsDisposed, this);
}
