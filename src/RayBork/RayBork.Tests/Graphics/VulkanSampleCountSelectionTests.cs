using RayBork.Internal;

namespace RayBork.Tests;

public sealed class VulkanSampleCountSelectionTests {
    [Theory]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    [InlineData(4, new[] { 1, 2, 4, 8 }, 4)]
    [InlineData(8, new[] { 1, 2, 4 }, 4)]
    [InlineData(3, new[] { 1, 2, 4 }, 2)]
    [InlineData(2, new[] { 1 }, 1)]
    [InlineData(1, new[] { 1, 2, 4 }, 1)]
    [InlineData(16, new[] { 8, 2, 4, 1 }, 8)]
    public void SelectReturnsTheHighestSupportedCountThatDoesNotExceedTheRequest(
        int requested,
        int[] supported,
        int expected) {
        Assert.Equal(expected, VulkanSampleCountSelection.Select(requested, supported));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EmptySupportFallsBackToOne() {
        Assert.Equal(1, VulkanSampleCountSelection.Select(4, []));
    }

    [Theory]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveRequestThrows(int requested) {
        Assert.Throws<ArgumentOutOfRangeException>(() => VulkanSampleCountSelection.Select(requested, [1, 2, 4]));
    }
}
