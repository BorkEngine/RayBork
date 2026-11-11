using RayBork.Internal;

namespace RayBork.Tests;

/// <summary>
/// A snapshot-backed <see cref="IPlatformWindow"/> for host tests. Setters update the snapshots
/// directly, <see cref="Close"/> requests an app exit, and <see cref="BeginTick"/> clears
/// <see cref="WasResized"/>.
/// </summary>
internal sealed class FakePlatformWindow : IPlatformWindow {
    private readonly Action _exitRequested;
    private string _clipboardText = string.Empty;

    internal FakePlatformWindow(Action exitRequested) {
        ArgumentNullException.ThrowIfNull(exitRequested);
        _exitRequested = exitRequested;
    }

    public string Title { get; set; } = "RayBork";

    public int Width { get; set; } = 1280;

    public int Height { get; set; } = 720;

    public int RenderWidth => Width;

    public int RenderHeight => Height;

    public float DpiScale { get; set; } = 1f;

    public Rect Bounds { get; set; } = new(0f, 0f, 1280f, 720f);

    public bool IsResizable { get; set; }

    public bool IsFullscreen { get; set; }

    public bool IsMinimized { get; set; }

    public bool IsFocused { get; set; } = true;

    public bool WasResized { get; set; }

    public bool CloseRequested { get; private set; }

    public WindowState State { get; set; } = WindowState.Normal;

    public IReadOnlyList<Monitor> Monitors { get; } = [
        new Monitor(
            0,
            "Fake Monitor",
            new Rect(0f, 0f, 1920f, 1080f),
            new Rect(0f, 0f, 1920f, 1040f),
            1f,
            60),
    ];

    public Monitor CurrentMonitor => Monitors[0];

    public CursorMode Cursor { get; private set; }

    public (int Width, int Height)? MinSize { get; private set; }

    public (int Width, int Height)? MaxSize { get; private set; }

    public int RequestedMonitorIndex { get; private set; }

    public void ApplyPendingCommands() {
    }

    public void BeginTick() => WasResized = false;

    public void SetTitle(string title) => Title = title;

    public void SetSize(int width, int height) {
        Width = width;
        Height = height;
    }

    public void SetFullscreen(bool fullscreen) {
        IsFullscreen = fullscreen;
        State = fullscreen ? WindowState.Fullscreen : WindowState.Normal;
    }

    public void SetBorderless(bool borderless) {
        State = borderless ? WindowState.BorderlessFullscreen : WindowState.Normal;
    }

    public void SetPosition(int x, int y) => Bounds = new Rect(x, y, Bounds.Width, Bounds.Height);

    public void SetMonitor(int monitorIndex) {
        ArgumentOutOfRangeException.ThrowIfNegative(monitorIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(monitorIndex, Monitors.Count);
        RequestedMonitorIndex = monitorIndex;
    }

    public void SetMinSize(int width, int height) => MinSize = (width, height);

    public void SetMaxSize(int width, int height) => MaxSize = (width, height);

    public void Minimize() {
        IsMinimized = true;
        State = WindowState.Minimized;
    }

    public void Maximize() {
        IsMinimized = false;
        State = WindowState.Maximized;
    }

    public void Restore() {
        IsMinimized = false;
        State = WindowState.Normal;
    }

    public void SetCursor(CursorMode mode) => Cursor = mode;

    public void SetClipboardText(string text) => _clipboardText = text;

    public string GetClipboardText() => _clipboardText;

    public void Close() {
        CloseRequested = true;
        _exitRequested();
    }

    internal void ApplyStartupOptions(PlatformOptions options) {
        Title = options.Title;
        Width = options.Width;
        Height = options.Height;
        IsResizable = options.Resizable;
        State = options.WindowState;
        IsMinimized = options.WindowState == WindowState.Minimized;
    }
}
