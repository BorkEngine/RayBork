using System.Text;
using RayBork.Internal;
using RayBork.Tests.Fonts.Glyph;

namespace RayBork.Tests.Assets.Catalog;

/// <summary>
/// Tests the app-owned <see cref="EngineDefaults"/> preload: the built-in mesh shader program, the white
/// and error textures, and the default and monospaced fonts are loaded once, handed to the game, and
/// released during shutdown.
/// </summary>
public sealed class EngineDefaultsTests {
    private const string MESH_SHADER_PATH = "/shaders/mesh";
    private const string WHITE_PATH = "/textures/white";
    private const string ERROR_PATH = "/textures/error";
    private const string FONT_PATH = "/fonts/fira-sans";
    private const string MONO_FONT_PATH = "/fonts/jetbrains-mono";

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task LoadAsyncPreloadsTheProgramBothTexturesAndBothDefaultFonts() {
        using CatalogTestHarness harness = new(userPacks: [BuildDefaultsPack()]);

        EngineDefaults defaults = await EngineDefaults.LoadAsync(harness.Catalog);

        try {
            Assert.False(defaults.IsDisposed);
            Assert.Equal(WHITE_PATH, defaults.White.Path);
            Assert.Equal(ERROR_PATH, defaults.Error.Path);
            Assert.Equal(FONT_PATH, defaults.DefaultFont.Path);
            Assert.Equal(1, defaults.DefaultFont.GlyphCount);
            Assert.Equal(0, defaults.DefaultFont.CachedGlyphCount);
            Assert.Equal(MONO_FONT_PATH, defaults.DefaultMonoFont.Path);
            Assert.Equal(1, defaults.DefaultMonoFont.GlyphCount);
            Assert.Equal(0, defaults.DefaultMonoFont.CachedGlyphCount);
            Assert.NotSame(defaults.DefaultFont, defaults.DefaultMonoFont);
            Assert.Equal(3, harness.Worker.UploadCallCount);

            Shader material = defaults.CreateMeshShader();
            try {
                Assert.Equal(MESH_SHADER_PATH, material.Path);
                Assert.False(material.IsDisposed);
                Assert.Equal(3, harness.Worker.UploadCallCount);
            } finally {
                material.Dispose();
            }
        } finally {
            defaults.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task CreateMeshShaderReturnsIndependentMaterialsOverThePreloadedProgram() {
        using CatalogTestHarness harness = new(userPacks: [BuildDefaultsPack()]);
        EngineDefaults defaults = await EngineDefaults.LoadAsync(harness.Catalog);

        try {
            Shader first = defaults.CreateMeshShader();
            Shader second = defaults.CreateMeshShader();
            try {
                Assert.NotSame(first, second);
                Assert.Same(first.Program, second.Program);
                Assert.Equal(3, harness.Worker.UploadCallCount);

                first.Dispose();
                Assert.True(first.IsDisposed);
                Assert.False(second.IsDisposed);
                Assert.False(second.Program.IsDisposed);
                Assert.Empty(harness.Worker.DestroyCalls);
            } finally {
                if (!first.IsDisposed) {
                    first.Dispose();
                }

                second.Dispose();
            }
        } finally {
            defaults.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DisposeReleasesTheOwnerReferenceForEveryDefaultExactlyOnce() {
        using CatalogTestHarness harness = new(userPacks: [BuildDefaultsPack()]);
        EngineDefaults defaults = await EngineDefaults.LoadAsync(harness.Catalog);
        Shader material = defaults.CreateMeshShader();
        material.Dispose();

        defaults.Dispose();

        Assert.True(defaults.IsDisposed);
        AssetCatalogLoadTests.WaitUntil(
            () => harness.Worker.DestroyCalls.Count == 3,
            "The program and both textures are destroyed when the defaults release their references.");
        Assert.Equal(0, harness.Bridge.LiveResourceCount);
        Assert.Throws<ObjectDisposedException>(() => _ = defaults.White);
        Assert.Throws<ObjectDisposedException>(() => _ = defaults.Error);
        Assert.Throws<ObjectDisposedException>(() => _ = defaults.DefaultFont);
        Assert.Throws<ObjectDisposedException>(() => _ = defaults.DefaultMonoFont);
        Assert.Throws<ObjectDisposedException>(defaults.CreateMeshShader);

        defaults.Dispose();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task AMissingDefaultFailsWithAClearLoadErrorAndReleasesThePartialLoad() {
        // The mesh shader program exists, but the white and error textures are not in the pack.
        byte[] pack = CatalogTestData.BuildPack(
            ("shaders/mesh", AssetEntryType.Shader, AssetSerialization.Serialize(CatalogTestData.CreateShaderDescriptor())),
            ("shaders/basic-vert", AssetEntryType.Raw, CatalogTestData.CreateShaderVertexPayload()),
            ("shaders/basic-frag", AssetEntryType.Raw, CatalogTestData.CreateShaderFragmentPayload()));
        using CatalogTestHarness harness = new(userPacks: [pack]);

        AssetLoadException thrown = await Assert.ThrowsAsync<AssetLoadException>(
            () => EngineDefaults.LoadAsync(harness.Catalog));

        Assert.Equal(DiagnosticCodes.ASSET_LOAD_FAILED, thrown.DiagnosticCode);
        Assert.Contains("built-in default", thrown.Message, StringComparison.Ordinal);
        Assert.Contains(WHITE_PATH, thrown.Message, StringComparison.Ordinal);
        AssetCatalogLoadTests.WaitUntil(
            () => harness.Worker.DestroyCalls.Count == 1,
            "The already loaded mesh program must be released when a later default fails.");
        Assert.Equal(0, harness.Bridge.LiveResourceCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task AMissingMeshShaderNamesTheShaderProgramInTheError() {
        byte[] pack = CatalogTestData.BuildPack(
            ("textures/white", AssetEntryType.Texture, AssetSerialization.Serialize(CreateTextureDescriptor("textures/white-ktx2"))),
            ("textures/white-ktx2", AssetEntryType.Raw, CatalogTestData.CreateTexturePayload()));
        using CatalogTestHarness harness = new(userPacks: [pack]);

        AssetLoadException thrown = await Assert.ThrowsAsync<AssetLoadException>(
            () => EngineDefaults.LoadAsync(harness.Catalog));

        Assert.Contains(MESH_SHADER_PATH, thrown.Message, StringComparison.Ordinal);
        Assert.IsType<AssetNotFoundException>(thrown.InnerException);
        Assert.False(harness.Catalog.Exists(MESH_SHADER_PATH));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DisposeReleasesBothDefaultFontsAndAReloadCreatesNewFonts() {
        using CatalogTestHarness harness = new(userPacks: [BuildDefaultsPack()]);
        EngineDefaults defaults = await EngineDefaults.LoadAsync(harness.Catalog);
        Font first = defaults.DefaultFont;
        Font firstMono = defaults.DefaultMonoFont;

        defaults.Dispose();

        Assert.True(first.IsDisposed);
        Assert.True(firstMono.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => _ = first.LineHeight);
        Assert.Throws<ObjectDisposedException>(() => _ = firstMono.LineHeight);

        Font second = await harness.Catalog.Load<Font>(FONT_PATH, TestContext.Current.CancellationToken);
        Font secondMono = await harness.Catalog.Load<Font>(
            MONO_FONT_PATH,
            TestContext.Current.CancellationToken);
        try {
            Assert.NotSame(first, second);
            Assert.NotSame(firstMono, secondMono);
            Assert.False(second.IsDisposed);
            Assert.Equal(1, second.GlyphCount);
            Assert.Equal(0, second.CachedGlyphCount);
            Assert.False(secondMono.IsDisposed);
            Assert.Equal(1, secondMono.GlyphCount);
            Assert.Equal(0, secondMono.CachedGlyphCount);
        } finally {
            second.Dispose();
            secondMono.Dispose();
        }

        Assert.True(second.IsDisposed);
        Assert.True(secondMono.IsDisposed);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task TheBuiltInMonoFontLoadsAsAWorkingFont() {
        using CatalogTestHarness harness = new(builtInPack: BuiltInAssetPack.Bytes.ToArray());

        Font font = await harness.Catalog.Load<Font>(
            MONO_FONT_PATH,
            TestContext.Current.CancellationToken);
        try {
            Assert.Equal(MONO_FONT_PATH, font.Path);
            Assert.True(font.GlyphCount > 0, "The mono font must map glyphs.");
            Assert.Equal(0, font.CachedGlyphCount);
            Assert.True(font.LineHeight > 0f, "The mono font must report a line height.");
            Assert.True(font.Ascent > 0f, "The mono font must report an ascent.");
            Assert.True(font.Measure("0123456789", 1f).X > 0f, "The mono font must measure text.");
        } finally {
            font.Dispose();
        }

        Assert.True(font.IsDisposed);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task GlyphTexturesGeneratedBeforeShutdownAreDestroyedWithTheDefaults() {
        using CatalogTestHarness harness = new(userPacks: [BuildDefaultsPack()]);
        EngineDefaults defaults = await EngineDefaults.LoadAsync(harness.Catalog);
        Font font = defaults.DefaultFont;

        font.BeginFrame(1);
        FrameAssetRetention probe = new();
        Assert.Null(font.AcquireGlyphTexture(CatalogTestData.FONT_GLYPH_ID, probe));
        probe.Release();
        await font.WhenGlyphSettledAsync(CatalogTestData.FONT_GLYPH_ID)
            .WaitAsync(GlyphPipelineTestHarness.TIMEOUT, TestContext.Current.CancellationToken);

        Assert.Equal(1, font.CachedGlyphCount);
        Assert.Equal(4, harness.Worker.UploadCallCount);

        defaults.Dispose();

        Assert.True(font.IsDisposed);
        AssetCatalogLoadTests.WaitUntil(
            () => harness.Worker.DestroyCalls.Count == 4,
            "The default font's glyph texture is destroyed with the other defaults.");
        Assert.Equal(0, harness.Bridge.LiveResourceCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task APackMissingBothFontsFailsAndNamesThem() {
        using CatalogTestHarness harness = new(userPacks: [BuildDefaultsPack(includeFont: false)]);

        AssetLoadException thrown = await Assert.ThrowsAsync<AssetLoadException>(
            () => EngineDefaults.LoadAsync(harness.Catalog));

        Assert.Equal(DiagnosticCodes.ASSET_LOAD_FAILED, thrown.DiagnosticCode);
        Assert.Contains(FONT_PATH, thrown.Message, StringComparison.Ordinal);
        Assert.Contains(MONO_FONT_PATH, thrown.Message, StringComparison.Ordinal);
        Assert.IsType<AssetNotFoundException>(thrown.InnerException);
        AssetCatalogLoadTests.WaitUntil(
            () => harness.Worker.DestroyCalls.Count == 3,
            "The program and both textures must be released when the font load fails.");
        Assert.Equal(0, harness.Bridge.LiveResourceCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task APackMissingOnlyTheMonoFontFailsAndReleasesTheLoadedDefaultFont() {
        using CatalogTestHarness harness = new(userPacks: [BuildDefaultsPack(includeMonoFont: false)]);
        FontCapturingCatalog assets = new(harness.Catalog);

        AssetLoadException thrown = await Assert.ThrowsAsync<AssetLoadException>(
            () => EngineDefaults.LoadAsync(assets));

        Assert.Equal(DiagnosticCodes.ASSET_LOAD_FAILED, thrown.DiagnosticCode);
        Assert.Contains(MONO_FONT_PATH, thrown.Message, StringComparison.Ordinal);
        Assert.IsType<AssetNotFoundException>(thrown.InnerException);
        Assert.NotNull(assets.LoadedFont);
        Assert.True(
            assets.LoadedFont.IsDisposed,
            "The already loaded default font must be released when the mono font load fails.");
        AssetCatalogLoadTests.WaitUntil(
            () => harness.Worker.DestroyCalls.Count == 3,
            "The program and both textures must be released when the mono font load fails.");
        Assert.Equal(0, harness.Bridge.LiveResourceCount);
    }

    private static byte[] BuildDefaultsPack(bool includeFont = true, bool includeMonoFont = true) {
        List<(string Path, AssetEntryType Type, byte[] Payload)> entries = [
            ("shaders/mesh", AssetEntryType.Shader, AssetSerialization.Serialize(CatalogTestData.CreateShaderDescriptor())),
            ("shaders/basic-vert", AssetEntryType.Raw, CatalogTestData.CreateShaderVertexPayload()),
            ("shaders/basic-frag", AssetEntryType.Raw, CatalogTestData.CreateShaderFragmentPayload()),
            ("textures/white", AssetEntryType.Texture, AssetSerialization.Serialize(CreateTextureDescriptor("textures/white-ktx2"))),
            ("textures/white-ktx2", AssetEntryType.Raw, CatalogTestData.CreateTexturePayload()),
            ("textures/error", AssetEntryType.Texture, AssetSerialization.Serialize(CreateTextureDescriptor("textures/error-ktx2"))),
            ("textures/error-ktx2", AssetEntryType.Raw, CatalogTestData.CreateTexturePayload()),
        ];
        if (includeFont) {
            entries.AddRange(CatalogTestData.CreateFontEntries());
            if (includeMonoFont) {
                entries.AddRange(CatalogTestData.CreateMonoFontEntries());
            }
        }

        return CatalogTestData.BuildPack([.. entries]);
    }

    /// <summary>
    /// Captures the <see cref="Font"/> a load returns so a failure test can assert the defaults released
    /// the font they had already acquired.
    /// </summary>
    private sealed class FontCapturingCatalog : IAssetCatalog {
        private readonly IAssetCatalog _inner;

        internal FontCapturingCatalog(IAssetCatalog inner) {
            ArgumentNullException.ThrowIfNull(inner);
            _inner = inner;
        }

        internal Font? LoadedFont { get; private set; }

        public bool Exists(string path) => _inner.Exists(path);

        public async Task<T> Load<T>(string path, CancellationToken cancellationToken = default) {
            T value = await _inner.Load<T>(path, cancellationToken).ConfigureAwait(false);
            if (value is Font font) {
                LoadedFont = font;
            }

            return value;
        }

        public Task<byte[]> LoadData(string path, CancellationToken cancellationToken = default)
            => _inner.LoadData(path, cancellationToken);
    }

    private static TextureAssetDescriptor CreateTextureDescriptor(string payload) => new() {
        Payload = payload,
        Width = 1,
        Height = 1,
        MipLevels = 1,
        Format = TextureFormat.Rgba8Srgb,
        IsSrgb = true,
        Filter = TextureFilter.Linear,
        Wrap = TextureWrap.Clamp,
        MaxAnisotropy = 1f,
    };
}
