using System.Buffers.Binary;
using RayBork.Internal;

namespace RayBork.Tests.Graphics.Assets;

/// <summary>
/// Unit tests for the narrow KTX2 2.0 reader that the Vulkan texture upload uses.
/// </summary>
public sealed class Ktx2ReaderTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ReadsRgba8PayloadWithCompleteMipChain() {
        byte[] payload = Ktx2TestWriter.Write(TextureFormat.Rgba8Srgb, 4, 4);
        TextureAssetDescriptor descriptor = Descriptor(TextureFormat.Rgba8Srgb, 4, 4, isSrgb: true);

        Ktx2Texture texture = Ktx2Reader.Read(payload, descriptor);

        Assert.Equal(TextureFormat.Rgba8Srgb, texture.Format);
        Assert.True(texture.IsSrgb);
        Assert.Equal(4u, texture.Width);
        Assert.Equal(4u, texture.Height);
        Assert.Equal(3, texture.MipLevels);
        Assert.Equal(new ulong[] { 64, 16, 4 }, texture.Levels.Select(static level => level.ByteLength));
        Assert.All(
            texture.Levels,
            level => Assert.True(level.ByteOffset + level.ByteLength <= (ulong)payload.Length));
    }

    [Theory]
    [InlineData(TextureFormat.Rgba8Unorm, false)]
    [InlineData(TextureFormat.Rgba8Srgb, true)]
    [InlineData(TextureFormat.Bc7Unorm, false)]
    [InlineData(TextureFormat.Bc7Srgb, true)]
    [InlineData(TextureFormat.Bc5Unorm, false)]
    [InlineData(TextureFormat.R8Unorm, false)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ReadsEveryPackedFormat(TextureFormat format, bool isSrgb) {
        byte[] payload = Ktx2TestWriter.Write(format, 4, 4);
        TextureAssetDescriptor descriptor = Descriptor(format, 4, 4, isSrgb);

        Ktx2Texture texture = Ktx2Reader.Read(payload, descriptor);

        Assert.Equal(format, texture.Format);
        Assert.Equal(isSrgb, texture.IsSrgb);
        Assert.Equal(3, texture.MipLevels);
        Assert.Equal(4u, texture.Width);
        Assert.Equal(4u, texture.Height);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ReadsOneByOneTextureWithASingleLevel() {
        byte[] payload = Ktx2TestWriter.Write(TextureFormat.R8Unorm, 1, 1);
        TextureAssetDescriptor descriptor = Descriptor(TextureFormat.R8Unorm, 1, 1, isSrgb: false);

        Ktx2Texture texture = Ktx2Reader.Read(payload, descriptor);

        Assert.Equal(1, texture.MipLevels);
        Ktx2Level level = Assert.Single(texture.Levels);
        Assert.Equal(1ul, level.ByteLength);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsBadIdentifier() {
        byte[] payload = Ktx2TestWriter.Write(TextureFormat.Rgba8Unorm, 4, 4);
        payload[0] ^= 0xFF;

        AssertInvalid(payload, Descriptor(TextureFormat.Rgba8Unorm, 4, 4, isSrgb: false), "identifier");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsIdentifierThatIsNotVersion2() {
        byte[] payload = Ktx2TestWriter.Write(TextureFormat.Rgba8Unorm, 4, 4);
        payload[5] = (byte)'1';

        AssertInvalid(payload, Descriptor(TextureFormat.Rgba8Unorm, 4, 4, isSrgb: false), "identifier");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsTruncatedHeader() {
        byte[] payload = Ktx2TestWriter.Write(TextureFormat.Rgba8Unorm, 4, 4)[..40];

        AssertInvalid(payload, Descriptor(TextureFormat.Rgba8Unorm, 4, 4, isSrgb: false), "header is truncated");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsNon2dPayload() {
        byte[] payload = Ktx2TestWriter.Write(TextureFormat.Rgba8Unorm, 4, 4);
        WriteUInt32(payload, Ktx2TestWriter.PIXEL_DEPTH_OFFSET, 1);

        AssertInvalid(payload, Descriptor(TextureFormat.Rgba8Unorm, 4, 4, isSrgb: false), "not a 2D image");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsArrayLayers() {
        byte[] payload = Ktx2TestWriter.Write(TextureFormat.Rgba8Unorm, 4, 4);
        WriteUInt32(payload, Ktx2TestWriter.LAYER_COUNT_OFFSET, 1);

        AssertInvalid(payload, Descriptor(TextureFormat.Rgba8Unorm, 4, 4, isSrgb: false), "array layer");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsMultipleFaces() {
        byte[] payload = Ktx2TestWriter.Write(TextureFormat.Rgba8Unorm, 4, 4);
        WriteUInt32(payload, Ktx2TestWriter.FACE_COUNT_OFFSET, 6);

        AssertInvalid(payload, Descriptor(TextureFormat.Rgba8Unorm, 4, 4, isSrgb: false), "face");
    }

    [Theory]
    [InlineData(1u, "BasisLZ")]
    [InlineData(2u, "Zstandard")]
    [InlineData(99u, "unknown (99)")]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsUnsupportedSupercompression(uint scheme, string expectedSchemeName) {
        byte[] payload = Ktx2TestWriter.Write(TextureFormat.Rgba8Unorm, 4, 4);
        WriteUInt32(payload, Ktx2TestWriter.SUPERCOMPRESSION_OFFSET, scheme);

        AssertInvalid(
            payload,
            Descriptor(TextureFormat.Rgba8Unorm, 4, 4, isSrgb: false),
            expectedSchemeName);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsIncompleteMipChain() {
        byte[] payload = Ktx2TestWriter.Write(TextureFormat.Rgba8Unorm, 4, 4, mipLevels: 2);

        AssertInvalid(payload, Descriptor(TextureFormat.Rgba8Unorm, 4, 4, isSrgb: false), "mip level");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsDescriptorMipCountThatDisagreesWithTheChain() {
        byte[] payload = Ktx2TestWriter.Write(TextureFormat.Rgba8Unorm, 4, 4);

        AssertInvalid(
            payload,
            Descriptor(TextureFormat.Rgba8Unorm, 4, 4, isSrgb: false, mipLevels: 2),
            "mip level");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsTruncatedLevelIndex() {
        byte[] payload = Ktx2TestWriter.Write(TextureFormat.Rgba8Unorm, 4, 4);
        byte[] truncated = payload[..(Ktx2TestWriter.HEADER_SIZE + Ktx2TestWriter.LEVEL_INDEX_ENTRY_SIZE)];

        AssertInvalid(truncated, Descriptor(TextureFormat.Rgba8Unorm, 4, 4, isSrgb: false), "level index is truncated");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsTruncatedLevelData() {
        byte[] payload = Ktx2TestWriter.Write(TextureFormat.Rgba8Unorm, 4, 4);
        byte[] truncated = payload[..^8];

        AssertInvalid(truncated, Descriptor(TextureFormat.Rgba8Unorm, 4, 4, isSrgb: false), "does not lie inside");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsMisalignedLevelOffset() {
        byte[] payload = Ktx2TestWriter.Write(TextureFormat.Rgba8Unorm, 4, 4);
        int entry = Ktx2TestWriter.LevelEntryOffset(0);
        ulong offset = BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(entry));
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(entry), offset + 1);

        AssertInvalid(payload, Descriptor(TextureFormat.Rgba8Unorm, 4, 4, isSrgb: false), "not aligned");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsWrongLevelByteLength() {
        byte[] payload = Ktx2TestWriter.Write(TextureFormat.Rgba8Unorm, 4, 4);
        int entry = Ktx2TestWriter.LevelEntryOffset(0);
        ulong length = BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(entry + 8));
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(entry + 8), length - 4);
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(entry + 16), length - 4);

        AssertInvalid(payload, Descriptor(TextureFormat.Rgba8Unorm, 4, 4, isSrgb: false), "requires");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsKeyValueData() {
        byte[] payload = Ktx2TestWriter.Write(TextureFormat.Rgba8Unorm, 4, 4);
        WriteUInt32(payload, Ktx2TestWriter.KVD_LENGTH_FIELD, 1);

        AssertInvalid(payload, Descriptor(TextureFormat.Rgba8Unorm, 4, 4, isSrgb: false), "key/value");
    }

    [Theory]
    [InlineData("total-size")]
    [InlineData("version")]
    [InlineData("block-size")]
    [InlineData("color-model")]
    [InlineData("transfer-function")]
    [InlineData("texel-block")]
    [InlineData("bytes-plane")]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsMalformedDataFormatDescriptor(string corruption) {
        byte[] payload = Ktx2TestWriter.Write(TextureFormat.Rgba8Srgb, 4, 4);
        int dfd = Ktx2TestWriter.DfdOffset(payload);
        switch (corruption) {
            case "total-size":
                WriteUInt32(payload, dfd, BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(dfd)) + 1);
                break;
            case "version":
                WriteUInt32(payload, dfd + 8, 1);
                break;
            case "block-size":
                WriteUInt32(payload, dfd + 8, (8u << 16) | 2u);
                break;
            case "color-model":
                payload[dfd + 12] = 2;
                break;
            case "transfer-function":
                payload[dfd + 14] = 1;
                break;
            case "texel-block":
                WriteUInt32(payload, dfd + 16, 3u);
                break;
            case "bytes-plane":
                WriteUInt32(payload, dfd + 20, 8u);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(corruption), corruption, "Unknown corruption.");
        }

        string expected = corruption switch {
            "total-size" => "total size",
            "version" => "version is 1",
            "block-size" => "block size 8",
            "color-model" => "color model",
            "transfer-function" => "transfer function",
            "texel-block" => "texel block",
            "bytes-plane" => "byte(s) per texel block",
            _ => throw new ArgumentOutOfRangeException(nameof(corruption), corruption, "Unknown corruption."),
        };
        AssertInvalid(payload, Descriptor(TextureFormat.Rgba8Srgb, 4, 4, isSrgb: true), expected);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsDescriptorDimensionMismatch() {
        byte[] payload = Ktx2TestWriter.Write(TextureFormat.Rgba8Unorm, 4, 4);

        AssertInvalid(
            payload,
            Descriptor(TextureFormat.Rgba8Unorm, 8, 8, isSrgb: false),
            "does not match the descriptor extent");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsDescriptorFormatMismatch() {
        byte[] payload = Ktx2TestWriter.Write(TextureFormat.Rgba8Srgb, 4, 4);

        AssertInvalid(
            payload,
            Descriptor(TextureFormat.Rgba8Unorm, 4, 4, isSrgb: false),
            "vkFormat");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsDescriptorSrgbFlagThatDisagreesWithTheFormat() {
        byte[] payload = Ktx2TestWriter.Write(TextureFormat.Rgba8Srgb, 4, 4);

        AssertInvalid(
            payload,
            Descriptor(TextureFormat.Rgba8Srgb, 4, 4, isSrgb: false),
            "sRGB flag");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsPayloadTransferThatDisagreesWithTheDescriptor() {
        byte[] payload = Ktx2TestWriter.Write(TextureFormat.Rgba8Srgb, 4, 4, srgb: false);

        AssertInvalid(
            payload,
            Descriptor(TextureFormat.Rgba8Srgb, 4, 4, isSrgb: true),
            "transfer function");
    }

    [Theory]
    [InlineData(TextureFormat.Rgba16Float)]
    [InlineData(TextureFormat.Depth32Float)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsRenderTargetFormatsInPackedTextures(TextureFormat format) {
        byte[] payload = Ktx2TestWriter.Write(TextureFormat.Rgba8Unorm, 1, 1);

        AssertInvalid(payload, Descriptor(format, 1, 1, isSrgb: false), "render-target");
    }

    private static AssetLoadException AssertInvalid(
        byte[] payload,
        TextureAssetDescriptor descriptor,
        string expectedMessagePart) {
        AssetLoadException failure = Assert.Throws<AssetLoadException>(() => Ktx2Reader.Read(payload, descriptor));
        Assert.Equal(DiagnosticCodes.ASSET_LOAD_FAILED, failure.DiagnosticCode);
        Assert.Contains(expectedMessagePart, failure.Message, StringComparison.Ordinal);
        return failure;
    }

    private static TextureAssetDescriptor Descriptor(
        TextureFormat format,
        uint width,
        uint height,
        bool isSrgb,
        uint? mipLevels = null) => new() {
            Payload = "textures/test-ktx2",
            Width = width,
            Height = height,
            MipLevels = mipLevels ?? (uint)Ktx2TestWriter.FullMipCount((int)width, (int)height),
            Format = format,
            IsSrgb = isSrgb,
            Filter = TextureFilter.Linear,
            Wrap = TextureWrap.Repeat,
            MaxAnisotropy = 1f,
        };

    private static void WriteUInt32(byte[] payload, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset), value);
}
