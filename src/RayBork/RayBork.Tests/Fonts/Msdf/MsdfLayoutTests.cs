using RayBork.Internal;

namespace RayBork.Tests.Fonts.Msdf;

/// <summary>
/// Tests the pure layout math of <see cref="MsdfGenerator.ComputeLayout"/>: the fitted glyph box, the
/// padded texture, the em metrics, the default and non-default settings, empty and degenerate boxes,
/// and argument validation.
/// </summary>
public sealed class MsdfLayoutTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DefaultSettingsFitTheGlyphBoxAndCompleteTheTextureWithPadding() {
        var bounds = new FontBounds(0, 0, 500, 1000);

        MsdfGlyphLayout layout = MsdfGenerator.ComputeLayout(bounds, unitsPerEm: 1000);

        Assert.Equal(18, layout.TextureWidth);
        Assert.Equal(32, layout.TextureHeight);
        Assert.Equal(MsdfGenerator.DEFAULT_PIXEL_SIZE, Math.Max(layout.TextureWidth, layout.TextureHeight));
        Assert.Equal(0.028f, layout.Scale, 6);
        Assert.Equal(MsdfGenerator.DEFAULT_RANGE, layout.Range);
        Assert.Equal(2f, layout.OriginX);
        Assert.Equal(30f, layout.OriginY);

        // The glyph box is ceil(bounds * scale) and the padding adds exactly two texels per side.
        Assert.Equal(14 + (2 * MsdfGenerator.DEFAULT_PADDING), layout.TextureWidth);
        Assert.Equal(28 + (2 * MsdfGenerator.DEFAULT_PADDING), layout.TextureHeight);
        Assert.Equal(14, layout.TextureWidth - (2 * MsdfGenerator.DEFAULT_PADDING));
        Assert.Equal(28, layout.TextureHeight - (2 * MsdfGenerator.DEFAULT_PADDING));

        Assert.Equal(-0.07142857f, layout.BearingXEm, 5);
        Assert.Equal(-0.07142857f, layout.BearingYEm, 5);
        Assert.Equal(18f / 28f, layout.WidthEm, 5);
        Assert.Equal(32f / 28f, layout.HeightEm, 5);

        // The quad dimensions are consistent with the texture size and the font scale.
        Assert.Equal(18f / (layout.Scale * 1000f), layout.WidthEm, 5);
        Assert.Equal(32f / (layout.Scale * 1000f), layout.HeightEm, 5);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void NonDefaultSettingsChangeTheScaleTheRangeAndTheMetrics() {
        var bounds = new FontBounds(0, 0, 1000, 500);

        MsdfGlyphLayout layout = MsdfGenerator.ComputeLayout(
            bounds,
            unitsPerEm: 2048,
            pixelSize: 64,
            range: 8,
            padding: 8);

        Assert.Equal(64, layout.TextureWidth);
        Assert.Equal(40, layout.TextureHeight);
        Assert.Equal(64, Math.Max(layout.TextureWidth, layout.TextureHeight));
        Assert.Equal(48f / 1000f, layout.Scale, 6);
        Assert.Equal(8, layout.Range);
        Assert.Equal(8f, layout.OriginX);
        Assert.Equal(32f, layout.OriginY);
        Assert.Equal(48, layout.TextureWidth - (2 * 8));
        Assert.Equal(24, layout.TextureHeight - (2 * 8));
        Assert.Equal((0f - (8f / (48f / 1000f))) / 2048f, layout.BearingXEm, 5);
        Assert.Equal((0f - (8f / (48f / 1000f))) / 2048f, layout.BearingYEm, 5);
        Assert.Equal(64f / ((48f / 1000f) * 2048f), layout.WidthEm, 5);
        Assert.Equal(40f / ((48f / 1000f) * 2048f), layout.HeightEm, 5);
    }

    [Theory]
    [InlineData(32, 2)]
    [InlineData(64, 8)]
    [InlineData(17, 0)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TheLongestPaddedTextureSideIsExactlyThePixelSize(int pixelSize, int padding) {
        var bounds = new FontBounds(0, 0, 1000, 1000);

        MsdfGlyphLayout layout = MsdfGenerator.ComputeLayout(
            bounds,
            unitsPerEm: 1000,
            pixelSize: pixelSize,
            padding: padding);

        Assert.Equal(pixelSize, Math.Max(layout.TextureWidth, layout.TextureHeight));
        Assert.Equal(pixelSize, layout.TextureWidth);
        Assert.Equal(pixelSize, layout.TextureHeight);
        Assert.Equal(pixelSize - (2 * padding), layout.TextureWidth - (2 * padding));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AnAllZeroBoxYieldsTheEmptyLayout() {
        MsdfGlyphLayout layout = MsdfGenerator.ComputeLayout(
            new FontBounds(0, 0, 0, 0),
            unitsPerEm: 1000);

        Assert.True(layout.IsEmpty);
        Assert.Equal(0, layout.TextureWidth);
        Assert.Equal(0, layout.TextureHeight);
        Assert.Equal(0f, layout.Scale);
        Assert.Equal(0, layout.Range);
        Assert.Equal(0f, layout.OriginX);
        Assert.Equal(0f, layout.OriginY);
        Assert.Equal(0f, layout.BearingXEm);
        Assert.Equal(0f, layout.BearingYEm);
        Assert.Equal(0f, layout.WidthEm);
        Assert.Equal(0f, layout.HeightEm);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AZeroHeightBoxStillProducesAValidLayout() {
        MsdfGlyphLayout layout = MsdfGenerator.ComputeLayout(
            new FontBounds(0, 0, 500, 0),
            unitsPerEm: 1000);

        Assert.False(layout.IsEmpty);
        Assert.Equal(32, layout.TextureWidth);
        Assert.Equal(4, layout.TextureHeight);
        Assert.Equal(32, Math.Max(layout.TextureWidth, layout.TextureHeight));
        Assert.Equal(28f / 500f, layout.Scale, 6);
        Assert.True(float.IsFinite(layout.BearingXEm));
        Assert.True(float.IsFinite(layout.BearingYEm));
        Assert.True(float.IsFinite(layout.WidthEm));
        Assert.True(float.IsFinite(layout.HeightEm));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AZeroWidthBoxStillProducesAValidLayout() {
        MsdfGlyphLayout layout = MsdfGenerator.ComputeLayout(
            new FontBounds(250, 0, 250, 1000),
            unitsPerEm: 1000);

        Assert.False(layout.IsEmpty);
        Assert.Equal(4, layout.TextureWidth);
        Assert.Equal(32, layout.TextureHeight);
        Assert.Equal(32, Math.Max(layout.TextureWidth, layout.TextureHeight));
        Assert.Equal(28f / 1000f, layout.Scale, 6);
        Assert.True(float.IsFinite(layout.BearingXEm));
        Assert.True(float.IsFinite(layout.WidthEm));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AZeroExtentBoxAtANonZeroPositionUsesTheFullTextureSquare() {
        MsdfGlyphLayout layout = MsdfGenerator.ComputeLayout(
            new FontBounds(10, 10, 10, 10),
            unitsPerEm: 1000);

        Assert.False(layout.IsEmpty);
        Assert.Equal(32, layout.TextureWidth);
        Assert.Equal(32, layout.TextureHeight);
        Assert.Equal(28f, layout.Scale);
        Assert.True(float.IsFinite(layout.BearingXEm));
        Assert.True(float.IsFinite(layout.BearingYEm));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void InvertedBoundsAreRejected() {
        ArgumentException horizontal = Assert.Throws<ArgumentException>(
            () => MsdfGenerator.ComputeLayout(new FontBounds(10, 0, 0, 10), unitsPerEm: 1000));
        ArgumentException vertical = Assert.Throws<ArgumentException>(
            () => MsdfGenerator.ComputeLayout(new FontBounds(0, 10, 10, 0), unitsPerEm: 1000));

        Assert.Equal("bounds", horizontal.ParamName);
        Assert.Equal("bounds", vertical.ParamName);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ScalarArgumentsAreValidated() {
        var bounds = new FontBounds(0, 0, 100, 100);

        Assert.Equal(
            "pixelSize",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => MsdfGenerator.ComputeLayout(bounds, unitsPerEm: 1000, pixelSize: 0)).ParamName);
        Assert.Equal(
            "pixelSize",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => MsdfGenerator.ComputeLayout(bounds, unitsPerEm: 1000, pixelSize: -1)).ParamName);
        Assert.Equal(
            "range",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => MsdfGenerator.ComputeLayout(bounds, unitsPerEm: 1000, range: 0)).ParamName);
        Assert.Equal(
            "range",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => MsdfGenerator.ComputeLayout(bounds, unitsPerEm: 1000, range: -1)).ParamName);
        Assert.Equal(
            "padding",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => MsdfGenerator.ComputeLayout(bounds, unitsPerEm: 1000, padding: -1)).ParamName);
        Assert.Equal(
            "unitsPerEm",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => MsdfGenerator.ComputeLayout(bounds, unitsPerEm: 0)).ParamName);
        Assert.Equal(
            "unitsPerEm",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => MsdfGenerator.ComputeLayout(bounds, unitsPerEm: -1)).ParamName);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void APixelSizeWithoutAGlyphBoxIsRejected() {
        var bounds = new FontBounds(0, 0, 100, 100);

        Assert.Equal(
            "pixelSize",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => MsdfGenerator.ComputeLayout(bounds, unitsPerEm: 1000, pixelSize: 32, padding: 16)).ParamName);
        Assert.Equal(
            "pixelSize",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => MsdfGenerator.ComputeLayout(bounds, unitsPerEm: 1000, pixelSize: 32, padding: 17)).ParamName);
        Assert.Equal(
            "pixelSize",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => MsdfGenerator.ComputeLayout(bounds, unitsPerEm: 1000, pixelSize: 4, padding: 2)).ParamName);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TheSmallestValidGlyphBoxIsOnePixel() {
        var bounds = new FontBounds(0, 0, 100, 100);

        MsdfGlyphLayout layout = MsdfGenerator.ComputeLayout(
            bounds,
            unitsPerEm: 1000,
            pixelSize: 33,
            padding: 16);

        Assert.Equal(33, layout.TextureWidth);
        Assert.Equal(33, layout.TextureHeight);
        Assert.Equal(1f / 100f, layout.Scale, 6);
    }
}
