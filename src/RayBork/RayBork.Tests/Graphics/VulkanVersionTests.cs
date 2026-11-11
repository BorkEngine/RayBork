using RayBork.Internal;

namespace RayBork.Tests;

public sealed class VulkanVersionTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PackUsesTheVulkanBitLayout() {
        Assert.Equal(4202496u, VulkanVersion.Pack(1, 2, 0));
        Assert.Equal(4202499u, VulkanVersion.Pack(1, 2, 3));
        Assert.Equal(0u, VulkanVersion.Pack(0, 0, 0));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UnpackRestoresEveryField() {
        uint packed = VulkanVersion.Pack(1, 4, 336);

        (int major, int minor, int patch) = VulkanVersion.Unpack(packed);

        Assert.Equal(1, major);
        Assert.Equal(4, minor);
        Assert.Equal(336, patch);
    }

    [Theory]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    [InlineData(1, 2, 0, "1.2.0")]
    [InlineData(1, 4, 336, "1.4.336")]
    [InlineData(0, 0, 0, "0.0.0")]
    public void FormatWritesEveryField(int major, int minor, int patch, string expected) {
        Assert.Equal(expected, VulkanVersion.Format(VulkanVersion.Pack(major, minor, patch)));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void FormatRendererNameWritesOnlyMajorAndMinor() {
        Assert.Equal("Vulkan 1.2", VulkanVersion.FormatRendererName(VulkanVersion.Pack(1, 2, 0)));
        Assert.Equal("Vulkan 1.4", VulkanVersion.FormatRendererName(VulkanVersion.Pack(1, 4, 336)));
    }
}
