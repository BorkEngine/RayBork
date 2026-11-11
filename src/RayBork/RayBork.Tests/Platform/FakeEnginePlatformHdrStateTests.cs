using RayBork.Internal;

namespace RayBork.Tests;

public sealed class FakeEnginePlatformHdrStateTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WindowHdrStateDefaultsToSdr() {
        FakeEnginePlatform platform = new();

        Assert.Equal(WindowHdrState.Sdr, platform.WindowHdrState);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WindowHdrStateIsSettable() {
        FakeEnginePlatform platform = new();
        WindowHdrState state = new(true, 2.5f, 4f);

        platform.WindowHdrState = state;

        Assert.Equal(state, platform.WindowHdrState);
        Assert.True(platform.WindowHdrState.HdrEnabled);
    }
}
