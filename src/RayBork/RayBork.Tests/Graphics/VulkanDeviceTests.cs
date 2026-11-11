using Microsoft.Extensions.Logging.Abstractions;
using RayBork.Internal;
using Silk.NET.Vulkan;

namespace RayBork.Tests;

[Collection("SDL")]
public sealed class VulkanDeviceTests {
    private const uint MINIMUM_VULKAN_API_VERSION = (1u << 22) | (2u << 12);

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void DeviceCreationProjectsEngineInfo() {
        using GpuTestHost host = GpuTestHost.CreateOrSkip();

        EngineInfo info = host.Device.CreateEngineInfo();

        Assert.Equal("RayBork", info.Name);
        Assert.Equal(EngineVersion.Current, info.Version);
        Assert.StartsWith("Vulkan 1.", info.RendererName, StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(info.GpuName));
        Assert.True(info.VulkanApiVersion >= MINIMUM_VULKAN_API_VERSION);
        Assert.Equal(string.Empty, info.SwapchainFormat);
        Assert.Equal(0, info.SampleCount);
        Assert.Equal(PresentMode.Mailbox, info.PresentMode);
        Assert.Equal(host.Instance.ValidationLayersEnabled, info.ValidationEnabled);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void DeviceCreationIsValidationClean() {
        using GpuTestHost host = GpuTestHost.CreateOrSkip();

        Assert.Empty(host.Instance.ConsumeValidationErrors());

        host.Device.Dispose();

        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void DeviceDisposalIsRepeatable() {
        using GpuTestHost host = GpuTestHost.CreateOrSkip();

        for (int index = 0; index < 3; index++) {
            using VulkanDevice device = VulkanDevice.Create(host.Instance, host.Surface, NullLogger.Instance);
            Assert.True(device.Device.Handle != 0);
        }

        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void CapabilitiesDescribeTheSurface() {
        using GpuTestHost host = GpuTestHost.CreateOrSkip();

        VulkanDeviceCapabilities capabilities = host.Device.Capabilities;

        Assert.NotEmpty(capabilities.Swapchain.Formats);
        Assert.NotEmpty(capabilities.Swapchain.PresentModes);
        Assert.True(capabilities.Swapchain.IsUsable);
        Assert.Contains(1, capabilities.SupportedSampleCounts);
        Assert.True(capabilities.MaxFramebufferWidth > 0);
        Assert.True(capabilities.MaxFramebufferHeight > 0);
        Assert.False(string.IsNullOrEmpty(capabilities.DeviceName));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void SurfaceReportsSwapchainFormatPairs() {
        using GpuTestHost host = GpuTestHost.CreateOrSkip();

        IReadOnlyList<VulkanSurfaceFormat> formats = host.Device.Capabilities.SurfaceFormats;

        Assert.NotEmpty(formats);
        Assert.All(formats, pair => Assert.NotEqual(Format.Undefined, pair.Format));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void HdrFloatFormatIsReportedWhenColorSpacesAreAvailable() {
        using GpuTestHost host = GpuTestHost.CreateOrSkip();
        VulkanDeviceCapabilities capabilities = host.Device.Capabilities;

        if (capabilities.SwapchainColorSpaceExtensionEnabled) {
            if (!capabilities.SupportsHdrFloatFormat) {
                Assert.Skip("The driver reports no scRGB float surface pair.");
            }
        } else {
            Assert.False(capabilities.SupportsExtendedSrgbLinear);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ValidationDisabledPath() {
        using GpuTestHost host = GpuTestHost.CreateOrSkip(requestValidation: false);

        Assert.False(host.Instance.ValidationLayersEnabled);
        Assert.False(host.Instance.DebugMessengerEnabled);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void InstanceRejectsMissingSurfaceExtension() {
        if (!GpuTestHost.IsVulkanLoaderAvailable()) {
            Assert.Skip("The Vulkan loader is not available on this host.");
        }

        GraphicsException exception = Assert.Throws<GraphicsException>(() => VulkanInstance.Create(new VulkanInstanceOptions {
            ApplicationName = "RayBork GPU Test",
            ApplicationVersion = EngineVersion.Current,
            SurfaceExtensions = ["VK_KHR_surface", "VK_KHR_not_a_real_surface"],
            RequestValidation = false,
            Logger = NullLogger.Instance,
        }));

        Assert.Equal(DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED, exception.DiagnosticCode);
        Assert.Contains("VK_KHR_not_a_real_surface", exception.Message, StringComparison.Ordinal);
        Assert.Contains("VK_KHR_surface", exception.Message, StringComparison.Ordinal);
    }
}
