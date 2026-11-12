# Window

[API overview](../api.md) · [document map](../api.md#document-map)

## 8. Window

```csharp
public interface IWindow {
    string Title { get; }
    int Width { get; }
    int Height { get; }
    int RenderWidth { get; }
    int RenderHeight { get; }
    float DpiScale { get; }
    Rect Bounds { get; }

    bool IsResizable { get; }
    bool IsFullscreen { get; }
    bool IsMinimized { get; }
    bool IsFocused { get; }
    bool WasResized { get; }
    bool CloseRequested { get; }
    WindowState State { get; }

    IReadOnlyList<Monitor> Monitors { get; }
    Monitor CurrentMonitor { get; }

    void SetTitle(string title);
    void SetSize(int width, int height);
    void SetFullscreen(bool fullscreen);
    void SetBorderless(bool borderless);
    void SetPosition(int x, int y);
    void SetMonitor(int monitorIndex);
    void SetMinSize(int width, int height);
    void SetMaxSize(int width, int height);
    void Minimize();
    void Maximize();
    void Restore();
    void SetCursor(CursorMode mode);
    void SetClipboardText(string text);
    string GetClipboardText();
    void Close();
}

public enum WindowState {
    Normal,
    Minimized,
    Maximized,
    Fullscreen,
    BorderlessFullscreen,
    Hidden,
}

public enum CursorMode {
    Normal,
    Hidden,
    Disabled,
}

public enum PresentMode {
    Mailbox,
    Fifo,
    Immediate,
}

public readonly record struct Monitor(
    int Index,
    string Name,
    Rect Bounds,
    Rect WorkArea,
    float DpiScale,
    int RefreshRate);
```

Semantics:

- `Width`/`Height` are **window coordinates**. On Windows they equal render pixels; on platforms
  that distinguish logical points they may differ from the physical framebuffer.
  `RenderWidth`/`RenderHeight` are **physical pixels**. `DpiScale` is the OS display/content scale,
  for example `1.5` at 150%, **not** the pixel ratio; the pixel ratio is `RenderWidth / Width`.
  Mouse positions are window coordinates; cameras and viewports are render pixels.
- `Bounds` is in desktop coordinates. It is a snapshot, not a live handle.
- `WasResized` is true for exactly the first tick after a resize; it is reset by the engine.
- `CloseRequested` is true after the window close request. The engine treats it as a graceful exit
  request and stops after the current tick. There is no close veto in v1; save in `Shutdown`.
- `SetFullscreen(true)` prefers borderless fullscreen for predictable WSL/desktop behavior. If the
  platform cannot provide it, the app falls back to windowed fullscreen and logs a warning.
- `PresentMode` defaults to `Mailbox`; if the surface does not support it, the engine falls back to
  `Fifo`. `Immediate` is only used when explicitly requested.
- `SetBorderless(true)` toggles a borderless window; it does not itself enter fullscreen.
- `SetSize` is ignored while fullscreen; it is remembered for the next windowed restore.
- `SetMonitor` moves a windowed window to the monitor at the given stable index. An out-of-range
  index throws `ArgumentOutOfRangeException`; the call is queued.
- `SetMinSize`/`SetMaxSize` use window units. A zero maximum means "no maximum".
- `SetCursor(CursorMode.Disabled)` locks and hides the cursor and enables relative mouse motion;
  use `Input.MouseDelta` for look. `Hidden` hides the cursor without locking.
- Window setters are **Any**-thread safe. They are queued to the main thread and coalesced; a
  getter returns the last confirmed value, which may lag the setter by one event-pump cycle.
  `SetClipboardText` follows the same queued, coalesced setter path.
- `GetClipboardText` returns the last cached clipboard snapshot immediately and queues a
  main-thread read that a later event-pump cycle refreshes.
- `Monitors` and `CurrentMonitor` are snapshots. Monitor indices are stable for the process
  lifetime; a monitor hotplug may change the list between ticks.
- `RayBork.Monitor` collides with `System.Threading.Monitor` when `System.Threading` is imported.
  Qualify `RayBork.Monitor` or use `var` for monitor locals; the public type name is unchanged.
- When `IsMinimized` or `IsFocused` is false, the tick loop is capped at 30 FPS as described in
  §4.1. The renderer may idle while the swapchain has no extent.
- v1 has one window. Multiple windows are post-1.0.
- Window icon, opacity, clipboard image, drag-and-drop, and accessibility are post-1.0. IME input
  is delivered through `TextInput` in M9.
