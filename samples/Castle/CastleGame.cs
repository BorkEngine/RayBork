using System.Globalization;
using System.Numerics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RayBork;

namespace Castle;

/// <summary>
/// The M4 Castle sample: loads the castle model, its stone texture, and its custom shader from the
/// baked pack, then draws the model with an orbiting perspective camera every tick.
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
/// The camera is created and disposed inside each <see cref="Tick"/>; the model, shader, and texture
/// are loaded once in <see cref="Setup"/> and disposed exactly once in <see cref="Shutdown"/>.
/// </para>
/// </remarks>
internal sealed class CastleGame : IGame {
    private const int STATS_LOG_INTERVAL = 30;
    private const float FIELD_OF_VIEW_DEGREES = 45f;
    private const float NEAR_PLANE = 0.01f;
    private const float ORBIT_RADIUS = 13f;
    private const float ORBIT_HEIGHT = 6.5f;
    private const float ORBIT_DEGREES_PER_SECOND = 9f;
    private const float MODEL_DEGREES_PER_SECOND = 4f;
    private const float CAMERA_TARGET_HEIGHT = 2.6f;
    private const float DEGREES_TO_RADIANS = MathF.PI / 180f;
    private const int VULKAN_MAJOR_SHIFT = 22;
    private const int VULKAN_MINOR_SHIFT = 12;
    private const uint VULKAN_MINOR_MASK = 0x3FFu;
    private const uint VULKAN_PATCH_MASK = 0xFFFu;

    private static readonly Color _skyColor = new(0.055f, 0.065f, 0.09f, 1f);
    private static readonly Vector4 _castleTint = new(1f, 0.97f, 0.92f, 1f);

    private readonly int _frameLimit;

    private EngineContext? _context;
    private ILogger _logger = NullLogger.Instance;
    private Model? _model;
    private Texture? _texture;
    private Shader? _shader;
    private long _lastFrameCount;
    private bool _sawDrawnFrame;
    private string _exitReason = "no ticks were run";

    /// <summary>
    /// Initializes the game.
    /// </summary>
    /// <param name="frameLimit">The number of ticks to run, or <c>0</c> to run until close.</param>
    internal CastleGame(int frameLimit) => _frameLimit = frameLimit;

    /// <inheritdoc/>
    public async Task Setup(EngineContext context) {
        _context = context;
        _logger = context.LoggerFactory.CreateLogger("Castle");

        _logger.LogInformation(
            "RayBork {EngineVersion} M4 Castle sample starting.",
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

        _model = await context.Assets.Load<Model>("/models/castle");
        _texture = await context.Assets.Load<Texture>("/textures/stone");
        _shader = await context.Assets.Load<Shader>("/shaders/castle");

        LogModelMetadata(_model);
        LogTextureMetadata(_texture);
        LogShaderMetadata(_shader);

        _shader.SetUniform("mainTexture", _texture);
        if (HasUniform(_shader, "uTint")) {
            _shader.SetUniform("uTint", _castleTint);
            _logger.LogInformation("Uniform uTint: {Tint}.", _castleTint);
        }

        _logger.LogInformation(
            "Controls: Escape exits; the camera orbits the castle and the model turns automatically.");
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

        float elapsedSeconds = (float)context.Time;
        RecordCastlePass(context, elapsedSeconds);
        ObserveCompletedFrame(context);

        if (context.FrameCount % STATS_LOG_INTERVAL == 0) {
            logger.LogInformation(
                "Tick {FrameCount}: time {Time:0.000}s, fps {Fps:0.0}, delta {DeltaTime:0.0000}s.",
                context.FrameCount,
                context.Time,
                context.Fps,
                context.DeltaTime);
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

        // The model owns one reference to every distinct part mesh and shader. Dispose the model
        // first, then this sample's own shader and texture references, exactly once each; the catalog
        // releases the GPU resources on the render thread and a recorded frame keeps them alive.
        _model?.Dispose();
        _model = null;
        _shader?.Dispose();
        _shader = null;
        _texture?.Dispose();
        _texture = null;

        if (!_sawDrawnFrame) {
            throw new InvalidOperationException(
                "The Castle gate failed: no completed frame reported both draw calls and instances, "
                    + "so the castle scene is not visible. Check that the pack, camera, and scene "
                    + "target are valid and that the renderer completes a frame.");
        }

        return Task.CompletedTask;
    }

    private void RecordCastlePass(EngineContext context, float elapsedSeconds) {
        float orbitAngle = elapsedSeconds * ORBIT_DEGREES_PER_SECOND * DEGREES_TO_RADIANS;
        Vector3 cameraPosition = new(
            MathF.Sin(orbitAngle) * ORBIT_RADIUS,
            ORBIT_HEIGHT,
            MathF.Cos(orbitAngle) * ORBIT_RADIUS);
        Vector3 target = new(0f, CAMERA_TARGET_HEIGHT, 0f);
        Transform cameraTransform = new(
            cameraPosition,
            Quaternion.LookRotation(target - cameraPosition, Vector3.UnitY),
            Vector3.One);

        // The camera is a frame-scoped recorder; the using scope disposes it before Tick returns.
        using (Camera camera = context.CreateCamera(
            cameraTransform,
            CameraProjection.Perspective,
            fieldOfView: FIELD_OF_VIEW_DEGREES,
            clearMode: ClearMode.ColorAndDepth,
            clearColor: _skyColor,
            near: NEAR_PLANE,
            far: float.PositiveInfinity)) {
            Transform modelTransform = new(
                Vector3.Zero,
                Quaternion.Euler(0f, elapsedSeconds * MODEL_DEGREES_PER_SECOND, 0f),
                Vector3.One);
            camera.DrawModel(_model!, modelTransform, _shader!);
        }
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
            model.Path,
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

    private static bool HasUniform(Shader shader, string name) {
        foreach (ShaderUniform uniform in shader.Uniforms) {
            if (string.Equals(uniform.Name, name, StringComparison.Ordinal)) {
                return true;
            }
        }

        return false;
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
