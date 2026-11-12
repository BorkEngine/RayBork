using RayBork.Bake;
using RayBork.Internal;
using RayBork.Tests.Bake.TestInfrastructure;

namespace RayBork.Tests.Bake;

public sealed class BakeSourceDiscoveryTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void OptionalSidecarBindsItsProcessorTable() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("textures/castle.opt", "source");
        workspace.WriteAsset("textures/castle.toml", """
            [optional]
            value = 7
            label = "sidecar"
            """);
        BakeApplication application = workspace.Builder()
            .AddProcessor(new OptionalSidecarProcessor())
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(0, result.ExitCode);
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        TestPayload payload = ReadPayload(archive, "textures/castle");
        Assert.Equal(7, payload.Value);
        Assert.Equal("sidecar", payload.Name);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CompoundExtensionUsesTheBasePathSidecarName() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("textures/castle.bundle.opt", "source");
        workspace.WriteAsset("textures/castle.toml", """
            [compound-optional]
            value = 5
            label = "compound"
            """);
        BakeApplication application = workspace.Builder()
            .AddProcessor(new CompoundOptionalProcessor())
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        TestPayload payload = ReadPayload(archive, "textures/castle");
        Assert.Equal(5, payload.Value);
        Assert.Equal("compound", payload.Name);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MissingOptionalSidecarUsesTheInMemoryDefault() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("textures/castle.opt", "source");
        BakeApplication application = workspace.Builder()
            .AddProcessor(new OptionalSidecarProcessor())
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(0, result.ExitCode);
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        TestPayload payload = ReadPayload(archive, "textures/castle");
        Assert.Equal(-1, payload.Value);
        Assert.Equal("default", payload.Name);
        Assert.False(File.Exists(Path.Combine(workspace.AssetRoot, "textures", "castle.toml")));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MissingRequiredSidecarIsAnError() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("textures/castle.req", "source");
        BakeApplication application = workspace.Builder()
            .AddProcessor(new RequiredSidecarProcessor())
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("RB4002", result.Error);
        Assert.Contains("required sidecar", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(workspace.GetPackPath()));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UnknownSidecarKeyIsAnError() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("a.opt", "source");
        workspace.WriteAsset("a.toml", """
            [optional]
            value = 1
            bogus = true
            """);
        BakeApplication application = workspace.Builder()
            .AddProcessor(new OptionalSidecarProcessor())
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("RB4002", result.Error);
        Assert.Contains("Unknown key 'bogus'", result.Error);
        Assert.Contains("a.toml(3,1)", result.Error);
        Assert.False(File.Exists(workspace.GetPackPath()));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ExtraRootTableInSidecarIsAnError() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("a.opt", "source");
        workspace.WriteAsset("a.toml", """
            [optional]
            value = 1

            [other]
            value = 2
            """);
        BakeApplication application = workspace.Builder()
            .AddProcessor(new OptionalSidecarProcessor())
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("RB4002", result.Error);
        Assert.Contains("Unknown root key 'other'", result.Error);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void OrphanTomlIsAnError() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("settings.toml", """
            [unknown]
            value = 1
            """);
        BakeApplication application = workspace.Builder().Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("RB4002", result.Error);
        Assert.Contains("orphan", result.Error);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TableNamedTomlIsAProcessorSourceWithoutASibling() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("shaders/castle.toml", """
            [shader]
            value = 3
            label = "table-source"
            """);
        BakeApplication application = workspace.Builder()
            .AddProcessor(new TableSourceProcessor())
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(0, result.ExitCode);
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        TestPayload payload = ReadPayload(archive, "shaders/castle");
        Assert.Equal(3, payload.Value);
        Assert.Equal("table-source", payload.Name);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TableNamedTomlWithASiblingIsTheSiblingSidecar() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("a.opt", "source");
        workspace.WriteAsset("a.toml", """
            [shader]
            value = 3
            """);
        BakeApplication application = workspace.Builder()
            .AddProcessor(new OptionalSidecarProcessor())
            .AddProcessor(new TableSourceProcessor())
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("RB4002", result.Error);
        Assert.Contains("'[optional]'", result.Error);
        Assert.False(File.Exists(workspace.GetPackPath()));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SidecarNextToASidecarFreeProcessorIsAnError() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("a.count", "source");
        workspace.WriteAsset("a.toml", """
            [counter]
            value = 1
            """);
        BakeApplication application = workspace.Builder()
            .AddProcessor(new CountingProcessor())
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("RB4002", result.Error);
        Assert.Contains("declares no sidecar", result.Error);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ExtraRootTableInATableNamedSourceIsAnError() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("castle.toml", """
            [shader]
            value = 1

            [extra]
            value = 2
            """);
        BakeApplication application = workspace.Builder()
            .AddProcessor(new TableSourceProcessor())
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("RB4002", result.Error);
        Assert.Contains("Unknown root key 'extra'", result.Error);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DuplicateCanonicalPathsInOnePackAreErrors() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("hero.one", "first");
        workspace.WriteAsset("hero.two", "second");
        BakeApplication application = workspace.Builder()
            .AddProcessor(new CountingProcessor("one", "one"))
            .AddProcessor(new CountingProcessor("two", "two"))
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("RB4003", result.Error);
        Assert.Contains("'hero'", result.Error);
        Assert.False(File.Exists(workspace.GetPackPath()));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CaseNormalizedCanonicalCollisionsAreErrors() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("Textures/Hero.one", "first");
        workspace.WriteAsset("Textures/hero.two", "second");
        BakeApplication application = workspace.Builder()
            .AddProcessor(new CountingProcessor("one", "one"))
            .AddProcessor(new CountingProcessor("two", "two"))
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("RB4003", result.Error);
        Assert.Contains("'textures/hero'", result.Error);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UnclaimedFileUsesTheRawFallback() {
        using var workspace = new TempBakeWorkspace();
        byte[] bytes = [1, 2, 3, 4, 5];
        workspace.WriteAssetBytes("data/readme.bin", bytes);
        BakeApplication application = workspace.Builder()
            .UseDefaultProcessors()
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(0, result.ExitCode);
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        Assert.True(archive.TryGetEntry("data/readme", out PackEntry? entry));
        Assert.Equal(AssetEntryType.Raw, entry.Type);
        Assert.Equal(bytes, archive.ReadPayload(entry));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UnclaimedFileWithoutRawFallbackIsAnError() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("data/readme.bin", "data");
        BakeApplication application = workspace.Builder().Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("RB4003", result.Error);
        Assert.Contains("raw fallback", result.Error);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CompoundTomlExtensionIsAProcessorSource() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("shaders/castle.shader.toml", "[shader]\nvertex = \"v.glsl\"");
        var processor = new CountingProcessor("shader-file", "shader.toml");
        BakeApplication application = workspace.Builder()
            .AddProcessor(processor)
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, processor.ProcessCount);
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        Assert.True(archive.TryGetEntry("shaders/castle", out _));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void LaterRegistrationOverridesAnEarlierExtension() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("a.count", "source");
        var first = new CountingProcessor("first", "count");
        var second = new CountingProcessor("second", "count");
        BakeApplication application = workspace.Builder()
            .AddProcessor(first)
            .AddProcessor(second)
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(0, first.ProcessCount);
        Assert.Equal(1, second.ProcessCount);
        Assert.True(File.Exists(workspace.GetPackPath()));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void LongestCompoundExtensionWins() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("thing.bundle.zip", "compound");
        var compound = new CountingProcessor("compound", "bundle.zip");
        var zip = new CountingProcessor("zip", "zip");
        BakeApplication application = workspace.Builder()
            .AddProcessor(zip)
            .AddProcessor(compound)
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, compound.ProcessCount);
        Assert.Equal(0, zip.ProcessCount);
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        Assert.True(archive.TryGetEntry("thing", out _));
    }

    private static TestPayload ReadPayload(PackArchive archive, string path) {
        Assert.True(archive.TryGetEntry(path, out PackEntry? entry));
        Assert.Equal(AssetEntryType.Data, entry.Type);
        return AssetSerialization.Deserialize<TestPayload>(archive.ReadPayload(entry));
    }
}
