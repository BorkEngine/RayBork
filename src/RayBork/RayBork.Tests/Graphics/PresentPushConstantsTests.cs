using System.Runtime.InteropServices;
using RayBork.Internal;

namespace RayBork.Tests;

public sealed class PresentPushConstantsTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void LayoutMatchesThePresentShaderABI() {
        Assert.Equal(16, Marshal.SizeOf<PresentPushConstants>());
        Assert.Equal(0, (int)Marshal.OffsetOf<PresentPushConstants>(nameof(PresentPushConstants.ExposureMultiplier)));
        Assert.Equal(4, (int)Marshal.OffsetOf<PresentPushConstants>(nameof(PresentPushConstants.HdrWhiteScale)));
        Assert.Equal(8, (int)Marshal.OffsetOf<PresentPushConstants>(nameof(PresentPushConstants.Tonemapping)));
        Assert.Equal(12, (int)Marshal.OffsetOf<PresentPushConstants>(nameof(PresentPushConstants.Encoding)));
    }
}
