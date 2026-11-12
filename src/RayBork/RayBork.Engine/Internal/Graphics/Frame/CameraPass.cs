using System.Numerics;

namespace RayBork.Internal;

/// <summary>
/// One recorded camera pass inside a <see cref="RenderFramePacket"/>.
/// </summary>
/// <remarks>
/// <para>
/// A pass is a value snapshot taken while the tick thread records it: the camera matrices, the camera
/// position, the clamped pixel viewport, the clear mode and color, and the ordered draw records. The
/// render thread never reads a live <see cref="Camera"/>, transform, or shader cursor.
/// </para>
/// <para>
/// The recording members are tick-thread only and legal until <see cref="Seal"/> runs, which happens
/// when the camera is disposed and again when the packet is sealed. A sealed pass rejects new draws
/// with <see cref="InvalidOperationException"/>. The pooled draw-record buffer is owned by the frame
/// slot, so it is reused after the render thread releases the slot and never mutates while the packet
/// is ready or rendering.
/// </para>
/// </remarks>
internal sealed class CameraPass {
    private RenderDrawRecord[] _draws = new RenderDrawRecord[4];
    private int _drawCount;
    private bool _sealed;

    /// <summary>Gets the zero-based creation order of the pass inside its frame.</summary>
    internal int Index { get; private set; }

    /// <summary>Gets the world-to-view matrix captured at camera creation.</summary>
    internal Matrix4x4 View { get; private set; }

    /// <summary>Gets the view-to-clip matrix, including the Vulkan Y-flip.</summary>
    internal Matrix4x4 Projection { get; private set; }

    /// <summary>Gets the combined <c>View * Projection</c> matrix.</summary>
    internal Matrix4x4 ViewProjection { get; private set; }

    /// <summary>Gets the world-space camera position.</summary>
    internal Vector3 CameraPosition { get; private set; }

    /// <summary>Gets the clamped viewport left edge in target pixels.</summary>
    internal int ViewportX { get; private set; }

    /// <summary>Gets the clamped viewport top edge in target pixels.</summary>
    internal int ViewportY { get; private set; }

    /// <summary>Gets the clamped viewport width in pixels.</summary>
    internal int ViewportWidth { get; private set; }

    /// <summary>Gets the clamped viewport height in pixels.</summary>
    internal int ViewportHeight { get; private set; }

    /// <summary>Gets the buffers this pass clears before it draws.</summary>
    internal ClearMode ClearMode { get; private set; }

    /// <summary>Gets the linear color written when color is cleared.</summary>
    internal Color ClearColor { get; private set; }

    /// <summary>Gets the sealed draw records in recording order.</summary>
    internal ReadOnlySpan<RenderDrawRecord> Draws => _draws.AsSpan(0, _drawCount);

    /// <summary>Gets the number of draw records recorded into the pass.</summary>
    internal int DrawCount => _drawCount;

    /// <summary>Gets whether the pass rejects new draws.</summary>
    internal bool IsSealed => _sealed;

    /// <summary>
    /// Initializes the pass snapshot. Called by the frame packet before the camera starts recording.
    /// </summary>
    internal void Begin(
        int index,
        Matrix4x4 view,
        Matrix4x4 projection,
        Matrix4x4 viewProjection,
        Vector3 cameraPosition,
        int viewportX,
        int viewportY,
        int viewportWidth,
        int viewportHeight,
        ClearMode clearMode,
        Color clearColor) {
        Index = index;
        View = view;
        Projection = projection;
        ViewProjection = viewProjection;
        CameraPosition = cameraPosition;
        ViewportX = viewportX;
        ViewportY = viewportY;
        ViewportWidth = viewportWidth;
        ViewportHeight = viewportHeight;
        ClearMode = clearMode;
        ClearColor = clearColor;
        _drawCount = 0;
        _sealed = false;
    }

    /// <summary>
    /// Appends a draw record in recording order and returns it.
    /// </summary>
    /// <param name="record">The pooled record rented from the frame packet.</param>
    /// <exception cref="ArgumentNullException"><paramref name="record"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">The pass is sealed.</exception>
    internal void Add(RenderDrawRecord record) {
        ArgumentNullException.ThrowIfNull(record);
        if (_sealed) {
            throw new InvalidOperationException(
                "The camera pass is already sealed; a draw cannot be recorded into a sealed pass.");
        }

        if (_drawCount == _draws.Length) {
            Array.Resize(ref _draws, checked(_draws.Length * 2));
        }

        _draws[_drawCount++] = record;
    }

    /// <summary>
    /// Stops recording. Idempotent.
    /// </summary>
    internal void Seal() => _sealed = true;

    /// <summary>
    /// Clears the pass for reuse by the next frame that owns the slot. Called only while the slot is
    /// free, so no render thread can observe the partly reset list.
    /// </summary>
    internal void Reset() {
        Array.Clear(_draws, 0, _drawCount);
        _drawCount = 0;
        _sealed = false;
        Index = 0;
        View = default;
        Projection = default;
        ViewProjection = default;
        CameraPosition = default;
        ViewportX = 0;
        ViewportY = 0;
        ViewportWidth = 0;
        ViewportHeight = 0;
        ClearMode = ClearMode.None;
        ClearColor = default;
    }
}
