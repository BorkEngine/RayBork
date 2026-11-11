using RayBork.Internal;

namespace RayBork.Tests;

public sealed class TickPacerTests {
    [Theory]
    [InlineData(60, false, 60.0)]
    [InlineData(60, true, 30.0)]
    [InlineData(120, false, 120.0)]
    [InlineData(120, true, 30.0)]
    [InlineData(30, false, 30.0)]
    [InlineData(30, true, 30.0)]
    [InlineData(10, false, 10.0)]
    [InlineData(10, true, 10.0)]
    [InlineData(0, false, 0.0)]
    [InlineData(0, true, 30.0)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GetIntervalUsesTheTargetFpsAndCapsLowPowerAtThirty(int targetFps, bool lowPower, double expectedFps) {
        TickPacer pacer = new(targetFps);
        TimeSpan expected = expectedFps > 0.0
            ? TimeSpan.FromSeconds(1.0 / expectedFps)
            : TimeSpan.Zero;

        Assert.Equal(expected, pacer.GetInterval(lowPower));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GetWaitReturnsTheRemainingInterval() {
        TickPacer pacer = new(60);

        TimeSpan wait = pacer.GetWait(TimeSpan.FromMilliseconds(5), lowPower: false);

        Assert.Equal(TimeSpan.FromSeconds(1.0 / 60.0) - TimeSpan.FromMilliseconds(5), wait);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GetWaitIsZeroWhenTheElapsedTimeReachesTheInterval() {
        TickPacer pacer = new(60);

        Assert.Equal(TimeSpan.Zero, pacer.GetWait(TimeSpan.FromSeconds(1.0 / 60.0), lowPower: false));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GetWaitIsZeroWhenTheIntervalHasElapsed() {
        TickPacer pacer = new(60);

        Assert.Equal(TimeSpan.Zero, pacer.GetWait(TimeSpan.FromSeconds(1), lowPower: false));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GetWaitIsZeroForAnUncappedFocusedPacer() {
        TickPacer pacer = new(0);

        Assert.Equal(TimeSpan.Zero, pacer.GetWait(TimeSpan.FromSeconds(1), lowPower: false));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GetWaitUsesTheLowPowerIntervalWhenTheConfiguredRateIsFaster() {
        TickPacer pacer = new(120);

        TimeSpan wait = pacer.GetWait(TimeSpan.FromMilliseconds(5), lowPower: true);

        Assert.Equal(TimeSpan.FromSeconds(1.0 / TickPacer.LOW_POWER_FPS) - TimeSpan.FromMilliseconds(5), wait);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GetWaitUsesTheLowPowerIntervalForAnUncappedPacer() {
        TickPacer pacer = new(0);

        TimeSpan wait = pacer.GetWait(TimeSpan.FromMilliseconds(5), lowPower: true);

        Assert.Equal(TimeSpan.FromSeconds(1.0 / TickPacer.LOW_POWER_FPS) - TimeSpan.FromMilliseconds(5), wait);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void LowPowerFpsIsThirty() {
        Assert.Equal(30, TickPacer.LOW_POWER_FPS);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ConstructorRejectsANegativeTargetFps() {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TickPacer(-1));
    }
}
