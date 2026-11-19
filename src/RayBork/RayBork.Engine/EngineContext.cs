using Microsoft.Extensions.Logging;
using RayBork.Internal;

namespace RayBork;

/// <summary>
/// The engine context: assets, defaults, window, input, logging, renderer status, frame clock, tone
/// mapping, and exit control.
/// </summary>
/// <remarks>
/// Lives for the app lifetime; per-frame members are tick-thread snapshots, so read them in
/// <see cref="IGame.Setup"/>, <see cref="IGame.Tick"/>, or <see cref="IGame.Shutdown"/>. Asset and camera
/// members throw <see cref="ObjectDisposedException"/> after the app stops or a dependency is disposed;
/// <see cref="Tonemapping"/> is the mutable exception, snapshotted per frame.
/// </remarks>
public sealed class EngineContext
{
    private readonly IAppControl _appControl;
    private readonly Func<double> _elapsedTime;
    private readonly IEnginePlatform _platform;
    private readonly RenderStatus _renderStatus;
    private readonly CameraService _cameras;
    private readonly ProceduralAssetService _proceduralAssets;
    private readonly RenderTextureService _renderTextures;
    private bool _hasFrame;
    private double _time;

    internal EngineContext(
        IEnginePlatform platform,
        ILoggerFactory loggerFactory,
        IAssetCatalog assets,
        EngineDefaults defaults,
        IAppControl appControl,
        IInput initialInput,
        double maxDeltaTime,
        Func<double> elapsedTime,
        RenderStatus renderStatus,
        CameraService cameras,
        TonemappingSettings tonemapping,
        RenderTextureService renderTextures
    )
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(defaults);
        ArgumentNullException.ThrowIfNull(appControl);
        ArgumentNullException.ThrowIfNull(initialInput);
        ArgumentNullException.ThrowIfNull(elapsedTime);
        ArgumentNullException.ThrowIfNull(renderStatus);
        ArgumentNullException.ThrowIfNull(cameras);
        ArgumentNullException.ThrowIfNull(tonemapping);
        ArgumentNullException.ThrowIfNull(renderTextures);
        if (assets is not IProceduralAssetProvider proceduralProvider)
        {
            throw new InvalidOperationException(
                "The asset catalog does not provide the procedural asset service; the engine wired the wrong catalog."
            );
        }

        _platform = platform;
        _appControl = appControl;
        _elapsedTime = elapsedTime;
        _renderStatus = renderStatus;
        _cameras = cameras;
        _proceduralAssets = proceduralProvider.ProceduralAssets;
        _renderTextures = renderTextures;
        Assets = assets;
        Defaults = defaults;
        Input = initialInput;
        LoggerFactory = loggerFactory;
        MaxDeltaTime = maxDeltaTime;
        Tonemapping = tonemapping;
    }

    /// <summary>
    /// Gets the app's asset catalog. Valid from <see cref="IGame.Setup"/> and safe from any thread.
    /// </summary>
    /// <remarks>
    /// Its loader thread runs before <see cref="IGame.Setup"/>. Engine assets are shared and
    /// reference-counted, while custom data returns a fresh caller-owned instance per call.
    /// </remarks>
    public IAssetCatalog Assets { get; }

    /// <summary>
    /// Gets the engine-owned defaults: mesh shader, white and error textures, and fonts.
    /// </summary>
    /// <remarks>
    /// Preloaded before <see cref="IGame.Setup"/>; the textures and fonts are engine-owned and must not be
    /// disposed. <see cref="EngineDefaults.CreateMeshShader"/> returns a new caller-owned material.
    /// </remarks>
    public EngineDefaults Defaults { get; }

    /// <summary>
    /// Creates a builder for one procedural <see cref="Mesh"/>.
    /// </summary>
    /// <param name="topology">The primitive topology; the default is triangle list.</param>
    /// <returns>The builder; <see cref="MeshBuilder.BuildAsync"/> creates one immutable mesh.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="topology"/> is not a defined value.</exception>
    /// <remarks>
    /// Safe from any thread; awaiting <see cref="MeshBuilder.BuildAsync"/> on the tick thread suspends
    /// that tick. A builder instance is not thread-safe, so create one per thread.
    /// </remarks>
    public MeshBuilder CreateMeshBuilder(
        PrimitiveTopology topology = PrimitiveTopology.TriangleList
    ) => _proceduralAssets.CreateMeshBuilder(topology);

    /// <summary>
    /// Creates a builder for one procedural <see cref="Texture"/>.
    /// </summary>
    /// <param name="width">The base-level width, in pixels.</param>
    /// <param name="height">The base-level height, in pixels.</param>
    /// <param name="format">
    /// The uncompressed format: <see cref="TextureFormat.Rgba8Srgb"/> (the default),
    /// <see cref="TextureFormat.Rgba8Unorm"/>, or <see cref="TextureFormat.R8Unorm"/>.
    /// </param>
    /// <param name="generateMips">
    /// Whether <see cref="TextureBuilder.BuildAsync"/> fills the mip chain; the default is true.
    /// </param>
    /// <param name="sampler">
    /// The sampler state, or <see langword="null"/> for linear filtering with repeat wrapping.
    /// </param>
    /// <returns>The builder; <see cref="TextureBuilder.BuildAsync"/> creates one immutable texture.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is not positive or exceeds 16384.</exception>
    /// <exception cref="ArgumentException">The format is not procedural or the sampler is invalid.</exception>
    /// <remarks>
    /// Safe from any thread; a builder is not thread-safe. CPU pixels stay builder-owned.
    /// </remarks>
    public TextureBuilder CreateTextureBuilder(
        int width,
        int height,
        TextureFormat format = TextureFormat.Rgba8Srgb,
        bool generateMips = true,
        SamplerState? sampler = null
    ) => _proceduralAssets.CreateTextureBuilder(width, height, format, generateMips, sampler);

    /// <summary>Creates a camera that records one pass into the current frame.</summary>
    /// <param name="transform">The world-space transform; the camera looks down local <c>-Z</c>.</param>
    /// <param name="projection">The projection kind; exactly its size argument is used.</param>
    /// <param name="fieldOfView">
    /// The perspective vertical field of view in degrees, in <c>(0, 180)</c>; <c>0</c> for orthographic.
    /// </param>
    /// <param name="orthographicHeight">
    /// The visible world-space height, finite and positive; <c>0</c> for perspective.
    /// </param>
    /// <param name="clearMode">The buffers to clear; the default is color and depth.</param>
    /// <param name="clearColor">
    /// The linear clear color; <see langword="null"/> is opaque black, HDR above <c>1</c> allowed.
    /// </param>
    /// <param name="near">The near clip distance; finite and greater than zero.</param>
    /// <param name="far">
    /// The far clip distance, greater than <paramref name="near"/>; the default is <c>1000</c>, and
    /// positive infinity is allowed for perspective only.
    /// </param>
    /// <param name="viewport">
    /// The pixel viewport, or <see langword="null"/> for the full target; clamped and non-empty.
    /// </param>
    /// <param name="target">
    /// The render texture to write, or <see langword="null"/> for the linear HDR scene target; the
    /// viewport and aspect ratio use the target's clamped size.
    /// </param>
    /// <param name="postProcess">
    /// The caller-owned stack snapshotted at creation, or <see langword="null"/> for none; it runs after
    /// geometry, before the next camera or present, and later edits apply to later cameras only.
    /// </param>
    /// <returns>The camera; dispose it before <see cref="IGame.Tick"/> returns.</returns>
    /// <exception cref="ArgumentException">
    /// A wrong size argument, an invalid viewport, or a non-finite clear color.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A projection or clear-mode value is undefined, has unknown bits, or is out of range.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Not the tick thread, no frame, another camera active, a singular transform, or a bad effect.
    /// </exception>
    /// <remarks>
    /// Legal only during <see cref="IGame.Tick"/>; dispose each camera before the next. An undisposed
    /// camera stops the app at tick return and the frame is not published.
    /// </remarks>
    public Camera CreateCamera(
        Transform transform,
        CameraProjection projection,
        float fieldOfView = 0f,
        float orthographicHeight = 0f,
        ClearMode clearMode = ClearMode.ColorAndDepth,
        Color? clearColor = null,
        float near = 0.01f,
        float far = 1000f,
        Rect? viewport = null,
        RenderTexture? target = null,
        PostProcessStack? postProcess = null
    ) =>
        _cameras.CreateCamera(
            transform,
            projection,
            fieldOfView,
            orthographicHeight,
            clearMode,
            clearColor,
            near,
            far,
            viewport,
            target,
            postProcess
        );

    /// <summary>
    /// Creates one runtime render texture after the render thread allocated its images.
    /// </summary>
    /// <param name="description">The size, color format, depth flag, and requested MSAA sample count.</param>
    /// <param name="cancellationToken">Cancels the request before the allocation completes.</param>
    /// <returns>
    /// The created texture; the caller owns it and must dispose any camera targeting it first.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A dimension is not positive or exceeds <c>16384</c>, or the sample count is not 1, 2, 4, or 8.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The format is not <see cref="TextureFormat.Rgba16Float"/>, <see cref="TextureFormat.Rgba8Unorm"/>,
    /// or <see cref="TextureFormat.Rgba8Srgb"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled before the render thread created the target.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The creation queue is full (256 pending) because the render thread is not draining it.
    /// </exception>
    /// <exception cref="GraphicsException">The renderer could not create the target's images.</exception>
    /// <remarks>
    /// Legal from <see cref="IGame.Setup"/> and <see cref="IGame.Tick"/>; the render thread services it
    /// independently, so it never deadlocks the tick thread. Setup can await it, while a tick must block
    /// and stalls until allocation finishes, so prefer Setup. Contents are undefined until a camera writes
    /// them. The effective MSAA count is the highest supported count not exceeding the request (or
    /// <c>1</c>, with a warning), reported by <see cref="RenderTexture.SampleCount"/> and re-selected after
    /// a device recovery.
    /// </remarks>
    public async Task<RenderTexture> CreateRenderTextureAsync(
        RenderTextureDescription description,
        CancellationToken cancellationToken = default
    ) => await _renderTextures.CreateAsync(description, cancellationToken).ConfigureAwait(false);

    /// <summary>Gets the current tick's input snapshot.</summary>
    public IInput Input { get; private set; }

    /// <summary>Gets the window; snapshots and queued setters make it thread-safe.</summary>
    public IWindow Window => _platform.Window;

    /// <summary>Gets the app-owned logger factory; thread-safe and never null.</summary>
    public ILoggerFactory LoggerFactory { get; }

    /// <summary>Gets renderer, device, and presentation info; thread-safe.</summary>
    /// <remarks>
    /// The render thread writes it at startup and after a recovery; a read can be a tick stale.
    /// </remarks>
    public EngineInfo Info => _renderStatus.Info;

    /// <summary>Gets monotonic seconds since start; live in Setup, captured at tick start.</summary>
    public double Time => _hasFrame ? _time : _elapsedTime();

    /// <summary>Gets the clamped delta; zero in Setup and on the first tick.</summary>
    public double DeltaTime { get; private set; }

    /// <summary>Gets the unclamped delta; zero in Setup.</summary>
    public double UnscaledDeltaTime { get; private set; }

    /// <summary>Gets the completed tick count; <c>0</c> in Setup and <c>1</c> on the first tick.</summary>
    public long FrameCount { get; private set; }

    /// <summary>Gets the smoothed tick rate, not the presented frame rate.</summary>
    public double Fps { get; private set; }

    /// <summary>Gets whether this is the first tick.</summary>
    public bool IsFirstTick { get; private set; }

    /// <summary>Gets whether the app is exiting after an exit request, close, or stop.</summary>
    public bool IsExiting => _appControl.IsExitRequested;

    /// <summary>Gets the <see cref="DeltaTime"/> clamp, in seconds.</summary>
    public double MaxDeltaTime { get; }

    /// <summary>Gets render stats of the last completed frame; thread-safe.</summary>
    public RenderStats Stats => _renderStatus.Stats;

    /// <summary>Gets the app-lifetime runtime tone-mapping settings.</summary>
    /// <remarks>
    /// Values start from the <see cref="RayBorkAppBuilder"/> configuration; a write affects the sealing
    /// frame, and published frames keep their snapshots. The object is stable and not thread-safe.
    /// </remarks>
    public TonemappingSettings Tonemapping { get; }

    /// <summary>Requests a graceful exit after the current tick.</summary>
    public void RequestExit() => _appControl.RequestExit();

    internal void SetInput(IInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Input = input;
    }

    internal void SetFrame(in FrameTime frame)
    {
        _time = frame.Time;
        DeltaTime = frame.DeltaTime;
        UnscaledDeltaTime = frame.UnscaledDeltaTime;
        FrameCount = frame.FrameCount;
        Fps = frame.Fps;
        IsFirstTick = frame.IsFirstTick;
        _hasFrame = true;
    }
}
