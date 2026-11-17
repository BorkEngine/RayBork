using RayBork.Internal;
using RayBork.Tests.Assets.Catalog;
using Silk.NET.Vulkan;

namespace RayBork.Tests.Rendering;

/// <summary>
/// Tests the public bloom settings, the pyramid extents, and the internal snapshot capture path without
/// a GPU.
/// </summary>
public sealed class BloomEffectTests
{
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TheDefaultSettingsMatchTheApprovedContract()
    {
        var effect = new BloomEffect();

        Assert.Equal(1f, effect.Threshold);
        Assert.Equal(0.5f, effect.SoftKnee);
        Assert.Equal(0.7f, effect.Intensity);
        Assert.Equal(0.7f, effect.Scatter);
        Assert.Equal(Color.White, effect.Tint);
        Assert.True(effect.Enabled);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EverySettingRejectsAnInvalidValueWithAClearException()
    {
        OnTickThread(() =>
        {
            var effect = new BloomEffect();

            AssertRejects(
                () => effect.Threshold = -1f,
                nameof(BloomEffect.Threshold),
                "non-negative"
            );
            AssertRejects(
                () => effect.Threshold = float.NaN,
                nameof(BloomEffect.Threshold),
                "finite"
            );
            AssertRejects(
                () => effect.SoftKnee = -1f,
                nameof(BloomEffect.SoftKnee),
                "non-negative"
            );
            AssertRejects(
                () => effect.SoftKnee = float.PositiveInfinity,
                nameof(BloomEffect.SoftKnee),
                "finite"
            );
            AssertRejects(
                () => effect.Intensity = -1f,
                nameof(BloomEffect.Intensity),
                "non-negative"
            );
            AssertRejects(
                () => effect.Intensity = float.NaN,
                nameof(BloomEffect.Intensity),
                "finite"
            );
            AssertRejects(() => effect.Scatter = -1f, nameof(BloomEffect.Scatter), "non-negative");
            AssertRejects(
                () => effect.Scatter = float.PositiveInfinity,
                nameof(BloomEffect.Scatter),
                "finite"
            );

            ArgumentException tint = Assert.Throws<ArgumentException>(() =>
                effect.Tint = new Color(1f, float.NaN, 1f, 1f)
            );
            Assert.Contains("finite", tint.Message, StringComparison.Ordinal);

            // A rejected value leaves the previous value in place.
            Assert.Equal(1f, effect.Threshold);
            Assert.Equal(0.5f, effect.SoftKnee);
            Assert.Equal(0.7f, effect.Intensity);
            Assert.Equal(0.7f, effect.Scatter);
            Assert.Equal(Color.White, effect.Tint);
        });
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SettingsAreTickThreadOnly()
    {
        var effect = new BloomEffect();

        Assert.Throws<InvalidOperationException>(() => effect.Threshold = 2f);
        Assert.Throws<InvalidOperationException>(() => effect.SoftKnee = 1f);
        Assert.Throws<InvalidOperationException>(() => effect.Intensity = 1f);
        Assert.Throws<InvalidOperationException>(() => effect.Scatter = 1f);
        Assert.Throws<InvalidOperationException>(() => effect.Tint = Color.White);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CaptureRequiresThePreloadedBloomAssets()
    {
        var effect = new BloomEffect();

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() =>
            PostProcessSnapshots.Capture(effect, targetHasDepth: true, "the app scene target")
        );

        Assert.Contains("built-in bloom assets", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task CaptureAcceptsADepthlessTargetAndReportsTheMaximumLayout()
    {
        using CatalogTestHarness harness = new(builtInPack: BuiltInAssetPack.Bytes.ToArray());
        BloomAssets assets = await BloomAssets.LoadAsync(harness.Catalog);
        try
        {
            OnTickThread(() =>
            {
                var effect = new BloomEffect(assets);
                BloomEffectSnapshot snapshot = Assert.IsType<BloomEffectSnapshot>(
                    PostProcessSnapshots.Capture(
                        effect,
                        targetHasDepth: false,
                        "render texture 3",
                        bloomAssets: assets
                    )
                );

                Assert.Equal(BloomPyramid.MAX_PASSES, snapshot.InstanceLayout.Length);
                for (int index = 0; index < snapshot.InstanceLayout.Length; index++)
                {
                    Assert.True(snapshot.InstanceLayout[index].Stride > 0);
                    Assert.Equal(1, snapshot.InstanceLayout[index].Count);
                }

                // The prefilter, downsample, and upsample records share a 112-byte stride; the composite
                // carries the vec4 tint and is the only 144-byte record.
                Assert.Equal(112, snapshot.InstanceLayout[0].Stride);
                Assert.Equal(112, snapshot.InstanceLayout[1].Stride);
                Assert.Equal(112, snapshot.InstanceLayout[5].Stride);
                Assert.Equal(144, snapshot.InstanceLayout[9].Stride);
                snapshot.Reset();
            });
        }
        finally
        {
            assets.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task TheCameraServiceCapturesABloomEffectOverTheAttachedAssets()
    {
        using CatalogTestHarness harness = new(builtInPack: BuiltInAssetPack.Bytes.ToArray());
        BloomAssets assets = await BloomAssets.LoadAsync(harness.Catalog);
        using CameraTestHost host = new();
        host.Service.AttachBloomAssets(assets);
        host.BeginFrame();
        try
        {
            var effect = new BloomEffect
            {
                Threshold = 3f,
                SoftKnee = 0.25f,
                Intensity = 1.5f,
                Scatter = 0.2f,
            };
            PostProcessStack stack = new();
            stack.Add(effect);
            using (Camera camera = host.CreatePerspectiveCamera(postProcess: stack))
            {
                Assert.False(camera.IsDisposed);
            }

            CameraPass pass = host.Packet.Passes[0];
            BloomEffectSnapshot snapshot = Assert.IsType<BloomEffectSnapshot>(
                pass.PostProcessEffects[0]
            );
            Assert.True(snapshot.Enabled);
            Assert.Equal(3f, snapshot.Threshold);
            Assert.Equal(0.25f, snapshot.SoftKnee);
            Assert.Equal(1.5f, snapshot.Intensity);
            Assert.Equal(0.2f, snapshot.Scatter);
            host.EndFrame();
        }
        finally
        {
            host.Service.DetachBloomAssets();
            assets.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task SnapshotFreezesTheSettingsAndIsPooledAfterReset()
    {
        using CatalogTestHarness harness = new(builtInPack: BuiltInAssetPack.Bytes.ToArray());
        BloomAssets assets = await BloomAssets.LoadAsync(harness.Catalog);
        try
        {
            OnTickThread(() =>
            {
                var effect = new BloomEffect(assets)
                {
                    Threshold = 2f,
                    SoftKnee = 0.25f,
                    Intensity = 1.5f,
                    Scatter = 0.2f,
                    Tint = new Color(0.5f, 0.75f, 1f, 1f),
                };

                BloomEffectSnapshot first = Capture(effect, assets);
                Assert.Equal(2f, first.Threshold);
                Assert.Equal(0.25f, first.SoftKnee);
                Assert.Equal(1.5f, first.Intensity);
                Assert.Equal(0.2f, first.Scatter);
                Assert.Equal(new Color(0.5f, 0.75f, 1f, 1f), first.Tint);
                Assert.Equal(BloomPyramid.MAX_PASSES, first.InstanceLayout.Length);

                // Later public changes only affect later captures.
                effect.Threshold = 6f;
                effect.SoftKnee = 0f;
                effect.Intensity = 3f;
                effect.Scatter = 1f;
                effect.Tint = Color.White;
                Assert.Equal(2f, first.Threshold);
                Assert.Equal(0.25f, first.SoftKnee);
                Assert.Equal(1.5f, first.Intensity);
                Assert.Equal(0.2f, first.Scatter);

                BloomEffectSnapshot second = Capture(effect, assets);
                Assert.Equal(6f, second.Threshold);
                Assert.Equal(0f, second.SoftKnee);
                Assert.Equal(3f, second.Intensity);
                Assert.Equal(1f, second.Scatter);
                Assert.NotSame(first, second);

                // Releasing the older captures returns them to the effect's pool; the next capture reuses
                // the most recently returned snapshot.
                first.Reset();
                second.Reset();
                BloomEffectSnapshot reused = Capture(effect, assets);
                Assert.Same(second, reused);
                reused.Reset();
            });
        }
        finally
        {
            assets.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task TheBuiltInAssetsExposeTheFourPyramidPrograms()
    {
        using CatalogTestHarness harness = new(builtInPack: BuiltInAssetPack.Bytes.ToArray());
        BloomAssets assets = await BloomAssets.LoadAsync(harness.Catalog);
        try
        {
            Assert.False(assets.IsDisposed);
            Assert.Equal(BloomShaderContract.PREFILTER_PATH, assets.PrefilterProgram.Path);
            Assert.Equal(BloomShaderContract.DOWNSAMPLE_PATH, assets.DownsampleProgram.Path);
            Assert.Equal(BloomShaderContract.UPSAMPLE_PATH, assets.UpsampleProgram.Path);
            Assert.Equal(BloomShaderContract.COMPOSITE_PATH, assets.CompositeProgram.Path);
        }
        finally
        {
            assets.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DisposedAssetsAreRejectedByCapture()
    {
        using CatalogTestHarness harness = new(builtInPack: BuiltInAssetPack.Bytes.ToArray());
        BloomAssets assets = await BloomAssets.LoadAsync(harness.Catalog);
        assets.Dispose();

        var effect = new BloomEffect(assets);
        Assert.Throws<ObjectDisposedException>(() =>
            PostProcessSnapshots.Capture(
                effect,
                targetHasDepth: true,
                "the app scene target",
                bloomAssets: assets
            )
        );
    }

    [Theory]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    [InlineData(1280, 720, new[] { 640, 320, 160, 80, 40 }, new[] { 360, 180, 90, 45, 22 })]
    [InlineData(64, 64, new[] { 32, 16, 8, 4, 2 }, new[] { 32, 16, 8, 4, 2 })]
    [InlineData(32, 32, new[] { 16, 8, 4, 2, 1 }, new[] { 16, 8, 4, 2, 1 })]
    [InlineData(8, 8, new[] { 4, 2, 1 }, new[] { 4, 2, 1 })]
    [InlineData(7, 5, new[] { 3, 1 }, new[] { 2, 1 })]
    [InlineData(3, 3, new[] { 1 }, new[] { 1 })]
    [InlineData(1, 1, new[] { 1 }, new[] { 1 })]
    public void ThePyramidClampsEveryExtentAndSkipsRepeatedOnes(
        int width,
        int height,
        int[] expectedWidths,
        int[] expectedHeights
    )
    {
        Span<Extent2D> levels = stackalloc Extent2D[BloomPyramid.MAX_LEVELS];

        int count = BloomPyramid.BuildLevels(width, height, levels);

        Assert.Equal(expectedWidths.Length, count);
        for (int index = 0; index < count; index++)
        {
            Assert.Equal((uint)expectedWidths[index], levels[index].Width);
            Assert.Equal((uint)expectedHeights[index], levels[index].Height);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ThePyramidRejectsInvalidExtentsAndAShortDestination()
    {
        Extent2D[] levels = new Extent2D[BloomPyramid.MAX_LEVELS];
        Assert.Throws<ArgumentOutOfRangeException>(() => BloomPyramid.BuildLevels(0, 64, levels));
        Assert.Throws<ArgumentOutOfRangeException>(() => BloomPyramid.BuildLevels(64, -1, levels));

        Extent2D[] shortLevels = new Extent2D[BloomPyramid.MAX_LEVELS - 1];
        Assert.Throws<ArgumentException>(() => BloomPyramid.BuildLevels(64, 64, shortLevels));
    }

    private static BloomEffectSnapshot Capture(BloomEffect effect, BloomAssets assets)
    {
        PostProcessEffectSnapshot snapshot = PostProcessSnapshots.Capture(
            effect,
            targetHasDepth: true,
            "the app scene target",
            bloomAssets: assets
        );
        return Assert.IsType<BloomEffectSnapshot>(snapshot);
    }

    private static void AssertRejects(
        Action action,
        string parameterName,
        string expectedMessagePart
    )
    {
        ArgumentOutOfRangeException thrown = Assert.Throws<ArgumentOutOfRangeException>(action);
        Assert.Equal(parameterName, thrown.ParamName);
        Assert.Contains(expectedMessagePart, thrown.Message, StringComparison.Ordinal);
    }

    private static void OnTickThread(Action action)
    {
        bool attached = TickThread.IsCurrent;
        if (!attached)
        {
            TickThread.Attach();
        }

        try
        {
            action();
        }
        finally
        {
            if (!attached)
            {
                TickThread.Detach();
            }
        }
    }
}
