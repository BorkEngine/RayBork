using RayBork.Internal;

namespace RayBork.Tests;

public sealed class VulkanQueueFamilySelectionTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void OneFamilyThatSupportsBothIsPreferred() {
        VulkanQueueFamilyInfo[] families = [
            new(0, SupportsGraphics: true, SupportsPresent: false, QueueCount: 1),
            new(1, SupportsGraphics: false, SupportsPresent: true, QueueCount: 1),
            new(2, SupportsGraphics: true, SupportsPresent: true, QueueCount: 1),
        ];

        (uint graphics, uint present) = VulkanQueueFamilySelection.Select(families);

        Assert.Equal(2u, graphics);
        Assert.Equal(2u, present);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SeparateFamiliesUseTheLowestSupportedIndices() {
        VulkanQueueFamilyInfo[] families = [
            new(3, SupportsGraphics: true, SupportsPresent: false, QueueCount: 1),
            new(1, SupportsGraphics: false, SupportsPresent: true, QueueCount: 1),
            new(5, SupportsGraphics: true, SupportsPresent: false, QueueCount: 1),
            new(2, SupportsGraphics: false, SupportsPresent: true, QueueCount: 1),
        ];

        (uint graphics, uint present) = VulkanQueueFamilySelection.Select(families);

        Assert.Equal(3u, graphics);
        Assert.Equal(1u, present);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void LowestCombinedFamilyWinsWhenSeveralSupportBoth() {
        VulkanQueueFamilyInfo[] families = [
            new(4, SupportsGraphics: true, SupportsPresent: true, QueueCount: 1),
            new(2, SupportsGraphics: true, SupportsPresent: true, QueueCount: 1),
        ];

        (uint graphics, uint present) = VulkanQueueFamilySelection.Select(families);

        Assert.Equal(2u, graphics);
        Assert.Equal(2u, present);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MissingGraphicsSupportThrows() {
        VulkanQueueFamilyInfo[] families = [
            new(0, SupportsGraphics: false, SupportsPresent: true, QueueCount: 1),
        ];

        GraphicsException exception = Assert.Throws<GraphicsException>(() => VulkanQueueFamilySelection.Select(families));

        Assert.Equal(DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED, exception.DiagnosticCode);
        Assert.Contains("graphics queue family", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MissingPresentationSupportThrows() {
        VulkanQueueFamilyInfo[] families = [
            new(0, SupportsGraphics: true, SupportsPresent: false, QueueCount: 1),
        ];

        GraphicsException exception = Assert.Throws<GraphicsException>(() => VulkanQueueFamilySelection.Select(families));

        Assert.Equal(DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED, exception.DiagnosticCode);
        Assert.Contains("presentation queue family", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EmptyFamilyListThrows() {
        GraphicsException exception = Assert.Throws<GraphicsException>(() => VulkanQueueFamilySelection.Select([]));

        Assert.Equal(DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED, exception.DiagnosticCode);
        Assert.Contains("graphics and presentation queue families", exception.Message, StringComparison.Ordinal);
    }
}
