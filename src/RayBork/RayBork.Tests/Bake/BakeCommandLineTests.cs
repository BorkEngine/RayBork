using RayBork.Bake;
using RayBork.Tests.Bake.TestInfrastructure;

namespace RayBork.Tests.Bake;

public sealed class BakeCommandLineTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void HelpAndVersionSucceed() {
        using var workspace = new TempBakeWorkspace();
        BakeApplication application = workspace.Builder().Build();

        BakeRunResult help = workspace.Run(application, "--help");
        BakeRunResult version = workspace.Run(application, "--version");

        Assert.Equal(0, help.ExitCode);
        Assert.Contains("usage: test-bake", help.Output);
        Assert.Contains("bake", help.Output);
        Assert.Equal(0, version.ExitCode);
        Assert.StartsWith("test-bake ", version.Output);
        Assert.Empty(help.Error);
        Assert.Empty(version.Error);
    }

    [Theory]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    [InlineData()]
    [InlineData("unknown")]
    [InlineData("bake", "--bogus")]
    [InlineData("bake", "--verbose", "--quiet")]
    [InlineData("bake", "--mode", "fast")]
    public void UsageErrorsReturnTwo(params string[] args) {
        using var workspace = new TempBakeWorkspace();
        BakeApplication application = workspace.Builder().Build();

        BakeRunResult result = workspace.RunRaw(application, args);

        Assert.Equal(2, result.ExitCode);
        Assert.NotEmpty(result.Error);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SuccessSummaryGoesToStdOutAndTheExitCodeIsZero() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("unit.count", "source");
        BakeApplication application = workspace.Builder()
            .AddProcessor(new CountingProcessor())
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("baked 'assets.pck'", result.Output);
        Assert.Contains("debug mode", result.Output);
        Assert.Empty(result.Error);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ProjectRootOverrideTakesPrecedenceOverTheBuilder() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("unit.count", "source");
        BakeApplication application = new BakeApplicationBuilder()
            .WithProjectRoot(workspace.Root)
            .WithPack("assets.pck", "Assets")
            .AddProcessor(new CountingProcessor())
            .Build();

        BakeRunResult result = workspace.Run(
            application,
            "bake",
            "--project-root",
            workspace.ProjectRoot,
            "--output-root",
            workspace.OutputRoot);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        Assert.True(File.Exists(workspace.GetPackPath()));
        Assert.True(File.Exists(workspace.ManifestPath));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SourceDiagnosticsAreCanonicalOnStdErrAndReturnOne() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("a.opt", "source");
        workspace.WriteAsset("a.toml", """
            [optional]
            value = 1
            bogus = 2
            """);
        BakeApplication application = workspace.Builder()
            .AddProcessor(new OptionalSidecarProcessor())
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(1, result.ExitCode);
        Assert.Matches(
            @"Assets/a\.toml\(3,1\): error RB4002: .+",
            result.Error);
        Assert.Contains("no pack was written", result.Output);
        Assert.Empty(Directory.GetFiles(workspace.OutputRoot));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ConfigurationErrorsReturnTwoWithADiagnostic() {
        using var workspace = new TempBakeWorkspace();
        BakeApplication application = new BakeApplicationBuilder()
            .WithProjectRoot(workspace.ProjectRoot)
            .WithPack("assets.pck", "Missing")
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("RB4001", result.Error);
        Assert.Contains("configuration is invalid", result.Output);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CleanDeletesPacksAndTheIncrementalManifest() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("unit.count", "source");
        BakeApplication application = workspace.Builder()
            .AddProcessor(new CountingProcessor())
            .Build();
        Assert.Equal(0, workspace.Run(application).ExitCode);
        Assert.True(File.Exists(workspace.GetPackPath()));
        Assert.True(File.Exists(workspace.ManifestPath));

        BakeRunResult clean = workspace.Run(
            application,
            "clean",
            "--output-root",
            workspace.OutputRoot);

        Assert.Equal(0, clean.ExitCode);
        Assert.Contains("cleaned 1 pack(s)", clean.Output);
        Assert.False(File.Exists(workspace.GetPackPath()));
        Assert.False(Directory.Exists(Path.GetDirectoryName(workspace.ManifestPath)!));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void InspectListsConfiguredPacksAndAnExplicitPack() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("unit.count", "source");
        BakeApplication application = workspace.Builder()
            .AddProcessor(new CountingProcessor())
            .Build();
        Assert.Equal(0, workspace.Run(application).ExitCode);

        BakeRunResult all = workspace.Run(
            application,
            "inspect",
            "--output-root",
            workspace.OutputRoot);
        BakeRunResult explicitPack = workspace.Run(
            application,
            "inspect",
            "--output-root",
            workspace.OutputRoot,
            "--pack",
            "assets.pck");

        Assert.Equal(0, all.ExitCode);
        Assert.Contains("inspected 1 pack(s)", all.Output);
        Assert.Contains("unit", all.Output);
        Assert.Contains("-> Data", all.Output);
        Assert.Equal(0, explicitPack.ExitCode);
        Assert.Contains("unit -> Data", explicitPack.Output);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void InspectExplicitPackDoesNotRequireAConfiguredPack() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("unit.count", "source");
        BakeApplication producing = workspace.Builder()
            .AddProcessor(new CountingProcessor())
            .Build();
        Assert.Equal(0, workspace.Run(producing).ExitCode);

        BakeApplication empty = new BakeApplicationBuilder().Build();
        BakeRunResult result = workspace.Run(
            empty,
            "inspect",
            "--output-root",
            workspace.OutputRoot,
            "--pack",
            "assets.pck");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("unit -> Data", result.Output);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void InspectMissingPackReturnsOne() {
        using var workspace = new TempBakeWorkspace();
        BakeApplication application = workspace.Builder().Build();

        BakeRunResult result = workspace.Run(
            application,
            "inspect",
            "--output-root",
            workspace.OutputRoot,
            "--pack",
            "missing.pck");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("RB1001", result.Error);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UnexpectedFailureReturnsThree() {
        using var workspace = new TempBakeWorkspace();
        BakeApplication application = workspace.Builder()
            .AddProcessor(new ThrowingExtensionsProcessor())
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("internal error", result.Error);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void QuietSuppressesTheSummaryButNotTheDiagnostics() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("unit.err", "source");
        BakeApplication application = workspace.Builder()
            .AddProcessor(new ErrorProcessor())
            .Build();

        BakeRunResult result = workspace.Run(
            application,
            "bake",
            "--output-root",
            workspace.OutputRoot,
            "--quiet");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("RB4006", result.Error);
    }
}
