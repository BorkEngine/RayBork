using RayBork.Internal;
using Silk.NET.Vulkan;

namespace RayBork.Tests;

public sealed class VulkanPresentModeSelectionTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MailboxIsUsedWhenAvailable() {
        VulkanPresentModeChoice choice = VulkanPresentModeSelection.Select(
            [PresentModeKHR.FifoKhr, PresentModeKHR.MailboxKhr],
            PresentMode.Mailbox);

        Assert.Equal(PresentModeKHR.MailboxKhr, choice.Native);
        Assert.Equal(PresentMode.Mailbox, choice.Public);
        Assert.False(choice.FellBack);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MailboxFallsBackToFifo() {
        VulkanPresentModeChoice choice = VulkanPresentModeSelection.Select(
            [PresentModeKHR.FifoKhr, PresentModeKHR.ImmediateKhr],
            PresentMode.Mailbox);

        Assert.Equal(PresentModeKHR.FifoKhr, choice.Native);
        Assert.Equal(PresentMode.Fifo, choice.Public);
        Assert.True(choice.FellBack);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void FifoIsUsedWhenAvailable() {
        VulkanPresentModeChoice choice = VulkanPresentModeSelection.Select(
            [PresentModeKHR.FifoKhr, PresentModeKHR.MailboxKhr],
            PresentMode.Fifo);

        Assert.Equal(PresentModeKHR.FifoKhr, choice.Native);
        Assert.Equal(PresentMode.Fifo, choice.Public);
        Assert.False(choice.FellBack);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void FifoFallsBackToMailbox() {
        VulkanPresentModeChoice choice = VulkanPresentModeSelection.Select(
            [PresentModeKHR.MailboxKhr, PresentModeKHR.ImmediateKhr],
            PresentMode.Fifo);

        Assert.Equal(PresentModeKHR.MailboxKhr, choice.Native);
        Assert.Equal(PresentMode.Mailbox, choice.Public);
        Assert.True(choice.FellBack);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ImmediateIsUsedWhenAvailable() {
        VulkanPresentModeChoice choice = VulkanPresentModeSelection.Select(
            [PresentModeKHR.FifoKhr, PresentModeKHR.ImmediateKhr],
            PresentMode.Immediate);

        Assert.Equal(PresentModeKHR.ImmediateKhr, choice.Native);
        Assert.Equal(PresentMode.Immediate, choice.Public);
        Assert.False(choice.FellBack);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ImmediateDoesNotFallBackAndThrows() {
        GraphicsException exception = Assert.Throws<GraphicsException>(
            () => VulkanPresentModeSelection.Select([PresentModeKHR.FifoKhr], PresentMode.Immediate));

        Assert.Equal(DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED, exception.DiagnosticCode);
        Assert.Contains("Immediate", exception.Message, StringComparison.Ordinal);
        Assert.Contains("FifoKhr", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SurfaceWithoutFifoOrMailboxThrows() {
        GraphicsException exception = Assert.Throws<GraphicsException>(
            () => VulkanPresentModeSelection.Select([], PresentMode.Mailbox));

        Assert.Equal(DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED, exception.DiagnosticCode);
        Assert.Contains("FIFO", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UndefinedPresentModeThrows() {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => VulkanPresentModeSelection.Select([PresentModeKHR.FifoKhr], (PresentMode)99));
    }
}
