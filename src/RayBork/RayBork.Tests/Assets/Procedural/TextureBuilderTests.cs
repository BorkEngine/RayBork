using RayBork.Internal;
using RayBork.Tests.Assets.Gpu;

namespace RayBork.Tests.Assets.Procedural;

public sealed class TextureBuilderTests {
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(16385, 1)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 16385)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DimensionsMustBePositiveAndWithinTheLimit(int width, int height) {
        using ProceduralTestHarness harness = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => harness.Service.CreateTextureBuilder(width, height));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void OnlyUncompressedProceduralFormatsAreAccepted() {
        using ProceduralTestHarness harness = new();

        Assert.Throws<ArgumentException>(
            () => harness.Service.CreateTextureBuilder(1, 1, TextureFormat.Bc7Srgb));
        Assert.Throws<ArgumentException>(
            () => harness.Service.CreateTextureBuilder(1, 1, TextureFormat.Bc5Unorm));
        Assert.Throws<ArgumentException>(
            () => harness.Service.CreateTextureBuilder(1, 1, TextureFormat.Rgba16Float));
        Assert.Throws<ArgumentException>(
            () => harness.Service.CreateTextureBuilder(1, 1, TextureFormat.Depth32Float));

        harness.Service.CreateTextureBuilder(1, 1, TextureFormat.Rgba8Unorm);
        harness.Service.CreateTextureBuilder(1, 1, TextureFormat.Rgba8Srgb);
        harness.Service.CreateTextureBuilder(1, 1, TextureFormat.R8Unorm);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AnInvalidSamplerStateIsRejected() {
        using ProceduralTestHarness harness = new();

        Assert.Throws<ArgumentException>(() => harness.Service.CreateTextureBuilder(
            1,
            1,
            sampler: new SamplerState((TextureFilter)42, TextureWrap.Repeat)));
        Assert.Throws<ArgumentException>(() => harness.Service.CreateTextureBuilder(
            1,
            1,
            sampler: new SamplerState(TextureFilter.Linear, (TextureWrap)42)));
        Assert.Throws<ArgumentException>(() => harness.Service.CreateTextureBuilder(
            1,
            1,
            sampler: new SamplerState(TextureFilter.Anisotropic, TextureWrap.Repeat, 0.5f)));
        Assert.Throws<ArgumentException>(() => harness.Service.CreateTextureBuilder(
            1,
            1,
            sampler: new SamplerState(TextureFilter.Anisotropic, TextureWrap.Repeat, float.NaN)));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void BytePixelsMustMatchTheSizeAndFormat() {
        using ProceduralTestHarness harness = new();

        TextureBuilder rgba = harness.Service.CreateTextureBuilder(2, 2, TextureFormat.Rgba8Srgb);
        Assert.Throws<ArgumentException>(() => rgba.SetPixels(new byte[15]));
        Assert.Throws<ArgumentException>(() => rgba.SetPixels(new byte[17]));
        rgba.SetPixels(new byte[16]);

        TextureBuilder r8 = harness.Service.CreateTextureBuilder(2, 2, TextureFormat.R8Unorm);
        Assert.Throws<ArgumentException>(() => r8.SetPixels(new byte[3]));
        r8.SetPixels(new byte[4]);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ColorPixelsMustMatchTheSizeAndBeFinite() {
        using ProceduralTestHarness harness = new();
        TextureBuilder builder = harness.Service.CreateTextureBuilder(2, 2);

        Assert.Throws<ArgumentException>(() => builder.SetPixels(new Color[3]));
        Assert.Throws<ArgumentException>(
            () => builder.SetPixels([
                Color.White,
                Color.White,
                Color.White,
                new Color(float.NaN, 0f, 0f, 1f),
            ]));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task BuildWithoutBasePixelsIsRejected() {
        using ProceduralTestHarness harness = new();
        TextureBuilder builder = harness.Service.CreateTextureBuilder(2, 2);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => builder.BuildAsync(TestContext.Current.CancellationToken));
        Assert.Throws<InvalidOperationException>(() => builder.GenerateMips());
        Assert.Equal(0, harness.Worker.UploadCallCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task BytePixelsAreUploadedVerbatimWithoutMips() {
        using ProceduralTestHarness harness = new();
        TextureBuilder builder = harness.Service.CreateTextureBuilder(
            2,
            2,
            TextureFormat.Rgba8Srgb,
            generateMips: false);
        byte[] pixels = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16];
        builder.SetPixels(pixels);

        Texture texture = await builder.BuildAsync(TestContext.Current.CancellationToken);
        try {
            Assert.StartsWith("/_procedural/texture/", texture.Path, StringComparison.Ordinal);
            Assert.True(texture.IsProcedural);
            Assert.Equal(2, texture.Width);
            Assert.Equal(2, texture.Height);
            Assert.Equal(1, texture.MipLevels);
            Assert.Equal(TextureFormat.Rgba8Srgb, texture.Format);
            Assert.True(texture.IsSrgb);
            Assert.Equal(new SamplerState(TextureFilter.Linear, TextureWrap.Repeat, 1f), texture.Sampler);

            ProceduralTextureGpuUpload upload = Assert.IsType<ProceduralTextureGpuUpload>(
                harness.Worker.UploadCalls[0]);
            Assert.Equal(pixels, upload.PixelData.ToArray());
            Assert.Equal(new[] { pixels.Length }, upload.LevelByteLengths.ToArray());
            Assert.Equal(pixels.Length, upload.EstimatedBytes);
        } finally {
            texture.Dispose();
        }

        Assert.True(texture.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => _ = texture.IsProcedural);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task CustomSamplerStateIsCarriedToTheTexture() {
        using ProceduralTestHarness harness = new();
        SamplerState sampler = new(TextureFilter.Nearest, TextureWrap.Clamp, 1f);
        TextureBuilder builder = harness.Service.CreateTextureBuilder(1, 1, TextureFormat.R8Unorm, sampler: sampler);
        builder.SetPixels(new byte[] { 7 });

        Texture texture = await builder.BuildAsync(TestContext.Current.CancellationToken);
        try {
            Assert.Equal(sampler, texture.Sampler);
            Assert.False(texture.IsSrgb);
        } finally {
            texture.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ColorPathConvertsLinearUnormChannels() {
        using ProceduralTestHarness harness = new();
        TextureBuilder builder = harness.Service.CreateTextureBuilder(
            2,
            1,
            TextureFormat.Rgba8Unorm,
            generateMips: false);
        builder.SetPixels([
            new Color(0f, 0.25f, 0.5f, 1f),
            new Color(1f, 0.5f, 0f, 0.5f),
        ]);

        Texture texture = await builder.BuildAsync(TestContext.Current.CancellationToken);
        try {
            ProceduralTextureGpuUpload upload = Assert.IsType<ProceduralTextureGpuUpload>(
                harness.Worker.UploadCalls[0]);
            Assert.Equal(
                new byte[] { 0, 64, 128, 255, 255, 128, 0, 128 },
                upload.PixelData.ToArray());
        } finally {
            texture.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ColorPathEncodesSrgbRgbAndLeavesAlphaLinear() {
        using ProceduralTestHarness harness = new();
        TextureBuilder builder = harness.Service.CreateTextureBuilder(
            2,
            1,
            TextureFormat.Rgba8Srgb,
            generateMips: false);
        builder.SetPixels([
            new Color(0f, 0.25f, 0.5f, 1f),
            new Color(1f, 0.5f, 0f, 0.5f),
        ]);

        Texture texture = await builder.BuildAsync(TestContext.Current.CancellationToken);
        try {
            ProceduralTextureGpuUpload upload = Assert.IsType<ProceduralTextureGpuUpload>(
                harness.Worker.UploadCalls[0]);
            Assert.Equal(
                new byte[] { 0, 137, 188, 255, 255, 188, 0, 128 },
                upload.PixelData.ToArray());
        } finally {
            texture.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ColorPathStoresOnlyRedForR8() {
        using ProceduralTestHarness harness = new();
        TextureBuilder builder = harness.Service.CreateTextureBuilder(
            2,
            1,
            TextureFormat.R8Unorm,
            generateMips: false);
        builder.SetPixels([
            new Color(0.5f, 1f, 1f, 1f),
            new Color(1f, 0f, 0f, 1f),
        ]);

        Texture texture = await builder.BuildAsync(TestContext.Current.CancellationToken);
        try {
            ProceduralTextureGpuUpload upload = Assert.IsType<ProceduralTextureGpuUpload>(
                harness.Worker.UploadCalls[0]);
            Assert.Equal(new byte[] { 128, 255 }, upload.PixelData.ToArray());
        } finally {
            texture.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task GenerateMipsProducesTheFullChainWithTheBakeLayout() {
        using ProceduralTestHarness harness = new();
        TextureBuilder builder = harness.Service.CreateTextureBuilder(4, 2);

        builder.SetPixels(CreateOpaquePixels(4, 2));
        Texture texture = await builder.BuildAsync(TestContext.Current.CancellationToken);
        try {
            Assert.Equal(3, texture.MipLevels);
            ProceduralTextureGpuUpload upload = Assert.IsType<ProceduralTextureGpuUpload>(
                harness.Worker.UploadCalls[0]);
            Assert.Equal(new[] { 32, 8, 4 }, upload.LevelByteLengths.ToArray());
            Assert.Equal(44, upload.PixelData.Length);
        } finally {
            texture.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task BoxFilterAveragesTheSourceRectangleWithHalfUpRounding() {
        using ProceduralTestHarness harness = new();
        TextureBuilder builder = harness.Service.CreateTextureBuilder(2, 2, TextureFormat.Rgba8Unorm);
        builder.SetPixels([
            0, 0, 0, 0,
            10, 20, 30, 40,
            20, 40, 60, 80,
            30, 60, 90, 120,
        ]);

        Texture texture = await builder.BuildAsync(TestContext.Current.CancellationToken);
        try {
            Assert.Equal(2, texture.MipLevels);
            ProceduralTextureGpuUpload upload = Assert.IsType<ProceduralTextureGpuUpload>(
                harness.Worker.UploadCalls[0]);
            byte[] mip = upload.PixelData.Span[16..].ToArray();
            Assert.Equal(new byte[] { 15, 30, 45, 60 }, mip);
        } finally {
            texture.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ExplicitGenerateMipsIsUsedWhenAutomaticGenerationIsOff() {
        using ProceduralTestHarness harness = new();
        TextureBuilder builder = harness.Service.CreateTextureBuilder(
            4,
            2,
            generateMips: false);
        builder.SetPixels(CreateOpaquePixels(4, 2));

        Texture withoutChain = await builder.BuildAsync(TestContext.Current.CancellationToken);
        try {
            Assert.Equal(1, withoutChain.MipLevels);
        } finally {
            withoutChain.Dispose();
        }

        builder.GenerateMips();
        Texture withChain = await builder.BuildAsync(TestContext.Current.CancellationToken);
        try {
            Assert.Equal(3, withChain.MipLevels);
        } finally {
            withChain.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task SettingPixelsAgainDropsAnExplicitMipChain() {
        using ProceduralTestHarness harness = new();
        TextureBuilder builder = harness.Service.CreateTextureBuilder(
            4,
            2,
            generateMips: false);
        builder.SetPixels(CreateOpaquePixels(4, 2));
        builder.GenerateMips();
        builder.SetPixels(CreateOpaquePixels(4, 2));

        Texture texture = await builder.BuildAsync(TestContext.Current.CancellationToken);
        try {
            Assert.Equal(1, texture.MipLevels);
        } finally {
            texture.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task BuildAsyncReturnsOnlyAfterTheDeferredGpuUploadCompletes() {
        using ProceduralTestHarness harness = new();
        harness.Worker.DeferUploads = true;
        TextureBuilder builder = harness.Service.CreateTextureBuilder(2, 2);
        builder.SetPixels(CreateOpaquePixels(2, 2));

        Task<Texture> build = builder.BuildAsync(TestContext.Current.CancellationToken);
        FakeGpuAssetUploadOperation operation = harness.Worker.WaitForUpload(_timeout);

        Assert.False(build.IsCompleted);
        operation.Complete();
        Texture texture = await build.WaitAsync(_timeout, TestContext.Current.CancellationToken);
        try {
            Assert.True(texture.IsProcedural);
        } finally {
            texture.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task CanceledBuildDestroysTheResourceAndLeavesTheBuilderReusable() {
        using ProceduralTestHarness harness = new();
        harness.Worker.DeferUploads = true;
        TextureBuilder builder = harness.Service.CreateTextureBuilder(2, 2);
        builder.SetPixels(CreateOpaquePixels(2, 2));
        using CancellationTokenSource cancellation = new();

        Task<Texture> build = builder.BuildAsync(cancellation.Token);
        harness.Worker.WaitForUpload(_timeout);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => build.WaitAsync(_timeout, TestContext.Current.CancellationToken));
        Assert.True(
            SpinWait.SpinUntil(() => harness.Worker.CancelCalls.Count == 1, _timeout),
            "The bridge did not cancel the upload operation.");
        Assert.True(
            SpinWait.SpinUntil(() => harness.Bridge.LiveResourceCount == 0, _timeout),
            "The canceled upload left a live resource.");

        harness.Worker.DeferUploads = false;
        Texture texture = await builder.BuildAsync(TestContext.Current.CancellationToken);
        try {
            Assert.True(texture.IsProcedural);
        } finally {
            texture.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task FailedUploadLeavesNoResourceAndTheBuilderReusable() {
        using ProceduralTestHarness harness = new();
        harness.Worker.DeferUploads = true;
        TextureBuilder builder = harness.Service.CreateTextureBuilder(2, 2);
        builder.SetPixels(CreateOpaquePixels(2, 2));

        Task<Texture> build = builder.BuildAsync(TestContext.Current.CancellationToken);
        FakeGpuAssetUploadOperation operation = harness.Worker.WaitForUpload(_timeout);
        operation.Fail(new AssetLoadException(
            "The test upload failed.",
            DiagnosticCodes.ASSET_GPU_UPLOAD_FAILED,
            null));

        await Assert.ThrowsAsync<AssetLoadException>(
            () => build.WaitAsync(_timeout, TestContext.Current.CancellationToken));
        Assert.Equal(0, harness.Bridge.LiveResourceCount);

        harness.Worker.DeferUploads = false;
        Texture texture = await builder.BuildAsync(TestContext.Current.CancellationToken);
        try {
            Assert.True(texture.IsProcedural);
        } finally {
            texture.Dispose();
        }
    }

    private static byte[] CreateOpaquePixels(int width, int height) {
        byte[] pixels = new byte[width * height * 4];
        for (int index = 0; index < pixels.Length; index += 4) {
            pixels[index] = (byte)(index & 0xFF);
            pixels[index + 1] = (byte)((index * 3) & 0xFF);
            pixels[index + 2] = (byte)((index * 7) & 0xFF);
            pixels[index + 3] = 255;
        }

        return pixels;
    }
}
