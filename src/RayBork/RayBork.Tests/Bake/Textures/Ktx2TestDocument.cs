using System.Buffers.Binary;
using RayBork.Internal;

namespace RayBork.Tests.Bake.Textures;

/// <summary>One parsed KTX2 level index entry.</summary>
internal readonly record struct Ktx2TestLevel(
    ulong ByteOffset,
    ulong ByteLength,
    ulong UncompressedByteLength);

/// <summary>One parsed Khronos Data Format Descriptor sample.</summary>
internal readonly record struct Ktx2TestSample(
    ushort BitOffset,
    int BitLength,
    byte ChannelType,
    uint Lower,
    uint Upper);

/// <summary>A parsed KTX2 data format descriptor.</summary>
internal sealed class Ktx2TestDfd {
    internal Ktx2TestDfd(
        int totalSize,
        int descriptorBlockSize,
        byte colorModel,
        byte transferFunction,
        byte blockWidth,
        byte blockHeight,
        int bytesPerBlock,
        IReadOnlyList<Ktx2TestSample> samples) {
        TotalSize = totalSize;
        DescriptorBlockSize = descriptorBlockSize;
        ColorModel = colorModel;
        TransferFunction = transferFunction;
        BlockWidth = blockWidth;
        BlockHeight = blockHeight;
        BytesPerBlock = bytesPerBlock;
        Samples = samples;
    }

    internal int TotalSize { get; }

    internal int DescriptorBlockSize { get; }

    internal byte ColorModel { get; }

    internal byte TransferFunction { get; }

    internal byte BlockWidth { get; }

    internal byte BlockHeight { get; }

    internal int BytesPerBlock { get; }

    internal IReadOnlyList<Ktx2TestSample> Samples { get; }
}

/// <summary>
/// Parses the narrow KTX2 2.0 profile independently of the engine reader so writer tests assert the
/// byte layout rather than trusting the code under test.
/// </summary>
internal sealed class Ktx2TestDocument {
    internal const int HEADER_SIZE = 80;
    internal const int LEVEL_INDEX_ENTRY_SIZE = 24;

    private static readonly byte[] _identifier =
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

    private Ktx2TestDocument(
        uint vkFormat,
        uint typeSize,
        uint width,
        uint height,
        uint pixelDepth,
        uint layerCount,
        uint faceCount,
        uint levelCount,
        uint supercompression,
        uint dfdOffset,
        uint dfdLength,
        uint kvdLength,
        ulong sgdLength,
        IReadOnlyList<Ktx2TestLevel> levels,
        Ktx2TestDfd dfd) {
        VkFormat = vkFormat;
        TypeSize = typeSize;
        Width = width;
        Height = height;
        PixelDepth = pixelDepth;
        LayerCount = layerCount;
        FaceCount = faceCount;
        LevelCount = levelCount;
        Supercompression = supercompression;
        DfdOffset = dfdOffset;
        DfdLength = dfdLength;
        KvdLength = kvdLength;
        SgdLength = sgdLength;
        Levels = levels;
        Dfd = dfd;
    }

    internal uint VkFormat { get; }

    internal uint TypeSize { get; }

    internal uint Width { get; }

    internal uint Height { get; }

    internal uint PixelDepth { get; }

    internal uint LayerCount { get; }

    internal uint FaceCount { get; }

    internal uint LevelCount { get; }

    internal uint Supercompression { get; }

    internal uint DfdOffset { get; }

    internal uint DfdLength { get; }

    internal uint KvdLength { get; }

    internal ulong SgdLength { get; }

    internal IReadOnlyList<Ktx2TestLevel> Levels { get; }

    internal Ktx2TestDfd Dfd { get; }

    internal static Ktx2TestDocument Parse(ReadOnlySpan<byte> payload) {
        Assert.True(payload.Length >= HEADER_SIZE, "The payload is shorter than the 80-byte header.");
        Assert.True(payload[.._identifier.Length].SequenceEqual(_identifier), "The KTX2 identifier is wrong.");

        uint vkFormat = ReadUInt32(payload, 12);
        uint typeSize = ReadUInt32(payload, 16);
        uint width = ReadUInt32(payload, 20);
        uint height = ReadUInt32(payload, 24);
        uint pixelDepth = ReadUInt32(payload, 28);
        uint layerCount = ReadUInt32(payload, 32);
        uint faceCount = ReadUInt32(payload, 36);
        uint levelCount = ReadUInt32(payload, 40);
        uint supercompression = ReadUInt32(payload, 44);
        uint dfdOffset = ReadUInt32(payload, 48);
        uint dfdLength = ReadUInt32(payload, 52);
        uint kvdLength = ReadUInt32(payload, 60);
        ulong sgdLength = ReadUInt64(payload, 72);

        var levels = new Ktx2TestLevel[levelCount];
        for (int level = 0; level < levels.Length; level++) {
            int entry = HEADER_SIZE + (level * LEVEL_INDEX_ENTRY_SIZE);
            levels[level] = new Ktx2TestLevel(
                ReadUInt64(payload, entry),
                ReadUInt64(payload, entry + 8),
                ReadUInt64(payload, entry + 16));
        }

        Ktx2TestDfd dfd = ParseDfd(payload, dfdOffset, dfdLength);
        return new Ktx2TestDocument(
            vkFormat,
            typeSize,
            width,
            height,
            pixelDepth,
            layerCount,
            faceCount,
            levelCount,
            supercompression,
            dfdOffset,
            dfdLength,
            kvdLength,
            sgdLength,
            levels,
            dfd);
    }

    /// <summary>Parses a payload with the engine's runtime reader and the matching descriptor.</summary>
    /// <param name="payload">The complete KTX2 payload.</param>
    /// <param name="format">The baked format.</param>
    /// <param name="width">The base width.</param>
    /// <param name="height">The base height.</param>
    /// <param name="mipLevels">The baked mip count.</param>
    /// <returns>The parsed engine texture.</returns>
    internal static Ktx2Texture ReadWithEngine(
        ReadOnlySpan<byte> payload,
        TextureFormat format,
        int width,
        int height,
        uint mipLevels) {
        var descriptor = new TextureAssetDescriptor {
            Payload = "textures/test-ktx2",
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = mipLevels,
            Format = format,
            IsSrgb = format is TextureFormat.Rgba8Srgb or TextureFormat.Bc7Srgb,
            Filter = TextureFilter.Linear,
            Wrap = TextureWrap.Repeat,
            MaxAnisotropy = 1f,
        };
        return Ktx2Reader.Read(payload, descriptor);
    }

    private static Ktx2TestDfd ParseDfd(ReadOnlySpan<byte> payload, uint dfdOffset, uint dfdLength) {
        int offset = checked((int)dfdOffset);
        Assert.Equal(dfdLength, ReadUInt32(payload, offset));

        uint versionBlockSize = ReadUInt32(payload, offset + 8);
        int version = (int)(versionBlockSize & 0xFFFF);
        int blockSize = (int)(versionBlockSize >> 16);
        Assert.Equal(2, version);

        uint colorModelWord = ReadUInt32(payload, offset + 12);
        byte colorModel = (byte)colorModelWord;
        byte transferFunction = (byte)(colorModelWord >> 16);

        uint blockDimensions = ReadUInt32(payload, offset + 16);
        byte blockWidth = (byte)((blockDimensions & 0xFF) + 1);
        byte blockHeight = (byte)(((blockDimensions >> 8) & 0xFF) + 1);
        int bytesPerBlock = (int)ReadUInt32(payload, offset + 20);

        int sampleCount = (blockSize - 24) / 16;
        var samples = new Ktx2TestSample[sampleCount];
        int sampleOffset = offset + 28;
        for (int sample = 0; sample < samples.Length; sample++) {
            samples[sample] = new Ktx2TestSample(
                ReadUInt16(payload, sampleOffset),
                payload[sampleOffset + 2] + 1,
                payload[sampleOffset + 3],
                ReadUInt32(payload, sampleOffset + 8),
                ReadUInt32(payload, sampleOffset + 12));
            sampleOffset += 16;
        }

        return new Ktx2TestDfd(
            (int)dfdLength,
            blockSize,
            colorModel,
            transferFunction,
            blockWidth,
            blockHeight,
            bytesPerBlock,
            samples);
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> payload, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(payload[offset..]);

    private static ushort ReadUInt16(ReadOnlySpan<byte> payload, int offset)
        => BinaryPrimitives.ReadUInt16LittleEndian(payload[offset..]);

    private static ulong ReadUInt64(ReadOnlySpan<byte> payload, int offset)
        => BinaryPrimitives.ReadUInt64LittleEndian(payload[offset..]);
}
