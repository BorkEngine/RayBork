namespace RayBork.Internal;

/// <summary>
/// A render-thread snapshot of the presentation surface: the physical size, whether the window is
/// minimized or focused, and the cached HDR state.
/// </summary>
/// <remarks>
/// The app builds this value from the window and platform snapshots; the renderer never reads the
/// window itself. Sizes are physical render pixels, not logical window units, and are zero when the
/// window has no drawable extent. A minimized or zero-extent surface must not acquire or present.
/// </remarks>
/// <param name="RenderWidth">The physical render width in pixels.</param>
/// <param name="RenderHeight">The physical render height in pixels.</param>
/// <param name="IsMinimized">Whether the window is minimized.</param>
/// <param name="IsFocused">Whether the window has input focus.</param>
/// <param name="Hdr">The cached SDL3 window HDR state.</param>
internal readonly record struct WindowSurfaceState(
    int RenderWidth,
    int RenderHeight,
    bool IsMinimized,
    bool IsFocused,
    WindowHdrState Hdr);
