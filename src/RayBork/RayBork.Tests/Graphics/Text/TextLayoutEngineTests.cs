using System.Numerics;
using RayBork.Internal;

namespace RayBork.Tests.Graphics.Text;

/// <summary>
/// Tests the pure CPU layout engine: alignment, origin and baseline math, unicode decoding, kerning
/// within lines, glyph quads, and block bounds. Every expectation is hand-computed from the fake
/// metrics; the engine needs no GPU, asset catalog, or font parser.
/// </summary>
public sealed class TextLayoutEngineTests {
    private const float TOLERANCE = 1e-5f;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EmptyTextHasNoLinesAndZeroBounds() {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        int count = Arrange(metrics, string.Empty, buffer, out TextLayoutResult result);

        Assert.Equal(0, count);
        Assert.Equal(0, buffer.Count);
        Assert.True(buffer.Glyphs.IsEmpty);
        Assert.Equal(0, result.LineCount);
        AssertVector(Vector2.Zero, result.BoundsMin);
        AssertVector(Vector2.Zero, result.BoundsMax);
        AssertVector(Vector2.Zero, result.BoundsSize);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SingleLineLeftAlignedStartsAtTheOrigin() {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        int count = Arrange(metrics, "ab", buffer, out TextLayoutResult result);

        Assert.Equal(2, count);
        Assert.Equal(2, buffer.Count);
        Assert.Equal(1, result.LineCount);
        AssertGlyph(buffer.Glyphs[0], TextLayoutTestData.GLYPH_A, 0f, -TextLayoutTestData.ASCENT);
        AssertGlyph(
            buffer.Glyphs[1],
            TextLayoutTestData.GLYPH_B,
            TextLayoutTestData.ADVANCE,
            -TextLayoutTestData.ASCENT);
        AssertVector(new Vector2(0f, -TextLayoutTestData.LINE_HEIGHT), result.BoundsMin);
        AssertVector(new Vector2(TextLayoutTestData.ADVANCE * 2f, 0f), result.BoundsMax);
    }

    [Theory]
    [InlineData(TextAlignment.Left)]
    [InlineData(TextAlignment.Center)]
    [InlineData(TextAlignment.Right)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SingleLineWithoutMaxWidthIsTheSameForEveryAlignment(TextAlignment alignment) {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        Arrange(metrics, "ab", buffer, out TextLayoutResult result, alignment: alignment);

        Assert.Equal(1, result.LineCount);
        AssertGlyph(buffer.Glyphs[0], TextLayoutTestData.GLYPH_A, 0f, -TextLayoutTestData.ASCENT);
        AssertGlyph(
            buffer.Glyphs[1],
            TextLayoutTestData.GLYPH_B,
            TextLayoutTestData.ADVANCE,
            -TextLayoutTestData.ASCENT);
    }

    [Theory]
    [InlineData(TextAlignment.Left, 0f)]
    [InlineData(TextAlignment.Center, 0.5f)]
    [InlineData(TextAlignment.Right, 1f)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SingleLineAlignmentShiftsInsideMaxWidth(TextAlignment alignment, float offset) {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        Arrange(metrics, "ab", buffer, out TextLayoutResult result, alignment: alignment, maxWidth: 2f);

        Assert.Equal(1, result.LineCount);
        Assert.Equal(offset, buffer.Glyphs[0].Position.X, TOLERANCE);
        Assert.Equal(offset + TextLayoutTestData.ADVANCE, buffer.Glyphs[1].Position.X, TOLERANCE);
        AssertVector(new Vector2(0f, -TextLayoutTestData.LINE_HEIGHT), result.BoundsMin);
        AssertVector(new Vector2(2f, 0f), result.BoundsMax);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void NewlinesStartNewLines() {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        int count = Arrange(metrics, "ab\nc", buffer, out TextLayoutResult result);

        Assert.Equal(3, count);
        Assert.Equal(2, result.LineCount);
        AssertGlyph(buffer.Glyphs[0], TextLayoutTestData.GLYPH_A, 0f, -1f);
        AssertGlyph(buffer.Glyphs[1], TextLayoutTestData.GLYPH_B, 0.5f, -1f);
        AssertGlyph(buffer.Glyphs[2], TextLayoutTestData.GLYPH_C, 0f, -2.5f);
        AssertVector(new Vector2(0f, -3f), result.BoundsMin);
        AssertVector(new Vector2(1f, 0f), result.BoundsMax);
    }

    [Theory]
    [InlineData(TextAlignment.Left, 0f, 0f)]
    [InlineData(TextAlignment.Center, 0.5f, 0.75f)]
    [InlineData(TextAlignment.Right, 1f, 1.5f)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AlignmentShiftsEveryLineInsideMaxWidth(
        TextAlignment alignment,
        float firstOffset,
        float secondOffset) {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        Arrange(metrics, "ab\nc", buffer, out TextLayoutResult result, alignment: alignment, maxWidth: 2f);

        Assert.Equal(2, result.LineCount);
        Assert.Equal(firstOffset, buffer.Glyphs[0].Position.X, TOLERANCE);
        Assert.Equal(firstOffset + 0.5f, buffer.Glyphs[1].Position.X, TOLERANCE);
        Assert.Equal(secondOffset, buffer.Glyphs[2].Position.X, TOLERANCE);
        AssertVector(new Vector2(0f, -3f), result.BoundsMin);
        AssertVector(new Vector2(2f, 0f), result.BoundsMax);
    }

    [Theory]
    [InlineData(TextAlignment.Left, 0f)]
    [InlineData(TextAlignment.Center, 0.25f)]
    [InlineData(TextAlignment.Right, 0.5f)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AlignmentWithoutMaxWidthUsesTheWidestLine(TextAlignment alignment, float secondOffset) {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        Arrange(metrics, "ab\nc", buffer, out TextLayoutResult result, alignment: alignment);

        Assert.Equal(secondOffset, buffer.Glyphs[2].Position.X, TOLERANCE);
        AssertVector(new Vector2(0f, -3f), result.BoundsMin);
        AssertVector(new Vector2(1f, 0f), result.BoundsMax);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CarriageReturnsAreIgnored() {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        int count = Arrange(metrics, "a\rb", buffer, out TextLayoutResult result);

        Assert.Equal(2, count);
        Assert.Equal(1, result.LineCount);
        AssertGlyph(buffer.Glyphs[1], TextLayoutTestData.GLYPH_B, 0.5f, -1f);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CarriageReturnBeforeNewlineIsIgnored() {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        int count = Arrange(metrics, "a\r\nb", buffer, out TextLayoutResult result);

        Assert.Equal(2, count);
        Assert.Equal(2, result.LineCount);
        AssertGlyph(buffer.Glyphs[1], TextLayoutTestData.GLYPH_B, 0f, -2.5f);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CarriageReturnOnlyTextHasOneEmptyLine() {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        int count = Arrange(metrics, "\r", buffer, out TextLayoutResult result);

        Assert.Equal(0, count);
        Assert.Equal(1, result.LineCount);
        AssertVector(new Vector2(0f, -1.5f), result.BoundsMin);
        AssertVector(Vector2.Zero, result.BoundsMax);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SurrogatePairsDecodeToOneGlyph() {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        int count = Arrange(metrics, "a\U0001F600b", buffer, out TextLayoutResult result);

        Assert.Equal(3, count);
        AssertGlyph(buffer.Glyphs[0], TextLayoutTestData.GLYPH_A, 0f, -1f);
        AssertGlyph(buffer.Glyphs[1], TextLayoutTestData.GLYPH_EMOJI, 0.5f, -1f);
        AssertGlyph(buffer.Glyphs[2], TextLayoutTestData.GLYPH_B, 1.5f, -1f);
        AssertVector(new Vector2(2f, 0f), result.BoundsMax);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UnpairedSurrogatesDecodeToTheReplacementGlyph() {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer high = new();
        TextLayoutBuffer low = new();

        int highCount = Arrange(metrics, new string((char)0xD83D, 1), high, out TextLayoutResult highResult);
        int lowCount = Arrange(metrics, new string((char)0xDE00, 1), low, out TextLayoutResult lowResult);

        Assert.Equal(1, highCount);
        Assert.Equal(1, lowCount);
        Assert.Equal(1, highResult.LineCount);
        Assert.Equal(1, lowResult.LineCount);
        Assert.Equal(TextLayoutTestData.GLYPH_REPLACEMENT, high.Glyphs[0].GlyphId);
        Assert.Equal(TextLayoutTestData.GLYPH_REPLACEMENT, low.Glyphs[0].GlyphId);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UnpairedSurrogateInsideTextTakesOneGlyphSlot() {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        int count = Arrange(metrics, "a\uD83Db", buffer, out TextLayoutResult result);

        Assert.Equal(3, count);
        Assert.Equal(TextLayoutTestData.GLYPH_A, buffer.Glyphs[0].GlyphId);
        Assert.Equal(TextLayoutTestData.GLYPH_REPLACEMENT, buffer.Glyphs[1].GlyphId);
        AssertGlyph(buffer.Glyphs[2], TextLayoutTestData.GLYPH_B, 1f, -1f);
        AssertVector(new Vector2(1.5f, 0f), result.BoundsMax);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void HandComputedQuadPositionsAndSizes() {
        FakeTextMetrics metrics = new FakeTextMetrics(lineHeight: 2f, ascent: 1.2f)
            .Glyph('a', 7, advanceX: 0.5f, bearingX: 0.1f, bearingY: -0.25f, width: 0.4f, height: 0.9f)
            .Glyph('b', 8, advanceX: 0.75f, bearingX: -0.05f, bearingY: 0.15f, width: 0.6f, height: 0.3f);
        TextLayoutBuffer buffer = new();

        int count = Arrange(metrics, "ab", buffer, out TextLayoutResult result, size: 2f);

        Assert.Equal(2, count);
        // Baseline 0 is -(1.2) * 2 = -2.4. 'a' sits at pen 0; 'b' at pen 0.5 * 2.
        AssertQuad(buffer.Glyphs[0], 7, x: 0.2f, y: -2.9f, width: 0.8f, height: 1.8f);
        AssertQuad(buffer.Glyphs[1], 8, x: 0.9f, y: -2.1f, width: 1.2f, height: 0.6f);
        AssertVector(new Vector2(0f, -4f), result.BoundsMin);
        AssertVector(new Vector2(2.5f, 0f), result.BoundsMax);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void BlockBoundsIncludeGlyphOvershoot() {
        FakeTextMetrics metrics = new FakeTextMetrics()
            .Glyph('a', 7, advanceX: 0.5f, bearingX: -0.5f, bearingY: 0.2f, width: 1f, height: 1.5f);
        TextLayoutBuffer buffer = new();

        Arrange(metrics, "a", buffer, out TextLayoutResult result);

        AssertQuad(buffer.Glyphs[0], 7, x: -0.5f, y: -0.8f, width: 1f, height: 1.5f);
        AssertVector(new Vector2(-0.5f, -1.5f), result.BoundsMin);
        AssertVector(new Vector2(0.5f, 0.7f), result.BoundsMax);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void BaselinesUseAscentAndLineHeight() {
        FakeTextMetrics metrics = new FakeTextMetrics(lineHeight: 1.2f, ascent: 0.8f)
            .Glyph('a', 1, advanceX: 0.5f, bearingY: 0.1f, width: 0.2f, height: 0.5f)
            .Glyph('b', 2, advanceX: 0.5f, bearingY: 0.1f, width: 0.2f, height: 0.5f)
            .Glyph('c', 3, advanceX: 0.5f, bearingY: 0.1f, width: 0.2f, height: 0.5f);
        TextLayoutBuffer buffer = new();

        int count = Arrange(metrics, "a\nb\nc", buffer, out TextLayoutResult result, size: 2f);

        Assert.Equal(3, count);
        Assert.Equal(3, result.LineCount);
        // Baselines: line 0 = -0.8 * 2, line 1 = -(0.8 + 1.2) * 2, line 2 = -(0.8 + 2.4) * 2.
        Assert.Equal(0f, buffer.Glyphs[0].Position.X, TOLERANCE);
        Assert.Equal(-1.4f, buffer.Glyphs[0].Position.Y, TOLERANCE);
        Assert.Equal(-3.8f, buffer.Glyphs[1].Position.Y, TOLERANCE);
        Assert.Equal(-6.2f, buffer.Glyphs[2].Position.Y, TOLERANCE);
        AssertVector(new Vector2(0f, -7.2f), result.BoundsMin);
        AssertVector(new Vector2(1f, 0f), result.BoundsMax);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void LineHeightIsIndependentOfAscent() {
        FakeTextMetrics metrics = new FakeTextMetrics(lineHeight: 2.5f, ascent: 1f)
            .Glyph('a', 1, advanceX: 0.5f)
            .Glyph('b', 2, advanceX: 0.5f)
            .Glyph('c', 3, advanceX: 0.5f);
        TextLayoutBuffer buffer = new();

        Arrange(metrics, "a\nb\nc", buffer, out TextLayoutResult result);

        Assert.Equal(3, result.LineCount);
        // Baselines: line 0 = -1, line 1 = -(1 + 2.5), line 2 = -(1 + 5). Only LineHeight may drive
        // the line step; an implementation that reused Ascent could not produce these values.
        Assert.Equal(-1f, buffer.Glyphs[0].Position.Y, TOLERANCE);
        Assert.Equal(-3.5f, buffer.Glyphs[1].Position.Y, TOLERANCE);
        Assert.Equal(-6f, buffer.Glyphs[2].Position.Y, TOLERANCE);
        Assert.Equal(7.5f, TextLayoutEngine.Measure(metrics, "a\nb\nc", 1f).Y, TOLERANCE);
        Assert.Equal(7.5f, result.BoundsSize.Y, TOLERANCE);
    }

    [Theory]
    [InlineData(TextAlignment.Center, -0.5f)]
    [InlineData(TextAlignment.Right, -1f)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void OverflowingLineUsesANegativeAlignmentOffset(TextAlignment alignment, float offset) {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        Arrange(metrics, "abcd", buffer, out TextLayoutResult result, alignment: alignment, maxWidth: 1f);

        Assert.Equal(1, result.LineCount);
        Assert.Equal(offset, buffer.Glyphs[0].Position.X, TOLERANCE);
        Assert.Equal(offset + 0.5f, buffer.Glyphs[1].Position.X, TOLERANCE);
        Assert.Equal(offset + 1f, buffer.Glyphs[2].Position.X, TOLERANCE);
        Assert.Equal(offset + 1.5f, buffer.Glyphs[3].Position.X, TOLERANCE);
        AssertVector(new Vector2(offset, -1.5f), result.BoundsMin);
        AssertVector(new Vector2(1f, 0f), result.BoundsMax);
    }

    [Theory]
    [InlineData(TextAlignment.Center, 0.25f)]
    [InlineData(TextAlignment.Right, 0.5f)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WrappedLinesAlignByTheirOwnWidth(TextAlignment alignment, float offset) {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        Arrange(
            metrics,
            "ab cd",
            buffer,
            out TextLayoutResult result,
            alignment: alignment,
            wrapping: TextWrapping.Word,
            maxWidth: 1.5f);

        Assert.Equal(2, result.LineCount);
        // Both lines are one world unit wide while the unwrapped paragraph is 2.5 wide; an
        // implementation that aligned by the paragraph width would use 0.75 or 1.5 instead.
        Assert.Equal(offset, buffer.Glyphs[0].Position.X, TOLERANCE);
        Assert.Equal(offset + 0.5f, buffer.Glyphs[1].Position.X, TOLERANCE);
        Assert.Equal(offset, buffer.Glyphs[2].Position.X, TOLERANCE);
        Assert.Equal(offset + 0.5f, buffer.Glyphs[3].Position.X, TOLERANCE);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UnmappedRunesAreSkippedAndDoNotBreakKerning() {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform()
            .Kerning(TextLayoutTestData.GLYPH_A, TextLayoutTestData.GLYPH_B, 0.25f);
        TextLayoutBuffer buffer = new();

        int count = Arrange(metrics, "azb", buffer, out TextLayoutResult result);

        Assert.Equal(2, count);
        AssertGlyph(buffer.Glyphs[0], TextLayoutTestData.GLYPH_A, 0f, -1f);
        AssertGlyph(buffer.Glyphs[1], TextLayoutTestData.GLYPH_B, 0.75f, -1f);
        AssertVector(new Vector2(0f, -1.5f), result.BoundsMin);
        AssertVector(new Vector2(1.25f, 0f), result.BoundsMax);
    }

    private static int Arrange(
        FakeTextMetrics metrics,
        string text,
        TextLayoutBuffer buffer,
        out TextLayoutResult result,
        float size = 1f,
        TextAlignment alignment = TextAlignment.Left,
        TextWrapping wrapping = TextWrapping.None,
        float maxWidth = 0f) {
        return TextLayoutEngine.Arrange(metrics, text, size, alignment, wrapping, maxWidth, buffer, out result);
    }

    private static void AssertGlyph(TextGlyphInstance glyph, int glyphId, float x, float y) {
        Assert.Equal(glyphId, glyph.GlyphId);
        Assert.Equal(x, glyph.Position.X, TOLERANCE);
        Assert.Equal(y, glyph.Position.Y, TOLERANCE);
    }

    private static void AssertQuad(
        TextGlyphInstance glyph,
        int glyphId,
        float x,
        float y,
        float width,
        float height) {
        AssertGlyph(glyph, glyphId, x, y);
        Assert.Equal(width, glyph.Size.X, TOLERANCE);
        Assert.Equal(height, glyph.Size.Y, TOLERANCE);
    }

    private static void AssertVector(Vector2 expected, Vector2 actual) {
        Assert.Equal(expected.X, actual.X, TOLERANCE);
        Assert.Equal(expected.Y, actual.Y, TOLERANCE);
    }
}
