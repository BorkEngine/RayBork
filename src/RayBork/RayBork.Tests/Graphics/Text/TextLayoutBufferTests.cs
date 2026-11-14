using RayBork.Internal;

namespace RayBork.Tests.Graphics.Text;

/// <summary>
/// Tests the reusable glyph buffer: it grows once, never shrinks, exposes exactly the records written
/// by the last call, and makes the steady-state recording path allocation-free.
/// </summary>
public sealed class TextLayoutBufferTests {
    private const string STEADY_STATE_TEXT = "abcde abcde abcde";
    private const int STEADY_STATE_GLYPHS = 17;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void BufferIsReusedAcrossCalls() {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        int first = Arrange(metrics, "a", buffer);

        Assert.Equal(1, first);
        Assert.Equal(1, buffer.Count);
        Assert.Equal(1, buffer.Glyphs.Length);
        Assert.Equal(TextLayoutTestData.GLYPH_A, buffer.Glyphs[0].GlyphId);

        int second = Arrange(metrics, "abc", buffer);
        int capacity = buffer.Capacity;

        Assert.Equal(3, second);
        Assert.True(capacity >= 3);
        Assert.Equal(3, buffer.Count);
        Assert.Equal(3, buffer.Glyphs.Length);
        Assert.Equal(TextLayoutTestData.GLYPH_A, buffer.Glyphs[0].GlyphId);
        Assert.Equal(TextLayoutTestData.GLYPH_B, buffer.Glyphs[1].GlyphId);
        Assert.Equal(TextLayoutTestData.GLYPH_C, buffer.Glyphs[2].GlyphId);

        int third = Arrange(metrics, "a", buffer);

        Assert.Equal(1, third);
        Assert.Equal(1, buffer.Count);
        Assert.Equal(1, buffer.Glyphs.Length);
        Assert.Equal(capacity, buffer.Capacity);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void BufferWithInitialCapacityDoesNotGrow() {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new(8);

        Arrange(metrics, "ab", buffer);

        Assert.Equal(8, buffer.Capacity);
        Assert.Equal(2, buffer.Count);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SteadyStateArrangeAllocatesNothing() {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();
        for (int i = 0; i < 100; i++) {
            TextLayoutEngine.Arrange(
                metrics,
                STEADY_STATE_TEXT,
                1f,
                TextAlignment.Left,
                TextWrapping.None,
                0f,
                buffer,
                out _);
        }
        int capacityAfterWarmUp = buffer.Capacity;

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) {
            TextLayoutEngine.Arrange(
                metrics,
                STEADY_STATE_TEXT,
                1f,
                TextAlignment.Left,
                TextWrapping.None,
                0f,
                buffer,
                out _);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0L, allocated);
        Assert.Equal(capacityAfterWarmUp, buffer.Capacity);
        Assert.Equal(STEADY_STATE_GLYPHS, buffer.Count);
    }

    private static int Arrange(FakeTextMetrics metrics, string text, TextLayoutBuffer buffer) {
        return TextLayoutEngine.Arrange(
            metrics,
            text,
            1f,
            TextAlignment.Left,
            TextWrapping.None,
            0f,
            buffer,
            out _);
    }
}
