using Microsoft.Extensions.Logging.Abstractions;
using RayBork.Internal;
using SDL3;

namespace RayBork.Tests;

/// <summary>
/// Owns one SDL platform for one test. <see cref="Create"/> initializes the dummy video driver;
/// <see cref="CreateVulkan"/> uses the real video driver because the dummy driver cannot create a
/// Vulkan window. The video driver hint is process global, so every test that uses this host joins
/// the serialized <c>SDL</c> collection.
/// </summary>
internal sealed class SdlTestHost : IDisposable {
    private const string VIDEO_DRIVER_HINT = "SDL_VIDEO_DRIVER";
    private const string DUMMY_VIDEO_DRIVER = "dummy";
    private const int WINDOW_WIDTH = 320;
    private const int WINDOW_HEIGHT = 200;
    private const string WINDOW_TITLE = "RayBork M1 Test";
    private const string VULKAN_WINDOW_TITLE = "RayBork M2 Vulkan Test";

    private bool _disposed;

    private SdlTestHost(SdlPlatform platform) {
        Platform = platform;
    }

    internal SdlPlatform Platform { get; }

    internal SdlWindow Window => (SdlWindow)Platform.Window;

    internal static SdlTestHost Create(Action? onExitRequested = null, WindowState windowState = WindowState.Normal) {
        SDL.SetHint(VIDEO_DRIVER_HINT, DUMMY_VIDEO_DRIVER);
        return CreateHost(onExitRequested, windowState, enableVulkan: false);
    }

    /// <summary>
    /// Creates a platform on the real video driver with a Vulkan-capable window. The window starts
    /// hidden, and the test is skipped when the host has no video device or Vulkan loader.
    /// </summary>
    internal static SdlTestHost CreateVulkan(Action? onExitRequested = null) {
        SDL.ResetHint(VIDEO_DRIVER_HINT);
        return CreateHost(onExitRequested, WindowState.Hidden, enableVulkan: true);
    }

    private static SdlTestHost CreateHost(Action? onExitRequested, WindowState windowState, bool enableVulkan) {
        SdlPlatform platform = new(NullLoggerFactory.Instance);
        bool initialized = false;
        try {
            platform.Initialize(
                new PlatformOptions {
                    Title = enableVulkan ? VULKAN_WINDOW_TITLE : WINDOW_TITLE,
                    Width = WINDOW_WIDTH,
                    Height = WINDOW_HEIGHT,
                    Resizable = true,
                    WindowState = windowState,
                    EnableVulkan = enableVulkan,
                },
                onExitRequested ?? (() => { }));
            initialized = true;
        } catch (Exception ex) when (ex is PlatformException or WindowException) {
            platform.Dispose();
            SDL.ResetHint(VIDEO_DRIVER_HINT);
            string driver = enableVulkan ? "Vulkan video driver initialization" : "SDL dummy video driver initialization";
            Assert.Skip($"{driver} failed: {ex.Message}");
        }

        if (!initialized) {
            platform.Dispose();
            SDL.ResetHint(VIDEO_DRIVER_HINT);
            throw new InvalidOperationException("SDL initialization failed.");
        }

        return new SdlTestHost(platform);
    }

    public void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;
        Platform.Dispose();
        SDL.ResetHint(VIDEO_DRIVER_HINT);
    }
}
