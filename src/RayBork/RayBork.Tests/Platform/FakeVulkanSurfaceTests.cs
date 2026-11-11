using RayBork.Internal;

namespace RayBork.Tests;

public sealed class FakeVulkanSurfaceTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DefaultExtensionsMatchTheHostPlatform() {
        FakeEnginePlatform platform = CreateInitializedPlatform();

        IReadOnlyList<string> extensions = platform.GetVulkanInstanceExtensions();

        Assert.Contains("VK_KHR_surface", extensions);
        if (OperatingSystem.IsWindows()) {
            Assert.Contains("VK_KHR_win32_surface", extensions);
        } else {
            Assert.Contains("VK_KHR_xlib_surface", extensions);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ConfiguredExtensionsAreReturned() {
        FakeEnginePlatform platform = CreateInitializedPlatform();
        string[] configured = ["VK_KHR_surface", "VK_KHR_wayland_surface"];
        platform.VulkanInstanceExtensions = configured;

        Assert.Same(configured, platform.GetVulkanInstanceExtensions());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CreateVulkanSurfaceReturnsTheConfiguredHandleAndRecordsTheCall() {
        FakeEnginePlatform platform = CreateInitializedPlatform();
        IntPtr instance = new(0xABC);
        IntPtr configuredSurface = new(0x1234);
        platform.VulkanSurface = configuredSurface;

        IntPtr surface = platform.CreateVulkanSurface(instance);

        Assert.Equal(configuredSurface, surface);
        Assert.Equal(1, platform.CreateSurfaceCallCount);
        Assert.Equal(instance, Assert.Single(platform.CreatedSurfaceInstances));
        Assert.Equal(Environment.CurrentManagedThreadId, platform.CreateSurfaceThreadId);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CreateVulkanSurfaceThrowsTheConfiguredException() {
        FakeEnginePlatform platform = CreateInitializedPlatform();
        InvalidOperationException failure = new("Surface creation failed.");
        platform.SurfaceCreationException = failure;

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => platform.CreateVulkanSurface(IntPtr.Zero));

        Assert.Same(failure, thrown);
        Assert.Equal(1, platform.CreateSurfaceCallCount);
        Assert.Empty(platform.CreatedSurfaceInstances);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DestroyVulkanSurfaceRecordsTheHandle() {
        FakeEnginePlatform platform = CreateInitializedPlatform();
        IntPtr surface = new(0x4321);

        platform.DestroyVulkanSurface(new IntPtr(0xABC), surface);
        platform.DestroyVulkanSurface(new IntPtr(0xABC), IntPtr.Zero);

        Assert.Equal(surface, Assert.Single(platform.DestroyedSurfaces));
        Assert.Equal(Environment.CurrentManagedThreadId, platform.DestroySurfaceThreadId);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SurfaceRequestsBeforeInitializeThrow() {
        FakeEnginePlatform platform = new();

        Assert.Throws<InvalidOperationException>(() => platform.CreateVulkanSurface(IntPtr.Zero));
        Assert.Throws<PlatformException>(() => platform.GetVulkanInstanceExtensions());
        Assert.Equal(0, platform.CreateSurfaceCallCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SurfaceRequestsAfterDisposeThrow() {
        FakeEnginePlatform platform = CreateInitializedPlatform();
        platform.Dispose();

        Assert.Throws<ObjectDisposedException>(() => platform.CreateVulkanSurface(IntPtr.Zero));
        Assert.Throws<PlatformException>(() => platform.GetVulkanInstanceExtensions());
        Assert.Equal(0, platform.CreateSurfaceCallCount);
    }

    private static FakeEnginePlatform CreateInitializedPlatform() {
        FakeEnginePlatform platform = new();
        platform.Initialize(
            new PlatformOptions {
                Title = "RayBork Fake",
                Width = 1280,
                Height = 720,
                Resizable = true,
                WindowState = WindowState.Normal,
            },
            () => { });
        return platform;
    }
}
