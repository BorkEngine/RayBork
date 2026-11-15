using Microsoft.Extensions.Logging.Abstractions;
using RayBork.Internal;

namespace RayBork.Tests;

/// <summary>
/// Owns one SDL platform for one test. <see cref="Create"/> initializes the dummy video driver;
/// <see cref="CreateVulkan"/> uses the real video driver because the dummy driver cannot create a
/// Vulkan window. The video driver hint is process global; <see cref="SdlPlatform"/> applies the
/// hint and initializes under one process gate, and serializes disposal across parallel test
/// classes. Dummy-driver hosts still belong to the non-parallel <c>SDL</c> collection so they never
/// overlap a real-driver GPU window.
/// </summary>
internal sealed class SdlTestHost : IDisposable {
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

    internal static SdlTestHost Create(
        Action? onExitRequested = null,
        WindowState windowState = WindowState.Normal,
        bool initializeGamepads = false) {
        return CreateHost(
            onExitRequested,
            windowState,
            enableVulkan: false,
            videoDriverHint: DUMMY_VIDEO_DRIVER,
            initializeGamepads,
            minimalTestMode: false);
    }

    /// <summary>
    /// Creates a platform on the real video driver with a Vulkan-capable window. The window starts
    /// hidden, and the test is skipped when the host has no video device or Vulkan loader.
    /// </summary>
    /// <param name="onExitRequested">Invoked when the platform requests a graceful exit.</param>
    /// <param name="minimalTestMode">
    /// Whether to skip monitor, text-input, and HDR refresh work. GPU hosts pass <see langword="true"/>;
    /// unit tests that inspect window HDR state must keep the full behavior.
    /// </param>
    internal static SdlTestHost CreateVulkan(Action? onExitRequested = null, bool minimalTestMode = false) {
        if (!TryCreateVulkan(out SdlTestHost? host, out string skipReason, onExitRequested, minimalTestMode)) {
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
    /// <param name="minimalTestMode">Whether to skip monitor, text-input, and HDR refresh work.</param>
    /// <returns><see langword="true"/> when the host was created.</returns>
    internal static bool TryCreateVulkan(
        out SdlTestHost? host,
        out string skipReason,
        Action? onExitRequested = null,
        bool minimalTestMode = false) {
        return TryCreateHost(
            onExitRequested,
            WindowState.Hidden,
            enableVulkan: true,
            videoDriverHint: null,
            initializeGamepads: false,
            minimalTestMode,
            out host,
            out skipReason);
    }

    private static SdlTestHost CreateHost(
        Action? onExitRequested,
        WindowState windowState,
        bool enableVulkan,
        string? videoDriverHint,
        bool initializeGamepads,
        bool minimalTestMode) {
        if (!TryCreateHost(
            onExitRequested,
            windowState,
            enableVulkan,
            videoDriverHint,
            initializeGamepads,
            minimalTestMode,
            out SdlTestHost? host,
            out string skipReason)) {
            Assert.Skip(skipReason);
        }

        return host!;
    }

    private static bool TryCreateHost(
        Action? onExitRequested,
        WindowState windowState,
        bool enableVulkan,
        string? videoDriverHint,
        bool initializeGamepads,
        bool minimalTestMode,
        out SdlTestHost? host,
        out string skipReason) {
        host = null;
        skipReason = string.Empty;
        SdlPlatform platform = new(NullLoggerFactory.Instance, initializeGamepads, minimalTestMode);
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
                onExitRequested ?? (() => { }),
                videoDriverHint);
        } catch (Exception ex) when (ex is PlatformException or WindowException) {
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
    }
}
