using RayBork.Internal;

namespace RayBork.Tests.Assets.Catalog;

/// <summary>
/// Tests the app-owned <see cref="EngineDefaults"/> preload: the built-in mesh shader program, the white
/// texture, and the error texture are loaded once, handed to the game, and released during shutdown.
/// </summary>
public sealed class EngineDefaultsTests {
    private const string MESH_SHADER_PATH = "/shaders/mesh";
    private const string WHITE_PATH = "/textures/white";
    private const string ERROR_PATH = "/textures/error";

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task LoadAsyncPreloadsTheProgramAndBothTextures() {
        using CatalogTestHarness harness = new(userPacks: [BuildDefaultsPack()]);

        EngineDefaults defaults = await EngineDefaults.LoadAsync(harness.Catalog);

        try {
            Assert.False(defaults.IsDisposed);
            Assert.Equal(WHITE_PATH, defaults.White.Path);
            Assert.Equal(ERROR_PATH, defaults.Error.Path);
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

    private static byte[] BuildDefaultsPack() => CatalogTestData.BuildPack(
        ("shaders/mesh", AssetEntryType.Shader, AssetSerialization.Serialize(CatalogTestData.CreateShaderDescriptor())),
        ("shaders/basic-vert", AssetEntryType.Raw, CatalogTestData.CreateShaderVertexPayload()),
        ("shaders/basic-frag", AssetEntryType.Raw, CatalogTestData.CreateShaderFragmentPayload()),
        ("textures/white", AssetEntryType.Texture, AssetSerialization.Serialize(CreateTextureDescriptor("textures/white-ktx2"))),
        ("textures/white-ktx2", AssetEntryType.Raw, CatalogTestData.CreateTexturePayload()),
        ("textures/error", AssetEntryType.Texture, AssetSerialization.Serialize(CreateTextureDescriptor("textures/error-ktx2"))),
        ("textures/error-ktx2", AssetEntryType.Raw, CatalogTestData.CreateTexturePayload()));

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
