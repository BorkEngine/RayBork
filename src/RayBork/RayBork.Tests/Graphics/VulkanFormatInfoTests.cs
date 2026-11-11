using RayBork.Internal;
using Silk.NET.Vulkan;

namespace RayBork.Tests;

public sealed class VulkanFormatInfoTests {
    [Theory]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    [InlineData(Format.R16G16B16A16Sfloat, "R16G16B16A16_SFLOAT")]
    [InlineData(Format.B8G8R8A8Srgb, "B8G8R8A8_SRGB")]
    [InlineData(Format.R8G8B8A8Srgb, "R8G8B8A8_SRGB")]
    [InlineData(Format.A2B10G10R10UnormPack32, "A2B10G10R10_UNORM_PACK32")]
    [InlineData(Format.B8G8R8A8Unorm, "B8G8R8A8_UNORM")]
    [InlineData(Format.R8G8B8A8Unorm, "R8G8B8A8_UNORM")]
    [InlineData(Format.D32Sfloat, "D32_SFLOAT")]
    public void DescribeReturnsThePublicSwapchainFormatNames(Format format, string expected) {
        Assert.Equal(expected, VulkanFormatInfo.Describe(format));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DescribeStripsThePrefixFromEveryOtherDefinedFormat() {
        Assert.Equal("X8_D24_UNORM_PACK32", VulkanFormatInfo.Describe(Format.X8D24UnormPack32));
        Assert.Equal("R16G16B16A16_SNORM", VulkanFormatInfo.Describe(Format.R16G16B16A16SNorm));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DescribeFallsBackToTheNumericValueForAnUndefinedFormat() {
        Assert.Equal("VK_FORMAT_2147483647", VulkanFormatInfo.Describe((Format)int.MaxValue));
    }

    [Theory]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    [InlineData(Format.R16G16B16A16Sfloat, true)]
    [InlineData(Format.A2B10G10R10UnormPack32, true)]
    [InlineData(Format.B8G8R8A8Srgb, false)]
    [InlineData(Format.R8G8B8A8Unorm, false)]
    [InlineData(Format.D32Sfloat, false)]
    public void IsHdrOnlyAcceptsTheM2HdrFormats(Format format, bool expected) {
        Assert.Equal(expected, VulkanFormatInfo.IsHdr(format));
    }

    [Theory]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    [InlineData(Format.B8G8R8A8Srgb, true)]
    [InlineData(Format.R8G8B8A8Srgb, true)]
    [InlineData(Format.B8G8R8A8Unorm, false)]
    [InlineData(Format.R16G16B16A16Sfloat, false)]
    public void IsSrgbDetectsTheTransferFunction(Format format, bool expected) {
        Assert.Equal(expected, VulkanFormatInfo.IsSrgb(format));
    }
}
