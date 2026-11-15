using System.Numerics;
using RayBork.Internal;

namespace RayBork.Tests;

/// <summary>
/// GPU integration tests for the M6 camera text draws: they record the production
/// <see cref="Camera.DrawText"/> helper over the built-in Fira Sans font, submit the sealed camera
/// packet through <see cref="Vulkan12Backend"/>, and assert validation-layer cleanliness, render
/// statistics, and stable linear scene-target pixel probes.
/// </summary>
/// <remarks>
/// <para>
/// Every test loads <c>/fonts/fira-sans</c> from the built-in pack through the real catalog loader and
/// warms the glyph textures through the real generation service. The waits pump the GPU upload bridge
/// instead of sleeping, and the pixel probes read single pixels with explicit tolerances. A glyph
/// texture is one padded quad, so the probes sit on deterministic interior pixels: the capital L's
/// bottom arm and its mirrored blank for the orientation check, and the capital I's stem center for the
/// occlusion and batching checks.
/// </para>
/// <para>
/// The tests run in parallel; <see cref="SdlPlatform"/> serializes the process-global SDL and Vulkan
/// loader setup. They use a single-sample target so a probe pixel is not resolved from multiple
/// samples.
/// </para>
/// </remarks>
public sealed class TextDrawGpuTests {
    private static readonly Color _clearColor = new(0.05f, 0.1f, 0.2f, 1f);
    private static readonly Color _textColor = new(0.9f, 0.35f, 0.1f, 1f);
    private static readonly Color _occluderColor = new(0.2f, 0.8f, 0.4f, 1f);
    private const float COLOR_TOLERANCE = 2e-3f;
    private const float INK_TOLERANCE = 2e-2f;
    private const float TEXT_SIZE = 0.5f;
    private const float ORTHOGRAPHIC_HEIGHT = 2f;
    private const float TEXT_Z = -4f;
    private const string SAMPLE_TEXT = "HI";
    private const string ORIENTATION_TEXT = "L";
    private const int BACKGROUND_MARGIN = 20;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void DrawTextRendersItsGlyphsAtTheExpectedScreenAreaAndOrientation() {
        using PrimitiveDrawGpuHarness harness = PrimitiveDrawGpuHarness.CreateOrSkip(sampleCount: 1);
        Font font = harness.LoadFont();
        harness.WarmGlyphs(font, ORIENTATION_TEXT);
        (TextLayoutResult layout, TextGlyphInstance[] glyphs) = ArrangeText(font, ORIENTATION_TEXT);
        Transform transform = CenteredTransform(layout, TEXT_Z);
        InkArea blockArea = default;
        InkArea glyphArea = default;
        harness.SubmitOrthographic(
            camera => {
                camera.DrawText(font, ORIENTATION_TEXT, transform, TEXT_SIZE, _textColor);
                blockArea = ScreenArea(camera, harness, layout.BoundsMin, layout.BoundsMax, transform);
                glyphArea = ScreenArea(
                    camera,
                    harness,
                    glyphs[0].Position,
                    glyphs[0].Position + glyphs[0].Size,
                    transform);
            },
            transform: null,
            clearColor: _clearColor,
            height: ORTHOGRAPHIC_HEIGHT);

        RenderStats stats = harness.Host.Backend.Stats;
        Assert.Equal(1, stats.DrawCalls);
        Assert.Equal(glyphs.Length, stats.Instances);
        Assert.Equal(glyphs.Length * 2, stats.Triangles);
        Assert.Equal(0, stats.CulledInstances);

        // L's horizontal arm is its bottom feature. A vertically flipped or transposed glyph texture
        // would not put ink over the arm's far end, and the mirrored probe inside the same quad would
        // not be blank.
        uint inkX = Pixel(glyphArea.MinX + ((glyphArea.MaxX - glyphArea.MinX) * 0.7f));
        uint inkY = Pixel(glyphArea.MaxY - ((glyphArea.MaxY - glyphArea.MinY) * 0.1f));
        uint mirroredY = Pixel(glyphArea.MinY + ((glyphArea.MaxY - glyphArea.MinY) * 0.1f));
        AssertColorNear(_textColor, harness.ReadColor(inkX, inkY), INK_TOLERANCE);
        AssertColorNear(_clearColor, harness.ReadColor(inkX, mirroredY), INK_TOLERANCE);

        // The probes outside the text block stay the clear color.
        uint backgroundX = blockArea.MinX >= BACKGROUND_MARGIN
            ? checked((uint)(blockArea.MinX - BACKGROUND_MARGIN))
            : checked((uint)(blockArea.MaxX + BACKGROUND_MARGIN));
        AssertColorNear(_clearColor, harness.ReadColor(backgroundX, inkY));
        AssertColorNear(_clearColor, harness.ReadColor(2, 2));
        Assert.Empty(harness.Host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void AnOpaqueQuadInFrontOfTheTextHidesIt() {
        using PrimitiveDrawGpuHarness harness = PrimitiveDrawGpuHarness.CreateOrSkip(sampleCount: 1);
        Font font = harness.LoadFont();
        harness.WarmGlyphs(font, SAMPLE_TEXT);
        (TextLayoutResult layout, TextGlyphInstance[] glyphs) = ArrangeText(font, SAMPLE_TEXT);
        Transform transform = CenteredTransform(layout, TEXT_Z);
        (uint X, uint Y) ink = default;
        harness.SubmitOrthographic(
            camera => {
                camera.DrawText(font, SAMPLE_TEXT, transform, TEXT_SIZE, _textColor);
                ink = GlyphCenterPixel(camera, glyphs[1], transform);
            },
            transform: null,
            clearColor: _clearColor,
            height: ORTHOGRAPHIC_HEIGHT);
        AssertColorNear(_textColor, harness.ReadColor(ink.X, ink.Y), INK_TOLERANCE);

        // The point is a camera-facing opaque quad with depth writes, recorded one world unit in front
        // of the text. The text's greater-or-equal depth test rejects every covered fragment.
        harness.SubmitOrthographic(
            camera => {
                camera.DrawText(font, SAMPLE_TEXT, transform, TEXT_SIZE, _textColor);
                camera.DrawPoint(new Vector3(0f, 0f, TEXT_Z + 1f), _occluderColor, 2f);
            },
            transform: null,
            clearColor: _clearColor,
            height: ORTHOGRAPHIC_HEIGHT);

        RenderStats stats = harness.Host.Backend.Stats;
        Assert.Equal(2, stats.DrawCalls);
        Assert.Equal(glyphs.Length + 1, stats.Instances);
        Assert.Equal((glyphs.Length * 2) + 2, stats.Triangles);
        AssertColorNear(_occluderColor, harness.ReadColor(ink.X, ink.Y));
        Assert.Empty(harness.Host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void TwoStringsInTheSameFrameAndFontReportOneDrawCall() {
        using PrimitiveDrawGpuHarness harness = PrimitiveDrawGpuHarness.CreateOrSkip(sampleCount: 1);
        Font font = harness.LoadFont();
        harness.WarmGlyphs(font, SAMPLE_TEXT);
        (TextLayoutResult layout, TextGlyphInstance[] glyphs) = ArrangeText(font, SAMPLE_TEXT);
        Transform first = CenteredTransform(layout, TEXT_Z);
        Transform second = first.WithPosition(first.Position + new Vector3(1f, 0f, 0f));
        (uint X, uint Y) firstInk = default;
        (uint X, uint Y) secondInk = default;
        harness.SubmitOrthographic(
            camera => {
                camera.DrawText(font, SAMPLE_TEXT, first, TEXT_SIZE, _textColor);
                camera.DrawText(font, SAMPLE_TEXT, second, TEXT_SIZE, _textColor);
                firstInk = GlyphCenterPixel(camera, glyphs[1], first);
                secondInk = GlyphCenterPixel(camera, glyphs[1], second);
            },
            transform: null,
            clearColor: _clearColor,
            height: ORTHOGRAPHIC_HEIGHT);

        RenderStats stats = harness.Host.Backend.Stats;
        Assert.Equal(1, stats.DrawCalls);
        Assert.Equal(glyphs.Length * 2, stats.Instances);
        Assert.Equal(glyphs.Length * 4, stats.Triangles);
        Assert.Equal(0, stats.CulledInstances);
        AssertColorNear(_textColor, harness.ReadColor(firstInk.X, firstInk.Y), INK_TOLERANCE);
        AssertColorNear(_textColor, harness.ReadColor(secondInk.X, secondInk.Y), INK_TOLERANCE);
        Assert.Empty(harness.Host.Instance.ConsumeValidationErrors());
    }

    /// <summary>
    /// Lays the text out with the engine layout engine over the loaded font's metrics.
    /// </summary>
    private static (TextLayoutResult Result, TextGlyphInstance[] Glyphs) ArrangeText(Font font, string text) {
        TextLayoutBuffer buffer = new();
        TextLayoutEngine.Arrange(
            font.Metrics,
            text,
            TEXT_SIZE,
            TextAlignment.Left,
            TextWrapping.None,
            0f,
            buffer,
            out TextLayoutResult result);
        return (result, buffer.Glyphs.ToArray());
    }

    /// <summary>
    /// Places the layout origin so the block's layout bounds are centered on the world origin in the
    /// plane at <paramref name="z"/>.
    /// </summary>
    private static Transform CenteredTransform(TextLayoutResult layout, float z) {
        Vector2 center = (layout.BoundsMin + layout.BoundsMax) * 0.5f;
        return new Transform(new Vector3(-center.X, -center.Y, z));
    }

    /// <summary>
    /// Gets the center pixel of one laid-out glyph's padded quad.
    /// </summary>
    private static (uint X, uint Y) GlyphCenterPixel(Camera camera, TextGlyphInstance glyph, Transform transform) {
        Vector2 local = glyph.Position + (glyph.Size * 0.5f);
        Vector2 screen = camera.WorldToScreen(transform.TransformPoint(new Vector3(local, 0f)));
        return (Pixel(screen.X), Pixel(screen.Y));
    }

    /// <summary>
    /// Gets the clamped screen-pixel rectangle of one local layout rectangle.
    /// </summary>
    private static InkArea ScreenArea(
        Camera camera,
        PrimitiveDrawGpuHarness harness,
        Vector2 localMin,
        Vector2 localMax,
        Transform transform) {
        Vector2 minimum = new(float.PositiveInfinity);
        Vector2 maximum = new(float.NegativeInfinity);
        for (int corner = 0; corner < 4; corner++) {
            Vector2 local = new(
                (corner & 1) == 0 ? localMin.X : localMax.X,
                (corner & 2) == 0 ? localMin.Y : localMax.Y);
            Vector2 screen = camera.WorldToScreen(transform.TransformPoint(new Vector3(local, 0f)));
            minimum = Vector2.Min(minimum, screen);
            maximum = Vector2.Max(maximum, screen);
        }

        int width = checked((int)harness.Width);
        int height = checked((int)harness.Height);
        return new InkArea(
            Math.Clamp((int)MathF.Floor(minimum.X), 0, width - 1),
            Math.Clamp((int)MathF.Floor(minimum.Y), 0, height - 1),
            Math.Clamp((int)MathF.Ceiling(maximum.X), 0, width - 1),
            Math.Clamp((int)MathF.Ceiling(maximum.Y), 0, height - 1));
    }

    private static uint Pixel(float coordinate) => checked((uint)MathF.Floor(coordinate));

    private static void AssertColorNear(Color expected, (float R, float G, float B) actual, float tolerance = COLOR_TOLERANCE) {
        Assert.True(
            MathF.Abs(expected.R - actual.R) <= tolerance,
            $"Red {actual.R} is not within {tolerance} of {expected.R}.");
        Assert.True(
            MathF.Abs(expected.G - actual.G) <= tolerance,
            $"Green {actual.G} is not within {tolerance} of {expected.G}.");
        Assert.True(
            MathF.Abs(expected.B - actual.B) <= tolerance,
            $"Blue {actual.B} is not within {tolerance} of {expected.B}.");
    }

    private readonly record struct InkArea(int MinX, int MinY, int MaxX, int MaxY);
}
