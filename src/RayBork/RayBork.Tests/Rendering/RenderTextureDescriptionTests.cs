using RayBork.Internal;

namespace RayBork.Tests;

/// <summary>
/// Unit tests for the render texture description validation and the documented MSAA sample-count
/// fallback.
/// </summary>
public sealed class RenderTextureDescriptionTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DefaultsDescribeAnHdrMsaaTarget() {
        RenderTextureDescription description = new(64, 32);

        Assert.Equal(64, description.Width);
        Assert.Equal(32, description.Height);
        Assert.Equal(TextureFormat.Rgba16Float, description.ColorFormat);
        Assert.True(description.Depth);
        Assert.Equal(4, description.SampleCount);
        Assert.True(RenderTextureValidation.IsHdr(description.ColorFormat));
        Assert.False(RenderTextureValidation.IsHdr(TextureFormat.Rgba8Unorm));
        Assert.False(RenderTextureValidation.IsHdr(TextureFormat.Rgba8Srgb));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(16384, 16384)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PositiveDimensionsWithinTheLimitAreAccepted(int width, int height) {
        RenderTextureValidation.Validate(new RenderTextureDescription(width, height));
    }

    [Theory]
    [InlineData(0, 64)]
    [InlineData(-1, 64)]
    [InlineData(64, 0)]
    [InlineData(64, -1)]
    [InlineData(16385, 64)]
    [InlineData(64, 16385)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void InvalidDimensionsAreRejected(int width, int height) {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RenderTextureValidation.Validate(new RenderTextureDescription(width, height)));
    }

    [Theory]
    [InlineData(TextureFormat.Rgba16Float)]
    [InlineData(TextureFormat.Rgba8Unorm)]
    [InlineData(TextureFormat.Rgba8Srgb)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SupportedColorFormatsAreAccepted(TextureFormat format) {
        RenderTextureValidation.Validate(new RenderTextureDescription(8, 8, format));
    }

    [Theory]
    [InlineData(TextureFormat.Bc7Unorm)]
    [InlineData(TextureFormat.Bc7Srgb)]
    [InlineData(TextureFormat.Bc5Unorm)]
    [InlineData(TextureFormat.R8Unorm)]
    [InlineData(TextureFormat.Depth32Float)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UnsupportedColorFormatsAreRejected(TextureFormat format) {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => RenderTextureValidation.Validate(new RenderTextureDescription(8, 8, format)));
        Assert.Contains("Rgba16Float", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SupportedSampleCountsAreAccepted(int sampleCount) {
        RenderTextureValidation.Validate(new RenderTextureDescription(8, 8, SampleCount: sampleCount));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(16)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UnsupportedSampleCountsAreRejected(int sampleCount) {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RenderTextureValidation.Validate(new RenderTextureDescription(8, 8, SampleCount: sampleCount)));
    }

    [Theory]
    [InlineData(1, new[] { 1, 2, 4, 8 }, 1)]
    [InlineData(2, new[] { 1, 2, 4, 8 }, 2)]
    [InlineData(4, new[] { 1, 2, 4, 8 }, 4)]
    [InlineData(8, new[] { 1, 2, 4, 8 }, 8)]
    [InlineData(4, new[] { 1, 2 }, 2)]
    [InlineData(8, new[] { 1 }, 1)]
    [InlineData(2, new[] { 1, 4, 8 }, 1)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SampleCountFallsBackToTheHighestSupportedCountNotExceedingTheRequest(
        int requested,
        int[] supported,
        int expected) {
        Assert.Equal(expected, RenderTextureValidation.SelectSampleCount(requested, supported));
    }
}
