using RayBork.Bake;
using RayBork.Bake.Incremental;
using RayBork.Internal;
using RayBork.Tests.Bake.TestInfrastructure;

namespace RayBork.Tests.Bake;

public sealed class BakeProcessorPipelineTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ShapedAndRawOutputsReachThePackWithTheirAlignment() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("units/archer.count", "source");
        var processor = new CountingProcessor();
        BakeApplication application = workspace.Builder()
            .AddProcessor(processor)
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, processor.ProcessCount);
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        Assert.True(archive.TryGetEntry("units/archer", out PackEntry? asset));
        Assert.Equal(AssetEntryType.Data, asset.Type);
        TestPayload payload = AssetSerialization.Deserialize<TestPayload>(archive.ReadPayload(asset));
        Assert.Equal("archer.count", payload.Name);

        Assert.True(archive.TryGetEntry("units/archer-raw", out PackEntry? raw));
        Assert.Equal(AssetEntryType.Raw, raw.Type);
        Assert.Equal(64u, raw.Alignment);
        Assert.Equal([1, 2, 3, 1], archive.ReadPayload(raw));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DescriptorTypesMapToTheirEngineEntryType() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("textures/castle.descriptor", "source");
        BakeApplication application = workspace.Builder()
            .AddProcessor(new DescriptorProcessor())
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(0, result.ExitCode);
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        Assert.True(archive.TryGetEntry("textures/castle", out PackEntry? texture));
        Assert.Equal(AssetEntryType.Texture, texture.Type);
        Assert.True(archive.TryGetEntry("textures/castle-ktx2", out PackEntry? payload));
        Assert.Equal(AssetEntryType.Raw, payload.Type);
        Assert.Equal(16u, payload.Alignment);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ProcessorErrorDiscardsEveryOutputForTheSource() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("a.err", "source");
        BakeApplication application = workspace.Builder()
            .AddProcessor(new ErrorProcessor())
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("RB4006", result.Error);
        Assert.Contains("deliberate source error", result.Error);
        Assert.False(File.Exists(workspace.GetPackPath()));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ProcessorExceptionIsARB4006Diagnostic() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("a.boom", "source");
        BakeApplication application = workspace.Builder()
            .AddProcessor(new ThrowProcessor())
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("RB4006", result.Error);
        Assert.Contains("deliberate processor failure", result.Error);
        Assert.False(File.Exists(workspace.GetPackPath()));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void InvalidOutputPathsAlignmentsAndDuplicatesAreRejected() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("a.badpath", "source");
        workspace.WriteAsset("b.badalign", "source");
        workspace.WriteAsset("c.dupout", "source");
        BakeApplication application = workspace.Builder()
            .AddProcessor(new InvalidOutputProcessor())
            .AddProcessor(new BadAlignmentProcessor())
            .AddProcessor(new DuplicateOutputProcessor())
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("not canonical", result.Error);
        Assert.Contains("alignment 32 is invalid", result.Error);
        Assert.Contains("produced more than once", result.Error);
        Assert.False(File.Exists(workspace.GetPackPath()));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AddInputIsRecordedInTheIncrementalManifest() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("data/a.toml", """
            [input]
            file = "extra.txt"
            """);
        workspace.WriteAsset("data/extra.txt", "dependency");
        BakeApplication application = workspace.Builder()
            .AddProcessor(new InputProcessor())
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        BakeManifest? manifest = BakeManifestStore.TryRead(workspace.ManifestPath);
        Assert.NotNull(manifest);
        BakeManifestSource source = Assert.Single(manifest.Packs[0].Sources);
        Assert.Contains(source.Inputs, input => input.Path == "Assets/data/a.toml");
        Assert.Contains(source.Inputs, input => input.Path == "Assets/data/extra.txt");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MissingAdditionalInputFailsTheSource() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("a.toml", """
            [missing-input]
            file = "missing.txt"
            """);
        BakeApplication application = workspace.Builder()
            .AddProcessor(new MissingInputProcessor())
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("RB4006", result.Error);
        Assert.Contains("does not exist", result.Error);
        Assert.False(File.Exists(workspace.GetPackPath()));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WarningDoesNotFailUnlessWarningsAreErrors() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("a.warn", "source");
        BakeApplication application = workspace.Builder()
            .AddProcessor(new WarningProcessor())
            .Build();

        BakeRunResult normal = workspace.Run(application);
        Assert.Equal(0, normal.ExitCode);
        Assert.Contains("warning RB4006", normal.Error);
        Assert.True(File.Exists(workspace.GetPackPath()));

        File.Delete(workspace.GetPackPath());
        BakeRunResult strict = workspace.Run(application, "bake", "--output-root", workspace.OutputRoot, "--warnings-as-errors");
        Assert.Equal(1, strict.ExitCode);
        Assert.False(File.Exists(workspace.GetPackPath()));
    }
}
