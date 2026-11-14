using RayBork.Internal;

namespace RayBork.Tests.Graphics.Text;

/// <summary>
/// Tests the documented validation contract of the text layout engine. Invalid input throws before the
/// engine touches the output buffer.
/// </summary>
public sealed class TextLayoutValidationTests {
    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ArrangeRejectsNonPositiveOrNonFiniteSize(float size) {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => TextLayoutEngine.Arrange(
            metrics,
            "a",
            size,
            TextAlignment.Left,
            TextWrapping.None,
            0f,
            buffer,
            out _));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MeasureRejectsNonPositiveOrNonFiniteSize(float size) {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();

        Assert.Throws<ArgumentOutOfRangeException>(() => TextLayoutEngine.Measure(metrics, "a", size));
    }

    [Theory]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ArrangeRejectsNegativeOrNonFiniteMaxWidth(float maxWidth) {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => TextLayoutEngine.Arrange(
            metrics,
            "a",
            1f,
            TextAlignment.Left,
            TextWrapping.None,
            maxWidth,
            buffer,
            out _));
    }

    [Theory]
    [InlineData(TextWrapping.Word, 0f)]
    [InlineData(TextWrapping.Word, -1f)]
    [InlineData(TextWrapping.Character, 0f)]
    [InlineData(TextWrapping.Character, -1f)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WrappingRequiresAPositiveMaxWidth(TextWrapping wrapping, float maxWidth) {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => TextLayoutEngine.Arrange(
            metrics,
            "a",
            1f,
            TextAlignment.Left,
            wrapping,
            maxWidth,
            buffer,
            out _));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ArrangeRejectsAnUndefinedAlignment() {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => TextLayoutEngine.Arrange(
            metrics,
            "a",
            1f,
            (TextAlignment)3,
            TextWrapping.None,
            0f,
            buffer,
            out _));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ArrangeRejectsAnUndefinedWrappingMode() {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => TextLayoutEngine.Arrange(
            metrics,
            "a",
            1f,
            TextAlignment.Left,
            (TextWrapping)3,
            1f,
            buffer,
            out _));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ArrangeRejectsNullMetrics() {
        TextLayoutBuffer buffer = new();

        Assert.Throws<ArgumentNullException>(() => TextLayoutEngine.Arrange(
            null!,
            "a",
            1f,
            TextAlignment.Left,
            TextWrapping.None,
            0f,
            buffer,
            out _));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ArrangeRejectsNullBuffer() {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();

        Assert.Throws<ArgumentNullException>(() => TextLayoutEngine.Arrange(
            metrics,
            "a",
            1f,
            TextAlignment.Left,
            TextWrapping.None,
            0f,
            null!,
            out _));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MeasureRejectsNullMetrics() {
        Assert.Throws<ArgumentNullException>(() => TextLayoutEngine.Measure(null!, "a", 1f));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void InvalidInputLeavesAPrefilledBufferUnchanged() {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();
        TextLayoutEngine.Arrange(
            metrics,
            "abc",
            1f,
            TextAlignment.Left,
            TextWrapping.None,
            0f,
            buffer,
            out _);
        int capacity = buffer.Capacity;

        Assert.Throws<ArgumentOutOfRangeException>(() => TextLayoutEngine.Arrange(
            metrics,
            "a",
            0f,
            TextAlignment.Left,
            TextWrapping.None,
            0f,
            buffer,
            out _));
        AssertPrefilled(buffer, capacity);

        Assert.Throws<ArgumentOutOfRangeException>(() => TextLayoutEngine.Arrange(
            metrics,
            "a",
            1f,
            TextAlignment.Left,
            TextWrapping.Word,
            0f,
            buffer,
            out _));
        AssertPrefilled(buffer, capacity);

        Assert.Throws<ArgumentOutOfRangeException>(() => TextLayoutEngine.Arrange(
            metrics,
            "a",
            1f,
            (TextAlignment)3,
            TextWrapping.None,
            0f,
            buffer,
            out _));
        AssertPrefilled(buffer, capacity);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void BufferRejectsNegativeInitialCapacity() {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextLayoutBuffer(-1));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void BufferRejectsNegativeReserveCount() {
        TextLayoutBuffer buffer = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Reserve(-1));
    }

    private static void AssertPrefilled(TextLayoutBuffer buffer, int capacity) {
        Assert.Equal(3, buffer.Count);
        Assert.Equal(3, buffer.Glyphs.Length);
        Assert.Equal(capacity, buffer.Capacity);
        Assert.Equal(TextLayoutTestData.GLYPH_A, buffer.Glyphs[0].GlyphId);
        Assert.Equal(TextLayoutTestData.GLYPH_B, buffer.Glyphs[1].GlyphId);
        Assert.Equal(TextLayoutTestData.GLYPH_C, buffer.Glyphs[2].GlyphId);
    }
}
