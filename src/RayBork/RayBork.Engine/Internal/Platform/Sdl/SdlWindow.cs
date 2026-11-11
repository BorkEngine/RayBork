using Microsoft.Extensions.Logging;
using SDL3;

namespace RayBork.Internal;

/// <summary>
/// The SDL3 window implementation. Getter snapshots may be read from any thread; every setter queues
/// a command and every native SDL call happens on the main thread in
/// <see cref="ApplyPendingCommands"/>. Tick-boundary flags are synchronized with the tick thread
/// through a <see cref="Lock"/>.
/// </summary>
internal sealed class SdlWindow : IPlatformWindow {
    private const float DEFAULT_DPI_SCALE = 1f;

    private readonly Lock _gate = new();
    private readonly IntPtr _handle;
    private readonly MonitorRegistry _monitors;
    private readonly ILogger _logger;
    private readonly Action _onExitRequested;
    private readonly WindowCommandQueue _commands = new();
    private readonly uint _windowId;

    private string _title;
    private int _width;
    private int _height;
    private int _renderWidth;
    private int _renderHeight;
    private float _dpiScale;
    private Rect _bounds;
    private bool _resizable;
    private bool _fullscreen;
    private bool _minimized;
    private bool _focused;
    private bool _wasResized;
    private bool _resizePending;
    private bool _closeRequested;
    private WindowState _state;
    private Monitor[] _monitorSnapshot = [];
    private Monitor _currentMonitor;
    private string _clipboardText = string.Empty;
    private FullscreenKind _fullscreenKind;

    private int _windowedWidth;
    private int _windowedHeight;
    private int _windowedX;
    private int _windowedY;
    private bool _hasWindowedSize;
    private bool _hasWindowedPosition;

    internal SdlWindow(
        IntPtr handle,
        MonitorRegistry monitors,
        ILogger logger,
        string title,
        int width,
        int height,
        Action onExitRequested) {
        if (handle == IntPtr.Zero) {
            throw new ArgumentException("The SDL3 window handle must not be zero.", nameof(handle));
        }

        ArgumentNullException.ThrowIfNull(monitors);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(onExitRequested);

        _handle = handle;
        _monitors = monitors;
        _logger = logger;
        _onExitRequested = onExitRequested;
        _title = title;
        _width = width;
        _height = height;
        _renderWidth = width;
        _renderHeight = height;
        _dpiScale = DEFAULT_DPI_SCALE;
        _bounds = new Rect(0f, 0f, width, height);
        _windowId = SDL.GetWindowID(handle);
    }

    /// <summary>
    /// Gets the SDL3 window id used to filter window events.
    /// </summary>
    internal uint WindowId => _windowId;

    /// <inheritdoc />
    public string Title {
        get {
            lock (_gate) {
                return _title;
            }
        }
    }

    /// <inheritdoc />
    public int Width {
        get {
            lock (_gate) {
                return _width;
            }
        }
    }

    /// <inheritdoc />
    public int Height {
        get {
            lock (_gate) {
                return _height;
            }
        }
    }

    /// <inheritdoc />
    public int RenderWidth {
        get {
            lock (_gate) {
                return _renderWidth;
            }
        }
    }

    /// <inheritdoc />
    public int RenderHeight {
        get {
            lock (_gate) {
                return _renderHeight;
            }
        }
    }

    /// <inheritdoc />
    public float DpiScale {
        get {
            lock (_gate) {
                return _dpiScale;
            }
        }
    }

    /// <inheritdoc />
    public Rect Bounds {
        get {
            lock (_gate) {
                return _bounds;
            }
        }
    }

    /// <inheritdoc />
    public bool IsResizable {
        get {
            lock (_gate) {
                return _resizable;
            }
        }
    }

    /// <inheritdoc />
    public bool IsFullscreen {
        get {
            lock (_gate) {
                return _fullscreen;
            }
        }
    }

    /// <inheritdoc />
    public bool IsMinimized {
        get {
            lock (_gate) {
                return _minimized;
            }
        }
    }

    /// <inheritdoc />
    public bool IsFocused {
        get {
            lock (_gate) {
                return _focused;
            }
        }
    }

    /// <inheritdoc />
    public bool WasResized {
        get {
            lock (_gate) {
                return _wasResized;
            }
        }
    }

    /// <inheritdoc />
    public bool CloseRequested {
        get {
            lock (_gate) {
                return _closeRequested;
            }
        }
    }

    /// <inheritdoc />
    public WindowState State {
        get {
            lock (_gate) {
                return _state;
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<Monitor> Monitors {
        get {
            lock (_gate) {
                return _monitorSnapshot;
            }
        }
    }

    /// <inheritdoc />
    public Monitor CurrentMonitor {
        get {
            lock (_gate) {
                return _currentMonitor;
            }
        }
    }

    /// <inheritdoc />
    public void SetTitle(string title) => _commands.EnqueueSetTitle(title);

    /// <inheritdoc />
    public void SetSize(int width, int height) => _commands.EnqueueSetSize(width, height);

    /// <inheritdoc />
    public void SetFullscreen(bool fullscreen) => _commands.EnqueueSetFullscreen(fullscreen);

    /// <inheritdoc />
    public void SetBorderless(bool borderless) => _commands.EnqueueSetBorderless(borderless);

    /// <inheritdoc />
    public void SetPosition(int x, int y) => _commands.EnqueueSetPosition(x, y);

    /// <inheritdoc />
    public void SetMonitor(int monitorIndex) {
        int monitorCount;
        lock (_gate) {
            monitorCount = _monitorSnapshot.Length;
        }

        if (monitorIndex < 0 || monitorIndex >= monitorCount) {
            throw new ArgumentOutOfRangeException(
                nameof(monitorIndex),
                monitorIndex,
                "The monitor index is outside the current monitor snapshot.");
        }

        _commands.EnqueueSetMonitor(monitorIndex);
    }

    /// <inheritdoc />
    public void SetMinSize(int width, int height) => _commands.EnqueueSetMinSize(width, height);

    /// <inheritdoc />
    public void SetMaxSize(int width, int height) => _commands.EnqueueSetMaxSize(width, height);

    /// <inheritdoc />
    public void Minimize() => _commands.EnqueueMinimize();

    /// <inheritdoc />
    public void Maximize() => _commands.EnqueueMaximize();

    /// <inheritdoc />
    public void Restore() => _commands.EnqueueRestore();

    /// <inheritdoc />
    public void SetCursor(CursorMode mode) => _commands.EnqueueSetCursor(mode);

    /// <inheritdoc />
    public void SetClipboardText(string text) => _commands.EnqueueClipboardWrite(text);

    /// <inheritdoc />
    public string GetClipboardText() {
        string text;
        lock (_gate) {
            text = _clipboardText;
        }

        _commands.EnqueueClipboardRead();
        return text;
    }

    /// <inheritdoc />
    public void Close() => _commands.EnqueueClose();

    /// <inheritdoc />
    public void ApplyPendingCommands() => ApplyCommands();

    /// <inheritdoc />
    public void BeginTick() {
        lock (_gate) {
            _wasResized = _resizePending;
            _resizePending = false;
        }
    }

    /// <summary>
    /// Applies every queued command on the main thread and reports whether any command was pending.
    /// </summary>
    internal bool ApplyCommands() {
        bool applied = false;
        while (_commands.TryTake(out WindowCommand command)) {
            applied = true;
            Apply(command);
        }

        return applied;
    }

    /// <summary>
    /// Marks a resize for the next <see cref="BeginTick"/> without clearing a pending flag.
    /// </summary>
    internal void MarkResizePending() {
        lock (_gate) {
            _resizePending = true;
        }
    }

    /// <summary>
    /// Handles a resize-like window event on the main thread: refreshes the snapshot and marks the
    /// resize for the next tick.
    /// </summary>
    internal void HandleResizeEvent() {
        MarkResizePending();
        RefreshSnapshot();
    }

    /// <summary>
    /// Handles a window state, focus, or visibility event on the main thread.
    /// </summary>
    internal void HandleStateEvent() => RefreshSnapshot();

    /// <summary>
    /// Marks the window close requested and invokes the exit callback once. Runs on the main thread.
    /// </summary>
    internal void RequestClose() {
        bool shouldNotify;
        lock (_gate) {
            shouldNotify = !_closeRequested;
            _closeRequested = true;
        }

        if (shouldNotify) {
            _onExitRequested();
        }
    }

    /// <summary>
    /// Applies the requested startup state and shows the window unless it starts hidden. Runs on the
    /// main thread before the first event pump.
    /// </summary>
    internal void ApplyStartupState(WindowState state) {
        if (!Enum.IsDefined(state)) {
            throw new ArgumentOutOfRangeException(nameof(state), state, "The startup window state is not defined.");
        }

        switch (state) {
            case WindowState.Normal:
                ShowWindow();
                break;
            case WindowState.Hidden:
                break;
            case WindowState.Minimized:
                ShowWindow();
                ApplyMinimize();
                break;
            case WindowState.Maximized:
                ShowWindow();
                ApplyMaximize();
                break;
            case WindowState.Fullscreen:
                ShowWindow();
                ApplyStartupFullscreen();
                break;
            case WindowState.BorderlessFullscreen:
                ShowWindow();
                if (!TryEnterBorderlessFullscreen()) {
                    _logger.LogWarning("SDL failed to enter borderless fullscreen: {Error}", SDL.GetError());
                }
                break;
        }

        RefreshSnapshot();
    }

    /// <summary>
    /// Refreshes the Any-thread snapshot from the native window. Runs on the main thread.
    /// </summary>
    internal void RefreshSnapshot() {
        int width = _width;
        int height = _height;
        int renderWidth = _renderWidth;
        int renderHeight = _renderHeight;
        float dpiScale = _dpiScale;
        Rect bounds = _bounds;

        bool hasSize = SDL.GetWindowSize(_handle, out int logicalWidth, out int logicalHeight);
        if (hasSize) {
            width = logicalWidth;
            height = logicalHeight;
        }

        if (SDL.GetWindowSizeInPixels(_handle, out int pixelWidth, out int pixelHeight)) {
            renderWidth = pixelWidth;
            renderHeight = pixelHeight;
        }

        float displayScale = SDL.GetWindowDisplayScale(_handle);
        if (displayScale > 0f) {
            dpiScale = displayScale;
        } else if (dpiScale <= 0f) {
            dpiScale = DEFAULT_DPI_SCALE;
        }

        if (hasSize && SDL.GetWindowPosition(_handle, out int x, out int y)) {
            bounds = new Rect(x, y, width, height);
        }

        SDL.WindowFlags flags = SDL.GetWindowFlags(_handle);
        Monitor[] monitors = [.. _monitors.Monitors];
        Monitor currentMonitor = _monitors.GetCurrent(SDL.GetDisplayForWindow(_handle));
        WindowState state = ComputeState(flags);

        lock (_gate) {
            _width = width;
            _height = height;
            _renderWidth = renderWidth;
            _renderHeight = renderHeight;
            _dpiScale = dpiScale;
            _bounds = bounds;
            _resizable = (flags & SDL.WindowFlags.Resizable) != 0;
            _fullscreen = (flags & SDL.WindowFlags.Fullscreen) != 0;
            _minimized = (flags & SDL.WindowFlags.Minimized) != 0;
            _focused = (flags & SDL.WindowFlags.InputFocus) != 0;
            _state = state;
            _monitorSnapshot = monitors;
            _currentMonitor = currentMonitor;
        }
    }

    private WindowState ComputeState(SDL.WindowFlags flags) {
        if ((flags & SDL.WindowFlags.Minimized) != 0) {
            return WindowState.Minimized;
        }

        if ((flags & SDL.WindowFlags.Fullscreen) != 0) {
            return _fullscreenKind == FullscreenKind.Borderless
                ? WindowState.BorderlessFullscreen
                : WindowState.Fullscreen;
        }

        if ((flags & SDL.WindowFlags.Hidden) != 0) {
            return WindowState.Hidden;
        }

        if ((flags & SDL.WindowFlags.Maximized) != 0) {
            return WindowState.Maximized;
        }

        return WindowState.Normal;
    }

    private void Apply(WindowCommand command) {
        switch (command) {
            case SetTitleCommand title:
                ApplyTitle(title.Title);
                break;
            case SetSizeCommand size:
                ApplySize(size.Width, size.Height);
                break;
            case SetPositionCommand position:
                ApplyPosition(position.X, position.Y);
                break;
            case SetMonitorCommand monitor:
                ApplySetMonitor(monitor.MonitorIndex);
                break;
            case SetMinSizeCommand minimum:
                ApplyMinSize(minimum.Width, minimum.Height);
                break;
            case SetMaxSizeCommand maximum:
                ApplyMaxSize(maximum.Width, maximum.Height);
                break;
            case SetFullscreenCommand fullscreen:
                ApplyFullscreen(fullscreen.Fullscreen);
                break;
            case SetBorderlessCommand borderless:
                ApplyBorderless(borderless.Borderless);
                break;
            case SetCursorCommand cursor:
                ApplyCursor(cursor.Mode);
                break;
            case MinimizeCommand:
                ApplyMinimize();
                RefreshSnapshot();
                break;
            case MaximizeCommand:
                ApplyMaximize();
                RefreshSnapshot();
                break;
            case RestoreCommand:
                ApplyRestore();
                break;
            case WriteClipboardCommand clipboard:
                ApplyClipboardWrite(clipboard.Text);
                break;
            case ReadClipboardCommand:
                ApplyClipboardRead();
                break;
            case CloseCommand:
                RequestClose();
                break;
        }
    }

    private void ApplyTitle(string title) {
        if (!SDL.SetWindowTitle(_handle, title)) {
            _logger.LogWarning("SDL failed to set the window title: {Error}", SDL.GetError());
            return;
        }

        lock (_gate) {
            _title = title;
        }
    }

    private void ApplySize(int width, int height) {
        if (_fullscreenKind != FullscreenKind.None) {
            _windowedWidth = width;
            _windowedHeight = height;
            _hasWindowedSize = true;
            return;
        }

        if (!SDL.SetWindowSize(_handle, width, height)) {
            _logger.LogWarning("SDL failed to resize the window to {Width}x{Height}: {Error}", width, height, SDL.GetError());
            return;
        }

        MarkResizePending();
        RefreshSnapshot();
    }

    private void ApplyPosition(int x, int y) {
        if (!SDL.SetWindowPosition(_handle, x, y)) {
            _logger.LogWarning("SDL failed to move the window to ({X}, {Y}): {Error}", x, y, SDL.GetError());
            return;
        }

        RefreshSnapshot();
    }

    private void ApplySetMonitor(int monitorIndex) {
        if (!_monitors.TryGetDisplayId(monitorIndex, out uint displayId)) {
            _logger.LogWarning("Monitor index {MonitorIndex} is no longer available.", monitorIndex);
            return;
        }

        Monitor monitor = _monitors.GetCurrent(displayId);
        int x = (int)MathF.Round(monitor.WorkArea.X + ((monitor.WorkArea.Width - _width) / 2f));
        int y = (int)MathF.Round(monitor.WorkArea.Y + ((monitor.WorkArea.Height - _height) / 2f));
        if (_fullscreenKind != FullscreenKind.None) {
            _windowedX = x;
            _windowedY = y;
            _hasWindowedPosition = true;
            return;
        }

        if (!SDL.SetWindowPosition(_handle, x, y)) {
            _logger.LogWarning("SDL failed to move the window to monitor {MonitorIndex}: {Error}", monitorIndex, SDL.GetError());
            return;
        }

        RefreshSnapshot();
    }

    private void ApplyMinSize(int width, int height) {
        if (!SDL.SetWindowMinimumSize(_handle, width, height)) {
            _logger.LogWarning("SDL failed to set the minimum window size: {Error}", SDL.GetError());
        }
    }

    private void ApplyMaxSize(int width, int height) {
        if (!SDL.SetWindowMaximumSize(_handle, width, height)) {
            _logger.LogWarning("SDL failed to set the maximum window size: {Error}", SDL.GetError());
        }
    }

    private void ApplyFullscreen(bool fullscreen) {
        if (fullscreen) {
            if (_fullscreenKind != FullscreenKind.None || _fullscreen) {
                return;
            }

            RememberWindowedPlacement();
            if (!TryEnterBorderlessFullscreen()) {
                _logger.LogWarning(
                    "Borderless fullscreen is unavailable; falling back to windowed fullscreen: {Error}",
                    SDL.GetError());
                if (!TryEnterExclusiveFullscreen()) {
                    _logger.LogWarning("SDL failed to enter fullscreen: {Error}", SDL.GetError());
                    return;
                }
            }
        } else {
            if (_fullscreenKind == FullscreenKind.None && !_fullscreen) {
                return;
            }

            if (!SDL.SetWindowFullscreen(_handle, false)) {
                _logger.LogWarning("SDL failed to leave fullscreen: {Error}", SDL.GetError());
                return;
            }

            _fullscreenKind = FullscreenKind.None;
            ReapplyWindowedPlacement();
        }

        RefreshSnapshot();
    }

    private void ApplyBorderless(bool borderless) {
        if (!SDL.SetWindowBordered(_handle, !borderless)) {
            _logger.LogWarning("SDL failed to set the window border: {Error}", SDL.GetError());
            return;
        }

        RefreshSnapshot();
    }

    private void ApplyCursor(CursorMode mode) {
        bool relative = mode == CursorMode.Disabled;
        if (!SDL.SetWindowRelativeMouseMode(_handle, relative)) {
            _logger.LogWarning("SDL failed to set relative mouse mode to {Relative}: {Error}", relative, SDL.GetError());
        }

        bool cursorApplied = mode == CursorMode.Normal ? SDL.ShowCursor() : SDL.HideCursor();
        if (!cursorApplied) {
            _logger.LogWarning("SDL failed to set the cursor mode to {CursorMode}: {Error}", mode, SDL.GetError());
        }
    }

    private void ApplyMinimize() {
        if (!SDL.MinimizeWindow(_handle)) {
            _logger.LogWarning("SDL failed to minimize the window: {Error}", SDL.GetError());
        }
    }

    private void ApplyMaximize() {
        if (!SDL.MaximizeWindow(_handle)) {
            _logger.LogWarning("SDL failed to maximize the window: {Error}", SDL.GetError());
        }
    }

    private void ApplyRestore() {
        SDL.WindowFlags flags = SDL.GetWindowFlags(_handle);
        if ((flags & SDL.WindowFlags.Fullscreen) != 0) {
            if (!SDL.SetWindowFullscreen(_handle, false)) {
                _logger.LogWarning("SDL failed to leave fullscreen: {Error}", SDL.GetError());
            } else {
                _fullscreenKind = FullscreenKind.None;
            }
        }

        if ((flags & (SDL.WindowFlags.Minimized | SDL.WindowFlags.Maximized)) != 0) {
            if (!SDL.RestoreWindow(_handle)) {
                _logger.LogWarning("SDL failed to restore the window: {Error}", SDL.GetError());
            }
        }

        if ((flags & SDL.WindowFlags.Hidden) != 0) {
            ShowWindow();
        }

        ReapplyWindowedPlacement();
        RefreshSnapshot();
    }

    private void ApplyClipboardWrite(string text) {
        if (!SDL.SetClipboardText(text)) {
            _logger.LogWarning("SDL failed to write clipboard text: {Error}", SDL.GetError());
            return;
        }

        lock (_gate) {
            _clipboardText = text;
        }
    }

    private void ApplyClipboardRead() {
        string? text = SDL.GetClipboardText();
        if (text is null) {
            if (SDL.HasClipboardText()) {
                _logger.LogWarning("SDL failed to read clipboard text: {Error}", SDL.GetError());
                return;
            }

            text = string.Empty;
        }

        lock (_gate) {
            _clipboardText = text;
        }
    }

    private void ApplyStartupFullscreen() {
        uint displayId = SDL.GetDisplayForWindow(_handle);
        SDL.DisplayMode? mode = displayId != 0 ? SDL.GetCurrentDisplayMode(displayId) : null;
        bool exclusive = mode.HasValue;
        if (mode.HasValue) {
            if (!SDL.SetWindowFullscreenMode(_handle, mode.Value)) {
                _logger.LogWarning("SDL failed to set the startup fullscreen display mode: {Error}", SDL.GetError());
            }
        } else {
            _logger.LogWarning("No current display mode is available; starting borderless fullscreen instead.");
            if (!SDL.SetWindowFullscreenMode(_handle, IntPtr.Zero)) {
                _logger.LogWarning("SDL failed to set borderless fullscreen mode: {Error}", SDL.GetError());
            }
        }

        if (!SDL.SetWindowFullscreen(_handle, true)) {
            _logger.LogWarning("SDL failed to enter startup fullscreen: {Error}", SDL.GetError());
            return;
        }

        _fullscreenKind = exclusive ? FullscreenKind.Exclusive : FullscreenKind.Borderless;
    }

    private bool TryEnterBorderlessFullscreen() {
        if (!SDL.SetWindowFullscreenMode(_handle, IntPtr.Zero)) {
            _logger.LogWarning("SDL failed to set borderless fullscreen mode: {Error}", SDL.GetError());
            return false;
        }

        if (!SDL.SetWindowFullscreen(_handle, true)) {
            return false;
        }

        _fullscreenKind = FullscreenKind.Borderless;
        return true;
    }

    private bool TryEnterExclusiveFullscreen() {
        uint displayId = SDL.GetDisplayForWindow(_handle);
        SDL.DisplayMode? mode = displayId != 0 ? SDL.GetCurrentDisplayMode(displayId) : null;
        if (!mode.HasValue) {
            return false;
        }

        if (!SDL.SetWindowFullscreenMode(_handle, mode.Value)) {
            _logger.LogWarning("SDL failed to set exclusive fullscreen mode: {Error}", SDL.GetError());
            return false;
        }

        if (!SDL.SetWindowFullscreen(_handle, true)) {
            return false;
        }

        _fullscreenKind = FullscreenKind.Exclusive;
        return true;
    }

    private void ShowWindow() {
        if (!SDL.ShowWindow(_handle)) {
            _logger.LogWarning("SDL failed to show the window: {Error}", SDL.GetError());
        }
    }

    private void RememberWindowedPlacement() {
        if (SDL.GetWindowSize(_handle, out int width, out int height)) {
            _windowedWidth = width;
            _windowedHeight = height;
            _hasWindowedSize = true;
        }

        if (SDL.GetWindowPosition(_handle, out int x, out int y)) {
            _windowedX = x;
            _windowedY = y;
            _hasWindowedPosition = true;
        }
    }

    private void ReapplyWindowedPlacement() {
        if (_hasWindowedSize && !SDL.SetWindowSize(_handle, _windowedWidth, _windowedHeight)) {
            _logger.LogWarning("SDL failed to restore the windowed size: {Error}", SDL.GetError());
        }

        if (_hasWindowedPosition && !SDL.SetWindowPosition(_handle, _windowedX, _windowedY)) {
            _logger.LogWarning("SDL failed to restore the windowed position: {Error}", SDL.GetError());
        }
    }

    private enum FullscreenKind {
        None,
        Exclusive,
        Borderless,
    }
}
