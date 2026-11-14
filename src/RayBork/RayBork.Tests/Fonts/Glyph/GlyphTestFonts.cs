using RayBork.Internal;

namespace RayBork.Tests.Fonts.Glyph;

/// <summary>
/// Synthetic faces for the glyph pipeline tests: one mapped square, one empty glyph, the two
/// outline-resolution failures, and a face with many small glyphs for the cache-budget test.
/// </summary>
internal static class GlyphTestFonts {
    /// <summary>The font units per em of every synthetic face.</summary>
    internal const int UNITS_PER_EM = 1000;

    /// <summary>Creates a face with one square glyph at glyph id 1 and a 600-unit advance.</summary>
    /// <param name="glyphId">Receives the square glyph id.</param>
    /// <returns>The parsed face.</returns>
    internal static SfntFace CreateSquareFace(out int glyphId) {
        SyntheticFontBuilder builder = SyntheticFontBuilder.Create();
        glyphId = builder.AddSimple(
            600,
            0,
            new SyntheticPoint(0, 0),
            new SyntheticPoint(500, 0),
            new SyntheticPoint(500, 500),
            new SyntheticPoint(0, 500));
        return SfntFace.Parse(builder.WithCmap(4, (0x41, glyphId)).Build());
    }

    /// <summary>Creates a face with one glyph that has no outline and a 250-unit advance.</summary>
    /// <param name="glyphId">Receives the empty glyph id.</param>
    /// <returns>The parsed face.</returns>
    internal static SfntFace CreateEmptyGlyphFace(out int glyphId) {
        SyntheticFontBuilder builder = SyntheticFontBuilder.Create();
        glyphId = builder.AddEmpty(250);
        return SfntFace.Parse(builder.WithCmap(4, (0x20, glyphId)).Build());
    }

    /// <summary>Creates a face with one composite glyph that uses point-matched component arguments.</summary>
    /// <param name="glyphId">Receives the unsupported glyph id.</param>
    /// <returns>The parsed face.</returns>
    internal static SfntFace CreateUnsupportedCompositeFace(out int glyphId) {
        SyntheticFontBuilder builder = SyntheticFontBuilder.Create();
        glyphId = builder.AddComposite(500, 0, SyntheticComponent.PointMatch(0));
        return SfntFace.Parse(builder.WithCmap(4, (0x42, glyphId)).Build());
    }

    /// <summary>Creates a face with one composite glyph whose stored header box is inverted.</summary>
    /// <param name="glyphId">Receives the malformed glyph id.</param>
    /// <returns>The parsed face.</returns>
    internal static SfntFace CreateInvertedBoundsFace(out int glyphId) {
        SyntheticFontBuilder builder = SyntheticFontBuilder.Create();
        glyphId = builder.AddComposite(
            500,
            0,
            new FontBounds(500, 500, 100, 100),
            SyntheticComponent.Translate(0, 0, 0));
        return SfntFace.Parse(builder.WithCmap(4, (0x43, glyphId)).Build());
    }

    /// <summary>
    /// Creates a face with <paramref name="glyphCount"/> small triangle glyphs after the <c>.notdef</c>
    /// glyph, for a cache that must evict.
    /// </summary>
    /// <param name="glyphCount">The number of texture glyphs to add.</param>
    /// <param name="firstGlyphId">Receives the first added glyph id.</param>
    /// <returns>The parsed face.</returns>
    internal static SfntFace CreateManyGlyphFace(int glyphCount, out int firstGlyphId) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(glyphCount);

        SyntheticFontBuilder builder = SyntheticFontBuilder.Create();
        firstGlyphId = builder.GlyphCount;
        for (int index = 0; index < glyphCount; index++) {
            builder.AddSimple(
                500,
                0,
                new SyntheticPoint(0, 0),
                new SyntheticPoint(100, 0),
                new SyntheticPoint(50, 100));
        }

        return SfntFace.Parse(builder.WithCmap(4, (0x41, firstGlyphId)).Build());
    }
}
