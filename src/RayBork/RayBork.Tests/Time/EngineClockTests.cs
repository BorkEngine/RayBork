using RayBork.Internal;

namespace RayBork.Tests;

public sealed class EngineClockTests {
    private const double MAX_DELTA_TIME = 0.25;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ConstructorRejectsNullTimeProvider() {
        Assert.Throws<ArgumentNullException>(() => new EngineClock(null!, MAX_DELTA_TIME));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ConstructorRejectsInvalidMaxDeltaTime(double maxDeltaTime) {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EngineClock(new ManualTimeProvider(), maxDeltaTime));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void BeginTickBeforeStartThrows() {
        EngineClock clock = new(new ManualTimeProvider(), MAX_DELTA_TIME);

        Assert.Throws<InvalidOperationException>(() => clock.BeginTick());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void StartTwiceThrows() {
        EngineClock clock = new(new ManualTimeProvider(), MAX_DELTA_TIME);
        clock.Start();

        Assert.Throws<InvalidOperationException>(() => clock.Start());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void FirstTickHasZeroDeltasAndStartsFrameCountAtOne() {
        ManualTimeProvider time = new();
        EngineClock clock = new(time, MAX_DELTA_TIME);
        clock.Start();
        time.Advance(TimeSpan.FromMilliseconds(100));

        TickTime tick = clock.BeginTick();

        Assert.Equal(TimeSpan.FromMilliseconds(100).TotalSeconds, tick.Time);
        Assert.Equal(0.0, tick.DeltaTime);
        Assert.Equal(0.0, tick.UnscaledDeltaTime);
        Assert.Equal(1L, tick.FrameCount);
        Assert.Equal(0.0, tick.Fps);
        Assert.True(tick.IsFirstTick);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void LaterTicksReportTheRawIntervalSinceThePreviousTick() {
        ManualTimeProvider time = new();
        EngineClock clock = new(time, MAX_DELTA_TIME);
        clock.Start();
        clock.BeginTick();

        time.Advance(TimeSpan.FromMilliseconds(16));
        TickTime second = clock.BeginTick();
        time.Advance(TimeSpan.FromMilliseconds(20));
        TickTime third = clock.BeginTick();

        Assert.Equal(TimeSpan.FromMilliseconds(16).TotalSeconds, second.Time);
        Assert.Equal(TimeSpan.FromMilliseconds(16).TotalSeconds, second.DeltaTime);
        Assert.Equal(TimeSpan.FromMilliseconds(16).TotalSeconds, second.UnscaledDeltaTime);
        Assert.Equal(2L, second.FrameCount);
        Assert.False(second.IsFirstTick);

        Assert.Equal(TimeSpan.FromMilliseconds(36).TotalSeconds, third.Time);
        Assert.Equal(TimeSpan.FromMilliseconds(20).TotalSeconds, third.DeltaTime);
        Assert.Equal(TimeSpan.FromMilliseconds(20).TotalSeconds, third.UnscaledDeltaTime);
        Assert.Equal(3L, third.FrameCount);
        Assert.False(third.IsFirstTick);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DeltaIsClampedWhileUnscaledDeltaAndTimeAreNot() {
        ManualTimeProvider time = new();
        EngineClock clock = new(time, MAX_DELTA_TIME);
        clock.Start();
        clock.BeginTick();
        time.Advance(TimeSpan.FromSeconds(2));

        TickTime tick = clock.BeginTick();

        Assert.Equal(MAX_DELTA_TIME, tick.DeltaTime);
        Assert.Equal(2.0, tick.UnscaledDeltaTime);
        Assert.Equal(2.0, tick.Time);
        Assert.Equal(2L, tick.FrameCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TimeProgressesIndependentlyOfClampedDeltas() {
        ManualTimeProvider time = new();
        EngineClock clock = new(time, MAX_DELTA_TIME);
        clock.Start();

        TickTime initial = clock.BeginTick();
        time.Advance(TimeSpan.FromSeconds(1));
        TickTime first = clock.BeginTick();
        time.Advance(TimeSpan.FromSeconds(1));
        TickTime second = clock.BeginTick();

        Assert.Equal(0.0, initial.DeltaTime);
        Assert.Equal(1.0, first.Time);
        Assert.Equal(2.0, second.Time);
        Assert.Equal(MAX_DELTA_TIME, first.DeltaTime);
        Assert.Equal(MAX_DELTA_TIME, second.DeltaTime);
        Assert.True(first.DeltaTime + second.DeltaTime < second.Time);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TimePropertyIsZeroBeforeStart() {
        EngineClock clock = new(new ManualTimeProvider(), MAX_DELTA_TIME);

        Assert.Equal(0.0, clock.Time);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TimePropertyAdvancesWithoutTicksAndTicksSnapshotIt() {
        ManualTimeProvider time = new();
        EngineClock clock = new(time, MAX_DELTA_TIME);
        clock.Start();

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1.0, clock.Time);

        TickTime first = clock.BeginTick();
        time.Advance(TimeSpan.FromMilliseconds(250));

        Assert.Equal(1.25, clock.Time);
        Assert.Equal(1.0, first.Time);

        TickTime second = clock.BeginTick();

        Assert.Equal(1.25, second.Time);
        Assert.Equal(TimeSpan.FromMilliseconds(250).TotalSeconds, second.DeltaTime);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void FpsIsTheAverageOfTheOneSecondWindow() {
        ManualTimeProvider time = new();
        EngineClock clock = new(time, MAX_DELTA_TIME);
        clock.Start();

        TickTime tick = default;
        for (int i = 0; i < 12; i++) {
            tick = clock.BeginTick();
            if (i < 11) {
                time.Advance(TimeSpan.FromMilliseconds(100));
            }
        }

        Assert.Equal(10.0, tick.Fps, precision: 6);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void FpsKeepsTwoSamplesWhenTheWindowIsStale() {
        ManualTimeProvider time = new();
        EngineClock clock = new(time, MAX_DELTA_TIME);
        clock.Start();
        clock.BeginTick();
        time.Advance(TimeSpan.FromSeconds(5));

        TickTime tick = clock.BeginTick();

        Assert.Equal(0.2, tick.Fps, precision: 6);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void FpsDropsSamplesOlderThanTheWindow() {
        ManualTimeProvider time = new();
        EngineClock clock = new(time, MAX_DELTA_TIME);
        clock.Start();
        clock.BeginTick();
        time.Advance(TimeSpan.FromMilliseconds(100));
        clock.BeginTick();
        time.Advance(TimeSpan.FromMilliseconds(100));
        clock.BeginTick();
        time.Advance(TimeSpan.FromSeconds(2));

        TickTime tick = clock.BeginTick();

        Assert.Equal(0.5, tick.Fps, precision: 6);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void FpsIsZeroWhenTickTimestampsDoNotAdvance() {
        ManualTimeProvider time = new();
        EngineClock clock = new(time, MAX_DELTA_TIME);
        clock.Start();
        time.Advance(TimeSpan.FromMilliseconds(16));

        TickTime first = clock.BeginTick();
        TickTime second = clock.BeginTick();
        TickTime third = clock.BeginTick();

        Assert.Equal(0.0, first.Fps);
        Assert.Equal(0.0, second.Fps);
        Assert.Equal(0.0, third.Fps);
        Assert.Equal(3L, third.FrameCount);
    }
}
