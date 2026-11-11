using RayBork.Internal;

namespace RayBork.Tests;

public sealed class WindowHdrStateTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SdrIsDisabledWithUnitScales() {
        WindowHdrState state = WindowHdrState.Sdr;

        Assert.False(state.HdrEnabled);
        Assert.Equal(1f, state.SdrWhiteLevel);
        Assert.Equal(1f, state.HdrHeadroom);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void NormalizeForcesSdrWhenHdrIsDisabled() {
        WindowHdrState state = WindowHdrState.Normalize(false, 2.5f, 4f);

        Assert.Equal(WindowHdrState.Sdr, state);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void NormalizePreservesValidValues() {
        WindowHdrState state = WindowHdrState.Normalize(true, 2.5f, 4f);

        Assert.True(state.HdrEnabled);
        Assert.Equal(2.5f, state.SdrWhiteLevel);
        Assert.Equal(4f, state.HdrHeadroom);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(0f)]
    [InlineData(-1f)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void NormalizeFallsBackToUnitWhiteLevelForInvalidValues(float sdrWhiteLevel) {
        WindowHdrState state = WindowHdrState.Normalize(true, sdrWhiteLevel, 4f);

        Assert.True(state.HdrEnabled);
        Assert.Equal(1f, state.SdrWhiteLevel);
        Assert.Equal(4f, state.HdrHeadroom);
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void NormalizeFallsBackToUnitHeadroomForInvalidValues(float hdrHeadroom) {
        WindowHdrState state = WindowHdrState.Normalize(true, 2.5f, hdrHeadroom);

        Assert.True(state.HdrEnabled);
        Assert.Equal(2.5f, state.SdrWhiteLevel);
        Assert.Equal(1f, state.HdrHeadroom);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void NormalizeKeepsHeadroomOfExactlyOne() {
        WindowHdrState state = WindowHdrState.Normalize(true, 1f, 1f);

        Assert.True(state.HdrEnabled);
        Assert.Equal(1f, state.SdrWhiteLevel);
        Assert.Equal(1f, state.HdrHeadroom);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RecordEqualityComparesEveryValue() {
        WindowHdrState first = new(true, 2f, 3f);
        WindowHdrState same = new(true, 2f, 3f);
        WindowHdrState differentEnabled = new(false, 2f, 3f);
        WindowHdrState differentWhiteLevel = new(true, 2.5f, 3f);
        WindowHdrState differentHeadroom = new(true, 2f, 4f);

        Assert.Equal(first, same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(first, differentEnabled);
        Assert.NotEqual(first, differentWhiteLevel);
        Assert.NotEqual(first, differentHeadroom);
        Assert.True(first == same);
        Assert.False(first != same);
    }
}
