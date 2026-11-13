using System.Numerics;
using RayBork.Internal;

namespace RayBork.Tests;

/// <summary>
/// GPU integration tests for the M5 camera primitive draw helpers: they record the production
/// <see cref="Camera"/> helpers over the real engine primitive assets, submit the sealed camera packet
/// through <see cref="Vulkan12Backend"/>, and assert validation-layer cleanliness, render statistics,
/// and stable linear scene-target pixel probes.
/// </summary>
/// <remarks>
/// Every test loads <see cref="PrimitiveAssets"/> from the built-in pack through a real GPU asset
/// bridge and worker (<see cref="PrimitiveDrawGpuHarness"/>), so the meshes, programs, render states,
/// batching, pipeline cache, and readback path are the production ones. There are no golden images and
/// no sleeps. Single-pixel assertions use a single-sample target; the validation and statistics case
/// uses the default four-sample target. The tests join the serialized <c>SDL</c> collection because SDL
/// and Vulkan initialization are process global.
/// </remarks>
[Collection("SDL")]
public sealed class PrimitiveDrawGpuTests {
    private static readonly Color _clearColor = new(0.05f, 0.1f, 0.2f, 1f);
    private const float COLOR_TOLERANCE = 2e-3f;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void EveryPrimitiveHelperDrawsWithExpectedStatistics() {
        using PrimitiveDrawGpuHarness harness = PrimitiveDrawGpuHarness.CreateOrSkip(sampleCount: 4);
        Texture texture = harness.CreateTexture(1, 1, new Color(0.25f, 0.5f, 0.75f, 1f));
        Color color = new(0.8f, 0.3f, 0.1f, 1f);

        // The primitive and textured draws sit in front of a camera at the world origin. The grid is
        // centered on the origin, so a second camera is translated far enough back to see every line.
        RenderFramePacket packet = harness.SubmitPerspectivePair(
            camera => {
                camera.DrawLine(new Vector3(-3.5f, -1.2f, -4f), new Vector3(-2.5f, -1.2f, -4f), color);
                camera.DrawLines(
                    [
                        new Vector3(-3f, -1.2f, -4f),
                        new Vector3(-2f, -1.2f, -4f),
                        new Vector3(-1f, -0.8f, -4f),
                    ],
                    color);
                camera.DrawPoint(new Vector3(1f, 1.4f, -4f), color, 0.4f);
                camera.DrawTriangle(
                    new Vector3(2.8f, -0.4f, -4f),
                    new Vector3(3.2f, -0.4f, -4f),
                    new Vector3(2.8f, 0.4f, -4f),
                    color);
                camera.DrawCube(new Vector3(-3f, 0f, -4f), Vector3.One, color);
                camera.DrawSphere(new Vector3(-1.5f, 0f, -4f), 0.7f, color);
                camera.DrawCylinder(new Vector3(0f, -0.5f, -4f), 0.5f, 1f, color, sides: 4);
                camera.DrawPlane(new Vector3(1.5f, -0.5f, -4f), Vector2.One, color);
                camera.DrawWireCube(new Vector3(-1f, 1.3f, -4f), new Vector3(0.6f), color);
                camera.DrawWireSphere(new Vector3(1f, 1.3f, -4f), 0.5f, color);
                camera.DrawBillboard(texture, new Vector3(2.8f, 1.3f, -4f), new Vector2(0.5f), Color.White);
                camera.DrawTexture(texture, new Rect(16f, 16f, 64f, 48f));
            },
            firstTransform: null,
            second: camera => camera.DrawGrid(2, 1f, color),
            secondTransform: new Transform(new Vector3(0f, 0f, 10f)),
            clearColor: _clearColor);

        // The first pass records eight primitive-program batches (cube, sphere, plane, point plus
        // cylinder sides, triangle plus cylinder caps, lines, wire cube, wire sphere) and two mesh
        // program batches (billboard and the screen quad). The second pass adds one grid line batch.
        RenderStats stats = harness.Host.Backend.Stats;
        Assert.Equal(11, stats.DrawCalls);
        Assert.Equal(30, stats.Instances);
        Assert.Equal(997, stats.Triangles);
        Assert.Equal(0, stats.CulledInstances);
        Assert.Equal(4, stats.PipelineCount);
        Assert.Equal(harness.Host.Backend.Info.SampleCount, stats.SampleCount);
        Assert.Equal(4, harness.Host.Settings.SampleCount);

        FramePlan plan = new BatchPlanner().Plan(packet);
        Assert.Equal(2, plan.Passes.Count);
        PlannedBatch gridBatch = Assert.Single(plan.Passes[1].Batches);
        Assert.Equal(PrimitiveTopology.LineList, gridBatch.Topology);
        Assert.Equal(6, gridBatch.InstanceCount);
        Assert.Empty(harness.Host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void OnePixelHorizontalLineCoversOnlyItsRow() {
        using PrimitiveDrawGpuHarness harness = PrimitiveDrawGpuHarness.CreateOrSkip(sampleCount: 1);
        Color lineColor = new(0.9f, 0.3f, 0.1f, 1f);
        uint width = harness.Width;
        uint height = harness.Height;
        // Put the segment exactly on a pixel-center row: a one-pixel line must cover that row only.
        uint row = height / 4;
        float focal = 1f / MathF.Tan(30f * (MathF.PI / 180f));
        float ndcY = (((row + 0.5f) / height) * 2f) - 1f;
        float worldY = -ndcY / focal;

        harness.SubmitPerspective(
            camera => camera.DrawLine(
                new Vector3(-0.25f, worldY, -1f),
                new Vector3(0.25f, worldY, -1f),
                lineColor),
            clearColor: _clearColor);

        AssertColorNear(lineColor, harness.ReadColor(width / 2, row));
        AssertColorNear(_clearColor, harness.ReadColor(width / 2, row + 1));
        AssertColorNear(_clearColor, harness.ReadColor(width / 2, row - 1));
        Assert.Empty(harness.Host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void SolidShapesRenderTheirTintAndPlaneCullsFromBelow() {
        using PrimitiveDrawGpuHarness harness = PrimitiveDrawGpuHarness.CreateOrSkip(sampleCount: 1);
        Color cubeColor = new(0.9f, 0.2f, 0.1f, 1f);
        Color sphereColor = new(0.1f, 0.9f, 0.3f, 1f);
        Color cylinderColor = new(0.2f, 0.3f, 0.9f, 1f);
        Color planeColor = new(0.9f, 0.8f, 0.1f, 1f);
        Vector2 cubePixel = default;
        Vector2 spherePixel = default;
        Vector2 cylinderPixel = default;

        harness.SubmitOrthographic(
            camera => {
                cubePixel = camera.WorldToScreen(new Vector3(-1.2f, 0f, 0f));
                spherePixel = camera.WorldToScreen(Vector3.Zero);
                cylinderPixel = camera.WorldToScreen(new Vector3(1.2f, 0f, 0f));
                camera.DrawCube(new Vector3(-1.2f, 0f, 0f), Vector3.One, cubeColor);
                camera.DrawSphere(Vector3.Zero, 0.5f, sphereColor);
                camera.DrawCylinder(new Vector3(1.2f, -0.5f, 0f), 0.4f, 1f, cylinderColor, sides: 8);
            },
            transform: new Transform(new Vector3(0f, 0f, 5f)),
            clearColor: _clearColor,
            height: 4f);

        AssertColorNear(cubeColor, harness.ReadColor(Pixel(cubePixel.X), Pixel(cubePixel.Y)));
        AssertColorNear(sphereColor, harness.ReadColor(Pixel(spherePixel.X), Pixel(spherePixel.Y)));
        AssertColorNear(cylinderColor, harness.ReadColor(Pixel(cylinderPixel.X), Pixel(cylinderPixel.Y)));

        // The plane faces world +Y: it is front-facing from above and back-face culled from below.
        Transform above = new(
            new Vector3(0f, 3f, 0f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2f),
            Vector3.One);
        harness.SubmitOrthographic(
            camera => camera.DrawPlane(Vector3.Zero, new Vector2(2f), planeColor),
            transform: above,
            clearColor: _clearColor,
            height: 4f);
        AssertColorNear(planeColor, harness.ReadColor(harness.Width / 2, harness.Height / 2));

        Transform below = new(
            new Vector3(0f, -3f, 0f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2f),
            Vector3.One);
        harness.SubmitOrthographic(
            camera => camera.DrawPlane(Vector3.Zero, new Vector2(2f), planeColor),
            transform: below,
            clearColor: _clearColor,
            height: 4f);
        AssertColorNear(_clearColor, harness.ReadColor(harness.Width / 2, harness.Height / 2));
        Assert.Empty(harness.Host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void TriangleWindingControlsFaceCullingAndDegenerateTrianglesDrawNothing() {
        using PrimitiveDrawGpuHarness harness = PrimitiveDrawGpuHarness.CreateOrSkip(sampleCount: 1);
        Color triangleColor = new(0.3f, 0.9f, 0.2f, 1f);
        Vector2 frontPixel = default;
        Vector2 backPixel = default;
        Vector2 degeneratePixel = default;

        harness.SubmitOrthographic(
            camera => {
                // Counter-clockwise as seen by the camera: the reference triangle's +Z front face.
                frontPixel = camera.WorldToScreen(new Vector3(0.8333f, -0.1667f, 0f));
                // The same vertices in clockwise order: the back face is culled.
                backPixel = camera.WorldToScreen(new Vector3(-1.1667f, -0.1667f, 0f));
                degeneratePixel = camera.WorldToScreen(new Vector3(0f, -1.5f, 0f));
                camera.DrawTriangle(
                    new Vector3(0.5f, -0.5f, 0f),
                    new Vector3(1.5f, -0.5f, 0f),
                    new Vector3(0.5f, 0.5f, 0f),
                    triangleColor);
                camera.DrawTriangle(
                    new Vector3(-1.5f, -0.5f, 0f),
                    new Vector3(-1.5f, 0.5f, 0f),
                    new Vector3(-0.5f, -0.5f, 0f),
                    triangleColor);
                camera.DrawTriangle(
                    new Vector3(-0.5f, -1.5f, 0f),
                    new Vector3(0f, -1.5f, 0f),
                    new Vector3(0.5f, -1.5f, 0f),
                    triangleColor);
            },
            transform: new Transform(new Vector3(0f, 0f, 5f)),
            clearColor: _clearColor,
            height: 4f);

        AssertColorNear(triangleColor, harness.ReadColor(Pixel(frontPixel.X), Pixel(frontPixel.Y)));
        AssertColorNear(_clearColor, harness.ReadColor(Pixel(backPixel.X), Pixel(backPixel.Y)));
        AssertColorNear(_clearColor, harness.ReadColor(Pixel(degeneratePixel.X), Pixel(degeneratePixel.Y)));
        RenderStats stats = harness.Host.Backend.Stats;
        Assert.Equal(1, stats.DrawCalls);
        Assert.Equal(2, stats.Instances);
        Assert.Equal(2, stats.Triangles);
        Assert.Empty(harness.Host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void BillboardAndScreenQuadSampleTheUploadedTexture() {
        using PrimitiveDrawGpuHarness harness = PrimitiveDrawGpuHarness.CreateOrSkip(sampleCount: 1);
        Color billboardColor = new(0.25f, 0.5f, 0.75f, 1f);
        Texture billboardTexture = harness.CreateTexture(1, 1, billboardColor);
        Color topLeft = new(1f, 0f, 0f, 1f);
        Color topRight = new(0f, 1f, 0f, 1f);
        Color bottomLeft = new(0f, 0f, 1f, 1f);
        Color bottomRight = new(1f, 1f, 0f, 1f);
        Texture orientationTexture = harness.CreateTexture(2, 2, topLeft, topRight, bottomLeft, bottomRight);
        Rect destination = new(
            harness.Width / 4f,
            harness.Height / 4f,
            harness.Width / 2f,
            harness.Height / 2f);

        harness.SubmitPerspective(
            camera => camera.DrawBillboard(
                billboardTexture,
                new Vector3(0f, 0f, -5f),
                new Vector2(2f),
                Color.White),
            clearColor: _clearColor);
        AssertColorNear(Unorm(billboardColor), harness.ReadColor(harness.Width / 2, harness.Height / 2));

        harness.SubmitPerspective(
            camera => camera.DrawTexture(orientationTexture, destination),
            clearColor: _clearColor);
        AssertScreenQuadProbes(harness, destination, topLeft, topRight, bottomLeft, bottomRight);

        harness.SubmitOrthographic(
            camera => camera.DrawTexture(orientationTexture, destination),
            transform: new Transform(new Vector3(0f, 0f, 5f)),
            clearColor: _clearColor,
            height: 4f);
        AssertScreenQuadProbes(harness, destination, topLeft, topRight, bottomLeft, bottomRight);
        Assert.Empty(harness.Host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void TranslatedPerspectiveCameraKeepsTheScreenQuadUnculled() {
        using PrimitiveDrawGpuHarness harness = PrimitiveDrawGpuHarness.CreateOrSkip(sampleCount: 1);
        Color textureColor = new(0.25f, 0.5f, 0.75f, 1f);
        Texture texture = harness.CreateTexture(1, 1, textureColor);
        Rect destination = new(16f, 16f, 64f, 48f);

        // The reported repro: a camera translated back from the origin with a destination rectangle
        // near the top-left. The old projective instance matrix was frustum-culled behind the camera,
        // so this pass planned no draw at all.
        harness.SubmitPerspective(
            camera => camera.DrawTexture(texture, destination),
            transform: new Transform(new Vector3(0f, 0f, 8f)),
            clearColor: _clearColor);

        RenderStats stats = harness.Host.Backend.Stats;
        Assert.Equal(0, stats.CulledInstances);
        Assert.Equal(1, stats.DrawCalls);
        Assert.Equal(1, stats.Instances);
        Assert.Equal(2, stats.Triangles);
        AssertColorNear(
            Unorm(textureColor),
            harness.ReadColor(
                Pixel(destination.X + (destination.Width * 0.5f)),
                Pixel(destination.Y + (destination.Height * 0.5f))));
        AssertColorNear(_clearColor, harness.ReadColor(Pixel(destination.X * 0.5f), harness.Height / 2));
        Assert.Empty(harness.Host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void OverlayTextureCoversGeometryWhileBillboardBehindACubeIsOccluded() {
        using PrimitiveDrawGpuHarness harness = PrimitiveDrawGpuHarness.CreateOrSkip(sampleCount: 1);
        Color cubeColor = new(0.8f, 0.4f, 0.1f, 1f);
        Color billboardColor = new(0.9f, 0.1f, 0.9f, 1f);
        Color overlayColor = new(0.1f, 0.9f, 0.3f, 1f);
        Texture billboardTexture = harness.CreateTexture(1, 1, billboardColor);
        Texture overlayTexture = harness.CreateTexture(1, 1, overlayColor);
        float centerX = harness.Width / 2f;
        float centerY = harness.Height / 2f;
        float cubeRight = 0f;
        float billboardRight = 0f;

        harness.SubmitPerspective(
            camera => {
                // The cube silhouette is widest at its nearest right edge; the billboard is wider so
                // it is visible between that edge and its own right edge.
                cubeRight = camera.WorldToScreen(new Vector3(2f, 0f, -3f)).X;
                billboardRight = camera.WorldToScreen(new Vector3(5f, 0f, -6f)).X;
                // The overlay covers only the middle fifth of the cube so the probe below stays on
                // the cube alone. Its half size follows the cube's screen width, not the window size.
                float halfSize = (cubeRight - centerX) * 0.2f;
                camera.DrawCube(new Vector3(0f, 0f, -5f), new Vector3(4f), cubeColor);
                camera.DrawBillboard(
                    billboardTexture,
                    new Vector3(0f, 0f, -6f),
                    new Vector2(10f),
                    Color.White);
                camera.DrawTexture(
                    overlayTexture,
                    new Rect(centerX - halfSize, centerY - halfSize, halfSize * 2f, halfSize * 2f));
            },
            clearColor: _clearColor);

        // The overlay ignores scene depth, so it replaces the cube color at the shared pixels.
        AssertColorNear(
            Unorm(overlayColor),
            harness.ReadColor(Pixel(centerX), Pixel(centerY)));
        // Outside the overlay the cube wins, and the billboard behind it stays occluded.
        AssertColorNear(
            cubeColor,
            harness.ReadColor(Pixel((centerX + cubeRight) * 0.5f), Pixel(centerY)));
        // Between the cube and the billboard edge the billboard is visible on its own.
        AssertColorNear(
            Unorm(billboardColor),
            harness.ReadColor(Pixel((cubeRight + billboardRight) * 0.5f), Pixel(centerY)));
        Assert.Empty(harness.Host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void BatchedHelpersShareInstancedDrawsAndLinesCountNoTriangles() {
        using PrimitiveDrawGpuHarness harness = PrimitiveDrawGpuHarness.CreateOrSkip(sampleCount: 1);
        Color color = new(0.8f, 0.3f, 0.1f, 1f);

        harness.SubmitPerspective(
            camera => {
                camera.DrawCube(new Vector3(-1f, 0f, -5f), Vector3.One, color);
                camera.DrawCube(new Vector3(1f, 0f, -5f), Vector3.One, color);
            },
            clearColor: _clearColor);
        RenderStats cubes = harness.Host.Backend.Stats;
        Assert.Equal(1, cubes.DrawCalls);
        Assert.Equal(2, cubes.Instances);
        Assert.Equal(24, cubes.Triangles);
        Assert.Equal(0, cubes.CulledInstances);

        harness.SubmitPerspective(
            camera => {
                camera.DrawLine(new Vector3(-2f, 0f, -5f), new Vector3(-1f, 0f, -5f), color);
                camera.DrawLines(
                    [
                        new Vector3(0f, 0f, -5f),
                        new Vector3(1f, 0f, -5f),
                        new Vector3(2f, 0f, -5f),
                    ],
                    color);
            },
            clearColor: _clearColor);
        RenderStats lines = harness.Host.Backend.Stats;
        Assert.Equal(1, lines.DrawCalls);
        Assert.Equal(3, lines.Instances);
        Assert.Equal(0, lines.Triangles);

        harness.SubmitPerspective(
            camera => camera.DrawGrid(2, 1f, color),
            transform: new Transform(new Vector3(0f, 0f, 10f)),
            clearColor: _clearColor);
        RenderStats grid = harness.Host.Backend.Stats;
        Assert.Equal(1, grid.DrawCalls);
        Assert.Equal(6, grid.Instances);
        Assert.Equal(0, grid.Triangles);

        harness.SubmitPerspective(
            camera => {
                camera.DrawGrid(2, 1f, color);
                camera.DrawLines(
                    [
                        new Vector3(-1f, 0f, 5f),
                        new Vector3(1f, 0f, 5f),
                    ],
                    color);
            },
            transform: new Transform(new Vector3(0f, 0f, 10f)),
            clearColor: _clearColor);
        RenderStats gridAndLines = harness.Host.Backend.Stats;
        Assert.Equal(1, gridAndLines.DrawCalls);
        Assert.Equal(7, gridAndLines.Instances);
        Assert.Equal(0, gridAndLines.Triangles);
        Assert.Empty(harness.Host.Instance.ConsumeValidationErrors());
    }

    private static void AssertScreenQuadProbes(
        PrimitiveDrawGpuHarness harness,
        Rect destination,
        Color topLeft,
        Color topRight,
        Color bottomLeft,
        Color bottomRight) {
        uint left = Pixel(destination.X + (destination.Width * 0.25f));
        uint right = Pixel(destination.X + (destination.Width * 0.75f));
        uint top = Pixel(destination.Y + (destination.Height * 0.25f));
        uint bottom = Pixel(destination.Y + (destination.Height * 0.75f));
        AssertColorNear(topLeft, harness.ReadColor(left, top));
        AssertColorNear(topRight, harness.ReadColor(right, top));
        AssertColorNear(bottomLeft, harness.ReadColor(left, bottom));
        AssertColorNear(bottomRight, harness.ReadColor(right, bottom));

        uint middleY = Pixel(destination.Y + (destination.Height * 0.5f));
        AssertColorNear(_clearColor, harness.ReadColor(Pixel(destination.X * 0.5f), middleY));
        AssertColorNear(
            _clearColor,
            harness.ReadColor(Pixel(destination.X + destination.Width + (destination.X * 0.5f)), middleY));
    }

    private static Color Unorm(Color color) => new(
        UnormChannel(color.R),
        UnormChannel(color.G),
        UnormChannel(color.B),
        UnormChannel(color.A));

    private static float UnormChannel(float value) =>
        (byte)((int)((Math.Clamp(value, 0f, 1f) * 255f) + 0.5f)) / 255f;

    private static uint Pixel(float coordinate) => checked((uint)MathF.Floor(coordinate));

    private static void AssertColorNear(Color expected, (float R, float G, float B) actual) {
        Assert.True(
            MathF.Abs(expected.R - actual.R) <= COLOR_TOLERANCE,
            $"Red {actual.R} is not within {COLOR_TOLERANCE} of {expected.R}.");
        Assert.True(
            MathF.Abs(expected.G - actual.G) <= COLOR_TOLERANCE,
            $"Green {actual.G} is not within {COLOR_TOLERANCE} of {expected.G}.");
        Assert.True(
            MathF.Abs(expected.B - actual.B) <= COLOR_TOLERANCE,
            $"Blue {actual.B} is not within {COLOR_TOLERANCE} of {expected.B}.");
    }
}
