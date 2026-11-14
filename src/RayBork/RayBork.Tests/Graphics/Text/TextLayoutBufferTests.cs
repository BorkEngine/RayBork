using RayBork.Internal;

namespace RayBork.Tests.Graphics.Text;

/// <summary>
/// Tests the reusable glyph buffer: it grows geometrically, never shrinks, exposes exactly the records
/// written by the last call, and makes the steady-state recording path allocation-free.
/// </summary>
public sealed class TextLayoutBufferTests {
    private const string STEADY_STATE_TEXT = "abcde abcde abcde";
    private const int STEADY_STATE_GLYPHS = 17;
    private const float WRAPPED_STEADY_STATE_MAX_WIDTH = 5f;

    /// <summary>
    /// The measured steady-state call count. A warm layout call allocates nothing, so the larger loop
    /// amortizes the one-time runtime allocations a test thread can observe (tiered compilation and GC
    /// bookkeeping) while the budget below stays far below one allocated object per call.
    /// </summary>
    private const int STEADY_STATE_MEASURED_CALLS = 1000;

    /// <summary>
    /// The allocation budget for the measured steady-state loop. Reaching it would take an allocation
    /// every few calls, which a warm layout call never does.
    /// </summary>
    private const int STEADY_STATE_ALLOCATION_BUDGET = 16 * 1024;

    private static readonly string _wrappedSteadyStateText = string.Join(" ", Enumerable.Repeat("abcde", 12));

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
    public void EmptyTextResetsAPrefilledBuffer() {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        Arrange(metrics, "abc", buffer);
        int capacity = buffer.Capacity;
        Assert.Equal(3, buffer.Count);

        int count = Arrange(metrics, string.Empty, buffer);

        Assert.Equal(0, count);
        Assert.Equal(0, buffer.Count);
        Assert.True(buffer.Glyphs.IsEmpty);
        Assert.Equal(capacity, buffer.Capacity);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void BufferGrowsInStepsAndNeverShrinks() {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();

        Arrange(metrics, new string('a', 70), buffer);

        Assert.Equal(70, buffer.Capacity);
        Assert.Equal(70, buffer.Count);

        Arrange(metrics, new string('a', 100), buffer);

        Assert.Equal(140, buffer.Capacity);

        Arrange(metrics, new string('a', 300), buffer);

        Assert.Equal(300, buffer.Capacity);
        Assert.Equal(300, buffer.Count);

        Arrange(metrics, new string('a', 100), buffer);

        Assert.Equal(300, buffer.Capacity);
        Assert.Equal(100, buffer.Count);
        Assert.Equal(100, buffer.Glyphs.Length);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SteadyStateArrangeDoesNotAllocatePerCall() {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();
        ArrangeRepeatedly(
            metrics, STEADY_STATE_TEXT, TextWrapping.None, 0f, buffer, STEADY_STATE_MEASURED_CALLS);
        int capacityAfterWarmUp = buffer.Capacity;

        long before = GC.GetAllocatedBytesForCurrentThread();
        ArrangeRepeatedly(
            metrics, STEADY_STATE_TEXT, TextWrapping.None, 0f, buffer, STEADY_STATE_MEASURED_CALLS);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(
            allocated < STEADY_STATE_ALLOCATION_BUDGET,
            $"The steady-state arrange allocated {allocated} bytes over "
                + $"{STEADY_STATE_MEASURED_CALLS} calls.");
        Assert.Equal(capacityAfterWarmUp, buffer.Capacity);
        Assert.Equal(STEADY_STATE_GLYPHS, buffer.Count);
    }

    [Theory]
    [InlineData(TextWrapping.Word, 60, 12)]
    [InlineData(TextWrapping.Character, 71, 8)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SteadyStateWrappedArrangeDoesNotAllocatePerCall(
        TextWrapping wrapping,
        int expectedGlyphs,
        int expectedLines) {
        FakeTextMetrics metrics = TextLayoutTestData.CreateUniform();
        TextLayoutBuffer buffer = new();
        ArrangeRepeatedly(
            metrics,
            _wrappedSteadyStateText,
            wrapping,
            WRAPPED_STEADY_STATE_MAX_WIDTH,
            buffer,
            STEADY_STATE_MEASURED_CALLS);
        int capacityAfterWarmUp = buffer.Capacity;

        long before = GC.GetAllocatedBytesForCurrentThread();
        TextLayoutResult result = ArrangeRepeatedly(
            metrics,
            _wrappedSteadyStateText,
            wrapping,
            WRAPPED_STEADY_STATE_MAX_WIDTH,
            buffer,
            STEADY_STATE_MEASURED_CALLS);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(
            allocated < STEADY_STATE_ALLOCATION_BUDGET,
            $"The steady-state arrange allocated {allocated} bytes over "
                + $"{STEADY_STATE_MEASURED_CALLS} calls.");
        Assert.Equal(capacityAfterWarmUp, buffer.Capacity);
        Assert.Equal(expectedGlyphs, buffer.Count);
        Assert.Equal(expectedLines, result.LineCount);
    }

    private static TextLayoutResult ArrangeRepeatedly(
        FakeTextMetrics metrics,
        string text,
        TextWrapping wrapping,
        float maxWidth,
        TextLayoutBuffer buffer,
        int calls) {
        TextLayoutResult result = default;
        for (int index = 0; index < calls; index++) {
            TextLayoutEngine.Arrange(
                metrics,
                text,
                1f,
                TextAlignment.Left,
                wrapping,
                maxWidth,
                buffer,
                out result);
        }

        return result;
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
