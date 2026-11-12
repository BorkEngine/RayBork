using System.Numerics;
using RayBork;

namespace Castle;

/// <summary>
/// A small RayBork recreation of raylib's <c>models_loading</c> example.
/// </summary>
/// <remarks>
/// The sample loads the raylib castle model and its diffuse texture, binds the texture on the
/// model's built-in mesh material, and draws the castle with an orbital perspective camera. The
/// model, texture, and camera setup mirror raylib's example; drag-and-drop, click selection, the
/// text overlay, and the reference grid are intentionally omitted for now.
/// </remarks>
internal sealed class CastleGame : IGame {
    private const string MODEL_PATH = "/models/obj/castle";
    private const string TEXTURE_PATH = "/models/obj/castle_diffuse";
    private const float FIELD_OF_VIEW_DEGREES = 45f;
    private const float NEAR_PLANE = 0.01f;
    private const float ORBIT_RADIANS_PER_PIXEL = 0.25f * DEGREES_TO_RADIANS;
    private const float ZOOM_PER_WHEEL_STEP = 1.15f;
    private const float MIN_DISTANCE = 5f;
    private const float MAX_DISTANCE = 500f;
    private const float MAX_PITCH_DEGREES = 89f;
    private const float DEGREES_TO_RADIANS = MathF.PI / 180f;

    // raylib's RAYWHITE converted to the engine's linear working space.
    private static readonly Color _rayWhite = new(0.913f, 0.913f, 0.913f, 1f);
    private static readonly Vector3 _cameraTarget = new(0f, 12f, 0f);
    private static readonly Vector3 _initialCameraPosition = new(50f, 50f, 50f);

    private readonly int _frameLimit;

    private Model? _model;
    private Texture? _texture;
    private float _yaw;
    private float _pitch;
    private float _distance;
    private int _frameCount;

    /// <summary>
    /// Creates the sample.
    /// </summary>
    /// <param name="frameLimit">The number of ticks to run, or zero to run until the window closes.</param>
    internal CastleGame(int frameLimit) {
        _frameLimit = frameLimit;

        // Start from raylib's camera position and target: (50, 50, 50) looking at (0, 12, 0).
        Vector3 offset = _initialCameraPosition - _cameraTarget;
        _distance = offset.Length();
        _yaw = MathF.Atan2(offset.X, offset.Z);
        _pitch = MathF.Asin(offset.Y / _distance);
    }

    /// <inheritdoc/>
    public async Task Setup(EngineContext context) {
        // LoadModel + LoadTexture in raylib.
        _model = await context.Assets.Load<Model>(MODEL_PATH);
        _texture = await context.Assets.Load<Texture>(TEXTURE_PATH);

        // model.materials[0].maps[MATERIAL_MAP_DIFFUSE].texture = texture in raylib.
        // A model part owns its material; the bake defaults the castle to the built-in mesh shader.
        foreach (ModelPart part in _model.Parts) {
            part.Shader.SetUniform("mainTexture", _texture);
        }
    }

    /// <inheritdoc/>
    public void Tick(EngineContext context) {
        if (context.Input.IsKeyPressed(Key.Escape)) {
            context.RequestExit();
        }

        UpdateOrbit(context.Input);

        // BeginMode3D(camera) / DrawModel(model, position, 1.0f, WHITE).
        using Camera camera = context.CreateCamera(
            CameraTransform(),
            projection: CameraProjection.Perspective,
            fieldOfView: FIELD_OF_VIEW_DEGREES,
            clearMode: ClearMode.ColorAndDepth,
            clearColor: _rayWhite,
            near: NEAR_PLANE,
            far: float.PositiveInfinity);

        camera.DrawModel(_model!, Transform.Identity);

        _frameCount++;
        if (_frameLimit > 0 && _frameCount >= _frameLimit) {
            context.RequestExit();
        }
    }

    /// <inheritdoc/>
    public Task Shutdown() {
        // UnloadModel(model) / UnloadTexture(texture) in raylib. The model disposes its own part
        // materials and mesh references; this sample owns its texture reference.
        _model?.Dispose();
        _model = null;
        _texture?.Dispose();
        _texture = null;
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

    private Transform CameraTransform() {
        // The engine camera uses a rotation transform; derive it from the same look-at that a
        // raylib Camera describes.
        Vector3 position = CameraPosition;
        Matrix4x4 lookAt = Matrix4x4.CreateLookAt(position, _cameraTarget, Vector3.UnitY);
        if (!Matrix4x4.Invert(lookAt, out Matrix4x4 cameraMatrix)) {
            throw new InvalidOperationException("The orbital camera matrix is singular.");
        }

        return new Transform(
            position,
            Quaternion.CreateFromRotationMatrix(cameraMatrix),
            Vector3.One);
    }

    private void UpdateOrbit(IInput input) {
        // UpdateCamera(&camera, CAMERA_ORBITAL): drag with the left mouse button to orbit and use
        // the wheel to move closer or farther. Screen Y grows downward, so adding the mouse delta
        // to the elevation makes dragging up look up rather than down.
        if (input.IsMouseButtonDown(MouseButton.Left)) {
            Vector2 delta = input.MouseDelta;
            _yaw -= delta.X * ORBIT_RADIANS_PER_PIXEL;

            float pitchLimit = MAX_PITCH_DEGREES * DEGREES_TO_RADIANS;
            _pitch = Math.Clamp(
                _pitch + (delta.Y * ORBIT_RADIANS_PER_PIXEL),
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
