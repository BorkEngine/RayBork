using Microsoft.Extensions.Logging.Abstractions;
using RayBork.Internal;
using SDL3;

namespace RayBork.Tests;

[Collection("SDL")]
public sealed class SdlPlatformTests {
    private readonly ITestOutputHelper _output;

    public SdlPlatformTests(ITestOutputHelper output) {
        _output = output;
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void InitializeCreatesWindowMonitorsAndInput() {
        using SdlTestHost host = SdlTestHost.Create(initializeGamepads: true);
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
        // The host may have a physical gamepad connected, which is also what a real game sees. Assert
        // the snapshot is internally consistent instead of assuming no device is present.
        foreach (IGamepad gamepad in input.Gamepads) {
            Assert.True(gamepad.IsConnected);
            Assert.True(gamepad.Index >= 0);
            Assert.NotEqual(0, gamepad.InstanceId);
            Assert.Equal(gamepad.InstanceId, input.GetGamepad(gamepad.Index)?.InstanceId);
        }

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
    public void WindowScopedEventsAreRoutedToTheirOwningWindow() {
        int firstExitRequests = 0;
        int secondExitRequests = 0;
        using SdlTestHost first = SdlTestHost.Create(() => firstExitRequests++);
        using SdlTestHost second = SdlTestHost.Create(() => secondExitRequests++);

        SDL.Event secondResize = new() {
            Type = (uint)SDL.EventType.WindowResized,
            Window = new SDL.WindowEvent {
                Type = SDL.EventType.WindowResized,
                WindowID = second.Window.WindowId,
                Data1 = 400,
                Data2 = 300,
            },
        };
        SDL.Event secondKeyDown = new() {
            Type = (uint)SDL.EventType.KeyDown,
            Key = new SDL.KeyboardEvent {
                Type = SDL.EventType.KeyDown,
                WindowID = second.Window.WindowId,
                Scancode = SDL.Scancode.W,
                Down = true,
            },
        };
        Assert.True(SDL.PushEvent(ref secondResize));
        Assert.True(SDL.PushEvent(ref secondKeyDown));
        first.Platform.Pump(TimeSpan.Zero);

        // Clear any resize flag queued while the dummy windows were created, then assert that the
        // foreign resize event did not reach the first window.
        first.Window.BeginTick();
        first.Window.BeginTick();
        second.Window.BeginTick();
        Assert.False(first.Window.WasResized);
        Assert.True(second.Window.WasResized);
        Assert.False(first.Platform.CaptureInput().IsKeyDown(Key.W));
        Assert.True(second.Platform.CaptureInput().IsKeyDown(Key.W));

        SDL.Event unknownClose = CloseEvent(uint.MaxValue);
        Assert.True(SDL.PushEvent(ref unknownClose));
        first.Platform.Pump(TimeSpan.Zero);
        Assert.False(first.Window.CloseRequested);
        Assert.False(second.Window.CloseRequested);

        SDL.Event secondClose = CloseEvent(second.Window.WindowId);
        Assert.True(SDL.PushEvent(ref secondClose));
        first.Platform.Pump(TimeSpan.Zero);

        Assert.False(first.Window.CloseRequested);
        Assert.Equal(0, firstExitRequests);
        Assert.True(second.Window.CloseRequested);
        Assert.Equal(1, secondExitRequests);

        SDL.Event firstClose = CloseEvent(first.Window.WindowId);
        Assert.True(SDL.PushEvent(ref firstClose));
        first.Platform.Pump(TimeSpan.Zero);

        Assert.True(first.Window.CloseRequested);
        Assert.Equal(1, firstExitRequests);
    }

    // This class is in the non-parallel SDL collection, so pushing directly into the process event
    // queue cannot race a GPU test's pump.
    private static SDL.Event CloseEvent(uint windowId) => new() {
        Type = (uint)SDL.EventType.WindowCloseRequested,
        Window = new SDL.WindowEvent {
            Type = SDL.EventType.WindowCloseRequested,
            WindowID = windowId,
        },
    };

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
    public void WindowHdrStateOnAHiddenVulkanWindowSatisfiesTheInvariants() {
        using SdlTestHost host = SdlTestHost.CreateVulkan();

        WindowHdrState state = host.Platform.WindowHdrState;

        _output.WriteLine(
            "Observed window HDR state: enabled {0}, SDR white level {1}, HDR headroom {2}.",
            state.HdrEnabled,
            state.SdrWhiteLevel,
            state.HdrHeadroom);
        Assert.True(float.IsFinite(state.SdrWhiteLevel));
        Assert.True(state.SdrWhiteLevel > 0f);
        Assert.True(float.IsFinite(state.HdrHeadroom));
        Assert.True(state.HdrHeadroom >= 1f);
        if (!state.HdrEnabled) {
            Assert.Equal(1f, state.SdrWhiteLevel);
            Assert.Equal(1f, state.HdrHeadroom);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WindowHdrStateIsReadableFromAnotherThread() {
        using SdlTestHost host = SdlTestHost.CreateVulkan();

        WindowHdrState expected = host.Platform.WindowHdrState;
        WindowHdrState observed = default;
        Thread thread = new(() => observed = host.Platform.WindowHdrState);
        thread.Start();
        thread.Join();

        Assert.Equal(expected, observed);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WindowHdrStateIsSdrBeforeInitializeAndAfterDispose() {
        SdlPlatform uninitialized = new(NullLoggerFactory.Instance);
        Assert.Equal(WindowHdrState.Sdr, uninitialized.WindowHdrState);
        uninitialized.Dispose();

        SdlTestHost host = SdlTestHost.Create();
        SdlPlatform platform = host.Platform;
        platform.Dispose();
        Assert.Equal(WindowHdrState.Sdr, platform.WindowHdrState);
        host.Dispose();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TwoPlatformsShareTheProcessWideSdlLifetime() {
        using SdlTestHost first = SdlTestHost.Create();
        using SdlTestHost second = SdlTestHost.Create();

        Assert.NotEqual(first.Window.WindowId, second.Window.WindowId);

        first.Window.SetTitle("first");
        second.Window.SetTitle("second");
        first.Platform.Pump(TimeSpan.Zero);
        second.Platform.Pump(TimeSpan.Zero);

        Assert.Equal("first", first.Window.Title);
        Assert.Equal("second", second.Window.Title);

        first.Dispose();
        Assert.False(second.Window.CloseRequested);

        second.Window.SetSize(400, 300);
        second.Platform.Pump(TimeSpan.Zero);
        Assert.Equal(400, second.Window.Width);
        Assert.Equal(300, second.Window.Height);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task PlatformsCanBeCreatedConcurrently() {
        Task<SdlTestHost> firstTask = Task.Run(() => SdlTestHost.Create());
        Task<SdlTestHost> secondTask = Task.Run(() => SdlTestHost.Create());

        SdlTestHost? first = null;
        SdlTestHost? second = null;
        try {
            await Task.WhenAll(firstTask, secondTask);
            first = await firstTask;
            second = await secondTask;

            Assert.NotEqual(first.Window.WindowId, second.Window.WindowId);
            first.Platform.Pump(TimeSpan.Zero);
            second.Platform.Pump(TimeSpan.Zero);
        } finally {
            if (first is null && firstTask.Status == TaskStatus.RanToCompletion) {
                first = await firstTask;
            }

            if (second is null && secondTask.Status == TaskStatus.RanToCompletion) {
                second = await secondTask;
            }

            first?.Dispose();
            second?.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void VulkanPlatformsShareTheLoaderReferenceCount() {
        using SdlTestHost first = SdlTestHost.CreateVulkan();
        using SdlTestHost second = SdlTestHost.CreateVulkan();
        VulkanInstance instance = VulkanInstance.Create(new VulkanInstanceOptions {
            ApplicationName = "RayBork SDL test",
            ApplicationVersion = EngineVersion.Current,
            SurfaceExtensions = first.Platform.GetVulkanInstanceExtensions(),
            RequestValidation = false,
            Logger = NullLogger.Instance,
        });
        try {
            IntPtr firstSurface = first.Platform.CreateVulkanSurface(instance.Handle.Handle);
            IntPtr secondSurface = second.Platform.CreateVulkanSurface(instance.Handle.Handle);
            Assert.Equal(2, SdlPlatform.VulkanLoaderUserCountForTests);

            first.Platform.DestroyVulkanSurface(instance.Handle.Handle, firstSurface);
            first.Dispose();
            Assert.Equal(1, SdlPlatform.VulkanLoaderUserCountForTests);

            second.Platform.DestroyVulkanSurface(instance.Handle.Handle, secondSurface);
            second.Dispose();
            Assert.Equal(0, SdlPlatform.VulkanLoaderUserCountForTests);
        } finally {
            first.Dispose();
            second.Dispose();
            instance.Dispose();
            instance.DisposeFromMainThread();
        }
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
