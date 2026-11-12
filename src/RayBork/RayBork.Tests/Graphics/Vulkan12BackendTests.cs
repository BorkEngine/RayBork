using RayBork.Internal;
using Silk.NET.Vulkan;

namespace RayBork.Tests;

[Collection("SDL")]
public sealed class Vulkan12BackendTests {
    private static readonly Color _clearColor = new(0.25f, 0.5f, 0.75f, 1f);

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void BackendInitializesAndReportsInfo() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        VulkanDeviceCapabilities capabilities = host.Device.Capabilities;
        VulkanSwapchainFormat expectedFormat = VulkanSwapchainFormatSelection.Select(
            capabilities.SurfaceFormats,
            WindowHdrState.Sdr);
        VulkanPresentModeChoice expectedPresentMode = VulkanPresentModeSelection.Select(
            capabilities.Swapchain.PresentModes,
            host.Settings.PresentMode);
        int expectedSampleCount = VulkanSampleCountSelection.Select(
            host.Settings.SampleCount,
            capabilities.SupportedSampleCounts);

        EngineInfo info = host.Backend.Info;

        Assert.Equal("RayBork", info.Name);
        Assert.Equal(EngineVersion.Current, info.Version);
        Assert.StartsWith("Vulkan 1.", info.RendererName, StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(info.GpuName));
        Assert.Equal(expectedFormat.Description, info.SwapchainFormat);
        Assert.Equal(expectedFormat.IsHdr, info.HdrOutput);
        Assert.Equal(expectedSampleCount, info.SampleCount);
        Assert.Equal(expectedPresentMode.Public, info.PresentMode);
        Assert.Equal(host.Instance.ValidationLayersEnabled, info.ValidationEnabled);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void BindlessCapabilitiesAndTableAreReported() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        VulkanDeviceCapabilities capabilities = host.Device.Capabilities;
        VulkanBindlessTextureTable table = host.Backend.BindlessTextures;

        Assert.True(capabilities.DescriptorIndexing);
        Assert.True(capabilities.BindlessTextureCapacity >= 1);
        Assert.True(capabilities.BindlessTextureCapacity <= VulkanDeviceCapabilities.TARGET_BINDLESS_TEXTURE_CAPACITY);
        Assert.Equal(capabilities.BindlessTextureCapacity, table.Capacity);
        Assert.Equal(1, table.LiveCount);
        Assert.NotEqual(0ul, table.WhiteImage.Handle);
        Assert.NotEqual(0ul, table.WhiteView.Handle);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void HdrWindowStateSelectsScRgbWhenSupported() {
        WindowHdrState hdr = new(true, 2f, 6.25f);
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: hdr);
        EngineInfo info = host.Backend.Info;

        if (info.HdrOutput) {
            Assert.Equal("R16G16B16A16_SFLOAT", info.SwapchainFormat);
        } else {
            Assert.False(host.Device.Capabilities.SupportsHdrFloatFormat);
            Assert.Contains(
                info.SwapchainFormat,
                new[] { "B8G8R8A8_SRGB", "R8G8B8A8_SRGB", "B8G8R8A8_UNORM", "R8G8B8A8_UNORM" });
        }

        RenderFrame(host, 1);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void SubmitAndPresentThreeFramesAreValidationClean() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);

        for (int frame = 1; frame <= 3; frame++) {
            RenderFrame(host, frame);
        }

        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void StatsDescribeTheFrame() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        const long frameIndex = 42;

        host.Backend.Submit(BackendPacketTestHelper.CreateClearingPacket(host, frameIndex, _clearColor));
        RenderStats stats = host.Backend.Stats;
        EngineInfo info = host.Backend.Info;

        Assert.Equal(frameIndex, stats.FrameIndex);
        Assert.Equal(0, stats.DrawCalls);
        Assert.Equal(0, stats.Instances);
        Assert.Equal(0, stats.Triangles);
        Assert.Equal(0, stats.CulledInstances);
        Assert.Equal(0, stats.PipelineCount);
        Assert.Equal(1, stats.TextureCount);
        Assert.Equal(info.SampleCount, stats.SampleCount);
        Assert.Equal(info.HdrOutput, stats.HdrOutput);
        Assert.True(stats.CpuFrameMilliseconds >= 0);
        Assert.InRange(stats.FramesInFlight, 0, 3);
        Assert.Equal(0, stats.RecoveryCount);
        host.Backend.Present();
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void SceneColorClearIsTheCameraPassColor() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        RenderFrame(host, 1);
        Extent2D extent = host.SceneTarget.Extent;

        AssertColorNear(_clearColor, ReadColor(host, 1, 1), 1e-4f);
        AssertColorNear(_clearColor, ReadColor(host, extent.Width - 2, extent.Height - 2), 1e-4f);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void HdrClearColorAboveOneIsStoredLiterally() {
        Color hdrClear = new(4f, 2f, 0.5f, 1f);
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        host.Backend.Submit(BackendPacketTestHelper.CreateClearingPacket(host, 1, hdrClear));
        host.Backend.Present();

        AssertColorNear(hdrClear, ReadColor(host, 8, 8), 1e-3f);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ClearOnlyColorKeepsDepthUndefined() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(sampleCount: 1, hdr: WindowHdrState.Sdr);
        BackendPacketTestHelper.SubmitClearingFrame(host, 1, _clearColor, ClearMode.Color);

        AssertColorNear(_clearColor, ReadColor(host, 1, 1), 1e-4f);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void DepthIsClearedToZero() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(sampleCount: 1, hdr: WindowHdrState.Sdr);
        RenderFrame(host, 1);
        Extent2D extent = host.SceneTarget.Extent;

        Assert.Equal(0f, ReadDepth(host, 1, 1));
        Assert.Equal(0f, ReadDepth(host, extent.Width / 2, extent.Height / 2));
        Assert.Equal(0f, ReadDepth(host, extent.Width - 2, extent.Height - 2));
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Theory]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    [InlineData(4)]
    [InlineData(1)]
    [InlineData(8)]
    public void MsaaResolveProducesTheClearColor(int requestedSampleCount) {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(
            sampleCount: requestedSampleCount,
            hdr: WindowHdrState.Sdr);
        RenderFrame(host, 1);
        Extent2D extent = host.SceneTarget.Extent;

        Assert.Equal(host.Backend.Info.SampleCount, host.SceneTarget.SampleCount);
        AssertColorNear(_clearColor, ReadColor(host, extent.Width / 2, extent.Height / 2), 1e-3f);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ResizeRecreatesThePresentationPath() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        RenderFrame(host, 1);
        EngineInfo infoBefore = host.Backend.Info;

        if (!TryResizeWindow(host, 400, 300)) {
            Assert.Skip("The host did not apply the SDL window resize synchronously.");
        }

        host.SyncWithWindow();
        host.Backend.Resize(400, 300);
        RenderFrame(host, 2);

        Assert.Equal(400u, host.SceneTarget.Extent.Width);
        Assert.Equal(300u, host.SceneTarget.Extent.Height);
        AssertColorNear(_clearColor, ReadColor(host, 1, 1), 1e-3f);
        Assert.Equal(infoBefore, host.Backend.Info);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void MinimizedSurfaceIdles() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        host.State = host.State with { IsMinimized = true };

        host.Backend.Submit(BackendPacketTestHelper.CreateClearingPacket(host, 1, _clearColor));
        host.Backend.Present();
        Assert.Equal(0, host.Backend.Stats.DrawCalls);

        host.State = host.State with { IsMinimized = false };
        RenderFrame(host, 2);
        Assert.Equal(0, host.Backend.Stats.DrawCalls);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void HdrStateChangeRecreatesThePath() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        RenderFrame(host, 1);
        Assert.False(host.Backend.Info.HdrOutput);

        host.State = host.State with { Hdr = new WindowHdrState(true, 2f, 6.25f) };
        RenderFrame(host, 2);

        Assert.Equal(host.Device.Capabilities.SupportsHdrFloatFormat, host.Backend.Info.HdrOutput);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    private static void RenderFrame(BackendTestHost host, long frameIndex) {
        BackendPacketTestHelper.SubmitClearingFrame(host, frameIndex, _clearColor);
    }

    private static bool TryResizeWindow(BackendTestHost host, int width, int height) {
        host.Platform.Window.SetSize(width, height);
        for (int attempt = 0; attempt < 200; attempt++) {
            host.Platform.Pump(TimeSpan.Zero);
            if (host.Platform.Window.RenderWidth == width && host.Platform.Window.RenderHeight == height) {
                return true;
            }
        }

        return false;
    }

    private static (float R, float G, float B) ReadColor(BackendTestHost host, uint x, uint y) {
        VulkanReadback.ReadSceneColorPixel(host.Device, host.SceneTarget, x, y, out float r, out float g, out float b);
        return (r, g, b);
    }

    private static float ReadDepth(BackendTestHost host, uint x, uint y) {
        VulkanReadback.ReadSceneDepthPixel(host.Device, host.SceneTarget, x, y, out float depth);
        return depth;
    }

    private static void AssertColorNear(Color expected, (float R, float G, float B) actual, float tolerance) {
        Assert.True(
            MathF.Abs(expected.R - actual.R) <= tolerance,
            $"Red {actual.R} is not within {tolerance} of {expected.R}.");
        Assert.True(
            MathF.Abs(expected.G - actual.G) <= tolerance,
            $"Green {actual.G} is not within {tolerance} of {expected.G}.");
        Assert.True(
            MathF.Abs(expected.B - actual.B) <= tolerance,
            $"Blue {actual.B} is not within {tolerance} of {expected.B}.");
    }
}
