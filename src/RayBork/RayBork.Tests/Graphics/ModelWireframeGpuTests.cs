using System.Numerics;
using RayBork.Internal;

namespace RayBork.Tests;

/// <summary>
/// GPU integration tests for <see cref="Camera.DrawWireModel"/>: the production camera records a model
/// over the engine's real cube and quad meshes, submits it through <see cref="Vulkan12Backend"/>, and
/// asserts polygon-mode-line pixels, render statistics, pipeline-variant separation, and
/// validation-layer cleanliness.
/// </summary>
/// <remarks>
/// <para>
/// The tests use a single-sample scene target so a one-pixel line has deterministic coverage, and a
/// two-unit transform scale over a four-unit-tall orthographic view so the unit cube spans exactly half
/// the target in both axes. A line is one pixel wide; every probe searches a small window around the
/// projected line with an explicit tolerance rather than requiring an exact rasterization rule. There
/// are no golden images and no sleeps.
/// </para>
/// <para>
/// The wireframe pipeline needs the core <c>fillModeNonSolid</c> device feature. A host that does not
/// support it is reported as a skip with the exact reason: M5 has no CPU line-geometry fallback, and the
/// renderer reports an unsupported wireframe draw instead of drawing nothing. Filled model draws never
/// need the feature.
/// </para>
/// <para>
/// The tests join the serialized <c>SDL</c> collection because SDL and Vulkan initialization are process
/// global.
/// </para>
/// </remarks>
[Collection("SDL")]
public sealed class ModelWireframeGpuTests {
    private const float COLOR_TOLERANCE = 2e-3f;
    private const uint LINE_PIXEL_TOLERANCE = 2;
    private static readonly Color _clearColor = new(0.05f, 0.1f, 0.2f, 1f);

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void WireframeCubeDrawsTheFrontTriangleEdgesAndNotTheFaceInterior() {
        using PrimitiveDrawGpuHarness harness = PrimitiveDrawGpuHarness.CreateOrSkip(sampleCount: 1);
        RequireFillModeNonSolid(harness);
        Color color = new(0.9f, 0.3f, 0.1f, 1f);
        using Model model = CreateCubeModel(harness);
        uint centerX = harness.Width / 2;
        uint centerY = harness.Height / 2;
        uint edgeOffset = harness.Height / 4;

        // The cube transform scales the unit cube by two, so its world extents are [-1, 1] and its
        // silhouette edges project a quarter of the target height from the center. The front face is
        // split into two triangles, so its diagonal crosses the center of the face and therefore the
        // center row.
        harness.SubmitOrthographic(
            camera => camera.DrawWireModel(
                model,
                new Transform(Vector3.Zero, Quaternion.Identity, new Vector3(2f)),
                color),
            transform: new Transform(new Vector3(0f, 0f, 5f)),
            clearColor: _clearColor,
            height: 4f);

        AssertTintedNear(harness, color, centerX - edgeOffset, centerY);
        AssertTintedNear(harness, color, centerX + edgeOffset, centerY);
        AssertTintedNear(harness, color, centerX, centerY);
        AssertColorNear(_clearColor, harness.ReadColor(centerX - (edgeOffset / 2), centerY));
        AssertColorNear(_clearColor, harness.ReadColor(centerX + (edgeOffset / 2), centerY));

        // The draw still reports the recorded triangle primitives: polygon-mode line changes the
        // rasterization, not the draw record's topology or index count.
        RenderStats stats = harness.Host.Backend.Stats;
        Assert.Equal(1, stats.DrawCalls);
        Assert.Equal(1, stats.Instances);
        Assert.Equal(12, stats.Triangles);
        Assert.Equal(1, stats.PipelineCount);
        Assert.Empty(harness.Host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void WireframeQuadLineSamplesThePartMaterialTexture() {
        using PrimitiveDrawGpuHarness harness = PrimitiveDrawGpuHarness.CreateOrSkip(sampleCount: 1);
        RequireFillModeNonSolid(harness);
        Color textureColor = new(0.25f, 0.5f, 0.75f, 1f);
        Texture texture = harness.CreateTexture(1, 1, textureColor);
        Shader material = harness.Primitives.MeshMaterial.Program.CreateMaterial();
        using Model model = new(new ModelPart("quad", harness.Primitives.Quad, material));
        uint centerX = harness.Width / 2;
        uint centerY = harness.Height / 2;
        uint edgeOffset = harness.Height / 4;

        // The unit quad is scaled by two, so its outline projects to a square spanning half the view
        // in both axes. The material cursor is set on the tick thread, as IGame.Tick would, and the
        // line fragment still samples the part material's texture.
        harness.SubmitOrthographic(
            camera => {
                material.SetUniform(PrimitiveDrawGpuHarness.MESH_TEXTURE_BINDING, texture);
                camera.DrawWireModel(
                    model,
                    new Transform(Vector3.Zero, Quaternion.Identity, new Vector3(2f)),
                    tint: null);
            },
            transform: new Transform(new Vector3(0f, 0f, 5f)),
            clearColor: _clearColor,
            height: 4f);

        AssertTintedNear(harness, Unorm(textureColor), centerX - edgeOffset, centerY);
        AssertTintedNear(harness, Unorm(textureColor), centerX + edgeOffset, centerY);
        AssertColorNear(_clearColor, harness.ReadColor(centerX - (edgeOffset / 2), centerY));
        Assert.Equal(2, harness.Host.Backend.Stats.Triangles);
        Assert.Empty(harness.Host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void FilledAndWireframeModelDrawsUseDistinctPipelines() {
        using PrimitiveDrawGpuHarness harness = PrimitiveDrawGpuHarness.CreateOrSkip(sampleCount: 1);
        RequireFillModeNonSolid(harness);
        Color color = new(0.2f, 0.8f, 0.4f, 1f);
        using Model model = CreateCubeModel(harness);
        uint centerY = harness.Height / 2;
        uint filledCenterX = harness.Width / 4;
        uint wireframeCenterX = harness.Width * 3 / 4;
        uint edgeOffset = harness.Height / 4;

        // Both draws use the same mesh, material, and render state, so only the polygon mode can
        // separate them into two batches and two pipelines.
        harness.SubmitOrthographic(
            camera => {
                camera.DrawModel(
                    model,
                    new Transform(new Vector3(-1.6f, 0f, 0f), Quaternion.Identity, new Vector3(2f)),
                    color);
                camera.DrawWireModel(
                    model,
                    new Transform(new Vector3(1.6f, 0f, 0f), Quaternion.Identity, new Vector3(2f)),
                    color);
            },
            transform: new Transform(new Vector3(0f, 0f, 5f)),
            clearColor: _clearColor,
            height: 4f);

        // The filled cube covers its face interior; the wireframe cube leaves it clear.
        AssertColorNear(color, harness.ReadColor(filledCenterX, centerY));
        AssertColorNear(_clearColor, harness.ReadColor(wireframeCenterX - (edgeOffset / 2), centerY));
        AssertTintedNear(harness, color, wireframeCenterX, centerY);

        RenderStats stats = harness.Host.Backend.Stats;
        Assert.Equal(2, stats.DrawCalls);
        Assert.Equal(2, stats.Instances);
        Assert.Equal(24, stats.Triangles);
        Assert.Equal(2, stats.PipelineCount);
        Assert.Equal(2, harness.Host.Backend.GeometryPipelines.Count);
        Assert.Empty(harness.Host.Instance.ConsumeValidationErrors());
    }

    private static Model CreateCubeModel(PrimitiveDrawGpuHarness harness) {
        Shader material = harness.Primitives.PrimitiveMaterial.Program.CreateMaterial();
        return new Model(new ModelPart("cube", harness.Primitives.Cube, material));
    }

    private static void RequireFillModeNonSolid(PrimitiveDrawGpuHarness harness) {
        VulkanDeviceCapabilities capabilities = harness.Host.Device.Capabilities;
        if (!capabilities.FillModeNonSolid) {
            Assert.Skip(
                $"The Vulkan device '{capabilities.DeviceName}' does not support fillModeNonSolid; "
                    + "DrawWireModel needs the polygon-mode-line pipeline and M5 has no line-geometry "
                    + "fallback.");
        }
    }

    private static void AssertTintedNear(
        PrimitiveDrawGpuHarness harness,
        Color expected,
        uint x,
        uint y) {
        for (uint offset = 0; offset <= LINE_PIXEL_TOLERANCE; offset++) {
            if (IsColorNear(expected, harness.ReadColor(x + offset, y))
                || (offset > 0 && IsColorNear(expected, harness.ReadColor(x - offset, y)))) {
                return;
            }
        }

        Assert.Fail(
            $"No pixel within {LINE_PIXEL_TOLERANCE} pixel(s) of ({x}, {y}) matched the expected "
                + $"color ({expected.R}, {expected.G}, {expected.B}).");
    }

    private static bool IsColorNear(Color expected, (float R, float G, float B) actual) =>
        MathF.Abs(expected.R - actual.R) <= COLOR_TOLERANCE
        && MathF.Abs(expected.G - actual.G) <= COLOR_TOLERANCE
        && MathF.Abs(expected.B - actual.B) <= COLOR_TOLERANCE;

    private static void AssertColorNear(Color expected, (float R, float G, float B) actual) {
        Assert.True(
            IsColorNear(expected, actual),
            $"Expected ({expected.R}, {expected.G}, {expected.B}) but read "
                + $"({actual.R}, {actual.G}, {actual.B}) within {COLOR_TOLERANCE}.");
    }

    private static Color Unorm(Color color) => new(
        UnormChannel(color.R),
        UnormChannel(color.G),
        UnormChannel(color.B),
        UnormChannel(color.A));

    private static float UnormChannel(float value) =>
        (byte)((int)((Math.Clamp(value, 0f, 1f) * 255f) + 0.5f)) / 255f;
}
