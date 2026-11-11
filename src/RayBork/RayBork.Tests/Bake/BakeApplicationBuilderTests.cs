using RayBork.Bake;
using RayBork.Tests.Bake.TestInfrastructure;

namespace RayBork.Tests.Bake;

public sealed class BakeApplicationBuilderTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MissingProjectRootIsAConfigurationError() {
        using var workspace = new TempBakeWorkspace();
        string missing = Path.Combine(workspace.Root, "missing");
        BakeApplication application = new BakeApplicationBuilder()
            .WithProjectRoot(missing)
            .WithPack("assets.pck", "Assets")
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("RB4001", result.Error);
        Assert.Contains("does not exist", result.Error);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AssetRootMustStayInsideTheProjectRoot() {
        using var workspace = new TempBakeWorkspace();
        BakeApplication application = workspace.Builder()
            .WithPack("outside.pck", "../Outside")
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("RB4001", result.Error);
        Assert.Contains("'..' segment", result.Error);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PackPathMustStayRelative() {
        using var workspace = new TempBakeWorkspace();
        BakeApplication application = new BakeApplicationBuilder()
            .WithProjectRoot(workspace.ProjectRoot)
            .WithPack("../assets.pck", "Assets")
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("RB4001", result.Error);
        Assert.Contains("pack path", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DuplicateAssetRegistrationsAreConfigurationErrors() {
        using var workspace = new TempBakeWorkspace();
        BakeApplication application = workspace.Builder()
            .RegisterAsset<GameSettings>()
            .RegisterAsset<GameSettings>()
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("registered more than once", result.Error);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DuplicatePackPathsAreConfigurationErrors() {
        using var workspace = new TempBakeWorkspace();
        BakeApplication application = workspace.Builder().Build();

        BakeRunResult result = workspace.Run(application);

        // A second identical pack output cannot be written: exercise the duplicate directly.
        BakeApplication duplicate = new BakeApplicationBuilder()
            .WithProjectRoot(workspace.ProjectRoot)
            .WithPack("assets.pck", "Assets")
            .WithPack("assets.pck", "Assets")
            .Build();
        BakeRunResult duplicateResult = workspace.Run(duplicate);

        Assert.Equal(2, duplicateResult.ExitCode);
        Assert.Contains("configured more than once", duplicateResult.Error);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void NoPackIsAConfigurationError() {
        using var workspace = new TempBakeWorkspace();
        BakeApplication application = new BakeApplicationBuilder()
            .WithProjectRoot(workspace.ProjectRoot)
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("declares no asset pack", result.Error);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UseDefaultAssetsIsIdempotent() {
        using var workspace = new TempBakeWorkspace();
        BakeApplication application = workspace.Builder()
            .UseDefaultAssets()
            .UseDefaultAssets()
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(workspace.GetPackPath()));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UnknownTextureDefaultsAreRejectedImmediately() {
        var builder = new BakeApplicationBuilder();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => builder.WithTextureDefaults((TextureFilter)999, TextureWrap.Clamp));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => builder.WithTextureDefaults(TextureFilter.Linear, (TextureWrap)999));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DuplicateProcessorNamesAreConfigurationErrors() {
        using var workspace = new TempBakeWorkspace();
        BakeApplication application = workspace.Builder()
            .AddProcessor(new CountingProcessor("same", "one"))
            .AddProcessor(new CountingProcessor("same", "two"))
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("registered more than once", result.Error);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void NullProcessorIsRejectedImmediately() {
        var builder = new BakeApplicationBuilder();

        Assert.Throws<ArgumentNullException>(() => builder.AddProcessor(null!));
        Assert.Throws<ArgumentNullException>(() => builder.RegisterAsset<GameSettings>((IAssetValidator<GameSettings>)null!));
    }
}
