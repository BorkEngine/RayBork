using Microsoft.Extensions.Logging.Abstractions;
using RayBork.Internal;
using SDL3;

namespace RayBork.Tests;

[Collection("SDL")]
public sealed class SdlPlatformTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void InitializeCreatesWindowMonitorsAndInput() {
        using SdlTestHost host = SdlTestHost.Create();
        IWindow window = host.Platform.Window;

        Assert.Equal(320, window.Width);
        Assert.Equal(200, window.Height);
        Assert.True(window.RenderWidth > 0);
        Assert.True(window.RenderHeight > 0);
        Assert.True(window.DpiScale > 0f);
        Assert.True(window.IsResizable);
        Assert.False(window.IsFullscreen);
        Assert.False(window.IsMinimized);
        Assert.False(window.CloseRequested);
        Assert.Equal(WindowState.Normal, window.State);
        Assert.NotEmpty(window.Monitors);
        Assert.Contains(window.Monitors, monitor => monitor.Index == window.CurrentMonitor.Index);
        Assert.True(window.CurrentMonitor.RefreshRate >= 0);

        IInput input = host.Platform.CaptureInput();
        Assert.Empty(input.Gamepads);
        Assert.Equal(string.Empty, input.TextInput);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void QueuedTitleAndSizeApplyOnPump() {
        using SdlTestHost host = SdlTestHost.Create();
        IWindow window = host.Platform.Window;

        window.SetTitle("RayBork M1 Window");
        window.SetSize(400, 300);
        host.Platform.Pump(TimeSpan.Zero);

        Assert.Equal("RayBork M1 Window", window.Title);
        Assert.Equal(400, window.Width);
        Assert.Equal(300, window.Height);
        Assert.True(window.RenderWidth > 0);
        Assert.True(window.RenderHeight > 0);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ResizeIsReportedForExactlyOneTick() {
        using SdlTestHost host = SdlTestHost.Create();
        SdlWindow window = host.Window;
        window.SetSize(360, 240);
        host.Platform.Pump(TimeSpan.Zero);

        window.BeginTick();
        Assert.True(window.WasResized);

        window.BeginTick();
        Assert.False(window.WasResized);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ClipboardTextRoundTripsWhenTheDriverSupportsIt() {
        using SdlTestHost host = SdlTestHost.Create();
        IWindow window = host.Platform.Window;
        const string clipboardText = "RayBork M1 clipboard";

        window.SetClipboardText(clipboardText);
        host.Platform.Pump(TimeSpan.Zero);
        Assert.Equal(clipboardText, window.GetClipboardText());

        host.Platform.Pump(TimeSpan.Zero);
        string roundTripped = window.GetClipboardText();
        if (roundTripped.Length == 0) {
            Assert.Skip("The dummy video driver does not support clipboard text.");
        }

        Assert.Equal(clipboardText, roundTripped);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CloseQueuesACloseRequestAndInvokesTheExitCallback() {
        int exitRequests = 0;
        using SdlTestHost host = SdlTestHost.Create(() => exitRequests++);
        IWindow window = host.Platform.Window;

        window.Close();
        Assert.False(window.CloseRequested);

        host.Platform.Pump(TimeSpan.Zero);

        Assert.True(window.CloseRequested);
        Assert.Equal(1, exitRequests);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WindowCloseEventInvokesTheExitCallback() {
        int exitRequests = 0;
        using SdlTestHost host = SdlTestHost.Create(() => exitRequests++);
        SdlWindow window = host.Window;

        SDL.Event closeEvent = new() {
            Type = (uint)SDL.EventType.WindowCloseRequested,
            Window = new SDL.WindowEvent {
                Type = SDL.EventType.WindowCloseRequested,
                WindowID = window.WindowId,
            },
        };
        Assert.True(SDL.PushEvent(ref closeEvent));

        host.Platform.Pump(TimeSpan.Zero);

        Assert.True(window.CloseRequested);
        Assert.Equal(1, exitRequests);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void QuitEventInvokesTheExitCallback() {
        int exitRequests = 0;
        using SdlTestHost host = SdlTestHost.Create(() => exitRequests++);

        SDL.Event quitEvent = new() {
            Type = (uint)SDL.EventType.Quit,
            Quit = new SDL.QuitEvent {
                Type = SDL.EventType.Quit,
            },
        };
        Assert.True(SDL.PushEvent(ref quitEvent));

        host.Platform.Pump(TimeSpan.Zero);

        Assert.True(host.Platform.Window.CloseRequested);
        Assert.Equal(1, exitRequests);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void HiddenStartupStateDoesNotShowTheWindow() {
        using SdlTestHost host = SdlTestHost.Create(windowState: WindowState.Hidden);

        Assert.Equal(WindowState.Hidden, host.Platform.Window.State);
    }

    [Theory]
    [InlineData(WindowState.Fullscreen)]
    [InlineData(WindowState.BorderlessFullscreen)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void StartupWindowStatesAreApplied(WindowState state) {
        using SdlTestHost host = SdlTestHost.Create(windowState: state);
        IWindow window = host.Platform.Window;
        if (window.State != state) {
            Assert.Skip($"The dummy video driver does not support the {state} startup state.");
        }

        Assert.Equal(state, window.State);
        Assert.True(window.IsFullscreen);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SettersValidateArgumentsImmediately() {
        using SdlTestHost host = SdlTestHost.Create();
        IWindow window = host.Platform.Window;

        Assert.Throws<ArgumentOutOfRangeException>(() => window.SetSize(0, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => window.SetSize(100, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => window.SetMinSize(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => window.SetMaxSize(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => window.SetMonitor(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => window.SetMonitor(window.Monitors.Count));
        Assert.Throws<ArgumentNullException>(() => window.SetTitle(null!));
        Assert.Throws<ArgumentNullException>(() => window.SetClipboardText(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => window.SetCursor((CursorMode)3));

        host.Platform.Pump(TimeSpan.Zero);
        Assert.Equal(320, window.Width);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SetSizeWhileFullscreenIsRememberedForTheWindowedRestore() {
        using SdlTestHost host = SdlTestHost.Create();
        IWindow window = host.Platform.Window;

        window.SetFullscreen(true);
        host.Platform.Pump(TimeSpan.Zero);
        if (!window.IsFullscreen) {
            Assert.Skip("The dummy video driver does not support fullscreen.");
        }

        int fullscreenWidth = window.Width;
        window.SetSize(500, 400);
        host.Platform.Pump(TimeSpan.Zero);
        Assert.Equal(fullscreenWidth, window.Width);

        window.SetFullscreen(false);
        host.Platform.Pump(TimeSpan.Zero);
        Assert.False(window.IsFullscreen);
        Assert.Equal(500, window.Width);
        Assert.Equal(400, window.Height);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DisposeBeforeInitializeIsSafe() {
        SdlPlatform platform = new(NullLoggerFactory.Instance);

        Assert.Throws<InvalidOperationException>(() => platform.Pump(TimeSpan.Zero));
        platform.Dispose();
        platform.Dispose();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DisposeIsIdempotentAndRejectsLaterPump() {
        SdlTestHost host = SdlTestHost.Create();
        SdlPlatform platform = host.Platform;

        platform.Dispose();
        platform.Dispose();

        Assert.Throws<ObjectDisposedException>(() => platform.Pump(TimeSpan.Zero));
        Assert.Throws<ObjectDisposedException>(() => platform.CaptureInput());
        host.Dispose();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void VulkanWindowExposesRequiredInstanceExtensions() {
        using SdlTestHost host = SdlTestHost.CreateVulkan();

        IReadOnlyList<string> extensions = host.Platform.GetVulkanInstanceExtensions();

        Assert.Contains("VK_KHR_surface", extensions);
        if (OperatingSystem.IsWindows()) {
            Assert.Contains("VK_KHR_win32_surface", extensions);
        } else {
            Assert.Contains(
                extensions,
                extension => extension is "VK_KHR_xlib_surface" or "VK_KHR_xcb_surface" or "VK_KHR_wayland_surface" or "VK_KHR_android_surface");
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void VulkanInstanceExtensionsAreDeduplicatedAndOrdinalSorted() {
        using SdlTestHost host = SdlTestHost.CreateVulkan();

        string[] extensions = [.. host.Platform.GetVulkanInstanceExtensions()];

        Assert.NotEmpty(extensions);
        Assert.Equal(extensions.Order(StringComparer.Ordinal).ToArray(), extensions);
        Assert.Equal(extensions.Length, extensions.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GetVulkanInstanceExtensionsOnNonVulkanWindowThrowsPlatformException() {
        using SdlTestHost host = SdlTestHost.Create();

        PlatformException exception = Assert.Throws<PlatformException>(() => host.Platform.GetVulkanInstanceExtensions());

        Assert.Equal(DiagnosticCodes.VULKAN_SURFACE_CREATION_FAILED, exception.DiagnosticCode);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GetVulkanInstanceExtensionsBeforeInitializeThrowsPlatformException() {
        SdlPlatform platform = new(NullLoggerFactory.Instance);

        Assert.Throws<PlatformException>(() => platform.GetVulkanInstanceExtensions());
        platform.Dispose();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CreateVulkanSurfaceOnNonVulkanWindowThrowsInvalidOperationException() {
        using SdlTestHost host = SdlTestHost.Create();

        Assert.Throws<InvalidOperationException>(() => host.Platform.CreateVulkanSurface(IntPtr.Zero));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CreateVulkanSurfaceBeforeInitializeThrowsInvalidOperationException() {
        SdlPlatform platform = new(NullLoggerFactory.Instance);

        Assert.Throws<InvalidOperationException>(() => platform.CreateVulkanSurface(IntPtr.Zero));
        platform.Dispose();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CreateVulkanSurfaceAfterDisposeThrowsObjectDisposedException() {
        SdlTestHost host = SdlTestHost.Create();
        SdlPlatform platform = host.Platform;

        platform.Dispose();

        Assert.Throws<ObjectDisposedException>(() => platform.CreateVulkanSurface(IntPtr.Zero));
        host.Dispose();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DestroyVulkanSurfaceIgnoresZeroAndUnknownHandles() {
        using SdlTestHost host = SdlTestHost.Create();
        IntPtr unknownSurface = new(0x5EED);

        host.Platform.DestroyVulkanSurface(IntPtr.Zero, IntPtr.Zero);
        host.Platform.DestroyVulkanSurface(IntPtr.Zero, unknownSurface);
        host.Platform.DestroyVulkanSurface(IntPtr.Zero, unknownSurface);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DestroyVulkanSurfaceIgnoresUnknownHandlesOnVulkanWindow() {
        using SdlTestHost host = SdlTestHost.CreateVulkan();
        IntPtr unknownSurface = new(0x5EED);

        host.Platform.DestroyVulkanSurface(IntPtr.Zero, unknownSurface);
        host.Platform.DestroyVulkanSurface(IntPtr.Zero, unknownSurface);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void VulkanWindowCreationSucceedsAndTheWindowIsUsable() {
        using SdlTestHost host = SdlTestHost.CreateVulkan();
        IWindow window = host.Platform.Window;

        Assert.Equal(320, window.Width);
        Assert.Equal(200, window.Height);
        Assert.Equal(WindowState.Hidden, window.State);

        window.SetSize(360, 240);
        host.Platform.Pump(TimeSpan.Zero);
        host.Window.BeginTick();

        Assert.Equal(360, window.Width);
        Assert.Equal(240, window.Height);
        Assert.True(window.WasResized);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void NonVulkanWindowCreationSucceedsAndTheWindowIsUsable() {
        using SdlTestHost host = SdlTestHost.Create();
        IWindow window = host.Platform.Window;

        Assert.Equal(320, window.Width);
        Assert.Equal(200, window.Height);
        Assert.Equal(WindowState.Normal, window.State);

        window.SetSize(360, 240);
        host.Platform.Pump(TimeSpan.Zero);

        Assert.Equal(360, window.Width);
        Assert.Equal(240, window.Height);
    }
}
