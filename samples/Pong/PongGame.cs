using System.Globalization;
using System.Numerics;
using RayBork;

namespace Pong;

/// <summary>
/// A two-paddle Pong match drawn as one screen-space orthographic pass.
/// </summary>
/// <remarks>
/// <para>
/// The gameplay runs on a 1280x720 design playfield with its origin at the top-left and <c>+Y</c>
/// pointing down. Each tick fits that 16:9 playfield into the current render pixels, keeps the
/// remainder as the camera's clear color, and maps design pixels to render pixels when it draws.
/// Sizes passed to the draw helpers are therefore always render pixels.
/// </para>
/// <para>
/// Every draw anchors a camera-facing plane with
/// <see cref="Camera.ScreenToWorldTransform(Vector2, float)"/>. The camera is orthographic with a
/// visible height equal to the render height, so the plane's world-units-per-pixel scale is exactly
/// one and the design layout needs no manual projection math.
/// </para>
/// </remarks>
internal sealed class PongGame : IGame {
    private const string SETTINGS_PATH = "/data/game";

    private const float DESIGN_WIDTH = 1280f;
    private const float DESIGN_HEIGHT = 720f;
    private const float SCREEN_PLANE_DISTANCE = 1f;
    private const float CAMERA_NEAR = 0.01f;
    private const float CAMERA_FAR = 100f;

    private const float BORDER_MARGIN = 8f;
    private const float BORDER_THICKNESS = 4f;
    private const float NET_WIDTH = 4f;
    private const float NET_DASH = 18f;
    private const float NET_GAP = 14f;
    private const float NET_MARGIN = 20f;

    private const float PADDLE_X_INSET = 56f;
    private const float SCORE_BOX_WIDTH = 140f;
    private const float SCORE_BOX_HEIGHT = 88f;
    private const float SCORE_BOX_TOP = 40f;
    private const float SCORE_BOX_GAP = 160f;
    private const float SCORE_FONT_SIZE = 64f;
    private const float STATUS_FONT_SIZE = 18f;
    private const float STATUS_CENTER_Y = DESIGN_HEIGHT - BORDER_MARGIN - BORDER_THICKNESS - 30f;
    private const float OVERLAY_TITLE_FONT_SIZE = 64f;
    private const float OVERLAY_HINT_FONT_SIZE = 22f;
    private const float OVERLAY_TITLE_OFFSET = 26f;
    private const float OVERLAY_HINT_OFFSET = 44f;

    // A fixed 120 Hz step keeps paddle and ball motion independent of the tick rate and stops the
    // ball from crossing a paddle or a side between two collision checks on a slow tick.
    private const float SIMULATION_STEP_SECONDS = 1f / 120f;
    private const float MAX_SIMULATION_SECONDS = 0.25f;
    private const float MAX_BOUNCE_SLOPE = 0.8f;
    private const float SERVE_SLOPE = 0.45f;
    private const float GAMEPAD_DEADZONE = 0.2f;

    private const int PLAYER_ONE = 1;
    private const int PLAYER_TWO = 2;

    private const string CONTROLS_TEXT =
        "W/S and Up/Down move paddles   Space pause   R restart   Esc exit";
    private const string PAUSED_STATUS = "PAUSED   Space resumes   R restarts   Esc exits";
    private const string MATCH_OVER_STATUS = "MATCH OVER   R restarts   Esc exits";
    private const string PAUSE_TITLE = "PAUSED";
    private const string PAUSE_HINT = "Press Space to resume";
    private const string PLAYER_ONE_WINS = "PLAYER ONE WINS";
    private const string PLAYER_TWO_WINS = "PLAYER TWO WINS";
    private const string RESTART_HINT = "Press R to play again";

    private static readonly Color _letterboxColor = Color.FromSrgbBytes(8, 10, 14);
    private static readonly Color _playfieldColor = Color.FromSrgbBytes(18, 26, 42);
    private static readonly Color _borderColor = Color.FromSrgbBytes(92, 74, 52);
    private static readonly Color _netColor = Color.FromSrgbBytes(78, 66, 50);
    private static readonly Color _scoreBoxColor = Color.FromSrgbBytes(26, 36, 56);
    private static readonly Color _ballColor = Color.FromSrgbBytes(255, 226, 138);
    private static readonly Color _scoreColor = Color.FromSrgbBytes(244, 236, 220);
    private static readonly Color _statusColor = Color.FromSrgbBytes(168, 178, 196);
    private static readonly Color _overlayColor = Color.FromSrgbBytes(6, 8, 12, 205);
    private static readonly Color _overlayTitleColor = Color.FromSrgbBytes(255, 206, 112);
    private static readonly Color _overlayHintColor = Color.FromSrgbBytes(224, 216, 200);
    private static readonly Vector2 _playfieldCenter = new(DESIGN_WIDTH * 0.5f, DESIGN_HEIGHT * 0.5f);

    private readonly int _frameLimit;

    private PongSettings? _settings;
    private Texture? _paddleTexture;
    private Font? _monoFont;
    private Vector2 _leftPaddle;
    private Vector2 _rightPaddle;
    private Vector2 _ballPosition;
    private Vector2 _ballVelocity;
    private int _leftScore;
    private int _rightScore;
    private int _winner;
    private bool _paused;
    private float _simulationAccumulator;
    private int _frameCount;

    /// <summary>
    /// Creates the match.
    /// </summary>
    /// <param name="frameLimit">The number of ticks to run, or zero to run until the window closes.</param>
    internal PongGame(int frameLimit) {
        _frameLimit = frameLimit;
    }

    /// <inheritdoc/>
    public async Task Setup(EngineContext context) {
        PongSettings settings = await context.Assets.Load<PongSettings>(SETTINGS_PATH);
        Validate(settings);
        _settings = settings;
        _paddleTexture = await context.Assets.Load<Texture>(settings.PaddleTexture);

        // The mono face is engine-owned and already preloaded; keep the reference for this sample
        // and never dispose it. The flat mono advance keeps the score and status lines steady.
        _monoFont = context.Defaults.DefaultMonoFont;
        ResetMatch();
    }

    /// <inheritdoc/>
    public void Tick(EngineContext context) {
        (float leftAxis, float rightAxis) = UpdateInput(context);
        if (!_paused && _winner == 0) {
            AdvanceSimulation((float)context.DeltaTime, leftAxis, rightAxis);
        }

        // One orthographic pass per tick. Its visible height is the render height, so the screen
        // plane at distance one has a scale of exactly one render pixel per plane unit.
        using Camera camera = context.CreateCamera(
            new Transform(new Vector3(0f, 0f, SCREEN_PLANE_DISTANCE)),
            projection: CameraProjection.Orthographic,
            orthographicHeight: context.Window.RenderHeight,
            clearMode: ClearMode.ColorAndDepth,
            clearColor: _letterboxColor,
            near: CAMERA_NEAR,
            far: CAMERA_FAR);
        Draw(camera);

        _frameCount++;
        if (_frameLimit > 0 && _frameCount >= _frameLimit) {
            context.RequestExit();
        }
    }

    /// <inheritdoc/>
    public Task Shutdown() {
        // The mono font is engine-owned; only the texture load is this sample's reference.
        _paddleTexture?.Dispose();
        _paddleTexture = null;
        _monoFont = null;
        _settings = null;
        return Task.CompletedTask;
    }

    private static void Validate(PongSettings settings) {
        if (string.IsNullOrWhiteSpace(settings.PaddleTexture)) {
            throw new InvalidOperationException(
                $"Pong settings '{SETTINGS_PATH}' must name a non-empty PaddleTexture.");
        }

        RequirePositive(settings.BallRadius, "BallRadius");
        RequirePositive(settings.PaddleWidth, "PaddleWidth");
        RequirePositive(settings.PaddleHeight, "PaddleHeight");
        RequirePositive(settings.PaddleSpeed, "PaddleSpeed");
        RequirePositive(settings.BallSpeed, "BallSpeed");
        if (settings.MaxScore <= 0) {
            throw new InvalidOperationException(
                $"Pong settings '{SETTINGS_PATH}' require a positive MaxScore; got "
                    + $"{settings.MaxScore.ToString(CultureInfo.InvariantCulture)}.");
        }
    }

    private static void RequirePositive(float value, string name) {
        if (!float.IsFinite(value) || value <= 0f) {
            throw new InvalidOperationException(FormattableString.Invariant(
                $"Pong settings '{SETTINGS_PATH}' require a positive finite {name}; got {value}."));
        }
    }

    private void ResetMatch() {
        _leftScore = 0;
        _rightScore = 0;
        _winner = 0;
        _paused = false;
        _simulationAccumulator = 0f;
        _leftPaddle = new Vector2(PADDLE_X_INSET, _playfieldCenter.Y);
        _rightPaddle = new Vector2(DESIGN_WIDTH - PADDLE_X_INSET, _playfieldCenter.Y);

        // The opening serve is fixed so a repeated run starts from the same state with no RNG.
        Serve(towardRight: true);
    }

    private void Serve(bool towardRight) {
        _ballPosition = _playfieldCenter;
        Vector2 direction = new(towardRight ? 1f : -1f, SERVE_SLOPE);
        _ballVelocity = Vector2.Normalize(direction) * _settings!.BallSpeed;
    }

    private (float Left, float Right) UpdateInput(EngineContext context) {
        IInput input = context.Input;
        IGamepad? gamepad = input.GetGamepad(0);
        if (gamepad is not null && !gamepad.IsConnected) {
            gamepad = null;
        }

        if (input.IsKeyPressed(Key.Escape)) {
            context.RequestExit();
        }

        bool restartPressed = input.IsKeyPressed(Key.R)
            || (gamepad?.IsButtonPressed(GamepadButton.Back) ?? false);
        if (restartPressed) {
            ResetMatch();
            return (0f, 0f);
        }

        if (_winner != 0) {
            return (0f, 0f);
        }

        bool pausePressed = input.IsKeyPressed(Key.Space)
            || (gamepad?.IsButtonPressed(GamepadButton.Start) ?? false);
        if (pausePressed) {
            _paused = !_paused;
        }

        if (_paused) {
            return (0f, 0f);
        }

        float left = ReadPaddleAxis(
            input,
            Key.W,
            Key.S,
            gamepad,
            GamepadAxis.LeftY,
            includeDpad: false);
        float right = ReadPaddleAxis(
            input,
            Key.Up,
            Key.Down,
            gamepad,
            GamepadAxis.RightY,
            includeDpad: true);
        return (left, right);
    }

    private static float ReadPaddleAxis(
        IInput input,
        Key up,
        Key down,
        IGamepad? gamepad,
        GamepadAxis axis,
        bool includeDpad) {
        float value = 0f;
        if (input.IsKeyDown(up)) {
            value -= 1f;
        }

        if (input.IsKeyDown(down)) {
            value += 1f;
        }

        if (gamepad is not null) {
            // The engine applies no deadzone, so the sample picks a small one to stop stick drift.
            float stick = gamepad.GetAxis(axis);
            value += MathF.Abs(stick) >= GAMEPAD_DEADZONE ? stick : 0f;
            if (includeDpad) {
                if (gamepad.IsButtonDown(GamepadButton.DpadUp)) {
                    value -= 1f;
                }

                if (gamepad.IsButtonDown(GamepadButton.DpadDown)) {
                    value += 1f;
                }
            }
        }

        return Math.Clamp(value, -1f, 1f);
    }

    private void AdvanceSimulation(float deltaSeconds, float leftAxis, float rightAxis) {
        _simulationAccumulator = MathF.Min(
            _simulationAccumulator + deltaSeconds,
            MAX_SIMULATION_SECONDS);
        while (_simulationAccumulator >= SIMULATION_STEP_SECONDS && _winner == 0) {
            Step(SIMULATION_STEP_SECONDS, leftAxis, rightAxis);
            _simulationAccumulator -= SIMULATION_STEP_SECONDS;
        }
    }

    private void Step(float deltaSeconds, float leftAxis, float rightAxis) {
        PongSettings settings = _settings!;
        float halfPaddleHeight = settings.PaddleHeight * 0.5f;
        float minY = halfPaddleHeight;
        float maxY = DESIGN_HEIGHT - halfPaddleHeight;

        // Screen Y grows down, so a positive axis moves a paddle toward the bottom.
        _leftPaddle.Y = Math.Clamp(
            _leftPaddle.Y + (leftAxis * settings.PaddleSpeed * deltaSeconds),
            minY,
            maxY);
        _rightPaddle.Y = Math.Clamp(
            _rightPaddle.Y + (rightAxis * settings.PaddleSpeed * deltaSeconds),
            minY,
            maxY);

        _ballPosition += _ballVelocity * deltaSeconds;

        if (_ballPosition.Y - settings.BallRadius < 0f) {
            _ballPosition.Y = settings.BallRadius;
            _ballVelocity.Y = MathF.Abs(_ballVelocity.Y);
        } else if (_ballPosition.Y + settings.BallRadius > DESIGN_HEIGHT) {
            _ballPosition.Y = DESIGN_HEIGHT - settings.BallRadius;
            _ballVelocity.Y = -MathF.Abs(_ballVelocity.Y);
        }

        BounceOffPaddle(_leftPaddle, isLeft: true);
        BounceOffPaddle(_rightPaddle, isLeft: false);

        if (_ballPosition.X < 0f) {
            AwardPoint(leftScored: false);
        } else if (_ballPosition.X > DESIGN_WIDTH) {
            AwardPoint(leftScored: true);
        }
    }

    private void BounceOffPaddle(Vector2 paddle, bool isLeft) {
        PongSettings settings = _settings!;
        float halfWidth = settings.PaddleWidth * 0.5f;
        float halfHeight = settings.PaddleHeight * 0.5f;
        BoundingBox box = new(
            new Vector3(paddle.X - halfWidth, paddle.Y - halfHeight, 0f),
            new Vector3(paddle.X + halfWidth, paddle.Y + halfHeight, 0f));
        Vector3 ballCenter = new(_ballPosition.X, _ballPosition.Y, 0f);
        if (!Collision.CheckBoxSphere(box, ballCenter, settings.BallRadius)) {
            return;
        }

        // The box/sphere test is symmetric, so a ball already leaving the paddle must not bounce
        // again while its sphere still overlaps the box.
        if (isLeft ? _ballVelocity.X >= 0f : _ballVelocity.X <= 0f) {
            return;
        }

        // The contact offset along the paddle sets the return angle and keeps the ball at the
        // configured speed; the serve and every bounce therefore share one speed.
        float offset = Math.Clamp((_ballPosition.Y - paddle.Y) / halfHeight, -1f, 1f);
        float horizontal = isLeft ? 1f : -1f;
        _ballVelocity = Vector2.Normalize(new Vector2(horizontal, offset * MAX_BOUNCE_SLOPE))
            * settings.BallSpeed;

        // Separate the sphere from the box so the next step does not retest the same overlap.
        float separation = halfWidth + settings.BallRadius + 0.01f;
        _ballPosition.X = isLeft ? paddle.X + separation : paddle.X - separation;
    }

    private void AwardPoint(bool leftScored) {
        if (leftScored) {
            _leftScore++;
        } else {
            _rightScore++;
        }

        int maxScore = _settings!.MaxScore;
        if (_leftScore >= maxScore) {
            _winner = PLAYER_ONE;
        } else if (_rightScore >= maxScore) {
            _winner = PLAYER_TWO;
        }

        if (_winner != 0) {
            _ballPosition = _playfieldCenter;
            _ballVelocity = Vector2.Zero;
            return;
        }

        // The loser of the point receives the next serve, so control alternates after every score.
        Serve(towardRight: leftScored);
    }

    private void Draw(Camera camera) {
        Layout layout = new(camera.Viewport.Width, camera.Viewport.Height);
        Font font = _monoFont!;
        PongSettings settings = _settings!;
        Vector2 leftScoreCenter = new(
            _playfieldCenter.X - SCORE_BOX_GAP,
            SCORE_BOX_TOP + (SCORE_BOX_HEIGHT * 0.5f));
        Vector2 rightScoreCenter = new(
            _playfieldCenter.X + SCORE_BOX_GAP,
            SCORE_BOX_TOP + (SCORE_BOX_HEIGHT * 0.5f));

        DrawDesignQuad(
            camera,
            layout,
            _playfieldCenter,
            new Vector2(DESIGN_WIDTH, DESIGN_HEIGHT),
            _playfieldColor);
        DrawBorder(camera, layout);
        DrawNet(camera, layout);
        DrawScoreBox(camera, layout, leftScoreCenter);
        DrawScoreBox(camera, layout, rightScoreCenter);

        DrawPaddle(camera, layout, settings, _paddleTexture!, _leftPaddle);
        DrawPaddle(camera, layout, settings, _paddleTexture!, _rightPaddle);
        camera.DrawDisc(
            ScreenTransform(camera, layout.ToPixel(_ballPosition)),
            settings.BallRadius * layout.Scale,
            _ballColor);

        // The shade is a transparent draw, and the planner submits transparent batches in
        // first-record order at equal depth. Record it before the text so every label stays on top.
        bool showOverlay = _paused || _winner != 0;
        if (showOverlay) {
            DrawOverlayShade(camera, layout);
        }

        DrawScoreText(camera, layout, font, _leftScore, leftScoreCenter);
        DrawScoreText(camera, layout, font, _rightScore, rightScoreCenter);

        string status;
        if (_winner != 0) {
            status = MATCH_OVER_STATUS;
        } else if (_paused) {
            status = PAUSED_STATUS;
        } else {
            status = CONTROLS_TEXT;
        }

        DrawCenteredText(
            camera,
            layout,
            font,
            status,
            STATUS_CENTER_Y,
            STATUS_FONT_SIZE,
            _statusColor);

        if (showOverlay) {
            string title = _winner switch {
                PLAYER_ONE => PLAYER_ONE_WINS,
                PLAYER_TWO => PLAYER_TWO_WINS,
                _ => PAUSE_TITLE,
            };
            string hint = _winner != 0 ? RESTART_HINT : PAUSE_HINT;
            DrawOverlayText(camera, layout, font, title, hint);
        }
    }

    private static void DrawBorder(Camera camera, Layout layout) {
        float outerInset = BORDER_MARGIN * 2f;
        float innerInset = (BORDER_MARGIN + BORDER_THICKNESS) * 2f;

        DrawDesignQuad(
            camera,
            layout,
            _playfieldCenter,
            new Vector2(DESIGN_WIDTH - outerInset, DESIGN_HEIGHT - outerInset),
            _borderColor);
        DrawDesignQuad(
            camera,
            layout,
            _playfieldCenter,
            new Vector2(DESIGN_WIDTH - innerInset, DESIGN_HEIGHT - innerInset),
            _playfieldColor);
    }

    private static void DrawNet(Camera camera, Layout layout) {
        float top = BORDER_MARGIN + BORDER_THICKNESS + NET_MARGIN;
        float bottom = DESIGN_HEIGHT - top;
        for (float y = top; y < bottom; y += NET_DASH + NET_GAP) {
            float height = MathF.Min(NET_DASH, bottom - y);
            DrawDesignQuad(
                camera,
                layout,
                new Vector2(_playfieldCenter.X, y + (height * 0.5f)),
                new Vector2(NET_WIDTH, height),
                _netColor);
        }
    }

    private static void DrawScoreBox(Camera camera, Layout layout, Vector2 boxCenter) {
        DrawDesignQuad(
            camera,
            layout,
            boxCenter,
            new Vector2(SCORE_BOX_WIDTH, SCORE_BOX_HEIGHT),
            _scoreBoxColor);
    }

    private static void DrawScoreText(
        Camera camera,
        Layout layout,
        Font font,
        int score,
        Vector2 boxCenter) {
        string text = score.ToString(CultureInfo.InvariantCulture);
        DrawCenteredTextInBox(camera, layout, font, text, boxCenter, SCORE_FONT_SIZE, _scoreColor);
    }

    private static void DrawPaddle(
        Camera camera,
        Layout layout,
        PongSettings settings,
        Texture texture,
        Vector2 designCenter) {
        camera.DrawQuad(
            ScreenTransform(camera, layout.ToPixel(designCenter)),
            new Vector2(settings.PaddleWidth, settings.PaddleHeight) * layout.Scale,
            texture);
    }

    private static void DrawOverlayShade(Camera camera, Layout layout) {
        DrawDesignQuad(
            camera,
            layout,
            _playfieldCenter,
            new Vector2(DESIGN_WIDTH, DESIGN_HEIGHT),
            _overlayColor,
            RenderState.AlphaBlend);
    }

    private static void DrawOverlayText(
        Camera camera,
        Layout layout,
        Font font,
        string title,
        string hint) {
        DrawCenteredText(
            camera,
            layout,
            font,
            title,
            (DESIGN_HEIGHT * 0.5f) - OVERLAY_TITLE_OFFSET,
            OVERLAY_TITLE_FONT_SIZE,
            _overlayTitleColor);
        DrawCenteredText(
            camera,
            layout,
            font,
            hint,
            (DESIGN_HEIGHT * 0.5f) + OVERLAY_HINT_OFFSET,
            OVERLAY_HINT_FONT_SIZE,
            _overlayHintColor);
    }

    private static void DrawDesignQuad(
        Camera camera,
        Layout layout,
        Vector2 designCenter,
        Vector2 designSize,
        Color color,
        RenderState? renderState = null) {
        camera.DrawQuad(
            ScreenTransform(camera, layout.ToPixel(designCenter)),
            designSize * layout.Scale,
            color,
            renderState);
    }

    private static void DrawCenteredText(
        Camera camera,
        Layout layout,
        Font font,
        string text,
        float designCenterY,
        float designFontSize,
        Color color) {
        Vector2 center = new(_playfieldCenter.X, designCenterY);
        DrawCenteredTextInBox(camera, layout, font, text, center, designFontSize, color);
    }

    private static void DrawCenteredTextInBox(
        Camera camera,
        Layout layout,
        Font font,
        string text,
        Vector2 designCenter,
        float designFontSize,
        Color color) {
        float pixelSize = designFontSize * layout.Scale;
        Vector2 measured = font.Measure(text, pixelSize);
        Vector2 center = layout.ToPixel(designCenter);
        Vector2 origin = new(
            center.X - (measured.X * 0.5f),
            center.Y - (measured.Y * 0.5f));
        camera.DrawText(
            font,
            text,
            ScreenTransform(camera, origin),
            pixelSize,
            color);
    }

    private static Transform ScreenTransform(Camera camera, Vector2 pixel) =>
        camera.ScreenToWorldTransform(pixel, SCREEN_PLANE_DISTANCE);

    private readonly struct Layout {
        internal Layout(float renderWidth, float renderHeight) {
            float width = renderWidth;
            float height = width * (DESIGN_HEIGHT / DESIGN_WIDTH);
            if (height > renderHeight) {
                height = renderHeight;
                width = height * (DESIGN_WIDTH / DESIGN_HEIGHT);
            }

            Left = (renderWidth - width) * 0.5f;
            Top = (renderHeight - height) * 0.5f;
            Scale = width / DESIGN_WIDTH;
        }

        internal float Left { get; }

        internal float Top { get; }

        /// <summary>Gets the render pixels per design pixel.</summary>
        internal float Scale { get; }

        internal Vector2 ToPixel(Vector2 design) =>
            new(Left + (design.X * Scale), Top + (design.Y * Scale));
    }
}
