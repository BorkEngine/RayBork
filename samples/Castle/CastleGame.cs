using System.Globalization;
using System.Numerics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RayBork;

namespace Castle;

/// <summary>
/// The M4 Castle sample: a RayBork recreation of raylib's <c>models_loading</c> example. It loads the
/// raylib castle model, its diffuse texture, and a custom unlit shader from the baked pack, draws the
/// model with an orbital perspective camera, and draws the reference grid with the built-in default
/// shader every tick.
/// </summary>
/// <remarks>
/// <para>
/// The sample is the M4 Native AOT gate. It records whether any completed frame reported both draw
/// calls and instances; when no completed frame ever does, <see cref="Shutdown"/> throws an
/// <see cref="InvalidOperationException"/> naming the Castle gate. A run that silently draws nothing
/// therefore fails instead of exiting successfully, while a slow host is judged on completed frames
/// rather than on a fixed tick number.
/// </para>
/// <para>
/// The camera is created and disposed inside each <see cref="Tick"/>; the model, shaders, texture, and
/// grid mesh are created or loaded once in <see cref="Setup"/> and disposed exactly once in
/// <see cref="Shutdown"/>.
/// </para>
/// </remarks>
internal sealed class CastleGame : IGame {
    private const int STATS_LOG_INTERVAL = 30;
    private const string MODEL_PATH = "/models/obj/castle";
    private const int GRID_SLICES = 20;
    private const float GRID_SPACING = 10f;
    private const float FIELD_OF_VIEW_DEGREES = 45f;
    private const float NEAR_PLANE = 0.01f;
    private const float ORBIT_RADIANS_PER_PIXEL = 0.25f * DEGREES_TO_RADIANS;
    private const float ZOOM_FACTOR_PER_WHEEL_STEP = 1.15f;
    private const float MIN_ORBIT_DISTANCE = 5f;
    private const float MAX_ORBIT_DISTANCE = 500f;
    private const float MAX_ORBIT_PITCH_DEGREES = 89f;
    private const float DEGREES_TO_RADIANS = MathF.PI / 180f;
    private const float RAYWHITE_LINEAR = 0.913f;
    private const int VULKAN_MAJOR_SHIFT = 22;
    private const int VULKAN_MINOR_SHIFT = 12;
    private const uint VULKAN_MINOR_MASK = 0x3FFu;
    private const uint VULKAN_PATCH_MASK = 0xFFFu;

    private static readonly Vector3 _cameraTarget = new(0f, 12f, 0f);
    private static readonly Vector3 _initialCameraPosition = new(50f, 50f, 50f);
    private static readonly Color _clearColor = new(RAYWHITE_LINEAR, RAYWHITE_LINEAR, RAYWHITE_LINEAR, 1f);
    private static readonly Color _gridColor = new(0.5f, 0.5f, 0.5f, 1f);

    private readonly int _frameLimit;

    private EngineContext? _context;
    private ILogger _logger = NullLogger.Instance;
    private Model? _model;
    private Texture? _texture;
    private Shader? _gridShader;
    private Mesh? _gridMesh;
    private float _yaw;
    private float _pitch;
    private float _distance;
    private long _lastFrameCount;
    private bool _sawDrawnFrame;
    private string _exitReason = "no ticks were run";

    /// <summary>
    /// Initializes the game.
    /// </summary>
    /// <param name="frameLimit">The number of ticks to run, or <c>0</c> to run until close.</param>
    internal CastleGame(int frameLimit) {
        _frameLimit = frameLimit;

        // The orbital state is derived from raylib's initial camera position and target so the first
        // frame matches the reference example exactly.
        Vector3 offset = _initialCameraPosition - _cameraTarget;
        _distance = offset.Length();
        _yaw = MathF.Atan2(offset.X, offset.Z);
        _pitch = MathF.Asin(offset.Y / _distance);
    }

    /// <inheritdoc/>
    public async Task Setup(EngineContext context) {
        _context = context;
        _logger = context.LoggerFactory.CreateLogger("Castle");

        _logger.LogInformation(
            "RayBork {EngineVersion} M4 Castle sample starting (raylib models_loading recreation).",
            EngineVersion.Current);

        EngineInfo info = context.Info;
        _logger.LogInformation(
            "Engine {EngineName} {Version}: renderer {RendererName}, GPU '{GpuName}', Vulkan {VulkanApiVersion}, validation {ValidationEnabled}.",
            info.Name,
            info.Version,
            info.RendererName,
            info.GpuName,
            FormatVulkanVersion(info.VulkanApiVersion),
            info.ValidationEnabled);
        _logger.LogInformation(
            "Presentation: swapchain {SwapchainFormat}, HDR {HdrOutput}, {SampleCount}x MSAA, present mode {PresentMode}.",
            info.SwapchainFormat,
            info.HdrOutput,
            info.SampleCount,
            info.PresentMode);
        _logger.LogInformation(
            "Window: {Width}x{Height} window units, {RenderWidth}x{RenderHeight} render pixels, DPI scale {DpiScale:0.###}.",
            context.Window.Width,
            context.Window.Height,
            context.Window.RenderWidth,
            context.Window.RenderHeight,
            context.Window.DpiScale);
        _logger.LogInformation(
            "Frame limit: {FrameLimit}.",
            _frameLimit == 0 ? "none (run until close)" : _frameLimit.ToString(CultureInfo.InvariantCulture));

        _model = await context.Assets.Load<Model>(MODEL_PATH);
        _texture = await context.Assets.Load<Texture>("/models/obj/castle_diffuse");
        _gridShader = await context.Assets.Load<Shader>("/shaders/mesh");

        // The model owns its part materials; the Castle pack bakes the one castle part with
        // /shaders/castle. Until the sample's cleanup task, bind the diffuse texture on that material
        // and draw the model with its part material.
        Shader castleMaterial = _model.Parts[0].Shader;
        LogModelMetadata(_model);
        LogTextureMetadata(_texture);
        LogShaderMetadata(castleMaterial);

        castleMaterial.SetUniform("mainTexture", _texture);
        _gridMesh = await CreateGridMeshAsync(context);

        _logger.LogInformation(
            "Grid mesh {Path}: {VertexCount} vertex/vertices, {IndexCount} index/indices, topology {Topology}, shader {ShaderPath}.",
            _gridMesh.Path,
            _gridMesh.VertexCount,
            _gridMesh.IndexCount,
            _gridMesh.Topology,
            _gridShader.Path);
        _logger.LogInformation(
            "Camera: target {Target}, initial position {Position}, field of view {FieldOfView} degrees, near {Near}, far +infinity.",
            _cameraTarget,
            _initialCameraPosition,
            FIELD_OF_VIEW_DEGREES,
            NEAR_PLANE);
        _logger.LogInformation(
            "Controls: Escape or the frame limit exits; hold the left mouse button and drag to orbit the castle; the mouse wheel zooms.");
    }

    /// <inheritdoc/>
    public void Tick(EngineContext context) {
        ILogger logger = _logger;
        string? exitReason = null;

        if (context.Input.IsKeyPressed(Key.Escape)) {
            exitReason = "Escape";
            logger.LogInformation("Escape pressed; requesting exit.");
            context.RequestExit();
        }

        UpdateOrbit(context.Input);
        RecordCastlePass(context);
        ObserveCompletedFrame(context);

        if (context.FrameCount % STATS_LOG_INTERVAL == 0) {
            logger.LogInformation(
                "Tick {FrameCount}: time {Time:0.000}s, fps {Fps:0.0}, delta {DeltaTime:0.0000}s, camera {CameraPosition}, distance {Distance:0.###}.",
                context.FrameCount,
                context.Time,
                context.Fps,
                context.DeltaTime,
                CameraPosition,
                _distance);
            LogRenderStats(logger, context.Stats, "Tick");
        }

        _lastFrameCount = context.FrameCount;

        if (exitReason is null && _frameLimit > 0 && context.FrameCount >= _frameLimit) {
            exitReason = $"frame limit ({_frameLimit})";
            logger.LogInformation("Frame limit reached; requesting exit.");
            context.RequestExit();
        }

        _exitReason = exitReason ?? "window close or engine request";
    }

    /// <inheritdoc/>
    public Task Shutdown() {
        // Shutdown has no context argument. The context is stable for the app lifetime and Stats is
        // safe to read from any thread; the snapshot describes the last frame the render thread
        // completed, which can lag the final tick by one frame.
        if (_context is not null) {
            LogRenderStats(_logger, _context.Stats, "Final");
        }

        _logger.LogInformation(
            "Castle stopped after {FrameCount} tick(s): exit {ExitReason}.",
            _lastFrameCount,
            _exitReason);

        // The model owns one reference to every distinct part mesh and its part materials. Dispose the
        // model first, then this sample's own texture, grid shader, and grid mesh references, exactly
        // once each; the catalog releases the GPU resources on the render thread and a recorded frame
        // keeps them alive.
        _model?.Dispose();
        _model = null;
        _texture?.Dispose();
        _texture = null;
        _gridShader?.Dispose();
        _gridShader = null;
        _gridMesh?.Dispose();
        _gridMesh = null;

        if (!_sawDrawnFrame) {
            throw new InvalidOperationException(
                "The Castle gate failed: no completed frame reported both draw calls and instances, "
                    + "so the castle scene is not visible. Check that the pack, camera, and scene "
                    + "target are valid and that the renderer completes a frame.");
        }

        return Task.CompletedTask;
    }

    private Vector3 CameraPosition {
        get {
            float cosPitch = MathF.Cos(_pitch);
            Vector3 direction = new(
                MathF.Sin(_yaw) * cosPitch,
                MathF.Sin(_pitch),
                MathF.Cos(_yaw) * cosPitch);
            return _cameraTarget + (direction * _distance);
        }
    }

    private static async Task<Mesh> CreateGridMeshAsync(EngineContext context) {
        MeshBuilder builder = context.CreateMeshBuilder(PrimitiveTopology.LineList);
        int halfSlices = GRID_SLICES / 2;
        float extent = halfSlices * GRID_SPACING;

        // raylib DrawGrid(20, 10.0f): 21 lines per axis from -100 to +100 at 10-unit spacing.
        for (int slice = -halfSlices; slice <= halfSlices; slice++) {
            float offset = slice * GRID_SPACING;

            uint first = (uint)builder.Vertices.Count;
            builder.Vertices.Add(new Vertex(new Vector3(offset, 0f, -extent), color: _gridColor));
            builder.Vertices.Add(new Vertex(new Vector3(offset, 0f, extent), color: _gridColor));
            builder.Indices.Add(first);
            builder.Indices.Add(first + 1u);

            first = (uint)builder.Vertices.Count;
            builder.Vertices.Add(new Vertex(new Vector3(-extent, 0f, offset), color: _gridColor));
            builder.Vertices.Add(new Vertex(new Vector3(extent, 0f, offset), color: _gridColor));
            builder.Indices.Add(first);
            builder.Indices.Add(first + 1u);
        }

        return await builder.BuildAsync();
    }

    private void UpdateOrbit(IInput input) {
        if (input.IsMouseButtonDown(MouseButton.Left)) {
            Vector2 delta = input.MouseDelta;
            _yaw -= delta.X * ORBIT_RADIANS_PER_PIXEL;

            float pitchLimit = MAX_ORBIT_PITCH_DEGREES * DEGREES_TO_RADIANS;
            _pitch = Math.Clamp(
                _pitch - (delta.Y * ORBIT_RADIANS_PER_PIXEL),
                -pitchLimit,
                pitchLimit);
        }

        float wheel = input.MouseWheel;
        if (wheel != 0f) {
            _distance = Math.Clamp(
                _distance * MathF.Pow(ZOOM_FACTOR_PER_WHEEL_STEP, -wheel),
                MIN_ORBIT_DISTANCE,
                MAX_ORBIT_DISTANCE);
        }
    }

    private void RecordCastlePass(EngineContext context) {
        Vector3 position = CameraPosition;
        Matrix4x4 lookAt = Matrix4x4.CreateLookAt(position, _cameraTarget, Vector3.UnitY);
        if (!Matrix4x4.Invert(lookAt, out Matrix4x4 cameraMatrix)) {
            throw new InvalidOperationException(
                "The Castle sample could not derive the orbital camera rotation.");
        }

        Transform cameraTransform = new(
            position,
            Quaternion.CreateFromRotationMatrix(cameraMatrix),
            Vector3.One);

        // The camera is a frame-scoped recorder; the using scope disposes it before Tick returns.
        using Camera camera = context.CreateCamera(
            cameraTransform,
            CameraProjection.Perspective,
            fieldOfView: FIELD_OF_VIEW_DEGREES,
            clearMode: ClearMode.ColorAndDepth,
            clearColor: _clearColor,
            near: NEAR_PLANE,
            far: float.PositiveInfinity);

        // raylib DrawModel at the origin with the model's per-part material, then DrawGrid.
        camera.DrawModel(_model!, Transform.Identity);
        camera.DrawMesh(_gridMesh!, Transform.Identity, _gridShader!);
    }

    private void ObserveCompletedFrame(EngineContext context) {
        if (_sawDrawnFrame) {
            return;
        }

        RenderStats stats = context.Stats;
        if (stats.DrawCalls > 0 && stats.Instances > 0) {
            _sawDrawnFrame = true;
            _logger.LogInformation(
                "Castle gate: completed frame {FrameIndex} reported {DrawCalls} draw call(s) and {Instances} instance(s).",
                stats.FrameIndex,
                stats.DrawCalls,
                stats.Instances);
        }
    }

    private void LogModelMetadata(Model model) {
        _logger.LogInformation(
            "Model {Path}: {PartCount} part(s), bounds {Min}..{Max}.",
            MODEL_PATH,
            model.Parts.Count,
            model.Bounds.Min,
            model.Bounds.Max);

        foreach (ModelPart part in model.Parts) {
            Mesh mesh = part.Mesh;
            _logger.LogInformation(
                "Part {Name}: mesh {MeshPath}, {VertexCount} vertex/vertices, {IndexCount} index/indices, bounds {Min}..{Max}, shader {ShaderPath}.",
                part.Name,
                mesh.Path,
                mesh.VertexCount,
                mesh.IndexCount,
                part.Bounds.Min,
                part.Bounds.Max,
                part.Shader.Path);
        }
    }

    private void LogTextureMetadata(Texture texture) {
        _logger.LogInformation(
            "Texture {Path}: {Width}x{Height}, {MipLevels} mip level(s), format {Format}, sRGB {IsSrgb}, sampler {Filter}/{Wrap}.",
            texture.Path,
            texture.Width,
            texture.Height,
            texture.MipLevels,
            texture.Format,
            texture.IsSrgb,
            texture.Sampler.Filter,
            texture.Sampler.Wrap);
    }

    private void LogShaderMetadata(Shader shader) {
        RenderState state = shader.DefaultRenderState;
        _logger.LogInformation(
            "Shader {Path}: stages {Stages}, {UniformCount} uniform(s), {TextureBindingCount} texture binding(s), default state {Blend}/{Cull}/{DepthTest}, depth write {DepthWrite}.",
            shader.Path,
            shader.Stages,
            shader.Uniforms.Count,
            shader.Textures.Count,
            state.Blend,
            state.Cull,
            state.DepthTest,
            state.DepthWrite);
    }

    private static string FormatVulkanVersion(uint packedVersion) {
        uint major = packedVersion >> VULKAN_MAJOR_SHIFT;
        uint minor = (packedVersion >> VULKAN_MINOR_SHIFT) & VULKAN_MINOR_MASK;
        uint patch = packedVersion & VULKAN_PATCH_MASK;
        return string.Create(CultureInfo.InvariantCulture, $"{major}.{minor}.{patch}");
    }

    private static void LogRenderStats(ILogger logger, RenderStats stats, string label) {
        logger.LogInformation(
            "{Label} render stats: frame {FrameIndex}, {DrawCalls} draw call(s), {Instances} instance(s), {Triangles} triangle(s), {CulledInstances} culled instance(s), {PipelineCount} pipeline(s), {TextureCount} texture(s), {SampleCount}x MSAA, HDR {HdrOutput}, CPU {CpuFrameMilliseconds:0.###} ms, {FramesInFlight} frame(s) in flight, {RecoveryCount} recoveries.",
            label,
            stats.FrameIndex,
            stats.DrawCalls,
            stats.Instances,
            stats.Triangles,
            stats.CulledInstances,
            stats.PipelineCount,
            stats.TextureCount,
            stats.SampleCount,
            stats.HdrOutput,
            stats.CpuFrameMilliseconds,
            stats.FramesInFlight,
            stats.RecoveryCount);
    }
}
