using System.Numerics;
using RayBork;

namespace RenderTarget;

/// <summary>
/// A procedural scene that renders an offscreen target, samples it on a rotating monitor in a later
/// camera pass, toggles a split-screen view and a custom vignette post-process, and reads the target
/// back for a screenshot.
/// </summary>
/// <remarks>
/// <para>
/// The sample owns one app-lifetime <see cref="RenderTexture"/> and one app-lifetime
/// <see cref="PostProcessStack"/>. Every tick records an offscreen camera into the target, then one
/// scene camera that samples <see cref="RenderTexture.ColorTexture"/> on the monitor, then a HUD
/// camera. When split-screen is on, the two scene cameras draw side by side instead of the single
/// scene camera; they do not take the post-process stack because M8 documents the effect source as
/// the full target, including content outside a viewport.
/// </para>
/// <para>
/// The main camera is recreated every tick so the effect toggle applies to the next recorded camera.
/// The stack snapshot freezes the effect order, the enabled state, and the shader cursor at camera
/// creation, so toggling it never changes a frame that was already recorded.
/// </para>
/// </remarks>
internal sealed class RenderTargetGame : IGame {
    private const string EFFECT_PATH = "/shaders/vignette";
    private const string SCREENSHOT_FILE_NAME = "rendertarget-readback.ppm";

    private const float FIELD_OF_VIEW_DEGREES = 50f;
    private const float NEAR_PLANE = 0.05f;
    private const float FAR_PLANE = 100f;
    private const float SCENE_RADIANS_PER_SECOND = 0.7f;

    private const int TARGET_WIDTH = 640;
    private const int TARGET_HEIGHT = 360;
    private const int TARGET_SAMPLE_COUNT = 4;

    private const float HUD_PLANE_DISTANCE = 1f;
    private const float HUD_TEXT_PIXEL_HEIGHT = 17f;
    private const float HUD_MARGIN_PIXELS = 10f;
    private const float HUD_PADDING_PIXELS = 8f;
    private const float HUD_PANEL_HEIGHT = 92f;

    // A split view needs two non-empty viewports; a 1-pixel-wide window cannot hold them.
    private const int MIN_SPLIT_RENDER_WIDTH = 2;

    private static readonly Color _background = Color.FromSrgbBytes(14, 18, 28);
    private static readonly Color _offscreenBackground = Color.FromSrgbBytes(10, 14, 24);
    private static readonly Color _splitLeftBackground = Color.FromSrgbBytes(54, 28, 20);
    private static readonly Color _splitRightBackground = Color.FromSrgbBytes(10, 28, 42);
    private static readonly Color _hudPanel = Color.FromSrgbBytes(6, 8, 12, 220);
    private static readonly Color _hudText = Color.FromSrgbBytes(236, 240, 248);
    private static readonly Color _bezelColor = Color.FromSrgbBytes(30, 34, 42);
    private static readonly Color _standColor = Color.FromSrgbBytes(74, 78, 88);
    private static readonly Color _gridColor = Color.Gray4;

    private static readonly Color[] _ringColors = [
        Color.Coral,
        Color.GoldenRod,
        Color.MediumSeaGreen,
        Color.CornflowerBlue,
        Color.MediumOrchid,
        Color.Turquoise,
    ];

    private static readonly Color[] _offscreenColors = [
        Color.Orange,
        Color.RoyalBlue,
        Color.SeaGreen,
        Color.Crimson,
    ];

    private static readonly Vector3 _lookTarget = new(0f, 1.9f, 0f);
    private static readonly Vector3 _offscreenLookTarget = new(0f, 1.0f, 0f);
    private static readonly Transform _hudTransform = new(new Vector3(0f, 0f, 1f));

    private readonly int _frameLimit;
    private readonly PostProcessStack _postEffects = new();

    private RenderTexture? _target;
    private Shader? _effectShader;
    private float _sceneAngle;
    private float _spinDirection = 1f;
    private bool _splitScreen;
    private int _frameCount;
    private string _readbackStatus =
        $"F2 reads the offscreen target and saves {SCREENSHOT_FILE_NAME}";

    /// <summary>
    /// Creates the sample.
    /// </summary>
    /// <param name="frameLimit">The number of ticks to run, or zero to run until the window closes.</param>
    internal RenderTargetGame(int frameLimit) {
        _frameLimit = frameLimit;
    }

    /// <inheritdoc/>
    public async Task Setup(EngineContext context) {
        // The target is created while Setup can await the render thread's allocation. It is an HDR
        // target with depth and 4x MSAA, so the offscreen scene renders exactly like the scene target.
        _target = await context.CreateRenderTextureAsync(new RenderTextureDescription(
            TARGET_WIDTH,
            TARGET_HEIGHT,
            TextureFormat.Rgba16Float,
            Depth: true,
            SampleCount: TARGET_SAMPLE_COUNT));

        // The effect shader is an ordinary baked sample shader that declares the reserved sourceColor
        // binding. The stack and the effect own no engine asset, so this sample disposes the shader.
        _effectShader = await context.Assets.Load<Shader>(EFFECT_PATH);
        _effectShader.SetUniform("uTint", new Vector4(1.0f, 0.95f, 0.87f, 1.0f));
        _effectShader.SetUniform("uVignette", 0.35f);
        _postEffects.Add(new ShaderPostProcessEffect(_effectShader));
    }

    /// <inheritdoc/>
    public void Tick(EngineContext context) {
        HandleInput(context);

        _sceneAngle += _spinDirection * (float)context.DeltaTime * SCENE_RADIANS_PER_SECOND;
        if (_sceneAngle >= MathF.Tau) {
            _sceneAngle -= MathF.Tau;
        } else if (_sceneAngle <= -MathF.Tau) {
            _sceneAngle += MathF.Tau;
        }

        RenderFrame(context);

        _frameCount++;
        if (_frameLimit > 0 && _frameCount >= _frameLimit) {
            context.RequestExit();
        }
    }

    /// <inheritdoc/>
    public Task Shutdown() {
        // The stack, the effect, and the target are app-lifetime objects owned by this sample. The
        // effect never owns the shader, so the sample disposes it separately.
        _postEffects.Clear();
        _effectShader?.Dispose();
        _effectShader = null;
        _target?.Dispose();
        _target = null;
        return Task.CompletedTask;
    }

    private void HandleInput(EngineContext context) {
        IInput input = context.Input;
        if (input.IsKeyPressed(Key.Escape)) {
            context.RequestExit();
        }

        if (input.IsKeyPressed(Key.S)) {
            _splitScreen = !_splitScreen;
        }

        if (input.IsKeyPressed(Key.E)) {
            // The stack is snapshotted when the next camera is created; the already recorded frame
            // keeps the value it captured.
            _postEffects.Enabled = !_postEffects.Enabled;
        }

        if (input.IsKeyPressed(Key.R)) {
            // Reset the animation to its start and reverse it so the reset is visible.
            _sceneAngle = 0f;
            _spinDirection = -_spinDirection;
        }

        if (input.IsKeyPressed(Key.F2)) {
            CaptureReadback();
        }
    }

    private void RenderFrame(EngineContext context) {
        // The offscreen pass must be recorded first: a later camera in the same tick samples the
        // target's ColorTexture after the renderer inserted the write-to-sample barrier.
        using (Camera offscreen = context.CreateCamera(
            OffscreenCameraTransform(),
            projection: CameraProjection.Perspective,
            fieldOfView: FIELD_OF_VIEW_DEGREES,
            clearMode: ClearMode.ColorAndDepth,
            clearColor: _offscreenBackground,
            near: NEAR_PLANE,
            far: FAR_PLANE,
            target: _target)) {
            DrawOffscreenScene(offscreen);
        }

        if (_splitScreen && context.Window.RenderWidth >= MIN_SPLIT_RENDER_WIDTH) {
            DrawSplitScreen(context);
        } else {
            // The main scene samples the offscreen target on the monitor and applies the custom
            // post-process stack. The stack is passed every tick so the E toggle affects this camera.
            using Camera scene = context.CreateCamera(
                MainCameraTransform(),
                projection: CameraProjection.Perspective,
                fieldOfView: FIELD_OF_VIEW_DEGREES,
                clearMode: ClearMode.ColorAndDepth,
                clearColor: _background,
                near: NEAR_PLANE,
                far: FAR_PLANE,
                postProcess: _postEffects);
            DrawScene(scene);
        }

        // The HUD is an ordinary later camera. ClearMode.Depth loads the scene color and clears depth,
        // so the status panel is never occluded by the scene, and it has no post-process stack.
        using Camera hud = context.CreateCamera(
            _hudTransform,
            projection: CameraProjection.Orthographic,
            orthographicHeight: context.Window.RenderHeight,
            clearMode: ClearMode.Depth,
            near: 0.01f,
            far: 10f);
        DrawHud(hud, context);
    }

    private void DrawSplitScreen(EngineContext context) {
        // Each viewport is its own render pass with its own clear and its own frame block. The two
        // halves cover the render width exactly, including an odd pixel.
        int renderWidth = context.Window.RenderWidth;
        int renderHeight = context.Window.RenderHeight;
        int leftWidth = renderWidth / 2;

        using (Camera left = context.CreateCamera(
            LeftSplitCameraTransform(),
            projection: CameraProjection.Perspective,
            fieldOfView: FIELD_OF_VIEW_DEGREES,
            clearMode: ClearMode.ColorAndDepth,
            clearColor: _splitLeftBackground,
            near: NEAR_PLANE,
            far: FAR_PLANE,
            viewport: new Rect(0f, 0f, leftWidth, renderHeight))) {
            DrawScene(left);
        }

        using Camera right = context.CreateCamera(
            RightSplitCameraTransform(),
            projection: CameraProjection.Perspective,
            fieldOfView: FIELD_OF_VIEW_DEGREES,
            clearMode: ClearMode.ColorAndDepth,
            clearColor: _splitRightBackground,
            near: NEAR_PLANE,
            far: FAR_PLANE,
            viewport: new Rect(leftWidth, 0f, renderWidth - leftWidth, renderHeight));
        DrawScene(right);
    }

    private void DrawOffscreenScene(Camera camera) {
        camera.DrawGrid(slices: 10, spacing: 1f, _gridColor);

        // A central cube keeps the readback center on geometry instead of the background.
        camera.DrawCube(new Vector3(0f, 0.75f, 0f), 1.2f, 1.2f, 1.2f, Color.GoldenRod);
        camera.DrawSphere(new Vector3(0f, 2.2f, 0f), 0.4f, Color.SkyBlue);

        // The ring turns the other way, so the monitor is visibly a second live render.
        for (int index = 0; index < _offscreenColors.Length; index++) {
            float angle = (-_sceneAngle * 1.4f)
                + (index * MathF.Tau / _offscreenColors.Length);
            Vector3 position = new(
                MathF.Cos(angle) * 1.9f,
                0.5f,
                MathF.Sin(angle) * 1.9f);
            camera.DrawCube(position, 0.7f, 0.7f, 0.7f, _offscreenColors[index]);
        }
    }

    private void DrawScene(Camera camera) {
        camera.DrawGrid(slices: 20, spacing: 1f, _gridColor);

        for (int index = 0; index < _ringColors.Length; index++) {
            float angle = (_sceneAngle * 0.8f)
                + (index * MathF.Tau / _ringColors.Length);
            Vector3 position = new(
                MathF.Cos(angle) * 3.3f,
                0.6f,
                MathF.Sin(angle) * 3.3f);
            Transform cube = new(
                position,
                Quaternion.Euler(0f, (angle * (180f / MathF.PI)) + 20f, 0f),
                Vector3.One);
            camera.DrawCube(cube, new Vector3(1.15f), _ringColors[index]);
        }

        camera.DrawCylinder(new Vector3(0f, 0f, 0f), 0.3f, 1.5f, _standColor);

        // The monitor sways around the vertical axis but never turns its back to the main camera, so
        // the offscreen color stays readable. The bezel is a slightly larger solid quad behind the
        // textured quad; a small offset along the monitor normal avoids z-fighting.
        float yaw = MathF.Sin(_sceneAngle * 0.8f) * 0.55f;
        Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
        Vector3 center = new(0f, 2.35f, 0f);
        Vector3 normal = Vector3.Transform(Vector3.UnitZ, rotation);

        camera.DrawQuad(
            new Transform(center - (normal * 0.05f), rotation, Vector3.One),
            new Vector2(2.95f, 1.85f),
            _bezelColor);
        camera.DrawQuad(
            new Transform(center, rotation, Vector3.One),
            new Vector2(2.65f, 1.55f),
            _target!.ColorTexture);
    }

    private void DrawHud(Camera camera, EngineContext context) {
        int renderWidth = context.Window.RenderWidth;
        float panelWidth = MathF.Max(renderWidth - (2f * HUD_MARGIN_PIXELS), 1f);
        float panelCenterX = HUD_MARGIN_PIXELS + (panelWidth * 0.5f);
        float panelCenterY = HUD_MARGIN_PIXELS + (HUD_PANEL_HEIGHT * 0.5f);

        Transform panel = camera.ScreenToWorldTransform(
            new Vector2(panelCenterX, panelCenterY),
            HUD_PLANE_DISTANCE);
        camera.DrawQuad(
            panel,
            new Vector2(panelWidth, HUD_PANEL_HEIGHT),
            _hudPanel,
            RenderState.AlphaBlend);

        Transform text = camera.ScreenToWorldTransform(
            new Vector2(
                HUD_MARGIN_PIXELS + HUD_PADDING_PIXELS,
                HUD_MARGIN_PIXELS + HUD_PADDING_PIXELS),
            HUD_PLANE_DISTANCE);
        camera.DrawText(
            context.Defaults.DefaultMonoFont,
            BuildStatusText(context),
            text,
            HUD_TEXT_PIXEL_HEIGHT,
            _hudText);
    }

    private string BuildStatusText(EngineContext context) {
        bool splitScreen = _splitScreen && context.Window.RenderWidth >= MIN_SPLIT_RENDER_WIDTH;
        string effect = _postEffects.Enabled ? "on" : "off";
        string controls = FormattableString.Invariant(
            $"S split-screen ({(splitScreen ? "on" : "off")})   E vignette ({effect}, single view)   F2 readback   R reset   Esc exit");
        string scene = splitScreen
            ? FormattableString.Invariant(
                $"scene split view, left/right cameras (no post effect)   target {_target!.Width}x{_target.Height} rgba16-float {_target.SampleCount}x")
            : FormattableString.Invariant(
                $"scene single view   post vignette {effect}   target {_target!.Width}x{_target.Height} rgba16-float {_target.SampleCount}x");
        return controls + "\n" + scene + "\n" + _readbackStatus;
    }

    /// <summary>
    /// Reads the offscreen target once and saves the display-referred pixels as a binary PPM.
    /// </summary>
    /// <remarks>
    /// Both readbacks stall the render thread while they wait for the target's last-use fence, so this
    /// runs only on the F2 press and reports the last completed frame that wrote or sampled the
    /// target. The target keeps its state and a later frame samples or writes it as usual.
    /// </remarks>
    private void CaptureReadback() {
        RenderTexture target = _target!;
        try {
            // Tick is void, so the documented screenshot pattern is a deliberate block on the tasks.
            Color[,] colors = target.ReadColorsAsync().GetAwaiter().GetResult();
            byte[] pixels = target.ReadPixelsAsync().GetAwaiter().GetResult();

            int width = target.Width;
            int height = target.Height;
            int centerX = width / 2;
            int centerY = height / 2;
            Color center = colors[centerY, centerX];
            int centerOffset = ((centerY * width) + centerX) * 4;

            string linear = FormattableString.Invariant(
                $"{center.R:0.000} {center.G:0.000} {center.B:0.000} {center.A:0.000}");
            string bytes = FormattableString.Invariant(
                $"{pixels[centerOffset]} {pixels[centerOffset + 1]} {pixels[centerOffset + 2]}");

            string path = Path.Combine(AppContext.BaseDirectory, SCREENSHOT_FILE_NAME);
            PpmScreenshot.Write(path, width, height, pixels);

            _readbackStatus = FormattableString.Invariant(
                $"readback {width}x{height} center: linear ({linear})  rgba8 ({bytes})  saved {SCREENSHOT_FILE_NAME}");
            Console.Out.WriteLine(FormattableString.Invariant(
                $"RenderTarget readback: {width}x{height} center linear ({linear}) rgba8 ({bytes}) -> {path}"));
        } catch (Exception exception) when (exception is not OutOfMemoryException) {
            _readbackStatus = $"readback failed: {exception.Message}";
            Console.Error.WriteLine($"RenderTarget readback failed: {exception}");
        }
    }

    private Transform OffscreenCameraTransform() {
        float orbit = _sceneAngle * 0.45f;
        Vector3 position = new(
            MathF.Sin(orbit) * 4.6f,
            2.7f,
            MathF.Cos(orbit) * 4.6f);
        return LookAt(position, _offscreenLookTarget);
    }

    private static Transform MainCameraTransform() =>
        LookAt(new Vector3(0f, 3.4f, 7.6f), _lookTarget);

    private static Transform LeftSplitCameraTransform() =>
        LookAt(new Vector3(-4.6f, 2.8f, 5.8f), _lookTarget);

    private static Transform RightSplitCameraTransform() =>
        LookAt(new Vector3(4.8f, 2.1f, 6.2f), _lookTarget);

    private static Transform LookAt(Vector3 position, Vector3 target) {
        Vector3 forward = Vector3.Normalize(target - position);
        return new Transform(position, Quaternion.LookRotation(forward, Vector3.UnitY), Vector3.One);
    }
}
