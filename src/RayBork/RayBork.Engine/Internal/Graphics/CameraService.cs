using System.Numerics;

namespace RayBork.Internal;

/// <summary>Owns tick-thread frame recording state for one app.</summary>
/// <remarks>
/// Cameras are sequential, not nested; nesting one or leaving one undisposed when the tick ends
/// throws <see cref="InvalidOperationException"/>, and the frame is abandoned. Every member is
/// tick-thread only; the service holds no GPU state.
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

    /// <summary>Initializes the service with the window that supplies the default camera viewport.</summary>
    internal CameraService(IWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _window = window;
    }

    /// <summary>Gets whether a frame is currently accepting cameras and draws.</summary>
    internal bool IsRecording => _packet is not null;

    /// <summary>Gets the app's primitive meshes and materials, or <see langword="null"/> while detached.</summary>
    /// <remarks>
    /// The app attaches them before <see cref="IGame.Setup"/> and detaches them after
    /// <see cref="IGame.Shutdown"/>; reading them is valid only while a frame records. The service
    /// never disposes them.
    /// </remarks>
    internal PrimitiveAssets? Primitives => _primitives;

    /// <summary>Attaches the app-lifetime primitive assets before the tick thread starts.</summary>
    internal void AttachPrimitives(PrimitiveAssets primitives)
    {
        ArgumentNullException.ThrowIfNull(primitives);
        _primitives = primitives;
    }

    /// <summary>Detaches the primitive assets after the tick thread joined.</summary>
    internal void DetachPrimitives() => _primitives = null;

    /// <summary>Attaches the app-lifetime SSAO assets used by <see cref="SsaoEffect"/> captures.</summary>
    internal void AttachSsaoAssets(SsaoAssets assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        _ssaoAssets = assets;
    }

    /// <summary>Detaches the SSAO assets before the app disposes them.</summary>
    internal void DetachSsaoAssets() => _ssaoAssets = null;

    /// <summary>Attaches the app-lifetime bloom assets used by <see cref="BloomEffect"/> captures.</summary>
    internal void AttachBloomAssets(BloomAssets assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        _bloomAssets = assets;
    }

    /// <summary>Detaches the bloom assets before the app disposes them.</summary>
    internal void DetachBloomAssets() => _bloomAssets = null;

    /// <summary>Attaches the settings snapshotted when <see cref="EndFrame"/> seals a frame.</summary>
    /// <remarks>Without an attach, sealed frames keep <see cref="TonemappingSnapshot.Default"/>.</remarks>
    internal void AttachTonemapping(TonemappingSettings tonemapping)
    {
        ArgumentNullException.ThrowIfNull(tonemapping);
        _tonemapping = tonemapping;
    }

    /// <summary>Gets the layout buffer that camera text draws reuse.</summary>
    /// <remarks>
    /// One buffer suffices because cameras are sequential; the glyph span it exposes is valid only
    /// until the next arrange call.
    /// </remarks>
    internal TextLayoutBuffer TextLayout => _textLayout;

    /// <summary>Reserves the reusable glyph scratch and returns <paramref name="count"/> matrix slots.</summary>
    /// <param name="count">The number of laid-out glyph records the call may fill.</param>
    /// <returns>Exactly <paramref name="count"/> slots for this call to write.</returns>
    /// <remarks>
    /// Fill one slot per ready glyph and clear both spans with <see cref="ClearTextGlyphs"/>.
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

    /// <summary>Gets the writable texture span reserved by <see cref="ReserveTextGlyphs"/>.</summary>
    internal Span<Texture?> TextGlyphTextures => _textGlyphTextures.AsSpan(0, _textGlyphCount);

    /// <summary>Clears the per-glyph texture references the last text draw wrote.</summary>
    /// <remarks>The frame's retention set, not this scratch, keeps the textures alive.</remarks>
    internal void ClearTextGlyphs()
    {
        if (_textGlyphCount == 0)
        {
            return;
        }

        Array.Clear(_textGlyphTextures, 0, _textGlyphCount);
        _textGlyphCount = 0;
    }

    /// <summary>Begins <paramref name="font"/>'s glyph cache frame once for the recording camera.</summary>
    /// <exception cref="InvalidOperationException">No frame is recording.</exception>
    /// <remarks>
    /// The pipeline keeps the highest frame index, so a repeat never moves the clock backwards.
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

    /// <summary>Starts recording a frame into <paramref name="packet"/>.</summary>
    /// <param name="packet">The writable packet acquired from the frame ring.</param>
    /// <param name="time">Absolute seconds since the app started.</param>
    /// <param name="deltaTime">Clamped seconds since the previous tick.</param>
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

    /// <summary>Seals the recording packet, snapshotting the attached tone-mapping settings first.</summary>
    /// <exception cref="InvalidOperationException">A camera leaked undisposed; the frame is not published.</exception>
    /// <remarks>Tone-mapping writes made anywhere in the tick apply to this frame.</remarks>
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

    /// <summary>Drops the recording state without sealing or publishing the packet.</summary>
    internal void AbandonFrame()
    {
        _packet = null;
        _activeCamera = null;
    }

    /// <summary>Creates a camera for the selected projection and appends its pass to the frame.</summary>
    /// <param name="transform">The world-space camera transform.</param>
    /// <param name="projection">The projection kind.</param>
    /// <param name="fieldOfView">The perspective vertical field of view in degrees.</param>
    /// <param name="orthographicHeight">The orthographic visible world-space height.</param>
    /// <param name="clearMode">The buffers the pass clears.</param>
    /// <param name="clearColor">The linear clear color, or <see langword="null"/> for opaque black.</param>
    /// <param name="near">The near clip distance.</param>
    /// <param name="far">The far clip distance.</param>
    /// <param name="viewport">Pixel viewport clamped to the target; <see langword="null"/> for the full target.</param>
    /// <param name="target">The render texture written, or <see langword="null"/> for the linear HDR scene target.</param>
    /// <param name="postProcess">The stack frozen at creation, so later edits affect only later cameras.</param>
    /// <exception cref="ArgumentException">The unused size argument is nonzero, the viewport
    /// clamps empty, or the clear color is non-finite.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The projection is undefined, the clear mode
    /// has unknown bits, or a selected parameter is out of range.</exception>
    /// <exception cref="InvalidOperationException">No frame records, another camera is active,
    /// or the transform is singular.</exception>
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

    /// <summary>Clears the active camera slot when the camera is disposed.</summary>
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

    /// <summary>Captures the stack entries into the reusable scratch the pending pass records.</summary>
    /// <remarks>The capture precedes the pass, so a rejected effect leaves the packet unchanged.</remarks>
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
