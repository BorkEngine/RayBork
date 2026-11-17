using System.Numerics;
using RayBork.Internal;

namespace RayBork.Tests.Fonts.Msdf;

/// <summary>Decodes and asserts the RGBA8 distance field of the MSDF generator tests.</summary>
internal static class MsdfTestField
{
    /// <summary>
    /// The documented median reconstruction tolerance as a fraction of the range: 1.5 of 255 byte
    /// steps. It covers the byte quantization and the float distance arithmetic.
    /// </summary>
    internal const float BYTE_TOLERANCE = 1.5f / 255f;

    /// <summary>Gets the median reconstruction tolerance in texels for one range.</summary>
    internal static float Tolerance(int range) => BYTE_TOLERANCE * range;

    /// <summary>Reconstructs the signed distance of one texel, in texels.</summary>
    internal static float Reconstruct(byte[] bitmap, int width, int texelX, int texelY, int range)
    {
        int index = ((texelY * width) + texelX) * 4;
        return (Median(bitmap[index], bitmap[index + 1], bitmap[index + 2]) - 0.5f) * range;
    }

    /// <summary>Computes the median of the three normalized channel values.</summary>
    internal static float Median(byte red, byte green, byte blue)
    {
        float r = red / 255f;
        float g = green / 255f;
        float b = blue / 255f;
        return MathF.Max(MathF.Min(r, g), MathF.Min(MathF.Max(r, g), b));
    }

    /// <summary>Gets the median normalized channel value of one texel.</summary>
    internal static float MedianAt(byte[] bitmap, in MsdfGlyphLayout layout, int texelX, int texelY)
    {
        int index = ((texelY * layout.TextureWidth) + texelX) * 4;
        return Median(bitmap[index], bitmap[index + 1], bitmap[index + 2]);
    }

    /// <summary>Asserts that every texel is opaque.</summary>
    internal static void AssertOpaque(byte[] bitmap)
    {
        for (int index = 3; index < bitmap.Length; index += 4)
        {
            Assert.Equal(255, bitmap[index]);
        }
    }

    /// <summary>Asserts that the field contains at least two different texel colours.</summary>
    internal static void AssertNotConstant(byte[] bitmap)
    {
        for (int index = 4; index < bitmap.Length; index += 4)
        {
            if (
                bitmap[index] != bitmap[0]
                || bitmap[index + 1] != bitmap[1]
                || bitmap[index + 2] != bitmap[2]
            )
            {
                return;
            }
        }

        Assert.Fail("The distance field is constant.");
    }

    /// <summary>Asserts that every texel is the outside extreme (black and opaque).</summary>
    internal static void AssertAllOutside(byte[] bitmap)
    {
        for (int index = 0; index < bitmap.Length; index += 4)
        {
            Assert.Equal(0, bitmap[index]);
            Assert.Equal(0, bitmap[index + 1]);
            Assert.Equal(0, bitmap[index + 2]);
            Assert.Equal(255, bitmap[index + 3]);
        }
    }

    /// <summary>
    /// Asserts that every texel reconstructs the exact signed distance to the contours, using the
    /// production non-zero winding rule.
    /// </summary>
    internal static void AssertMatchesContours(
        byte[] bitmap,
        in MsdfGlyphLayout layout,
        Vector2[][] contours,
        float extraTolerance = 0f
    ) =>
        AssertFieldMatches(
            bitmap,
            layout,
            point => MsdfTestShapes.SignedDistanceToContours(contours, point),
            extraTolerance
        );

    /// <summary>
    /// Asserts that every texel reconstructs the exact signed distance to the contours, using an
    /// even-odd fill rule that is independent of the production winding rule. The analytic test shapes
    /// are simple or nested, so both rules classify them identically.
    /// </summary>
    internal static void AssertMatchesEvenOddContours(
        byte[] bitmap,
        in MsdfGlyphLayout layout,
        Vector2[][] contours,
        float extraTolerance = 0f
    ) =>
        AssertFieldMatches(
            bitmap,
            layout,
            point => MsdfTestShapes.SignedDistanceToEvenOddContours(contours, point),
            extraTolerance
        );

    /// <summary>
    /// Asserts that every texel reconstructs the exact signed distance to an axis-aligned rectangle in
    /// texture pixel space. The rectangle is given directly, so this oracle shares neither the layout
    /// transform nor the production winding rule with the generator.
    /// </summary>
    internal static void AssertMatchesPixelRectangle(
        byte[] bitmap,
        in MsdfGlyphLayout layout,
        float left,
        float top,
        float right,
        float bottom
    ) =>
        AssertFieldMatches(
            bitmap,
            layout,
            point =>
            {
                if (point.X > left && point.X < right && point.Y > top && point.Y < bottom)
                {
                    return MathF.Min(
                        MathF.Min(point.X - left, right - point.X),
                        MathF.Min(point.Y - top, bottom - point.Y)
                    );
                }

                float deltaX = MathF.Max(MathF.Max(left - point.X, point.X - right), 0f);
                float deltaY = MathF.Max(MathF.Max(top - point.Y, point.Y - bottom), 0f);
                return -MathF.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
            },
            0f
        );

    /// <summary>Asserts that one texel reconstructs the expected signed distance.</summary>
    internal static void AssertTexelNear(
        byte[] bitmap,
        in MsdfGlyphLayout layout,
        int texelX,
        int texelY,
        float expected,
        float tolerance
    )
    {
        float actual = Reconstruct(bitmap, layout.TextureWidth, texelX, texelY, layout.Range);
        Assert.True(
            MathF.Abs(actual - expected) <= tolerance,
            $"Texel ({texelX}, {texelY}) reconstructed {actual} but the expected distance is {expected}."
        );
    }

    private static void AssertFieldMatches(
        byte[] bitmap,
        in MsdfGlyphLayout layout,
        Func<Vector2, float> signedDistance,
        float extraTolerance
    )
    {
        Assert.Equal(layout.TextureWidth * layout.TextureHeight * 4, bitmap.Length);
        AssertOpaque(bitmap);
        float tolerance = Tolerance(layout.Range) + extraTolerance;
        float halfRange = layout.Range * 0.5f;
        for (int y = 0; y < layout.TextureHeight; y++)
        {
            for (int x = 0; x < layout.TextureWidth; x++)
            {
                Vector2 texelCentre = new(x + 0.5f, y + 0.5f);
                float expected = Math.Clamp(signedDistance(texelCentre), -halfRange, halfRange);
                float actual = Reconstruct(bitmap, layout.TextureWidth, x, y, layout.Range);
                Assert.True(
                    MathF.Abs(actual - expected) <= tolerance,
                    $"Texel ({x}, {y}) reconstructed {actual} but the exact distance is {expected}."
                );
            }
        }
    }
}
