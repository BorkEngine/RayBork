using RayBork.Internal;

namespace RayBork.Tests.Assets.Gpu;

/// <summary>
/// GPU tests for the logical-device generation that gates GPU asset restoration.
/// </summary>
[Collection("SDL")]
public sealed class DeviceGenerationTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void GenerationChangesOnlyWhenTheLogicalDeviceIsRecreated() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        host.Backend.Submit(new RenderFramePacket(1));
        host.Backend.Present();
        int initial = host.Backend.DeviceGeneration;
        Assert.Equal(1, initial);

        Assert.True(host.Backend.Recover(RenderRecoveryReason.SwapchainOutOfDate));
        Assert.Equal(initial, host.Backend.DeviceGeneration);

        Assert.True(host.Backend.Recover(RenderRecoveryReason.SurfaceLost));
        Assert.Equal(initial, host.Backend.DeviceGeneration);

        Assert.True(host.Backend.Recover(RenderRecoveryReason.DeviceLost));
        Assert.Equal(initial + 1, host.Backend.DeviceGeneration);

        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }
}
