using System.Text;

namespace RayBork.Internal;

/// <summary>
/// The <see cref="ITextMetrics"/> adapter of one parsed font face: it supplies the layout engine with
/// em-unit face metrics without generating a glyph texture.
/// </summary>
/// <remarks>
/// <para>
/// The engine exposes a <c>Font</c> asset and never makes the public type implement this internal
/// interface; <see cref="Font.Metrics"/> returns one instance of this adapter. The adapter is a
/// reference type, so an interface call never boxes it.
/// </para>
/// <para>
/// Every rune maps through the face's Unicode cmap; a rune the cmap does not map falls back to glyph
/// id zero (<c>.notdef</c>), so layout always advances the pen by a real glyph. Metrics exist whether
/// or not a glyph texture was generated; <see cref="FontGlyphPipeline.GetMetrics"/> reads the face and
/// the MSDF layout and is safe to call from the thread that runs the layout.
/// </para>
/// </remarks>
internal sealed class FontTextMetrics : ITextMetrics {
    private readonly FontGlyphPipeline _pipeline;
    private readonly int _unitsPerEm;

    /// <summary>
    /// Initializes the adapter over one parsed face and its glyph pipeline.
    /// </summary>
    /// <param name="pipeline">The font's glyph pipeline; it owns the parsed face.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pipeline"/> is <c>null</c>.</exception>
    internal FontTextMetrics(FontGlyphPipeline pipeline) {
        ArgumentNullException.ThrowIfNull(pipeline);

        _pipeline = pipeline;
        _unitsPerEm = pipeline.Face.UnitsPerEm;
        LineHeight = (pipeline.Face.Ascender - pipeline.Face.Descender + pipeline.Face.LineGap)
            / (float)_unitsPerEm;
        Ascent = pipeline.Face.Ascender / (float)_unitsPerEm;
        Descent = -pipeline.Face.Descender / (float)_unitsPerEm;
    }

    /// <inheritdoc/>
    public float LineHeight { get; }

    /// <inheritdoc/>
    public float Ascent { get; }

    /// <summary>
    /// Gets the distance from the baseline to the bottom of the descent in em units.
    /// </summary>
    internal float Descent { get; }

    /// <inheritdoc/>
    /// <remarks>
    /// The face maps every Unicode rune through its cmap and substitutes <c>.notdef</c> for an unmapped
    /// rune, so this method always reports a glyph.
    /// </remarks>
    public bool TryGetGlyphMetrics(Rune rune, out TextGlyphMetrics metrics) {
        int glyphId = SfntFace.NOTDEF_GLYPH_ID;
        _pipeline.Face.TryGetGlyphId(rune, out glyphId);
        metrics = _pipeline.GetMetrics(glyphId);
        return true;
    }

    /// <inheritdoc/>
    public float GetKerning(int leftGlyphId, int rightGlyphId)
        => _pipeline.Face.GetKerning(leftGlyphId, rightGlyphId) / (float)_unitsPerEm;
}
