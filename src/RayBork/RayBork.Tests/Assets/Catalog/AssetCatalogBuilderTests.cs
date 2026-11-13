using Microsoft.Extensions.Logging.Abstractions;
using RayBork.Internal;

namespace RayBork.Tests.Assets.Catalog;

public sealed class AssetCatalogBuilderTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WithAssetsPackRejectsNullAndStoresPathsInCallOrder() {
        RayBorkAppBuilder builder = CreateBuilder();

        Assert.Throws<ArgumentNullException>(() => builder.WithAssetsPack(null!));

        builder
            .WithAssetsPack("first.pck")
            .WithAssetsPacks("second.pck", "third.pck")
            .WithAssetsPack("fourth.pck");

        using RayBorkApp app = Build(builder);
        Assert.Equal(
            ["first.pck", "second.pck", "third.pck", "fourth.pck"],
            app.Configuration.AssetPackPaths);
        Assert.Empty(app.Configuration.AssetTypes);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WithAssetsPackRejectsEmptyOrWhitespace(string path) {
        RayBorkAppBuilder builder = CreateBuilder();

        Assert.Throws<ArgumentException>(() => builder.WithAssetsPack(path));
        Assert.Throws<ArgumentException>(() => builder.WithAssetsPacks(path));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WithAssetsPacksRejectsNullArrayAndNullElement() {
        RayBorkAppBuilder builder = CreateBuilder();

        Assert.Throws<ArgumentNullException>(() => builder.WithAssetsPacks(null!));
        Assert.Throws<ArgumentNullException>(() => builder.WithAssetsPacks("pack.pck", null!));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WithAssetTypeIsIdempotentAndSnapshotted() {
        RayBorkAppBuilder builder = CreateBuilder()
            .WithAssetType<CatalogTestSettings>()
            .WithAssetType<CatalogTestSettings>();

        using RayBorkApp app = Build(builder);

        AssetTypeRegistration registration = Assert.Single(app.Configuration.AssetTypes);
        Assert.Equal(typeof(CatalogTestSettings), registration.AssetType);
        Assert.Equal(AssetEntryType.Data, registration.EntryType);
        LoaderAssetTypeRegistration<CatalogTestSettings> loaderRegistration =
            Assert.IsType<LoaderAssetTypeRegistration<CatalogTestSettings>>(registration);
        Assert.Equal(
            CatalogTestData.CUSTOM_DATA_FAILURE_DESCRIPTION,
            loaderRegistration.FailureDescription);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WithAssetLoaderIsSnapshottedForAPlainType() {
        PlainAssetLoader loader = new();

        RayBorkAppBuilder builder = CreateBuilder().WithAssetLoader(loader);

        using RayBorkApp app = Build(builder);
        AssetTypeRegistration registration = Assert.Single(app.Configuration.AssetTypes);
        Assert.Equal(typeof(PlainAsset), registration.AssetType);
        Assert.Equal(AssetEntryType.Data, registration.EntryType);
        LoaderAssetTypeRegistration<PlainAsset> loaderRegistration =
            Assert.IsType<LoaderAssetTypeRegistration<PlainAsset>>(registration);
        Assert.Same(loader, loaderRegistration.Loader);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WithAssetLoaderRejectsANullLoader() {
        RayBorkAppBuilder builder = CreateBuilder();

        Assert.Throws<ArgumentNullException>(() => builder.WithAssetLoader<PlainAsset>(null!));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WithAssetLoaderRejectsTheSameTypeTwice() {
        RayBorkAppBuilder builder = CreateBuilder().WithAssetLoader(new PlainAssetLoader());

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(
            () => builder.WithAssetLoader(new PlainAssetLoader()));

        Assert.Equal(
            $"The asset type '{typeof(PlainAsset).FullName}' is registered more than once.",
            thrown.Message);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WithAssetTypeThenWithAssetLoaderRejectsTheConflict() {
        RayBorkAppBuilder builder = CreateBuilder().WithAssetType<CatalogTestSettings>();

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(
            () => builder.WithAssetLoader(new CategoriesLoader()));

        Assert.Equal(
            $"The asset type '{typeof(CatalogTestSettings).FullName}' is registered more than once.",
            thrown.Message);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WithAssetLoaderThenWithAssetTypeRejectsTheConflict() {
        RayBorkAppBuilder builder = CreateBuilder()
            .WithAssetLoader(new CategoriesLoader());

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(
            builder.WithAssetType<CatalogTestSettings>);

        Assert.Equal(
            $"The asset type '{typeof(CatalogTestSettings).FullName}' is registered more than once.",
            thrown.Message);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WithAssetLoaderForABuiltInEngineTypeFailsFromBuild() {
        RayBorkAppBuilder builder = CreateBuilder()
            .WithAssetLoader<Mesh>(new MeshLoader());

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => Build(builder));

        Assert.Equal(
            $"The asset type '{typeof(Mesh).FullName}' is registered more than once.",
            thrown.Message);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void BuildDoesNotOpenPackFiles() {
        string directory = Path.Combine(Path.GetTempPath(), "raybork-catalog-missing-" + Guid.NewGuid().ToString("N"));
        string missing = Path.Combine(directory, "missing.pck");
        RayBorkAppBuilder builder = CreateBuilder().WithAssetsPack(missing);

        using RayBorkApp app = Build(builder);

        Assert.Equal([missing], app.Configuration.AssetPackPaths);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AssetMethodsRejectCallsAfterBuild() {
        RayBorkAppBuilder builder = CreateBuilder();
        using RayBorkApp app = Build(builder);

        Assert.Throws<InvalidOperationException>(() => builder.WithAssetsPack("later.pck"));
        Assert.Throws<InvalidOperationException>(() => builder.WithAssetsPacks("later.pck"));
        Assert.Throws<InvalidOperationException>(() => builder.WithAssetType<CatalogTestSettings>());
        Assert.Throws<InvalidOperationException>(
            () => builder.WithAssetLoader(new PlainAssetLoader()));
    }

    private static RayBorkAppBuilder CreateBuilder()
        => new RayBorkAppBuilder()
            .WithGame(new ScriptedGame())
            .WithLoggerFactory(NullLoggerFactory.Instance);

    private static RayBorkApp Build(RayBorkAppBuilder builder) {
        FakeEnginePlatform platform = new();
        FakeRendererBackend renderer = new();
        return builder.Build(_ => platform, () => renderer, TimeProvider.System);
    }

    private sealed class PlainAsset {
        internal PlainAsset(string path) => Path = path;

        internal string Path { get; }
    }

    private sealed class PlainAssetLoader : IAssetLoader<PlainAsset> {
        public AssetEntryType EntryType => AssetEntryType.Data;

        public Task<PlainAsset> LoadAsync(AssetLoadContext context)
            => Task.FromResult(new PlainAsset(context.Path));
    }

    private sealed class CategoriesLoader : IAssetLoader<CatalogTestSettings> {
        public AssetEntryType EntryType => AssetEntryType.Data;

        public Task<CatalogTestSettings> LoadAsync(AssetLoadContext context)
            => Task.FromResult(CatalogTestData.CreateSettings());
    }

    private sealed class MeshLoader : IAssetLoader<Mesh> {
        public AssetEntryType EntryType => AssetEntryType.Mesh;

        public Task<Mesh> LoadAsync(AssetLoadContext context)
            => throw new InvalidOperationException("a built-in engine type must fail before any load runs");
    }
}
