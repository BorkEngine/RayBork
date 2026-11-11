using RayBork.Internal;
using Silk.NET.Vulkan;

namespace RayBork.Tests;

public sealed class VulkanSwapchainFormatSelectionTests {
    private static readonly ColorSpaceKHR _srgb = ColorSpaceKHR.SpaceSrgbNonlinearKhr;
    private static readonly ColorSpaceKHR _scrgb = ColorSpaceKHR.SpaceExtendedSrgbLinearExt;
    private static readonly ColorSpaceKHR _hdr10 = ColorSpaceKHR.SpaceHdr10ST2084Ext;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void HdrPrefersTheExtendedSrgbLinearFloatPair() {
        VulkanSurfaceFormat[] formats = [
            new(Format.B8G8R8A8Srgb, _srgb),
            new(Format.R16G16B16A16Sfloat, _srgb),
            new(Format.R16G16B16A16Sfloat, _scrgb),
        ];

        VulkanSwapchainFormat selected = VulkanSwapchainFormatSelection.Select(formats, Hdr());

        Assert.Equal(Format.R16G16B16A16Sfloat, selected.Format);
        Assert.Equal(_scrgb, selected.ColorSpace);
        Assert.Equal(PresentEncoding.ScRgbHdr, selected.Encoding);
        Assert.True(selected.IsHdr);
        Assert.Equal("R16G16B16A16_SFLOAT", selected.Description);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void HdrUsesTheSrgbNonlinearFloatPairWhenExtendedLinearIsMissing() {
        VulkanSurfaceFormat[] formats = [
            new(Format.R16G16B16A16Sfloat, _srgb),
            new(Format.B8G8R8A8Srgb, _srgb),
        ];

        VulkanSwapchainFormat selected = VulkanSwapchainFormatSelection.Select(formats, Hdr());

        Assert.Equal(Format.R16G16B16A16Sfloat, selected.Format);
        Assert.Equal(_srgb, selected.ColorSpace);
        Assert.Equal(PresentEncoding.ScRgbHdr, selected.Encoding);
        Assert.True(selected.IsHdr);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void HdrFallsBackToSdrWhenNoFloatPairExists() {
        VulkanSurfaceFormat[] formats = [new(Format.B8G8R8A8Srgb, _srgb)];

        VulkanSwapchainFormat selected = VulkanSwapchainFormatSelection.Select(formats, Hdr());

        Assert.Equal(Format.B8G8R8A8Srgb, selected.Format);
        Assert.Equal(PresentEncoding.LinearClamp, selected.Encoding);
        Assert.False(selected.IsHdr);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SdrPrefersBgra8SrgbOverEveryOtherPair() {
        VulkanSurfaceFormat[] formats = [
            new(Format.R16G16B16A16Sfloat, _scrgb),
            new(Format.R8G8B8A8Unorm, _srgb),
            new(Format.B8G8R8A8Unorm, _srgb),
            new(Format.R8G8B8A8Srgb, _srgb),
            new(Format.B8G8R8A8Srgb, _srgb),
        ];

        VulkanSwapchainFormat selected = VulkanSwapchainFormatSelection.Select(formats, WindowHdrState.Sdr);

        Assert.Equal(Format.B8G8R8A8Srgb, selected.Format);
        Assert.Equal(PresentEncoding.LinearClamp, selected.Encoding);
        Assert.False(selected.IsHdr);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SdrFallsBackToRgba8Srgb() {
        VulkanSurfaceFormat[] formats = [new(Format.R8G8B8A8Srgb, _srgb), new(Format.B8G8R8A8Unorm, _srgb)];

        VulkanSwapchainFormat selected = VulkanSwapchainFormatSelection.Select(formats, WindowHdrState.Sdr);

        Assert.Equal(Format.R8G8B8A8Srgb, selected.Format);
        Assert.Equal(PresentEncoding.LinearClamp, selected.Encoding);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SdrFallsBackToBgra8UnormWithManualSrgbEncoding() {
        VulkanSurfaceFormat[] formats = [new(Format.B8G8R8A8Unorm, _srgb), new(Format.R8G8B8A8Unorm, _srgb)];

        VulkanSwapchainFormat selected = VulkanSwapchainFormatSelection.Select(formats, WindowHdrState.Sdr);

        Assert.Equal(Format.B8G8R8A8Unorm, selected.Format);
        Assert.Equal(PresentEncoding.ManualSrgb, selected.Encoding);
        Assert.False(selected.IsHdr);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SdrFallsBackToRgba8UnormWithManualSrgbEncoding() {
        VulkanSurfaceFormat[] formats = [new(Format.R8G8B8A8Unorm, _srgb)];

        VulkanSwapchainFormat selected = VulkanSwapchainFormatSelection.Select(formats, WindowHdrState.Sdr);

        Assert.Equal(Format.R8G8B8A8Unorm, selected.Format);
        Assert.Equal(PresentEncoding.ManualSrgb, selected.Encoding);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SdrFallbackUsesTheFloatPairWithLinearClamp() {
        VulkanSurfaceFormat[] formats = [new(Format.R16G16B16A16Sfloat, _srgb)];

        VulkanSwapchainFormat selected = VulkanSwapchainFormatSelection.Select(formats, WindowHdrState.Sdr);

        Assert.Equal(Format.R16G16B16A16Sfloat, selected.Format);
        Assert.Equal(PresentEncoding.LinearClamp, selected.Encoding);
        Assert.False(selected.IsHdr);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void LegacyUndefinedEntryMeansAnyFormat() {
        VulkanSurfaceFormat[] formats = [new(Format.Undefined, default)];

        VulkanSwapchainFormat selected = VulkanSwapchainFormatSelection.Select(formats, WindowHdrState.Sdr);

        Assert.Equal(Format.B8G8R8A8Srgb, selected.Format);
        Assert.Equal(_srgb, selected.ColorSpace);
        Assert.Equal(PresentEncoding.LinearClamp, selected.Encoding);
        Assert.False(selected.IsHdr);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void NoUsablePairThrowsWithTheAvailablePairsInTheMessage() {
        VulkanSurfaceFormat[] formats = [new(Format.A2B10G10R10UnormPack32, _hdr10)];

        GraphicsException exception = Assert.Throws<GraphicsException>(
            () => VulkanSwapchainFormatSelection.Select(formats, Hdr()));

        Assert.Equal(DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED, exception.DiagnosticCode);
        Assert.Contains("A2B10G10R10_UNORM_PACK32", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Hdr10ST2084Ext", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EmptySurfaceFormatListThrows() {
        GraphicsException exception = Assert.Throws<GraphicsException>(
            () => VulkanSwapchainFormatSelection.Select([], WindowHdrState.Sdr));

        Assert.Equal(DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED, exception.DiagnosticCode);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void Hdr10IsNeverSelected() {
        VulkanSurfaceFormat[] formats = [
            new(Format.A2B10G10R10UnormPack32, _hdr10),
            new(Format.B8G8R8A8Srgb, _srgb),
        ];

        VulkanSwapchainFormat selected = VulkanSwapchainFormatSelection.Select(formats, Hdr());

        Assert.Equal(Format.B8G8R8A8Srgb, selected.Format);
        Assert.False(selected.IsHdr);
    }

    private static WindowHdrState Hdr() => new(true, 2f, 6.25f);
}
