using RayBork.Internal;
using Silk.NET.Vulkan;

namespace RayBork.Tests;

/// <summary>
/// Unit tests for the Vulkan 1.2 depth resolve selection: sample zero is preferred, min and max are the
/// documented fallbacks, and a device without a usable depth mode produces a diagnostic naming the
/// property and the renderer's requirement.
/// </summary>
public sealed class VulkanDepthStencilResolveTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SampleZeroIsPreferredWhenTheDeviceAdvertisesIt() {
        ResolveModeFlags supported = ResolveModeFlags.SampleZeroBit
            | ResolveModeFlags.MinBit
            | ResolveModeFlags.MaxBit;

        Assert.Equal(ResolveModeFlags.SampleZeroBit, VulkanDepthStencilResolve.SelectDepthResolveMode(supported));
    }

    [Theory]
    [InlineData(ResolveModeFlags.MinBit)]
    [InlineData(ResolveModeFlags.MaxBit)]
    [InlineData(ResolveModeFlags.MinBit | ResolveModeFlags.MaxBit)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MinAndMaxAreTheFallbacksWhenSampleZeroIsMissing(ResolveModeFlags supported) {
        ResolveModeFlags expected = (supported & ResolveModeFlags.MinBit) != 0
            ? ResolveModeFlags.MinBit
            : ResolveModeFlags.MaxBit;

        Assert.Equal(expected, VulkanDepthStencilResolve.SelectDepthResolveMode(supported));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AverageOnlyIsNotAUsableDepthResolveMode() {
        Assert.Equal(
            ResolveModeFlags.None,
            VulkanDepthStencilResolve.SelectDepthResolveMode(ResolveModeFlags.AverageBit));
        Assert.Equal(
            ResolveModeFlags.None,
            VulkanDepthStencilResolve.SelectDepthResolveMode(ResolveModeFlags.None));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TheMissingModeDiagnosticNamesThePropertyTheRequirementAndTheDevice() {
        string message = VulkanDepthStencilResolve.CreateMissingModeMessage(
            "No Resolve GPU",
            ResolveModeFlags.None);

        Assert.Contains("No Resolve GPU", message, StringComparison.Ordinal);
        Assert.Contains("VkPhysicalDeviceDepthStencilResolveProperties.supportedDepthResolveModes", message, StringComparison.Ordinal);
        Assert.Contains("VK_RESOLVE_MODE_SAMPLE_ZERO_BIT", message, StringComparison.Ordinal);
        Assert.Contains("VK_RESOLVE_MODE_MIN_BIT", message, StringComparison.Ordinal);
        Assert.Contains("VK_RESOLVE_MODE_MAX_BIT", message, StringComparison.Ordinal);
        Assert.Contains("unsampleable depth view", message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DescribeNamesEveryAdvertisedMode() {
        Assert.Equal(
            "VK_RESOLVE_MODE_SAMPLE_ZERO_BIT | VK_RESOLVE_MODE_MIN_BIT",
            VulkanDepthStencilResolve.Describe(
                ResolveModeFlags.SampleZeroBit | ResolveModeFlags.MinBit));
        Assert.Equal(
            "VK_RESOLVE_MODE_NONE",
            VulkanDepthStencilResolve.Describe(ResolveModeFlags.None));
    }
}
