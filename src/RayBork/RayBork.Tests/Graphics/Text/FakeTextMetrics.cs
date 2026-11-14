using System.Text;
using RayBork.Internal;

namespace RayBork.Tests.Graphics.Text;

/// <summary>
/// A deterministic in-memory <see cref="ITextMetrics"/> provider for text layout tests. Glyphs are
/// registered explicitly, so an unregistered rune behaves like a rune with no glyph in the face.
/// </summary>
internal sealed class FakeTextMetrics : ITextMetrics {
    private readonly Dictionary<int, TextGlyphMetrics> _glyphs = [];
    private readonly Dictionary<(int Left, int Right), float> _kerning = [];

    internal FakeTextMetrics(float lineHeight = 1.5f, float ascent = 1f, float descent = 0.5f) {
        LineHeight = lineHeight;
        Ascent = ascent;
        Descent = descent;
    }

    public float LineHeight { get; }

    public float Ascent { get; }

    public float Descent { get; }

    internal FakeTextMetrics Glyph(
        char character,
        int glyphId,
        float advanceX,
        float bearingX = 0f,
        float bearingY = 0f,
        float width = 0f,
        float height = 0f) {
        _glyphs[character] = new TextGlyphMetrics(glyphId, advanceX, bearingX, bearingY, width, height);
        return this;
    }

    internal FakeTextMetrics Glyph(
        Rune rune,
        int glyphId,
        float advanceX,
        float bearingX = 0f,
        float bearingY = 0f,
        float width = 0f,
        float height = 0f) {
        _glyphs[rune.Value] = new TextGlyphMetrics(glyphId, advanceX, bearingX, bearingY, width, height);
        return this;
    }

    internal FakeTextMetrics Kerning(int leftGlyphId, int rightGlyphId, float adjustment) {
        _kerning[(leftGlyphId, rightGlyphId)] = adjustment;
        return this;
    }

    public bool TryGetGlyphMetrics(Rune rune, out TextGlyphMetrics metrics) {
        return _glyphs.TryGetValue(rune.Value, out metrics);
    }

    public float GetKerning(int leftGlyphId, int rightGlyphId) {
        return _kerning.TryGetValue((leftGlyphId, rightGlyphId), out float adjustment) ? adjustment : 0f;
    }
}
