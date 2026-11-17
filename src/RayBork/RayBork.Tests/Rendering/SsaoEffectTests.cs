using RayBork.Internal;
using RayBork.Tests.Assets.Catalog;

namespace RayBork.Tests.Rendering;

/// <summary>
/// Tests the public SSAO settings and the internal snapshot capture path without a GPU.
/// </summary>
public sealed class SsaoEffectTests {
    [Theory]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    [InlineData(SsaoQuality.Performance, 8, 4)]
    [InlineData(SsaoQuality.Low, 16, 4)]
    [InlineData(SsaoQuality.Medium, 16, 8)]
    [InlineData(SsaoQuality.High, 64, 8)]
    [InlineData(SsaoQuality.Ultra, 64, 16)]
    public void QualitySelectsTheBakedSampleCounts(
        SsaoQuality quality,
        int expectedAoSamples,
        int expectedDenoiseSamples) {
        Assert.Equal(expectedAoSamples, SsaoQualityInfo.AoSampleCount(quality));
        Assert.Equal(expectedDenoiseSamples, SsaoQualityInfo.DenoiseSampleCount(quality));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void QualitySelectsThePrecompiledShaderPath() {
        Assert.Equal(
            SsaoShaderContract.AO_8_PATH,
            SsaoQualityInfo.AoShaderPath(SsaoQuality.Performance));
        Assert.Equal(
            SsaoShaderContract.AO_16_PATH,
            SsaoQualityInfo.AoShaderPath(SsaoQuality.Low));
        Assert.Equal(
            SsaoShaderContract.AO_16_PATH,
            SsaoQualityInfo.AoShaderPath(SsaoQuality.Medium));
        Assert.Equal(
            SsaoShaderContract.AO_64_PATH,
            SsaoQualityInfo.AoShaderPath(SsaoQuality.High));
        Assert.Equal(
            SsaoShaderContract.AO_64_PATH,
            SsaoQualityInfo.AoShaderPath(SsaoQuality.Ultra));
        Assert.Equal(
            SsaoShaderContract.DENOISE_4_PATH,
            SsaoQualityInfo.DenoiseShaderPath(SsaoQuality.Performance));
        Assert.Equal(
            SsaoShaderContract.DENOISE_4_PATH,
            SsaoQualityInfo.DenoiseShaderPath(SsaoQuality.Low));
        Assert.Equal(
            SsaoShaderContract.DENOISE_8_PATH,
            SsaoQualityInfo.DenoiseShaderPath(SsaoQuality.Medium));
        Assert.Equal(
            SsaoShaderContract.DENOISE_8_PATH,
            SsaoQualityInfo.DenoiseShaderPath(SsaoQuality.High));
        Assert.Equal(
            SsaoShaderContract.DENOISE_16_PATH,
            SsaoQualityInfo.DenoiseShaderPath(SsaoQuality.Ultra));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TheDefaultSettingsMatchTheApprovedContract() {
        var effect = new SsaoEffect();

        Assert.Equal(SsaoQuality.Medium, effect.Quality);
        Assert.Equal(1.5f, effect.Radius);
        Assert.Equal(1f, effect.DistanceFalloff);
        Assert.Equal(2f, effect.Intensity);
        Assert.Equal(Color.Black, effect.Color);
        Assert.Equal(12, effect.DenoiseRadius);
        Assert.False(effect.ScreenSpaceRadius);
        Assert.Equal(0f, effect.BiasOffset);
        Assert.Equal(1f, effect.BiasMultiplier);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EverySettingRejectsAnInvalidValueWithAClearException() {
        OnTickThread(() => {
            var effect = new SsaoEffect();

            ArgumentOutOfRangeException quality = Assert.Throws<ArgumentOutOfRangeException>(
                () => effect.Quality = (SsaoQuality)99);
            Assert.Contains("defined", quality.Message, StringComparison.Ordinal);

            AssertRejects(() => effect.Radius = 0f, nameof(SsaoEffect.Radius), "greater than zero");
            AssertRejects(() => effect.Radius = float.NaN, nameof(SsaoEffect.Radius), "finite");
            AssertRejects(
                () => effect.DistanceFalloff = 0f,
                nameof(SsaoEffect.DistanceFalloff),
                "greater than zero");
            AssertRejects(
                () => effect.DistanceFalloff = float.PositiveInfinity,
                nameof(SsaoEffect.DistanceFalloff),
                "finite");
            AssertRejects(() => effect.Intensity = -1f, nameof(SsaoEffect.Intensity), "non-negative");
            AssertRejects(
                () => effect.Intensity = float.NaN,
                nameof(SsaoEffect.Intensity),
                "finite");
            AssertRejects(
                () => effect.DenoiseRadius = -1,
                nameof(SsaoEffect.DenoiseRadius),
                "zero or positive");
            AssertRejects(() => effect.BiasOffset = float.NaN, nameof(SsaoEffect.BiasOffset), "finite");
            AssertRejects(
                () => effect.BiasMultiplier = float.PositiveInfinity,
                nameof(SsaoEffect.BiasMultiplier),
                "finite");

            ArgumentException color = Assert.Throws<ArgumentException>(
                () => effect.Color = new Color(0f, float.NaN, 0f, 1f));
            Assert.Contains("finite", color.Message, StringComparison.Ordinal);

            // A rejected value leaves the previous value in place.
            Assert.Equal(SsaoQuality.Medium, effect.Quality);
            Assert.Equal(1.5f, effect.Radius);
            Assert.Equal(1f, effect.DistanceFalloff);
            Assert.Equal(2f, effect.Intensity);
            Assert.Equal(12, effect.DenoiseRadius);
            Assert.Equal(1f, effect.BiasMultiplier);
        });
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SettingsAreTickThreadOnly() {
        var effect = new SsaoEffect();

        Assert.Throws<InvalidOperationException>(() => effect.Radius = 2f);
        Assert.Throws<InvalidOperationException>(() => effect.Quality = SsaoQuality.High);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CaptureRejectsADepthlessTargetBeforeTouchingTheAssets() {
        var effect = new SsaoEffect();

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(
            () => PostProcessSnapshots.Capture(effect, targetHasDepth: false, "render texture 7"));

        Assert.Contains("depth", thrown.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("render texture 7", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CaptureRequiresThePreloadedSsaoAssets() {
        var effect = new SsaoEffect();

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(
            () => PostProcessSnapshots.Capture(effect, targetHasDepth: true, "the app scene target"));

        Assert.Contains("built-in SSAO assets", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task SnapshotFreezesTheSettingsAndQualityAndIsPooledAfterReset() {
        using CatalogTestHarness harness = new(builtInPack: BuiltInAssetPack.Bytes.ToArray());
        SsaoAssets assets = await SsaoAssets.LoadAsync(harness.Catalog);
        OnTickThread(() => {
            var effect = new SsaoEffect(assets) {
                Quality = SsaoQuality.Medium,
                Radius = 2.25f,
                DenoiseRadius = 7,
            };

            SsaoEffectSnapshot first = Capture(effect, assets);
            Assert.Equal(SsaoQuality.Medium, first.Quality);
            Assert.Equal(2.25f, first.Radius);
            Assert.Equal(7, first.DenoiseRadius);
            Assert.Equal(16, first.AoSampleCount);
            Assert.Equal(8, first.DenoiseSampleCount);
            Assert.Equal(4, first.InstanceLayout.Length);
            Assert.True(first.InstanceLayout[0].Stride > 0);
            Assert.Equal(first.InstanceLayout[1].Stride, first.InstanceLayout[2].Stride);
            Assert.True(first.InstanceLayout[3].Stride > 0);

            // Later public changes only affect later captures.
            effect.Quality = SsaoQuality.Ultra;
            effect.Radius = 4f;
            effect.DenoiseRadius = 3;
            Assert.Equal(SsaoQuality.Medium, first.Quality);
            Assert.Equal(2.25f, first.Radius);
            Assert.Equal(7, first.DenoiseRadius);

            SsaoEffectSnapshot second = Capture(effect, assets);
            Assert.Equal(SsaoQuality.Ultra, second.Quality);
            Assert.Equal(4f, second.Radius);
            Assert.Equal(3, second.DenoiseRadius);
            Assert.Equal(64, second.AoSampleCount);
            Assert.Equal(16, second.DenoiseSampleCount);
            Assert.NotSame(first, second);

            // Releasing the older capture returns it to the effect's pool; the next capture reuses it.
            first.Reset();
            second.Reset();
            SsaoEffectSnapshot reused = Capture(effect, assets);
            Assert.Same(second, reused);
            reused.Reset();
        });
        assets.Dispose();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DuplicateAndDisabledStackEntriesAreCapturedForRendering() {
        using CatalogTestHarness harness = new(builtInPack: BuiltInAssetPack.Bytes.ToArray());
        SsaoAssets assets = await SsaoAssets.LoadAsync(harness.Catalog);
        using CameraTestHost host = new();
        host.Service.AttachSsaoAssets(assets);
        host.BeginFrame();
        PostProcessStack stack = new();
        var first = new SsaoEffect(assets);
        var second = new SsaoEffect(assets) { Enabled = false };
        try {
            stack.Add(first);
            stack.Add(second);
            stack.Add(first);
            using (Camera camera = host.CreatePerspectiveCamera(postProcess: stack)) {
                Assert.True(camera.IsDisposed == false);
            }

            // The actual duplicate-effect render is covered by
            // SsaoGpuTests.SsaoTwiceKeepsTheChainSource; this capture test proves both duplicate entries
            // carry a full four-pass layout and the disabled entry keeps its position.
            CameraPass pass = host.Packet.Passes[0];
            Assert.Equal(3, pass.PostProcessEffectCount);
            Assert.Equal(2, pass.PostProcessEffects.ToArray().Count(static snapshot => snapshot.Enabled));
            Assert.False(pass.PostProcessEffects[1].Enabled);
            SsaoEffectSnapshot firstSnapshot = Assert.IsType<SsaoEffectSnapshot>(pass.PostProcessEffects[0]);
            Assert.IsType<DisabledPostProcessEffectSnapshot>(pass.PostProcessEffects[1]);
            SsaoEffectSnapshot secondSnapshot = Assert.IsType<SsaoEffectSnapshot>(pass.PostProcessEffects[2]);
            Assert.Equal(4, firstSnapshot.InstanceLayout.Length);
            Assert.Equal(4, secondSnapshot.InstanceLayout.Length);
            Assert.True(firstSnapshot.InstanceLayout[0].Stride > 0);
            Assert.True(secondSnapshot.InstanceLayout[0].Stride > 0);
        } finally {
            host.EndFrame();
            host.Service.DetachSsaoAssets();
            assets.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task TheBuiltInAssetsExposeTheThreeArenaAndTheBlueNoiseContract() {
        using CatalogTestHarness harness = new(builtInPack: BuiltInAssetPack.Bytes.ToArray());
        SsaoAssets assets = await SsaoAssets.LoadAsync(harness.Catalog);
        try {
            Assert.False(assets.IsDisposed);
            Assert.Equal(128, assets.BlueNoise.Width);
            Assert.Equal(128, assets.BlueNoise.Height);
            Assert.Equal(TextureFormat.Rgba8Unorm, assets.BlueNoise.Format);
            Assert.False(assets.BlueNoise.IsSrgb);
            Assert.Equal(TextureFilter.Nearest, assets.BlueNoise.Sampler.Filter);
            Assert.Equal(TextureWrap.Repeat, assets.BlueNoise.Sampler.Wrap);
            Assert.Equal(
                SsaoShaderContract.AO_8_PATH,
                assets.AoProgram(SsaoQuality.Performance).Path);
            Assert.Equal(
                SsaoShaderContract.AO_16_PATH,
                assets.AoProgram(SsaoQuality.Medium).Path);
            Assert.Equal(
                SsaoShaderContract.AO_64_PATH,
                assets.AoProgram(SsaoQuality.Ultra).Path);
            Assert.Equal(
                SsaoShaderContract.DENOISE_16_PATH,
                assets.DenoiseProgram(SsaoQuality.Ultra).Path);
        } finally {
            assets.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DisposedAssetsAreRejectedByCapture() {
        using CatalogTestHarness harness = new(builtInPack: BuiltInAssetPack.Bytes.ToArray());
        SsaoAssets assets = await SsaoAssets.LoadAsync(harness.Catalog);
        assets.Dispose();

        var effect = new SsaoEffect(assets);
        Assert.Throws<ObjectDisposedException>(
            () => PostProcessSnapshots.Capture(effect, targetHasDepth: true, "the app scene target"));
    }

    private static SsaoEffectSnapshot Capture(SsaoEffect effect, SsaoAssets assets) {
        PostProcessEffectSnapshot snapshot = PostProcessSnapshots.Capture(
            effect,
            targetHasDepth: true,
            "the app scene target",
            assets);
        return Assert.IsType<SsaoEffectSnapshot>(snapshot);
    }

    private static void AssertRejects(
        Action action,
        string parameterName,
        string expectedMessagePart) {
        ArgumentOutOfRangeException thrown = Assert.Throws<ArgumentOutOfRangeException>(action);
        Assert.Equal(parameterName, thrown.ParamName);
        Assert.Contains(expectedMessagePart, thrown.Message, StringComparison.Ordinal);
    }

    private static void OnTickThread(Action action) {
        bool attached = TickThread.IsCurrent;
        if (!attached) {
            TickThread.Attach();
        }

        try {
            action();
        } finally {
            if (!attached) {
                TickThread.Detach();
            }
        }
    }
}
