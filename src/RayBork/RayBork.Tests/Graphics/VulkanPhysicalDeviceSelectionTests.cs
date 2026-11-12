using RayBork.Internal;
using Silk.NET.Vulkan;

namespace RayBork.Tests;

public sealed class VulkanPhysicalDeviceSelectionTests {
    private const uint API_1_2 = (1u << 22) | (2u << 12);
    private const uint API_1_1 = (1u << 22) | (1u << 12);
    private const uint API_1_3 = (1u << 22) | (3u << 12);

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DiscreteGpuWinsOverIntegratedGpuRegardlessOfIndex() {
        VulkanPhysicalDeviceCandidate[] candidates = [
            Candidate(0, "Integrated", PhysicalDeviceType.IntegratedGpu, API_1_3),
            Candidate(1, "Discrete", PhysicalDeviceType.DiscreteGpu, API_1_2),
        ];

        Assert.Equal(1, VulkanPhysicalDeviceSelector.Select(candidates));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void VirtualCpuAndOtherDevicesRankBelowIntegratedGpu() {
        VulkanPhysicalDeviceCandidate[] candidates = [
            Candidate(0, "Other", PhysicalDeviceType.Other),
            Candidate(1, "Cpu", PhysicalDeviceType.Cpu),
            Candidate(2, "Virtual", PhysicalDeviceType.VirtualGpu),
            Candidate(3, "Integrated", PhysicalDeviceType.IntegratedGpu),
        ];

        Assert.Equal(3, VulkanPhysicalDeviceSelector.Select(candidates));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void Vulkan11DeviceIsRejected() {
        VulkanPhysicalDeviceCandidate[] candidates = [
            Candidate(0, "Old GPU", PhysicalDeviceType.DiscreteGpu, API_1_1),
        ];

        GraphicsException exception = Assert.Throws<GraphicsException>(() => VulkanPhysicalDeviceSelector.Select(candidates));

        Assert.Equal(DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED, exception.DiagnosticCode);
        Assert.Contains("Old GPU", exception.Message, StringComparison.Ordinal);
        Assert.Contains("older than Vulkan 1.2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MissingPresentationQueueIsRejected() {
        VulkanPhysicalDeviceCandidate[] candidates = [
            Candidate(0, "No Present GPU", supportsPresent: false),
        ];

        GraphicsException exception = Assert.Throws<GraphicsException>(() => VulkanPhysicalDeviceSelector.Select(candidates));

        Assert.Contains("no presentation queue family", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MissingSwapchainExtensionIsRejected() {
        VulkanPhysicalDeviceCandidate[] candidates = [
            Candidate(0, "No Swapchain GPU", hasSwapchainExtension: false),
        ];

        GraphicsException exception = Assert.Throws<GraphicsException>(() => VulkanPhysicalDeviceSelector.Select(candidates));

        Assert.Contains("VK_KHR_swapchain", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MissingBindlessDescriptorFeaturesAreRejectedAndNamed() {
        VulkanPhysicalDeviceCandidate[] candidates = [
            Candidate(
                0,
                "No Bindless GPU",
                missingBindlessFeatures: "runtimeDescriptorArray, descriptorBindingPartiallyBound"),
        ];

        GraphicsException exception = Assert.Throws<GraphicsException>(() => VulkanPhysicalDeviceSelector.Select(candidates));

        Assert.Equal(DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED, exception.DiagnosticCode);
        Assert.Contains("runtimeDescriptorArray", exception.Message, StringComparison.Ordinal);
        Assert.Contains("descriptorBindingPartiallyBound", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void HigherApiVersionBreaksATypeTie() {
        VulkanPhysicalDeviceCandidate[] candidates = [
            Candidate(0, "Older Discrete", PhysicalDeviceType.DiscreteGpu, API_1_2),
            Candidate(1, "Newer Discrete", PhysicalDeviceType.DiscreteGpu, API_1_3),
        ];

        Assert.Equal(1, VulkanPhysicalDeviceSelector.Select(candidates));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void LowerIndexBreaksAFullTie() {
        VulkanPhysicalDeviceCandidate[] candidates = [
            Candidate(2, "Second", PhysicalDeviceType.DiscreteGpu),
            Candidate(1, "First", PhysicalDeviceType.DiscreteGpu),
        ];

        Assert.Equal(1, VulkanPhysicalDeviceSelector.Select(candidates));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void NoCandidateExceptionNamesEveryRejectedDeviceAndReason() {
        VulkanPhysicalDeviceCandidate[] candidates = [
            Candidate(0, "Old GPU", PhysicalDeviceType.DiscreteGpu, API_1_1),
            Candidate(1, "No Present GPU", supportsPresent: false, hasSwapchainExtension: false),
        ];

        GraphicsException exception = Assert.Throws<GraphicsException>(() => VulkanPhysicalDeviceSelector.Select(candidates));

        Assert.Equal(DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED, exception.DiagnosticCode);
        Assert.Contains("[0] Old GPU", exception.Message, StringComparison.Ordinal);
        Assert.Contains("[1] No Present GPU", exception.Message, StringComparison.Ordinal);
        Assert.Contains("no presentation queue family", exception.Message, StringComparison.Ordinal);
        Assert.Contains("VK_KHR_swapchain", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void IsSuitableRequiresEveryM2Fact() {
        Assert.True(VulkanPhysicalDeviceSelector.IsSuitable(Candidate(0, "Suitable", PhysicalDeviceType.DiscreteGpu)));
        Assert.False(VulkanPhysicalDeviceSelector.IsSuitable(Candidate(0, "No Graphics", supportsGraphics: false)));
        Assert.False(VulkanPhysicalDeviceSelector.IsSuitable(Candidate(0, "No Present", supportsPresent: false)));
        Assert.False(VulkanPhysicalDeviceSelector.IsSuitable(Candidate(0, "No Swapchain", hasSwapchainExtension: false)));
        Assert.False(VulkanPhysicalDeviceSelector.IsSuitable(Candidate(0, "Old", PhysicalDeviceType.DiscreteGpu, API_1_1)));
        Assert.False(VulkanPhysicalDeviceSelector.IsSuitable(
            Candidate(0, "No Bindless", missingBindlessFeatures: "shaderSampledImageArrayNonUniformIndexing")));
    }

    private static VulkanPhysicalDeviceCandidate Candidate(
        int index,
        string name,
        PhysicalDeviceType deviceType = PhysicalDeviceType.DiscreteGpu,
        uint apiVersion = API_1_2,
        bool supportsGraphics = true,
        bool supportsPresent = true,
        bool hasSwapchainExtension = true,
        string missingBindlessFeatures = "") =>
        new(
            index,
            name,
            apiVersion,
            deviceType,
            supportsGraphics,
            supportsPresent,
            hasSwapchainExtension,
            missingBindlessFeatures);
}
