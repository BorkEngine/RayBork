using System.Globalization;
using System.Numerics;

namespace RayBork.Tests;

public sealed class ColorTests {
    private const float TOLERANCE = 1e-5f;
    private const float OKLAB_TOLERANCE = 1e-4f;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ConstructorStoresLinearComponents() {
        Color color = new(0.1f, 0.2f, 0.3f, 0.4f);

        Assert.Equal(0.1f, color.R);
        Assert.Equal(0.2f, color.G);
        Assert.Equal(0.3f, color.B);
        Assert.Equal(0.4f, color.A);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ThreeComponentConstructorDefaultsAlphaToOne() {
        Color color = new(0.1f, 0.2f, 0.3f);

        Assert.Equal(0.1f, color.R);
        Assert.Equal(0.2f, color.G);
        Assert.Equal(0.3f, color.B);
        Assert.Equal(1f, color.A);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void IndexerReturnsComponentsAndThrowsOutOfRange() {
        Color color = new(1f, 2f, 3f, 4f);

        Assert.Equal(1f, color[0]);
        Assert.Equal(2f, color[1]);
        Assert.Equal(3f, color[2]);
        Assert.Equal(4f, color[3]);
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = color[-1]; });
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = color[4]; });
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void OperatorsWorkComponentWise() {
        Color left = new(1f, 2f, 3f, 4f);
        Color right = new(5f, 6f, 7f, 8f);

        Assert.Equal(new Color(6f, 8f, 10f, 12f), left + right);
        Assert.Equal(new Color(-4f, -4f, -4f, -4f), left - right);
        Assert.Equal(new Color(5f, 12f, 21f, 32f), left * right);
        Assert.Equal(new Color(0.5f, 1f, 1.5f, 2f), left * 0.5f);
        Assert.Equal(new Color(0.5f, 1f, 1.5f, 2f), 0.5f * left);
        Assert.Equal(new Color(2f, 4f, 6f, 8f), left / 0.5f);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ImplicitVector4ConversionsRoundTrip() {
        Color color = new(0.1f, 0.2f, 0.3f, 0.4f);

        Vector4 vector = color;
        Color roundTrip = vector;

        Assert.Equal(new Vector4(0.1f, 0.2f, 0.3f, 0.4f), vector);
        Assert.Equal(color, roundTrip);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EqualityIsExactPerComponent() {
        Color color = new(0.1f, 0.2f, 0.3f, 0.4f);
        Color same = new(0.1f, 0.2f, 0.3f, 0.4f);
        Color different = new(0.1f, 0.2f, 0.3f, 0.400001f);

        Assert.True(color.Equals(same));
        Assert.True(same.Equals(color));
        Assert.False(color.Equals(different));
        Assert.True(color.Equals((object)same));
        Assert.False(color.Equals(null));
        Assert.False(color.Equals("not a color"));
        Assert.Equal(color.GetHashCode(), same.GetHashCode());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PaletteUsesLinearPresetValues() {
        Assert.Equal(new Color(0f, 0f, 0f, 0f), Color.Clear);
        Assert.Equal(new Color(0f, 0f, 0f, 0f), Color.Transparent);
        Assert.Equal(new Color(0f, 0f, 0f, 1f), Color.Black);
        Assert.Equal(new Color(1f, 1f, 1f, 1f), Color.White);
        Assert.Equal(new Color(1f, 0f, 0f, 1f), Color.Red);
        Assert.Equal(new Color(0f, 1f, 0f, 1f), Color.Green);
        Assert.Equal(new Color(0f, 0f, 1f, 1f), Color.Blue);
        Assert.Equal(new Color(1f, 1f, 0f, 1f), Color.Yellow);
        Assert.Equal(new Color(0f, 1f, 1f, 1f), Color.Cyan);
        Assert.Equal(new Color(1f, 0f, 1f, 1f), Color.Magenta);
        Assert.Equal(new Color(0.5f, 0.5f, 0.5f, 1f), Color.Gray);
        Assert.Equal(new Color(0.5f, 0.5f, 0.5f, 1f), Color.Grey);
        Assert.Equal(new Color(0f, 0.5f, 0.5f, 1f), Color.DarkCyan);
        Assert.Equal(Color.Gray, Color.Grey);
        Assert.Equal(default(Color), Color.Clear);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GrayscaleUsesPerceivedLuminanceWeights() {
        Assert.Equal(0f, Color.Black.Grayscale);
        Assert.Equal(1f, Color.White.Grayscale);
        Assert.Equal(0.299f, new Color(1f, 0f, 0f).Grayscale);
        Assert.Equal(0.587f, new Color(0f, 1f, 0f).Grayscale);
        Assert.Equal(0.114f, new Color(0f, 0f, 1f).Grayscale);
        Assert.Equal(0.5f, new Color(0.5f, 0.5f, 0.5f).Grayscale, TOLERANCE);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MaxColorComponentIncludesAlpha() {
        Assert.Equal(0.9f, new Color(0.1f, 0.2f, 0.3f, 0.9f).MaxColorComponent);
        Assert.Equal(1f, Color.White.MaxColorComponent);
        Assert.Equal(0f, Color.Clear.MaxColorComponent);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void LerpClampsWhileLerpUnclampedExtrapolates() {
        Color black = new(0f, 0f, 0f, 1f);
        Color white = new(1f, 1f, 1f, 1f);

        Assert.Equal(new Color(0.5f, 0.5f, 0.5f, 1f), Color.Lerp(black, white, 0.5f));
        Assert.Equal(new Color(0f, 0f, 0f, 1f), Color.Lerp(black, white, -1f));
        Assert.Equal(new Color(1f, 1f, 1f, 1f), Color.Lerp(black, white, 2f));
        Assert.Equal(new Color(2f, 2f, 2f, 1f), Color.LerpUnclamped(black, white, 2f));
        Assert.Equal(new Color(-1f, -1f, -1f, 1f), Color.LerpUnclamped(black, white, -1f));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void OkLabMatchesKnownValues() {
        Color.White.ToOkLab(out float whiteL, out float whiteA, out float whiteB);
        Assert.Equal(1f, whiteL, OKLAB_TOLERANCE);
        Assert.Equal(0f, whiteA, OKLAB_TOLERANCE);
        Assert.Equal(0f, whiteB, OKLAB_TOLERANCE);

        new Color(0.5f, 0.5f, 0.5f).ToOkLab(out float grayL, out float grayA, out float grayB);
        Assert.Equal(0.7937005f, grayL, OKLAB_TOLERANCE);
        Assert.Equal(0f, grayA, OKLAB_TOLERANCE);
        Assert.Equal(0f, grayB, OKLAB_TOLERANCE);

        Color.Red.ToOkLab(out float redL, out float redA, out float redB);
        Assert.Equal(0.6279554f, redL, OKLAB_TOLERANCE);
        Assert.Equal(0.2248631f, redA, OKLAB_TOLERANCE);
        Assert.Equal(0.1258463f, redB, OKLAB_TOLERANCE);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void OkLabRoundTripsLinearColors() {
        Color color = new(0.2f, 0.6f, 0.3f, 0.4f);

        color.ToOkLab(out float l, out float a, out float b);
        Color roundTrip = Color.FromOkLab(l, a, b, color.A);

        Assert.Equal(color.R, roundTrip.R, TOLERANCE);
        Assert.Equal(color.G, roundTrip.G, TOLERANCE);
        Assert.Equal(color.B, roundTrip.B, TOLERANCE);
        Assert.Equal(color.A, roundTrip.A, TOLERANCE);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void LerpOkLabInterpolatesPerceptuallyAndClamps() {
        Color middle = Color.LerpOkLab(Color.Black, Color.White, 0.5f);
        Assert.Equal(0.125f, middle.R, TOLERANCE);
        Assert.Equal(0.125f, middle.G, TOLERANCE);
        Assert.Equal(0.125f, middle.B, TOLERANCE);
        Assert.Equal(1f, middle.A);

        AssertColorEqual(Color.Red, Color.LerpOkLab(Color.Red, Color.Blue, -1f));
        AssertColorEqual(Color.Blue, Color.LerpOkLab(Color.Red, Color.Blue, 2f));

        Color extrapolated = Color.LerpOkLabUnclamped(Color.Black, Color.White, 2f);
        Assert.Equal(8f, extrapolated.R, 1e-3f);
        Assert.Equal(8f, extrapolated.G, 1e-3f);
        Assert.Equal(8f, extrapolated.B, 1e-3f);
    }

    [Theory]
    [InlineData(0f, 1f, 0f, 0f)]
    [InlineData(1f / 6f, 1f, 1f, 0f)]
    [InlineData(1f / 3f, 0f, 1f, 0f)]
    [InlineData(0.5f, 0f, 1f, 1f)]
    [InlineData(2f / 3f, 0f, 0f, 1f)]
    [InlineData(5f / 6f, 1f, 0f, 1f)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void HSVToRGBProducesThePrimarySectors(
        float hue,
        float expectedRed,
        float expectedGreen,
        float expectedBlue) {
        Color color = Color.HSVToRGB(hue, 1f, 1f);

        Assert.Equal(expectedRed, color.R, TOLERANCE);
        Assert.Equal(expectedGreen, color.G, TOLERANCE);
        Assert.Equal(expectedBlue, color.B, TOLERANCE);
        Assert.Equal(1f, color.A);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void HSVToRGBWrapsHueAndHandlesZeroSaturation() {
        Assert.Equal(Color.Red, Color.HSVToRGB(1f, 1f, 1f));
        Assert.Equal(Color.Magenta, Color.HSVToRGB(-1f / 6f, 1f, 1f));
        Assert.Equal(Color.Cyan, Color.HSVToRGB(1.5f, 1f, 1f));
        Assert.Equal(new Color(0.25f, 0.25f, 0.25f, 1f), Color.HSVToRGB(0.4f, 0f, 0.25f));
        Assert.Equal(new Color(0f, 0f, 0f, 1f), Color.HSVToRGB(0.4f, 1f, 0f));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void HSVToRGBSupportsHdrValues() {
        Color hdr = Color.HSVToRGB(0f, 1f, 2f);
        Color clamped = Color.HSVToRGB(0f, 1f, 2f, false);

        Assert.Equal(2f, hdr.R);
        Assert.Equal(0f, hdr.G);
        Assert.Equal(0f, hdr.B);
        Assert.Equal(1f, hdr.A);

        Assert.Equal(1f, clamped.R);
        Assert.Equal(0f, clamped.G);
        Assert.Equal(0f, clamped.B);
    }

    [Theory]
    [InlineData(1f, 0f, 0f, 0f, 1f, 1f)]
    [InlineData(0f, 1f, 0f, 1f / 3f, 1f, 1f)]
    [InlineData(0f, 0f, 1f, 2f / 3f, 1f, 1f)]
    [InlineData(1f, 1f, 0f, 1f / 6f, 1f, 1f)]
    [InlineData(0f, 1f, 1f, 0.5f, 1f, 1f)]
    [InlineData(1f, 0f, 1f, 5f / 6f, 1f, 1f)]
    [InlineData(1f, 1f, 1f, 0f, 0f, 1f)]
    [InlineData(0f, 0f, 0f, 0f, 0f, 0f)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RGBToHSVProducesExpectedValues(
        float red,
        float green,
        float blue,
        float expectedHue,
        float expectedSaturation,
        float expectedValue) {
        Color.RGBToHSV(new Color(red, green, blue), out float hue, out float saturation, out float value);

        Assert.Equal(expectedHue, hue, TOLERANCE);
        Assert.Equal(expectedSaturation, saturation, TOLERANCE);
        Assert.Equal(expectedValue, value, TOLERANCE);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void HSVToRGBRoundTripsArbitraryColors() {
        Color original = new(0.2f, 0.5f, 0.8f);

        Color.RGBToHSV(original, out float hue, out float saturation, out float value);
        Color roundTrip = Color.HSVToRGB(hue, saturation, value);

        Assert.Equal(original.R, roundTrip.R, TOLERANCE);
        Assert.Equal(original.G, roundTrip.G, TOLERANCE);
        Assert.Equal(original.B, roundTrip.B, TOLERANCE);
        Assert.Equal(1f, roundTrip.A);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RGBToHSVPreservesHdrValue() {
        Color.RGBToHSV(new Color(2f, 0f, 0f), out float hue, out float saturation, out float value);

        Assert.Equal(0f, hue);
        Assert.Equal(1f, saturation);
        Assert.Equal(2f, value);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ToStringUsesStableRgbaForm() {
        Color color = new(1f, 0.5f, 0.25f, 0.75f);

        Assert.Equal("RGBA(1, 0.5, 0.25, 0.75)", color.ToString(null, CultureInfo.InvariantCulture));
        Assert.Equal("RGBA(1.00, 0.50, 0.25, 0.75)", color.ToString("F2", CultureInfo.InvariantCulture));
        Assert.Equal("RGBA(1, 0, 0, 1)", Color.Red.ToString());
        Assert.Equal(
            color.ToString(null, CultureInfo.InvariantCulture),
            ((IFormattable)color).ToString(null, CultureInfo.InvariantCulture));
    }

    private static void AssertColorEqual(Color expected, Color actual, float tolerance = TOLERANCE) {
        Assert.Equal(expected.R, actual.R, tolerance);
        Assert.Equal(expected.G, actual.G, tolerance);
        Assert.Equal(expected.B, actual.B, tolerance);
        Assert.Equal(expected.A, actual.A, tolerance);
    }
}
