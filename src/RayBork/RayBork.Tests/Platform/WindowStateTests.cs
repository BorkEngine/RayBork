using Microsoft.Extensions.Logging.Abstractions;
using RayBork.Internal;
using SDL3;

namespace RayBork.Tests;

[Collection("SDL")]
public sealed class WindowStateTests
{
    private static readonly MonitorInfo _firstMonitor = new(
        10,
        "First",
        new Rect(0f, 0f, 1920f, 1080f),
        new Rect(0f, 0f, 1920f, 1040f),
        1f,
        60
    );
    private static readonly MonitorInfo _secondMonitor = new(
        20,
        "Second",
        new Rect(1920f, 0f, 2560f, 1440f),
        new Rect(1920f, 0f, 2560f, 1400f),
        1.5f,
        144
    );
    private static readonly MonitorInfo _thirdMonitor = new(
        30,
        "Third",
        new Rect(-1280f, 0f, 1280f, 720f),
        new Rect(-1280f, 0f, 1280f, 720f),
        2f,
        75
    );

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void FullscreenKindIsClearedOnlyForANativeWindowedStateWithoutAPendingEntry(
        bool nativeFullscreen,
        bool entryPending,
        bool expected
    )
    {
        Assert.Equal(expected, SdlWindow.ShouldClearFullscreenKind(nativeFullscreen, entryPending));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SetFullscreenPrefersBorderlessAndLeavesToWindowed()
    {
        using SdlTestHost host = SdlTestHost.Create();
        IWindow window = host.Platform.Window;
        IntPtr handle = GetHandle(host);
        if (!TryProbeNativeFullscreen(handle))
        {
            Assert.Skip("The dummy video driver does not support SDL fullscreen.");
        }

        // Probe borderless support with raw SDL before accepting the engine path: the engine only
        // falls back to exclusive fullscreen after the borderless request fails, and the exclusive
        // fallback also installs a display mode.
        bool borderlessSupported = TryProbeBorderlessFullscreen(handle);
        DrainWindowEvents(host);

        window.SetFullscreen(true);
        host.Platform.Pump(TimeSpan.Zero);

        Assert.True(window.IsFullscreen);
        if (borderlessSupported)
        {
            Assert.Equal(WindowState.BorderlessFullscreen, window.State);
            Assert.False(SDL.GetWindowFullscreenMode(handle).HasValue);
        }
        else
        {
            Assert.Equal(WindowState.Fullscreen, window.State);
            Assert.True(SDL.GetWindowFullscreenMode(handle).HasValue);
        }

        window.SetFullscreen(false);
        host.Platform.Pump(TimeSpan.Zero);

        Assert.False(window.IsFullscreen);
        Assert.Equal(WindowState.Normal, window.State);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void QueuedLeaveAfterANewerEntryKeepsTheBorderlessKind()
    {
        using SdlTestHost host = SdlTestHost.Create();
        IWindow window = host.Platform.Window;
        IntPtr handle = GetHandle(host);
        if (!TryProbeNativeFullscreen(handle))
        {
            Assert.Skip("The dummy video driver does not support SDL fullscreen.");
        }

        if (!TryProbeBorderlessFullscreen(handle))
        {
            Assert.Skip("The dummy video driver does not support borderless SDL fullscreen.");
        }

        DrainWindowEvents(host);

        window.SetFullscreen(true);
        host.Platform.Pump(TimeSpan.Zero);
        Assert.Equal(WindowState.BorderlessFullscreen, window.State);

        // Restore and re-enter in one command drain. The leave event queued by Restore is handled
        // after the newer entry has already been confirmed, so the leave handler may cancel only
        // the pending entry: clearing the kind would report Fullscreen for a borderless window.
        window.Restore();
        window.SetFullscreen(true);
        host.Platform.Pump(TimeSpan.Zero);

        Assert.True(window.IsFullscreen);
        Assert.Equal(WindowState.BorderlessFullscreen, window.State);
        Assert.False(SDL.GetWindowFullscreenMode(handle).HasValue);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ConfirmedExternalLeaveClearsTheKindForALaterExternalEntry()
    {
        using SdlTestHost host = SdlTestHost.Create();
        IWindow window = host.Platform.Window;
        IntPtr handle = GetHandle(host);
        if (!TryProbeNativeFullscreen(handle))
        {
            Assert.Skip("The dummy video driver does not support SDL fullscreen.");
        }

        DrainWindowEvents(host);

        window.SetFullscreen(true);
        host.Platform.Pump(TimeSpan.Zero);
        Assert.True(window.IsFullscreen);
        Assert.NotEqual(WindowState.Normal, window.State);

        // A raw leave is the OS/SDL-initiated path: the native flag clears immediately. Refresh the
        // snapshot without pumping, so the snapshot clear is the only code that can clear the kind;
        // the later external entry would reuse a stale BorderlessFullscreen kind otherwise.
        Assert.True(SDL.SetWindowFullscreen(handle, false));
        host.Window.RefreshSnapshot();
        Assert.False(window.IsFullscreen);
        Assert.Equal(WindowState.Normal, window.State);

        Assert.True(SDL.SetWindowFullscreen(handle, true));
        host.Window.RefreshSnapshot();
        Assert.Equal(WindowState.Fullscreen, window.State);
        Assert.NotEqual(WindowState.BorderlessFullscreen, window.State);

        // Pump the queued raw leave and external-entry events before re-entering through the engine.
        Assert.True(SDL.SetWindowFullscreen(handle, false));
        host.Platform.Pump(TimeSpan.Zero);
        Assert.False(window.IsFullscreen);

        // A cleared kind must not block a later engine entry through the pending-entry path.
        window.SetFullscreen(true);
        host.Platform.Pump(TimeSpan.Zero);
        Assert.True(window.IsFullscreen);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void NativeFullscreenBeforeRefreshRemembersSizeAndMonitor()
    {
        using SdlTestHost host = SdlTestHost.Create();
        IWindow window = host.Platform.Window;
        IntPtr handle = GetHandle(host);
        if (!TryProbeNativeFullscreen(handle))
        {
            Assert.Skip("The dummy video driver does not support SDL fullscreen.");
        }

        DrainWindowEvents(host);
        Assert.NotEmpty(window.Monitors);

        // Enter through raw SDL and do not pump: the engine snapshot still reports windowed while
        // the live native flag is already fullscreen.
        Assert.True(SDL.SetWindowFullscreen(handle, true));
        Assert.False(host.Window.IsFullscreen);
        Assert.True(SDL.GetWindowSize(handle, out int nativeWidth, out int nativeHeight));
        Assert.True(SDL.GetWindowPosition(handle, out int nativeX, out int nativeY));

        window.SetSize(500, 400);
        window.SetMonitor(window.Monitors[0].Index);
        host.Window.ApplyCommands();

        Assert.True(SDL.GetWindowSize(handle, out int currentWidth, out int currentHeight));
        Assert.True(SDL.GetWindowPosition(handle, out int currentX, out int currentY));
        Assert.Equal(nativeWidth, currentWidth);
        Assert.Equal(nativeHeight, currentHeight);
        Assert.Equal(nativeX, currentX);
        Assert.Equal(nativeY, currentY);

        window.SetFullscreen(false);
        host.Window.ApplyCommands();

        Assert.False(HasNativeFullscreen(handle));
        Assert.False(window.IsFullscreen);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MinimumAndMaximumSizeAreAppliedThroughTheWindowInterface()
    {
        using SdlTestHost host = SdlTestHost.Create();
        IWindow window = host.Platform.Window;
        IntPtr handle = GetHandle(host);
        DrainWindowEvents(host);

        // SDL stores the minimum and maximum size in its common layer, so a raw round trip decides
        // whether the driver supports reporting the constraint. A later mismatch after the engine
        // call can then only mean the engine failed to queue or apply it, not driver incapacity.
        if (
            !SDL.SetWindowMinimumSize(handle, 64, 48)
            || !SDL.GetWindowMinimumSize(handle, out int probeMinWidth, out int probeMinHeight)
            || probeMinWidth != 64
            || probeMinHeight != 48
        )
        {
            Assert.Skip("The dummy video driver does not report window minimum sizes.");
        }

        if (
            !SDL.SetWindowMaximumSize(handle, 1280, 1024)
            || !SDL.GetWindowMaximumSize(handle, out int probeMaxWidth, out int probeMaxHeight)
            || probeMaxWidth != 1280
            || probeMaxHeight != 1024
        )
        {
            Assert.Skip("The dummy video driver does not report window maximum sizes.");
        }

        window.SetMinSize(240, 180);
        window.SetMaxSize(900, 700);
        host.Platform.Pump(TimeSpan.Zero);

        Assert.True(SDL.GetWindowMinimumSize(handle, out int minWidth, out int minHeight));
        Assert.Equal(240, minWidth);
        Assert.Equal(180, minHeight);
        Assert.True(SDL.GetWindowMaximumSize(handle, out int maxWidth, out int maxHeight));
        Assert.Equal(900, maxWidth);
        Assert.Equal(700, maxHeight);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void FocusStateTracksNativeFlagsAndPushedEventsOnlyRefreshTheSnapshot()
    {
        using SdlTestHost host = SdlTestHost.Create();
        IWindow window = host.Platform.Window;
        IntPtr handle = GetHandle(host);
        DrainWindowEvents(host);

        Assert.True(SDL.HideWindow(handle));
        host.Platform.Pump(TimeSpan.Zero);
        Assert.False(window.IsFocused);
        Assert.False(HasInputFocus(handle));

        // Pushed window events only drive the platform refresh; they never change SDL's flags.
        PushWindowEvent(host.Window.WindowId, SDL.EventType.WindowFocusGained);
        host.Platform.Pump(TimeSpan.Zero);
        Assert.False(window.IsFocused);
        Assert.False(HasInputFocus(handle));

        Assert.True(SDL.ShowWindow(handle));
        host.Platform.Pump(TimeSpan.Zero);
        Assert.True(window.IsFocused);
        Assert.True(HasInputFocus(handle));

        PushWindowEvent(host.Window.WindowId, SDL.EventType.WindowFocusLost);
        host.Platform.Pump(TimeSpan.Zero);
        Assert.True(window.IsFocused);
        Assert.True(HasInputFocus(handle));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void BorderlessTogglesTheNativeBorderWithoutEnteringFullscreen()
    {
        using SdlTestHost host = SdlTestHost.Create();
        IWindow window = host.Platform.Window;
        IntPtr handle = GetHandle(host);
        DrainWindowEvents(host);
        Assert.Equal(WindowState.Normal, window.State);

        window.SetBorderless(true);
        host.Platform.Pump(TimeSpan.Zero);
        if (!HasBorderless(handle))
        {
            Assert.Skip("The dummy video driver ignores SDL_SetWindowBordered.");
        }

        Assert.True(HasBorderless(handle));
        Assert.False(window.IsFullscreen);
        Assert.Equal(WindowState.Normal, window.State);

        window.SetBorderless(false);
        host.Platform.Pump(TimeSpan.Zero);
        Assert.False(HasBorderless(handle));
        Assert.False(window.IsFullscreen);
        Assert.Equal(WindowState.Normal, window.State);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DisplayScaleAndPixelSizeEventsReportExactlyOneResize()
    {
        using SdlTestHost host = SdlTestHost.Create();
        SdlWindow window = host.Window;

        // Drain the window-creation events and clear the resize flag so the pushed events are the
        // only possible source of the next WasResized report.
        DrainWindowEvents(host);
        Assert.False(window.WasResized);

        PushWindowEvent(window.WindowId, SDL.EventType.WindowDisplayScaleChanged);
        PushWindowEvent(window.WindowId, SDL.EventType.WindowPixelSizeChanged);
        host.Platform.Pump(TimeSpan.Zero);

        window.BeginTick();
        Assert.True(window.WasResized);
        window.BeginTick();
        Assert.False(window.WasResized);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SetMonitorValidatesTheSnapshotOnAnotherThread()
    {
        using SdlTestHost host = SdlTestHost.Create();
        SdlWindow window = host.Window;
        Assert.NotEmpty(window.Monitors);

        int validIndex = window.Monitors[0].Index;
        int invalidIndex = window.Monitors[window.Monitors.Count - 1].Index + 1;
        Exception? validFailure = null;
        Exception? invalidFailure = null;
        Thread worker = new(() =>
        {
            validFailure = Record.Exception(() => window.SetMonitor(validIndex));
            invalidFailure = Record.Exception(() => window.SetMonitor(invalidIndex));
        });
        worker.Start();
        worker.Join();

        Assert.Null(validFailure);
        Assert.IsType<ArgumentOutOfRangeException>(invalidFailure);

        host.Platform.Pump(TimeSpan.Zero);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SetMonitorAcceptsAStableIndexThatLeavesAGapInTheWindowSnapshot()
    {
        using SdlTestHost host = SdlTestHost.Create();
        IntPtr handle = GetHandle(host);

        // Removing the middle display keeps its stable index reserved, so the snapshot holds
        // indices 0 and 2 while Count is 2. The count is not an upper bound for a stable index, so
        // the window must validate against the indices themselves or it rejects a valid monitor.
        MonitorRegistry registry = new();
        registry.Update([_firstMonitor, _secondMonitor, _thirdMonitor], _firstMonitor.DisplayId);
        registry.Update([_firstMonitor, _thirdMonitor], _firstMonitor.DisplayId);
        SdlWindow window = new(
            handle,
            registry,
            NullLogger.Instance,
            "Monitor gap",
            320,
            200,
            () => { }
        );

        window.RefreshSnapshot();

        Assert.Equal(2, window.Monitors.Count);
        Assert.Equal(0, window.Monitors[0].Index);
        Assert.Equal(2, window.Monitors[1].Index);
        Assert.Null(Record.Exception(() => window.SetMonitor(2)));
        Assert.IsType<ArgumentOutOfRangeException>(Record.Exception(() => window.SetMonitor(1)));

        // Drain the accepted move so no command outlives the test window and the queue path is
        // exercised for the gap index.
        Assert.True(window.ApplyCommands());
    }

    private static IntPtr GetHandle(SdlTestHost host)
    {
        IntPtr handle = SDL.GetWindowFromID(host.Window.WindowId);
        Assert.NotEqual(IntPtr.Zero, handle);
        return handle;
    }

    private static bool TryProbeNativeFullscreen(IntPtr handle)
    {
        if (!SDL.SetWindowFullscreen(handle, true))
        {
            return false;
        }

        bool entered = HasNativeFullscreen(handle);
        bool left = SDL.SetWindowFullscreen(handle, false);
        return entered && left && !HasNativeFullscreen(handle);
    }

    private static bool TryProbeBorderlessFullscreen(IntPtr handle)
    {
        if (!SDL.SetWindowFullscreenMode(handle, IntPtr.Zero))
        {
            return false;
        }

        if (!SDL.SetWindowFullscreen(handle, true))
        {
            return false;
        }

        bool entered = HasNativeFullscreen(handle);
        bool left = SDL.SetWindowFullscreen(handle, false);
        return entered && left && !HasNativeFullscreen(handle);
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

    private static bool HasNativeFullscreen(IntPtr handle) =>
        (SDL.GetWindowFlags(handle) & SDL.WindowFlags.Fullscreen) != 0;

    private static bool HasInputFocus(IntPtr handle) =>
        (SDL.GetWindowFlags(handle) & SDL.WindowFlags.InputFocus) != 0;

    private static bool HasBorderless(IntPtr handle) =>
        (SDL.GetWindowFlags(handle) & SDL.WindowFlags.Borderless) != 0;
}
