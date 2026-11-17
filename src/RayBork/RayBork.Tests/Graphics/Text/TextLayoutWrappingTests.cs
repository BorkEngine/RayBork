using System.Numerics;
using RayBork.Internal;

namespace RayBork.Tests.Graphics.Text;

/// <summary>
/// Tests word and character wrapping and kerning. The uniform fake uses a <c>0.5</c> em advance, so a
/// word of <c>n</c> runes is <c>n * 0.5</c> world units wide at size <c>1</c>.
/// </summary>
public sealed class TextLayoutWrappingTests
{
    private const float TOLERANCE = 1e-5f;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WordWrapBreaksAtASpaceAndConsumesIt()
    {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        int count = Arrange(
            metrics,
            "ab cd",
            buffer,
            TextWrapping.Word,
            1.5f,
            out TextLayoutResult result
        );

        Assert.Equal(4, count);
        Assert.Equal(2, result.LineCount);
        AssertGlyph(buffer.Glyphs[0], TextLayoutTestData.GLYPH_A, 0f, -1f);
        AssertGlyph(buffer.Glyphs[1], TextLayoutTestData.GLYPH_B, 0.5f, -1f);
        AssertGlyph(buffer.Glyphs[2], TextLayoutTestData.GLYPH_C, 0f, -2.5f);
        AssertGlyph(buffer.Glyphs[3], TextLayoutTestData.GLYPH_D, 0.5f, -2.5f);
        AssertVector(new Vector2(0f, -3f), result.BoundsMin);
        AssertVector(new Vector2(1.5f, 0f), result.BoundsMax);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WordThatExactlyFitsMaxWidthDoesNotWrap()
    {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        int count = Arrange(
            metrics,
            "ab cd",
            buffer,
            TextWrapping.Word,
            2.5f,
            out TextLayoutResult result
        );

        Assert.Equal(5, count);
        Assert.Equal(1, result.LineCount);
        AssertGlyph(buffer.Glyphs[2], TextLayoutTestData.GLYPH_SPACE, 1f, -1f);
        AssertGlyph(buffer.Glyphs[3], TextLayoutTestData.GLYPH_C, 1.5f, -1f);
        AssertGlyph(buffer.Glyphs[4], TextLayoutTestData.GLYPH_D, 2f, -1f);
        AssertVector(new Vector2(2.5f, 0f), result.BoundsMax);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WordThatOverflowsMaxWidthWraps()
    {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        int count = Arrange(
            metrics,
            "ab cd",
            buffer,
            TextWrapping.Word,
            2.4f,
            out TextLayoutResult result
        );

        Assert.Equal(4, count);
        Assert.Equal(2, result.LineCount);
        AssertGlyph(buffer.Glyphs[2], TextLayoutTestData.GLYPH_C, 0f, -2.5f);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WordWiderThanMaxWidthOverflowsOnItsOwnLine()
    {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        int count = Arrange(
            metrics,
            "abcd",
            buffer,
            TextWrapping.Word,
            1f,
            out TextLayoutResult result
        );

        Assert.Equal(4, count);
        Assert.Equal(1, result.LineCount);
        AssertGlyph(buffer.Glyphs[3], TextLayoutTestData.GLYPH_D, 1.5f, -1f);
        AssertVector(new Vector2(1.5f, 0f), result.BoundsMax);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WordWiderThanMaxWidthAfterAShortWordOverflowsAlone()
    {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        int count = Arrange(
            metrics,
            "ab cde",
            buffer,
            TextWrapping.Word,
            1f,
            out TextLayoutResult result
        );

        Assert.Equal(5, count);
        Assert.Equal(2, result.LineCount);
        AssertGlyph(buffer.Glyphs[2], TextLayoutTestData.GLYPH_C, 0f, -2.5f);
        AssertGlyph(buffer.Glyphs[3], TextLayoutTestData.GLYPH_D, 0.5f, -2.5f);
        AssertGlyph(buffer.Glyphs[4], TextLayoutTestData.GLYPH_E, 1f, -2.5f);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ConsecutiveSpacesAreDrawnWhenTheLineFits()
    {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        int count = Arrange(
            metrics,
            "a   b",
            buffer,
            TextWrapping.Word,
            4f,
            out TextLayoutResult result
        );

        Assert.Equal(5, count);
        Assert.Equal(1, result.LineCount);
        AssertGlyph(buffer.Glyphs[1], TextLayoutTestData.GLYPH_SPACE, 0.5f, -1f);
        AssertGlyph(buffer.Glyphs[2], TextLayoutTestData.GLYPH_SPACE, 1f, -1f);
        AssertGlyph(buffer.Glyphs[3], TextLayoutTestData.GLYPH_SPACE, 1.5f, -1f);
        AssertGlyph(buffer.Glyphs[4], TextLayoutTestData.GLYPH_B, 2f, -1f);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ConsecutiveSpacesAreConsumedAtABreak()
    {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        int count = Arrange(
            metrics,
            "a   b",
            buffer,
            TextWrapping.Word,
            0.5f,
            out TextLayoutResult result
        );

        Assert.Equal(2, count);
        Assert.Equal(2, result.LineCount);
        AssertGlyph(buffer.Glyphs[0], TextLayoutTestData.GLYPH_A, 0f, -1f);
        AssertGlyph(buffer.Glyphs[1], TextLayoutTestData.GLYPH_B, 0f, -2.5f);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TrailingSpaceStaysOnItsLineAtABreak()
    {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        int count = Arrange(
            metrics,
            "ab cd ",
            buffer,
            TextWrapping.Word,
            1.5f,
            out TextLayoutResult result
        );

        Assert.Equal(5, count);
        Assert.Equal(2, result.LineCount);
        AssertGlyph(buffer.Glyphs[2], TextLayoutTestData.GLYPH_C, 0f, -2.5f);
        AssertGlyph(buffer.Glyphs[3], TextLayoutTestData.GLYPH_D, 0.5f, -2.5f);
        AssertGlyph(buffer.Glyphs[4], TextLayoutTestData.GLYPH_SPACE, 1f, -2.5f);
        AssertVector(new Vector2(0f, -3f), result.BoundsMin);
        AssertVector(new Vector2(1.5f, 0f), result.BoundsMax);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TrailingSpaceWithoutABreakIsDrawn()
    {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        int count = Arrange(
            metrics,
            "ab ",
            buffer,
            TextWrapping.Word,
            1.5f,
            out TextLayoutResult result
        );

        Assert.Equal(3, count);
        Assert.Equal(1, result.LineCount);
        AssertGlyph(buffer.Glyphs[2], TextLayoutTestData.GLYPH_SPACE, 1f, -1f);
        AssertVector(new Vector2(1.5f, 0f), result.BoundsMax);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void LeadingSpaceStaysOnTheFirstLine()
    {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        int count = Arrange(
            metrics,
            " ab",
            buffer,
            TextWrapping.Word,
            1f,
            out TextLayoutResult result
        );

        Assert.Equal(3, count);
        Assert.Equal(1, result.LineCount);
        AssertGlyph(buffer.Glyphs[0], TextLayoutTestData.GLYPH_SPACE, 0f, -1f);
        AssertGlyph(buffer.Glyphs[1], TextLayoutTestData.GLYPH_A, 0.5f, -1f);
        AssertGlyph(buffer.Glyphs[2], TextLayoutTestData.GLYPH_B, 1f, -1f);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WordWrapBreaksBetweenWordsGreedily()
    {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        int count = Arrange(
            metrics,
            "aa bb cc",
            buffer,
            TextWrapping.Word,
            2.5f,
            out TextLayoutResult result
        );

        Assert.Equal(7, count);
        Assert.Equal(2, result.LineCount);
        AssertGlyph(buffer.Glyphs[4], TextLayoutTestData.GLYPH_B, 2f, -1f);
        AssertGlyph(buffer.Glyphs[5], TextLayoutTestData.GLYPH_C, 0f, -2.5f);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CharacterWrapBreaksBetweenRunes()
    {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        int count = Arrange(
            metrics,
            "abcd",
            buffer,
            TextWrapping.Character,
            1.2f,
            out TextLayoutResult result
        );

        Assert.Equal(4, count);
        Assert.Equal(2, result.LineCount);
        AssertGlyph(buffer.Glyphs[0], TextLayoutTestData.GLYPH_A, 0f, -1f);
        AssertGlyph(buffer.Glyphs[1], TextLayoutTestData.GLYPH_B, 0.5f, -1f);
        AssertGlyph(buffer.Glyphs[2], TextLayoutTestData.GLYPH_C, 0f, -2.5f);
        AssertGlyph(buffer.Glyphs[3], TextLayoutTestData.GLYPH_D, 0.5f, -2.5f);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CharacterWrapKeepsAFittingLine()
    {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        int count = Arrange(
            metrics,
            "ab",
            buffer,
            TextWrapping.Character,
            1f,
            out TextLayoutResult result
        );

        Assert.Equal(2, count);
        Assert.Equal(1, result.LineCount);
        AssertGlyph(buffer.Glyphs[1], TextLayoutTestData.GLYPH_B, 0.5f, -1f);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CharacterWrapMovesAWideRuneToItsOwnLine()
    {
        FakeTextMetrics metrics = TextLayoutTestData
            .CreateUniform()
            .Glyph('d', TextLayoutTestData.GLYPH_D, 2f);
        TextLayoutBuffer buffer = new();

        int count = Arrange(
            metrics,
            "ad",
            buffer,
            TextWrapping.Character,
            1f,
            out TextLayoutResult result
        );

        Assert.Equal(2, count);
        Assert.Equal(2, result.LineCount);
        AssertGlyph(buffer.Glyphs[0], TextLayoutTestData.GLYPH_A, 0f, -1f);
        AssertGlyph(buffer.Glyphs[1], TextLayoutTestData.GLYPH_D, 0f, -2.5f);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CharacterWrapDropsKerningAcrossTheBreak()
    {
        FakeTextMetrics metrics = TextLayoutTestData
            .CreateUniform()
            .Kerning(TextLayoutTestData.GLYPH_B, TextLayoutTestData.GLYPH_C, 1f);
        TextLayoutBuffer wrapped = new();
        TextLayoutBuffer unwrapped = new();

        Arrange(metrics, "abcd", wrapped, TextWrapping.Character, 1.2f, out _);
        Arrange(metrics, "abcd", unwrapped, TextWrapping.Character, 4f, out _);

        AssertGlyph(wrapped.Glyphs[2], TextLayoutTestData.GLYPH_C, 0f, -2.5f);
        AssertGlyph(unwrapped.Glyphs[2], TextLayoutTestData.GLYPH_C, 2f, -1f);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WordWrapDropsKerningAcrossTheBreak()
    {
        FakeTextMetrics metrics = TextLayoutTestData
            .CreateUniform()
            .Kerning(TextLayoutTestData.GLYPH_B, TextLayoutTestData.GLYPH_C, 1f);
        TextLayoutBuffer buffer = new();

        Arrange(metrics, "ab cd", buffer, TextWrapping.Word, 1.5f, out _);

        AssertGlyph(buffer.Glyphs[2], TextLayoutTestData.GLYPH_C, 0f, -2.5f);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void KerningAppliesBetweenAdjacentGlyphs()
    {
        FakeTextMetrics metrics = TextLayoutTestData
            .CreateUniform()
            .Kerning(TextLayoutTestData.GLYPH_A, TextLayoutTestData.GLYPH_B, 0.25f);
        TextLayoutBuffer buffer = new();

        Arrange(metrics, "ab", buffer, out TextLayoutResult result);

        AssertGlyph(buffer.Glyphs[1], TextLayoutTestData.GLYPH_B, 0.75f, -1f);
        AssertVector(new Vector2(1.25f, 0f), result.BoundsMax);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void KerningAppliesAcrossAWhitespaceGlyphWithinALine()
    {
        FakeTextMetrics metrics = TextLayoutTestData
            .CreateUniform()
            .Kerning(TextLayoutTestData.GLYPH_A, TextLayoutTestData.GLYPH_SPACE, 0.25f)
            .Kerning(TextLayoutTestData.GLYPH_SPACE, TextLayoutTestData.GLYPH_B, 0.5f);
        TextLayoutBuffer buffer = new();

        Arrange(metrics, "a b", buffer, out TextLayoutResult result);

        AssertGlyph(buffer.Glyphs[1], TextLayoutTestData.GLYPH_SPACE, 0.75f, -1f);
        AssertGlyph(buffer.Glyphs[2], TextLayoutTestData.GLYPH_B, 1.75f, -1f);
        AssertVector(new Vector2(2.25f, 0f), result.BoundsMax);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WhitespaceOnlyTextIsOneLine()
    {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        int count = Arrange(metrics, "   ", buffer, out TextLayoutResult result);

        Assert.Equal(3, count);
        Assert.Equal(1, result.LineCount);
        AssertGlyph(buffer.Glyphs[0], TextLayoutTestData.GLYPH_SPACE, 0f, -1f);
        AssertGlyph(buffer.Glyphs[1], TextLayoutTestData.GLYPH_SPACE, 0.5f, -1f);
        AssertGlyph(buffer.Glyphs[2], TextLayoutTestData.GLYPH_SPACE, 1f, -1f);
        AssertVector(new Vector2(1.5f, 0f), result.BoundsMax);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WhitespaceOnlyTextIsOneLineWithWordWrapping()
    {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        int count = Arrange(
            metrics,
            "   ",
            buffer,
            TextWrapping.Word,
            1f,
            out TextLayoutResult result
        );

        Assert.Equal(3, count);
        Assert.Equal(1, result.LineCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WordExactlyFittingANewLineStaysOnIt()
    {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        int count = Arrange(
            metrics,
            "ab cde",
            buffer,
            TextWrapping.Word,
            1.5f,
            out TextLayoutResult result
        );

        Assert.Equal(5, count);
        Assert.Equal(2, result.LineCount);
        AssertGlyph(buffer.Glyphs[2], TextLayoutTestData.GLYPH_C, 0f, -2.5f);
        AssertGlyph(buffer.Glyphs[4], TextLayoutTestData.GLYPH_E, 1f, -2.5f);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WordWrapRestartsAtEachParagraph()
    {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        int count = Arrange(
            metrics,
            "ab cd\nef",
            buffer,
            TextWrapping.Word,
            1.5f,
            out TextLayoutResult result
        );

        Assert.Equal(6, count);
        Assert.Equal(3, result.LineCount);
        AssertGlyph(buffer.Glyphs[0], TextLayoutTestData.GLYPH_A, 0f, -1f);
        AssertGlyph(buffer.Glyphs[2], TextLayoutTestData.GLYPH_C, 0f, -2.5f);
        AssertGlyph(buffer.Glyphs[4], TextLayoutTestData.GLYPH_E, 0f, -4f);
        AssertGlyph(buffer.Glyphs[5], TextLayoutTestData.GLYPH_F, 0.5f, -4f);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EmptyParagraphKeepsItsLine()
    {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        int count = Arrange(
            metrics,
            "ab\n\ncd",
            buffer,
            TextWrapping.Word,
            1.5f,
            out TextLayoutResult result
        );

        Assert.Equal(4, count);
        Assert.Equal(3, result.LineCount);
        AssertGlyph(buffer.Glyphs[2], TextLayoutTestData.GLYPH_C, 0f, -4f);
        AssertGlyph(buffer.Glyphs[3], TextLayoutTestData.GLYPH_D, 0.5f, -4f);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void KerningScalesWithSize()
    {
        FakeTextMetrics metrics = TextLayoutTestData
            .CreateUniform()
            .Kerning(TextLayoutTestData.GLYPH_A, TextLayoutTestData.GLYPH_B, 0.25f);
        TextLayoutBuffer buffer = new();

        int count = Arrange(
            metrics,
            "ab",
            buffer,
            TextWrapping.None,
            0f,
            out TextLayoutResult result,
            size: 2f
        );

        Assert.Equal(2, count);
        Assert.Equal(1, result.LineCount);
        AssertGlyph(buffer.Glyphs[0], TextLayoutTestData.GLYPH_A, 0f, -2f);
        AssertGlyph(buffer.Glyphs[1], TextLayoutTestData.GLYPH_B, 1.5f, -2f);
        AssertVector(new Vector2(0f, -3f), result.BoundsMin);
        AssertVector(new Vector2(2.5f, 0f), result.BoundsMax);
        AssertVector(new Vector2(2.5f, 3f), TextLayoutEngine.Measure(metrics, "ab", 2f));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WordWrappingScalesWithSize()
    {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer unscaled = new();
        TextLayoutBuffer scaled = new();

        Arrange(
            metrics,
            "ab cd",
            unscaled,
            TextWrapping.Word,
            3f,
            out TextLayoutResult unscaledResult
        );
        Arrange(
            metrics,
            "ab cd",
            scaled,
            TextWrapping.Word,
            3f,
            out TextLayoutResult scaledResult,
            size: 2f
        );

        Assert.Equal(1, unscaledResult.LineCount);
        Assert.Equal(5, unscaled.Count);
        Assert.Equal(2, scaledResult.LineCount);
        Assert.Equal(4, scaled.Count);
        AssertGlyph(scaled.Glyphs[2], TextLayoutTestData.GLYPH_C, 0f, -5f);
        AssertGlyph(scaled.Glyphs[3], TextLayoutTestData.GLYPH_D, 1f, -5f);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CharacterWrappingScalesWithSize()
    {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer unscaled = new();
        TextLayoutBuffer scaled = new();

        Arrange(
            metrics,
            "abcd",
            unscaled,
            TextWrapping.Character,
            3f,
            out TextLayoutResult unscaledResult
        );
        Arrange(
            metrics,
            "abcd",
            scaled,
            TextWrapping.Character,
            3f,
            out TextLayoutResult scaledResult,
            size: 2f
        );

        Assert.Equal(1, unscaledResult.LineCount);
        Assert.Equal(4, unscaled.Count);
        Assert.Equal(2, scaledResult.LineCount);
        Assert.Equal(4, scaled.Count);
        AssertGlyph(scaled.Glyphs[3], TextLayoutTestData.GLYPH_D, 0f, -5f);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CharacterWrapDoesNotSplitASurrogatePair()
    {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        int count = Arrange(
            metrics,
            "a\U0001F600b",
            buffer,
            TextWrapping.Character,
            0.5f,
            out TextLayoutResult result
        );

        Assert.Equal(3, count);
        Assert.Equal(3, result.LineCount);
        Assert.Equal(TextLayoutTestData.GLYPH_A, buffer.Glyphs[0].GlyphId);
        Assert.Equal(TextLayoutTestData.GLYPH_EMOJI, buffer.Glyphs[1].GlyphId);
        Assert.Equal(TextLayoutTestData.GLYPH_B, buffer.Glyphs[2].GlyphId);
        AssertGlyph(buffer.Glyphs[1], TextLayoutTestData.GLYPH_EMOJI, 0f, -2.5f);
        AssertGlyph(buffer.Glyphs[2], TextLayoutTestData.GLYPH_B, 0f, -4f);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CharacterWrapCanStartALineWithWhitespace()
    {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        int count = Arrange(
            metrics,
            "a b",
            buffer,
            TextWrapping.Character,
            0.5f,
            out TextLayoutResult result
        );

        Assert.Equal(3, count);
        Assert.Equal(3, result.LineCount);
        AssertGlyph(buffer.Glyphs[0], TextLayoutTestData.GLYPH_A, 0f, -1f);
        AssertGlyph(buffer.Glyphs[1], TextLayoutTestData.GLYPH_SPACE, 0f, -2.5f);
        AssertGlyph(buffer.Glyphs[2], TextLayoutTestData.GLYPH_B, 0f, -4f);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CarriageReturnBeforeNewlineUnderWordWrapping()
    {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        int count = Arrange(
            metrics,
            "ab\r\ncd",
            buffer,
            TextWrapping.Word,
            1.5f,
            out TextLayoutResult result
        );

        Assert.Equal(4, count);
        Assert.Equal(2, result.LineCount);
        AssertGlyph(buffer.Glyphs[0], TextLayoutTestData.GLYPH_A, 0f, -1f);
        AssertGlyph(buffer.Glyphs[1], TextLayoutTestData.GLYPH_B, 0.5f, -1f);
        AssertGlyph(buffer.Glyphs[2], TextLayoutTestData.GLYPH_C, 0f, -2.5f);
        AssertGlyph(buffer.Glyphs[3], TextLayoutTestData.GLYPH_D, 0.5f, -2.5f);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CarriageReturnBeforeNewlineUnderCharacterWrapping()
    {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        int count = Arrange(
            metrics,
            "ab\r\ncd",
            buffer,
            TextWrapping.Character,
            1.2f,
            out TextLayoutResult result
        );

        Assert.Equal(4, count);
        Assert.Equal(2, result.LineCount);
        AssertGlyph(buffer.Glyphs[0], TextLayoutTestData.GLYPH_A, 0f, -1f);
        AssertGlyph(buffer.Glyphs[1], TextLayoutTestData.GLYPH_B, 0.5f, -1f);
        AssertGlyph(buffer.Glyphs[2], TextLayoutTestData.GLYPH_C, 0f, -2.5f);
        AssertGlyph(buffer.Glyphs[3], TextLayoutTestData.GLYPH_D, 0.5f, -2.5f);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CarriageReturnInsideAParagraphDoesNotBreakAWord()
    {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        int count = Arrange(
            metrics,
            "ab\rcd",
            buffer,
            TextWrapping.Word,
            1f,
            out TextLayoutResult result
        );

        // \r is ignored, so "ab\rcd" stays one word and overflows as one line. Without the word-scan
        // carriage-return guard, "ab" and "cd" become separate words and wrap into two lines.
        Assert.Equal(4, count);
        Assert.Equal(1, result.LineCount);
        AssertGlyph(buffer.Glyphs[0], TextLayoutTestData.GLYPH_A, 0f, -1f);
        AssertGlyph(buffer.Glyphs[1], TextLayoutTestData.GLYPH_B, 0.5f, -1f);
        AssertGlyph(buffer.Glyphs[2], TextLayoutTestData.GLYPH_C, 1f, -1f);
        AssertGlyph(buffer.Glyphs[3], TextLayoutTestData.GLYPH_D, 1.5f, -1f);
        AssertVector(new Vector2(1.5f, 0f), result.BoundsMax);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CarriageReturnInsideAParagraphIsNotAGlyph()
    {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        int count = Arrange(
            metrics,
            "ab\rcd",
            buffer,
            TextWrapping.Character,
            100f,
            out TextLayoutResult result
        );

        // The fake maps \r, so a fifth glyph record would appear if the read path did not skip it.
        Assert.Equal(4, count);
        Assert.Equal(1, result.LineCount);
        AssertGlyph(buffer.Glyphs[0], TextLayoutTestData.GLYPH_A, 0f, -1f);
        AssertGlyph(buffer.Glyphs[1], TextLayoutTestData.GLYPH_B, 0.5f, -1f);
        AssertGlyph(buffer.Glyphs[2], TextLayoutTestData.GLYPH_C, 1f, -1f);
        AssertGlyph(buffer.Glyphs[3], TextLayoutTestData.GLYPH_D, 1.5f, -1f);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CharacterWrapBreakCandidateIncludesKerning()
    {
        FakeTextMetrics metrics = TextLayoutTestData
            .CreateUniform()
            .Kerning(TextLayoutTestData.GLYPH_B, TextLayoutTestData.GLYPH_C, -0.5f);
        TextLayoutBuffer buffer = new();

        int count = Arrange(
            metrics,
            "abc",
            buffer,
            TextWrapping.Character,
            1.2f,
            out TextLayoutResult result
        );

        // The b-to-c kern pulls c back to b's pen; the line ends at 1.0 and fits. A candidate that
        // ignored kerning would measure 1.5, break after b, and produce two lines.
        Assert.Equal(3, count);
        Assert.Equal(1, result.LineCount);
        AssertGlyph(buffer.Glyphs[0], TextLayoutTestData.GLYPH_A, 0f, -1f);
        AssertGlyph(buffer.Glyphs[1], TextLayoutTestData.GLYPH_B, 0.5f, -1f);
        AssertGlyph(buffer.Glyphs[2], TextLayoutTestData.GLYPH_C, 0.5f, -1f);
        AssertVector(new Vector2(1f, 1.5f), TextLayoutEngine.Measure(metrics, "abc", 1f));
    }

    private static int Arrange(
        FakeTextMetrics metrics,
        string text,
        TextLayoutBuffer buffer,
        TextWrapping wrapping,
        float maxWidth,
        out TextLayoutResult result,
        float size = 1f
    )
    {
        return TextLayoutEngine.Arrange(
            metrics,
            text,
            size,
            TextAlignment.Left,
            wrapping,
            maxWidth,
            buffer,
            out result
        );
    }

    private static int Arrange(
        FakeTextMetrics metrics,
        string text,
        TextLayoutBuffer buffer,
        out TextLayoutResult result
    )
    {
        return TextLayoutEngine.Arrange(
            metrics,
            text,
            1f,
            TextAlignment.Left,
            TextWrapping.None,
            0f,
            buffer,
            out result
        );
    }

    private static void AssertGlyph(TextGlyphInstance glyph, int glyphId, float x, float y)
    {
        Assert.Equal(glyphId, glyph.GlyphId);
        Assert.Equal(x, glyph.Position.X, TOLERANCE);
        Assert.Equal(y, glyph.Position.Y, TOLERANCE);
    }

    private static void AssertVector(Vector2 expected, Vector2 actual)
    {
        Assert.Equal(expected.X, actual.X, TOLERANCE);
        Assert.Equal(expected.Y, actual.Y, TOLERANCE);
    }
}
