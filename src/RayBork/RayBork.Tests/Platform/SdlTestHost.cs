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
        if (!TryCreateVulkan(out SdlTestHost? host, out string skipReason, onExitRequested)) {
            Assert.Skip(skipReason);
        }

        return host!;
    }

    /// <summary>
    /// Tries to create a platform on the real video driver with a Vulkan-capable window.
    /// </summary>
    /// <param name="host">The created host, or <see langword="null"/> when the host is unavailable.</param>
    /// <param name="skipReason">A specific reason when <paramref name="host"/> is <see langword="null"/>.</param>
    /// <param name="onExitRequested">Invoked when the platform requests a graceful exit.</param>
    /// <returns><see langword="true"/> when the host was created.</returns>
    internal static bool TryCreateVulkan(
        out SdlTestHost? host,
        out string skipReason,
        Action? onExitRequested = null) {
        SDL.ResetHint(VIDEO_DRIVER_HINT);
        return TryCreateHost(onExitRequested, WindowState.Hidden, enableVulkan: true, out host, out skipReason);
    }

    private static SdlTestHost CreateHost(Action? onExitRequested, WindowState windowState, bool enableVulkan) {
        if (!TryCreateHost(onExitRequested, windowState, enableVulkan, out SdlTestHost? host, out string skipReason)) {
            Assert.Skip(skipReason);
        }

        return host!;
    }

    private static bool TryCreateHost(
        Action? onExitRequested,
        WindowState windowState,
        bool enableVulkan,
        out SdlTestHost? host,
        out string skipReason) {
        host = null;
        skipReason = string.Empty;
        SdlPlatform platform = new(NullLoggerFactory.Instance);
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
        } catch (Exception ex) when (ex is PlatformException or WindowException) {
            platform.Dispose();
            SDL.ResetHint(VIDEO_DRIVER_HINT);
            string driver = enableVulkan ? "Vulkan video driver initialization" : "SDL dummy video driver initialization";
            skipReason = $"{driver} failed: {ex.Message}";
            return false;
        }

        host = new SdlTestHost(platform);
        return true;
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
