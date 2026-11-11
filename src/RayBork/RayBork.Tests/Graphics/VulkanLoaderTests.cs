using Microsoft.Extensions.Logging.Abstractions;
using RayBork.Internal;

namespace RayBork.Tests;

public sealed class VulkanLoaderTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void VulkanLoaderRejectsMissingExports() {
        VulkanLoader loader = null!;
        try {
            loader = VulkanLoader.Load(NullLogger.Instance);
        } catch (GraphicsException) {
            Assert.Skip("Vulkan loader is not installed");
        }

        using (loader) {
            Assert.False(loader.TryGetProcAddress("vkDefinitelyNotARealCommand", out _, null));
            Assert.True(loader.TryGetProcAddress("vkGetInstanceProcAddr", out nint address, null));
            Assert.NotEqual(IntPtr.Zero, address);
        }
    }
}
