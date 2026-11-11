using RayBork.Internal;

namespace RayBork.Tests;

public sealed class VulkanSwapchainImageCountTests {
    [Theory]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    [InlineData(1u, 0u, 3u)]
    [InlineData(2u, 0u, 3u)]
    [InlineData(4u, 0u, 4u)]
    [InlineData(2u, 2u, 2u)]
    [InlineData(1u, 2u, 2u)]
    [InlineData(4u, 2u, 4u)]
    public void SelectRespectsThePreferredCountAndTheSurfaceLimits(
        uint minImageCount,
        uint maxImageCount,
        uint expected) {
        Assert.Equal(expected, VulkanSwapchainImageCount.Select(minImageCount, maxImageCount));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ZeroMaximumMeansNoUpperLimit() {
        Assert.Equal(3u, VulkanSwapchainImageCount.Select(1, 0));
    }
}
