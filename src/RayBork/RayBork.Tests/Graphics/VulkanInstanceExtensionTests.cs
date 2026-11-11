using RayBork.Internal;

namespace RayBork.Tests;

public sealed class VulkanInstanceExtensionTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ValidateReturnsAnEmptyListWhenEverythingIsAvailable() {
        IReadOnlyList<string> missing = VulkanInstanceExtensions.Validate(
            ["VK_KHR_surface", "VK_KHR_win32_surface", "VK_EXT_debug_utils"],
            ["VK_KHR_surface", "VK_KHR_win32_surface"]);

        Assert.Empty(missing);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ValidateReturnsMissingNamesInRequirementOrderWithoutDuplicates() {
        IReadOnlyList<string> missing = VulkanInstanceExtensions.Validate(
            ["VK_KHR_surface"],
            ["VK_KHR_win32_surface", "VK_KHR_surface", "VK_KHR_win32_surface", "VK_EXT_debug_utils"]);

        Assert.Equal(["VK_KHR_win32_surface", "VK_EXT_debug_utils"], missing);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ValidateComparesOrdinalAndCaseSensitively() {
        IReadOnlyList<string> missing = VulkanInstanceExtensions.Validate(
            ["vk_khr_surface"],
            ["VK_KHR_surface"]);

        Assert.Equal(["VK_KHR_surface"], missing);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RequireThrowsGraphicsExceptionWithTheMissingAndPlatformExtensions() {
        GraphicsException exception = Assert.Throws<GraphicsException>(() => VulkanInstanceExtensions.Require(
            ["VK_KHR_surface"],
            ["VK_KHR_surface", "VK_KHR_not_a_real_surface"]));

        Assert.Equal(DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED, exception.DiagnosticCode);
        Assert.Contains("VK_KHR_not_a_real_surface", exception.Message, StringComparison.Ordinal);
        Assert.Contains("VK_KHR_surface", exception.Message, StringComparison.Ordinal);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RequireDoesNothingWhenEveryExtensionIsAvailable() {
        VulkanInstanceExtensions.Require(
            ["VK_KHR_surface", "VK_KHR_win32_surface"],
            ["VK_KHR_surface"]);
    }
}
