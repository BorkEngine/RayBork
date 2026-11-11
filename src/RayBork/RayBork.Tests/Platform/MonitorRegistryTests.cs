using RayBork.Internal;

namespace RayBork.Tests;

public sealed class MonitorRegistryTests {
    private static readonly MonitorInfo _first = new(10, "First", new Rect(0f, 0f, 1920f, 1080f), new Rect(0f, 0f, 1920f, 1040f), 1f, 60);
    private static readonly MonitorInfo _second = new(20, "Second", new Rect(1920f, 0f, 2560f, 1440f), new Rect(1920f, 0f, 2560f, 1400f), 1.5f, 144);
    private static readonly MonitorInfo _third = new(30, "Third", new Rect(-1280f, 0f, 1280f, 720f), new Rect(-1280f, 0f, 1280f, 720f), 2f, 75);

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void FirstUpdateAssignsSequentialStableIndices() {
        MonitorRegistry registry = new();

        registry.Update([_first, _second], _first.DisplayId);

        Assert.Equal(2, registry.Count);
        Assert.Equal(0, registry.Monitors[0].Index);
        Assert.Equal(1, registry.Monitors[1].Index);
        Assert.Equal("First", registry.Monitors[0].Name);
        Assert.Equal("Second", registry.Monitors[1].Name);
        Assert.Equal(_first.DisplayId, registry.PrimaryDisplayId);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ReorderingDisplaysKeepsTheirIndicesAndSortsByIndex() {
        MonitorRegistry registry = new();
        registry.Update([_first, _second], _first.DisplayId);

        registry.Update([_second, _first], _second.DisplayId);

        Assert.Equal(0, registry.Monitors[0].Index);
        Assert.Equal("First", registry.Monitors[0].Name);
        Assert.Equal(1, registry.Monitors[1].Index);
        Assert.Equal("Second", registry.Monitors[1].Name);
        Assert.Equal(_second.DisplayId, registry.PrimaryDisplayId);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RemovingAndReaddingADisplayKeepsItsOriginalIndex() {
        MonitorRegistry registry = new();
        registry.Update([_first, _second, _third], _first.DisplayId);

        registry.Update([_third, _first], _first.DisplayId);

        Assert.Equal(2, registry.Count);
        Assert.Equal(0, registry.Monitors[0].Index);
        Assert.Equal(2, registry.Monitors[1].Index);
        Assert.False(registry.TryGetDisplayId(1, out _));

        registry.Update([_second], _second.DisplayId);

        Assert.Equal(1, Assert.Single(registry.Monitors).Index);
        Assert.True(registry.TryGetDisplayId(1, out uint displayId));
        Assert.Equal(_second.DisplayId, displayId);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MonitorDataIsSnapshotted() {
        MonitorRegistry registry = new();

        registry.Update([_second], _second.DisplayId);

        Monitor monitor = Assert.Single(registry.Monitors);
        Assert.Equal(_second.Name, monitor.Name);
        Assert.Equal(_second.Bounds, monitor.Bounds);
        Assert.Equal(_second.WorkArea, monitor.WorkArea);
        Assert.Equal(_second.DpiScale, monitor.DpiScale);
        Assert.Equal(_second.RefreshRate, monitor.RefreshRate);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CurrentMonitorFallsBackToPrimaryThenFirst() {
        MonitorRegistry registry = new();
        registry.Update([_first, _second], _second.DisplayId);

        Assert.Equal(1, registry.GetCurrent(_second.DisplayId).Index);
        Assert.Equal(1, registry.GetCurrent(9999).Index);
        Assert.Equal(_first.DisplayId, GetDisplayIdForIndex(registry, 0));

        registry.Update([_first], _first.DisplayId);
        Assert.Equal(0, registry.GetCurrent(9999).Index);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EmptyRegistryReturnsDefaultMonitor() {
        MonitorRegistry registry = new();

        Monitor current = registry.GetCurrent(1);

        Assert.Equal(0, current.Index);
        Assert.Empty(registry.Monitors);
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DuplicateDisplayIdsAreRejected() {
        MonitorRegistry registry = new();

        Assert.Throws<InvalidOperationException>(() => registry.Update([_first, _first], _first.DisplayId));
    }

    private static uint GetDisplayIdForIndex(MonitorRegistry registry, int index) {
        return registry.TryGetDisplayId(index, out uint displayId)
            ? displayId
            : throw new InvalidOperationException($"No display id for index {index}.");
    }
}
