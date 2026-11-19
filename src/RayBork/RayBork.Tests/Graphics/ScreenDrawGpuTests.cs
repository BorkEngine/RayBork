using System.Numerics;
using RayBork.Internal;

namespace RayBork.Tests;

/// <summary>
/// GPU integration tests for the M7 screen-space drawing model through the production camera, draw
/// helpers, and <see cref="Vulkan12Backend"/>.
/// </summary>
/// <remarks>
/// Every test loads the real assets through <see cref="PrimitiveDrawGpuHarness"/> and probes linear
/// scene-target pixels with explicit tolerances. GPU test classes run in parallel while
/// <see cref="SdlPlatform"/> serializes the process-global SDL and Vulkan loader setup; a host without
/// a usable Vulkan device is skipped.
/// </remarks>
public sealed class ScreenDrawGpuTests
{
    private static readonly Color _clearColor = new(0.05f, 0.1f, 0.2f, 1f);
    private static readonly Color _topLeft = new(1f, 0f, 0f, 1f);
    private static readonly Color _topRight = new(0f, 1f, 0f, 1f);
    private static readonly Color _bottomLeft = new(0f, 0f, 1f, 1f);
    private static readonly Color _bottomRight = new(1f, 1f, 0f, 1f);

    /// <summary>The tolerance for a color that the target wrote unchanged.</summary>
    private const float COLOR_TOLERANCE = 2e-3f;

    /// <summary>
    /// The tolerance for a blended color; the half-float scene target stores only a few steps of
    /// precision.
    /// </summary>
    private const float BLEND_TOLERANCE = 4e-3f;

    /// <summary>
    /// The tolerance for an MSDF ink probe; coverage still rounds at the ink edge.
    /// </summary>
    private const float INK_TOLERANCE = 2e-2f;

    /// <summary>The screen-plane distance for screen-space quads.</summary>
    private const float SCREEN_DISTANCE = 1f;

    /// <summary>The em height, in render pixels, of the score text.</summary>
    private const float TEXT_PIXEL_SIZE = 120f;

    /// <summary>The two-glyph score whose second glyph has a solid stem through its center.</summary>
    private const string SCORE_TEXT = "HI";

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void PixelAlignedTexturedQuadSamplesEachQuadrantAndLeavesTheClearOutside()
    {
        using PrimitiveDrawGpuHarness harness = PrimitiveDrawGpuHarness.CreateOrSkip(
            sampleCount: 1
        );
        Texture texture = CreateQuadrantTexture(harness);

        // Both destinations start on a whole render pixel, so every pixel center lies inside exactly one
        // quadrant. The first is even in both axes. The second is odd in both axes, which puts the texture
        // quadrant boundary exactly on the center of pixel column 104 and of row 65; the probes skip
        // those two pixels so nearest sampling never has to break a tie.
        Vector2 anchoredPixel = default;
        Transform anchored = default;
        harness.SubmitOrthographic(
            camera =>
            {
                Rect destination = new(24f, 24f, 32f, 24f);
                anchored = ScreenRectTransform(camera, destination);
                anchoredPixel = camera.WorldToScreen(anchored.Position);
                DrawTexturedRect(camera, texture, destination);
                DrawTexturedRect(camera, texture, new Rect(101f, 61f, 7f, 9f));
            },
            clearColor: _clearColor,
            height: harness.Height
        );

        // Passing the target height makes the plane support one world unit per render pixel at 100% DPI:
        // the rectangle's center lands on its render pixel and the transform has a unit scale.
        Assert.Equal(40f, anchoredPixel.X, 1e-3f);
        Assert.Equal(36f, anchoredPixel.Y, 1e-3f);
        Assert.Equal(1f, anchored.Scale.X, 1e-5f);
        Assert.Equal(1f, anchored.Scale.Y, 1e-5f);

        // Even destination: each quadrant spans sixteen columns by twelve rows.
        AssertColorNear(_topLeft, harness.ReadColor(32, 30));
        AssertColorNear(_topRight, harness.ReadColor(48, 30));
        AssertColorNear(_bottomLeft, harness.ReadColor(32, 42));
        AssertColorNear(_bottomRight, harness.ReadColor(48, 42));

        // Odd destination: the top-left cell is three columns wide and the top row four rows tall.
        AssertColorNear(_topLeft, harness.ReadColor(102, 63));
        AssertColorNear(_topRight, harness.ReadColor(106, 63));
        AssertColorNear(_bottomLeft, harness.ReadColor(102, 67));
        AssertColorNear(_bottomRight, harness.ReadColor(106, 67));

        // One pixel outside either destination is the pass clear color, so the quad edges are exactly on
        // the requested pixel boundaries and neither destination overdraws its neighbor.
        AssertColorNear(_clearColor, harness.ReadColor(23, 30));
        AssertColorNear(_clearColor, harness.ReadColor(24, 23));
        AssertColorNear(_clearColor, harness.ReadColor(56, 30));
        AssertColorNear(_clearColor, harness.ReadColor(24, 48));
        AssertColorNear(_clearColor, harness.ReadColor(100, 65));
        AssertColorNear(_clearColor, harness.ReadColor(105, 70));

        // Two records over the same quad mesh, mesh program, opaque state, full source rect, and texture
        // merge into one instanced draw.
        RenderStats stats = harness.Host.Backend.Stats;
        Assert.Equal(1, stats.DrawCalls);
        Assert.Equal(2, stats.Instances);
        Assert.Equal(4, stats.Triangles);
        Assert.Equal(0, stats.CulledInstances);
        Assert.Empty(harness.Host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void SourceRectangleSamplingSelectsTheNormalizedSubRectangle()
    {
        using PrimitiveDrawGpuHarness harness = PrimitiveDrawGpuHarness.CreateOrSkip(
            sampleCount: 1
        );
        Texture texture = CreateQuadrantTexture(harness);

        harness.SubmitOrthographic(
            camera =>
            {
                // The full-texture form samples the authored (0, 0, 1, 1) rectangle.
                DrawTexturedRect(camera, texture, new Rect(24f, 24f, 32f, 32f));
                // The source form samples the texture's top-right cell over the whole destination quad.
                DrawTexturedRect(
                    camera,
                    texture,
                    new Rect(88f, 24f, 32f, 32f),
                    new Rect(0.5f, 0f, 0.5f, 0.5f)
                );
                // The bottom-left source cell proves the V axis is offset as well as the U axis.
                DrawTexturedRect(
                    camera,
                    texture,
                    new Rect(152f, 24f, 32f, 32f),
                    new Rect(0f, 0.5f, 0.5f, 0.5f)
                );
            },
            clearColor: _clearColor,
            height: harness.Height
        );

        // The full-texture form keeps the four cells in the authored orientation.
        AssertColorNear(_topLeft, harness.ReadColor(32, 32));
        AssertColorNear(_topRight, harness.ReadColor(48, 32));
        AssertColorNear(_bottomLeft, harness.ReadColor(32, 48));
        AssertColorNear(_bottomRight, harness.ReadColor(48, 48));

        // The top-right source cell covers the entire destination quad, including its corners.
        AssertColorNear(_topRight, harness.ReadColor(92, 28));
        AssertColorNear(_topRight, harness.ReadColor(104, 40));
        AssertColorNear(_topRight, harness.ReadColor(116, 52));

        // The bottom-left source cell covers the entire destination quad as well.
        AssertColorNear(_bottomLeft, harness.ReadColor(168, 40));
        AssertColorNear(_bottomLeft, harness.ReadColor(160, 28));

        // The gap between the destinations stays clear.
        AssertColorNear(_clearColor, harness.ReadColor(72, 40));

        // The source rectangle is per-instance data, so the three forms stay in one draw call.
        RenderStats stats = harness.Host.Backend.Stats;
        Assert.Equal(1, stats.DrawCalls);
        Assert.Equal(3, stats.Instances);
        Assert.Equal(6, stats.Triangles);
        Assert.Equal(0, stats.CulledInstances);
        Assert.Empty(harness.Host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void AlphaBlendedSolidQuadBlendsOverTheClearAndOpaqueQuadCoversIt()
    {
        using PrimitiveDrawGpuHarness harness = PrimitiveDrawGpuHarness.CreateOrSkip(
            sampleCount: 1
        );
        Color blendedTint = new(0.9f, 0.2f, 0.1f, 0.5f);
        Color opaqueTint = new(0.1f, 0.8f, 0.3f, 1f);
        Color textureColor = new(0.8f, 0.4f, 0.2f, 1f);
        Color texturedTint = new(1f, 1f, 1f, 0.5f);
        Texture texture = harness.CreateTexture(1, 1, textureColor);

        harness.SubmitOrthographic(
            camera =>
            {
                DrawSolidRect(
                    camera,
                    new Rect(40f, 40f, 80f, 60f),
                    blendedTint,
                    RenderState.AlphaBlend
                );
                DrawSolidRect(
                    camera,
                    new Rect(200f, 40f, 80f, 60f),
                    opaqueTint,
                    RenderState.Opaque
                );
                // The textured form multiplies the texture by the per-instance tint on the mesh
                // program, so the same source-over result applies to the mesh-program fragment.
                DrawTexturedRect(
                    camera,
                    texture,
                    new Rect(120f, 120f, 80f, 60f),
                    texturedTint,
                    RenderState.AlphaBlend
                );
            },
            clearColor: _clearColor,
            height: harness.Height
        );

        // The alpha preset is source-over: src.rgb * src.a + dst.rgb * (1 - src.a).
        Color expectedBlend = SourceOver(blendedTint, _clearColor);
        Color expectedTextureBlend = SourceOver(
            new Color(
                Unorm(textureColor).R,
                Unorm(textureColor).G,
                Unorm(textureColor).B,
                texturedTint.A
            ),
            _clearColor
        );
        AssertColorNear(expectedBlend, harness.ReadColor(80, 70), BLEND_TOLERANCE);
        AssertColorNear(opaqueTint, harness.ReadColor(240, 70));
        AssertColorNear(expectedTextureBlend, harness.ReadColor(160, 150), BLEND_TOLERANCE);
        AssertColorNear(_clearColor, harness.ReadColor(160, 70));

        // The two primitive draws split on the effective render state; the textured record uses the
        // mesh program, so it forms a third draw call.
        RenderStats stats = harness.Host.Backend.Stats;
        Assert.Equal(3, stats.DrawCalls);
        Assert.Equal(3, stats.Instances);
        Assert.Equal(6, stats.Triangles);
        Assert.Equal(0, stats.CulledInstances);
        Assert.Empty(harness.Host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void TexturedQuadsBatchAcrossTexturesAndSplitOnlyOnRenderState()
    {
        using PrimitiveDrawGpuHarness harness = PrimitiveDrawGpuHarness.CreateOrSkip(
            sampleCount: 1
        );
        Texture red = harness.CreateTexture(1, 1, new Color(1f, 0f, 0f, 1f));
        Texture blue = harness.CreateTexture(1, 1, new Color(0f, 0f, 1f, 1f));

        // Two records with different shared textures use the same quad mesh, mesh program, source
        // rectangle, and opaque state, so the per-instance texture bindings keep them in one batch.
        RenderFramePacket packet = harness.SubmitOrthographic(
            camera =>
            {
                DrawTexturedRect(camera, red, new Rect(40f, 40f, 64f, 64f));
                DrawTexturedRect(camera, blue, new Rect(200f, 40f, 64f, 64f));
            },
            clearColor: _clearColor,
            height: harness.Height
        );

        RenderStats batched = harness.Host.Backend.Stats;
        Assert.Equal(1, batched.DrawCalls);
        Assert.Equal(2, batched.Instances);
        Assert.Equal(4, batched.Triangles);
        Assert.Equal(0, batched.CulledInstances);
        AssertColorNear(new Color(1f, 0f, 0f, 1f), harness.ReadColor(72, 72));
        AssertColorNear(new Color(0f, 0f, 1f, 1f), harness.ReadColor(232, 72));
        AssertColorNear(_clearColor, harness.ReadColor(152, 72));

        // The textured quad path records the built-in mesh program and its opaque state, and the two
        // textures are per-instance bindings rather than a batch-key difference.
        FramePlan plan = new BatchPlanner().Plan(packet);
        PassPlan passPlan = Assert.Single(plan.Passes);
        PlannedBatch texturedBatch = Assert.Single(passPlan.Batches);
        Assert.Same(harness.Primitives.MeshMaterial.Program.Lifetime, texturedBatch.Program);
        Assert.Equal(RenderState.Opaque, texturedBatch.RenderState);
        Assert.Same(red, texturedBatch.Instances[0].Record.GetTextureBindings()[0]);
        Assert.Same(blue, texturedBatch.Instances[1].Record.GetTextureBindings()[0]);

        // One texture and three tints: the two opaque records merge into a two-instance batch and the
        // alpha-blended record forms its own batch, so the state difference splits the draw call.
        Texture white = harness.CreateTexture(1, 1, Color.White);
        Color firstOpaque = new(0.9f, 0.1f, 0.1f, 1f);
        Color alpha = new(0.1f, 0.9f, 0.1f, 1f);
        Color secondOpaque = new(0.1f, 0.1f, 0.9f, 1f);
        harness.SubmitOrthographic(
            camera =>
            {
                DrawTexturedRect(
                    camera,
                    white,
                    new Rect(32f, 80f, 64f, 64f),
                    firstOpaque,
                    RenderState.Opaque
                );
                DrawTexturedRect(
                    camera,
                    white,
                    new Rect(128f, 80f, 64f, 64f),
                    alpha,
                    RenderState.AlphaBlend
                );
                DrawTexturedRect(
                    camera,
                    white,
                    new Rect(224f, 80f, 64f, 64f),
                    secondOpaque,
                    RenderState.Opaque
                );
            },
            clearColor: _clearColor,
            height: harness.Height
        );

        RenderStats split = harness.Host.Backend.Stats;
        Assert.Equal(2, split.DrawCalls);
        Assert.Equal(3, split.Instances);
        Assert.Equal(6, split.Triangles);
        Assert.Equal(0, split.CulledInstances);
        AssertColorNear(firstOpaque, harness.ReadColor(64, 112));
        AssertColorNear(alpha, harness.ReadColor(160, 112));
        AssertColorNear(secondOpaque, harness.ReadColor(256, 112));
        Assert.Empty(harness.Host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void DiscAndSolidQuadRenderTheirInteriorsAndLeaveTheClearOutside()
    {
        using PrimitiveDrawGpuHarness harness = PrimitiveDrawGpuHarness.CreateOrSkip(
            sampleCount: 1
        );
        Color quadColor = new(0.9f, 0.3f, 0.1f, 1f);
        Color discColor = new(0.1f, 0.6f, 0.9f, 1f);

        harness.SubmitOrthographic(
            camera =>
            {
                DrawSolidRect(camera, new Rect(40f, 40f, 64f, 40f), quadColor, RenderState.Opaque);
                // The disc center is the center of pixel (240, 100); the probe below reads pixel
                // (251, 111), about 15.6 pixels away on a 45 degree diagonal. The disc fan has ten
                // degree radial edges, so that pixel sits between the edges at 40 and 50 degrees, is
                // 8 pixels inside the rim, and is far from the shared fan vertex.
                Transform discTransform = camera.ScreenToWorldTransform(
                    new Vector2(240.5f, 100.5f),
                    SCREEN_DISTANCE
                );
                camera.DrawDisc(discTransform, 24f, discColor);
            },
            clearColor: _clearColor,
            height: harness.Height
        );

        AssertColorNear(quadColor, harness.ReadColor(72, 60));
        AssertColorNear(discColor, harness.ReadColor(251, 111));

        // The probes are one pixel outside the shapes: just left of the quad, and well clear of the disc
        // above and to its right.
        AssertColorNear(_clearColor, harness.ReadColor(39, 60));
        AssertColorNear(_clearColor, harness.ReadColor(240, 73));
        AssertColorNear(_clearColor, harness.ReadColor(268, 100));

        // The quad is two triangles and the engine disc mesh is thirty-six.
        RenderStats stats = harness.Host.Backend.Stats;
        Assert.Equal(2, stats.DrawCalls);
        Assert.Equal(2, stats.Instances);
        Assert.Equal(38, stats.Triangles);
        Assert.Equal(0, stats.CulledInstances);
        Assert.Empty(harness.Host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ScreenSpaceScoreAndSpriteShareOneFrame()
    {
        using PrimitiveDrawGpuHarness harness = PrimitiveDrawGpuHarness.CreateOrSkip(
            sampleCount: 1
        );
        Font font = harness.LoadFont();
        harness.WarmGlyphs(font, SCORE_TEXT);
        (TextLayoutResult layout, TextGlyphInstance[] glyphs) = ArrangeText(font, SCORE_TEXT);
        Color textColor = new(0.9f, 0.35f, 0.1f, 1f);
        Color spriteColor = new(0.2f, 0.8f, 0.4f, 1f);
        Texture spriteTexture = harness.CreateTexture(1, 1, spriteColor);
        (uint X, uint Y) ink = default;

        harness.SubmitOrthographic(
            camera =>
            {
                // The screen transform scale is one world unit per render pixel, so a text layout at 120
                // units gives a 120-pixel em height and the block's layout bounds center on pixel (80, 100).
                Transform textTransform = ScreenTextTransform(
                    camera,
                    layout,
                    new Vector2(80f, 100f)
                );
                camera.DrawText(font, SCORE_TEXT, textTransform, TEXT_PIXEL_SIZE, textColor);
                ink = GlyphCenterPixel(camera, glyphs[1], textTransform);
                DrawTexturedRect(camera, spriteTexture, new Rect(220f, 60f, 60f, 60f));
            },
            clearColor: _clearColor,
            height: harness.Height
        );

        // The capital I stem fills the center of its padded glyph quad, so a frozen glyph texture or a
        // mis-transformed block cannot leave that pixel at the clear color.
        AssertColorNear(textColor, harness.ReadColor(ink.X, ink.Y), INK_TOLERANCE);
        AssertColorNear(spriteColor, harness.ReadColor(250, 90));
        AssertColorNear(_clearColor, harness.ReadColor(20, 20));

        // Two glyph instances over the MSDF program and one textured quad over the mesh program.
        RenderStats stats = harness.Host.Backend.Stats;
        Assert.Equal(2, stats.DrawCalls);
        Assert.Equal(3, stats.Instances);
        Assert.Equal(6, stats.Triangles);
        Assert.Equal(0, stats.CulledInstances);
        Assert.Empty(harness.Host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void FullViewportQuadIsNotFrustumCulled()
    {
        using PrimitiveDrawGpuHarness harness = PrimitiveDrawGpuHarness.CreateOrSkip(
            sampleCount: 1
        );
        Color color = new(0.2f, 0.5f, 0.8f, 1f);
        uint width = harness.Width;
        uint height = harness.Height;

        // The quad's world bounds sit exactly on the viewport planes, the case the frustum
        // Intersects tolerance exists for: the positive corner is on the plane, not behind it.
        harness.SubmitOrthographic(
            camera =>
            {
                Vector2 center = new(width / 2f, height / 2f);
                Transform transform = camera.ScreenToWorldTransform(center, SCREEN_DISTANCE);
                camera.DrawQuad(transform, new Vector2(width, height), color);
            },
            clearColor: _clearColor,
            height: height
        );

        RenderStats stats = harness.Host.Backend.Stats;
        Assert.Equal(1, stats.DrawCalls);
        Assert.Equal(1, stats.Instances);
        Assert.Equal(2, stats.Triangles);
        Assert.Equal(0, stats.CulledInstances);

        // Every corner pixel and the center are covered by the quad, not by the clear color.
        AssertColorNear(color, harness.ReadColor(0, 0));
        AssertColorNear(color, harness.ReadColor(width - 1, 0));
        AssertColorNear(color, harness.ReadColor(0, height - 1));
        AssertColorNear(color, harness.ReadColor(width - 1, height - 1));
        AssertColorNear(color, harness.ReadColor(width / 2, height / 2));
        Assert.Empty(harness.Host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void OrthographicCameraRendersThreeDimensionalCubeGeometry()
    {
        using PrimitiveDrawGpuHarness harness = PrimitiveDrawGpuHarness.CreateOrSkip(
            sampleCount: 1
        );
        Color nearColor = new(0.9f, 0.2f, 0.1f, 1f);
        Color farColor = new(0.1f, 0.3f, 0.9f, 1f);
        Texture texture = harness.CreateTexture(1, 1, Color.White);
        Shader material = harness.Primitives.MeshMaterial.Program.CreateMaterial();
        using Model model = new(new ModelPart("cube", harness.Primitives.Cube, material));

        // The orthographic height of four world units maps to the target height, so one world unit is a
        // quarter of the target height. The far cube spans [-2, 2] world units and the near cube spans
        // [-1, 1]; the near cube's front face is closer to the camera and wins the depth test at the
        // center, while the far cube stays visible around it.
        harness.SubmitOrthographic(
            camera =>
            {
                material.SetUniform(PrimitiveDrawGpuHarness.MESH_TEXTURE_BINDING, texture);
                camera.DrawModel(
                    model,
                    new Transform(Vector3.Zero, Quaternion.Identity, new Vector3(4f)),
                    farColor
                );
                camera.DrawModel(
                    model,
                    new Transform(new Vector3(0f, 0f, 2f), Quaternion.Identity, new Vector3(2f)),
                    nearColor
                );
            },
            transform: new Transform(new Vector3(0f, 0f, 5f)),
            clearColor: _clearColor,
            height: 4f
        );

        uint centerX = harness.Width / 2;
        uint centerY = harness.Height / 2;
        uint pixelsPerUnit = harness.Height / 4;
        uint farCubePixel = centerX + ((pixelsPerUnit * 3) / 2);
        uint outsidePixel = centerX + ((pixelsPerUnit * 12) / 5);
        AssertColorNear(nearColor, harness.ReadColor(centerX, centerY));
        AssertColorNear(farColor, harness.ReadColor(farCubePixel, centerY));
        AssertColorNear(_clearColor, harness.ReadColor(outsidePixel, centerY));

        // Both cubes are the engine cube mesh over the mesh program and the opaque state, so they batch
        // into one instanced draw of two cubes.
        RenderStats stats = harness.Host.Backend.Stats;
        Assert.Equal(1, stats.DrawCalls);
        Assert.Equal(2, stats.Instances);
        Assert.Equal(24, stats.Triangles);
        Assert.Equal(0, stats.CulledInstances);
        Assert.Empty(harness.Host.Instance.ConsumeValidationErrors());
    }

    /// <summary>Creates the 2x2 nearest-sampled texture holding the four quadrant colors.</summary>
    private static Texture CreateQuadrantTexture(PrimitiveDrawGpuHarness harness) =>
        harness.CreateTexture(2, 2, _topLeft, _topRight, _bottomLeft, _bottomRight);

    /// <summary>Draws one textured quad into a render-pixel rectangle using the full texture.</summary>
    private static void DrawTexturedRect(
        Camera camera,
        Texture texture,
        Rect destination,
        Color? tint = null,
        RenderState? renderState = null
    )
    {
        Transform transform = ScreenRectTransform(camera, destination);
        camera.DrawQuad(transform, destination.Size, texture, tint, renderState);
    }

    /// <summary>Draws one textured quad with an explicit normalized source rectangle.</summary>
    private static void DrawTexturedRect(
        Camera camera,
        Texture texture,
        Rect destination,
        Rect source
    )
    {
        Transform transform = ScreenRectTransform(camera, destination);
        camera.DrawQuad(transform, destination.Size, texture, source);
    }

    /// <summary>Draws one solid quad into a render-pixel rectangle.</summary>
    private static void DrawSolidRect(
        Camera camera,
        Rect destination,
        Color color,
        RenderState renderState
    )
    {
        Transform transform = ScreenRectTransform(camera, destination);
        camera.DrawQuad(transform, destination.Size, color, renderState);
    }

    /// <summary>Gets the screen-plane transform centered on a render-pixel rectangle.</summary>
    private static Transform ScreenRectTransform(Camera camera, Rect destination) =>
        camera.ScreenToWorldTransform(
            new Vector2(
                destination.X + (destination.Width * 0.5f),
                destination.Y + (destination.Height * 0.5f)
            ),
            SCREEN_DISTANCE
        );

    /// <summary>Lays the score out with the engine layout engine at the pixel em height.</summary>
    private static (TextLayoutResult Result, TextGlyphInstance[] Glyphs) ArrangeText(
        Font font,
        string text
    )
    {
        TextLayoutBuffer buffer = new();
        TextLayoutEngine.Arrange(
            font.Metrics,
            text,
            TEXT_PIXEL_SIZE,
            TextAlignment.Left,
            TextWrapping.None,
            0f,
            buffer,
            out TextLayoutResult result
        );
        return (result, buffer.Glyphs.ToArray());
    }

    /// <summary>Places the layout origin so its bounds center on a render-pixel point.</summary>
    /// <remarks>The layout runs <c>+X</c> right and <c>+Y</c> up while render rows increase downward.</remarks>
    private static Transform ScreenTextTransform(
        Camera camera,
        TextLayoutResult layout,
        Vector2 centerPixel
    )
    {
        Vector2 layoutCenter = (layout.BoundsMin + layout.BoundsMax) * 0.5f;
        Vector2 originPixel = new(centerPixel.X - layoutCenter.X, centerPixel.Y + layoutCenter.Y);
        return camera.ScreenToWorldTransform(originPixel, SCREEN_DISTANCE);
    }

    /// <summary>Gets the center pixel of one laid-out glyph's padded quad.</summary>
    private static (uint X, uint Y) GlyphCenterPixel(
        Camera camera,
        TextGlyphInstance glyph,
        Transform transform
    )
    {
        Vector2 local = glyph.Position + (glyph.Size * 0.5f);
        Vector2 screen = camera.WorldToScreen(transform.TransformPoint(new Vector3(local, 0f)));
        return (Pixel(screen.X), Pixel(screen.Y));
    }

    private static uint Pixel(float coordinate) => checked((uint)MathF.Floor(coordinate));

    /// <summary>Gets the source-over result of a straight-alpha source over the destination.</summary>
    private static Color SourceOver(Color source, Color destination) =>
        new(
            (source.R * source.A) + (destination.R * (1f - source.A)),
            (source.G * source.A) + (destination.G * (1f - source.A)),
            (source.B * source.A) + (destination.B * (1f - source.A)),
            1f
        );

    /// <summary>Quantizes a color to the value an RGBA8 texture upload stores.</summary>
    private static Color Unorm(Color color) =>
        new(
            UnormChannel(color.R),
            UnormChannel(color.G),
            UnormChannel(color.B),
            UnormChannel(color.A)
        );

    private static float UnormChannel(float value) =>
        (byte)((int)((Math.Clamp(value, 0f, 1f) * 255f) + 0.5f)) / 255f;

    private static void AssertColorNear(
        Color expected,
        (float R, float G, float B) actual,
        float tolerance = COLOR_TOLERANCE
    )
    {
        Assert.True(
            MathF.Abs(expected.R - actual.R) <= tolerance,
            $"Red {actual.R} is not within {tolerance} of {expected.R}."
        );
        Assert.True(
            MathF.Abs(expected.G - actual.G) <= tolerance,
            $"Green {actual.G} is not within {tolerance} of {expected.G}."
        );
        Assert.True(
            MathF.Abs(expected.B - actual.B) <= tolerance,
            $"Blue {actual.B} is not within {tolerance} of {expected.B}."
        );
    }
}
