using RayBork.Internal;

namespace RayBork.Tests;

public sealed class PresentEncodingTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ValuesMatchThePresentShaderABI() {
        Assert.Equal(0, (int)PresentEncoding.LinearClamp);
        Assert.Equal(1, (int)PresentEncoding.ManualSrgb);
        Assert.Equal(2, (int)PresentEncoding.ScRgbHdr);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TheEncodingSetIsExactlyTheThreeShaderBranches() {
        Assert.Equal(3, Enum.GetValues<PresentEncoding>().Length);
    }
}
