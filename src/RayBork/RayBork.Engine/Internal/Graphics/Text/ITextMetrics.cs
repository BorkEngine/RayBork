using System.Text;

namespace RayBork.Internal;

/// <summary>Supplies the em-unit, y-up font metrics that <see cref="TextLayoutEngine"/> uses.</summary>
/// <remarks>Implementations must be thread-safe. A rune without a glyph returns <see langword="false"/>
/// and produces no record or advance.</remarks>
internal interface ITextMetrics
{
    /// <summary>Gets the positive baseline-to-baseline distance in em units, including the line gap.</summary>
    float LineHeight { get; }

    /// <summary>Gets the positive baseline-to-ascent-top distance in em units.</summary>
    float Ascent { get; }

    /// <summary>Tries to get the em-unit layout metrics of one mapped rune.</summary>
    /// <param name="rune">The decoded rune.</param>
    /// <param name="metrics">The rune's em-unit metrics.</param>
    /// <returns><see langword="true"/> when the rune maps to a glyph.</returns>
    bool TryGetGlyphMetrics(Rune rune, out TextGlyphMetrics metrics);

    /// <summary>Gets the em-unit horizontal kerning adjustment between two glyphs.</summary>
    /// <param name="leftGlyphId">The left glyph id.</param>
    /// <param name="rightGlyphId">The right glyph id.</param>
    /// <returns>The adjustment, or <c>0</c> when the pair has no kerning.</returns>
    float GetKerning(int leftGlyphId, int rightGlyphId);
}
