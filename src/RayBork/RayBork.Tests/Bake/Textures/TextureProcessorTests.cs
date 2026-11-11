using RayBork.Bake;
using RayBork.Internal;
using RayBork.Tests.Bake.TestInfrastructure;

namespace RayBork.Tests.Bake.Textures;

public sealed class TextureProcessorTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PngWithoutSidecarUsesTheLatestBuilderDefaultsAndSrgb() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAssetBytes("textures/castle.png", TextureTestData.TinyPng);
        BakeApplication application = workspace.Builder()
            .UseDefaultProcessors()
            .WithTextureDefaults(TextureFilter.Nearest, TextureWrap.Clamp)
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        Assert.True(archive.TryGetEntry("textures/castle", out PackEntry? entry));
        Assert.Equal(AssetEntryType.Texture, entry.Type);
        TextureAssetDescriptor descriptor = ReadDescriptor(archive, entry);
        Assert.Equal(2u, descriptor.Width);
        Assert.Equal(2u, descriptor.Height);
        Assert.Equal(2u, descriptor.MipLevels);
        Assert.Equal(TextureFormat.Rgba8Srgb, descriptor.Format);
        Assert.True(descriptor.IsSrgb);
        Assert.Equal(TextureFilter.Nearest, descriptor.Filter);
        Assert.Equal(TextureWrap.Clamp, descriptor.Wrap);
        Assert.Equal(1f, descriptor.MaxAnisotropy);
        Assert.Equal("textures/castle-ktx2", descriptor.Payload);

        Assert.True(archive.TryGetEntry("textures/castle-ktx2", out PackEntry? payloadEntry));
        Assert.Equal(AssetEntryType.Raw, payloadEntry.Type);
        Assert.Equal(16u, payloadEntry.Alignment);
        ReadOnlySpan<byte> payload = archive.ReadPayload(payloadEntry);
        Ktx2TestDocument document = Ktx2TestDocument.Parse(payload);
        Assert.Equal(43u, document.VkFormat);
        Assert.Equal(2, document.Dfd.TransferFunction);
        Ktx2Texture parsed = Ktx2Reader.Read(payload, descriptor);
        Assert.Equal(TextureFormat.Rgba8Srgb, parsed.Format);
        Assert.True(parsed.IsSrgb);
        Assert.Equal(2u, parsed.Width);
        Assert.Equal(2u, parsed.Height);
        Assert.Equal(2, parsed.MipLevels);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SidecarOverridesTheFormatTransferAndSampler() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAssetBytes("textures/castle.png", TextureTestData.TinyPng);
        workspace.WriteAsset("textures/castle.toml", """
            [texture]
            format = "bc7"
            srgb = false
            filter = "anisotropic"
            wrap = "mirror"
            anisotropy = 8
            """);
        BakeApplication application = workspace.Builder()
            .UseDefaultProcessors()
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        Assert.True(archive.TryGetEntry("textures/castle", out PackEntry? entry));
        TextureAssetDescriptor descriptor = ReadDescriptor(archive, entry);
        Assert.Equal(TextureFormat.Bc7Unorm, descriptor.Format);
        Assert.False(descriptor.IsSrgb);
        Assert.Equal(TextureFilter.Anisotropic, descriptor.Filter);
        Assert.Equal(TextureWrap.Mirror, descriptor.Wrap);
        Assert.Equal(8f, descriptor.MaxAnisotropy);

        Assert.True(archive.TryGetEntry("textures/castle-ktx2", out PackEntry? payloadEntry));
        ReadOnlySpan<byte> payload = archive.ReadPayload(payloadEntry);
        Ktx2TestDocument document = Ktx2TestDocument.Parse(payload);
        Assert.Equal(145u, document.VkFormat);
        Assert.Equal(1, document.Dfd.TransferFunction);
        Ktx2Texture parsed = Ktx2Reader.Read(payload, descriptor);
        Assert.Equal(TextureFormat.Bc7Unorm, parsed.Format);
        Assert.False(parsed.IsSrgb);
        Assert.Equal(2, parsed.MipLevels);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void Bc5AndR8SidecarsStoreLinearData() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAssetBytes("textures/normal.png", TextureTestData.TinyPng);
        workspace.WriteAsset("textures/normal.toml", """
            [texture]
            format = "bc5"
            """);
        workspace.WriteAssetBytes("textures/mask.png", TextureTestData.TinyPng);
        workspace.WriteAsset("textures/mask.toml", """
            [texture]
            format = "r8"
            """);
        BakeApplication application = workspace.Builder()
            .UseDefaultProcessors()
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        TextureAssetDescriptor normal = ReadDescriptor(archive, "textures/normal");
        Assert.Equal(TextureFormat.Bc5Unorm, normal.Format);
        Assert.False(normal.IsSrgb);
        TextureAssetDescriptor mask = ReadDescriptor(archive, "textures/mask");
        Assert.Equal(TextureFormat.R8Unorm, mask.Format);
        Assert.False(mask.IsSrgb);

        Assert.True(archive.TryGetEntry("textures/normal-ktx2", out PackEntry? normalEntry));
        Ktx2TestDocument normalDocument = Ktx2TestDocument.Parse(archive.ReadPayload(normalEntry));
        Assert.Equal(141u, normalDocument.VkFormat);
        Assert.Equal(1, normalDocument.Dfd.TransferFunction);
        Assert.Equal(2, normalDocument.Dfd.Samples.Count);
        Ktx2Texture normalParsed = Ktx2Reader.Read(archive.ReadPayload(normalEntry), normal);
        Assert.Equal(TextureFormat.Bc5Unorm, normalParsed.Format);

        Assert.True(archive.TryGetEntry("textures/mask-ktx2", out PackEntry? maskEntry));
        Ktx2TestDocument maskDocument = Ktx2TestDocument.Parse(archive.ReadPayload(maskEntry));
        Assert.Equal(9u, maskDocument.VkFormat);
        Assert.Equal(1, maskDocument.Dfd.TransferFunction);
        Assert.Single(maskDocument.Dfd.Samples);
        Ktx2Texture maskParsed = Ktx2Reader.Read(archive.ReadPayload(maskEntry), mask);
        Assert.Equal(TextureFormat.R8Unorm, maskParsed.Format);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void APartialSidecarInheritsTheBuilderSamplerDefaults() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAssetBytes("textures/castle.png", TextureTestData.TinyPng);
        workspace.WriteAsset("textures/castle.toml", """
            [texture]
            format = "bc7"
            """);
        BakeApplication application = workspace.Builder()
            .UseDefaultProcessors()
            .WithTextureDefaults(TextureFilter.Nearest, TextureWrap.Mirror)
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        TextureAssetDescriptor descriptor = ReadDescriptor(archive, "textures/castle");
        Assert.Equal(TextureFormat.Bc7Srgb, descriptor.Format);
        Assert.True(descriptor.IsSrgb);
        Assert.Equal(TextureFilter.Nearest, descriptor.Filter);
        Assert.Equal(TextureWrap.Mirror, descriptor.Wrap);
        Assert.Equal(1f, descriptor.MaxAnisotropy);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void LinearRgba8SidecarStoresTheUnormFormat() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAssetBytes("textures/data.png", TextureTestData.TinyPng);
        workspace.WriteAsset("textures/data.toml", """
            [texture]
            format = "rgba8"
            srgb = false
            """);
        BakeApplication application = workspace.Builder()
            .UseDefaultProcessors()
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        TextureAssetDescriptor descriptor = ReadDescriptor(archive, "textures/data");
        Assert.Equal(TextureFormat.Rgba8Unorm, descriptor.Format);
        Assert.False(descriptor.IsSrgb);

        Assert.True(archive.TryGetEntry("textures/data-ktx2", out PackEntry? payloadEntry));
        ReadOnlySpan<byte> payload = archive.ReadPayload(payloadEntry);
        Ktx2TestDocument document = Ktx2TestDocument.Parse(payload);
        Assert.Equal(37u, document.VkFormat);
        Assert.Equal(1, document.Dfd.TransferFunction);
        Ktx2Texture parsed = Ktx2Reader.Read(payload, descriptor);
        Assert.Equal(TextureFormat.Rgba8Unorm, parsed.Format);
        Assert.False(parsed.IsSrgb);
    }

    [Theory]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    [InlineData("format = \"rgba16\"")]
    [InlineData("format = \"bmp\"")]
    [InlineData("filter = \"cubic\"")]
    [InlineData("wrap = \"border\"")]
    [InlineData("format = \"bc5\"\nsrgb = true")]
    [InlineData("format = \"r8\"\nsrgb = true")]
    [InlineData("anisotropy = 0.0")]
    [InlineData("anisotropy = -2.0")]
    [InlineData("anisotropy = nan")]
    public void InvalidSidecarValuesAreRb4007(string sidecar) {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAssetBytes("textures/castle.png", TextureTestData.TinyPng);
        workspace.WriteAsset("textures/castle.toml", "[texture]\n" + sidecar);
        BakeApplication application = workspace.Builder()
            .UseDefaultProcessors()
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("RB4007", result.Error);
        Assert.False(File.Exists(workspace.GetPackPath()));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UnknownSidecarKeysAreRb4002() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAssetBytes("textures/castle.png", TextureTestData.TinyPng);
        workspace.WriteAsset("textures/castle.toml", """
            [texture]
            format = "bc7"
            bilinear = true
            """);
        BakeApplication application = workspace.Builder()
            .UseDefaultProcessors()
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("RB4002", result.Error);
        Assert.Contains("Unknown key 'bilinear'", result.Error);
        Assert.False(File.Exists(workspace.GetPackPath()));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WrongSidecarValueTypesAreRb4002() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAssetBytes("textures/castle.png", TextureTestData.TinyPng);
        workspace.WriteAsset("textures/castle.toml", """
            [texture]
            format = 7
            """);
        BakeApplication application = workspace.Builder()
            .UseDefaultProcessors()
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("RB4002", result.Error);
        Assert.Contains("must be a string", result.Error);
        Assert.False(File.Exists(workspace.GetPackPath()));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SixteenBitPngIsRejectedBeforeDecode() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAssetBytes("textures/deep.png", TextureTestData.SixteenBitPng);
        BakeApplication application = workspace.Builder()
            .UseDefaultProcessors()
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("RB4007", result.Error);
        Assert.Contains("16-bit", result.Error);
        Assert.False(File.Exists(workspace.GetPackPath()));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void OversizeDeclaredDimensionsAreRejected() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAssetBytes("textures/huge.png", TextureTestData.CreatePngHeader(20000, 2));
        BakeApplication application = workspace.Builder()
            .UseDefaultProcessors()
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("RB4007", result.Error);
        Assert.Contains("20000x2", result.Error);
        Assert.False(File.Exists(workspace.GetPackPath()));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MalformedSourceIsRejectedWithoutPartialOutputs() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAssetBytes("textures/broken.png", TextureTestData.TruncatedPng);
        BakeApplication application = workspace.Builder()
            .UseDefaultProcessors()
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("RB4007", result.Error);
        Assert.False(File.Exists(workspace.GetPackPath()));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TgaAndBmpSourcesDecodeThroughTheSameReader() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAssetBytes("textures/stamp.bmp", CreateBitmap());
        workspace.WriteAssetBytes("textures/badge.tga", CreateTga());
        BakeApplication application = workspace.Builder()
            .UseDefaultProcessors()
            .Build();

        BakeRunResult result = workspace.Run(application);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        TextureAssetDescriptor bmp = ReadDescriptor(archive, "textures/stamp");
        Assert.Equal(2u, bmp.Width);
        Assert.Equal(2u, bmp.Height);
        TextureAssetDescriptor tga = ReadDescriptor(archive, "textures/badge");
        Assert.Equal(2u, tga.Width);
        Assert.Equal(2u, tga.Height);
    }

    private static TextureAssetDescriptor ReadDescriptor(PackArchive archive, string path) {
        Assert.True(archive.TryGetEntry(path, out PackEntry? entry));
        return ReadDescriptor(archive, entry);
    }

    private static TextureAssetDescriptor ReadDescriptor(PackArchive archive, PackEntry entry)
        => AssetSerialization.Deserialize<TextureAssetDescriptor>(archive.ReadPayload(entry));

    private static byte[] CreateBitmap() {
        var bmp = new byte[70];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        WriteInt32(bmp, 2, bmp.Length);
        WriteInt32(bmp, 10, 54);
        WriteInt32(bmp, 14, 40);
        WriteInt32(bmp, 18, 2);
        WriteInt32(bmp, 22, 2);
        WriteInt16(bmp, 26, 1);
        WriteInt16(bmp, 28, 24);
        WriteInt32(bmp, 34, 16);
        byte[] rowBottom = [0x00, 0x00, 0xFF, 0x00, 0xFF, 0x00, 0x00, 0x00];
        byte[] rowTop = [0xFF, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0x00, 0x00];
        rowBottom.CopyTo(bmp, 54);
        rowTop.CopyTo(bmp, 62);
        return bmp;
    }

    private static byte[] CreateTga() {
        var tga = new byte[18 + 12];
        tga[2] = 2;
        WriteInt16(tga, 12, 2);
        WriteInt16(tga, 14, 2);
        tga[16] = 24;
        tga[17] = 0x20;
        byte[] pixels = [
            0x00, 0x00, 0xFF,
            0x00, 0xFF, 0x00,
            0xFF, 0x00, 0x00,
            0xFF, 0xFF, 0xFF,
        ];
        pixels.CopyTo(tga, 18);
        return tga;
    }

    private static void WriteInt16(byte[] buffer, int offset, int value) {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
    }

    private static void WriteInt32(byte[] buffer, int offset, int value) {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
        buffer[offset + 3] = (byte)(value >> 24);
    }
}
