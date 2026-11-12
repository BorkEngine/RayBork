using RayBork.Bake;
using RayBork.Internal;
using RayBork.Tests.Bake.TestInfrastructure;

namespace RayBork.Tests.Bake;

public sealed class BakePackOutputTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void BakeWritesAReadableRayBorkPackAndAManifest() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("unit.count", "source");
        BakeApplication application = workspace.Builder()
            .AddProcessor(new CountingProcessor())
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        Assert.True(File.Exists(workspace.GetPackPath()));
        Assert.True(File.Exists(workspace.ManifestPath));
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        Assert.NotEmpty(archive.Entries);
        Assert.Contains("baked", result.Output);
        Assert.Empty(result.Error);
        Assert.Empty(Directory.GetFiles(workspace.OutputRoot, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DebugStoresUncompressedAndReleaseCompressesLargePayloads() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("blob.bin", new string('A', 8192));
        BakeApplication application = workspace.Builder()
            .UseDefaultProcessors()
            .Build();

        BakeRunResult debug = workspace.Run(application);
        Assert.True(debug.ExitCode == 0, debug.Error + debug.Output);
        using (PackArchive archive = PackArchive.Open(workspace.GetPackPath())) {
            Assert.True(archive.TryGetEntry("blob", out PackEntry? entry));
            Assert.False(entry.IsCompressed);
        }

        BakeRunResult release = workspace.Run(
            application,
            "bake",
            "--output-root",
            workspace.OutputRoot,
            "--mode",
            "release",
            "--force");
        Assert.True(release.ExitCode == 0, release.Error + release.Output);
        using (PackArchive archive = PackArchive.Open(workspace.GetPackPath())) {
            Assert.True(archive.TryGetEntry("blob", out PackEntry? entry));
            Assert.True(entry.IsCompressed);
            Assert.Equal(new string('A', 8192), System.Text.Encoding.UTF8.GetString(archive.ReadPayload(entry)));
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CheckProcessesEverySourceAndWritesNothing() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("unit.count", "source");
        var processor = new CountingProcessor();
        BakeApplication application = workspace.Builder()
            .AddProcessor(processor)
            .Build();

        BakeRunResult result = workspace.Run(
            application,
            "bake",
            "--output-root",
            workspace.OutputRoot,
            "--check");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, processor.ProcessCount);
        Assert.Contains("valid (--check)", result.Output);
        Assert.Contains("no pack written", result.Output);
        Assert.False(File.Exists(workspace.GetPackPath()));
        Assert.False(File.Exists(workspace.ManifestPath));

        BakeRunResult normal = workspace.Run(application);
        Assert.Equal(0, normal.ExitCode);
        Assert.Equal(2, processor.ProcessCount);
        Assert.True(File.Exists(workspace.GetPackPath()));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void FailedPackLeavesThePreviousTargetUntouched() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("unit.count", "source");
        BakeApplication application = workspace.Builder()
            .AddProcessor(new CountingProcessor())
            .Build();
        BakeRunResult first = workspace.Run(application);
        Assert.Equal(0, first.ExitCode);
        byte[] previous = File.ReadAllBytes(workspace.GetPackPath());

        workspace.WriteAsset("broken.toml", "[not valid");
        BakeRunResult failed = workspace.Run(
            application,
            "bake",
            "--output-root",
            workspace.OutputRoot,
            "--force");

        Assert.Equal(1, failed.ExitCode);
        Assert.Contains("RB4002", failed.Error);
        Assert.Equal(previous, File.ReadAllBytes(workspace.GetPackPath()));
        Assert.Empty(Directory.GetFiles(workspace.OutputRoot, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MultiplePacksAreWrittenInConfigurationOrder() {
        using var workspace = new TempBakeWorkspace();
        string overlayRoot = Path.Combine(workspace.ProjectRoot, "Overlay");
        Directory.CreateDirectory(overlayRoot);
        workspace.WriteAsset("main.count", "main");
        File.WriteAllText(Path.Combine(overlayRoot, "overlay.count"), "overlay");
        BakeApplication application = new BakeApplicationBuilder()
            .WithProjectRoot(workspace.ProjectRoot)
            .WithPack("main.pck", "Assets")
            .WithPack("overlay/overlay.pck", "Overlay")
            .AddProcessor(new CountingProcessor())
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        Assert.True(File.Exists(Path.Combine(workspace.OutputRoot, "main.pck")));
        Assert.True(File.Exists(Path.Combine(workspace.OutputRoot, "overlay", "overlay.pck")));
        using PackArchive main = PackArchive.Open(Path.Combine(workspace.OutputRoot, "main.pck"));
        using PackArchive overlay = PackArchive.Open(Path.Combine(workspace.OutputRoot, "overlay", "overlay.pck"));
        Assert.Contains("main", main.Entries.Select(entry => entry.Path));
        Assert.Contains("overlay", overlay.Entries.Select(entry => entry.Path));
    }
}
