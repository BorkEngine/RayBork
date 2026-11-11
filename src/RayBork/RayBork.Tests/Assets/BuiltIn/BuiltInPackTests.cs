using RayBork.Internal;

namespace RayBork.Tests.Assets.BuiltIn;

/// <summary>
/// Tests the generated built-in asset pack embedded in <c>RayBork.Engine</c>.
/// </summary>
/// <remarks>
/// The pack is produced by the engine's build target before this assembly compiles, so these tests
/// exercise the real embedded bytes instead of a checked-in fixture. No generated pack is committed.
/// </remarks>
public sealed class BuiltInPackTests {
    private const string DISPLAY_PATH = "<built-in>";
    private const string WHITE_PATH = "textures/white";
    private const string WHITE_PAYLOAD_PATH = "textures/white-ktx2";
    private const string ERROR_PATH = "textures/error";
    private const string ERROR_PAYLOAD_PATH = "textures/error-ktx2";
    private const string PRESENT_PATH = "shaders/present";
    private const string PRESENT_VERTEX_PAYLOAD_PATH = "shaders/present-vert";
    private const string PRESENT_FRAGMENT_PAYLOAD_PATH = "shaders/present-frag";
    private const int ENTRY_COUNT = 7;
    private const uint GPU_PAYLOAD_ALIGNMENT = 16;

    private static readonly byte[] _ktx2Identifier =
    [
        0xAB,
        0x4B,
        0x54,
        0x58,
        0x20,
        0x32,
        0x30,
        0xBB,
        0x0D,
        0x0A,
        0x1A,
        0x0A,
    ];

    private static readonly byte[] _spirvMagic = [0x03, 0x02, 0x23, 0x07];

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EmbeddedPackIsNonEmptyAndOpens() {
        ReadOnlyMemory<byte> bytes = BuiltInAssetPack.Bytes;

        Assert.False(bytes.IsEmpty);

        using PackArchive archive = PackArchive.Open(bytes, DISPLAY_PATH);

        Assert.Equal(DISPLAY_PATH, archive.DisplayPath);
        Assert.Equal(bytes.Length, archive.Length);
        Assert.Equal(ENTRY_COUNT, archive.Entries.Count);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ContainsTheBuiltInAssetAndPayloadEntries() {
        using PackArchive archive = OpenBuiltInPack();

        AssertEntry(archive, WHITE_PATH, AssetEntryType.Texture, isGpuPayload: false);
        AssertEntry(archive, WHITE_PAYLOAD_PATH, AssetEntryType.Raw, isGpuPayload: true);
        AssertEntry(archive, ERROR_PATH, AssetEntryType.Texture, isGpuPayload: false);
        AssertEntry(archive, ERROR_PAYLOAD_PATH, AssetEntryType.Raw, isGpuPayload: true);
        AssertEntry(archive, PRESENT_PATH, AssetEntryType.Shader, isGpuPayload: false);
        AssertEntry(archive, PRESENT_VERTEX_PAYLOAD_PATH, AssetEntryType.Raw, isGpuPayload: true);
        AssertEntry(archive, PRESENT_FRAGMENT_PAYLOAD_PATH, AssetEntryType.Raw, isGpuPayload: true);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WhiteTextureIsAnOpaqueOnePixelSrgbTexture() {
        using PackArchive archive = OpenBuiltInPack();
        TextureAssetDescriptor descriptor = ReadTextureDescriptor(archive, WHITE_PATH);

        Assert.Equal(WHITE_PAYLOAD_PATH, descriptor.Payload);
        Assert.Equal(1u, descriptor.Width);
        Assert.Equal(1u, descriptor.Height);
        Assert.Equal(1u, descriptor.MipLevels);
        Assert.Equal(TextureFormat.Rgba8Srgb, descriptor.Format);
        Assert.True(descriptor.IsSrgb);
        Assert.Equal(TextureFilter.Linear, descriptor.Filter);
        Assert.Equal(TextureWrap.Clamp, descriptor.Wrap);
        Assert.Equal(1f, descriptor.MaxAnisotropy);
        AssertKtx2Payload(archive, descriptor.Payload);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ErrorTextureIsTheFourByFourCheckerboard() {
        using PackArchive archive = OpenBuiltInPack();
        TextureAssetDescriptor descriptor = ReadTextureDescriptor(archive, ERROR_PATH);

        Assert.Equal(ERROR_PAYLOAD_PATH, descriptor.Payload);
        Assert.Equal(4u, descriptor.Width);
        Assert.Equal(4u, descriptor.Height);
        Assert.Equal(3u, descriptor.MipLevels);
        Assert.Equal(TextureFormat.Rgba8Srgb, descriptor.Format);
        Assert.True(descriptor.IsSrgb);
        Assert.Equal(TextureFilter.Nearest, descriptor.Filter);
        Assert.Equal(TextureWrap.Clamp, descriptor.Wrap);
        Assert.Equal(1f, descriptor.MaxAnisotropy);
        AssertKtx2Payload(archive, descriptor.Payload);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PresentShaderCarriesBothMainSpirvStages() {
        using PackArchive archive = OpenBuiltInPack();
        ShaderAssetDescriptor descriptor = ReadShaderDescriptor(archive, PRESENT_PATH);

        Assert.Equal(PRESENT_VERTEX_PAYLOAD_PATH, descriptor.VertexPayload);
        Assert.Equal(PRESENT_FRAGMENT_PAYLOAD_PATH, descriptor.FragmentPayload);
        Assert.Equal("main", descriptor.VertexEntry);
        Assert.Equal("main", descriptor.FragmentEntry);
        Assert.Equal(ShaderStageMask.Vertex | ShaderStageMask.Fragment, descriptor.Stages);
        Assert.Equal(RenderState.Opaque, descriptor.DefaultRenderState);
        Assert.Empty(descriptor.Uniforms);
        Assert.Empty(descriptor.Textures);

        AssertSpirvPayload(archive, descriptor.VertexPayload);
        AssertSpirvPayload(archive, descriptor.FragmentPayload);
    }

    private static PackArchive OpenBuiltInPack()
        => PackArchive.Open(BuiltInAssetPack.Bytes, DISPLAY_PATH);

    private static void AssertEntry(
        PackArchive archive,
        string path,
        AssetEntryType type,
        bool isGpuPayload) {
        Assert.True(archive.TryGetEntry(path, out PackEntry? entry), $"The entry '{path}' is missing.");
        Assert.Equal(path, entry.Path);
        Assert.Equal(type, entry.Type);
        Assert.Equal(isGpuPayload ? GPU_PAYLOAD_ALIGNMENT : 1u, entry.Alignment);
        Assert.False(entry.IsCompressed);
    }

    private static TextureAssetDescriptor ReadTextureDescriptor(PackArchive archive, string path) {
        Assert.True(archive.TryGetEntry(path, out PackEntry? entry), $"The entry '{path}' is missing.");
        return AssetSerialization.Deserialize<TextureAssetDescriptor>(archive.ReadPayload(entry));
    }

    private static ShaderAssetDescriptor ReadShaderDescriptor(PackArchive archive, string path) {
        Assert.True(archive.TryGetEntry(path, out PackEntry? entry), $"The entry '{path}' is missing.");
        return AssetSerialization.Deserialize<ShaderAssetDescriptor>(archive.ReadPayload(entry));
    }

    private static void AssertKtx2Payload(PackArchive archive, string path) {
        byte[] payload = ReadRawPayload(archive, path);

        Assert.True(
            payload.Length > _ktx2Identifier.Length,
            $"The KTX2 payload '{path}' is {payload.Length} bytes.");
        Assert.Equal(_ktx2Identifier, payload.AsSpan(0, _ktx2Identifier.Length).ToArray());
    }

    private static void AssertSpirvPayload(PackArchive archive, string path) {
        byte[] payload = ReadRawPayload(archive, path);

        Assert.True(payload.Length > _spirvMagic.Length, $"The SPIR-V payload '{path}' is empty.");
        Assert.Equal(_spirvMagic, payload.AsSpan(0, _spirvMagic.Length).ToArray());
        Assert.Equal(0, payload.Length % sizeof(uint));
    }

    private static byte[] ReadRawPayload(PackArchive archive, string path) {
        Assert.True(archive.TryGetEntry(path, out PackEntry? entry), $"The entry '{path}' is missing.");
        return archive.ReadPayload(entry);
    }
}
