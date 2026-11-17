using RayBork.Internal;

namespace RayBork.Tests;

/// <summary>
/// Covers the platform-level gamepad paths that the dummy SDL video driver can prove. No physical
/// device is required: the platform is created without opening gamepads, so capture is empty and
/// rumble requests for instance ids that the platform does not own are ignored.
/// </summary>
[Collection("SDL")]
public sealed class GamepadPlatformTests
{
    private const int UNKNOWN_INSTANCE_ID = 0x7FFFFFFF;

    // A uint instance id above int.MaxValue arrives as a negative int through IGamepad.InstanceId.
    private const int HIGH_BIT_INSTANCE_ID = -1;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void VibrationRequestsForUnknownInstanceIdsAreIgnored()
    {
        using SdlTestHost host = SdlTestHost.Create();

        host.Platform.SetGamepadVibration(UNKNOWN_INSTANCE_ID, 1f, 1f, TimeSpan.FromSeconds(1));
        host.Platform.SetGamepadVibration(
            HIGH_BIT_INSTANCE_ID,
            0.5f,
            0.25f,
            TimeSpan.FromSeconds(0.1)
        );
        host.Platform.StopGamepadVibration(UNKNOWN_INSTANCE_ID);
        host.Platform.StopGamepadVibration(HIGH_BIT_INSTANCE_ID);

        host.Platform.Pump(TimeSpan.Zero);

        Assert.Empty(host.Platform.CaptureInput().Gamepads);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CaptureWithoutOpenGamepadsReturnsNoHandles()
    {
        using SdlTestHost host = SdlTestHost.Create();

        IInput input = host.Platform.CaptureInput();

        Assert.Empty(input.Gamepads);
        Assert.Null(input.GetGamepad(0));
        Assert.Null(input.GetGamepad(-1));
    }
}
