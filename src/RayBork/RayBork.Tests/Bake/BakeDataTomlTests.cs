using RayBork.Bake;
using RayBork.Bake.Diagnostics;
using RayBork.Internal;
using RayBork.Tests.Bake.TestInfrastructure;

namespace RayBork.Tests.Bake;

public sealed class BakeDataTomlTests {
    private static string TypeName => typeof(GameSettings).FullName!;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RegisteredDataTomlEmitsOneDataEntry() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("data/settings.toml", $$"""
            [raybork]
            type = "{{TypeName}}"

            name = "The Castle"
            difficulty = 2
            paddle_speed = 3.5
            max_players = 4
            """);
        BakeApplication application = workspace.Builder()
            .RegisterAsset<GameSettings>()
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        Assert.True(archive.TryGetEntry("data/settings", out PackEntry? entry));
        Assert.Equal(AssetEntryType.Data, entry.Type);
        GameSettings settings = AssetSerialization.Deserialize<GameSettings>(archive.ReadPayload(entry));
        Assert.Equal("The Castle", settings.Name);
        Assert.Equal(2, settings.Difficulty);
        Assert.Equal(3.5f, settings.PaddleSpeed);
        Assert.Equal(4, settings.MaxPlayers);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RootKeysBeforeTheMetadataTableAlsoBind() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("data/settings.toml", $$"""
            name = "Root Form"
            difficulty = 3

            [raybork]
            type = "{{TypeName}}"
            """);
        BakeApplication application = workspace.Builder()
            .RegisterAsset<GameSettings>()
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        Assert.True(archive.TryGetEntry("data/settings", out PackEntry? entry));
        GameSettings settings = AssetSerialization.Deserialize<GameSettings>(archive.ReadPayload(entry));
        Assert.Equal("Root Form", settings.Name);
        Assert.Equal(3, settings.Difficulty);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ValidatorSuccessAndFailureRunAfterBinding() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("data/good.toml", DataToml(difficulty: 2));
        BakeApplication application = workspace.Builder()
            .RegisterAsset<GameSettings>(new SettingsValidator())
            .Build();

        BakeRunResult good = workspace.Run(application);
        Assert.True(good.ExitCode == 0, good.Error + good.Output);
        Assert.True(File.Exists(workspace.GetPackPath()));

        File.Delete(workspace.GetPackPath());
        workspace.WriteAsset("data/good.toml", DataToml(difficulty: 0));
        BakeRunResult bad = workspace.Run(application, "bake", "--output-root", workspace.OutputRoot, "--force");

        Assert.Equal(1, bad.ExitCode);
        Assert.Contains("RB4012", bad.Error);
        Assert.Contains("difficulty", bad.Error);
        Assert.False(File.Exists(workspace.GetPackPath()));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ValidatorWarningDoesNotFail() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("data/settings.toml", DataToml(difficulty: 2));
        BakeApplication application = workspace.Builder()
            .RegisterAsset<GameSettings>(new WarningSettingsValidator())
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("warning RB4012", result.Error);
        Assert.True(File.Exists(workspace.GetPackPath()));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UnregisteredTypeIsAnError() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("data/settings.toml", DataToml(difficulty: 2));
        BakeApplication application = workspace.Builder().Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("RB4002", result.Error);
        Assert.Contains("is not registered", result.Error);
        Assert.False(File.Exists(workspace.GetPackPath()));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MissingOrMalformedTypeIsAnError() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("data/missing.toml", """
            [raybork]
            other = 1
            """);
        BakeApplication application = workspace.Builder()
            .RegisterAsset<GameSettings>()
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("RB4002", result.Error);
        Assert.Contains("type", result.Error);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UnknownDataKeyAndMissingRequiredMemberAreErrors() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("data/settings.toml", $$"""
            [raybork]
            type = "{{TypeName}}"

            difficulty = 1
            unknown_key = true
            """);
        BakeApplication application = workspace.Builder()
            .RegisterAsset<GameSettings>()
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Unknown key 'unknown_key'", result.Error);
        Assert.Contains("Missing required key 'name'", result.Error);
        Assert.Contains("RB4002", result.Error);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DataTomlWithADotInItsNameIsNotCanonical() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("data/v1.settings.toml", DataToml(difficulty: 1));
        BakeApplication application = workspace.Builder()
            .RegisterAsset<GameSettings>()
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("RB4003", result.Error);
        Assert.Contains("canonical", result.Error);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DataAndProcessorSourcesCannotShareACanonicalPath() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("data/level.toml", DataToml(difficulty: 1));
        workspace.WriteAsset("data/level.count", "source");
        BakeApplication application = workspace.Builder()
            .RegisterAsset<GameSettings>()
            .AddProcessor(new CountingProcessor())
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("RB4003", result.Error);
        Assert.Contains("'data/level'", result.Error);
    }

    private static string DataToml(int difficulty) => $$"""
        [raybork]
        type = "{{TypeName}}"

        name = "Settings"
        difficulty = {{difficulty}}
        """;

    private sealed class SettingsValidator : IAssetValidator<GameSettings> {
        public void Validate(
            string assetPath,
            GameSettings asset,
            IAssetProcessorDiagnostics diagnostics) {
            if (asset.Difficulty <= 0) {
                diagnostics.Error("RB4012", $"asset '{assetPath}' has a non-positive difficulty");
            }
        }
    }

    private sealed class WarningSettingsValidator : IAssetValidator<GameSettings> {
        public void Validate(
            string assetPath,
            GameSettings asset,
            IAssetProcessorDiagnostics diagnostics)
            => diagnostics.Warning("RB4012", $"asset '{assetPath}' is only a warning");
    }
}
