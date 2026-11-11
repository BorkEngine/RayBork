using RayBork.Internal;
using Silk.NET.Vulkan;

namespace RayBork.Tests;

public sealed class VulkanSwapchainExtentSelectionTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DefinedCurrentExtentWins() {
        SurfaceCapabilitiesKHR capabilities = new() {
            CurrentExtent = new Extent2D { Width = 640, Height = 480 },
            MinImageExtent = new Extent2D { Width = 1, Height = 1 },
            MaxImageExtent = new Extent2D { Width = 4096, Height = 4096 },
        };

        Extent2D extent = VulkanSwapchainExtentSelection.Select(capabilities, 100, 100);

        Assert.Equal(640u, extent.Width);
        Assert.Equal(480u, extent.Height);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UndefinedCurrentExtentClampsTheRequestedSizeToTheSupportedRange() {
        SurfaceCapabilitiesKHR capabilities = new() {
            CurrentExtent = new Extent2D { Width = uint.MaxValue, Height = uint.MaxValue },
            MinImageExtent = new Extent2D { Width = 100, Height = 100 },
            MaxImageExtent = new Extent2D { Width = 2000, Height = 2000 },
        };

        Extent2D extent = VulkanSwapchainExtentSelection.Select(capabilities, 4000, 50);

        Assert.Equal(2000u, extent.Width);
        Assert.Equal(100u, extent.Height);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UndefinedCurrentExtentUsesTheRequestedSizeWhenItIsInRange() {
        SurfaceCapabilitiesKHR capabilities = new() {
            CurrentExtent = new Extent2D { Width = uint.MaxValue, Height = uint.MaxValue },
            MinImageExtent = new Extent2D { Width = 1, Height = 1 },
            MaxImageExtent = new Extent2D { Width = 4096, Height = 4096 },
        };

        Extent2D extent = VulkanSwapchainExtentSelection.Select(capabilities, 800, 600);

        Assert.Equal(800u, extent.Width);
        Assert.Equal(600u, extent.Height);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void NonPositiveRenderSizeSelectsTheMinimumExtent() {
        SurfaceCapabilitiesKHR capabilities = new() {
            CurrentExtent = new Extent2D { Width = uint.MaxValue, Height = uint.MaxValue },
            MinImageExtent = new Extent2D { Width = 7, Height = 5 },
            MaxImageExtent = new Extent2D { Width = 4096, Height = 4096 },
        };

        Extent2D extent = VulkanSwapchainExtentSelection.Select(capabilities, 0, -1);

        Assert.Equal(7u, extent.Width);
        Assert.Equal(5u, extent.Height);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ZeroMaximumExtentMeansNoLimit() {
        SurfaceCapabilitiesKHR capabilities = new() {
            CurrentExtent = new Extent2D { Width = uint.MaxValue, Height = uint.MaxValue },
            MinImageExtent = new Extent2D { Width = 1, Height = 1 },
            MaxImageExtent = default,
        };

        Extent2D extent = VulkanSwapchainExtentSelection.Select(capabilities, 1920, 1080);

        Assert.Equal(1920u, extent.Width);
        Assert.Equal(1080u, extent.Height);
    }
}
