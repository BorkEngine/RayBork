using RayBork.Internal;
using Silk.NET.Vulkan;

namespace RayBork.Tests.Graphics.Assets;

/// <summary>
/// Unit tests for the packed texture format mapping the KTX2 reader and Vulkan upload share.
/// </summary>
public sealed class VulkanTextureFormatTests {
    [Theory]
    [InlineData(TextureFormat.Rgba8Unorm, Format.R8G8B8A8Unorm)]
    [InlineData(TextureFormat.Rgba8Srgb, Format.R8G8B8A8Srgb)]
    [InlineData(TextureFormat.Bc7Unorm, Format.BC7UnormBlock)]
    [InlineData(TextureFormat.Bc7Srgb, Format.BC7SrgbBlock)]
    [InlineData(TextureFormat.Bc5Unorm, Format.BC5UnormBlock)]
    [InlineData(TextureFormat.R8Unorm, Format.R8Unorm)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MapsPackedFormatsToVulkanFormats(TextureFormat format, Format expected) {
        Assert.Equal(expected, VulkanTextureFormat.ToVkFormat(format));
    }

    [Theory]
    [InlineData(TextureFormat.Rgba16Float)]
    [InlineData(TextureFormat.Depth32Float)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsRenderTargetFormats(TextureFormat format) {
        Assert.False(VulkanTextureFormat.IsPacked(format));
        AssetLoadException failure = Assert.Throws<AssetLoadException>(() => VulkanTextureFormat.ToVkFormat(format));
        Assert.Equal(DiagnosticCodes.ASSET_LOAD_FAILED, failure.DiagnosticCode);
        Assert.Contains("render-target", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(TextureFormat.Rgba8Unorm, false)]
    [InlineData(TextureFormat.Rgba8Srgb, true)]
    [InlineData(TextureFormat.Bc7Unorm, false)]
    [InlineData(TextureFormat.Bc7Srgb, true)]
    [InlineData(TextureFormat.Bc5Unorm, false)]
    [InlineData(TextureFormat.R8Unorm, false)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ReportsSrgbFromTheFormat(TextureFormat format, bool expected) {
        Assert.Equal(expected, VulkanTextureFormat.IsSrgb(format));
        Assert.Equal(expected ? VulkanTextureFormat.TRANSFER_SRGB : VulkanTextureFormat.TRANSFER_LINEAR, VulkanTextureFormat.DfdTransferFunction(format));
    }

    [Theory]
    [InlineData(TextureFormat.Rgba8Unorm, 4, 4, 64)]
    [InlineData(TextureFormat.Rgba8Unorm, 1, 1, 4)]
    [InlineData(TextureFormat.Bc7Unorm, 4, 4, 16)]
    [InlineData(TextureFormat.Bc7Unorm, 1, 1, 16)]
    [InlineData(TextureFormat.Bc5Unorm, 3, 3, 16)]
    [InlineData(TextureFormat.R8Unorm, 4, 4, 16)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ComputesLevelByteLength(TextureFormat format, int width, int height, int expected) {
        Assert.Equal(expected, VulkanTextureFormat.GetLevelByteLength(format, width, height));
    }

    [Theory]
    [InlineData(TextureFormat.Rgba8Unorm, 4)]
    [InlineData(TextureFormat.R8Unorm, 4)]
    [InlineData(TextureFormat.Bc7Unorm, 16)]
    [InlineData(TextureFormat.Bc5Unorm, 16)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ComputesLevelAlignment(TextureFormat format, int expected) {
        Assert.Equal(expected, VulkanTextureFormat.GetLevelAlignment(format));
    }

    [Theory]
    [InlineData(1u, 1u, 1u)]
    [InlineData(4u, 4u, 3u)]
    [InlineData(5u, 3u, 3u)]
    [InlineData(1024u, 1u, 11u)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ComputesTheFullMipChain(uint width, uint height, uint expected) {
        Assert.Equal(expected, VulkanTextureFormat.FullMipCount(width, height));
    }

    [Theory]
    [InlineData(4u, 0, 4)]
    [InlineData(4u, 1, 2)]
    [InlineData(4u, 2, 1)]
    [InlineData(1u, 5, 1)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ComputesLevelExtent(uint baseExtent, int level, int expected) {
        Assert.Equal(expected, VulkanTextureFormat.LevelExtent(baseExtent, level));
    }

    [Theory]
    [InlineData(TextureFormat.Rgba8Unorm, VulkanTextureFormat.COLOR_MODEL_RGBSDA)]
    [InlineData(TextureFormat.R8Unorm, VulkanTextureFormat.COLOR_MODEL_RGBSDA)]
    [InlineData(TextureFormat.Bc7Unorm, VulkanTextureFormat.COLOR_MODEL_BC7)]
    [InlineData(TextureFormat.Bc5Unorm, VulkanTextureFormat.COLOR_MODEL_BC5)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ReportsTheDfdColorModel(TextureFormat format, byte expected) {
        Assert.Equal(expected, VulkanTextureFormat.DfdColorModel(format));
    }
}
