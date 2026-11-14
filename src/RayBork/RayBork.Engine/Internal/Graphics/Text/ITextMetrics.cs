using System.Text;

namespace RayBork.Internal;

/// <summary>
/// Supplies the font metrics that <see cref="TextLayoutEngine"/> lays text out with. All values are in
/// em units and use a y-up baseline.
/// </summary>
/// <remarks>
/// <para>
/// The runtime <c>Font</c> provides this interface to the layout engine. The engine never needs
/// a glyph texture and never parses a font: metrics exist for every mapped rune whether or not the
/// rune's glyph texture has been generated. Implementations must be safe to call from any thread, and
/// the engine only calls them from the thread that called the enclosing layout operation.
/// </para>
/// <para>
/// A provider that substitutes a replacement glyph for an unmapped rune returns that glyph from
/// <see cref="TryGetGlyphMetrics"/>. Returning <see langword="false"/> means the rune has no glyph; the
/// layout engine then draws nothing for it and does not advance the pen.
/// </para>
/// </remarks>
internal interface ITextMetrics {
    /// <summary>
    /// Gets the baseline-to-baseline distance in em units, including the line gap. It must be positive.
    /// </summary>
    float LineHeight { get; }

    /// <summary>
    /// Gets the distance from the baseline to the top of the ascent in em units. It must be positive.
    /// </summary>
    float Ascent { get; }

    /// <summary>
    /// Gets the distance from the baseline to the bottom of the descent in em units. It must be
    /// positive.
    /// </summary>
    /// <remarks>
    /// The layout box is defined by <see cref="LineHeight"/> and <see cref="Ascent"/> only; the engine
    /// exposes the descent so a provider can report the same value as the public <c>Font.Descent</c>.
    /// </remarks>
    float Descent { get; }

    /// <summary>
    /// Tries to get the layout metrics of one mapped rune.
    /// </summary>
    /// <param name="rune">The decoded rune.</param>
    /// <param name="metrics">The rune's metrics in em units.</param>
    /// <returns><see langword="true"/> when the provider maps the rune to a glyph.</returns>
    bool TryGetGlyphMetrics(Rune rune, out TextGlyphMetrics metrics);

    /// <summary>
    /// Gets the horizontal kerning adjustment between two adjacent glyphs in em units.
    /// </summary>
    /// <param name="leftGlyphId">The glyph id of the left glyph.</param>
    /// <param name="rightGlyphId">The glyph id of the right glyph.</param>
    /// <returns>The adjustment applied between the two glyphs, or <c>0</c> when the pair has no kerning.</returns>
    float GetKerning(int leftGlyphId, int rightGlyphId);
}
