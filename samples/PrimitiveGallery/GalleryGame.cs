using System.Numerics;
using RayBork;

namespace PrimitiveGallery;

/// <summary>
/// A small orbitable gallery that draws every M5 primitive helper and the procedural mesh path in one
/// camera pass: a reference grid, two rows of solid, wire, line, and textured shapes, a middle row of
/// generated meshes, and the family pairs that show the completed M5 overloads.
/// </summary>
/// <remarks>
/// The world helpers run along the initial view's horizontal axis, so the three rows read left to
/// right before the first orbit. The lower row runs along the grid and pairs each solid shape with
/// its wire or tessellated family form. The middle row builds five shapes with <see cref="MeshBuilder"/>
/// and draws them through a caller-owned mesh material. The upper row groups the wire, line, curve,
/// ray, and billboard helpers. The scene is procedural: the texture and the generated meshes are built
/// in <see cref="Setup"/>, and every asset is disposed in <see cref="Shutdown"/>, so the sample needs
/// no assets executable or pack.
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

    private static readonly Vector3 _orbitCenter = new(0f, 2.4f, 0f);
    private static readonly Vector3 _initialCameraPosition = new(8f, 6.2f, 12.5f);

    private readonly int _frameLimit;
    private readonly Vector3 _galleryRight;
    private readonly Vector3 _galleryView;

    private Texture? _texture;
    private Shader? _meshShader;
    private Mesh? _generatedCube;
    private Mesh? _generatedSphere;
    private Mesh? _generatedTorus;
    private Mesh? _generatedKnot;
    private Mesh? _generatedCone;
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

        // The helpers are laid out along the initial view's horizontal axis. Deriving the axes from
        // the starting camera keeps the three rows reading left to right before the first orbit; the
        // view axis also rolls the one transformed shape so its placement is visible.
        Vector3 viewDirection = Vector3.Normalize(_orbitCenter - _initialCameraPosition);
        _galleryView = viewDirection;
        _galleryRight = Vector3.Normalize(Vector3.Cross(viewDirection, Vector3.UnitY));
    }

    /// <inheritdoc/>
    public async Task Setup(EngineContext context) {
        // The sample has no pack, so the one texture it shows is built here. A warm left half, a cool
        // right half, and a checkerboard make the billboard visibly textured and let its orientation
        // read at a glance.
        TextureBuilder textureBuilder = context.CreateTextureBuilder(TEXTURE_SIZE, TEXTURE_SIZE);
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

        textureBuilder.SetPixels(pixels);
        _texture = await textureBuilder.BuildAsync();

        // The generated row is the procedural mesh path rather than a primitive helper. Generate*
        // replaces the builder geometry with one named primitive and BuildAsync uploads that snapshot
        // as a caller-owned mesh; one builder is reused for the five builds because every build takes
        // its own snapshot before the upload.
        MeshBuilder meshBuilder = context.CreateMeshBuilder();
        meshBuilder.GenerateCube(0.9f, 0.9f, 0.9f);
        _generatedCube = await meshBuilder.BuildAsync();
        meshBuilder.GenerateSphere(0.5f, rings: 12, slices: 24);
        _generatedSphere = await meshBuilder.BuildAsync();
        meshBuilder.GenerateTorus(0.42f, 0.14f, radSeg: 32, sides: 12);
        _generatedTorus = await meshBuilder.BuildAsync();
        meshBuilder.GenerateKnot(0.4f, 0.12f, radSeg: 48, sides: 8);
        _generatedKnot = await meshBuilder.BuildAsync();
        meshBuilder.GenerateCone(0.42f, 0.9f, slices: 24);
        _generatedCone = await meshBuilder.BuildAsync();

        // One caller-owned material over the built-in mesh program draws the whole row; each draw
        // supplies its own tint. The unbound mainTexture samples the renderer's white fallback.
        _meshShader = context.Defaults.CreateMeshShader();
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

        _frameCount++;
        if (_frameLimit > 0 && _frameCount >= _frameLimit) {
            context.RequestExit();
        }
    }

    /// <inheritdoc/>
    public Task Shutdown() {
        // The texture, the generated meshes, and the mesh material are owned by this sample; the
        // camera pass owned only frame-scoped references.
        _generatedCube?.Dispose();
        _generatedCube = null;
        _generatedSphere?.Dispose();
        _generatedSphere = null;
        _generatedTorus?.Dispose();
        _generatedTorus = null;
        _generatedKnot?.Dispose();
        _generatedKnot = null;
        _generatedCone?.Dispose();
        _generatedCone = null;
        _meshShader?.Dispose();
        _meshShader = null;
        _texture?.Dispose();
        _texture = null;
        return Task.CompletedTask;
    }

    private void DrawGallery(Camera camera) {
        // The reference grid sits on the ground plane and gives the floating rows a frame of
        // reference. It is always centered on the world origin.
        camera.DrawGrid(slices: 20, spacing: 1f, Color.Gray5);

        // The lower row is six stations on the grid: the first five pair a solid helper with the
        // wire, tessellated, or strip form of the same family, and the last puts the point above the
        // plane. Every pair is spaced wide enough that its two shapes read as separate objects rather
        // than touching at the starting camera distance.
        camera.DrawCube(Station(-9.3f, 0.5f), 1f, 1f, 1f, Color.LightSeaGreen);
        camera.DrawWireCube(Station(-7.3f, 0.5f), 1f, 1f, 1f, Color.Orange);

        camera.DrawSphere(Station(-5.4f, 0.5f), 0.5f, Color.RoyalBlue);
        camera.DrawSphere(Station(-3.8f, 0.5f), 0.5f, rings: 5, slices: 10, Color.CadetBlue);

        camera.DrawCylinder(Station(-1.8f, 0f), 0.45f, 1.5f, Color.Purple);
        // The cone is the gallery's one Transform overload: the transform position is its
        // bottom-cap center, the roll around the view axis leans it, and the unit scale keeps the
        // authored radii and height, so the placement contract is visible instead of hidden behind
        // an identity transform.
        camera.DrawCylinder(
            new Transform(
                Station(0f, 0f),
                Quaternion.CreateFromAxisAngle(_galleryView, 0.3f),
                Vector3.One),
            radiusTop: 0f,
            radiusBottom: 0.5f,
            height: 1.6f,
            sides: 16,
            Color.MediumOrchid);

        camera.DrawCapsule(
            Station(1.9f, 0.3f),
            Station(1.9f, 1.5f),
            0.3f,
            rings: 4,
            slices: 10,
            Color.IndianRed);
        camera.DrawWireCapsule(
            Station(3f, 0.3f),
            Station(3f, 1.5f),
            0.3f,
            rings: 4,
            slices: 10,
            Color.LightCoral);

        // The triangle and the strip are built in the row's vertical plane and face the initial
        // camera, so their front faces and counter-clockwise winding are visible from the start.
        Vector3 triangleCenter = Station(4.55f, 0.95f);
        Vector3 triangleHalfWidth = _galleryRight * 0.55f;
        camera.DrawTriangle(
            triangleCenter - triangleHalfWidth - (Vector3.UnitY * 0.75f),
            triangleCenter + triangleHalfWidth - (Vector3.UnitY * 0.75f),
            triangleCenter + (Vector3.UnitY * 0.75f),
            Color.LimeGreen);

        // The strip alternates its corners above and below the row axis, which is the sliding window
        // of a triangle strip with the same winding as the single triangle.
        Vector3 stripCenter = Station(6.15f, 0.95f);
        camera.DrawTriangleStrip(
            [
                stripCenter - (_galleryRight * 0.55f) + (Vector3.UnitY * 0.75f),
                stripCenter - (_galleryRight * 0.55f) - (Vector3.UnitY * 0.75f),
                stripCenter - (_galleryRight * 0.18f) + (Vector3.UnitY * 0.6f),
                stripCenter - (_galleryRight * 0.18f) - (Vector3.UnitY * 0.6f),
                stripCenter + (_galleryRight * 0.55f) + (Vector3.UnitY * 0.75f),
                stripCenter + (_galleryRight * 0.55f) - (Vector3.UnitY * 0.75f),
            ],
            Color.YellowGreen);

        camera.DrawPoint(Station(8.5f, 1.25f), Color.Yellow, size: 0.55f);
        camera.DrawPlane(Station(8.5f, 0.05f), new Vector2(2.2f, 2.2f), Color.MediumVioletRed);

        // The generated row sits between the two helper rows and keeps its own left-to-right order.
        DrawGeneratedRow(camera);

        // The upper row floats above the generated meshes: the wire shape family, the line and curve
        // helpers, the ray and bounding box, and the two billboards that sample the texture.
        camera.DrawWireCube(Station(-8.5f, 5.5f), new Vector3(1.4f), Color.Orange);

        camera.DrawWireSphere(Station(-6.1f, 5.5f), 0.55f, Color.Turquoise);
        camera.DrawWireSphere(Station(-4.6f, 5.5f), 0.55f, rings: 4, slices: 10, Color.SkyBlue);

        camera.DrawWireCylinder(
            Station(-2.6f, 4.75f),
            radiusTop: 0.45f,
            radiusBottom: 0.45f,
            height: 1.5f,
            sides: 12,
            Color.DarkCyan);
        // A circle lies in its local XZ plane; rotating it 90 degrees around the row axis stands it
        // up to face the initial camera.
        camera.DrawCircle(Station(-0.9f, 5.5f), 0.8f, _galleryRight, 90f, Color.MediumSlateBlue);

        camera.DrawLine(Station(0.25f, 4.6f), Station(1.15f, 6.2f), Color.SoftRed);
        camera.DrawLines(
            [
                Station(1.45f, 4.9f),
                Station(1.9f, 6.1f),
                Station(2.35f, 4.9f),
                Station(2.8f, 6.1f),
            ],
            Color.DarkOrange);

        // The ray starts just below and left of the box and points through its center. DrawRay draws
        // the ray along its direction, so the box sits mid-ray and the line keeps going past it
        // instead of stopping inside the box.
        Vector3 boxCenter = Station(5f, 5.5f);
        Vector3 rayOrigin = boxCenter - (_galleryRight * 1f) - (Vector3.UnitY * 0.6f);
        camera.DrawBoundingBox(
            new BoundingBox(boxCenter - new Vector3(0.65f), boxCenter + new Vector3(0.65f)),
            Color.Silver);
        camera.DrawRay(new Ray(rayOrigin, boxCenter - rayOrigin), Color.Gold);

        // The right billboard samples only the warm left half of the texture, so the source
        // rectangle reads against the full-texture quad beside it.
        camera.DrawBillboard(_texture!, Station(7.4f, 5.5f), new Vector2(1.2f), Color.Beige);
        camera.DrawBillboard(
            _texture!,
            new Rect(0f, 0f, 0.5f, 1f),
            Station(9f, 5.5f),
            new Vector2(1.2f),
            Color.Beige);
    }

    private void DrawGeneratedRow(Camera camera) {
        // Every mesh in this row was generated and uploaded in Setup. DrawMesh takes the caller-owned
        // material and a per-shape tint; the torus generator lies in the local XZ plane, so a quarter
        // turn around the row axis faces its hole at the initial camera. The cone generator stands on
        // +Y and its transform position is the base center, so it sits half its height below the row
        // center.
        camera.DrawMesh(
            _generatedCube!,
            new Transform(Station(-7.2f, 3f)),
            _meshShader!,
            tint: Color.Coral);
        camera.DrawMesh(
            _generatedSphere!,
            new Transform(Station(-3.6f, 3f)),
            _meshShader!,
            tint: Color.Aquamarine);
        camera.DrawMesh(
            _generatedTorus!,
            new Transform(
                Station(0f, 3f),
                Quaternion.CreateFromAxisAngle(_galleryRight, MathF.PI * 0.5f),
                Vector3.One),
            _meshShader!,
            tint: Color.GoldenRod);
        camera.DrawMesh(
            _generatedKnot!,
            new Transform(Station(3.6f, 3f)),
            _meshShader!,
            tint: Color.Violet);
        camera.DrawMesh(
            _generatedCone!,
            new Transform(Station(7.2f, 2.55f)),
            _meshShader!,
            tint: Color.SeaGreen);
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
