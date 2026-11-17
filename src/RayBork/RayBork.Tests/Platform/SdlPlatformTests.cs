using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RayBork.Internal;
using SDL3;

namespace RayBork.Tests;

[Collection("SDL")]
public sealed class SdlPlatformTests
{
    private const string DUMMY_VIDEO_DRIVER = "dummy";

    private readonly ITestOutputHelper _output;

    public SdlPlatformTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void InitializeCreatesWindowMonitorsAndInput()
    {
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

        IntPtr handle = SDL.GetWindowFromID(host.Window.WindowId);
        Assert.NotEqual(IntPtr.Zero, handle);
        Assert.True(
            (SDL.GetWindowFlags(handle) & SDL.WindowFlags.HighPixelDensity) != 0,
            "The SDL window must be created with SDL_WINDOW_HIGH_PIXEL_DENSITY."
        );

        IInput input = host.Platform.CaptureInput();
        // The host may have a physical gamepad connected, which is also what a real game sees. Assert
        // the snapshot is internally consistent instead of assuming no device is present.
        foreach (IGamepad gamepad in input.Gamepads)
        {
            Assert.True(gamepad.IsConnected);
            Assert.True(gamepad.Index >= 0);
            Assert.NotEqual(0, gamepad.InstanceId);
            Assert.Equal(gamepad.InstanceId, input.GetGamepad(gamepad.Index)?.InstanceId);
        }

        Assert.Equal(string.Empty, input.TextInput);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void QueuedTitleAndSizeApplyOnPump()
    {
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
    public void ResizeIsReportedForExactlyOneTick()
    {
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
    public void ClipboardTextRoundTripsWhenTheDriverSupportsIt()
    {
        using SdlTestHost host = SdlTestHost.Create();
        IWindow window = host.Platform.Window;
        const string clipboardText = "RayBork M1 clipboard";

        window.SetClipboardText(clipboardText);
        host.Platform.Pump(TimeSpan.Zero);
        Assert.Equal(clipboardText, window.GetClipboardText());

        host.Platform.Pump(TimeSpan.Zero);
        string roundTripped = window.GetClipboardText();
        if (roundTripped.Length == 0)
        {
            Assert.Skip("The dummy video driver does not support clipboard text.");
        }

        Assert.Equal(clipboardText, roundTripped);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CloseQueuesACloseRequestAndInvokesTheExitCallback()
    {
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
    public void WindowScopedEventsAreRoutedToTheirOwningWindow()
    {
        int firstExitRequests = 0;
        int secondExitRequests = 0;
        using SdlTestHost first = SdlTestHost.Create(() => firstExitRequests++);
        using SdlTestHost second = SdlTestHost.Create(() => secondExitRequests++);

        SDL.Event secondResize = new()
        {
            Type = (uint)SDL.EventType.WindowResized,
            Window = new SDL.WindowEvent
            {
                Type = SDL.EventType.WindowResized,
                WindowID = second.Window.WindowId,
                Data1 = 400,
                Data2 = 300,
            },
        };
        SDL.Event secondKeyDown = new()
        {
            Type = (uint)SDL.EventType.KeyDown,
            Key = new SDL.KeyboardEvent
            {
                Type = SDL.EventType.KeyDown,
                WindowID = second.Window.WindowId,
                Scancode = SDL.Scancode.W,
                Down = true,
            },
        };
        SDL.Event secondMouseMotion = new()
        {
            Type = (uint)SDL.EventType.MouseMotion,
            Motion = new SDL.MouseMotionEvent
            {
                Type = SDL.EventType.MouseMotion,
                WindowID = second.Window.WindowId,
                X = 10f,
                Y = 20f,
            },
        };
        Assert.True(SDL.PushEvent(ref secondResize));
        Assert.True(SDL.PushEvent(ref secondKeyDown));
        Assert.True(SDL.PushEvent(ref secondMouseMotion));
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
        Assert.Equal(0f, first.Platform.CaptureInput().MousePosition.X);
        // The routed position is scaled to render pixels, so derive the expected value from the
        // window instead of hard-coding it; a high-density test display would use a different ratio.
        float pixelScaleX = second.Window.RenderWidth / (float)second.Window.Width;
        float pixelScaleY = second.Window.RenderHeight / (float)second.Window.Height;
        Assert.Equal(10f * pixelScaleX, second.Platform.CaptureInput().MousePosition.X);
        Assert.Equal(20f * pixelScaleY, second.Platform.CaptureInput().MousePosition.Y);

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
    private static SDL.Event CloseEvent(uint windowId) =>
        new()
        {
            Type = (uint)SDL.EventType.WindowCloseRequested,
            Window = new SDL.WindowEvent
            {
                Type = SDL.EventType.WindowCloseRequested,
                WindowID = windowId,
            },
        };

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void QuitEventInvokesTheExitCallback()
    {
        int exitRequests = 0;
        using SdlTestHost host = SdlTestHost.Create(() => exitRequests++);

        SDL.Event quitEvent = new()
        {
            Type = (uint)SDL.EventType.Quit,
            Quit = new SDL.QuitEvent { Type = SDL.EventType.Quit },
        };
        Assert.True(SDL.PushEvent(ref quitEvent));

        host.Platform.Pump(TimeSpan.Zero);

        Assert.True(host.Platform.Window.CloseRequested);
        Assert.Equal(1, exitRequests);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void HiddenStartupStateDoesNotShowTheWindow()
    {
        using SdlTestHost host = SdlTestHost.Create(windowState: WindowState.Hidden);

        Assert.Equal(WindowState.Hidden, host.Platform.Window.State);
    }

    [Theory]
    [InlineData(WindowState.Fullscreen)]
    [InlineData(WindowState.BorderlessFullscreen)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void StartupWindowStatesAreApplied(WindowState state)
    {
        using SdlTestHost host = SdlTestHost.Create(windowState: state);
        IWindow window = host.Platform.Window;
        if (window.State != state)
        {
            Assert.Skip($"The dummy video driver does not support the {state} startup state.");
        }

        Assert.Equal(state, window.State);
        Assert.True(window.IsFullscreen);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SettersValidateArgumentsImmediately()
    {
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
    public void SetSizeWhileFullscreenIsRememberedForTheWindowedRestore()
    {
        using SdlTestHost host = SdlTestHost.Create();
        IWindow window = host.Platform.Window;

        window.SetFullscreen(true);
        host.Platform.Pump(TimeSpan.Zero);
        if (!window.IsFullscreen)
        {
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
    public void DisposeBeforeInitializeIsSafe()
    {
        SdlPlatform platform = new(NullLoggerFactory.Instance);

        Assert.Throws<InvalidOperationException>(() => platform.Pump(TimeSpan.Zero));
        platform.Dispose();
        platform.Dispose();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DisposeIsIdempotentAndRejectsLaterPump()
    {
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
    public void VulkanWindowExposesRequiredInstanceExtensions()
    {
        using SdlTestHost host = SdlTestHost.CreateVulkan();

        IReadOnlyList<string> extensions = host.Platform.GetVulkanInstanceExtensions();

        Assert.Contains("VK_KHR_surface", extensions);
        if (OperatingSystem.IsWindows())
        {
            Assert.Contains("VK_KHR_win32_surface", extensions);
        }
        else
        {
            Assert.Contains(extensions, extension =>
                extension
                    is "VK_KHR_xlib_surface"
                        or "VK_KHR_xcb_surface"
                        or "VK_KHR_wayland_surface"
                        or "VK_KHR_android_surface"
            );
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void VulkanInstanceExtensionsAreDeduplicatedAndOrdinalSorted()
    {
        using SdlTestHost host = SdlTestHost.CreateVulkan();

        string[] extensions = [.. host.Platform.GetVulkanInstanceExtensions()];

        Assert.NotEmpty(extensions);
        Assert.Equal(extensions.Order(StringComparer.Ordinal).ToArray(), extensions);
        Assert.Equal(extensions.Length, extensions.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GetVulkanInstanceExtensionsOnNonVulkanWindowThrowsPlatformException()
    {
        using SdlTestHost host = SdlTestHost.Create();

        PlatformException exception = Assert.Throws<PlatformException>(() =>
            host.Platform.GetVulkanInstanceExtensions()
        );

        Assert.Equal(DiagnosticCodes.VULKAN_SURFACE_CREATION_FAILED, exception.DiagnosticCode);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GetVulkanInstanceExtensionsBeforeInitializeThrowsPlatformException()
    {
        SdlPlatform platform = new(NullLoggerFactory.Instance);

        Assert.Throws<PlatformException>(() => platform.GetVulkanInstanceExtensions());
        platform.Dispose();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CreateVulkanSurfaceOnNonVulkanWindowThrowsInvalidOperationException()
    {
        using SdlTestHost host = SdlTestHost.Create();

        Assert.Throws<InvalidOperationException>(() =>
            host.Platform.CreateVulkanSurface(IntPtr.Zero)
        );
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CreateVulkanSurfaceBeforeInitializeThrowsInvalidOperationException()
    {
        SdlPlatform platform = new(NullLoggerFactory.Instance);

        Assert.Throws<InvalidOperationException>(() => platform.CreateVulkanSurface(IntPtr.Zero));
        platform.Dispose();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CreateVulkanSurfaceAfterDisposeThrowsObjectDisposedException()
    {
        SdlTestHost host = SdlTestHost.Create();
        SdlPlatform platform = host.Platform;

        platform.Dispose();

        Assert.Throws<ObjectDisposedException>(() => platform.CreateVulkanSurface(IntPtr.Zero));
        host.Dispose();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DestroyVulkanSurfaceIgnoresZeroAndUnknownHandles()
    {
        using SdlTestHost host = SdlTestHost.Create();
        IntPtr unknownSurface = new(0x5EED);

        host.Platform.DestroyVulkanSurface(IntPtr.Zero, IntPtr.Zero);
        host.Platform.DestroyVulkanSurface(IntPtr.Zero, unknownSurface);
        host.Platform.DestroyVulkanSurface(IntPtr.Zero, unknownSurface);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DestroyVulkanSurfaceIgnoresUnknownHandlesOnVulkanWindow()
    {
        using SdlTestHost host = SdlTestHost.CreateVulkan();
        IntPtr unknownSurface = new(0x5EED);

        host.Platform.DestroyVulkanSurface(IntPtr.Zero, unknownSurface);
        host.Platform.DestroyVulkanSurface(IntPtr.Zero, unknownSurface);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void VulkanWindowCreationSucceedsAndTheWindowIsUsable()
    {
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
    public void WindowHdrStateOnAHiddenVulkanWindowSatisfiesTheInvariants()
    {
        using SdlTestHost host = SdlTestHost.CreateVulkan();

        WindowHdrState state = host.Platform.WindowHdrState;

        _output.WriteLine(
            "Observed window HDR state: enabled {0}, SDR white level {1}, HDR headroom {2}.",
            state.HdrEnabled,
            state.SdrWhiteLevel,
            state.HdrHeadroom
        );
        Assert.True(float.IsFinite(state.SdrWhiteLevel));
        Assert.True(state.SdrWhiteLevel > 0f);
        Assert.True(float.IsFinite(state.HdrHeadroom));
        Assert.True(state.HdrHeadroom >= 1f);
        if (!state.HdrEnabled)
        {
            Assert.Equal(1f, state.SdrWhiteLevel);
            Assert.Equal(1f, state.HdrHeadroom);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WindowHdrStateIsReadableFromAnotherThread()
    {
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
    public void WindowHdrStateIsSdrBeforeInitializeAndAfterDispose()
    {
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
    public void TwoPlatformsShareTheProcessWideSdlLifetime()
    {
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

        second.Window.SetSize(400, 300);
        second.Platform.Pump(TimeSpan.Zero);
        Assert.Equal(400, second.Window.Width);
        Assert.Equal(300, second.Window.Height);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task PlatformsCanBeCreatedConcurrently()
    {
        Task<SdlTestHost> firstTask = Task.Run(() => SdlTestHost.Create());
        Task<SdlTestHost> secondTask = Task.Run(() => SdlTestHost.Create());

        SdlTestHost? first = null;
        SdlTestHost? second = null;
        try
        {
            await Task.WhenAll(firstTask, secondTask);
            first = await firstTask;
            second = await secondTask;

            Assert.NotEqual(first.Window.WindowId, second.Window.WindowId);
            first.Platform.Pump(TimeSpan.Zero);
            second.Platform.Pump(TimeSpan.Zero);
        }
        finally
        {
            if (first is null && firstTask.Status == TaskStatus.RanToCompletion)
            {
                first = await firstTask;
            }

            if (second is null && secondTask.Status == TaskStatus.RanToCompletion)
            {
                second = await secondTask;
            }

            first?.Dispose();
            second?.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void VulkanPlatformsShareTheLoaderReferenceCount()
    {
        if (!GpuTestHost.IsVulkanLoaderAvailable())
        {
            Assert.Skip("The Vulkan loader is unavailable.");
            return;
        }

        using SdlTestHost first = SdlTestHost.CreateVulkan();
        using SdlTestHost second = SdlTestHost.CreateVulkan();
        Assert.Equal(2, SdlPlatform.SdlUserCountForTests);

        VulkanInstance? instance = null;
        IntPtr firstSurface = IntPtr.Zero;
        IntPtr secondSurface = IntPtr.Zero;
        try
        {
            IReadOnlyList<string> extensions;
            try
            {
                extensions = first.Platform.GetVulkanInstanceExtensions();
            }
            catch (PlatformException exception)
            {
                Assert.Skip(
                    $"SDL cannot provide the Vulkan instance extensions: {exception.Message}"
                );
                return;
            }

            try
            {
                instance = VulkanInstance.Create(
                    new VulkanInstanceOptions
                    {
                        ApplicationName = "RayBork SDL test",
                        ApplicationVersion = EngineVersion.Current,
                        SurfaceExtensions = extensions,
                        RequestValidation = false,
                        Logger = NullLogger.Instance,
                    }
                );
            }
            catch (GraphicsException exception)
            {
                Assert.Skip($"A Vulkan instance is unavailable: {exception.Message}");
                return;
            }

            try
            {
                firstSurface = first.Platform.CreateVulkanSurface(instance.Handle.Handle);
                secondSurface = second.Platform.CreateVulkanSurface(instance.Handle.Handle);
            }
            catch (PlatformException exception)
            {
                Assert.Skip($"SDL cannot create a Vulkan surface: {exception.Message}");
                return;
            }

            Assert.Equal(2, SdlPlatform.VulkanLoaderUserCountForTests);

            first.Platform.DestroyVulkanSurface(instance.Handle.Handle, firstSurface);
            firstSurface = IntPtr.Zero;
            first.Dispose();
            Assert.Equal(1, SdlPlatform.SdlUserCountForTests);
            Assert.Equal(1, SdlPlatform.VulkanLoaderUserCountForTests);

            second.Platform.DestroyVulkanSurface(instance.Handle.Handle, secondSurface);
            secondSurface = IntPtr.Zero;
            second.Dispose();
            Assert.Equal(0, SdlPlatform.SdlUserCountForTests);
            Assert.Equal(0, SdlPlatform.VulkanLoaderUserCountForTests);
        }
        finally
        {
            if (instance is not null)
            {
                if (firstSurface != IntPtr.Zero)
                {
                    first.Platform.DestroyVulkanSurface(instance.Handle.Handle, firstSurface);
                }

                if (secondSurface != IntPtr.Zero)
                {
                    second.Platform.DestroyVulkanSurface(instance.Handle.Handle, secondSurface);
                }
            }

            first.Dispose();
            second.Dispose();
            instance?.Dispose();
            instance?.DisposeFromMainThread();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void NonVulkanWindowCreationSucceedsAndTheWindowIsUsable()
    {
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

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CursorOnScreenFollowsMouseEnterAndLeaveEvents()
    {
        using SdlTestHost host = SdlTestHost.Create();
        SdlWindow window = host.Window;
        DrainWindowEvents(host);

        // The dummy driver shows the window through SDL's core fallback, which makes the window the
        // mouse focus, so the snapshot starts true.
        Assert.True(window.IsCursorOnScreen);
        bool observedOnWorker = false;
        Thread worker = new(() => observedOnWorker = window.IsCursorOnScreen);
        worker.Start();
        worker.Join();
        Assert.True(observedOnWorker);

        PushWindowEvent(window.WindowId, SDL.EventType.WindowMouseLeave);
        host.Platform.Pump(TimeSpan.Zero);
        Assert.False(window.IsCursorOnScreen);

        PushWindowEvent(window.WindowId, SDL.EventType.WindowMouseEnter);
        host.Platform.Pump(TimeSpan.Zero);
        Assert.True(window.IsCursorOnScreen);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CursorOnScreenRefreshesFromThePlatformOnFocusChanges()
    {
        using SdlTestHost host = SdlTestHost.Create();
        SdlWindow window = host.Window;
        IntPtr handle = GetHandle(host);
        DrainWindowEvents(host);
        if (SDL.GetMouseFocus() != handle)
        {
            Assert.Skip("The dummy video driver does not report mouse focus for a shown window.");
        }

        Assert.True(window.IsCursorOnScreen);

        // A pushed leave clears the snapshot without changing the platform state. The focus gain
        // must refresh it from the platform instead of preserving or assuming a value.
        PushWindowEvent(window.WindowId, SDL.EventType.WindowMouseLeave);
        host.Platform.Pump(TimeSpan.Zero);
        Assert.False(window.IsCursorOnScreen);
        Assert.Equal(handle, SDL.GetMouseFocus());

        PushWindowEvent(window.WindowId, SDL.EventType.WindowFocusGained);
        host.Platform.Pump(TimeSpan.Zero);
        Assert.True(window.IsCursorOnScreen);

        // Focus loss does not move the pointer: the refreshed value stays true instead of assuming
        // the window lost the pointer.
        PushWindowEvent(window.WindowId, SDL.EventType.WindowFocusLost);
        host.Platform.Pump(TimeSpan.Zero);
        Assert.Equal(handle, SDL.GetMouseFocus());
        Assert.True(window.IsCursorOnScreen);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CursorOnScreenClearsForMinimizeAndHiddenEvents()
    {
        using SdlTestHost host = SdlTestHost.Create();
        SdlWindow window = host.Window;
        IntPtr handle = GetHandle(host);
        DrainWindowEvents(host);
        Assert.True(window.IsCursorOnScreen);

        // The dummy driver has no minimize implementation, so the event is pushed directly; the
        // platform handler clears the snapshot and stops text input.
        PushWindowEvent(window.WindowId, SDL.EventType.WindowMinimized);
        host.Platform.Pump(TimeSpan.Zero);
        Assert.False(window.IsCursorOnScreen);

        PushWindowEvent(window.WindowId, SDL.EventType.WindowRestored);
        host.Platform.Pump(TimeSpan.Zero);
        Assert.True(window.IsCursorOnScreen);

        Assert.True(SDL.HideWindow(handle));
        host.Platform.Pump(TimeSpan.Zero);
        Assert.Equal(WindowState.Hidden, window.State);
        Assert.False(window.IsCursorOnScreen);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CursorModeDoesNotChangeCursorOnScreen()
    {
        using SdlTestHost host = SdlTestHost.Create();
        IWindow window = host.Platform.Window;
        DrainWindowEvents(host);
        Assert.True(window.IsCursorOnScreen);

        window.SetCursor(CursorMode.Hidden);
        host.Platform.Pump(TimeSpan.Zero);
        Assert.True(window.IsCursorOnScreen);

        window.SetCursor(CursorMode.Disabled);
        host.Platform.Pump(TimeSpan.Zero);
        Assert.True(window.IsCursorOnScreen);

        window.SetCursor(CursorMode.Normal);
        host.Platform.Pump(TimeSpan.Zero);
        Assert.True(window.IsCursorOnScreen);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TextInputStopsOnFocusLossAndRestartsOnFocusGain()
    {
        using SdlTestHost host = SdlTestHost.Create();
        SdlWindow window = host.Window;
        IntPtr handle = GetHandle(host);
        DrainWindowEvents(host);
        if (!SDL.TextInputActive(handle))
        {
            Assert.Skip("The dummy video driver did not activate text input.");
        }

        if (!window.IsFocused)
        {
            Assert.Skip("The dummy video driver does not report keyboard focus.");
        }

        PushWindowEvent(window.WindowId, SDL.EventType.WindowFocusLost);
        host.Platform.Pump(TimeSpan.Zero);
        Assert.False(SDL.TextInputActive(handle));

        PushWindowEvent(window.WindowId, SDL.EventType.WindowFocusGained);
        host.Platform.Pump(TimeSpan.Zero);
        Assert.True(SDL.TextInputActive(handle));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TextInputStopsForMinimizeAndHideAndDoesNotRestartWhileHidden()
    {
        using SdlTestHost host = SdlTestHost.Create();
        SdlWindow window = host.Window;
        IntPtr handle = GetHandle(host);
        DrainWindowEvents(host);
        if (!SDL.TextInputActive(handle))
        {
            Assert.Skip("The dummy video driver did not activate text input.");
        }

        PushWindowEvent(window.WindowId, SDL.EventType.WindowMinimized);
        host.Platform.Pump(TimeSpan.Zero);
        Assert.False(SDL.TextInputActive(handle));

        PushWindowEvent(window.WindowId, SDL.EventType.WindowRestored);
        host.Platform.Pump(TimeSpan.Zero);
        Assert.True(SDL.TextInputActive(handle));

        Assert.True(SDL.HideWindow(handle));
        host.Platform.Pump(TimeSpan.Zero);
        Assert.False(SDL.TextInputActive(handle));

        // A hidden window must not restart text input, even when a focus event claims it is active.
        PushWindowEvent(window.WindowId, SDL.EventType.WindowFocusGained);
        host.Platform.Pump(TimeSpan.Zero);
        Assert.False(SDL.TextInputActive(handle));

        PushWindowEvent(window.WindowId, SDL.EventType.WindowRestored);
        host.Platform.Pump(TimeSpan.Zero);
        Assert.False(SDL.TextInputActive(handle));

        Assert.True(SDL.ShowWindow(handle));
        host.Platform.Pump(TimeSpan.Zero);
        Assert.True(window.IsFocused);
        Assert.True(SDL.TextInputActive(handle));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SetTextInputAreaQueuesRoundedWindowCoordinates()
    {
        using SdlTestHost host = SdlTestHost.Create();
        IWindow window = host.Platform.Window;
        IntPtr handle = GetHandle(host);
        DrainWindowEvents(host);
        if (!SDL.GetTextInputArea(handle, out SDL.Rect initial, out int initialCursor))
        {
            Assert.Skip("The dummy video driver does not report the text input area.");
        }

        Assert.Equal(0, initial.W);
        Assert.Equal(0, initial.H);
        Assert.Equal(0, initialCursor);

        window.SetTextInputArea(new Rect(12.4f, 20.6f, 100.4f, 30.6f), 7);

        // The setter is queued: the native area changes only after the main thread applies it.
        Assert.True(SDL.GetTextInputArea(handle, out SDL.Rect beforePump, out _));
        Assert.Equal(0, beforePump.W);

        host.Platform.Pump(TimeSpan.Zero);

        Assert.True(SDL.GetTextInputArea(handle, out SDL.Rect area, out int cursor));
        Assert.Equal(12, area.X);
        Assert.Equal(21, area.Y);
        Assert.Equal(100, area.W);
        Assert.Equal(31, area.H);
        Assert.Equal(7, cursor);
    }

    [Theory]
    [InlineData(0f, 40f)]
    [InlineData(100f, 0f)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ZeroSizeTextInputAreaClearsThePlatformArea(float width, float height)
    {
        using SdlTestHost host = SdlTestHost.Create();
        IWindow window = host.Platform.Window;
        IntPtr handle = GetHandle(host);
        DrainWindowEvents(host);
        if (!SDL.GetTextInputArea(handle, out _, out _))
        {
            Assert.Skip("The dummy video driver does not report the text input area.");
        }

        window.SetTextInputArea(new Rect(10f, 12f, 120f, 44f), 8);
        host.Platform.Pump(TimeSpan.Zero);

        window.SetTextInputArea(new Rect(10f, 12f, width, height), 8);
        host.Platform.Pump(TimeSpan.Zero);

        Assert.True(SDL.GetTextInputArea(handle, out SDL.Rect cleared, out int cursor));
        Assert.Equal(0, cleared.X);
        Assert.Equal(0, cleared.Y);
        Assert.Equal(0, cleared.W);
        Assert.Equal(0, cleared.H);
        Assert.Equal(0, cursor);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SetTextInputAreaIsAnyThreadAndValidatesOnTheCallerThread()
    {
        using SdlTestHost host = SdlTestHost.Create();
        IWindow window = host.Platform.Window;
        IntPtr handle = GetHandle(host);
        DrainWindowEvents(host);
        if (!SDL.GetTextInputArea(handle, out _, out _))
        {
            Assert.Skip("The dummy video driver does not report the text input area.");
        }

        Assert.Throws<ArgumentException>(() =>
            window.SetTextInputArea(new Rect(float.NaN, 0f, 10f, 10f), 0)
        );
        Assert.Throws<ArgumentException>(() =>
            window.SetTextInputArea(new Rect(3e9f, 0f, 10f, 10f), 0)
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            window.SetTextInputArea(new Rect(0f, 0f, -1f, 10f), 0)
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            window.SetTextInputArea(new Rect(0f, 0f, 10f, 10f), -1)
        );

        Exception? failure = null;
        Thread worker = new(() =>
            failure = Record.Exception(() =>
                window.SetTextInputArea(new Rect(8.6f, 9.4f, 64.2f, 32.8f), 5)
            )
        );
        worker.Start();
        worker.Join();
        Assert.Null(failure);

        host.Platform.Pump(TimeSpan.Zero);

        Assert.True(SDL.GetTextInputArea(handle, out SDL.Rect area, out int cursor));
        Assert.Equal(9, area.X);
        Assert.Equal(9, area.Y);
        Assert.Equal(64, area.W);
        Assert.Equal(33, area.H);
        Assert.Equal(5, cursor);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DisposeStopsTextInputBeforeDestroyingTheWindow()
    {
        TrackingLoggerFactory loggerFactory = new();
        SdlPlatform platform = new(loggerFactory, initializeGamepads: false);
        try
        {
            try
            {
                platform.Initialize(
                    new PlatformOptions
                    {
                        Title = "RayBork text input dispose test",
                        Width = 320,
                        Height = 200,
                        Resizable = true,
                        WindowState = WindowState.Normal,
                        EnableVulkan = false,
                    },
                    () => { },
                    DUMMY_VIDEO_DRIVER
                );
            }
            catch (Exception exception) when (exception is PlatformException or WindowException)
            {
                Assert.Skip($"SDL dummy video driver initialization failed: {exception.Message}");
            }

            uint windowId = ((SdlWindow)platform.Window).WindowId;
            IntPtr handle = SDL.GetWindowFromID(windowId);
            Assert.NotEqual(IntPtr.Zero, handle);
            Assert.True(SDL.TextInputActive(handle));

            // SDL answers text input queries only for a live window, so the stop is observed through
            // the platform lifecycle log; the window itself is destroyed immediately afterwards.
            platform.Dispose();

            Assert.Contains(loggerFactory.Entries, entry =>
                entry.Level == LogLevel.Debug && entry.Message == "Text input stopped."
            );
            Assert.Equal(IntPtr.Zero, SDL.GetWindowFromID(windowId));
        }
        finally
        {
            platform.Dispose();
        }
    }

    private static IntPtr GetHandle(SdlTestHost host)
    {
        IntPtr handle = SDL.GetWindowFromID(host.Window.WindowId);
        Assert.NotEqual(IntPtr.Zero, handle);
        return handle;
    }

    private static void DrainWindowEvents(SdlTestHost host)
    {
        host.Platform.Pump(TimeSpan.Zero);
        host.Window.BeginTick();
        host.Window.BeginTick();
    }

    private static void PushWindowEvent(uint windowId, SDL.EventType type)
    {
        SDL.Event sdlEvent = new()
        {
            Type = (uint)type,
            Window = new SDL.WindowEvent { Type = type, WindowID = windowId },
        };
        Assert.True(SDL.PushEvent(ref sdlEvent));
    }
}
