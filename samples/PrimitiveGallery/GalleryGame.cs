using System.Numerics;
using RayBork;

namespace PrimitiveGallery;

/// <summary>
/// A small orbitable gallery that draws every M5 primitive helper in one camera pass: a reference
/// grid, two rows of solid and wire shapes, a billboard, and a screen-space texture overlay.
/// </summary>
/// <remarks>
/// The world helpers run along the initial view's horizontal axis, so the two rows read left to right
/// before the first orbit. The scene is procedural: the only texture is built in <see cref="Setup"/>
/// and disposed in <see cref="Shutdown"/>, so the sample needs no assets executable or pack.
/// </remarks>
internal sealed class GalleryGame : IGame {
    private const float FIELD_OF_VIEW_DEGREES = 45f;
    private const float NEAR_PLANE = 0.05f;
    private const float ORBIT_DEGREES_PER_PIXEL = 0.25f;
    private const float ZOOM_PER_WHEEL_STEP = 1.15f;
    private const float MIN_DISTANCE = 6f;
    private const float MAX_DISTANCE = 60f;
    private const float MAX_PITCH_DEGREES = 80f;

    private const int TEXTURE_SIZE = 128;
    private const int CHECKER_SIZE = 16;
    private const float OVERLAY_WIDTH = 160f;
    private const float OVERLAY_HEIGHT = 90f;
    private const float OVERLAY_MARGIN = 16f;

    private static readonly Vector3 _orbitCenter = new(0f, 2.4f, 0f);
    private static readonly Vector3 _initialCameraPosition = new(8f, 6.2f, 12.5f);

    private readonly int _frameLimit;
    private readonly Vector3 _galleryRight;

    private Texture? _texture;
    private float _yawDegrees;
    private float _pitchDegrees;
    private float _distance;
    private int _frameCount;

    /// <summary>
    /// Creates the gallery and starts the camera at the same framing it keeps before the first orbit.
    /// </summary>
    /// <param name="frameLimit">The number of ticks to run, or zero to run until the window closes.</param>
    internal GalleryGame(int frameLimit) {
        _frameLimit = frameLimit;

        Vector3 offset = _initialCameraPosition - _orbitCenter;
        _distance = offset.Length();

        // Start from the rotation that looks from the initial camera position at the orbit center,
        // then store its degrees so the rest of the sample only deals in degrees. This mirrors the
        // orbit convention in the other samples.
        Vector3 angles = Quaternion.LookRotation(-offset, Vector3.UnitY).EulerAngles;
        _yawDegrees = MathF.IEEERemainder(angles.Y, 360f);
        _pitchDegrees = MathF.IEEERemainder(-angles.X, 360f);

        // The helpers are laid out along the initial view's horizontal axis. Deriving the axis from
        // the starting camera keeps the two rows reading left to right before the first orbit.
        Vector3 viewDirection = Vector3.Normalize(_orbitCenter - _initialCameraPosition);
        _galleryRight = Vector3.Normalize(Vector3.Cross(viewDirection, Vector3.UnitY));
    }

    /// <inheritdoc/>
    public async Task Setup(EngineContext context) {
        // The sample has no pack, so the one texture it shows is built here. A warm left half, a cool
        // right half, and a checkerboard make the billboard and the overlay visibly textured and
        // let their orientation read at a glance.
        TextureBuilder builder = context.CreateTextureBuilder(TEXTURE_SIZE, TEXTURE_SIZE);
        Color[] pixels = new Color[TEXTURE_SIZE * TEXTURE_SIZE];
        for (int y = 0; y < TEXTURE_SIZE; y++) {
            for (int x = 0; x < TEXTURE_SIZE; x++) {
                bool light = (((x / CHECKER_SIZE) + (y / CHECKER_SIZE)) & 1) == 0;
                bool warm = x < TEXTURE_SIZE / 2;
                pixels[(y * TEXTURE_SIZE) + x] = (warm, light) switch {
                    (true, true) => Color.GoldenRod,
                    (true, false) => Color.SaddleBrown,
                    (false, true) => Color.CornflowerBlue,
                    _ => Color.MidnightBlue,
                };
            }
        }

        builder.SetPixels(pixels);
        _texture = await builder.BuildAsync();
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
            clearColor: Color.Gray3,
            near: NEAR_PLANE,
            far: float.PositiveInfinity);

        DrawGallery(camera);
        DrawOverlay(camera, context);

        _frameCount++;
        if (_frameLimit > 0 && _frameCount >= _frameLimit) {
            context.RequestExit();
        }
    }

    /// <inheritdoc/>
    public Task Shutdown() {
        // The texture is owned by this sample; the camera pass owned only a frame-scoped reference.
        _texture?.Dispose();
        _texture = null;
        return Task.CompletedTask;
    }

    private void DrawGallery(Camera camera) {
        // The reference grid sits on the ground plane and gives the floating rows a frame of
        // reference. It is always centered on the world origin.
        camera.DrawGrid(slices: 20, spacing: 1f, Color.Gray5);

        // The lower row holds the solid helpers. Each one starts on or just above the grid.
        camera.DrawCube(Station(-8.5f, 0.8f), new Vector3(1.6f), Color.LightSeaGreen);
        camera.DrawSphere(Station(-5.1f, 0.8f), 0.8f, Color.RoyalBlue);
        camera.DrawCylinder(Station(-1.7f, 0f), 0.7f, 1.8f, Color.Purple);

        // The triangle is built in the row's vertical plane and faces the initial camera, so its
        // front face and counter-clockwise winding are visible from the starting view.
        Vector3 triangleCenter = Station(1.7f, 0.95f);
        Vector3 triangleHalfWidth = _galleryRight * 0.9f;
        camera.DrawTriangle(
            triangleCenter - triangleHalfWidth - (Vector3.UnitY * 0.9f),
            triangleCenter + triangleHalfWidth - (Vector3.UnitY * 0.9f),
            triangleCenter + (Vector3.UnitY * 0.9f),
            Color.LimeGreen);

        camera.DrawPoint(Station(5.1f, 1f), Color.Yellow, size: 0.7f);
        camera.DrawPlane(Station(8.5f, 0.05f), new Vector2(2.6f, 2.6f), Color.MediumVioletRed);

        // The upper row floats above the solids: the two wire helpers, the line helpers, and the
        // world-space textured billboard that faces the orbiting camera.
        camera.DrawWireCube(Station(-8.5f, 4.7f), new Vector3(1.8f), Color.Orange);
        camera.DrawWireSphere(Station(-5.1f, 4.7f), 1f, Color.Turquoise);
        camera.DrawLine(Station(-2.6f, 3.7f), Station(-0.8f, 5.5f), Color.SoftRed);
        camera.DrawLines(
            [
                Station(0.4f, 4.1f),
                Station(1.1f, 5.3f),
                Station(1.8f, 4.1f),
                Station(2.5f, 5.3f),
            ],
            Color.DarkOrange);
        camera.DrawBillboard(_texture!, Station(5.1f, 4.7f), new Vector2(2.2f), Color.Beige);
    }

    private void DrawOverlay(Camera camera, EngineContext context) {
        // DrawTexture takes render pixels with a top-left origin, so anchor the quad to the
        // bottom-left corner of the current framebuffer. It is an overlay and ignores scene depth.
        Rect destination = new(
            OVERLAY_MARGIN,
            context.Window.RenderHeight - OVERLAY_HEIGHT - OVERLAY_MARGIN,
            OVERLAY_WIDTH,
            OVERLAY_HEIGHT);
        camera.DrawTexture(_texture!, destination);
    }

    private Vector3 Station(float offset, float height) =>
        (_galleryRight * offset) + (Vector3.UnitY * height);

    private void UpdateOrbit(IInput input) {
        // Drag to orbit; the wheel moves the camera closer or farther away. Screen Y grows
        // downward, so adding the vertical delta raises the camera when the pointer moves up.
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
