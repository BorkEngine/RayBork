using RayBork.Internal;

namespace RayBork.Tests.Assets.Catalog;

public sealed class AssetTypeRegistryTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DuplicateAssetTypeRegistrationThrows() {
        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(
            () => new AssetTypeRegistry([CreateRegistration(), CreateRegistration()]));

        Assert.Equal(
            $"The asset type '{typeof(CatalogTestSettings).FullName}' is registered more than once.",
            thrown.Message);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TryGetAndContainsReportTheRegisteredType() {
        LoaderAssetTypeRegistration<CatalogTestSettings> registration = CreateRegistration();
        AssetTypeRegistry registry = new([registration]);

        Assert.Equal(1, registry.Count);
        Assert.True(registry.Contains(typeof(CatalogTestSettings)));
        Assert.False(registry.Contains(typeof(string)));
        Assert.True(registry.TryGet(typeof(CatalogTestSettings), out AssetTypeRegistration? found));
        Assert.Same(registration, found);
        Assert.False(registry.TryGet(typeof(string), out _));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GetRejectsAnUnregisteredTypeWithTheCatalogMessage() {
        AssetTypeRegistry registry = new([]);

        NotSupportedException thrown = Assert.Throws<NotSupportedException>(
            () => registry.Get(typeof(CatalogTestSettings)));

        Assert.Contains(typeof(CatalogTestSettings).FullName!, thrown.Message, StringComparison.Ordinal);
        Assert.Contains("WithAssetType<T>()", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void LoaderRegistrationCarriesTheConfiguredFailureDescription() {
        MessagePackAssetLoader<CatalogTestSettings> loader = new();

        LoaderAssetTypeRegistration<CatalogTestSettings> defaulted = new(loader);
        Assert.Equal("could not be loaded", defaulted.FailureDescription);

        LoaderAssetTypeRegistration<CatalogTestSettings> customData = new(
            loader,
            CatalogTestData.CUSTOM_DATA_FAILURE_DESCRIPTION);
        Assert.Equal(CatalogTestData.CUSTOM_DATA_FAILURE_DESCRIPTION, customData.FailureDescription);

        Assert.Throws<ArgumentNullException>(
            () => new LoaderAssetTypeRegistration<CatalogTestSettings>(loader, null!));
        Assert.Throws<ArgumentException>(
            () => new LoaderAssetTypeRegistration<CatalogTestSettings>(loader, " "));
    }

    private static LoaderAssetTypeRegistration<CatalogTestSettings> CreateRegistration()
        => new(
            new MessagePackAssetLoader<CatalogTestSettings>(),
            CatalogTestData.CUSTOM_DATA_FAILURE_DESCRIPTION);
}
