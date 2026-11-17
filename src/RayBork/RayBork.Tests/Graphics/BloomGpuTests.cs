using System.Numerics;
using RayBork.Internal;

namespace RayBork.Tests;

/// <summary>
/// GPU integration tests for the built-in <see cref="BloomEffect"/>. They render a small HDR probe scene
/// through the real backend and the built-in bloom shaders, then assert numeric readback differences
/// for the halo, threshold, soft knee, intensity, scatter, chain order, small targets, depth-less
/// targets, readback, and target reuse.
/// </summary>
/// <remarks>
/// The tests never compare images. They assert validation-layer cleanliness and linear RGB probes with
/// explicit tolerances; the post targets and the store-back are scene-linear and the present pass is
/// not part of these frames.
/// </remarks>
public sealed class BloomGpuTests {
    private const int TARGET_WIDTH = 128;
    private const int TARGET_HEIGHT = 96;
    private const string EFFECT_TARGET_DESCRIPTION = "the render texture target";
    private static readonly Color _dark = new(0.02f, 0.03f, 0.04f, 1f);
    private static readonly Color _brightGold = new(12f, 8f, 2f, 1f);
    private static readonly Color _uniformHdr = new(4f, 4f, 4f, 1f);

    private const string FULLSCREEN_VERTEX_SOURCE = """
        #version 450

        layout(location = 0) in vec3 aPosition;
        layout(location = 2) in vec2 aUv;

        layout(location = 0) out vec2 vUv;

        void main_vertex() {
            vUv = aUv;
            gl_Position = vec4(aPosition.xy * 2.0, 0.0, 1.0);
        }
        """;

    private const string INVERT_FRAGMENT_SOURCE = """
        #version 450

        layout(location = 0) in vec2 vUv;

        layout(location = 0) out vec4 oColor;

        void main_fragment() {
            vec3 scene = texture(sourceColor, vUv).rgb;
            oColor = vec4(vec3(1.0) - scene, 1.0);
        }
        """;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void HdrSpotBloomsIntoTheDarkSurroundAndDisabledMatchesTheBaseline() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using RenderTexture target = host.CreateRenderTexture(
            new RenderTextureDescription(TARGET_WIDTH, TARGET_HEIGHT, SampleCount: 1));
        using BloomScene scene = new(harness, "bloom-halo");
        BloomAssets assets = harness.LoadBloomAssetsFromBuiltInPack();
        VulkanDrawTestHarness.DrawSpec bright = scene.Draw(
            TARGET_WIDTH,
            TARGET_HEIGHT,
            new Vector2(64f, 48f),
            new Vector2(20f, 20f),
            _brightGold);

        harness.Submit(harness.CreateFrameWithPasses(
            1,
            Pass(target, [bright], [])));
        (float R, float G, float B) baselineHalo = harness.ReadRenderTargetColor(target, 64, 28);
        (float R, float G, float B) baselineCenter = harness.ReadRenderTargetColor(target, 64, 48);

        harness.Submit(harness.CreateFrameWithPasses(
            2,
            Pass(target, [bright], [CaptureBloom(assets)])));
        (float R, float G, float B) halo = harness.ReadRenderTargetColor(target, 64, 28);
        (float R, float G, float B) center = harness.ReadRenderTargetColor(target, 64, 48);

        Assert.True(
            halo.R > baselineHalo.R + 0.01f,
            $"The HDR halo is not brighter than the baseline: baseline {baselineHalo.R}, actual "
                + $"{halo.R}.");
        Assert.True(
            halo.G > baselineHalo.G + 0.01f,
            $"The HDR halo green is not brighter than the baseline: baseline {baselineHalo.G}, "
                + $"actual {halo.G}.");
        Assert.True(
            center.R > baselineCenter.R,
            $"The bright center does not contain the added bloom: baseline {baselineCenter.R}, "
                + $"actual {center.R}.");

        harness.Submit(harness.CreateFrameWithPasses(
            3,
            Pass(target, [bright], [CaptureBloom(assets, enabled: false)])));
        (float R, float G, float B) disabledHalo = harness.ReadRenderTargetColor(target, 64, 28);
        AssertColorNear(baselineHalo, disabledHalo, 0.005f);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ThresholdAndSoftKneeShapeThePrefilter() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using RenderTexture target = host.CreateRenderTexture(
            new RenderTextureDescription(64, 64, SampleCount: 1));
        BloomAssets assets = harness.LoadBloomAssetsFromBuiltInPack();
        Color source = new(1.5f, 1.5f, 1.5f, 1f);

        harness.Submit(harness.CreateFrameWithPasses(
            1,
            Pass(target, [], [], source)));
        (float R, float G, float B) baseline = harness.ReadRenderTargetColor(target, 32, 32);

        harness.Submit(harness.CreateFrameWithPasses(
            2,
            Pass(target, [], [CaptureBloom(assets, threshold: 1f, softKnee: 0f, intensity: 4f)], source)));
        (float R, float G, float B) hard = harness.ReadRenderTargetColor(target, 32, 32);

        harness.Submit(harness.CreateFrameWithPasses(
            3,
            Pass(target, [], [CaptureBloom(assets, threshold: 1f, softKnee: 1f, intensity: 4f)], source)));
        (float R, float G, float B) soft = harness.ReadRenderTargetColor(target, 32, 32);

        harness.Submit(harness.CreateFrameWithPasses(
            4,
            Pass(target, [], [CaptureBloom(assets, threshold: 10f, intensity: 4f)], source)));
        (float R, float G, float B) highThreshold = harness.ReadRenderTargetColor(target, 32, 32);

        AssertColorNear((source.R, source.G, source.B), baseline, 0.01f);
        Assert.True(
            hard.R > baseline.R + 0.5f,
            $"The hard-threshold bloom is not visible: baseline {baseline.R}, actual {hard.R}.");
        Assert.True(
            soft.R > hard.R + 0.2f,
            $"The soft knee did not add more bloom than the hard threshold: hard {hard.R}, soft "
                + $"{soft.R}.");
        AssertColorNear((source.R, source.G, source.B), highThreshold, 0.02f);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void IntensityAndScatterChangeTheHalo() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using RenderTexture target = host.CreateRenderTexture(
            new RenderTextureDescription(TARGET_WIDTH, TARGET_HEIGHT, SampleCount: 1));
        using BloomScene scene = new(harness, "bloom-scatter");
        BloomAssets assets = harness.LoadBloomAssetsFromBuiltInPack();
        VulkanDrawTestHarness.DrawSpec bright = scene.Draw(
            TARGET_WIDTH,
            TARGET_HEIGHT,
            new Vector2(64f, 48f),
            new Vector2(16f, 16f),
            _brightGold);

        harness.Submit(harness.CreateFrameWithPasses(1, Pass(target, [bright], [])));
        (float R, float G, float B) baselineHalo = harness.ReadRenderTargetColor(target, 64, 30);
        (float R, float G, float B) baselineCenter = harness.ReadRenderTargetColor(target, 64, 48);

        harness.Submit(harness.CreateFrameWithPasses(
            2,
            Pass(target, [bright], [CaptureBloom(assets, intensity: 0f)])));
        (float R, float G, float B) zeroIntensity = harness.ReadRenderTargetColor(target, 64, 48);
        AssertColorNear(baselineCenter, zeroIntensity, 0.01f);

        harness.Submit(harness.CreateFrameWithPasses(
            3,
            Pass(target, [bright], [CaptureBloom(assets, intensity: 2f, scatter: 0f)])));
        (float R, float G, float B) noScatter = harness.ReadRenderTargetColor(target, 64, 30);
        AssertColorNear(baselineHalo, noScatter, 0.02f);

        harness.Submit(harness.CreateFrameWithPasses(
            4,
            Pass(target, [bright], [CaptureBloom(assets, intensity: 2f, scatter: 1f)])));
        (float R, float G, float B) scattered = harness.ReadRenderTargetColor(target, 64, 30);
        Assert.True(
            scattered.R > baselineHalo.R + 0.02f,
            $"Scatter did not spread the halo: baseline {baselineHalo.R}, actual {scattered.R}.");
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ACustomEffectBeforeBloomPreservesTheCustomOutput() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using RenderTexture target = host.CreateRenderTexture(
            new RenderTextureDescription(64, 64, SampleCount: 1));
        BloomAssets assets = harness.LoadBloomAssetsFromBuiltInPack();
        using Shader invert = harness.CreateShader(
            "shaders/bloom-chain-invert",
            FULLSCREEN_VERTEX_SOURCE,
            INVERT_FRAGMENT_SOURCE,
            textures: ["sourceColor"]);
        PostProcessEffectSnapshot invertSnapshot = PostProcessSnapshots.Capture(
            new ShaderPostProcessEffect(invert),
            targetHasDepth: true,
            EFFECT_TARGET_DESCRIPTION);
        Color darkSource = new(0.1f, 0.1f, 0.1f, 1f);

        harness.Submit(harness.CreateFrameWithPasses(
            1,
            Pass(
                target,
                [],
                [invertSnapshot, CaptureBloom(assets, threshold: 0.5f, intensity: 2f)],
                darkSource)));

        (float R, float G, float B) result = harness.ReadRenderTargetColor(target, 32, 32);

        // The invert output is 0.9. If bloom sampled the original 0.1 it would find nothing above the
        // 0.5 threshold and the result would stay at 0.9; the added bloom proves the chain source was
        // the custom effect's target.
        Assert.True(
            result.R > 1.05f,
            $"The bloom did not sample the previous custom effect: red {result.R}.");
        Assert.True(
            result.R < 4f,
            $"The chain result is unexpectedly large: red {result.R}.");
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void BloomTwiceKeepsTheChainSourceAndAddsMoreBloom() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using RenderTexture target = host.CreateRenderTexture(
            new RenderTextureDescription(TARGET_WIDTH, TARGET_HEIGHT, SampleCount: 1));
        using BloomScene scene = new(harness, "bloom-twice");
        BloomAssets assets = harness.LoadBloomAssetsFromBuiltInPack();
        VulkanDrawTestHarness.DrawSpec bright = scene.Draw(
            TARGET_WIDTH,
            TARGET_HEIGHT,
            new Vector2(64f, 48f),
            new Vector2(16f, 16f),
            _brightGold);

        harness.Submit(harness.CreateFrameWithPasses(
            1,
            Pass(target, [bright], [CaptureBloom(assets)])));
        (float R, float G, float B) single = harness.ReadRenderTargetColor(target, 64, 30);

        harness.Submit(harness.CreateFrameWithPasses(
            2,
            Pass(target, [bright], [CaptureBloom(assets), CaptureBloom(assets)])));
        (float R, float G, float B) twice = harness.ReadRenderTargetColor(target, 64, 30);

        Assert.True(
            twice.R > single.R + 0.02f,
            $"The second bloom did not add to the chain source: single {single.R}, twice {twice.R}.");
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void SmallTargetsCollapseThePyramidAndStillBloom() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using RenderTexture small = host.CreateRenderTexture(
            new RenderTextureDescription(7, 5, SampleCount: 1));
        using RenderTexture singlePixel = host.CreateRenderTexture(
            new RenderTextureDescription(1, 1, SampleCount: 1));
        BloomAssets assets = harness.LoadBloomAssetsFromBuiltInPack();

        harness.Submit(harness.CreateFrameWithPasses(
            1,
            Pass(small, [], [CaptureBloom(assets, threshold: 1f, intensity: 1f)], _uniformHdr)));
        (float R, float G, float B) smallResult = harness.ReadRenderTargetColor(small, 3, 2);
        Assert.True(
            smallResult.R > _uniformHdr.R + 0.5f,
            $"The 7x5 target did not bloom: source {_uniformHdr.R}, actual {smallResult.R}.");

        harness.Submit(harness.CreateFrameWithPasses(
            2,
            Pass(singlePixel, [], [CaptureBloom(assets, threshold: 1f, intensity: 1f)], _uniformHdr)));
        (float R, float G, float B) singleResult = harness.ReadRenderTargetColor(singlePixel, 0, 0);
        Assert.True(
            singleResult.R > _uniformHdr.R + 0.5f,
            $"The 1x1 target did not bloom: source {_uniformHdr.R}, actual {singleResult.R}.");
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public async Task ReadbackIncludesBloomAndADepthlessTargetRunsTheChain() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using RenderTexture target = host.CreateRenderTexture(
            new RenderTextureDescription(64, 48, Depth: false, SampleCount: 1));
        using BloomScene scene = new(harness, "bloom-depthless");
        BloomAssets assets = harness.LoadBloomAssetsFromBuiltInPack();
        VulkanDrawTestHarness.DrawSpec bright = scene.Draw(
            64,
            48,
            new Vector2(32f, 24f),
            new Vector2(16f, 16f),
            _brightGold);

        harness.Submit(harness.CreateFrameWithPasses(1, Pass(target, [bright], [])));
        Color[,] baseline = await host.ReadColorsAsync(target);
        (float R, float G, float B) baselineHalo = (baseline[10, 32].R, baseline[10, 32].G, baseline[10, 32].B);

        harness.Submit(harness.CreateFrameWithPasses(
            2,
            Pass(target, [bright], [CaptureBloom(assets, targetHasDepth: false)])));
        Color[,] result = await host.ReadColorsAsync(target);
        (float R, float G, float B) halo = (result[10, 32].R, result[10, 32].G, result[10, 32].B);

        Assert.True(
            halo.R > baselineHalo.R + 0.01f,
            $"The depth-less target's readback does not include the bloom: baseline "
                + $"{baselineHalo.R}, actual {halo.R}.");
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void SsaoBeforeBloomSharesTheCameraTargetPostSetAndStillBlooms() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using RenderTexture target = host.CreateRenderTexture(
            new RenderTextureDescription(TARGET_WIDTH, TARGET_HEIGHT, SampleCount: 1));
        using BloomScene scene = new(harness, "bloom-ssao-chain");
        SsaoAssets ssaoAssets = harness.LoadSsaoAssetsFromBuiltInPack();
        BloomAssets bloomAssets = harness.LoadBloomAssetsFromBuiltInPack();
        VulkanDrawTestHarness.DrawSpec bright = scene.Draw(
            TARGET_WIDTH,
            TARGET_HEIGHT,
            new Vector2(64f, 48f),
            new Vector2(20f, 20f),
            _brightGold);

        harness.Submit(harness.CreateFrameWithPasses(1, Pass(target, [bright], [])));
        (float R, float G, float B) baselineHalo = harness.ReadRenderTargetColor(target, 64, 28);

        var ssao = new SsaoEffect(ssaoAssets) {
            Radius = 1f,
            DistanceFalloff = 10f,
            DenoiseRadius = 0,
        };
        PostProcessEffectSnapshot ssaoSnapshot = PostProcessSnapshots.Capture(
            ssao,
            targetHasDepth: true,
            EFFECT_TARGET_DESCRIPTION,
            ssaoAssets);
        PostProcessEffectSnapshot bloomSnapshot = CaptureBloom(bloomAssets, intensity: 2f);

        harness.Submit(harness.CreateFrameWithPasses(
            2,
            Pass(target, [bright], [ssaoSnapshot, bloomSnapshot])));
        (float R, float G, float B) halo = harness.ReadRenderTargetColor(target, 64, 28);

        Assert.True(
            halo.R > baselineHalo.R + 0.01f,
            $"The bloom after SSAO did not add a halo: baseline {baselineHalo.R}, actual {halo.R}.");
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ThresholdZeroKeepsLitPixels() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using RenderTexture target = host.CreateRenderTexture(
            new RenderTextureDescription(64, 64, SampleCount: 1));
        BloomAssets assets = harness.LoadBloomAssetsFromBuiltInPack();
        Color dim = new(0.25f, 0.25f, 0.25f, 1f);

        harness.Submit(harness.CreateFrameWithPasses(
            1,
            Pass(target, [], [], dim)));
        (float R, float G, float B) baseline = harness.ReadRenderTargetColor(target, 32, 32);

        harness.Submit(harness.CreateFrameWithPasses(
            2,
            Pass(target, [], [CaptureBloom(assets, threshold: 0f, intensity: 2f)], dim)));
        (float R, float G, float B) result = harness.ReadRenderTargetColor(target, 32, 32);

        Assert.True(
            result.R > baseline.R + 0.2f,
            $"Threshold 0 did not bloom a lit pixel: baseline {baseline.R}, actual {result.R}.");
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void TintColorsTheAddedBloom() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using RenderTexture target = host.CreateRenderTexture(
            new RenderTextureDescription(64, 64, SampleCount: 1));
        BloomAssets assets = harness.LoadBloomAssetsFromBuiltInPack();
        Color source = new(4f, 4f, 4f, 1f);
        Color green = new(0f, 1f, 0f, 1f);

        harness.Submit(harness.CreateFrameWithPasses(
            1,
            Pass(target, [], [CaptureBloom(assets, intensity: 1f, tint: green)], source)));
        (float R, float G, float B) result = harness.ReadRenderTargetColor(target, 32, 32);

        Assert.True(
            result.G > source.G + 0.2f,
            $"The green tint did not add green bloom: source {source.G}, actual {result.G}.");
        AssertColorNear((source.R, 0f, source.B), (result.R, 0f, result.B), 0.02f);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void SoftKneeAboveOneDoesNotBloomNearBlackPixels() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using RenderTexture target = host.CreateRenderTexture(
            new RenderTextureDescription(32, 32, SampleCount: 1));
        BloomAssets assets = harness.LoadBloomAssetsFromBuiltInPack();
        Color nearBlack = new(0.02f, 0.02f, 0.02f, 1f);

        harness.Submit(harness.CreateFrameWithPasses(
            1,
            Pass(
                target,
                [],
                [CaptureBloom(assets, threshold: 1f, softKnee: 4f, intensity: 4f)],
                nearBlack)));
        (float R, float G, float B) result = harness.ReadRenderTargetColor(target, 16, 16);

        AssertColorNear((nearBlack.R, nearBlack.G, nearBlack.B), result, 0.005f);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ResizingTheTargetRecreatesTheBloomPyramidAndStillBlooms() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using RenderTexture target = host.CreateRenderTexture(
            new RenderTextureDescription(64, 48, SampleCount: 1));
        BloomAssets assets = harness.LoadBloomAssetsFromBuiltInPack();

        harness.Submit(harness.CreateFrameWithPasses(
            1,
            Pass(target, [], [CaptureBloom(assets, intensity: 1f)], _uniformHdr)));
        (float R, float G, float B) before = harness.ReadRenderTargetColor(target, 32, 24);
        Assert.True(before.R > _uniformHdr.R + 0.5f);
        int recreationBefore = host.Backend.PostTargetRecreationCountForTests;

        host.ResizeRenderTexture(target, 96, 32);
        harness.Submit(harness.CreateFrameWithPasses(
            2,
            Pass(target, [], [CaptureBloom(assets, intensity: 1f)], _uniformHdr)));
        (float R, float G, float B) after = harness.ReadRenderTargetColor(target, 48, 16);

        Assert.True(
            after.R > _uniformHdr.R + 0.5f,
            $"The resized target did not bloom: source {_uniformHdr.R}, actual {after.R}.");
        Assert.True(host.Backend.PostTargetRecreationCountForTests > recreationBefore);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void MultipleTargetSizesReuseTheBloomPostTargets() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using RenderTexture small = host.CreateRenderTexture(
            new RenderTextureDescription(32, 24, SampleCount: 1));
        using RenderTexture medium = host.CreateRenderTexture(
            new RenderTextureDescription(64, 48, SampleCount: 1));
        using RenderTexture wide = host.CreateRenderTexture(
            new RenderTextureDescription(96, 32, SampleCount: 1));
        BloomAssets assets = harness.LoadBloomAssetsFromBuiltInPack();

        // Each size uses six extents; three sizes exceed the old stack threshold and exercise the
        // slot's reusable stale-extent scratch array.
        for (long frame = 1; frame <= 3; frame++) {
            harness.Submit(CreateMultiTargetFrame(harness, frame, small, medium, wide, assets));
        }

        int recreationAfterWarmup = host.Backend.PostTargetRecreationCountForTests;
        int texturesAfterWarmup = harness.TextureCount;
        Assert.True(recreationAfterWarmup > 0);

        for (long frame = 4; frame <= 6; frame++) {
            harness.Submit(CreateMultiTargetFrame(harness, frame, small, medium, wide, assets));
        }

        Assert.Equal(recreationAfterWarmup, host.Backend.PostTargetRecreationCountForTests);
        Assert.Equal(texturesAfterWarmup, harness.TextureCount);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    private static RenderFramePacket CreateMultiTargetFrame(
        VulkanDrawTestHarness harness,
        long frame,
        RenderTexture small,
        RenderTexture medium,
        RenderTexture wide,
        BloomAssets assets) => harness.CreateFrameWithPasses(
        frame,
        Pass(small, [], [CaptureBloom(assets, intensity: 1f)], _uniformHdr),
        Pass(medium, [], [CaptureBloom(assets, intensity: 1f)], _uniformHdr),
        Pass(wide, [], [CaptureBloom(assets, intensity: 1f)], _uniformHdr));

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ALaterHudCameraWithoutAStackKeepsItsColorsWhileTheSceneHaloBlooms() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using RenderTexture target = host.CreateRenderTexture(
            new RenderTextureDescription(TARGET_WIDTH, TARGET_HEIGHT, SampleCount: 1));
        using BloomScene scene = new(harness, "bloom-hud");
        BloomAssets assets = harness.LoadBloomAssetsFromBuiltInPack();
        VulkanDrawTestHarness.DrawSpec bright = scene.Draw(
            TARGET_WIDTH,
            TARGET_HEIGHT,
            new Vector2(64f, 48f),
            new Vector2(20f, 20f),
            _brightGold);
        VulkanDrawTestHarness.DrawSpec hud = scene.Draw(
            TARGET_WIDTH,
            TARGET_HEIGHT,
            new Vector2(12f, 12f),
            new Vector2(16f, 16f),
            new Color(0.2f, 0.9f, 0.3f, 1f));
        VulkanDrawTestHarness.PassSpec scenePass = Pass(target, [bright], [CaptureBloom(assets)]);
        VulkanDrawTestHarness.PassSpec hudPass = new(
            CreateOrthoView(TARGET_WIDTH, TARGET_HEIGHT),
            0,
            0,
            TARGET_WIDTH,
            TARGET_HEIGHT,
            ClearMode.Depth,
            Color.Black,
            [hud],
            target.TargetId,
            []);

        harness.Submit(harness.CreateFrameWithPasses(1, scenePass, hudPass));

        (float R, float G, float B) hudPixel = harness.ReadRenderTargetColor(target, 12, 12);
        (float R, float G, float B) halo = harness.ReadRenderTargetColor(target, 64, 28);

        AssertColorNear((0.2f, 0.9f, 0.3f), hudPixel, 0.01f);
        Assert.True(
            halo.R > _dark.R + 0.01f,
            $"The scene halo is not bloomed with the later HUD pass: baseline {_dark.R}, actual "
                + $"{halo.R}.");
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void SteadyStateReusesThePyramidTargets() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using RenderTexture target = host.CreateRenderTexture(
            new RenderTextureDescription(64, 48, SampleCount: 1));
        BloomAssets assets = harness.LoadBloomAssetsFromBuiltInPack();
        PostProcessEffectSnapshot bloom = CaptureBloom(assets);

        // One frame per ring slot creates each extent's three-target set; later frames on the same slot
        // must reuse them instead of allocating.
        for (long frame = 1; frame <= 3; frame++) {
            harness.Submit(harness.CreateFrameWithPasses(
                frame,
                Pass(target, [], [bloom], _uniformHdr)));
        }

        int recreationAfterWarmup = host.Backend.PostTargetRecreationCountForTests;
        int texturesAfterWarmup = harness.TextureCount;
        Assert.True(recreationAfterWarmup > 0);

        for (long frame = 4; frame <= 6; frame++) {
            harness.Submit(harness.CreateFrameWithPasses(
                frame,
                Pass(target, [], [bloom], _uniformHdr)));
        }

        Assert.Equal(recreationAfterWarmup, host.Backend.PostTargetRecreationCountForTests);
        Assert.Equal(texturesAfterWarmup, harness.TextureCount);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    private static PostProcessEffectSnapshot CaptureBloom(
        BloomAssets assets,
        float threshold = 1f,
        float softKnee = 0.5f,
        float intensity = 2f,
        float scatter = 0.7f,
        Color? tint = null,
        bool targetHasDepth = true,
        bool enabled = true) {
        var effect = new BloomEffect(assets) {
            Threshold = threshold,
            SoftKnee = softKnee,
            Intensity = intensity,
            Scatter = scatter,
            Tint = tint ?? Color.White,
            Enabled = enabled,
        };
        return PostProcessSnapshots.Capture(
            effect,
            targetHasDepth,
            EFFECT_TARGET_DESCRIPTION,
            bloomAssets: assets);
    }

    private static VulkanDrawTestHarness.PassSpec Pass(
        RenderTexture target,
        VulkanDrawTestHarness.DrawSpec[] draws,
        PostProcessEffectSnapshot[] effects,
        Color? clearColor = null) => new(
        CreateOrthoView(target.Width, target.Height),
        0,
        0,
        target.Width,
        target.Height,
        ClearMode.ColorAndDepth,
        clearColor ?? _dark,
        draws,
        target.TargetId,
        effects);

    private static VulkanDrawTestHarness.CameraView CreateOrthoView(int width, int height) {
        Transform transform = new(new Vector3(0f, 0f, 1f));
        Matrix4x4 view = CameraMath.CreateViewMatrix(transform);
        float aspect = width / (float)height;
        Matrix4x4 projection = CameraMath.CreateOrthographicProjection(height, aspect, 0.1f, 10f);
        return new VulkanDrawTestHarness.CameraView(
            view,
            projection,
            view * projection,
            transform.Position,
            width,
            height);
    }

    private static Matrix4x4 PixelQuad(
        int targetWidth,
        int targetHeight,
        Vector2 center,
        Vector2 size) => Matrix4x4.CreateScale(size.X, size.Y, 1f)
        * Matrix4x4.CreateTranslation(
            center.X - (targetWidth * 0.5f),
            (targetHeight * 0.5f) - center.Y,
            0f);

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

    private sealed class BloomScene : IDisposable {
        private readonly Shader _shader;
        private readonly Texture _texture;
        private readonly Mesh _quad;

        internal BloomScene(VulkanDrawTestHarness harness, string prefix) {
            _shader = harness.CreateShader(
                prefix + "-shader",
                VulkanDrawTestHarness.DEFAULT_VERTEX_SOURCE,
                VulkanDrawTestHarness.DEFAULT_FRAGMENT_SOURCE,
                [
                    new ShaderUniformAsset {
                        Name = "uTint",
                        Type = ShaderUniformType.Vector4,
                        Default = [1f, 1f, 1f, 1f],
                    },
                ],
                ["mainTexture"]);
            _texture = harness.CreateTexture(prefix + "-white", Color.White);
            _quad = harness.CreateQuad(prefix + "-quad", 0.5f, 0.5f);
            _shader.SetUniform("mainTexture", _texture);
            _shader.SetUniform("uTint", Color.White);
        }

        internal VulkanDrawTestHarness.DrawSpec Draw(
            int targetWidth,
            int targetHeight,
            Vector2 center,
            Vector2 size,
            Color color) => new(
            _quad,
            _shader,
            [PixelQuad(targetWidth, targetHeight, center, size)],
            Tint: color);

        public void Dispose() {
            _shader.Dispose();
            _texture.Dispose();
            _quad.Dispose();
        }
    }
}
