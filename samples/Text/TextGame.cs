using System.Numerics;
using RayBork;

namespace Text;

/// <summary>
/// A world-space text wall showing faces, sizes, alignments, wrapping, Latin and Cyrillic scripts,
/// billboards, and lazy glyph generation.
/// </summary>
/// <remarks>
/// Text draws in the transform's local XY plane facing local +Z and batches into one MSDF text
/// draw. Drag to orbit and the wheel zooms.
/// </remarks>
internal sealed class TextGame : IGame
{
    private const string TITLE = "RayBork";
    private const string LORA_FONT_PATH = "/fonts/lora";

    private const string PARAGRAPH =
        "Typography is geometry here: each glyph is a quad with a signed-distance field, so one "
        + "face stays crisp at any size. LongUnbreakableIdentifierThatOverflowsItsLine is wider than "
        + "this box.";
    private const string LATIN_PARAGRAPH =
        "The quick brown fox jumps over the lazy dog. Pack my box with five dozen liquor jugs, then "
        + "let the fox jump again while each glyph is generated on demand.";
    private const string CYRILLIC_PARAGRAPH =
        "Съешь же ещё этих мягких французских булок, да выпей чаю. Широкая электрификация южных "
        + "губерний даст мощный толчок подъёму сельского хозяйства.";
    private const string LAZY_PREFIX = "Lazy Greek glyphs: ";
    private const string LAZY_GREEK = "αβγδεζηθικλμνξοπρστυφχψω";

    private const float FIELD_OF_VIEW_DEGREES = 45f;
    private const float NEAR_PLANE = 0.05f;
    private const float ORBIT_DEGREES_PER_PIXEL = 0.25f;
    private const float ZOOM_PER_WHEEL_STEP = 1.15f;
    private const float MIN_DISTANCE = 2.5f;
    private const float MAX_DISTANCE = 40f;
    private const float MAX_PITCH_DEGREES = 75f;

    // The paragraph is drawn three times with the same box width. maxWidth is in world units, so the
    // same 4.2-unit box holds a different number of lines at each size.
    private const float PARAGRAPH_MAX_WIDTH = 4.2f;
    private const float PARAGRAPH_LARGE_SIZE = 0.32f;
    private const float PARAGRAPH_MEDIUM_SIZE = 0.16f;
    private const float PARAGRAPH_SMALL_SIZE = 0.12f;
    private const float BODY_MAX_WIDTH = 3.9f;
    private const float BODY_SIZE = 0.14f;
    private const float HEADING_SIZE = 0.16f;

    private const float LEFT_COLUMN_X = -6.8f;
    private const float RIGHT_COLUMN_X = -1.9f;
    private const float LORA_COLUMN_X = 2.35f;
    private const float LORA_TITLE_X = -3.85f;
    private const float TITLE_TOP_Y = 3.9f;
    private const float TITLE_SIZE = 0.7f;
    private const float TITLE_LABEL_Y = 2.98f;
    private const float LABEL_SIZE = 0.11f;
    private const float STATUS_X = 2.3f;
    private const float STATUS_TOP_Y = 3.9f;
    private const float STATUS_SIZE = 0.14f;
    private const float STATUS_MAX_WIDTH = 4.4f;
    private const float SIZE_DEMO_TOP_Y = 2.72f;
    private const float MEDIUM_DEMO_TOP_Y = -0.1f;
    private const float SMALL_DEMO_TOP_Y = -1.38f;
    private const float ROW_ONE_HEADING_Y = 2.8f;
    private const float ROW_ONE_PARAGRAPH_Y = 2.55f;
    private const float ROW_TWO_HEADING_Y = 1.7f;
    private const float ROW_TWO_PARAGRAPH_Y = 1.45f;
    private const float BILLBOARD_X = 2.3f;
    private const float BILLBOARD_Y = -0.7f;
    private const float BILLBOARD_Z = 1.5f;
    private const float BILLBOARD_SIZE = 0.16f;
    private const float LAZY_LINE_TOP_Y = -1.95f;
    private const float LAZY_LINE_SIZE = 0.16f;

    private const int LAZY_GLYPH_INTERVAL_FRAMES = 30;

    private static readonly Vector3 _orbitCenter = new(0f, 0.9f, 0f);
    private static readonly Vector3 _initialCameraPosition = new(0f, 0.9f, 9.6f);
    private static readonly Quaternion _mediumParagraphRotation = Quaternion.CreateFromAxisAngle(
        Vector3.UnitZ,
        -5f * (MathF.PI / 180f)
    );
    private static readonly Quaternion _smallParagraphRotation = Quaternion.CreateFromAxisAngle(
        Vector3.UnitY,
        10f * (MathF.PI / 180f)
    );

    private readonly int _frameLimit;
    private readonly StatusOverlay _overlay = new();

    private Font? _loraFont;
    private float _yawDegrees;
    private float _pitchDegrees;
    private float _distance;
    private int _frameCount;
    private int _lazyGlyphCount;

    /// <summary>Creates the scene and starts the camera square to the text wall.</summary>
    internal TextGame(int frameLimit)
    {
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
    public async Task Setup(EngineContext context)
    {
        // The built-in default faces are engine-owned defaults. The custom Lora face comes from this
        // sample's text.pck; the sample owns that reference and releases it in Shutdown. These loads
        // are CPU-only: no glyph texture is generated until a draw needs one.
        _loraFont = await context.Assets.Load<Font>(LORA_FONT_PATH);
    }

    /// <inheritdoc/>
    public void Tick(EngineContext context)
    {
        if (context.Input.IsKeyPressed(Key.Escape))
        {
            context.RequestExit();
        }

        UpdateOrbit(context.Input);

        // Request one more Greek letter every 30 ticks. The first request happens on the first tick so
        // a short run still exercises the asynchronous glyph generation; each new letter is skipped by
        // the draw until its MSDF texture is ready, then appears without a stall.
        if ((_frameCount % LAZY_GLYPH_INTERVAL_FRAMES) == 0)
        {
            _lazyGlyphCount = Math.Min(_lazyGlyphCount + 1, LAZY_GREEK.Length);
        }

        // The orbit transform sits at the point we want to look at. Rotating it moves the camera
        // around that point; the camera is its child at a local +Z offset. Camera forward is -Z, so
        // the child looks back at the orbit center.
        Transform orbit = new(
            _orbitCenter,
            Quaternion.Euler(-_pitchDegrees, _yawDegrees, 0f),
            Vector3.One
        );
        Transform cameraTransform = new Transform(new Vector3(0f, 0f, _distance)).WithParent(orbit);

        using Camera camera = context.CreateCamera(
            cameraTransform,
            projection: CameraProjection.Perspective,
            fieldOfView: FIELD_OF_VIEW_DEGREES,
            clearMode: ClearMode.ColorAndDepth,
            clearColor: Color.Gray2,
            near: NEAR_PLANE,
            far: float.PositiveInfinity
        );

        DrawTextScene(camera, context);

        _frameCount++;
        if (_frameLimit > 0 && _frameCount >= _frameLimit)
        {
            context.RequestExit();
        }
    }

    /// <inheritdoc/>
    public Task Shutdown()
    {
        // The built-in default fonts are engine-owned and must not be disposed; only the Lora load is
        // this sample's reference.
        _loraFont?.Dispose();
        _loraFont = null;
        return Task.CompletedTask;
    }

    private void DrawTextScene(Camera camera, EngineContext context)
    {
        Font firaSans = context.Defaults.DefaultFont;
        Font mono = context.Defaults.DefaultMonoFont;
        Font lora = _loraFont!;

        // The same title in both faces. Lora is a serif, so the shape difference is visible at a
        // glance; the labels stay in the built-in face so only the title compares the two.
        camera.DrawText(
            firaSans,
            TITLE,
            new Transform(new Vector3(LEFT_COLUMN_X, TITLE_TOP_Y, 0f)),
            TITLE_SIZE,
            Color.White
        );
        camera.DrawText(
            lora,
            TITLE,
            new Transform(new Vector3(LORA_TITLE_X, TITLE_TOP_Y, 0f)),
            TITLE_SIZE,
            Color.White
        );
        camera.DrawText(
            firaSans,
            "built-in Fira Sans",
            new Transform(new Vector3(LEFT_COLUMN_X, TITLE_LABEL_Y, 0f)),
            LABEL_SIZE,
            Color.Gray7
        );
        camera.DrawText(
            firaSans,
            "baked Lora (OFL)",
            new Transform(new Vector3(LORA_TITLE_X, TITLE_LABEL_Y, 0f)),
            LABEL_SIZE,
            Color.Gray7
        );

        // The status line is ordinary world-space text too; it reports the presented rate, the tick
        // rate, and the last completed frame's render statistics, so on the first frame the draw,
        // instance, and presented-frame values are still zero. The mono face keeps the changing
        // counters from shifting their neighbors.
        camera.DrawText(
            mono,
            _overlay.Build(context),
            new Transform(new Vector3(STATUS_X, STATUS_TOP_Y, 0f)),
            STATUS_SIZE,
            Color.LightGreen,
            TextAlignment.Right,
            TextWrapping.None,
            STATUS_MAX_WIDTH
        );

        // One paragraph, three sizes, three alignments, two wrappings, and two rotations. The large
        // copy uses Word wrapping and contains a word wider than the 4.2-unit box, so that word
        // overflows its line instead of being split. The small copy uses Character wrapping, which
        // breaks between any two glyphs. Every copy passes the explicit maxWidth box; the rotation
        // is the text plane's transform, not a layout parameter.
        camera.DrawText(
            firaSans,
            PARAGRAPH,
            new Transform(new Vector3(LEFT_COLUMN_X, SIZE_DEMO_TOP_Y, 0f)),
            PARAGRAPH_LARGE_SIZE,
            Color.White,
            TextAlignment.Left,
            TextWrapping.Word,
            PARAGRAPH_MAX_WIDTH
        );
        camera.DrawText(
            firaSans,
            PARAGRAPH,
            new Transform(
                new Vector3(LEFT_COLUMN_X, MEDIUM_DEMO_TOP_Y, 0f),
                _mediumParagraphRotation,
                Vector3.One
            ),
            PARAGRAPH_MEDIUM_SIZE,
            Color.Khaki,
            TextAlignment.Center,
            TextWrapping.Word,
            PARAGRAPH_MAX_WIDTH
        );
        camera.DrawText(
            firaSans,
            PARAGRAPH,
            new Transform(
                new Vector3(LEFT_COLUMN_X, SMALL_DEMO_TOP_Y, 0f),
                _smallParagraphRotation,
                Vector3.One
            ),
            PARAGRAPH_SMALL_SIZE,
            Color.PaleTurquoise,
            TextAlignment.Right,
            TextWrapping.Character,
            PARAGRAPH_MAX_WIDTH
        );

        // Both faces cover Latin and Cyrillic. Each heading is drawn in the face it labels, and each
        // paragraph wraps inside a 3.9-unit box.
        DrawParagraph(
            camera,
            firaSans,
            "Fira Sans, Latin",
            LATIN_PARAGRAPH,
            RIGHT_COLUMN_X,
            ROW_ONE_HEADING_Y,
            ROW_ONE_PARAGRAPH_Y
        );
        DrawParagraph(
            camera,
            lora,
            "Lora, Latin",
            LATIN_PARAGRAPH,
            LORA_COLUMN_X,
            ROW_ONE_HEADING_Y,
            ROW_ONE_PARAGRAPH_Y
        );
        DrawParagraph(
            camera,
            firaSans,
            "Fira Sans, Cyrillic",
            CYRILLIC_PARAGRAPH,
            RIGHT_COLUMN_X,
            ROW_TWO_HEADING_Y,
            ROW_TWO_PARAGRAPH_Y
        );
        DrawParagraph(
            camera,
            lora,
            "Lora, Cyrillic",
            CYRILLIC_PARAGRAPH,
            LORA_COLUMN_X,
            ROW_TWO_HEADING_Y,
            ROW_TWO_PARAGRAPH_Y
        );

        // A billboard label floats off the wall and turns to face the orbiting camera. It uses Lora,
        // so the custom face is visible in the camera-facing path as well as on the wall.
        camera.DrawTextBillboard(
            lora,
            "Billboard: this label always faces the camera",
            new Vector3(BILLBOARD_X, BILLBOARD_Y, BILLBOARD_Z),
            BILLBOARD_SIZE,
            Color.PaleGoldenRod
        );

        // One more Greek letter is appended every 30 ticks. Greek is not drawn anywhere else in the
        // scene, so each new letter starts a fresh asynchronous MSDF generation.
        string lazyLine = string.Concat(LAZY_PREFIX, LAZY_GREEK.AsSpan(0, _lazyGlyphCount));
        camera.DrawText(
            firaSans,
            lazyLine,
            new Transform(new Vector3(LEFT_COLUMN_X, LAZY_LINE_TOP_Y, 0f)),
            LAZY_LINE_SIZE,
            Color.Turquoise
        );
    }

    private static void DrawParagraph(
        Camera camera,
        Font font,
        string heading,
        string paragraph,
        float x,
        float headingY,
        float paragraphY
    )
    {
        camera.DrawText(
            font,
            heading,
            new Transform(new Vector3(x, headingY, 0f)),
            HEADING_SIZE,
            Color.Gold
        );
        camera.DrawText(
            font,
            paragraph,
            new Transform(new Vector3(x, paragraphY, 0f)),
            BODY_SIZE,
            Color.Gainsboro,
            TextAlignment.Left,
            TextWrapping.Word,
            BODY_MAX_WIDTH
        );
    }

    private void UpdateOrbit(IInput input)
    {
        // Drag to orbit; the wheel moves the camera closer or farther away. Screen Y grows
        // downward, so adding the vertical delta raises the camera when the pointer moves down.
        if (input.IsMouseButtonDown(MouseButton.Left))
        {
            Vector2 delta = input.MouseDelta;
            _yawDegrees -= delta.X * ORBIT_DEGREES_PER_PIXEL;

            float pitchLimit = MAX_PITCH_DEGREES;
            _pitchDegrees = Math.Clamp(
                _pitchDegrees + (delta.Y * ORBIT_DEGREES_PER_PIXEL),
                -pitchLimit,
                pitchLimit
            );
        }

        float wheel = input.MouseWheel;
        if (wheel != 0f)
        {
            _distance = Math.Clamp(
                _distance * MathF.Pow(ZOOM_PER_WHEEL_STEP, -wheel),
                MIN_DISTANCE,
                MAX_DISTANCE
            );
        }
    }
}
