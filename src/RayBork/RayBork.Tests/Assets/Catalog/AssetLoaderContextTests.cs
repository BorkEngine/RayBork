using RayBork.Internal;

namespace RayBork.Tests.Assets.Catalog;

/// <summary>
/// Tests the nested-load surface of <see cref="AssetLoadContext"/> through the real catalog.
/// </summary>
public sealed class AssetLoaderContextTests {
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ANestedLoadThroughTheContextCompletesAndReleasesTheLoadedAsset() {
        NestedLoadLoader loader = new();
        using CatalogTestHarness harness = new(
            builtInPack: null,
            userPacks: [CatalogTestData.BuildStandardPack()],
            assetTypes: [new LoaderAssetTypeRegistration<CatalogTestSettings>(loader)]);

        CatalogTestSettings settings = await harness.Catalog
            .Load<CatalogTestSettings>("/data/settings", TestContext.Current.CancellationToken)
            .WaitAsync(_timeout, TestContext.Current.CancellationToken);

        Assert.Equal("catalog", settings.Name);
        Assert.Equal(7, settings.Difficulty);
        Assert.True(loader.TextureExists);
        Assert.Equal(4, loader.TextureWidth);
        Assert.Equal(CatalogTestData.CreateDataPayload(), loader.Data);

        AssetCatalogLoadTests.WaitUntil(
            () => harness.Worker.DestroyCalls.Count == 1,
            "Disposing the nested texture releases the load's only owner reference.");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ALoaderThatRequestsItsOwnTypeAndPathFailsInsteadOfRecursing() {
        using CatalogTestHarness harness = new(
            builtInPack: null,
            userPacks: [CatalogTestData.BuildStandardPack()],
            assetTypes: [new LoaderAssetTypeRegistration<CatalogTestSettings>(new SelfLoadLoader())]);

        AssetLoadException thrown = await Assert.ThrowsAsync<AssetLoadException>(
            () => harness.Catalog.Load<CatalogTestSettings>(
                "/data/settings",
                TestContext.Current.CancellationToken));

        Assert.Equal(DiagnosticCodes.ASSET_LOAD_FAILED, thrown.DiagnosticCode);
        Assert.Contains("currently loading", thrown.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.Worker.UploadCallCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task AValueLoaderRunsOnTheCatalogLoaderThread() {
        ThreadNameLoader loader = new();
        using CatalogTestHarness harness = new(
            builtInPack: null,
            userPacks: [CatalogTestData.BuildStandardPack()],
            assetTypes: [new LoaderAssetTypeRegistration<CatalogTestSettings>(loader)]);

        CatalogTestSettings settings = await harness.Catalog.Load<CatalogTestSettings>(
            "/data/settings",
            TestContext.Current.CancellationToken);

        Assert.Equal("catalog", settings.Name);
        Assert.Equal(AssetCatalog.LOADER_THREAD_NAME, loader.ThreadName);
    }

    private sealed class NestedLoadLoader : IAssetLoader<CatalogTestSettings> {
        internal bool TextureExists { get; private set; }

        internal int TextureWidth { get; private set; }

        internal byte[]? Data { get; private set; }

        public AssetEntryType EntryType => AssetEntryType.Data;

        public async Task<CatalogTestSettings> LoadAsync(AssetLoadContext context) {
            TextureExists = context.Exists("/textures/albedo");
            Data = await context.LoadDataAsync(context.Path).ConfigureAwait(false);
            Texture texture = await context.LoadAsync<Texture>("/textures/albedo").ConfigureAwait(false);
            TextureWidth = texture.Width;
            texture.Dispose();
            byte[] payload = await context.LoadPayloadAsync().ConfigureAwait(false);
            return AssetSerialization.Deserialize<CatalogTestSettings>(payload);
        }
    }

    private sealed class SelfLoadLoader : IAssetLoader<CatalogTestSettings> {
        public AssetEntryType EntryType => AssetEntryType.Data;

        public Task<CatalogTestSettings> LoadAsync(AssetLoadContext context)
            => context.LoadAsync<CatalogTestSettings>(context.Path);
    }

    private sealed class ThreadNameLoader : IAssetLoader<CatalogTestSettings> {
        internal string? ThreadName { get; private set; }

        public AssetEntryType EntryType => AssetEntryType.Data;

        public Task<CatalogTestSettings> LoadAsync(AssetLoadContext context) {
            ThreadName = Thread.CurrentThread.Name;
            return Task.FromResult(CatalogTestData.CreateSettings());
        }
    }
}
