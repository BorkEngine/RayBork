using RayBork.Internal;

namespace RayBork.Tests;

public sealed class TickRunnerTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CalculateWaitUsesTheTimeSinceTheCurrentTickStarted() {
        ManualTimeProvider time = new();
        TickPacer pacer = new(100);
        long tickStartedAt = time.GetTimestamp();
        time.Advance(TimeSpan.FromMilliseconds(6));

        TimeSpan wait = TickRunner.CalculateWait(time, tickStartedAt, pacer, lowPower: false);

        Assert.Equal(TimeSpan.FromMilliseconds(4), wait);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CalculateWaitDoesNotUseThePreviousTickInterval() {
        ManualTimeProvider time = new();
        TickPacer pacer = new(60);
        long tickStartedAt = time.GetTimestamp();
        time.Advance(TimeSpan.FromMilliseconds(1));

        TimeSpan wait = TickRunner.CalculateWait(time, tickStartedAt, pacer, lowPower: false);

        double expected = (1.0 / 60.0) - 0.001;
        Assert.Equal(expected, wait.TotalSeconds, precision: 6);
        Assert.True(wait < TimeSpan.FromSeconds(1.0 / 60.0), "The previous tick interval must not be used as the elapsed time.");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CalculateWaitReturnsZeroWhenTheTickOverrunsTheInterval() {
        ManualTimeProvider time = new();
        TickPacer pacer = new(100);
        long tickStartedAt = time.GetTimestamp();
        time.Advance(TimeSpan.FromMilliseconds(25));

        TimeSpan wait = TickRunner.CalculateWait(time, tickStartedAt, pacer, lowPower: false);

        Assert.Equal(TimeSpan.Zero, wait);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CalculateWaitAppliesTheLowPowerThirtyFpsCap() {
        ManualTimeProvider time = new();
        TickPacer pacer = new(1000);
        long tickStartedAt = time.GetTimestamp();
        time.Advance(TimeSpan.FromMilliseconds(10));

        TimeSpan wait = TickRunner.CalculateWait(time, tickStartedAt, pacer, lowPower: true);

        double expected = (1.0 / TickPacer.LOW_POWER_FPS) - 0.010;
        Assert.Equal(expected, wait.TotalSeconds, precision: 6);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CalculateWaitRejectsNullDependencies() {
        ManualTimeProvider time = new();
        TickPacer pacer = new(60);

        Assert.Throws<ArgumentNullException>(() => TickRunner.CalculateWait(null!, 0, pacer, lowPower: false));
        Assert.Throws<ArgumentNullException>(() => TickRunner.CalculateWait(time, 0, null!, lowPower: false));
    }
}
