using System.Text;

namespace RayBork.Tests.Graphics.Text;

/// <summary>
/// Shared metrics and helpers for the text layout tests. The uniform provider maps every character the
/// tests use to a glyph with a <c>0.5</c> em advance (the emoji is <c>1</c> em) and an empty quad, so
/// positions, line widths, and bounds are easy to hand-compute.
/// </summary>
internal static class TextLayoutTestData {
    internal const float LINE_HEIGHT = 1.5f;
    internal const float ASCENT = 1f;
    internal const float ADVANCE = 0.5f;
    internal const int GLYPH_A = 1;
    internal const int GLYPH_B = 2;
    internal const int GLYPH_C = 3;
    internal const int GLYPH_D = 4;
    internal const int GLYPH_E = 5;
    internal const int GLYPH_F = 7;
    internal const int GLYPH_CR = 8;
    internal const int GLYPH_SPACE = 6;
    internal const int GLYPH_EMOJI = 50;
    internal const int GLYPH_REPLACEMENT = 99;

    internal static FakeTextMetrics CreateUniform() {
        return new FakeTextMetrics(LINE_HEIGHT, ASCENT)
            .Glyph('a', GLYPH_A, ADVANCE)
            .Glyph('b', GLYPH_B, ADVANCE)
            .Glyph('c', GLYPH_C, ADVANCE)
            .Glyph('d', GLYPH_D, ADVANCE)
            .Glyph('e', GLYPH_E, ADVANCE)
            .Glyph('f', GLYPH_F, ADVANCE)
            .Glyph(' ', GLYPH_SPACE, ADVANCE)
            .Glyph('\r', GLYPH_CR, ADVANCE)
            .Glyph('\uFFFD', GLYPH_REPLACEMENT, ADVANCE)
            .Glyph(new Rune(0x1F600), GLYPH_EMOJI, 1f);
    }
}
