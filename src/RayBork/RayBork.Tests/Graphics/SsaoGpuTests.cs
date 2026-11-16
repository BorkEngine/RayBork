using System.Numerics;
using RayBork.Internal;

namespace RayBork.Tests;

/// <summary>
/// GPU integration tests for the built-in <see cref="SsaoEffect"/>. They render a small procedural
/// wall-and-cube scene through the real camera service, post-processor, built-in pack shaders, and
/// backbuffer readback, then probe a flat wall area and a wall area beside the cube.
/// </summary>
public sealed class SsaoGpuTests {
    private const int TARGET_WIDTH = 128;
    private const int TARGET_HEIGHT = 96;
    private const float SSAO_RADIUS = 1f;
    private const float SSAO_FALLOFF = 10f;
    private const float RESULT_TOLERANCE_BASELINE = 0.035f;
    private const float DARKENING_MINIMUM = 0.08f;

    private static readonly Vector3 _cameraPosition = new(0f, 0f, 2f);
    private static readonly Vector3 _flatProbe = new(-1.2f, -0.9f, 0f);
    private static readonly Vector3 _cornerProbe = new(0.7f, 0.02f, 0f);
    private static readonly Color _sceneColor = new(0.4f, 0.5f, 0.6f, 1f);

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void FlatWallStaysNearWhiteAndTheCubeContactDarkensForEveryQuality() {
        using PrimitiveDrawGpuHarness harness = PrimitiveDrawGpuHarness.CreateOrSkip();
        using RenderTexture target = CreateTarget(harness);
        SceneProbes probes = Submit(harness, target, postProcess: null, orthographic: true);
        SceneColor baseline = Read(harness, target, probes);
        AssertColorNear(_sceneColor, baseline.Flat, RESULT_TOLERANCE_BASELINE);

        using TickThreadScope tick = new();
        foreach (SsaoQuality quality in Enum.GetValues<SsaoQuality>()) {
            SsaoEffect effect = new() {
                Quality = quality,
                Radius = SSAO_RADIUS,
                DistanceFalloff = SSAO_FALLOFF,
                DenoiseRadius = 0,
            };
            PostProcessStack stack = new();
            stack.Add(effect);

            SceneProbes current = Submit(harness, target, stack, orthographic: true);
            SceneColor result = Read(harness, target, current);
            Assert.True(
                Luminance(result.Corner) < Luminance(baseline.Corner) - DARKENING_MINIMUM,
                $"Quality {quality} did not darken the corner: baseline {Luminance(baseline.Corner)}, "
                    + $"actual {Luminance(result.Corner)}.");
            AssertColorNear(_sceneColor, result.Flat, RESULT_TOLERANCE_BASELINE);
        }

        Assert.Empty(harness.Host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void DisabledSsaoMatchesTheUnprocessedBaseline() {
        using PrimitiveDrawGpuHarness harness = PrimitiveDrawGpuHarness.CreateOrSkip();
        using RenderTexture target = CreateTarget(harness);
        SceneProbes probes = Submit(harness, target, postProcess: null, orthographic: true);
        SceneColor baseline = Read(harness, target, probes);

        using TickThreadScope tick = new();
        var effect = new SsaoEffect {
            Radius = SSAO_RADIUS,
            DistanceFalloff = SSAO_FALLOFF,
            DenoiseRadius = 0,
            Enabled = false,
        };
        PostProcessStack stack = new();
        stack.Add(effect);
        Submit(harness, target, stack, orthographic: true);
        SceneColor disabled = Read(harness, target, probes);

        AssertColorNear(baseline.Flat, disabled.Flat, 0.01f);
        AssertColorNear(baseline.Corner, disabled.Corner, 0.01f);
        Assert.Empty(harness.Host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void PerspectiveAndOrthographicCamerasBothRunSsao() {
        using PrimitiveDrawGpuHarness harness = PrimitiveDrawGpuHarness.CreateOrSkip();
        using RenderTexture target = CreateTarget(harness);
        using TickThreadScope tick = new();
        var effect = new SsaoEffect {
            Radius = SSAO_RADIUS,
            DistanceFalloff = SSAO_FALLOFF,
            DenoiseRadius = 0,
        };
        PostProcessStack stack = new();
        stack.Add(effect);

        SceneProbes perspectiveProbes = Submit(harness, target, stack, orthographic: false);
        SceneColor perspective = Read(harness, target, perspectiveProbes);
        Assert.True(
            Luminance(perspective.Corner) < Luminance(perspective.Flat) - DARKENING_MINIMUM,
            $"The perspective corner is not darker than the flat wall: corner "
                + $"{Luminance(perspective.Corner)}, flat {Luminance(perspective.Flat)}.");

        SceneProbes orthographicProbes = Submit(harness, target, stack, orthographic: true);
        SceneColor orthographic = Read(harness, target, orthographicProbes);
        Assert.True(
            Luminance(orthographic.Corner) < Luminance(orthographic.Flat) - DARKENING_MINIMUM,
            $"The orthographic corner is not darker than the flat wall: corner "
                + $"{Luminance(orthographic.Corner)}, flat {Luminance(orthographic.Flat)}.");
        Assert.Empty(harness.Host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ScreenSpaceRadiusIsMeasuredInPixelsInsteadOfWorldUnits() {
        using PrimitiveDrawGpuHarness harness = PrimitiveDrawGpuHarness.CreateOrSkip();
        using RenderTexture target = CreateTarget(harness);
        SceneProbes probes = Submit(harness, target, postProcess: null, orthographic: true);
        SceneColor baseline = Read(harness, target, probes);
        using TickThreadScope tick = new();

        var world = new SsaoEffect {
            Radius = 0.04f,
            DistanceFalloff = SSAO_FALLOFF,
            DenoiseRadius = 0,
        };
        PostProcessStack worldStack = new();
        worldStack.Add(world);
        Submit(harness, target, worldStack, orthographic: true);
        SceneColor worldResult = Read(harness, target, probes);

        var screen = new SsaoEffect {
            Radius = 24f,
            DistanceFalloff = 1f,
            DenoiseRadius = 0,
            ScreenSpaceRadius = true,
        };
        PostProcessStack screenStack = new();
        screenStack.Add(screen);
        Submit(harness, target, screenStack, orthographic: true);
        SceneColor screenResult = Read(harness, target, probes);

        Assert.True(
            Luminance(worldResult.Corner) > Luminance(baseline.Corner) - 0.02f,
            "A 0.04-world-unit radius should not darken the corner meaningfully.");
        Assert.True(
            Luminance(screenResult.Corner) < Luminance(worldResult.Corner) - DARKENING_MINIMUM,
            "A 24-pixel screen-space radius should darken the corner more than a tiny world radius.");
        Assert.Empty(harness.Host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void IntensityAndRadiusChangeTheCompositeOutput() {
        using PrimitiveDrawGpuHarness harness = PrimitiveDrawGpuHarness.CreateOrSkip();
        using RenderTexture target = CreateTarget(harness);
        SceneProbes probes = Submit(harness, target, postProcess: null, orthographic: true);
        SceneColor baseline = Read(harness, target, probes);
        using TickThreadScope tick = new();

        var intensityZero = new SsaoEffect {
            Radius = SSAO_RADIUS,
            DistanceFalloff = SSAO_FALLOFF,
            DenoiseRadius = 0,
            Intensity = 0f,
        };
        PostProcessStack zeroStack = new();
        zeroStack.Add(intensityZero);
        Submit(harness, target, zeroStack, orthographic: true);
        SceneColor zero = Read(harness, target, probes);
        AssertColorNear(baseline.Corner, zero.Corner, 0.01f);

        var smallRadius = new SsaoEffect {
            Radius = 0.05f,
            DistanceFalloff = SSAO_FALLOFF,
            DenoiseRadius = 0,
            Intensity = 2f,
        };
        PostProcessStack smallStack = new();
        smallStack.Add(smallRadius);
        Submit(harness, target, smallStack, orthographic: true);
        SceneColor small = Read(harness, target, probes);

        var largeRadius = new SsaoEffect {
            Radius = SSAO_RADIUS,
            DistanceFalloff = SSAO_FALLOFF,
            DenoiseRadius = 0,
            Intensity = 2f,
        };
        PostProcessStack largeStack = new();
        largeStack.Add(largeRadius);
        Submit(harness, target, largeStack, orthographic: true);
        SceneColor large = Read(harness, target, probes);

        Assert.True(
            Luminance(large.Corner) < Luminance(small.Corner) - DARKENING_MINIMUM,
            "The larger radius should darken the corner more than the tiny radius.");
        Assert.Empty(harness.Host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void DenoiseRadiusChangesTheNeighborhoodAndReadbackContainsSsao() {
        using PrimitiveDrawGpuHarness harness = PrimitiveDrawGpuHarness.CreateOrSkip();
        using RenderTexture target = CreateTarget(harness);
        SceneProbes probes = Submit(harness, target, postProcess: null, orthographic: true);
        SceneColor baseline = Read(harness, target, probes);
        using TickThreadScope tick = new();

        var sharpEffect = new SsaoEffect {
            Radius = SSAO_RADIUS,
            DistanceFalloff = SSAO_FALLOFF,
            DenoiseRadius = 0,
        };
        PostProcessStack sharpStack = new();
        sharpStack.Add(sharpEffect);
        Submit(harness, target, sharpStack, orthographic: true);
        SceneColor sharpResult = Read(harness, target, probes);

        var blurredEffect = new SsaoEffect {
            Radius = SSAO_RADIUS,
            DistanceFalloff = SSAO_FALLOFF,
            DenoiseRadius = 24,
        };
        PostProcessStack blurredStack = new();
        blurredStack.Add(blurredEffect);
        Submit(harness, target, blurredStack, orthographic: true);
        SceneColor blurredResult = Read(harness, target, probes);

        Assert.True(
            MathF.Abs(Luminance(blurredResult.Corner) - Luminance(sharpResult.Corner)) > 0.005f,
            "The wide denoise radius should change the sharp corner's neighborhood.");
        Assert.True(
            Luminance(blurredResult.Corner) < Luminance(baseline.Corner) - 0.02f,
            $"The blurred result must still contain the corner AO: blurred "
                + $"{Luminance(blurredResult.Corner)}, baseline {Luminance(baseline.Corner)}.");

        Color[,] colors = ReadColorGrid(harness, target);
        int x = (int)probes.Corner.X;
        int y = (int)probes.Corner.Y;
        AssertColorNear(
            (blurredResult.Corner.R, blurredResult.Corner.G, blurredResult.Corner.B),
            (colors[y, x].R, colors[y, x].G, colors[y, x].B),
            0.01f);
        Assert.Empty(harness.Host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void DepthlessTargetIsRejectedWithAClearError() {
        using PrimitiveDrawGpuHarness harness = PrimitiveDrawGpuHarness.CreateOrSkip();
        using RenderTexture target = harness.Host.CreateRenderTexture(
            new RenderTextureDescription(64, 48, Depth: false, SampleCount: 1));
        using TickThreadScope tick = new();
        var effect = new SsaoEffect();
        PostProcessStack stack = new();
        stack.Add(effect);

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(
            () => Submit(harness, target, stack, orthographic: true));

        Assert.Contains("depth", thrown.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("render texture", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ASmallTargetAndACompositeOfTheOriginalSceneColorWork() {
        using PrimitiveDrawGpuHarness harness = PrimitiveDrawGpuHarness.CreateOrSkip();
        using RenderTexture target = harness.Host.CreateRenderTexture(
            new RenderTextureDescription(48, 32, SampleCount: 1));
        using TickThreadScope tick = new();
        var effect = new SsaoEffect {
            Radius = SSAO_RADIUS,
            DistanceFalloff = SSAO_FALLOFF,
            DenoiseRadius = 0,
        };
        PostProcessStack stack = new();
        stack.Add(effect);

        SceneProbes probes = Submit(harness, target, stack, orthographic: true);
        SceneColor result = Read(harness, target, probes);
        AssertColorNear(_sceneColor, result.Flat, 0.04f);
        Assert.True(
            Luminance(result.Corner) < Luminance(result.Flat) - 0.04f,
            $"The small target's corner must still be darkened: corner "
                + $"{Luminance(result.Corner)}, flat {Luminance(result.Flat)}.");
        Assert.Empty(harness.Host.Instance.ConsumeValidationErrors());
    }

    private static RenderTexture CreateTarget(PrimitiveDrawGpuHarness harness) =>
        harness.Host.CreateRenderTexture(new RenderTextureDescription(TARGET_WIDTH, TARGET_HEIGHT));

    private static SceneProbes Submit(
        PrimitiveDrawGpuHarness harness,
        RenderTexture target,
        PostProcessStack? postProcess,
        bool orthographic) {
        SceneProbes probes = default;
        Action<Camera> draw = camera => probes = DrawScene(camera);
        if (orthographic) {
            harness.SubmitOrthographic(
                draw,
                new Transform(_cameraPosition),
                _sceneColor,
                height: 3f,
                near: 0.1f,
                far: 10f,
                postProcess,
                target);
        } else {
            harness.SubmitPerspective(
                draw,
                new Transform(_cameraPosition),
                _sceneColor,
                fieldOfView: 60f,
                near: 0.1f,
                far: 10f,
                postProcess,
                target);
        }

        return probes;
    }

    private static SceneProbes DrawScene(Camera camera) {
        var quad = new Transform(Vector3.Zero);
        camera.DrawQuad(quad, new Vector2(6f, 4f), _sceneColor);
        camera.DrawCube(new Vector3(0.7f, 0.4f, 0.6f), 0.6f, 0.6f, 0.6f, _sceneColor);
        return new SceneProbes(
            RoundScreen(camera.WorldToScreen(_flatProbe)),
            RoundScreen(camera.WorldToScreen(_cornerProbe)));
    }

    private static Vector2 RoundScreen(Vector2 position) => new(
        MathF.Round(position.X, MidpointRounding.AwayFromZero),
        MathF.Round(position.Y, MidpointRounding.AwayFromZero));

    private static Color[,] ReadColorGrid(PrimitiveDrawGpuHarness harness, RenderTexture target) =>
        harness.Host.ReadColorsAsync(target).GetAwaiter().GetResult();

    private static SceneColor Read(
        PrimitiveDrawGpuHarness harness,
        RenderTexture target,
        SceneProbes probes) => new(
        harness.ReadRenderTargetColor(target, (uint)probes.Flat.X, (uint)probes.Flat.Y),
        harness.ReadRenderTargetColor(target, (uint)probes.Corner.X, (uint)probes.Corner.Y));

    private static float Luminance((float R, float G, float B) color) =>
        (0.2126f * color.R) + (0.7152f * color.G) + (0.0722f * color.B);

    private static void AssertColorNear(
        Color expected,
        (float R, float G, float B) actual,
        float tolerance) => AssertColorNear(
        (expected.R, expected.G, expected.B),
        actual,
        tolerance);

    private static void AssertColorNear(
        (float R, float G, float B) expected,
        (float R, float G, float B) actual,
        float tolerance) {
        Assert.True(
            MathF.Abs(expected.R - actual.R) <= tolerance,
            $"Red {actual.R} is not within {tolerance} of {expected.R}.");
        Assert.True(
            MathF.Abs(expected.G - actual.G) <= tolerance,
            $"Green {actual.G} is not within {tolerance} of {expected.G}.");
        Assert.True(
            MathF.Abs(expected.B - actual.B) <= tolerance,
            $"Blue {actual.B} is not within {tolerance} of {expected.B}.");
    }

    private readonly record struct SceneProbes(Vector2 Flat, Vector2 Corner);

    private readonly record struct SceneColor((float R, float G, float B) Flat, (float R, float G, float B) Corner);

    private sealed class TickThreadScope : IDisposable {
        private readonly bool _ownsAttachment;

        internal TickThreadScope() {
            _ownsAttachment = !TickThread.IsCurrent;
            if (_ownsAttachment) {
                TickThread.Attach();
            }
        }

        public void Dispose() {
            if (_ownsAttachment) {
                TickThread.Detach();
            }
        }
    }
}
