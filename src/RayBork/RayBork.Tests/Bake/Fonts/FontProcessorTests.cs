using RayBork.Bake;
using RayBork.Internal;
using RayBork.Tests.Bake.TestInfrastructure;

namespace RayBork.Tests.Bake.Fonts;

/// <summary>Tests for the built-in font processor and its sfnt validation.</summary>
public sealed class FontProcessorTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ValidTtfBakesTheDescriptorAndOriginalFaceBytes() {
        using var workspace = new TempBakeWorkspace();
        byte[] face = FontTestData.BuildTtf();
        workspace.WriteAssetBytes("fonts/minimal.ttf", face);
        BakeApplication application = workspace.Builder().UseDefaultProcessors().Build();

        BakeRunResult result = workspace.Run(application);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        FontAssetDescriptor descriptor = ReadFont(archive, "fonts/minimal");
        Assert.Equal("fonts/minimal-face", descriptor.FacePayload);
        Assert.Equal("ttf", descriptor.Format);
        Assert.Equal((uint)face.Length, descriptor.ByteLength);
        Assert.Equal(["U+0020-007E"], descriptor.Ranges);
        Assert.Equal(32, descriptor.MsdfPixelSize);
        Assert.Equal(4, descriptor.MsdfRange);
        Assert.Equal(2, descriptor.MsdfPadding);

        Assert.True(archive.TryGetEntry("fonts/minimal-face", out PackEntry? payload));
        Assert.NotNull(payload);
        Assert.Equal(AssetEntryType.Raw, payload.Type);
        Assert.Equal(16u, payload.Alignment);
        Assert.Equal(face, archive.ReadPayload(payload));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void OtfCffFaceIsAccepted() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAssetBytes("fonts/minimal.otf", FontTestData.BuildOtf());
        BakeApplication application = workspace.Builder().UseDefaultProcessors().Build();

        BakeRunResult result = workspace.Run(application);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        Assert.Equal("otf", ReadFont(archive, "fonts/minimal").Format);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TrueMagicFaceIsAccepted() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAssetBytes("fonts/true.ttf", FontTestData.BuildTrueMagicTtf());
        BakeApplication application = workspace.Builder().UseDefaultProcessors().Build();

        BakeRunResult result = workspace.Run(application);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        Assert.Equal("ttf", ReadFont(archive, "fonts/true").Format);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void BadMagicIsRejectedWithoutOutputs() {
        AssertRejected(FontTestData.WithBadMagic(FontTestData.BuildTtf()), "magic");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TruncatedTableDirectoryIsRejected() {
        AssertRejected(
            FontTestData.WithTruncatedDirectory(FontTestData.BuildTtf()),
            "table directory extends past");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void OutOfBoundsTableIsRejected() {
        AssertRejected(
            FontTestData.WithOutOfBoundsTable(FontTestData.BuildTtf()),
            "extends past");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MissingRequiredTableIsRejected() {
        AssertRejected(FontTestData.BuildTtf("cmap"), "cmap");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TrueTypeCollectionIsRejected() {
        AssertRejected(FontTestData.BuildTtc(), "TrueType Collection");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void FontSidecarSettingsAreStored() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAssetBytes("fonts/custom.ttf", FontTestData.BuildTtf());
        workspace.WriteAsset("fonts/custom.toml", """
            [font]
            ranges = ["U+0020-007E", "U+00A0-00FF"]
            msdf_pixel_size = 64
            msdf_range = 8
            msdf_padding = 0
            """);
        BakeApplication application = workspace.Builder().UseDefaultProcessors().Build();

        BakeRunResult result = workspace.Run(application);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        FontAssetDescriptor descriptor = ReadFont(archive, "fonts/custom");
        Assert.Equal(["U+0020-007E", "U+00A0-00FF"], descriptor.Ranges);
        Assert.Equal(64, descriptor.MsdfPixelSize);
        Assert.Equal(8, descriptor.MsdfRange);
        Assert.Equal(0, descriptor.MsdfPadding);
    }

    [Theory]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    [InlineData("[font]\nranges = [\"0020\"]\n", "RB4010")]
    [InlineData("[font]\nranges = [\"U+ZZZZ\"]\n", "RB4010")]
    [InlineData("[font]\nranges = [\"U+0100-0020\"]\n", "RB4010")]
    [InlineData("[font]\nranges = [\"U+110000\"]\n", "RB4010")]
    [InlineData("[font]\nmsdf_pixel_size = 0\n", "RB4010")]
    [InlineData("[font]\nmsdf_range = -1\n", "RB4010")]
    [InlineData("[font]\nmsdf_padding = -1\n", "RB4010")]
    [InlineData("[font]\nranges = \"U+0020\"\n", "RB4002")]
    [InlineData("[font]\nmsdf_pixel_size = \"big\"\n", "RB4002")]
    [InlineData("[font]\nbogus = 1\n", "RB4002")]
    public void InvalidSidecarValuesAreReported(string sidecar, string code) {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAssetBytes("fonts/bad.ttf", FontTestData.BuildTtf());
        workspace.WriteAsset("fonts/bad.toml", sidecar);
        BakeApplication application = workspace.Builder().UseDefaultProcessors().Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(code, result.Error);
        Assert.False(File.Exists(workspace.GetPackPath()));
    }

    private static void AssertRejected(byte[] face, string expectedMessage) {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAssetBytes("fonts/invalid.ttf", face);
        BakeApplication application = workspace.Builder().UseDefaultProcessors().Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("RB4010", result.Error);
        Assert.Contains(expectedMessage, result.Error);
        Assert.False(File.Exists(workspace.GetPackPath()));
    }

    private static FontAssetDescriptor ReadFont(PackArchive archive, string path) {
        Assert.True(archive.TryGetEntry(path, out PackEntry? entry), $"missing font '{path}'");
        Assert.NotNull(entry);
        Assert.Equal(AssetEntryType.Font, entry.Type);
        return AssetSerialization.Deserialize<FontAssetDescriptor>(archive.ReadPayload(entry));
    }
}
