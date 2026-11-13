using System.Numerics;

namespace RayBork.Internal;

/// <summary>
/// Owns the tick-thread camera recording state for one app: the frame packet currently recording and
/// the single active <see cref="Camera"/>.
/// </summary>
/// <remarks>
/// <para>
/// The tick runner begins a frame after it acquires a writable ring slot and ends it after
/// <see cref="IGame.Tick"/> returns. Cameras are sequential, not nested: creating one while another is
/// active is an <see cref="InvalidOperationException"/>, and a camera left undisposed when the tick
/// ends is an <see cref="InvalidOperationException"/> that names the leaked camera. The tick runner
/// abandons the frame in both cases.
/// </para>
/// <para>
/// Every member is tick-thread only. The service holds no GPU state; it only builds camera snapshots
/// and forwards them into the frame packet.
/// </para>
/// </remarks>
internal sealed class CameraService {
    private readonly IWindow _window;
    private RenderFramePacket? _packet;
    private Camera? _activeCamera;
    private PrimitiveAssets? _primitives;
    private int _nextCameraNumber;

    /// <summary>
    /// Initializes the service with the window used to resolve the default camera viewport.
    /// </summary>
    /// <param name="window">The app window; its render size is read at camera creation.</param>
    /// <exception cref="ArgumentNullException"><paramref name="window"/> is <c>null</c>.</exception>
    internal CameraService(IWindow window) {
        ArgumentNullException.ThrowIfNull(window);
        _window = window;
    }

    /// <summary>Gets whether a frame is currently accepting cameras and draws.</summary>
    internal bool IsRecording => _packet is not null;

    /// <summary>
    /// Gets the app's engine primitive meshes and materials, or <see langword="null"/> before the app
    /// attaches them and after it detaches them during shutdown.
    /// </summary>
    /// <remarks>
    /// The app attaches the assets before <see cref="IGame.Setup"/> runs and detaches them after
    /// <see cref="IGame.Shutdown"/> returned, so the primitive draw helpers may read them only while a
    /// frame records. The service does not own the assets and never disposes them.
    /// </remarks>
    internal PrimitiveAssets? Primitives => _primitives;

    /// <summary>
    /// Attaches the app-lifetime primitive assets for the later camera draw helpers.
    /// </summary>
    /// <param name="primitives">The app's primitive assets.</param>
    /// <exception cref="ArgumentNullException"><paramref name="primitives"/> is <c>null</c>.</exception>
    /// <remarks>Called by the app on the main thread before the tick thread starts.</remarks>
    internal void AttachPrimitives(PrimitiveAssets primitives) {
        ArgumentNullException.ThrowIfNull(primitives);
        _primitives = primitives;
    }

    /// <summary>
    /// Detaches the primitive assets before the app disposes them. Called on the main thread after the
    /// tick thread joined.
    /// </summary>
    internal void DetachPrimitives() => _primitives = null;

    /// <summary>
    /// Starts recording a frame into <paramref name="packet"/>.
    /// </summary>
    /// <param name="packet">The writable packet acquired from the frame ring.</param>
    /// <param name="time">Absolute seconds since the app started.</param>
    /// <param name="deltaTime">Clamped seconds since the previous tick.</param>
    /// <exception cref="ArgumentNullException"><paramref name="packet"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">A frame is already recording.</exception>
    internal void BeginFrame(RenderFramePacket packet, double time, double deltaTime) {
        ArgumentNullException.ThrowIfNull(packet);
        if (_packet is not null) {
            throw new InvalidOperationException("A frame is already being recorded.");
        }

        packet.BeginRecording(time, deltaTime);
        _packet = packet;
        _activeCamera = null;
        _nextCameraNumber = 1;
    }

    /// <summary>
    /// Ends the recording phase and seals the packet.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A camera created during the frame was not disposed before the tick returned. The exception names
    /// the leaked camera; the tick runner abandons the frame instead of publishing it.
    /// </exception>
    internal void EndFrame() {
        RenderFramePacket? packet = _packet;
        if (packet is null) {
            return;
        }

        Camera? leaked = _activeCamera;
        _activeCamera = null;
        _packet = null;
        if (leaked is not null) {
            throw new InvalidOperationException(
                $"Camera {leaked.Number} was created during this tick but was not disposed before Tick "
                    + "returned. Create a camera, record its draws, and dispose it before the callback "
                    + "returns; the frame was not published.");
        }

        packet.Seal();
    }

    /// <summary>
    /// Drops the recording state without sealing or publishing the packet. Used when the tick failed
    /// after the ring slot was acquired.
    /// </summary>
    internal void AbandonFrame() {
        _packet = null;
        _activeCamera = null;
    }

    /// <summary>
    /// Creates a perspective camera and appends its pass to the recording frame.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The caller is not the tick thread, no frame is recording, or another camera is active.
    /// </exception>
    internal Camera CreateCamera(
        Transform transform,
        float fieldOfView,
        ClearMode clearMode,
        Color? clearColor,
        float near,
        float far,
        Rect? viewport) {
        ValidateRecordingState(nameof(EngineContext.CreateCamera));
        CameraMath.ValidatePerspective(fieldOfView, near, far);
        return CreateCameraCore(
            transform,
            CameraProjection.Perspective,
            fieldOfView,
            orthographicHeight: 0f,
            near,
            far,
            clearMode,
            clearColor,
            viewport);
    }

    /// <summary>
    /// Creates an orthographic camera and appends its pass to the recording frame.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The caller is not the tick thread, no frame is recording, or another camera is active.
    /// </exception>
    internal Camera CreateOrthographicCamera(
        Transform transform,
        float height,
        ClearMode clearMode,
        Color? clearColor,
        float near,
        float far,
        Rect? viewport) {
        ValidateRecordingState(nameof(EngineContext.CreateOrthographicCamera));
        CameraMath.ValidateOrthographic(height, near, far);
        return CreateCameraCore(
            transform,
            CameraProjection.Orthographic,
            fieldOfView: 0f,
            height,
            near,
            far,
            clearMode,
            clearColor,
            viewport);
    }

    /// <summary>
    /// Clears the active camera slot when the camera is disposed. Called by <see cref="Camera.Dispose"/>.
    /// </summary>
    /// <param name="camera">The disposed camera.</param>
    /// <exception cref="ArgumentNullException"><paramref name="camera"/> is <c>null</c>.</exception>
    internal void Release(Camera camera) {
        ArgumentNullException.ThrowIfNull(camera);
        if (ReferenceEquals(_activeCamera, camera)) {
            _activeCamera = null;
        }
    }

    private Camera CreateCameraCore(
        Transform transform,
        CameraProjection projection,
        float fieldOfView,
        float orthographicHeight,
        float near,
        float far,
        ClearMode clearMode,
        Color? clearColor,
        Rect? viewport) {
        if ((clearMode & ~ClearMode.All) != 0) {
            throw new ArgumentOutOfRangeException(
                nameof(clearMode),
                clearMode,
                "The clear mode contains bits outside ClearMode.All.");
        }

        Rect clampedViewport = CameraMath.ResolveViewport(
            viewport,
            _window.RenderWidth,
            _window.RenderHeight,
            nameof(viewport));
        Color resolvedClearColor = CameraMath.ResolveClearColor(clearColor, nameof(clearColor));
        Matrix4x4 view = CameraMath.CreateViewMatrix(transform);
        float aspectRatio = clampedViewport.Width / clampedViewport.Height;
        Matrix4x4 projectionMatrix = projection == CameraProjection.Perspective
            ? CameraMath.CreatePerspectiveProjection(fieldOfView, aspectRatio, near, far)
            : CameraMath.CreateOrthographicProjection(orthographicHeight, aspectRatio, near, far);
        Matrix4x4 viewProjection = view * projectionMatrix;

        RenderFramePacket packet = _packet!;
        CameraPass pass = packet.BeginPass();
        pass.Begin(
            _nextCameraNumber - 1,
            view,
            projectionMatrix,
            viewProjection,
            transform.Position,
            (int)clampedViewport.X,
            (int)clampedViewport.Y,
            (int)clampedViewport.Width,
            (int)clampedViewport.Height,
            clearMode,
            resolvedClearColor);

        Camera camera = new(
            this,
            packet,
            pass,
            _nextCameraNumber,
            transform,
            projection,
            fieldOfView,
            orthographicHeight,
            near,
            far,
            clearMode,
            resolvedClearColor,
            clampedViewport,
            view,
            projectionMatrix,
            viewProjection);
        _nextCameraNumber++;
        _activeCamera = camera;
        return camera;
    }

    private void ValidateRecordingState(string operation) {
        TickThread.RequireCurrent(operation);
        if (_packet is null) {
            throw new InvalidOperationException(
                $"{operation} is legal only during IGame.Tick while the engine records a frame. "
                    + "Camera creation is not legal in Setup or Shutdown.");
        }

        if (_activeCamera is { } active) {
            throw new InvalidOperationException(
                $"Camera {active.Number} is still active. Cameras cannot be nested; dispose it before "
                    + "creating another camera.");
        }
    }
}
