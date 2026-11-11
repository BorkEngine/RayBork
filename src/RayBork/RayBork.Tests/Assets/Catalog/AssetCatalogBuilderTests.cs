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
}
