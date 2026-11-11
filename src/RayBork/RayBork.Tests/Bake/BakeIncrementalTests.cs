using RayBork.Bake;
using RayBork.Bake.Incremental;
using RayBork.Internal;
using RayBork.Tests.Bake.TestInfrastructure;

namespace RayBork.Tests.Bake;

public sealed class BakeIncrementalTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UnchangedSourceIsReportedUpToDateAndNotReprocessed() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("unit.count", "source");
        var processor = new CountingProcessor();
        BakeApplication application = workspace.Builder()
            .AddProcessor(processor)
            .Build();

        BakeRunResult first = workspace.Run(application);
        BakeRunResult second = workspace.Run(application);

        Assert.True(first.ExitCode == 0, first.Error + first.Output);
        Assert.True(second.ExitCode == 0, second.Error + second.Output);
        Assert.Equal(1, processor.ProcessCount);
        Assert.Contains("up to date", second.Output);
        Assert.True(File.Exists(workspace.GetPackPath()));
        Assert.True(File.Exists(workspace.ManifestPath));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DeletingOneSourceReusesTheUnchangedSource() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("a.count", "first");
        workspace.WriteAsset("b.count", "second");
        var processor = new CountingProcessor();
        BakeApplication application = workspace.Builder()
            .AddProcessor(processor)
            .Build();
        Assert.Equal(0, workspace.Run(application).ExitCode);
        Assert.Equal(2, processor.ProcessCount);

        File.Delete(Path.Combine(workspace.AssetRoot, "a.count"));
        BakeRunResult deleted = workspace.Run(application);

        Assert.Equal(0, deleted.ExitCode);
        Assert.Equal(2, processor.ProcessCount);
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        Assert.False(archive.TryGetEntry("a", out _));
        Assert.True(archive.TryGetEntry("b", out _));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ChangedSourceIsReprocessedAndDeletedSourceDisappears() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("unit.count", "first");
        var processor = new CountingProcessor();
        BakeApplication application = workspace.Builder()
            .AddProcessor(processor)
            .Build();
        Assert.Equal(0, workspace.Run(application).ExitCode);

        workspace.WriteAsset("unit.count", "second");
        BakeRunResult changed = workspace.Run(application);

        Assert.Equal(0, changed.ExitCode);
        Assert.Equal(2, processor.ProcessCount);
        Assert.Equal(2, ReadPayloadValue(workspace, "unit"));

        File.Delete(Path.Combine(workspace.AssetRoot, "unit.count"));
        BakeRunResult deleted = workspace.Run(application);

        Assert.Equal(0, deleted.ExitCode);
        Assert.Equal(2, processor.ProcessCount);
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        Assert.Empty(archive.Entries);
        BakeManifest? manifest = BakeManifestStore.TryRead(workspace.ManifestPath);
        Assert.NotNull(manifest);
        Assert.Empty(manifest.Packs[0].Sources);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void OnlyChangedSourcesAreReprocessed() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("a/one.count", "first");
        workspace.WriteAsset("a/two.count", "second");
        workspace.WriteAsset("b/one.count", "first");
        workspace.WriteAsset("b/two.count", "second");
        var processor = new CountingProcessor();
        BakeApplication application = workspace.Builder()
            .AddProcessor(processor)
            .Build();
        Assert.Equal(0, workspace.Run(application).ExitCode);
        Assert.Equal(4, processor.ProcessCount);

        workspace.WriteAsset("a/one.count", "changed");
        BakeRunResult result = workspace.Run(application);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(5, processor.ProcessCount);
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        Assert.True(archive.TryGetEntry("a/one", out _));
        Assert.True(archive.TryGetEntry("b/two", out _));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ToolVersionChangeReprocessesEverySource() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("unit.count", "source");
        var processor = new CountingProcessor();
        BakeApplication first = workspace.Builder()
            .AddProcessor(processor)
            .WithToolVersionOverride("test-version-1")
            .Build();
        BakeApplication second = workspace.Builder()
            .AddProcessor(processor)
            .WithToolVersionOverride("test-version-2")
            .Build();

        Assert.Equal(0, workspace.Run(first).ExitCode);
        BakeRunResult result = workspace.Run(second);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(2, processor.ProcessCount);
        Assert.DoesNotContain("up to date", result.Output);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ForceBypassesTheCache() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("unit.count", "source");
        var processor = new CountingProcessor();
        BakeApplication application = workspace.Builder()
            .AddProcessor(processor)
            .Build();
        Assert.Equal(0, workspace.Run(application).ExitCode);

        BakeRunResult forced = workspace.Run(
            application,
            "bake",
            "--output-root",
            workspace.OutputRoot,
            "--force");

        Assert.Equal(0, forced.ExitCode);
        Assert.Equal(2, processor.ProcessCount);
        Assert.DoesNotContain("up to date", forced.Output);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SidecarChangeIsReprocessed() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("a.opt", "source");
        workspace.WriteAsset("a.toml", """
            [optional]
            value = 1
            label = "one"
            """);
        var processor = new OptionalSidecarProcessor();
        BakeApplication application = workspace.Builder()
            .AddProcessor(processor)
            .Build();
        Assert.Equal(0, workspace.Run(application).ExitCode);

        workspace.WriteAsset("a.toml", """
            [optional]
            value = 2
            label = "two"
            """);
        BakeRunResult changed = workspace.Run(application);

        Assert.Equal(0, changed.ExitCode);
        Assert.Equal(2, processor.ProcessCount);
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        Assert.True(archive.TryGetEntry("a", out PackEntry? entry));
        TestPayload payload = AssetSerialization.Deserialize<TestPayload>(archive.ReadPayload(entry));
        Assert.Equal(2, payload.Value);
        Assert.Equal("two", payload.Name);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AdditionalInputChangeIsReprocessed() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("a.toml", """
            [input]
            file = "extra.txt"
            """);
        workspace.WriteAsset("extra.txt", "one");
        var processor = new InputProcessor();
        BakeApplication application = workspace.Builder()
            .AddProcessor(processor)
            .Build();
        Assert.Equal(0, workspace.Run(application).ExitCode);

        workspace.WriteAsset("extra.txt", "two");
        BakeRunResult changed = workspace.Run(application);

        Assert.Equal(0, changed.ExitCode);
        Assert.Equal(2, processor.ProcessCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MissingPackOrCorruptManifestForcesAFullBake() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("unit.count", "source");
        var processor = new CountingProcessor();
        BakeApplication application = workspace.Builder()
            .AddProcessor(processor)
            .Build();
        Assert.Equal(0, workspace.Run(application).ExitCode);

        File.Delete(workspace.GetPackPath());
        BakeRunResult missingPack = workspace.Run(application);
        Assert.Equal(0, missingPack.ExitCode);
        Assert.Equal(2, processor.ProcessCount);

        File.WriteAllText(workspace.ManifestPath, "{ not json");
        BakeRunResult corruptManifest = workspace.Run(application);
        Assert.Equal(0, corruptManifest.ExitCode);
        Assert.Equal(3, processor.ProcessCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ConfigurationChangeInvalidatesTheCache() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("unit.count", "source");
        var processor = new CountingProcessor();
        BakeApplication first = workspace.Builder()
            .AddProcessor(processor)
            .Build();
        BakeApplication second = workspace.Builder()
            .AddProcessor(processor)
            .AddProcessor(new TableSourceProcessor())
            .Build();

        Assert.Equal(0, workspace.Run(first).ExitCode);
        BakeRunResult result = workspace.Run(second);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(2, processor.ProcessCount);
    }

    private static int ReadPayloadValue(TempBakeWorkspace workspace, string path) {
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        Assert.True(archive.TryGetEntry(path, out PackEntry? entry));
        TestPayload payload = AssetSerialization.Deserialize<TestPayload>(archive.ReadPayload(entry));
        return payload.Value;
    }
}
