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
/// Every member is tick-thread only. The service holds no GPU state; it only builds camera and
/// tone-mapping snapshots and forwards them into the frame packet.
/// </para>
/// </remarks>
internal sealed class CameraService
{
    private const int MINIMUM_TEXT_GLYPH_CAPACITY = 64;

    private readonly IWindow _window;
    private readonly TextLayoutBuffer _textLayout = new();
    private readonly HashSet<Font> _textFontsStarted = [];
    private readonly List<PostProcessEffectSnapshot> _postProcessScratch = [];
    private RenderFramePacket? _packet;
    private Camera? _activeCamera;
    private PrimitiveAssets? _primitives;
    private SsaoAssets? _ssaoAssets;
    private BloomAssets? _bloomAssets;
    private TonemappingSettings? _tonemapping;
    private Matrix4x4[] _textGlyphMatrices = [];
    private Texture?[] _textGlyphTextures = [];
    private int _textGlyphCount;
    private int _nextCameraNumber;

    /// <summary>
    /// Initializes the service with the window used to resolve the default camera viewport.
    /// </summary>
    /// <param name="window">The app window; its render size is read at camera creation.</param>
    /// <exception cref="ArgumentNullException"><paramref name="window"/> is <c>null</c>.</exception>
    internal CameraService(IWindow window)
    {
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
    internal void AttachPrimitives(PrimitiveAssets primitives)
    {
        ArgumentNullException.ThrowIfNull(primitives);
        _primitives = primitives;
    }

    /// <summary>
    /// Detaches the primitive assets before the app disposes them. Called on the main thread after the
    /// tick thread joined.
    /// </summary>
    internal void DetachPrimitives() => _primitives = null;

    /// <summary>
    /// Attaches the app-lifetime preloaded SSAO assets used by <see cref="SsaoEffect"/> captures.
    /// </summary>
    /// <param name="assets">The loaded engine-owned SSAO assets.</param>
    /// <exception cref="ArgumentNullException"><paramref name="assets"/> is <c>null</c>.</exception>
    /// <remarks>Called by the app on the main thread before the tick thread starts.</remarks>
    internal void AttachSsaoAssets(SsaoAssets assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        _ssaoAssets = assets;
    }

    /// <summary>
    /// Detaches the SSAO assets before the app disposes them. Called on the main thread after the tick
    /// thread joined.
    /// </summary>
    internal void DetachSsaoAssets() => _ssaoAssets = null;

    /// <summary>
    /// Attaches the app-lifetime preloaded bloom assets used by <see cref="BloomEffect"/> captures.
    /// </summary>
    /// <param name="assets">The loaded engine-owned bloom assets.</param>
    /// <exception cref="ArgumentNullException"><paramref name="assets"/> is <c>null</c>.</exception>
    /// <remarks>Called by the app on the main thread before the tick thread starts.</remarks>
    internal void AttachBloomAssets(BloomAssets assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        _bloomAssets = assets;
    }

    /// <summary>
    /// Detaches the bloom assets before the app disposes them. Called on the main thread after the tick
    /// thread joined.
    /// </summary>
    internal void DetachBloomAssets() => _bloomAssets = null;

    /// <summary>
    /// Attaches the app-lifetime runtime tone-mapping settings whose values every sealed frame
    /// snapshots.
    /// </summary>
    /// <param name="tonemapping">The app's tone-mapping settings.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tonemapping"/> is <c>null</c>.</exception>
    /// <remarks>
    /// Called by the app on the main thread before the tick thread starts. The service never mutates
    /// the settings; it reads <see cref="TonemappingSettings.Snapshot"/> when
    /// <see cref="EndFrame"/> seals a frame. Without an attach, a frame keeps
    /// <see cref="TonemappingSnapshot.Default"/>, which is the path used by tests and GPU harnesses
    /// that construct packets directly.
    /// </remarks>
    internal void AttachTonemapping(TonemappingSettings tonemapping)
    {
        ArgumentNullException.ThrowIfNull(tonemapping);
        _tonemapping = tonemapping;
    }

    /// <summary>
    /// Gets the layout buffer the camera text draws reuse.
    /// </summary>
    /// <remarks>
    /// The service owns one buffer for the app lifetime, so a warmed buffer lays text out without
    /// allocating. Cameras are sequential, so one buffer is sufficient; the glyph span it exposes is
    /// valid only until the next arrange call, and a text draw consumes it before returning.
    /// </remarks>
    internal TextLayoutBuffer TextLayout => _textLayout;

    /// <summary>
    /// Reserves the reusable per-call glyph scratch and returns its writable matrix span.
    /// </summary>
    /// <param name="count">The number of laid-out glyph records the call may record.</param>
    /// <returns>Exactly <paramref name="count"/> matrix slots for this call to fill.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    /// <remarks>
    /// A call fills one slot per ready glyph, so the reserved span can be longer than the recorded
    /// instance count. The matching texture span is <see cref="TextGlyphTextures"/>; call
    /// <see cref="ClearTextGlyphs"/> once the draw record copied both spans.
    /// </remarks>
    internal Span<Matrix4x4> ReserveTextGlyphs(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (_textGlyphMatrices.Length < count)
        {
            int capacity = Math.Max(
                count,
                Math.Max(_textGlyphMatrices.Length * 2, MINIMUM_TEXT_GLYPH_CAPACITY)
            );
            _textGlyphMatrices = new Matrix4x4[capacity];
            _textGlyphTextures = new Texture?[capacity];
        }

        _textGlyphCount = count;
        return _textGlyphMatrices.AsSpan(0, count);
    }

    /// <summary>
    /// Gets the writable per-glyph texture span reserved by <see cref="ReserveTextGlyphs"/>.
    /// </summary>
    internal Span<Texture?> TextGlyphTextures => _textGlyphTextures.AsSpan(0, _textGlyphCount);

    /// <summary>
    /// Clears the per-glyph texture references the last text draw wrote.
    /// </summary>
    /// <remarks>
    /// The matrices hold no references, so only the textures are cleared: the app-lifetime service
    /// must not keep a glyph texture reachable between frames. The frame's retention set, not this
    /// scratch, owns the resource hold that keeps the texture alive while the frame renders.
    /// </remarks>
    internal void ClearTextGlyphs()
    {
        if (_textGlyphCount == 0)
        {
            return;
        }

        Array.Clear(_textGlyphTextures, 0, _textGlyphCount);
        _textGlyphCount = 0;
    }

    /// <summary>
    /// Marks <paramref name="font"/> as begun for the camera that is recording and starts its glyph
    /// cache clock once.
    /// </summary>
    /// <param name="font">The font recording text is about to acquire glyphs from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="font"/> is <c>null</c>.</exception>
    /// <exception cref="ObjectDisposedException"><paramref name="font"/> is disposed.</exception>
    /// <exception cref="InvalidOperationException">No frame is recording.</exception>
    /// <remarks>
    /// The camera draw path calls this for every text draw it records, and the per-camera set makes the
    /// actual <see cref="Font.BeginFrame"/> call happen once per camera and font. The pipeline keeps the
    /// highest frame index, so a later camera in the same tick may pass the same index again without
    /// moving the clock backwards.
    /// </remarks>
    internal void BeginTextFont(Font font)
    {
        ArgumentNullException.ThrowIfNull(font);
        RenderFramePacket packet = _packet
            ?? throw new InvalidOperationException(
                "A font's frame can begin only while a frame is recording."
            );
        if (_textFontsStarted.Add(font))
        {
            font.BeginFrame(packet.FrameIndex);
        }
    }

    /// <summary>
    /// Starts recording a frame into <paramref name="packet"/>.
    /// </summary>
    /// <param name="packet">The writable packet acquired from the frame ring.</param>
    /// <param name="time">Absolute seconds since the app started.</param>
    /// <param name="deltaTime">Clamped seconds since the previous tick.</param>
    /// <exception cref="ArgumentNullException"><paramref name="packet"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">A frame is already recording.</exception>
    internal void BeginFrame(RenderFramePacket packet, double time, double deltaTime)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (_packet is not null)
        {
            throw new InvalidOperationException("A frame is already being recorded.");
        }

        packet.BeginRecording(time, deltaTime);
        _packet = packet;
        _activeCamera = null;
        _nextCameraNumber = 1;
    }

    /// <summary>
    /// Ends the recording phase and seals the packet. The frame snapshot of the attached runtime
    /// tone-mapping settings is copied into the packet before it seals.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A camera created during the frame was not disposed before the tick returned. The exception names
    /// the leaked camera; the tick runner abandons the frame instead of publishing it.
    /// </exception>
    internal void EndFrame()
    {
        RenderFramePacket? packet = _packet;
        if (packet is null)
        {
            return;
        }

        Camera? leaked = _activeCamera;
        _activeCamera = null;
        _packet = null;
        if (leaked is not null)
        {
            throw new InvalidOperationException(
                $"Camera {leaked.Number} was created during this tick but was not disposed before Tick "
                    + "returned. Create a camera, record its draws, and dispose it before the callback "
                    + "returns; the frame was not published."
            );
        }

        // The frame-wide tone mapping is sealed with the frame: writes made anywhere in Tick are in
        // effect here, so they apply to this frame's present pass.
        packet.Tonemapping = _tonemapping?.Snapshot() ?? TonemappingSnapshot.Default;
        packet.Seal();
    }

    /// <summary>
    /// Drops the recording state without sealing or publishing the packet. Used when the tick failed
    /// after the ring slot was acquired.
    /// </summary>
    internal void AbandonFrame()
    {
        _packet = null;
        _activeCamera = null;
    }

    /// <summary>
    /// Creates a camera for the requested projection and appends its pass to the recording frame.
    /// Exactly the size argument selected by <paramref name="projection"/> is used.
    /// </summary>
    /// <param name="transform">The world-space camera transform.</param>
    /// <param name="projection">The projection kind.</param>
    /// <param name="fieldOfView">The perspective vertical field of view in degrees.</param>
    /// <param name="orthographicHeight">The orthographic visible world-space height.</param>
    /// <param name="clearMode">The buffers the pass clears.</param>
    /// <param name="clearColor">The linear clear color, or <see langword="null"/> for opaque black.</param>
    /// <param name="near">The near clip distance.</param>
    /// <param name="far">The far clip distance.</param>
    /// <param name="viewport">The requested pixel viewport, or <see langword="null"/> for the full target.</param>
    /// <param name="target">
    /// The render texture the pass writes, or <see langword="null"/> for the app's linear HDR scene
    /// target. The viewport defaults to the target's full size and is clamped to it; the aspect ratio
    /// uses the clamped viewport.
    /// </param>
    /// <param name="postProcess">
    /// The caller-owned post-process stack to snapshot for this camera, or <see langword="null"/> for no
    /// post-processing. The stack, every entry's enabled value, and each custom effect's shader cursor
    /// and texture bindings are frozen here; later changes apply to later cameras only.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The size argument that does not belong to <paramref name="projection"/> is nonzero, the resolved
    /// viewport is invalid or empty, or the clear color has a non-finite component.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="projection"/> is not defined, the clear mode has unknown bits, or a selected
    /// projection or clip-plane parameter is outside its range.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// The camera's <paramref name="target"/> is disposed, or a post-process effect's shader or a texture
    /// its material binds is disposed.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The caller is not the tick thread, no frame is recording, another camera is active, the camera
    /// transform is singular, or a post-process effect violates the custom shader contract.
    /// </exception>
    internal Camera CreateCamera(
        Transform transform,
        CameraProjection projection,
        float fieldOfView,
        float orthographicHeight,
        ClearMode clearMode,
        Color? clearColor,
        float near,
        float far,
        Rect? viewport,
        RenderTexture? target = null,
        PostProcessStack? postProcess = null
    )
    {
        ValidateRecordingState(nameof(EngineContext.CreateCamera));
        if (target is not null)
        {
            ObjectDisposedException.ThrowIf(target.IsDisposed, target);
        }

        CameraMath.ValidateProjection(projection, fieldOfView, orthographicHeight, near, far);
        if ((clearMode & ~ClearMode.All) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(clearMode),
                clearMode,
                "The clear mode contains bits outside ClearMode.All."
            );
        }

        int targetWidth = target?.Width ?? _window.RenderWidth;
        int targetHeight = target?.Height ?? _window.RenderHeight;
        Rect clampedViewport = CameraMath.ResolveViewport(
            viewport,
            targetWidth,
            targetHeight,
            nameof(viewport)
        );
        Color resolvedClearColor = CameraMath.ResolveClearColor(clearColor, nameof(clearColor));
        Matrix4x4 view = CameraMath.CreateViewMatrix(transform);
        float aspectRatio = clampedViewport.Width / clampedViewport.Height;
        Matrix4x4 projectionMatrix =
            projection == CameraProjection.Perspective
                ? CameraMath.CreatePerspectiveProjection(fieldOfView, aspectRatio, near, far)
                : CameraMath.CreateOrthographicProjection(
                    orthographicHeight,
                    aspectRatio,
                    near,
                    far
                );
        Matrix4x4 viewProjection = view * projectionMatrix;

        RenderFramePacket packet = _packet!;
        CapturePostProcessEffects(postProcess, target);
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
            resolvedClearColor,
            target?.TargetId ?? 0
        );

        try
        {
            foreach (PostProcessEffectSnapshot effect in _postProcessScratch)
            {
                pass.AddPostProcessEffect(effect);
                effect.Retain(packet.Retention);
            }
        }
        finally
        {
            _postProcessScratch.Clear();
        }

        if (target is not null)
        {
            // The recorded frame keeps the target alive until the render thread released its slot, so a
            // RenderTexture disposed before then is not destroyed under the frame.
            packet.Retention.Add(target.Lifetime);
        }

        // Font frame clocks are begun once per camera and font, so a new camera starts a fresh set.
        _textFontsStarted.Clear();
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
            viewProjection,
            target
        );
        _nextCameraNumber++;
        _activeCamera = camera;
        return camera;
    }

    /// <summary>
    /// Clears the active camera slot when the camera is disposed. Called by <see cref="Camera.Dispose"/>.
    /// </summary>
    /// <param name="camera">The disposed camera.</param>
    /// <exception cref="ArgumentNullException"><paramref name="camera"/> is <c>null</c>.</exception>
    internal void Release(Camera camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        if (ReferenceEquals(_activeCamera, camera))
        {
            _activeCamera = null;
        }
    }

    private void ValidateRecordingState(string operation)
    {
        TickThread.RequireCurrent(operation);
        if (_packet is null)
        {
            throw new InvalidOperationException(
                $"{operation} is legal only during IGame.Tick while the engine records a frame. "
                    + "Camera creation is not legal in Setup or Shutdown."
            );
        }

        if (_activeCamera is { } active)
        {
            throw new InvalidOperationException(
                $"Camera {active.Number} is still active. Cameras cannot be nested; dispose it before "
                    + "creating another camera."
            );
        }
    }

    /// <summary>
    /// Captures the stack entries into the scratch snapshots the pending pass records.
    /// </summary>
    /// <remarks>
    /// The capture runs before the pass is appended, so a rejected effect leaves the frame packet
    /// unchanged. The scratch list is reused for the app lifetime, so a camera with a stack allocates
    /// only the snapshots themselves.
    /// </remarks>
    /// <param name="postProcess">The stack to snapshot, or <see langword="null"/> for none.</param>
    /// <param name="target">The camera's target, or <see langword="null"/> for the scene target.</param>
    private void CapturePostProcessEffects(PostProcessStack? postProcess, RenderTexture? target)
    {
        _postProcessScratch.Clear();
        if (postProcess is null || !postProcess.Enabled)
        {
            return;
        }

        bool targetHasDepth = target is null || target.DepthTexture is not null;
        string targetDescription = target is null
            ? "the app scene target"
            : $"render texture {target.TargetId}";
        try
        {
            foreach (PostProcessEffect effect in postProcess.Effects)
            {
                _postProcessScratch.Add(
                    PostProcessSnapshots.Capture(
                        effect,
                        targetHasDepth,
                        targetDescription,
                        _ssaoAssets,
                        _bloomAssets
                    )
                );
            }
        }
        catch
        {
            // A rejected effect leaves no snapshot behind; the already captured ones release the program
            // and texture references they would otherwise keep until the next camera.
            foreach (PostProcessEffectSnapshot snapshot in _postProcessScratch)
            {
                snapshot.Reset();
            }

            _postProcessScratch.Clear();
            throw;
        }
    }
}
