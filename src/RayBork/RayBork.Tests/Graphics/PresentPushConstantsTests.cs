using System.Runtime.InteropServices;
using RayBork.Internal;

namespace RayBork.Tests;

public sealed class PresentPushConstantsTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void LayoutMatchesThePresentShaderABI() {
        Assert.Equal(32, Marshal.SizeOf<PresentPushConstants>());
        Assert.Equal(0, (int)Marshal.OffsetOf<PresentPushConstants>(nameof(PresentPushConstants.ExposureMultiplier)));
        Assert.Equal(4, (int)Marshal.OffsetOf<PresentPushConstants>(nameof(PresentPushConstants.HdrWhiteScale)));
        Assert.Equal(8, (int)Marshal.OffsetOf<PresentPushConstants>(nameof(PresentPushConstants.HdrPeakScale)));
        Assert.Equal(12, (int)Marshal.OffsetOf<PresentPushConstants>(nameof(PresentPushConstants.AgxWhiteSceneLuminance)));
        Assert.Equal(16, (int)Marshal.OffsetOf<PresentPushConstants>(nameof(PresentPushConstants.Tonemapping)));
        Assert.Equal(20, (int)Marshal.OffsetOf<PresentPushConstants>(nameof(PresentPushConstants.Encoding)));
        Assert.Equal(24, (int)Marshal.OffsetOf<PresentPushConstants>(nameof(PresentPushConstants.HdrHighlight)));
        Assert.Equal(28, (int)Marshal.OffsetOf<PresentPushConstants>(nameof(PresentPushConstants.Reserved)));
    }
}
