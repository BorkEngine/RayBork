using RayBork.Internal;
using Silk.NET.Vulkan;

namespace RayBork.Tests;

/// <summary>
/// Unit tests for the swapchain image usage selection that makes the presented-pixel readback probes
/// possible without requesting usage the surface does not advertise.
/// </summary>
public sealed class VulkanSwapchainImageUsageTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AddsTransferSourceWhenTheSurfaceAdvertisesIt() {
        (ImageUsageFlags usage, bool supportsReadback) = VulkanSwapchainImageUsage.Select(
            ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit);

        Assert.Equal(ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit, usage);
        Assert.True(supportsReadback);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RequestsOnlyColorAttachmentWhenTransferSourceIsUnavailable() {
        (ImageUsageFlags usage, bool supportsReadback) = VulkanSwapchainImageUsage.Select(
            ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferDstBit);

        Assert.Equal(ImageUsageFlags.ColorAttachmentBit, usage);
        Assert.False(supportsReadback);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void NeverRequestsUsageTheSurfaceDidNotAdvertise() {
        (ImageUsageFlags usage, bool supportsReadback) = VulkanSwapchainImageUsage.Select((ImageUsageFlags)0);

        Assert.Equal(ImageUsageFlags.ColorAttachmentBit, usage);
        Assert.False(supportsReadback);
    }
}
