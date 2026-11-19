namespace RayBork;

/// <summary>
/// Snapshot-based access to the application window.
/// </summary>
/// <remarks>
/// Getters return the last confirmed snapshot and are safe from any thread. Value setters are queued
/// to the main window thread and coalesced, so a getter may lag a setter by one event-pump cycle.
/// </remarks>
public interface IWindow
{
    /// <summary>Gets the last confirmed window title.</summary>
    string Title { get; }

    /// <summary>Gets the client width in logical units.</summary>
    int Width { get; }

    /// <summary>Gets the client height in logical units.</summary>
    int Height { get; }

    /// <summary>Gets the framebuffer width in physical pixels.</summary>
    int RenderWidth { get; }

    /// <summary>Gets the framebuffer height in physical pixels.</summary>
    int RenderHeight { get; }

    /// <summary>
    /// Gets the OS display scale of the window's monitor, such as <c>1.5</c> at 150%; not the
    /// pixel ratio (<see cref="RenderWidth"/> divided by <see cref="Width"/>).
    /// </summary>
    float DpiScale { get; }

    /// <summary>Gets the window rectangle in desktop coordinates.</summary>
    Rect Bounds { get; }

    /// <summary>Gets whether the user can resize the window.</summary>
    bool IsResizable { get; }

    /// <summary>Gets whether the window is fullscreen.</summary>
    bool IsFullscreen { get; }

    /// <summary>Gets whether the window is minimized.</summary>
    bool IsMinimized { get; }

    /// <summary>Gets whether the window has keyboard focus.</summary>
    bool IsFocused { get; }

    /// <summary>
    /// Gets whether the pointer is over the client area; false while minimized or hidden.
    /// </summary>
    bool IsCursorOnScreen { get; }

    /// <summary>Gets whether a resize occurred since the last tick; true for one tick.</summary>
    bool WasResized { get; }

    /// <summary>
    /// Gets whether the user requested a close; the engine stops after this tick, with no veto.
    /// </summary>
    bool CloseRequested { get; }

    /// <summary>Gets the current window state.</summary>
    WindowState State { get; }

    /// <summary>Gets a monitor snapshot; indices are stable for the process lifetime.</summary>
    IReadOnlyList<Monitor> Monitors { get; }

    /// <summary>Gets the monitor that currently contains the window.</summary>
    Monitor CurrentMonitor { get; }

    /// <summary>Queues a request to change the window title.</summary>
    void SetTitle(string title);

    /// <summary>Queues a resize in logical units; ignored while fullscreen.</summary>
    void SetSize(int width, int height);

    /// <summary>Queues a request to enter or leave fullscreen, preferring borderless.</summary>
    void SetFullscreen(bool fullscreen);

    /// <summary>Queues a border change; does not itself enter fullscreen.</summary>
    void SetBorderless(bool borderless);

    /// <summary>Queues a move to the given desktop coordinates.</summary>
    void SetPosition(int x, int y);

    /// <summary>Queues a move to an index from <see cref="Monitors"/>; indices may have gaps.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The index is absent from the current snapshot.
    /// </exception>
    void SetMonitor(int monitorIndex);

    /// <summary>Queues the minimum window size in logical units.</summary>
    void SetMinSize(int width, int height);

    /// <summary>
    /// Queues the maximum window size in logical units; zero means no limit on that axis.
    /// </summary>
    void SetMaxSize(int width, int height);

    /// <summary>Queues a request to minimize the window.</summary>
    void Minimize();

    /// <summary>Queues a request to maximize the window.</summary>
    void Maximize();

    /// <summary>Queues a request to restore the window to its normal state.</summary>
    void Restore();

    /// <summary>Queues a cursor mode change; use <see cref="CursorMode.Disabled"/> for mouse-look.</summary>
    void SetCursor(CursorMode mode);

    /// <summary>
    /// Queues the native text input area in logical window units; zero width or height clears it.
    /// </summary>
    /// <remarks>
    /// Area components are rounded to whole window units. <paramref name="cursorOffset"/> is a
    /// non-negative offset from the area's <c>X</c> coordinate. Committed text still arrives through
    /// <see cref="IInput.TextInput"/>.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// An area component is not finite or does not fit in 32 bits.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A dimension or the cursor offset is negative.
    /// </exception>
    void SetTextInputArea(Rect area, int cursorOffset);

    /// <summary>Queues a clipboard text replacement.</summary>
    void SetClipboardText(string text);

    /// <summary>Gets the last confirmed clipboard text, or an empty string when there is none.</summary>
    string GetClipboardText();

    /// <summary>Queues a close request; the engine stops after the current tick.</summary>
    void Close();
}
