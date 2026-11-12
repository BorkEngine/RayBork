using System.Numerics;
using RayBork;

namespace Castle;

/// <summary>
/// A small orbitable castle scene: load a model and its texture, bind the texture to the model's
/// materials, and draw the model with a camera that orbits a fixed point.
/// </summary>
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

    private static readonly Vector3 _orbitCenter = new(0f, 12f, 0f);
    private static readonly Vector3 _initialCameraPosition = new(50f, 50f, 50f);
    private static readonly Color _background = new(0.913f, 0.913f, 0.913f, 1f);

    private readonly int _frameLimit;

    private Model? _model;
    private Texture? _texture;
    private float _yaw;
    private float _pitch;
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
        _yaw = MathF.Atan2(offset.X, offset.Z);
        _pitch = MathF.Asin(offset.Y / _distance);
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
    }

    /// <inheritdoc/>
    public void Tick(EngineContext context) {
        if (context.Input.IsKeyPressed(Key.Escape)) {
            context.RequestExit();
        }

        UpdateOrbit(context.Input);

        // The orbit transform sits at the point we want to look at. Rotating it moves the camera
        // around that point; the camera is its child at a local +Z offset. Camera forward is -Z, so
        // the child looks back at the orbit center.
        Transform orbit = new(
            _orbitCenter,
            Quaternion.CreateFromYawPitchRoll(_yaw, _pitch, 0f),
            Vector3.One);
        Transform cameraTransform = new Transform(new Vector3(0f, 0f, _distance)).WithParent(orbit);

        using Camera camera = context.CreateCamera(
            cameraTransform,
            projection: CameraProjection.Perspective,
            fieldOfView: FIELD_OF_VIEW_DEGREES,
            clearMode: ClearMode.ColorAndDepth,
            clearColor: _background,
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
        // The model owns its part materials; the texture is owned by this scene.
        _model?.Dispose();
        _model = null;
        _texture?.Dispose();
        _texture = null;
        return Task.CompletedTask;
    }

    private void UpdateOrbit(IInput input) {
        // Drag to orbit; the wheel moves the camera closer or farther away. Screen Y grows
        // downward, so adding the vertical delta raises the camera when the pointer moves up.
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
