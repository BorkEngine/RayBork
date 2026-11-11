using RayBork.Internal;

namespace RayBork.Tests;

public sealed class DirectSurfaceProviderTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CreateSurfaceForwardsTheInstanceAndReturnsThePlatformHandle() {
        FakeEnginePlatform platform = CreateInitializedPlatform();
        DirectSurfaceProvider provider = new(platform);
        IntPtr surface = new(0x1234);
        platform.VulkanSurface = surface;

        IntPtr created = provider.CreateSurface(new IntPtr(0xABC));

        Assert.Equal(surface, created);
        Assert.Equal(1, platform.CreateSurfaceCallCount);
        Assert.Equal(new IntPtr(0xABC), Assert.Single(platform.CreatedSurfaceInstances));
        Assert.Equal(Environment.CurrentManagedThreadId, platform.CreateSurfaceThreadId);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CreateSurfaceWrapsAPlatformFailureAsGraphicsException() {
        FakeEnginePlatform platform = CreateInitializedPlatform();
        DirectSurfaceProvider provider = new(platform);
        PlatformException failure = new("No Vulkan surface.");
        platform.SurfaceCreationException = failure;

        GraphicsException exception = Assert.Throws<GraphicsException>(() => provider.CreateSurface(IntPtr.Zero));

        Assert.Equal(DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED, exception.DiagnosticCode);
        Assert.Same(failure, exception.InnerException);
        Assert.Contains(failure.Message, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DestroySurfaceIgnoresZeroAndForwardsKnownHandles() {
        FakeEnginePlatform platform = CreateInitializedPlatform();
        DirectSurfaceProvider provider = new(platform);

        provider.DestroySurface(new IntPtr(0xABC), IntPtr.Zero);
        Assert.Empty(platform.DestroyedSurfaces);

        provider.DestroySurface(new IntPtr(0xABC), new IntPtr(0x1234));
        Assert.Equal(new IntPtr(0x1234), Assert.Single(platform.DestroyedSurfaces));
        Assert.Equal(Environment.CurrentManagedThreadId, platform.DestroySurfaceThreadId);
    }

    private static FakeEnginePlatform CreateInitializedPlatform() {
        FakeEnginePlatform platform = new();
        platform.Initialize(
            new PlatformOptions {
                Title = "RayBork Direct Surface",
                Width = 320,
                Height = 200,
                Resizable = true,
                WindowState = WindowState.Normal,
            },
            () => { });
        return platform;
    }
}
