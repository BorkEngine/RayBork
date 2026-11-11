using Microsoft.Extensions.Logging.Abstractions;
using RayBork.Internal;
using SDL3;

namespace RayBork.Tests;

/// <summary>
/// Owns one SDL platform initialized with the dummy video driver for one test. The hint is process
/// global, so every test that uses this host joins the serialized <c>SDL</c> collection.
/// </summary>
internal sealed class SdlTestHost : IDisposable {
    private const string VIDEO_DRIVER_HINT = "SDL_VIDEO_DRIVER";
    private const string DUMMY_VIDEO_DRIVER = "dummy";
    private const int WINDOW_WIDTH = 320;
    private const int WINDOW_HEIGHT = 200;
    private const string WINDOW_TITLE = "RayBork M1 Test";

    private bool _disposed;

    private SdlTestHost(SdlPlatform platform) {
        Platform = platform;
    }

    internal SdlPlatform Platform { get; }

    internal SdlWindow Window => (SdlWindow)Platform.Window;

    internal static SdlTestHost Create(Action? onExitRequested = null, WindowState windowState = WindowState.Normal) {
        SDL.SetHint(VIDEO_DRIVER_HINT, DUMMY_VIDEO_DRIVER);
        SdlPlatform platform = new(NullLoggerFactory.Instance);
        bool initialized = false;
        try {
            platform.Initialize(
                new PlatformOptions {
                    Title = WINDOW_TITLE,
                    Width = WINDOW_WIDTH,
                    Height = WINDOW_HEIGHT,
                    Resizable = true,
                    WindowState = windowState,
                },
                onExitRequested ?? (() => { }));
            initialized = true;
        } catch (Exception ex) when (ex is PlatformException or WindowException) {
            platform.Dispose();
            SDL.ResetHint(VIDEO_DRIVER_HINT);
            Assert.Skip($"SDL dummy video driver initialization failed: {ex.Message}");
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
