using RayBork.Internal;

namespace RayBork.Tests.Fonts.Glyph;

/// <summary>
/// Tests the validated per-font MSDF settings the later <c>Font</c> creates from the face descriptor.
/// </summary>
public sealed class FontGlyphSettingsTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DefaultsMatchTheMsdfGenerator() {
        FontGlyphSettings settings = FontGlyphSettings.CreateDefault(1000);

        Assert.Equal(1000, settings.UnitsPerEm);
        Assert.Equal(MsdfGenerator.DEFAULT_PIXEL_SIZE, settings.MsdfPixelSize);
        Assert.Equal(MsdfGenerator.DEFAULT_RANGE, settings.MsdfRange);
        Assert.Equal(MsdfGenerator.DEFAULT_PADDING, settings.MsdfPadding);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ZeroPaddingIsAccepted() {
        FontGlyphSettings settings = new(1000, 32, 4, 0);

        Assert.Equal(0, settings.MsdfPadding);
    }

    [Theory]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    [InlineData(0, 32, 4, 2)]
    [InlineData(-1, 32, 4, 2)]
    [InlineData(1000, 0, 4, 2)]
    [InlineData(1000, 32, 0, 2)]
    [InlineData(1000, 32, 4, -1)]
    [InlineData(1000, 4, 4, 2)]
    [InlineData(1000, 3, 4, 2)]
    public void InvalidValuesAreRejected(
        int unitsPerEm,
        int pixelSize,
        int range,
        int padding) {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new FontGlyphSettings(unitsPerEm, pixelSize, range, padding));
    }
}
