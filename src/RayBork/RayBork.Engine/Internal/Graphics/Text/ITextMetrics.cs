using System.Text;

namespace RayBork.Internal;

/// <summary>
/// Supplies the font metrics that <see cref="TextLayoutEngine"/> lays text out with. All values are in
/// em units and use a y-up baseline.
/// </summary>
/// <remarks>
/// <para>
/// The engine never parses a font and never needs a glyph texture. The runtime <c>Font</c> exposes an
/// internal adapter class that implements this interface; the adapter is a reference type, so an
/// interface call never boxes it. C# itself would allow a public class to implement this internal
/// interface, but the engine assembly's architecture policy rejects a public type whose implemented
/// interfaces live in <c>RayBork.Internal</c>: <c>EnginePublicSurfaceExposesNoBackendTypes</c> audits
/// every exported type with <c>PublicSurfaceInspector</c>. The adapter is therefore a design choice
/// forced by the repository's architecture policy, not a compiler requirement.
/// </para>
/// <para>
/// Metrics exist for every mapped rune whether or not the rune's glyph texture has been generated.
/// Implementations must be safe to call from any thread, and the engine only calls them from the thread
/// that called the enclosing layout operation.
/// </para>
/// <para>
/// A provider that substitutes a replacement glyph for an unmapped rune returns that glyph from
/// <see cref="TryGetGlyphMetrics"/>. Returning <see langword="false"/> means the rune has no glyph; the
/// layout engine then draws nothing for it and does not advance the pen, and the kerning pair bridges
/// the rune to the next mapped glyph.
/// </para>
/// </remarks>
internal interface ITextMetrics
{
    /// <summary>
    /// Gets the baseline-to-baseline distance in em units, including the line gap. It must be positive.
    /// </summary>
    float LineHeight { get; }

    /// <summary>
    /// Gets the distance from the baseline to the top of the ascent in em units. It must be positive.
    /// </summary>
    float Ascent { get; }

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
