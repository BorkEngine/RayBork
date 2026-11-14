using RayBork.Internal;

namespace RayBork.Tests;

public sealed class VulkanPresentResultTests {
    [Theory]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    [InlineData((int)SwapchainResult.Success, true)]
    [InlineData((int)SwapchainResult.Suboptimal, true)]
    [InlineData((int)SwapchainResult.OutOfDate, false)]
    [InlineData((int)SwapchainResult.SurfaceLost, false)]
    public void OnlyCompletedPresentationResultsCountAsPresented(int result, bool expected) {
        Assert.Equal(expected, Vulkan12Backend.CountsAsPresented((SwapchainResult)result));
    }
}
