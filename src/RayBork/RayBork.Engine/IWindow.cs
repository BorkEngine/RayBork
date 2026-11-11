namespace RayBork;

/// <summary>
/// Snapshot-based access to the application window.
/// </summary>
/// <remarks>
/// Getters return the last confirmed snapshot and are safe to call from any thread. Setters are queued
/// to the main window thread and coalesced; a getter may lag a setter by one event-pump cycle.
/// </remarks>
public interface IWindow {
    /// <summary>
    /// Gets the last confirmed window title.
    /// </summary>
    string Title { get; }

    /// <summary>
    /// Gets the client width of the window in logical units.
    /// </summary>
    int Width { get; }

    /// <summary>
    /// Gets the client height of the window in logical units.
    /// </summary>
    int Height { get; }

    /// <summary>
    /// Gets the framebuffer width in physical pixels.
    /// </summary>
    int RenderWidth { get; }

    /// <summary>
    /// Gets the framebuffer height in physical pixels.
    /// </summary>
    int RenderHeight { get; }

    /// <summary>
    /// Gets the OS display/content scale of the monitor that contains the window, such as <c>1.5</c>
    /// at 150%. This is the SDL display scale, not the pixel ratio. The pixel ratio is
    /// <see cref="RenderWidth"/> divided by <see cref="Width"/>.
    /// </summary>
    float DpiScale { get; }

    /// <summary>
    /// Gets the window rectangle in desktop coordinates.
    /// </summary>
    Rect Bounds { get; }

    /// <summary>
    /// Gets whether the window can be resized by the user.
    /// </summary>
    bool IsResizable { get; }

    /// <summary>
    /// Gets whether the window is fullscreen.
    /// </summary>
    bool IsFullscreen { get; }

    /// <summary>
    /// Gets whether the window is minimized.
    /// </summary>
    bool IsMinimized { get; }

    /// <summary>
    /// Gets whether the window has keyboard focus.
    /// </summary>
    bool IsFocused { get; }

    /// <summary>
    /// Gets whether the window was resized since the previous tick. The value is <see langword="true"/>
    /// for exactly the first tick after a resize; the engine resets it.
    /// </summary>
    bool WasResized { get; }

    /// <summary>
    /// Gets whether the user requested a close. The engine treats it as a graceful exit request and
    /// stops after the current tick. There is no close veto; save in <see cref="IGame.Shutdown"/>.
    /// </summary>
    bool CloseRequested { get; }

    /// <summary>
    /// Gets the current window state.
    /// </summary>
    WindowState State { get; }

    /// <summary>
    /// Gets a snapshot of the connected monitors. Monitor indices are stable for the process
    /// lifetime; a hotplug may change the list between ticks.
    /// </summary>
    IReadOnlyList<Monitor> Monitors { get; }

    /// <summary>
    /// Gets the monitor that currently contains the window.
    /// </summary>
    Monitor CurrentMonitor { get; }

    /// <summary>
    /// Queues a request to change the window title.
    /// </summary>
    /// <param name="title">The new title.</param>
    void SetTitle(string title);

    /// <summary>
    /// Queues a request to resize the window. The request is ignored while the window is fullscreen.
    /// </summary>
    /// <param name="width">The new client width in logical units.</param>
    /// <param name="height">The new client height in logical units.</param>
    void SetSize(int width, int height);

    /// <summary>
    /// Queues a request to enter or leave fullscreen. Entering prefers borderless fullscreen.
    /// </summary>
    /// <param name="fullscreen">Whether the window should be fullscreen.</param>
    void SetFullscreen(bool fullscreen);

    /// <summary>
    /// Queues a request to toggle the window border. This does not itself enter fullscreen.
    /// </summary>
    /// <param name="borderless">Whether the window should be borderless.</param>
    void SetBorderless(bool borderless);

    /// <summary>
    /// Queues a request to move the window.
    /// </summary>
    /// <param name="x">The new left edge in desktop coordinates.</param>
    /// <param name="y">The new top edge in desktop coordinates.</param>
    void SetPosition(int x, int y);

    /// <summary>
    /// Queues a request to move the window to another monitor.
    /// </summary>
    /// <param name="monitorIndex">The stable monitor index from <see cref="Monitors"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">The index is out of range.</exception>
    void SetMonitor(int monitorIndex);

    /// <summary>
    /// Queues a request to set the minimum window size, in logical units.
    /// </summary>
    /// <param name="width">The minimum width.</param>
    /// <param name="height">The minimum height.</param>
    void SetMinSize(int width, int height);

    /// <summary>
    /// Queues a request to set the maximum window size, in logical units. A zero value means no
    /// maximum on that axis.
    /// </summary>
    /// <param name="width">The maximum width, or zero for no maximum.</param>
    /// <param name="height">The maximum height, or zero for no maximum.</param>
    void SetMaxSize(int width, int height);

    /// <summary>
    /// Queues a request to minimize the window.
    /// </summary>
    void Minimize();

    /// <summary>
    /// Queues a request to maximize the window.
    /// </summary>
    void Maximize();

    /// <summary>
    /// Queues a request to restore the window to its normal state.
    /// </summary>
    void Restore();

    /// <summary>
    /// Queues a request to change the cursor mode. Use <see cref="CursorMode.Disabled"/> for
    /// mouse-look controls.
    /// </summary>
    /// <param name="mode">The new cursor mode.</param>
    void SetCursor(CursorMode mode);

    /// <summary>
    /// Queues a request to replace the clipboard text.
    /// </summary>
    /// <param name="text">The new clipboard text.</param>
    void SetClipboardText(string text);

    /// <summary>
    /// Gets the last confirmed clipboard text.
    /// </summary>
    /// <returns>The clipboard text, or an empty string when the clipboard contains no text.</returns>
    string GetClipboardText();

    /// <summary>
    /// Queues a request to close the window. The engine treats it as a graceful exit request.
    /// </summary>
    void Close();
}
