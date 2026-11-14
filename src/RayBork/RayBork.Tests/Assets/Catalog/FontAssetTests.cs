using System.Text;
using RayBork.Internal;
using RayBork.Tests.Assets.Gpu;
using RayBork.Tests.Fonts;
using RayBork.Tests.Fonts.Glyph;

namespace RayBork.Tests.Assets.Catalog;

/// <summary>
/// Tests the catalog path of <c>Load&lt;Font&gt;</c>: a CPU-only, reference-counted engine asset that
/// parses the face, generates glyph textures on demand, and reports <c>RB1011</c> for a bad face.
/// </summary>
public sealed class FontAssetTests {
    private static readonly string _fontPath = "/" + CatalogTestData.FONT_PATH;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task LoadParsesTheFaceWithoutAGpuUpload() {
        using CatalogTestHarness harness = new(userPacks: [BuildFontPack()]);

        Font font = await harness.Catalog.Load<Font>(_fontPath, TestContext.Current.CancellationToken);

        try {
            Assert.Equal(_fontPath, font.Path);
            Assert.False(font.IsDisposed);
            Assert.Equal(1, font.GlyphCount);
            Assert.Equal(0, font.CachedGlyphCount);
            Assert.Equal(0, harness.Worker.UploadCallCount);
            Assert.Empty(harness.Worker.DestroyCalls);
            Assert.Equal(0, harness.Bridge.LiveResourceCount);
        } finally {
            font.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task TwoLoadsReturnTheSameObjectAndReferenceCountTheOwners() {
        using CatalogTestHarness harness = new(userPacks: [BuildFontPack()]);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        Font first = await harness.Catalog.Load<Font>(_fontPath, cancellationToken);
        Font second = await harness.Catalog.Load<Font>(_fontPath, cancellationToken);

        Assert.Same(first, second);
        Assert.Equal(0, harness.Worker.UploadCallCount);

        first.Dispose();
        Assert.False(first.IsDisposed);

        Font third = await harness.Catalog.Load<Font>(_fontPath, cancellationToken);
        Assert.Same(first, third);

        second.Dispose();
        Assert.False(first.IsDisposed);
        third.Dispose();
        Assert.True(first.IsDisposed);
        Assert.Throws<ObjectDisposedException>(first.Dispose);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ADisposedFontKeepsPathAndStateReadableAndRejectsEveryOtherMember() {
        using CatalogTestHarness harness = new(userPacks: [BuildFontPack()]);

        Font font = await harness.Catalog.Load<Font>(_fontPath, TestContext.Current.CancellationToken);
        string path = font.Path;
        font.Dispose();

        Assert.True(font.IsDisposed);
        Assert.Equal(path, font.Path);
        Assert.Throws<ObjectDisposedException>(() => _ = font.LineHeight);
        Assert.Throws<ObjectDisposedException>(() => _ = font.Ascent);
        Assert.Throws<ObjectDisposedException>(() => _ = font.Descent);
        Assert.Throws<ObjectDisposedException>(() => _ = font.GlyphCount);
        Assert.Throws<ObjectDisposedException>(() => _ = font.CachedGlyphCount);
        Assert.Throws<ObjectDisposedException>(() => font.TryGetGlyph(new Rune('A'), out _));
        Assert.Throws<ObjectDisposedException>(() => font.Measure("A", 1f));
        Assert.Throws<ObjectDisposedException>(() => _ = font.Metrics);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task AFontPathLoadedAsAnotherTypeIsATypeMismatch() {
        using CatalogTestHarness harness = new(userPacks: [CatalogTestData.BuildStandardPack(), BuildFontPack()]);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        AssetTypeMismatchException asTexture = await Assert.ThrowsAsync<AssetTypeMismatchException>(
            () => harness.Catalog.Load<Texture>(_fontPath, cancellationToken));
        AssetTypeMismatchException asMesh = await Assert.ThrowsAsync<AssetTypeMismatchException>(
            () => harness.Catalog.Load<Font>("/meshes/quad", cancellationToken));

        Assert.Equal(DiagnosticCodes.ASSET_TYPE_MISMATCH, asTexture.DiagnosticCode);
        Assert.Equal(DiagnosticCodes.ASSET_TYPE_MISMATCH, asMesh.DiagnosticCode);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task MetricsComeFromTheParsedFace() {
        using CatalogTestHarness harness = new(userPacks: [BuildFontPack()]);

        Font font = await harness.Catalog.Load<Font>(_fontPath, TestContext.Current.CancellationToken);

        try {
            Assert.Equal(1f, font.LineHeight);
            Assert.Equal(0.8f, font.Ascent);
            Assert.Equal(0.2f, font.Descent);
            Assert.Equal(new System.Numerics.Vector2(6f, 10f), font.Measure("A", 10f));
        } finally {
            font.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task GlyphGenerationUploadsOneProceduralTexturePerGlyph() {
        using CatalogTestHarness harness = new(userPacks: [BuildFontPack()]);
        Font font = await harness.Catalog.Load<Font>(_fontPath, TestContext.Current.CancellationToken);
        try {
            Assert.False(font.TryGetGlyph(new Rune('A'), out _));
            Assert.Equal(0, font.CachedGlyphCount);

            font.BeginFrame(1);
            FrameAssetRetention probe = new();
            Assert.Null(font.AcquireGlyphTexture(CatalogTestData.FONT_GLYPH_ID, probe));
            probe.Release();
            await font.WhenGlyphSettledAsync(CatalogTestData.FONT_GLYPH_ID)
                .WaitAsync(GlyphPipelineTestHarness.TIMEOUT, TestContext.Current.CancellationToken);

            Assert.Equal(1, font.CachedGlyphCount);
            Assert.True(font.TryGetGlyph(new Rune('A'), out Glyph glyph));
            Assert.Equal(new Rune('A'), glyph.Rune);
            Assert.Equal(1, harness.Worker.UploadCallCount);
            Assert.Equal(AssetGpuUploadKind.ProceduralTexture, harness.Worker.UploadCalls[0].Kind);
            Assert.Contains("_procedural/texture/", harness.Worker.UploadCalls[0].Path, StringComparison.Ordinal);

            FrameAssetRetention frame = new();
            Texture texture = font.AcquireGlyphTexture(CatalogTestData.FONT_GLYPH_ID, frame)
                ?? throw new InvalidOperationException("The generated glyph texture was not cached.");
            Assert.Equal(1, frame.Count);
            Assert.Equal(TextureFormat.Rgba8Unorm, texture.Format);
            Assert.False(texture.IsSrgb);
            Assert.Equal(new SamplerState(TextureFilter.Linear, TextureWrap.Clamp), texture.Sampler);
            frame.Release();
        } finally {
            font.Dispose();
        }

        // Disposing the font disposes its glyph cache, which releases the procedural texture.
        AssetCatalogLoadTests.WaitUntil(
            () => harness.Worker.DestroyCalls.Count == 1,
            "The glyph texture is destroyed when the disposed font releases its cache.");
        Assert.Equal(0, harness.Bridge.LiveResourceCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ACorruptFaceIsAFontLoadFailure() {
        byte[] corrupt = [0x00, 0x01, 0x00, 0x00];
        AssetLoadException thrown = await LoadFailureAsync(CatalogTestData.CreateFontEntries(face: corrupt));

        Assert.Equal(DiagnosticCodes.FONT_LOAD_FAILED, thrown.DiagnosticCode);
        Assert.Contains("invalid font face", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ACff2FaceIsRejectedAsAnUnsupportedFeature() {
        byte[] cff2 = SyntheticFontBuilder.Create()
            .UseCff2([0x01, 0x00, 0x04, 0x04])
            .WithCmap(4, (0x41, 1))
            .Build();
        AssetLoadException thrown = await LoadFailureAsync(CatalogTestData.CreateFontEntries(face: cff2));

        Assert.Equal(DiagnosticCodes.FONT_LOAD_FAILED, thrown.DiagnosticCode);
        Assert.Contains("unsupported font feature", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("CFF2", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task AMissingFacePayloadIsAFontLoadFailureNamingThePayload() {
        byte[] face = CatalogTestData.CreateFontFace();
        AssetLoadException thrown = await LoadFailureAsync([
            (CatalogTestData.FONT_PATH,
                AssetEntryType.Font,
                AssetSerialization.Serialize(CatalogTestData.CreateFontDescriptor(face))),
        ]);

        Assert.Equal(DiagnosticCodes.FONT_LOAD_FAILED, thrown.DiagnosticCode);
        Assert.Contains(CatalogTestData.FONT_PAYLOAD_PATH, thrown.Message, StringComparison.Ordinal);
        Assert.Contains("not in the visible pack stack", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task AWrongByteLengthIsAFontLoadFailureNamingBothLengths() {
        byte[] face = CatalogTestData.CreateFontFace();
        FontAssetDescriptor descriptor = CatalogTestData.CreateFontDescriptor(face, byteLength: (uint)face.Length + 1);
        AssetLoadException thrown = await LoadFailureAsync(
            CatalogTestData.CreateFontEntries(face, descriptor));

        Assert.Equal(DiagnosticCodes.FONT_LOAD_FAILED, thrown.DiagnosticCode);
        Assert.Contains("descriptor declares", thrown.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    [InlineData(0, 4, 2)]
    [InlineData(32, 0, 2)]
    [InlineData(32, 4, -1)]
    [InlineData(4, 4, 2)]
    public async Task InvalidMsdfSettingsAreAFontLoadFailure(int pixelSize, int range, int padding) {
        byte[] face = CatalogTestData.CreateFontFace();
        FontAssetDescriptor descriptor = CatalogTestData.CreateFontDescriptor(
            face,
            msdfPixelSize: pixelSize,
            msdfRange: range,
            msdfPadding: padding);
        AssetLoadException thrown = await LoadFailureAsync(
            CatalogTestData.CreateFontEntries(face, descriptor));

        Assert.Equal(DiagnosticCodes.FONT_LOAD_FAILED, thrown.DiagnosticCode);
        Assert.Contains("invalid MSDF settings", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task AFailedFontLoadIssuesNoGpuUpload() {
        byte[] face = CatalogTestData.CreateFontFace();
        FontAssetDescriptor bad = CatalogTestData.CreateFontDescriptor(
            face,
            msdfPixelSize: 0);
        using CatalogTestHarness harness = new(userPacks: [BuildFontPack(face, bad)]);

        await Assert.ThrowsAsync<AssetLoadException>(
            () => harness.Catalog.Load<Font>(_fontPath, TestContext.Current.CancellationToken));

        Assert.Equal(0, harness.Worker.UploadCallCount);
        Assert.Equal(0, harness.Bridge.LiveResourceCount);
    }

    private static async Task<AssetLoadException> LoadFailureAsync(
        (string Path, AssetEntryType Type, byte[] Payload)[] entries) {
        using CatalogTestHarness harness = new(userPacks: [CatalogTestData.BuildPack(entries)]);
        return await Assert.ThrowsAsync<AssetLoadException>(
            () => harness.Catalog.Load<Font>(_fontPath, TestContext.Current.CancellationToken));
    }

    private static byte[] BuildFontPack(
        byte[]? face = null,
        FontAssetDescriptor? descriptor = null)
        => CatalogTestData.BuildPack(CatalogTestData.CreateFontEntries(face, descriptor));
}
