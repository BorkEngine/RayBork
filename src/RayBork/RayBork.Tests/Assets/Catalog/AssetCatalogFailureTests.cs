using System.Buffers.Binary;
using RayBork.Internal;
using RayBork.Tests.Assets.Gpu;

namespace RayBork.Tests.Assets.Catalog;

public sealed class AssetCatalogFailureTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void StartRejectsATruncatedPackAndStartsNoLoaderThread() {
        using TempPackDirectory directory = new();
        string path = directory.GetPath("truncated.pck");
        File.WriteAllBytes(path, CatalogTestData.BuildStandardPack()[..16]);
        (GpuAssetBridge bridge, AssetCatalog catalog) = CreateUnstartedCatalog([path]);

        try {
            AssetPackException thrown = Assert.Throws<AssetPackException>(catalog.Start);

            Assert.Equal(DiagnosticCodes.ASSET_PACK_READ_FAILED, thrown.DiagnosticCode);
            Assert.Null(catalog.LoaderThread);
        } finally {
            catalog.Stop();
            bridge.Close(null);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void StartRejectsABadMagicAndStartsNoLoaderThread() {
        using TempPackDirectory directory = new();
        string path = directory.GetPath("magic.pck");
        byte[] pack = CatalogTestData.BuildStandardPack();
        pack[0] = (byte)'X';
        File.WriteAllBytes(path, pack);
        (GpuAssetBridge bridge, AssetCatalog catalog) = CreateUnstartedCatalog([path]);

        try {
            AssetPackException thrown = Assert.Throws<AssetPackException>(catalog.Start);

            Assert.Equal(DiagnosticCodes.ASSET_PACK_UNSUPPORTED_VERSION, thrown.DiagnosticCode);
            Assert.Null(catalog.LoaderThread);
        } finally {
            catalog.Stop();
            bridge.Close(null);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void StartRejectsABadIndexHashAndStartsNoLoaderThread() {
        using TempPackDirectory directory = new();
        string path = directory.GetPath("index-hash.pck");
        PackTestPack editor = new(CatalogTestData.BuildStandardPack());
        editor.CorruptIndexHash();
        File.WriteAllBytes(path, editor.Bytes);
        (GpuAssetBridge bridge, AssetCatalog catalog) = CreateUnstartedCatalog([path]);

        try {
            AssetPackException thrown = Assert.Throws<AssetPackException>(catalog.Start);

            Assert.Equal(DiagnosticCodes.ASSET_PACK_INDEX_HASH_MISMATCH, thrown.DiagnosticCode);
            Assert.Null(catalog.LoaderThread);
        } finally {
            catalog.Stop();
            bridge.Close(null);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void StartRejectsAMissingPackAndMountsNothing() {
        using TempPackDirectory directory = new();
        string missing = directory.GetPath("missing.pck");
        (GpuAssetBridge bridge, AssetCatalog catalog) = CreateUnstartedCatalog([missing]);

        try {
            AssetPackException thrown = Assert.Throws<AssetPackException>(catalog.Start);

            Assert.Equal(DiagnosticCodes.ASSET_PACK_READ_FAILED, thrown.DiagnosticCode);
            Assert.Null(catalog.LoaderThread);
            Assert.Throws<InvalidOperationException>(() => catalog.Exists("/meshes/quad"));
        } finally {
            catalog.Stop();
            bridge.Close(null);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task LoadWithoutAWorkerFailsAsAssetLoad() {
        using TempPackDirectory directory = new();
        string path = directory.GetPath("catalog.pck");
        File.WriteAllBytes(path, CatalogTestData.BuildStandardPack());
        GpuAssetBridge bridge = new();
        AssetCatalog catalog = new(
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
            bridge,
            ReadOnlyMemory<byte>.Empty,
            [path],
            []);
        catalog.Start();

        try {
            AssetLoadException thrown = await Assert.ThrowsAsync<AssetLoadException>(
                () => catalog.Load<Mesh>("/meshes/quad", TestContext.Current.CancellationToken));

            Assert.Equal(DiagnosticCodes.ASSET_LOAD_FAILED, thrown.DiagnosticCode);
        } finally {
            catalog.Stop();
            bridge.Close(null);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task MissingSiblingPayloadFailsAsAssetLoadWithoutUpload() {
        ShaderAssetDescriptor template = CatalogTestData.CreateShaderDescriptor();
        ShaderAssetDescriptor descriptor = new() {
            VertexPayload = "shaders/missing-vert",
            FragmentPayload = CatalogTestData.SHADER_FRAGMENT_PAYLOAD_PATH,
            VertexEntry = template.VertexEntry,
            FragmentEntry = template.FragmentEntry,
            Stages = template.Stages,
            DefaultRenderState = template.DefaultRenderState,
            Uniforms = template.Uniforms,
            Textures = template.Textures,
        };
        byte[] pack = CatalogTestData.BuildPack(
            (CatalogTestData.SHADER_PATH, AssetEntryType.Shader, AssetSerialization.Serialize(descriptor)),
            (CatalogTestData.SHADER_FRAGMENT_PAYLOAD_PATH, AssetEntryType.Raw, CatalogTestData.CreateShaderFragmentPayload()));
        using CatalogTestHarness harness = new(userPacks: [pack]);

        AssetLoadException thrown = await Assert.ThrowsAsync<AssetLoadException>(
            () => harness.Catalog.Load<Shader>("/shaders/basic", TestContext.Current.CancellationToken));

        Assert.Equal(DiagnosticCodes.ASSET_LOAD_FAILED, thrown.DiagnosticCode);
        Assert.Equal(0, harness.Worker.UploadCallCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AFailedMountDiscardsThePacksOpenedBeforeIt() {
        using TempPackDirectory directory = new();
        string validPath = directory.GetPath("valid.pck");
        File.WriteAllBytes(validPath, CatalogTestData.BuildStandardPack());
        string missingPath = directory.GetPath("missing.pck");
        (GpuAssetBridge bridge, AssetCatalog catalog) = CreateUnstartedCatalog([validPath, missingPath]);

        try {
            AssetPackException thrown = Assert.Throws<AssetPackException>(catalog.Start);

            Assert.Equal(DiagnosticCodes.ASSET_PACK_READ_FAILED, thrown.DiagnosticCode);
            Assert.Null(catalog.LoaderThread);
            Assert.Throws<InvalidOperationException>(() => catalog.Exists("/meshes/quad"));
        } finally {
            catalog.Stop();
            bridge.Close(null);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task InvalidTextureDescriptorFailsBeforeTheUpload() {
        TextureAssetDescriptor template = CatalogTestData.CreateTextureDescriptor();
        TextureAssetDescriptor descriptor = new() {
            Payload = CatalogTestData.TEXTURE_PAYLOAD_PATH,
            Width = template.Width,
            Height = template.Height,
            MipLevels = 9,
            Format = template.Format,
            IsSrgb = template.IsSrgb,
            Filter = template.Filter,
            Wrap = template.Wrap,
            MaxAnisotropy = template.MaxAnisotropy,
        };
        byte[] pack = CatalogTestData.BuildPack(
            (CatalogTestData.TEXTURE_PATH, AssetEntryType.Texture, AssetSerialization.Serialize(descriptor)),
            (CatalogTestData.TEXTURE_PAYLOAD_PATH, AssetEntryType.Raw, CatalogTestData.CreateTexturePayload()));
        using CatalogTestHarness harness = new(userPacks: [pack]);

        AssetLoadException thrown = await Assert.ThrowsAsync<AssetLoadException>(
            () => harness.Catalog.Load<Texture>("/textures/albedo", TestContext.Current.CancellationToken));

        Assert.Equal(DiagnosticCodes.ASSET_LOAD_FAILED, thrown.DiagnosticCode);
        Assert.Equal(0, harness.Worker.UploadCallCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task LoadDataSurfacesAPayloadHashMismatch() {
        PackTestPack editor = new(CatalogTestData.BuildStandardPack());
        editor.FlipPayloadByte("data/settings", 0);
        using CatalogTestHarness harness = new(userPacks: [editor.Bytes]);

        AssetLoadException thrown = await Assert.ThrowsAsync<AssetLoadException>(
            () => harness.Catalog.LoadData("/data/settings", TestContext.Current.CancellationToken));

        Assert.Equal(DiagnosticCodes.ASSET_PACK_PAYLOAD_HASH_MISMATCH, thrown.DiagnosticCode);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task LoadDataForAMissingPathThrowsAssetNotFound() {
        using CatalogTestHarness harness = new(userPacks: [CatalogTestData.BuildStandardPack()]);

        AssetNotFoundException thrown = await Assert.ThrowsAsync<AssetNotFoundException>(
            () => harness.Catalog.LoadData("/data/absent", TestContext.Current.CancellationToken));

        Assert.Equal(DiagnosticCodes.ASSET_NOT_FOUND, thrown.DiagnosticCode);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task MeshIndexOutsideTheVertexListFailsAsAssetLoad() {
        byte[] payload = CatalogTestData.CreateMeshPayload();
        BinaryPrimitives.WriteUInt32LittleEndian(
            payload.AsSpan((int)CatalogTestData.MESH_VERTEX_COUNT * MeshAssetDescriptor.VERTEX_STRIDE),
            CatalogTestData.MESH_VERTEX_COUNT);
        byte[] pack = CatalogTestData.BuildPack(
            (CatalogTestData.MESH_PATH, AssetEntryType.Mesh, AssetSerialization.Serialize(CatalogTestData.CreateMeshDescriptor())),
            (CatalogTestData.MESH_PAYLOAD_PATH, AssetEntryType.Raw, payload));
        using CatalogTestHarness harness = new(userPacks: [pack]);

        AssetLoadException thrown = await Assert.ThrowsAsync<AssetLoadException>(
            () => harness.Catalog.Load<Mesh>("/meshes/quad", TestContext.Current.CancellationToken));

        Assert.Equal(DiagnosticCodes.ASSET_LOAD_FAILED, thrown.DiagnosticCode);
        Assert.Contains("outside", thrown.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.Worker.UploadCallCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task AFailingValueLoaderReportsTheRegisteredFailureDescription() {
        using CatalogTestHarness harness = new(
            builtInPack: null,
            userPacks: [CatalogTestData.BuildStandardPack()],
            assetTypes: [new LoaderAssetTypeRegistration<CatalogTestSettings>(new ThrowingSettingsLoader())]);

        AssetLoadException thrown = await Assert.ThrowsAsync<AssetLoadException>(
            () => harness.Catalog.Load<CatalogTestSettings>(
                "/data/settings",
                TestContext.Current.CancellationToken));

        Assert.Equal(DiagnosticCodes.ASSET_LOAD_FAILED, thrown.DiagnosticCode);
        Assert.Contains("could not be loaded", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("the loader failed on purpose", thrown.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            CatalogTestData.CUSTOM_DATA_FAILURE_DESCRIPTION,
            thrown.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task CustomDataDeserializationFailureReportsTheCustomDataDescription() {
        byte[] pack = CatalogTestData.BuildPack(
            (CatalogTestData.DATA_PATH, AssetEntryType.Data, [0xFF, 0xFF, 0xFF, 0xFF]));
        using CatalogTestHarness harness = new(
            builtInPack: null,
            userPacks: [pack],
            assetTypes: [CatalogTestData.SettingsRegistration]);

        AssetLoadException thrown = await Assert.ThrowsAsync<AssetLoadException>(
            () => harness.Catalog.Load<CatalogTestSettings>(
                "/data/settings",
                TestContext.Current.CancellationToken));

        Assert.Equal(DiagnosticCodes.ASSET_LOAD_FAILED, thrown.DiagnosticCode);
        Assert.Contains(
            CatalogTestData.CUSTOM_DATA_FAILURE_DESCRIPTION,
            thrown.Message,
            StringComparison.Ordinal);
    }

    private static (GpuAssetBridge Bridge, AssetCatalog Catalog) CreateUnstartedCatalog(string[] paths) {
        GpuAssetBridge bridge = new();
        bridge.AttachWorker(new FakeAssetGpuWorker());
        AssetCatalog catalog = new(
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
            bridge,
            ReadOnlyMemory<byte>.Empty,
            paths,
            []);
        return (bridge, catalog);
    }

    private sealed class ThrowingSettingsLoader : IAssetLoader<CatalogTestSettings> {
        public AssetEntryType EntryType => AssetEntryType.Data;

        public Task<CatalogTestSettings> LoadAsync(AssetLoadContext context)
            => throw new InvalidOperationException("the loader failed on purpose");
    }
}
