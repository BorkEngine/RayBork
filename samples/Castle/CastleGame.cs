using System.Numerics;
using RayBork;

namespace Castle;

/// <summary>
/// A small orbitable castle scene: load a model and its texture, bind the texture to the model's
/// materials, draw the model solid with a camera that orbits a fixed point, outline and hit-test the
/// castle under the mouse, and draw the status line from a later HUD camera.
/// </summary>
internal sealed class CastleGame : IGame {
    private const string MODEL_PATH = "/models/obj/castle";
    private const string TEXTURE_PATH = "/models/obj/castle_diffuse";
    private const float FIELD_OF_VIEW_DEGREES = 45f;
    private const float NEAR_PLANE = 0.01f;
    private const float ORBIT_DEGREES_PER_PIXEL = 0.25f;
    private const float ZOOM_PER_WHEEL_STEP = 1.15f;
    private const float MIN_DISTANCE = 5f;
    private const float MAX_DISTANCE = 500f;
    // The orthographic far plane must stay beyond the maximum orbit distance plus the scene, so it
    // scales with the zoom range instead of pinning a distance the controls could outgrow.
    private const float ORTHO_FAR = MAX_DISTANCE * 2f;
    private const float MAX_PITCH_DEGREES = 89f;
    private const int GRID_SLICES = 20;
    private const float GRID_SPACING = 10f;
    private const float HIT_POINT_RADIUS = 0.2f;

    // The status line is screen-aligned text drawn on a plane this far in front of the HUD camera.
    // The screen transform's scale makes the text size below render pixels in either mode.
    private const float OVERLAY_DISTANCE = 1f;
    private const float OVERLAY_PIXEL_HEIGHT = 18f;
    private const float OVERLAY_MARGIN_PIXELS = 8f;

    private static readonly Vector3 _orbitCenter = new(0f, 12f, 0f);
    private static readonly Vector3 _initialCameraPosition = new(50f, 50f, 50f);
    private static readonly Color _hoverBoundsColor = Color.Cyan;
    private static readonly Color _hitPointColor = Color.Red;

    private readonly int _frameLimit;
    private readonly StatusOverlay _overlay = new();
    private readonly PostProcessStack _postProcess = new();
    private readonly SsaoEffect _ssao = new();

    private Model? _model;
    private Texture? _texture;
    private BoundingBox _worldBounds;
    private float _yawDegrees;
    private float _pitchDegrees;
    private float _distance;
    private CameraProjection _projection = CameraProjection.Perspective;
    private int _frameCount;

    /// <summary>
    /// Creates the scene and starts the camera at the same position and target as the reference
    /// scene: (50, 50, 50) looking at (0, 12, 0).
    /// </summary>
    /// <param name="frameLimit">The number of ticks to run, or zero to run until the window closes.</param>
    internal CastleGame(int frameLimit) {
        _frameLimit = frameLimit;

        Vector3 offset = _initialCameraPosition - _orbitCenter;
        _distance = offset.Length();

        // Start from the rotation that looks from the initial camera position at the orbit center,
        // then store its degrees so the rest of the sample only deals in degrees.
        Vector3 angles = Quaternion.LookRotation(-offset, Vector3.UnitY).EulerAngles;
        _yawDegrees = MathF.IEEERemainder(angles.Y, 360f);
        _pitchDegrees = MathF.IEEERemainder(-angles.X, 360f);
    }

    /// <inheritdoc/>
    public async Task Setup(EngineContext context) {
        _model = await context.Assets.Load<Model>(MODEL_PATH);
        _texture = await context.Assets.Load<Texture>(TEXTURE_PATH);

        // A model part owns its material. The bake assigns the built-in mesh material, so binding
        // the texture here is enough; no custom shader is needed.
        foreach (ModelPart part in _model.Parts) {
            part.Shader.SetUniform("mainTexture", _texture);
        }

        // The castle is static and drawn with Transform.Identity, so its model-space bounds are its
        // world-space bounds. Model.Bounds is the union of the part bounds, and each part bound is
        // its mesh bounds after the part's local transform.
        _worldBounds = _model.Bounds;

        // The SSAO effect is app-lifetime and enabled by default at Medium quality. The stack is
        // snapshotted for the scene camera only; the later HUD camera keeps no stack.
        _postProcess.Add(_ssao);
    }

    /// <inheritdoc/>
    public void Tick(EngineContext context) {
        if (context.Input.IsKeyPressed(Key.Escape)) {
            context.RequestExit();
        }

        // O swaps the projection before either camera is created; perspective remains the default.
        if (context.Input.IsKeyPressed(Key.O)) {
            _projection = _projection == CameraProjection.Perspective
                ? CameraProjection.Orthographic
                : CameraProjection.Perspective;
        }

        // A toggles the built-in SSAO effect. The change applies to cameras created after this tick.
        if (context.Input.IsKeyPressed(Key.A)) {
            _ssao.Enabled = !_ssao.Enabled;
        }

        UpdateOrbit(context.Input);

        // The orbit transform sits at the point we want to look at. Rotating it moves the camera
        // around that point; the camera is its child at a local +Z offset. Camera forward is -Z, so
        // the child looks back at the orbit center. A positive elevation must move the camera up,
        // which is the negative pitch in the yaw/pitch rotation.
        Transform orbit = new(
            _orbitCenter,
            Quaternion.Euler(-_pitchDegrees, _yawDegrees, 0f),
            Vector3.One);
        Transform cameraTransform = new Transform(new Vector3(0f, 0f, _distance)).WithParent(orbit);

        // The scene pass clears color and depth and draws every depth-tested scene draw. The status
        // line is not part of it.
        using (Camera scene = CreatePass(
            context,
            cameraTransform,
            ClearMode.ColorAndDepth,
            Color.WhiteSmoke,
            _postProcess)) {
            scene.DrawModel(_model!, Transform.Identity, Color.White);

            // raylib's reference grid: 20 slices 10 units apart, centered on the origin where the
            // castle stands, drawn after the model in the same opaque, depth-tested pass.
            scene.DrawGrid(GRID_SLICES, GRID_SPACING, Color.Gray);

            DrawHoverOverlay(scene, context);
        }

        // The DrawFPS-style overlay is an ordinary later HUD camera: presented FPS over a one-second
        // window, the tick rate, the render CPU time, the last completed frame's draw and instance
        // counts, and the current projection. ClearMode.Depth loads the scene color and clears depth,
        // so the scene cannot occlude the text, and there is no overlay pass. The shared transform and
        // projection keep the text size and screen anchoring.
        using Camera hud = CreatePass(context, cameraTransform, ClearMode.Depth, clearColor: null);
        DrawStatusOverlay(hud, context);

        _frameCount++;
        if (_frameLimit > 0 && _frameCount >= _frameLimit) {
            context.RequestExit();
        }
    }

    /// <summary>
    /// Creates one camera pass with the current orbit transform and projection. Both passes share the
    /// transform, projection, viewport, and clip planes, so the screen transform is identical.
    /// </summary>
    private Camera CreatePass(
        EngineContext context,
        Transform transform,
        ClearMode clearMode,
        Color? clearColor,
        PostProcessStack? postProcess = null) {
        // The orthographic height reproduces the perspective framing at the current orbit distance,
        // so the orbit, wheel zoom, and window resize behave the same in both modes. A finite far
        // plane is enough: the orthographic pass only needs to cover the scene.
        bool orthographic = _projection == CameraProjection.Orthographic;
        float orthographicHeight = orthographic
            ? 2f * _distance * MathF.Tan(FIELD_OF_VIEW_DEGREES * (MathF.PI / 180f) * 0.5f)
            : 0f;

        return context.CreateCamera(
            transform,
            projection: _projection,
            fieldOfView: orthographic ? 0f : FIELD_OF_VIEW_DEGREES,
            orthographicHeight: orthographicHeight,
            clearMode: clearMode,
            clearColor: clearColor,
            near: NEAR_PLANE,
            far: orthographic ? ORTHO_FAR : float.PositiveInfinity,
            postProcess: postProcess);
    }

    /// <summary>
    /// Outlines the castle when the pointer ray hits its world bounds and marks the nearest mesh hit.
    /// </summary>
    private void DrawHoverOverlay(Camera camera, EngineContext context) {
        // MousePosition and ScreenToWorldRay both use render pixels, so the pointer needs no unit
        // conversion; ScreenToWorldRay applies the camera viewport itself.
        Ray pointerRay = camera.ScreenToWorldRay(context.Input.MousePosition);

        if (!Collision.GetRayBox(pointerRay, _worldBounds).Hit) {
            return;
        }

        // The box stays visible while the pointer is over the bounds, even when the ray passes through
        // an opening and misses every triangle.
        camera.DrawBoundingBox(_worldBounds, _hoverBoundsColor);

        RayCollision nearest = default;
        IReadOnlyList<ModelPart> parts = _model!.Parts;
        // A part is drawn at LocalTransform * Transform.Identity, so the local transform alone maps
        // its mesh to world space. Indexed iteration avoids the enumerator the interface's foreach
        // would allocate every frame.
        for (int index = 0; index < parts.Count; index++) {
            ModelPart part = parts[index];
            RayCollision hit = Collision.GetRayMesh(
                pointerRay,
                part.Mesh,
                part.LocalTransform.ToMatrix());
            if (hit.Hit && (!nearest.Hit || hit.Distance < nearest.Distance)) {
                nearest = hit;
            }
        }

        if (nearest.Hit) {
            camera.DrawSphere(nearest.Point, HIT_POINT_RADIUS, _hitPointColor);
        }
    }

    /// <summary>
    /// Draws the presented-FPS, tick-rate, render-time, draw-call, and instance statistics and the
    /// current projection at the top-left of the view.
    /// </summary>
    /// <remarks>
    /// The text is screen-aligned and drawn on a plane <see cref="OVERLAY_DISTANCE"/> world unit in
    /// front of the HUD camera. That camera clears depth after the scene pass, so scene geometry cannot
    /// occlude the line.
    /// </remarks>
    private void DrawStatusOverlay(Camera camera, EngineContext context) {
        // The returned transform's scale is world units per render pixel, so an em height of 18 local
        // units stays 18 render pixels in both projections. The built-in mono face keeps the changing
        // counters from shifting the text around them. The projection gets its own line so it stays
        // visible when the statistics line runs past the right edge.
        Transform overlay = camera.ScreenToWorldTransform(
            new Vector2(OVERLAY_MARGIN_PIXELS, OVERLAY_MARGIN_PIXELS),
            OVERLAY_DISTANCE);
        string projectionName = _projection == CameraProjection.Perspective ? "perspective" : "ortho";
        string ssaoState = _ssao.Enabled ? "on" : "off";
        string statusText =
            $"{_overlay.Build(context)}\nprojection {projectionName}   ssao {ssaoState}";

        camera.DrawText(
            context.Defaults.DefaultMonoFont,
            statusText,
            overlay,
            OVERLAY_PIXEL_HEIGHT,
            Color.Black);
    }

    /// <inheritdoc/>
    public Task Shutdown() {
        // The model owns its part materials; the texture is owned by this scene.
        _model?.Dispose();
        _model = null;
        _texture?.Dispose();
        _texture = null;
        return Task.CompletedTask;
    }

    private void UpdateOrbit(IInput input) {
        // Drag to orbit; the wheel moves the camera closer or farther away. Screen Y grows
        // downward, so adding the vertical delta raises the camera when the pointer moves down.
        if (input.IsMouseButtonDown(MouseButton.Left)) {
            Vector2 delta = input.MouseDelta;
            _yawDegrees -= delta.X * ORBIT_DEGREES_PER_PIXEL;

            float pitchLimit = MAX_PITCH_DEGREES;
            _pitchDegrees = Math.Clamp(
                _pitchDegrees + (delta.Y * ORBIT_DEGREES_PER_PIXEL),
                -pitchLimit,
                pitchLimit);
        }

        float wheel = input.MouseWheel;
        if (wheel != 0f) {
            _distance = Math.Clamp(
                _distance * MathF.Pow(ZOOM_PER_WHEEL_STEP, -wheel),
                MIN_DISTANCE,
                MAX_DISTANCE);
        }
    }
}

