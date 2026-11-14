using System.Numerics;
using RayBork.Internal;

namespace RayBork.Tests.Graphics.Text;

/// <summary>
/// Tests <see cref="TextLayoutEngine.Measure"/>: the widest line advance including kerning and the
/// line-count based height. Measurement ignores wrapping and a width limit and needs no glyph texture.
/// </summary>
public sealed class TextLayoutMeasurementTests {
    private const float TOLERANCE = 1e-5f;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MeasureSingleLine() {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();

        Vector2 size = TextLayoutEngine.Measure(metrics, "ab", 1f);

        AssertVector(new Vector2(1f, 1.5f), size);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MeasureIncludesKerning() {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform()
            .Kerning(TextLayoutTestData.GLYPH_A, TextLayoutTestData.GLYPH_B, 0.25f);

        Vector2 size = TextLayoutEngine.Measure(metrics, "ab", 1f);

        AssertVector(new Vector2(1.25f, 1.5f), size);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MeasureMultiLineUsesTheWidestLine() {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();

        Vector2 size = TextLayoutEngine.Measure(metrics, "ab\ncde", 1f);

        AssertVector(new Vector2(1.5f, 3f), size);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MeasureTrailingNewlineAddsAnEmptyLine() {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();

        Vector2 size = TextLayoutEngine.Measure(metrics, "ab\n", 1f);

        AssertVector(new Vector2(1f, 3f), size);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MeasureEmptyTextIsZero() {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();

        Vector2 size = TextLayoutEngine.Measure(metrics, string.Empty, 1f);

        AssertVector(Vector2.Zero, size);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MeasureAtRequestedSize() {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();

        Vector2 size = TextLayoutEngine.Measure(metrics, "ab", 2f);

        AssertVector(new Vector2(2f, 3f), size);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MeasureIgnoresCarriageReturn() {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();

        Vector2 size = TextLayoutEngine.Measure(metrics, "a\rb", 1f);

        AssertVector(new Vector2(1f, 1.5f), size);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MeasureWhitespaceOnlyText() {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();

        Vector2 size = TextLayoutEngine.Measure(metrics, "   ", 1f);

        AssertVector(new Vector2(1.5f, 1.5f), size);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MeasureUnpairedSurrogateAsTheReplacementGlyph() {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();

        Vector2 size = TextLayoutEngine.Measure(metrics, "\uD83D", 1f);

        AssertVector(new Vector2(0.5f, 1.5f), size);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MeasureConsecutiveNewlines() {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();

        Vector2 size = TextLayoutEngine.Measure(metrics, "\n\n", 1f);

        AssertVector(new Vector2(0f, 4.5f), size);
    }

    private static void AssertVector(Vector2 expected, Vector2 actual) {
        Assert.Equal(expected.X, actual.X, TOLERANCE);
        Assert.Equal(expected.Y, actual.Y, TOLERANCE);
    }
}
