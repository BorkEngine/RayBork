using Microsoft.Extensions.Logging;
using RayBork.Internal;

namespace RayBork;

/// <summary>
/// The engine context: the asset catalog, window, input, logging, renderer status, frame clock, and
/// exit control.
/// </summary>
/// <remarks>
/// The context is stable for the app lifetime, but its per-frame members are tick-thread snapshots.
/// Read them from <see cref="IGame.Setup"/>, <see cref="IGame.Tick"/>, or <see cref="IGame.Shutdown"/>
/// instead of caching them across frames.
/// </remarks>
public sealed class EngineContext {
    private readonly IAppControl _appControl;
    private readonly Func<double> _elapsedTime;
    private readonly IEnginePlatform _platform;
    private readonly RenderStatus _renderStatus;
    private readonly CameraService _cameras;
    private readonly ProceduralAssetService _proceduralAssets;
    private bool _hasFrame;
    private double _time;

    internal EngineContext(
        IEnginePlatform platform,
        ILoggerFactory loggerFactory,
        IAssetCatalog assets,
        IAppControl appControl,
        IInput initialInput,
        double maxDeltaTime,
        Func<double> elapsedTime,
        RenderStatus renderStatus,
        CameraService cameras) {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(appControl);
        ArgumentNullException.ThrowIfNull(initialInput);
        ArgumentNullException.ThrowIfNull(elapsedTime);
        ArgumentNullException.ThrowIfNull(renderStatus);
        ArgumentNullException.ThrowIfNull(cameras);
        if (assets is not IProceduralAssetProvider proceduralProvider) {
            throw new InvalidOperationException(
                "The asset catalog does not provide the procedural asset service; the engine wired the wrong catalog.");
        }

        _platform = platform;
        _appControl = appControl;
        _elapsedTime = elapsedTime;
        _renderStatus = renderStatus;
        _cameras = cameras;
        _proceduralAssets = proceduralProvider.ProceduralAssets;
        Assets = assets;
        Input = initialInput;
        LoggerFactory = loggerFactory;
        MaxDeltaTime = maxDeltaTime;
    }

    /// <summary>
    /// Gets the app's asset catalog. It is valid from <see cref="IGame.Setup"/> and safe to call from
    /// any thread.
    /// </summary>
    /// <remarks>
    /// The catalog is mounted and its loader thread is running before <see cref="IGame.Setup"/> runs.
    /// Engine assets are shared and reference-counted; custom data returns a fresh caller-owned
    /// instance per call. After the app stops, every catalog member throws
    /// <see cref="ObjectDisposedException"/>.
    /// </remarks>
    public IAssetCatalog Assets { get; }

    /// <summary>
    /// Creates a builder for one procedural <see cref="Mesh"/>.
    /// </summary>
    /// <param name="topology">The primitive topology of the mesh. The default is triangle list.</param>
    /// <returns>The builder. Its <see cref="MeshBuilder.BuildAsync"/> creates one immutable mesh.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="topology"/> is not a defined value.</exception>
    /// <exception cref="ObjectDisposedException">The app has stopped.</exception>
    /// <remarks>
    /// <para>
    /// The factory and the builder's CPU and build members are safe to call from any thread and do not
    /// touch tick-affine engine state. Awaiting <see cref="MeshBuilder.BuildAsync"/> on the tick thread
    /// suspends that tick's continuation like an asset load; a build may also be started on a worker
    /// thread. A builder instance is not thread-safe, so create one per thread.
    /// </para>
    /// <para>
    /// After the app stops, this member and every builder it created throw
    /// <see cref="ObjectDisposedException"/>.
    /// </para>
    /// </remarks>
    public MeshBuilder CreateMeshBuilder(PrimitiveTopology topology = PrimitiveTopology.TriangleList)
        => _proceduralAssets.CreateMeshBuilder(topology);

    /// <summary>
    /// Creates a builder for one procedural <see cref="Texture"/>.
    /// </summary>
    /// <param name="width">The base-level width in pixels.</param>
    /// <param name="height">The base-level height in pixels.</param>
    /// <param name="format">
    /// The uncompressed storage format: <see cref="TextureFormat.Rgba8Srgb"/> (the default),
    /// <see cref="TextureFormat.Rgba8Unorm"/>, or <see cref="TextureFormat.R8Unorm"/>.
    /// </param>
    /// <param name="generateMips">
    /// Whether <see cref="TextureBuilder.BuildAsync"/> generates the full mip chain when no chain
    /// exists. The default is <see langword="true"/>.
    /// </param>
    /// <param name="sampler">
    /// The sampler state, or <see langword="null"/> for linear filtering with repeat wrapping.
    /// </param>
    /// <returns>The builder. Its <see cref="TextureBuilder.BuildAsync"/> creates one immutable texture.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is not positive or exceeds 16384.</exception>
    /// <exception cref="ArgumentException">
    /// The format is not an uncompressed procedural format, or the sampler state is not valid.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The app has stopped.</exception>
    /// <remarks>
    /// The factory and the builder's CPU and build members are safe to call from any thread and do not
    /// touch tick-affine engine state. A builder instance is not thread-safe, so create one per thread.
    /// The CPU pixels stay owned by the builder and are never exposed on the built asset. After the app
    /// stops, this member and every builder it created throw <see cref="ObjectDisposedException"/>.
    /// </remarks>
    public TextureBuilder CreateTextureBuilder(
        int width,
        int height,
        TextureFormat format = TextureFormat.Rgba8Srgb,
        bool generateMips = true,
        SamplerState? sampler = null)
        => _proceduralAssets.CreateTextureBuilder(width, height, format, generateMips, sampler);

    /// <summary>
    /// Creates a perspective camera that records one pass into the current frame.
    /// </summary>
    /// <param name="transform">The world-space camera transform. The camera looks down local <c>-Z</c>.</param>
    /// <param name="projection">
    /// The projection kind. M4 accepts <see cref="CameraProjection.Perspective"/> here; use
    /// <see cref="CreateOrthographicCamera"/> when an orthographic height is needed.
    /// </param>
    /// <param name="fieldOfView">
    /// The vertical field of view in degrees. It must be finite and inside <c>(0, 180)</c>.
    /// </param>
    /// <param name="clearMode">The buffers the pass clears. The default is color and depth.</param>
    /// <param name="clearColor">
    /// The linear clear color. <see langword="null"/> means opaque black; HDR components above <c>1</c>
    /// are allowed.
    /// </param>
    /// <param name="near">The near clip distance. It must be finite and greater than zero.</param>
    /// <param name="far">
    /// The far clip distance. It must be greater than <paramref name="near"/>; positive infinity is
    /// allowed for a perspective camera.
    /// </param>
    /// <param name="viewport">
    /// The pixel viewport, or <see langword="null"/> for the full window render size. The rectangle is
    /// clamped to the target and must not clamp to an empty region.
    /// </param>
    /// <returns>The camera. Dispose it before <see cref="IGame.Tick"/> returns.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="projection"/> is <see cref="CameraProjection.Orthographic"/>, the resolved
    /// viewport is invalid or empty, or the clear color has a non-finite component.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">A projection or clip-plane parameter is outside its range.</exception>
    /// <exception cref="InvalidOperationException">
    /// The caller is not the tick thread, no frame is recording, another camera is active, or the camera
    /// transform is singular.
    /// </exception>
    /// <remarks>
    /// Legal only during <see cref="IGame.Tick"/>. Cameras are sequential: dispose one before creating
    /// the next. A camera left undisposed when the tick returns stops the app with
    /// <see cref="InvalidOperationException"/> naming it; the frame is not published.
    /// </remarks>
    public Camera CreateCamera(
        Transform transform,
        CameraProjection projection,
        float fieldOfView,
        ClearMode clearMode = ClearMode.ColorAndDepth,
        Color? clearColor = null,
        float near = 0.01f,
        float far = float.PositiveInfinity,
        Rect? viewport = null) {
        if (!Enum.IsDefined(projection)) {
            throw new ArgumentOutOfRangeException(nameof(projection), projection, "The projection is not a defined value.");
        }

        if (projection != CameraProjection.Perspective) {
            throw new ArgumentException(
                "CreateCamera records a perspective camera. Use CreateOrthographicCamera for an "
                    + "orthographic camera, which requires a visible height.",
                nameof(projection));
        }

        return _cameras.CreateCamera(transform, fieldOfView, clearMode, clearColor, near, far, viewport);
    }

    /// <summary>
    /// Creates an orthographic camera that records one pass into the current frame.
    /// </summary>
    /// <param name="transform">The world-space camera transform. The camera looks down local <c>-Z</c>.</param>
    /// <param name="height">The visible world-space height. It must be finite and greater than zero.</param>
    /// <param name="clearMode">The buffers the pass clears. The default is color and depth.</param>
    /// <param name="clearColor">
    /// The linear clear color. <see langword="null"/> means opaque black; HDR components above <c>1</c>
    /// are allowed.
    /// </param>
    /// <param name="near">The near clip distance. It must be finite and greater than zero.</param>
    /// <param name="far">The finite far clip distance. It must be greater than <paramref name="near"/>.</param>
    /// <param name="viewport">
    /// The pixel viewport, or <see langword="null"/> for the full window render size. The rectangle is
    /// clamped to the target and must not clamp to an empty region.
    /// </param>
    /// <returns>The camera. Dispose it before <see cref="IGame.Tick"/> returns.</returns>
    /// <exception cref="ArgumentException">
    /// The resolved viewport is invalid or empty, or the clear color has a non-finite component.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">A projection or clip-plane parameter is outside its range.</exception>
    /// <exception cref="InvalidOperationException">
    /// The caller is not the tick thread, no frame is recording, another camera is active, or the camera
    /// transform is singular.
    /// </exception>
    /// <remarks>
    /// Legal only during <see cref="IGame.Tick"/>. Cameras are sequential: dispose one before creating
    /// the next. A camera left undisposed when the tick returns stops the app with
    /// <see cref="InvalidOperationException"/> naming it; the frame is not published.
    /// </remarks>
    public Camera CreateOrthographicCamera(
        Transform transform,
        float height,
        ClearMode clearMode = ClearMode.ColorAndDepth,
        Color? clearColor = null,
        float near = 0.01f,
        float far = 1000f,
        Rect? viewport = null)
        => _cameras.CreateOrthographicCamera(transform, height, clearMode, clearColor, near, far, viewport);

    /// <summary>
    /// Gets the input snapshot for the current tick. Read it on the tick thread.
    /// </summary>
    public IInput Input { get; private set; }

    /// <summary>
    /// Gets the application window. Getters are snapshots and setters are queued, so this member is
    /// safe to use from any thread.
    /// </summary>
    public IWindow Window => _platform.Window;

    /// <summary>
    /// Gets the app-owned logger factory. It is never <see langword="null"/> and is safe to use from
    /// any thread.
    /// </summary>
    public ILoggerFactory LoggerFactory { get; }

    /// <summary>
    /// Gets the renderer, device, and presentation information. Valid from <see cref="IGame.Setup"/>
    /// and safe to read from any thread.
    /// </summary>
    /// <remarks>
    /// The render thread writes the value at startup and after a recovery that changes the device or
    /// presentation state. A read can be stale by one tick and is not transactional with respect to
    /// <see cref="Stats"/>.
    /// </remarks>
    public EngineInfo Info => _renderStatus.Info;

    /// <summary>
    /// Gets absolute monotonic seconds since the app started. During <see cref="IGame.Setup"/> this is
    /// the live elapsed time; during ticks it is the value captured at the start of the tick. It is
    /// not reset at the first tick. Read it on the tick thread.
    /// </summary>
    public double Time => _hasFrame ? _time : _elapsedTime();

    /// <summary>
    /// Gets the clamped seconds since the previous tick. It is <c>0</c> in
    /// <see cref="IGame.Setup"/> and on the first tick. Read it on the tick thread.
    /// </summary>
    public double DeltaTime { get; private set; }

    /// <summary>
    /// Gets the unclamped seconds since the previous tick. It is <c>0</c> in
    /// <see cref="IGame.Setup"/>. Read it on the tick thread.
    /// </summary>
    public double UnscaledDeltaTime { get; private set; }

    /// <summary>
    /// Gets the number of completed ticks. It is <c>0</c> in <see cref="IGame.Setup"/> and <c>1</c>
    /// during the first <see cref="IGame.Tick"/>. Read it on the tick thread.
    /// </summary>
    public long FrameCount { get; private set; }

    /// <summary>
    /// Gets the smoothed number of ticks per second. Read it on the tick thread.
    /// </summary>
    public double Fps { get; private set; }

    /// <summary>
    /// Gets whether the current tick is the first tick. It is <see langword="true"/> only during the
    /// first <see cref="IGame.Tick"/>. Read it on the tick thread.
    /// </summary>
    public bool IsFirstTick { get; private set; }

    /// <summary>
    /// Gets whether the app is exiting. It becomes <see langword="true"/> after
    /// <see cref="RequestExit"/>, a window close request, or an engine stop request. Read it on the
    /// tick thread.
    /// </summary>
    public bool IsExiting => _appControl.IsExitRequested;

    /// <summary>
    /// Gets the configured clamp for <see cref="DeltaTime"/>, in seconds. Read it on the tick thread.
    /// </summary>
    public double MaxDeltaTime { get; }

    /// <summary>
    /// Gets the render statistics of the last completed frame. Valid from <see cref="IGame.Setup"/>
    /// and safe to read from any thread.
    /// </summary>
    /// <remarks>
    /// The render thread updates the snapshot after a frame completes, so a read can be stale by one
    /// tick. Individual fields are not a consistent transactional snapshot; use it for diagnostics
    /// and overlays, not for synchronization.
    /// </remarks>
    public RenderStats Stats => _renderStatus.Stats;

    /// <summary>
    /// Requests a graceful exit. The current tick completes and the final frame is presented. Call it
    /// on the tick thread.
    /// </summary>
    public void RequestExit() => _appControl.RequestExit();

    internal void SetInput(IInput input) {
        ArgumentNullException.ThrowIfNull(input);
        Input = input;
    }

    internal void SetFrame(in FrameTime frame) {
        _time = frame.Time;
        DeltaTime = frame.DeltaTime;
        UnscaledDeltaTime = frame.UnscaledDeltaTime;
        FrameCount = frame.FrameCount;
        Fps = frame.Fps;
        IsFirstTick = frame.IsFirstTick;
        _hasFrame = true;
    }
}
