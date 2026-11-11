using RayBork.Internal;

namespace RayBork.Tests;

/// <summary>
/// GPU tests for the <see cref="Vulkan12Backend"/> recovery contract: swapchain, surface, and device
/// recreation, the recovery bound, and the fatal path.
/// </summary>
/// <remarks>
/// The tests exercise the production recovery path through <see cref="Vulkan12Backend.Recover"/> or
/// through the test-only fault injection that routes the next frame call through the same code. They
/// assert validation-layer cleanliness, statistics, and the surface-provider counters instead of
/// pixels. Every test joins the serialized <c>SDL</c> collection because SDL's video driver and
/// Vulkan initialization are process global.
/// </remarks>
[Collection("SDL")]
public sealed class Vulkan12BackendRecoveryTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ForcedSwapchainOutOfDateRecoversAndIncrementsRecoveryCount() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        RenderFrame(host, 1);
        int recoveriesBefore = host.Backend.Stats.RecoveryCount;

        bool recovered = host.Backend.Recover(RenderRecoveryReason.SwapchainOutOfDate);

        Assert.True(recovered);
        Assert.Equal(recoveriesBefore + 1, host.Backend.Stats.RecoveryCount);
        RenderFrame(host, 2);
        Assert.Equal(1, host.Backend.Stats.DrawCalls);
        Assert.Equal(recoveriesBefore + 1, host.Backend.Stats.RecoveryCount);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void SimulatedSwapchainOutOfDateRecoversOnTheNextSubmit() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        RenderFrame(host, 1);
        int recoveriesBefore = host.Backend.Stats.RecoveryCount;

        host.Backend.SimulateSwapchainOutOfDateForTests();
        RenderFrame(host, 2);

        Assert.Equal(recoveriesBefore + 1, host.Backend.Stats.RecoveryCount);
        Assert.Equal(1, host.Backend.Stats.DrawCalls);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void SimulatedDeviceLossRecoversOnTheNextSubmit() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        RenderFrame(host, 1);
        EngineInfo infoBefore = host.Backend.Info;

        host.Backend.SimulateDeviceLossForTests();
        RenderFrame(host, 2);

        Assert.Equal(1, host.Backend.Stats.RecoveryCount);
        Assert.Equal(1, host.Backend.Stats.DrawCalls);
        Assert.Equal(infoBefore.GpuName, host.Backend.Info.GpuName);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void SimulatedSurfaceLossRecoversOnTheNextSubmit() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        RenderFrame(host, 1);
        int createdBefore = host.SurfaceProvider.CreateCount;
        int destroyedBefore = host.SurfaceProvider.DestroyCount;

        host.Backend.SimulateSurfaceLossForTests();
        RenderFrame(host, 2);

        Assert.Equal(1, host.Backend.Stats.RecoveryCount);
        Assert.Equal(1, host.Backend.Stats.DrawCalls);
        Assert.Equal(destroyedBefore + 1, host.SurfaceProvider.DestroyCount);
        Assert.Equal(createdBefore + 1, host.SurfaceProvider.CreateCount);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ForcedSurfaceLossRecreatesTheSurface() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        RenderFrame(host, 1);
        int recoveriesBefore = host.Backend.Stats.RecoveryCount;
        int createdBefore = host.SurfaceProvider.CreateCount;
        int destroyedBefore = host.SurfaceProvider.DestroyCount;

        bool recovered = host.Backend.Recover(RenderRecoveryReason.SurfaceLost);

        Assert.True(recovered);
        Assert.Equal(destroyedBefore + 1, host.SurfaceProvider.DestroyCount);
        Assert.Equal(createdBefore + 1, host.SurfaceProvider.CreateCount);
        Assert.Equal(recoveriesBefore + 1, host.Backend.Stats.RecoveryCount);
        RenderFrame(host, 2);
        Assert.Equal(1, host.Backend.Stats.DrawCalls);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ForcedDeviceLossRecreatesTheDeviceAndResources() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        RenderFrame(host, 1);
        EngineInfo infoBefore = host.Backend.Info;
        int recoveriesBefore = host.Backend.Stats.RecoveryCount;

        bool recovered = host.Backend.Recover(RenderRecoveryReason.DeviceLost);

        Assert.True(recovered);
        EngineInfo infoAfter = host.Backend.Info;
        Assert.Equal(infoBefore.GpuName, infoAfter.GpuName);
        Assert.Equal(host.Device.Capabilities.DeviceName, infoAfter.GpuName);
        Assert.False(string.IsNullOrEmpty(infoAfter.SwapchainFormat));
        Assert.True(infoAfter.SampleCount > 0);
        Assert.True(Enum.IsDefined(infoAfter.PresentMode));
        Assert.Equal(recoveriesBefore + 1, host.Backend.Stats.RecoveryCount);
        RenderFrame(host, 2);
        Assert.Equal(1, host.Backend.Stats.DrawCalls);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void RepeatedDeviceLossIsBoundedAndBecomesFatal() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        RenderFrame(host, 1);

        for (int attempt = 1; attempt <= Vulkan12Backend.MAX_CONSECUTIVE_RECOVERIES; attempt++) {
            Assert.True(host.Backend.Recover(RenderRecoveryReason.DeviceLost));
        }

        Assert.False(host.Backend.Recover(RenderRecoveryReason.DeviceLost));
        host.Backend.SimulateDeviceLossForTests();
        GraphicsDeviceLostException exception = Assert.Throws<GraphicsDeviceLostException>(
            () => host.Backend.Submit(new RenderFramePacket(2)));
        Assert.Equal(DiagnosticCodes.GRAPHICS_DEVICE_LOST, exception.DiagnosticCode);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
        Assert.Null(Record.Exception(host.Dispose));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void UnrecoverableFailureIsFatal() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);

        host.Backend.SimulateUnrecoverableFailureForTests();
        GraphicsDeviceLostException exception = Assert.Throws<GraphicsDeviceLostException>(
            () => host.Backend.Submit(new RenderFramePacket(1)));

        Assert.Equal(DiagnosticCodes.GRAPHICS_DEVICE_LOST, exception.DiagnosticCode);
        Assert.Null(Record.Exception(host.Dispose));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void RecoveryIsValidationClean() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        RenderFrame(host, 1);

        Assert.True(host.Backend.Recover(RenderRecoveryReason.SwapchainOutOfDate));
        Assert.True(host.Backend.Recover(RenderRecoveryReason.SurfaceLost));
        Assert.True(host.Backend.Recover(RenderRecoveryReason.DeviceLost));

        Assert.Equal(3, host.Backend.Stats.RecoveryCount);
        RenderFrame(host, 2);
        Assert.Equal(3, host.Backend.Stats.RecoveryCount);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void DisposeAfterRecoveryIsClean() {
        BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        RenderFrame(host, 1);
        Assert.True(host.Backend.Recover(RenderRecoveryReason.DeviceLost));
        Assert.Empty(host.Instance.ConsumeValidationErrors());

        Assert.Null(Record.Exception(host.Dispose));
    }

    private static void RenderFrame(BackendTestHost host, long frameIndex) {
        host.Backend.Submit(new RenderFramePacket(frameIndex));
        host.Backend.Present();
    }
}
