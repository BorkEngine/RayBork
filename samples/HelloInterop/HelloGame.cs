using System.Globalization;
using System.Numerics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RayBork;
using SDL3;

namespace HelloInterop;

internal sealed class HelloGame : IGame {
    private const int TICK_LOG_INTERVAL = 60;
    private const int VULKAN_MAJOR_SHIFT = 22;
    private const int VULKAN_MINOR_SHIFT = 12;
    private const uint VULKAN_MINOR_MASK = 0x3FFu;
    private const uint VULKAN_PATCH_MASK = 0xFFFu;

    private static readonly Key[] _loggedKeys = [
        Key.Space,
        Key.Enter,
        Key.Tab,
        Key.F1,
        Key.F11,
        Key.F12,
    ];

    private static readonly MouseButton[] _loggedMouseButtons = [
        MouseButton.Left,
        MouseButton.Middle,
        MouseButton.Right,
        MouseButton.X1,
        MouseButton.X2,
    ];

    private static readonly GamepadButton[] _loggedGamepadButtons = [
        GamepadButton.South,
        GamepadButton.East,
        GamepadButton.West,
        GamepadButton.North,
        GamepadButton.LeftShoulder,
        GamepadButton.RightShoulder,
        GamepadButton.Start,
    ];

    private readonly int _frameLimit;
    private readonly TonemappingMode _tonemapping;
    private readonly float _exposure;
    private readonly string? _assetsPackPath;
    private readonly Dictionary<int, string> _gamepads = [];

    private EngineContext? _context;
    private ILogger _logger = NullLogger.Instance;
    private Texture? _texture;
    private Mesh? _mesh;
    private Shader? _shader;
    private Mesh? _proceduralMesh;
    private Texture? _proceduralTexture;
    private bool _lastFocused;
    private bool _lastMinimized;
    private WindowState _lastWindowState;
    private long _lastFrameCount;
    private double _lastTime;
    private double _lastFps;
    private int _lastWindowWidth;
    private int _lastWindowHeight;
    private int _lastRenderWidth;
    private int _lastRenderHeight;
    private float _lastDpiScale;
    private string _exitReason = "no ticks were run";

    internal HelloGame(
        int frameLimit,
        TonemappingMode tonemapping,
        float exposure,
        string? assetsPackPath) {
        _frameLimit = frameLimit;
        _tonemapping = tonemapping;
        _exposure = exposure;
        _assetsPackPath = assetsPackPath;
    }

    public async Task Setup(EngineContext context) {
        _context = context;
        _logger = context.LoggerFactory.CreateLogger("HelloInterop");
        IWindow window = context.Window;

        _logger.LogInformation("RayBork {EngineVersion} M1-M4 diagnostic host starting.", EngineVersion.Current);

        int sdlVersion = SDL.GetVersion();
        int major = sdlVersion / 1_000_000;
        int minor = (sdlVersion / 1_000) % 1_000;
        int patch = sdlVersion % 1_000;
        _logger.LogInformation(
            "SDL {Major}.{Minor}.{Patch}, revision {Revision}, video driver '{VideoDriver}'.",
            major,
            minor,
            patch,
            SDL.GetRevision() ?? "unknown",
            SDL.GetCurrentVideoDriver() ?? "unknown");

        _logger.LogInformation(
            "Window '{Title}': {Width}x{Height}, render {RenderWidth}x{RenderHeight}, DPI scale {DpiScale:0.###}, state {State}, resizable {Resizable}.",
            window.Title,
            window.Width,
            window.Height,
            window.RenderWidth,
            window.RenderHeight,
            window.DpiScale,
            window.State,
            window.IsResizable);

        var monitors = window.Monitors;
        _logger.LogInformation("Monitors: {MonitorCount}.", monitors.Count);
        foreach (var monitor in monitors) {
            _logger.LogInformation(
                "Monitor {Index} '{Name}': bounds {Bounds}, work area {WorkArea}, DPI scale {DpiScale:0.###}, refresh {RefreshRate} Hz.",
                monitor.Index,
                monitor.Name,
                monitor.Bounds,
                monitor.WorkArea,
                monitor.DpiScale,
                monitor.RefreshRate);
        }

        var currentMonitor = window.CurrentMonitor;
        _logger.LogInformation(
            "Current monitor: {Index} '{Name}', DPI scale {DpiScale:0.###}.",
            currentMonitor.Index,
            currentMonitor.Name,
            currentMonitor.DpiScale);

        EngineInfo info = context.Info;
        _logger.LogInformation(
            "Engine {EngineName} {EngineVersion}: renderer {RendererName}, GPU '{GpuName}', Vulkan {VulkanApiVersion}, validation {ValidationEnabled}.",
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
            "Requested scene: exposure {Exposure:0.###} EV, tone mapping {Tonemapping}.",
            _exposure,
            _tonemapping);

        _logger.LogInformation("Tick thread: {ThreadName}.", Thread.CurrentThread.Name ?? "unnamed");
        _logger.LogInformation(
            "Controls: Escape exits; Space, Enter, Tab, F1, F11, and F12 are logged; mouse buttons, the wheel, and gamepad buttons are logged.");
        _logger.LogInformation(
            "Frame limit: {FrameLimit}.",
            _frameLimit == 0 ? "none (run until close)" : _frameLimit.ToString(CultureInfo.InvariantCulture));

        _lastFocused = window.IsFocused;
        _lastMinimized = window.IsMinimized;
        _lastWindowState = window.State;
        _lastWindowWidth = window.Width;
        _lastWindowHeight = window.Height;
        _lastRenderWidth = window.RenderWidth;
        _lastRenderHeight = window.RenderHeight;
        _lastDpiScale = window.DpiScale;

        await LoadAssetsAsync(context);
        await BuildProceduralAssetsAsync(context);
    }

    private async Task BuildProceduralAssetsAsync(EngineContext context) {
        MeshBuilder meshBuilder = context.CreateMeshBuilder();
        meshBuilder.SetVertices([
            new Vertex(new Vector3(-0.5f, -0.5f, 0f), uv: new Vector2(0f, 1f)),
            new Vertex(new Vector3(0.5f, -0.5f, 0f), uv: new Vector2(1f, 1f)),
            new Vertex(new Vector3(0.5f, 0.5f, 0f), uv: new Vector2(1f, 0f)),
            new Vertex(new Vector3(-0.5f, 0.5f, 0f), uv: new Vector2(0f, 0f)),
        ]);
        meshBuilder.SetIndices([0u, 1u, 2u, 0u, 2u, 3u]);
        meshBuilder.ComputeNormals();
        meshBuilder.ComputeTangents();
        _proceduralMesh = await meshBuilder.BuildAsync();
        _logger.LogInformation(
            "Procedural mesh {Path}: {VertexCount} vertex/vertices, {IndexCount} index/indices, bounds {Min}..{Max}, procedural {IsProcedural}.",
            _proceduralMesh.Path,
            _proceduralMesh.VertexCount,
            _proceduralMesh.IndexCount,
            _proceduralMesh.Bounds.Min,
            _proceduralMesh.Bounds.Max,
            _proceduralMesh.IsProcedural);

        TextureBuilder textureBuilder = context.CreateTextureBuilder(
            2,
            2,
            TextureFormat.Rgba8Srgb,
            generateMips: true);
        textureBuilder.SetPixels([
            Color.Red,
            Color.Green,
            Color.Blue,
            Color.White,
        ]);
        _proceduralTexture = await textureBuilder.BuildAsync();
        _logger.LogInformation(
            "Procedural texture {Path}: {Width}x{Height}, {MipLevels} mip level(s), format {Format}, sRGB {IsSrgb}, procedural {IsProcedural}.",
            _proceduralTexture.Path,
            _proceduralTexture.Width,
            _proceduralTexture.Height,
            _proceduralTexture.MipLevels,
            _proceduralTexture.Format,
            _proceduralTexture.IsSrgb,
            _proceduralTexture.IsProcedural);
    }

    private async Task LoadAssetsAsync(EngineContext context) {
        if (_assetsPackPath is null) {
            return;
        }

        _logger.LogInformation("Assets pack: {PackPath}.", _assetsPackPath);

        _texture = await context.Assets.Load<Texture>("/textures/circle");
        _mesh = await context.Assets.Load<Mesh>("/models/box/body");
        _shader = await context.Assets.Load<Shader>("/shaders/simple");
        HelloSettings settings = await context.Assets.Load<HelloSettings>("/data/hello");

        Texture texture = _texture;
        _logger.LogInformation(
            "Texture {Path}: {Width}x{Height}, {MipLevels} mip level(s), format {Format}, sRGB {IsSrgb}, sampler {Filter}/{Wrap}, max anisotropy {MaxAnisotropy:0.###}.",
            texture.Path,
            texture.Width,
            texture.Height,
            texture.MipLevels,
            texture.Format,
            texture.IsSrgb,
            texture.Sampler.Filter,
            texture.Sampler.Wrap,
            texture.Sampler.MaxAnisotropy);

        Mesh mesh = _mesh;
        _logger.LogInformation(
            "Mesh {Path}: {VertexCount} vertex/vertices, {IndexCount} index/indices, topology {Topology}, bounds {Min}..{Max}.",
            mesh.Path,
            mesh.VertexCount,
            mesh.IndexCount,
            mesh.Topology,
            mesh.Bounds.Min,
            mesh.Bounds.Max);

        Shader shader = _shader;
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

        _logger.LogInformation(
            "Custom data {Path}: name '{Name}', difficulty {Difficulty}.",
            "/data/hello",
            settings.Name,
            settings.Difficulty);
    }

    public void Tick(EngineContext context) {
        ILogger logger = _logger;
        IInput input = context.Input;
        IWindow window = context.Window;
        string? exitReason = null;

        if (input.IsKeyPressed(Key.Escape)) {
            exitReason = "Escape";
            logger.LogInformation("Escape pressed; requesting exit.");
            context.RequestExit();
        }

        foreach (Key key in _loggedKeys) {
            if (input.IsKeyPressed(key)) {
                logger.LogInformation("Key pressed: {Key}.", key);
            }
        }

        string text = input.TextInput;
        if (text.Length > 0) {
            logger.LogInformation("Text input: '{Text}'.", text);
        }

        foreach (MouseButton button in _loggedMouseButtons) {
            if (input.IsMouseButtonPressed(button)) {
                logger.LogInformation(
                    "Mouse {Button} pressed at ({X:0.#}, {Y:0.#}).",
                    button,
                    input.MousePosition.X,
                    input.MousePosition.Y);
            }

            if (input.IsMouseButtonReleased(button)) {
                logger.LogInformation("Mouse {Button} released.", button);
            }
        }

        if (input.MouseWheel != 0f) {
            logger.LogInformation("Mouse wheel: {Wheel:0.###}.", input.MouseWheel);
        }

        if (window.WasResized) {
            logger.LogInformation(
                "Window resized: {Width}x{Height}, render {RenderWidth}x{RenderHeight}, DPI scale {DpiScale:0.###}.",
                window.Width,
                window.Height,
                window.RenderWidth,
                window.RenderHeight,
                window.DpiScale);
        }

        bool focused = window.IsFocused;
        if (focused != _lastFocused) {
            logger.LogInformation(focused ? "Window focus gained." : "Window focus lost.");
            _lastFocused = focused;
        }

        bool minimized = window.IsMinimized;
        if (minimized != _lastMinimized) {
            logger.LogInformation(minimized ? "Window minimized." : "Window restored from minimized state.");
        }

        WindowState state = window.State;
        if (state != _lastWindowState && minimized == _lastMinimized) {
            logger.LogInformation("Window state changed: {PreviousState} -> {State}.", _lastWindowState, state);
        }

        _lastMinimized = minimized;
        _lastWindowState = state;

        LogGamepads(input.Gamepads, logger);

        if (context.FrameCount % TICK_LOG_INTERVAL == 0) {
            logger.LogInformation(
                "Tick {FrameCount}: time {Time:0.000}s, fps {Fps:0.0}, delta {DeltaTime:0.0000}s.",
                context.FrameCount,
                context.Time,
                context.Fps,
                context.DeltaTime);
            LogRenderStats(logger, context.Stats, "Tick");
        }

        _lastFrameCount = context.FrameCount;
        _lastTime = context.Time;
        _lastFps = context.Fps;
        _lastWindowWidth = window.Width;
        _lastWindowHeight = window.Height;
        _lastRenderWidth = window.RenderWidth;
        _lastRenderHeight = window.RenderHeight;
        _lastDpiScale = window.DpiScale;

        if (exitReason is null && _frameLimit > 0 && context.FrameCount >= _frameLimit) {
            exitReason = $"frame limit ({_frameLimit})";
            logger.LogInformation("Frame limit reached; requesting exit.");
            context.RequestExit();
        }

        _exitReason = exitReason ?? "window close or engine request";
    }

    public Task Shutdown() {
        // Shutdown has no context argument. The context is stable for the app lifetime and Stats is
        // safe to read from any thread; the snapshot describes the last frame the render thread
        // completed, which can lag the final tick by one frame.
        if (_context is not null) {
            LogRenderStats(_logger, _context.Stats, "Final");
        }

        _logger.LogInformation("Elapsed runtime: {Elapsed:0.000}s.", _lastTime);
        _logger.LogInformation(
            "HelloInterop stopped after {FrameCount} tick(s): time {Time:0.000}s, fps {Fps:0.0}, last window {Width}x{Height} (render {RenderWidth}x{RenderHeight}, DPI scale {DpiScale:0.###}), exit {ExitReason}.",
            _lastFrameCount,
            _lastTime,
            _lastFps,
            _lastWindowWidth,
            _lastWindowHeight,
            _lastRenderWidth,
            _lastRenderHeight,
            _lastDpiScale,
            _exitReason);

        // Shutdown runs on the tick thread, which owns the loaded engine assets. Dispose exactly once
        // per successful load; the catalog releases the GPU resource on the render thread.
        _texture?.Dispose();
        _mesh?.Dispose();
        _shader?.Dispose();
        _proceduralMesh?.Dispose();
        _proceduralTexture?.Dispose();

        return Task.CompletedTask;
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

    private void LogGamepads(IReadOnlyList<IGamepad> gamepads, ILogger logger) {
        HashSet<int> connected = [];
        foreach (IGamepad gamepad in gamepads) {
            connected.Add(gamepad.InstanceId);
            if (!_gamepads.ContainsKey(gamepad.InstanceId)) {
                _gamepads.Add(gamepad.InstanceId, gamepad.Name);
                logger.LogInformation(
                    "Gamepad connected: '{Name}' (index {Index}, instance {InstanceId}).",
                    gamepad.Name,
                    gamepad.Index,
                    gamepad.InstanceId);
            }

            foreach (GamepadButton button in _loggedGamepadButtons) {
                if (gamepad.IsButtonPressed(button)) {
                    logger.LogInformation(
                        "Gamepad '{Name}' (index {Index}) button pressed: {Button}.",
                        gamepad.Name,
                        gamepad.Index,
                        button);
                }
            }
        }

        List<int>? disconnected = null;
        foreach (var gamepad in _gamepads) {
            if (!connected.Contains(gamepad.Key)) {
                disconnected ??= [];
                disconnected.Add(gamepad.Key);
            }
        }

        if (disconnected is null) {
            return;
        }

        foreach (int instanceId in disconnected) {
            logger.LogInformation(
                "Gamepad disconnected: '{Name}' (instance {InstanceId}).",
                _gamepads[instanceId],
                instanceId);
            _gamepads.Remove(instanceId);
        }
    }
}
