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
    bool IsCursorOnScreen { get; }
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
    void SetTextInputArea(Rect area, int cursorOffset);
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
  `RenderWidth`/`RenderHeight` are **physical pixels**. The window is created with
  `SDL_WINDOW_HIGH_PIXEL_DENSITY`, so a high-density display gets the full physical backbuffer.
  `DpiScale` is the OS display/content scale, for example `1.5` at 150%, **not** the pixel ratio;
  the pixel ratio is `RenderWidth / Width`. Mouse positions and deltas are render pixels, the same
  units as cameras and viewports, so `Input.MousePosition` goes directly to
  `Camera.ScreenToWorldRay`; `Width`, `Height`, and `SetSize` stay window coordinates, and `Bounds`
  stays in desktop coordinates.
- `Bounds` is in desktop coordinates. It is a snapshot, not a live handle.
- `WasResized` is true for exactly the first tick after a resize; it is reset by the engine.
- `CloseRequested` is true after the window close request. The engine treats it as a graceful exit
  request and stops after the current tick. There is no close veto in v1; save in `Shutdown`.
- `SetFullscreen(true)` prefers borderless fullscreen for predictable WSL/desktop behavior. If the
  platform cannot provide it, the app falls back to windowed fullscreen and logs a warning. An
  asynchronous borderless or exclusive entry keeps its kind until the compositor confirms it, and
  size and monitor commands are remembered for the next windowed restore while an entry is pending;
  leaving fullscreen cancels an entry that was abandoned instead of waiting for a confirmation.
- `PresentMode` defaults to `Mailbox`; if the surface does not support it, the engine falls back to
  `Fifo`. `Immediate` is only used when explicitly requested.
- `SetBorderless(true)` toggles a borderless window; it does not itself enter fullscreen.
- `SetSize` is ignored while fullscreen; it is remembered for the next windowed restore.
- `SetMonitor` moves a windowed window to the monitor at the given stable index. Stable indices may
  have gaps after a hotplug, so the index is validated against the entries in `Monitors`, not the
  snapshot count. An index that is not present in the current snapshot throws
  `ArgumentOutOfRangeException`; the call is queued.
- `SetMinSize`/`SetMaxSize` use window units. A zero maximum means "no maximum".
- `SetCursor(CursorMode.Disabled)` locks and hides the cursor and enables relative mouse motion;
  use `Input.MouseDelta` for look. `Hidden` hides the cursor without locking.
- `IsCursorOnScreen` is an **Any**-thread snapshot: it is true while the pointer is over the
  window's client area. `CursorMode` does not change it, it is false while the window is minimized
  or hidden, and a focus change refreshes it from the platform instead of assuming a value.
- `SetTextInputArea` is an **Any**-thread queued, coalesced setter that places the native IME
  candidate window over `area`. The area is in logical window units, the same units as `Width` and
  `Height`, not render pixels, and the non-negative `cursorOffset` is relative to `area.X` in the
  same units. A zero width or height clears the platform area. It adds no composition text:
  committed text still arrives through `IInput.TextInput`.
- Window setters are **Any**-thread safe. They are queued to the main thread and coalesced; a
  getter returns the last confirmed value, which may lag the setter by one event-pump cycle.
  `SetClipboardText` is queued on the same main-thread window-command path but is an ordered
  action, not a coalesced setter: each call queues a separate write, reads and writes apply in
  order, and `GetClipboardText` returns the last applied text.
- `GetClipboardText` returns the last cached clipboard snapshot immediately and queues a
  main-thread read that a later event-pump cycle refreshes.
- `Monitors` and `CurrentMonitor` are snapshots. Monitor indices are stable for the process
  lifetime; a monitor hotplug may change the list between ticks.
- `RayBork.Monitor` collides with `System.Threading.Monitor` when `System.Threading` is imported.
  Qualify `RayBork.Monitor` or use `var` for monitor locals; the public type name is unchanged.
- When `IsMinimized` or `IsFocused` is false, the tick loop is capped at 30 FPS as described in
  §4.1. The renderer may idle while the swapchain has no extent.
- v1 has one window. Multiple windows are post-1.0.
- Window icon, opacity, clipboard image, drag-and-drop, and accessibility are post-1.0. IME is
  committed-only in M9: typed and IME-committed text arrives through `IInput.TextInput`, composition
  preview and candidate lists are post-1.0, and `SetTextInputArea` only places the native candidate
  window.
