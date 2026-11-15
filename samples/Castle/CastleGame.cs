using System.Numerics;
using RayBork;

namespace Castle;

/// <summary>
/// A small orbitable castle scene: load a model and its texture, bind the texture to the model's
/// materials, draw the model solid with a camera that orbits a fixed point, and outline and hit-test
/// the castle under the mouse.
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
    private const float MAX_PITCH_DEGREES = 89f;
    private const int GRID_SLICES = 20;
    private const float GRID_SPACING = 10f;
    private const float HIT_POINT_RADIUS = 0.2f;

    // The status line is world-space text until M7 adds the screen-space HUD path. It sits on a
    // camera-facing plane one world unit in front of the camera.
    private const float OVERLAY_DISTANCE = 1f;
    private const float OVERLAY_PIXEL_HEIGHT = 18f;
    private const float OVERLAY_MARGIN_PIXELS = 8f;

    private static readonly Vector3 _orbitCenter = new(0f, 12f, 0f);
    private static readonly Vector3 _initialCameraPosition = new(50f, 50f, 50f);
    private static readonly Color _hoverBoundsColor = Color.Cyan;
    private static readonly Color _hitPointColor = Color.Red;

    private readonly int _frameLimit;
    private readonly StatusOverlay _overlay = new();

    private Model? _model;
    private Texture? _texture;
    private BoundingBox _worldBounds;
    private float _yawDegrees;
    private float _pitchDegrees;
    private float _distance;
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
    }

    /// <inheritdoc/>
    public void Tick(EngineContext context) {
        if (context.Input.IsKeyPressed(Key.Escape)) {
            context.RequestExit();
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

        using Camera camera = context.CreateCamera(
            cameraTransform,
            projection: CameraProjection.Perspective,
            fieldOfView: FIELD_OF_VIEW_DEGREES,
            clearMode: ClearMode.ColorAndDepth,
            clearColor: Color.WhiteSmoke,
            near: NEAR_PLANE,
            far: float.PositiveInfinity);

        camera.DrawModel(_model!, Transform.Identity, Color.White);

        // raylib's reference grid: 20 slices 10 units apart, centered on the origin where the castle
        // stands, drawn after the model in the same opaque, depth-tested pass.
        camera.DrawGrid(GRID_SLICES, GRID_SPACING, Color.Gray);

        DrawHoverOverlay(camera, context);

        // The DrawFPS-style overlay: presented FPS over a one-second window, the tick rate, the
        // render CPU time, and the last completed frame's draw and instance counts. It is recorded
        // last and is also world-space text.
        DrawStatusOverlay(camera, context);

        _frameCount++;
        if (_frameLimit > 0 && _frameCount >= _frameLimit) {
            context.RequestExit();
        }
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
    /// Draws the presented-FPS, tick-rate, render-time, draw-call, and instance line at the top-left
    /// of the view.
    /// </summary>
    /// <remarks>
    /// Screen-space text arrives in M7. Until then the line is world-space text on a camera-facing
    /// plane one unit in front of the camera, positioned and oriented with the camera's own axes.
    /// </remarks>
    private void DrawStatusOverlay(Camera camera, EngineContext context) {
        // One render pixel spans worldPerPixel world units at the overlay distance. The text size is
        // an em height in world units, so an 18-pixel line uses 18 * worldPerPixel. The built-in mono
        // face keeps the changing counters from shifting the text around them.
        float halfHeight = OVERLAY_DISTANCE * MathF.Tan(camera.FieldOfView * (MathF.PI / 180f) * 0.5f);
        float worldPerPixel = (2f * halfHeight) / camera.Viewport.Height;
        float halfWidth = halfHeight * (camera.Viewport.Width / camera.Viewport.Height);
        float margin = OVERLAY_MARGIN_PIXELS * worldPerPixel;

        // The camera transform's axes are the screen axes: +X is right, +Y is up, and +Z points back
        // toward the viewer. The text layout origin is the top-left of the first line, so placing it
        // at the inset corner of the plane and reusing the camera rotation keeps the line upright,
        // facing the camera, and where a HUD would sit.
        Vector3 position = camera.Transform.Position
            + (camera.Transform.Forward * OVERLAY_DISTANCE)
            + (camera.Transform.Up * (halfHeight - margin))
            - (camera.Transform.Right * (halfWidth - margin));

        camera.DrawText(
            context.Defaults.DefaultMonoFont,
            _overlay.Build(context),
            new Transform(position, camera.Transform.Rotation, Vector3.One),
            OVERLAY_PIXEL_HEIGHT * worldPerPixel,
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

