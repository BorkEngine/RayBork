using System.Buffers.Binary;

namespace RayBork.Tests.Graphics.Assets;

/// <summary>
/// Writes the narrow KTX2 2.0 profile the runtime reader accepts, for the parser and GPU upload tests.
/// </summary>
/// <remarks>
/// The writer is an independent implementation of the bake profile so a reader change is exercised
/// against a second encoder: identifier, 80-byte header, level index, one basic data format
/// descriptor block, and a complete mip chain. Tests corrupt the returned bytes at the documented
/// field offsets to cover the failure modes.
/// </remarks>
internal static class Ktx2TestWriter {
    internal const int HEADER_SIZE = 80;
    internal const int LEVEL_INDEX_ENTRY_SIZE = 24;

    internal const int VK_FORMAT_OFFSET = 12;
    internal const int TYPE_SIZE_OFFSET = 16;
    internal const int WIDTH_OFFSET = 20;
    internal const int HEIGHT_OFFSET = 24;
    internal const int PIXEL_DEPTH_OFFSET = 28;
    internal const int LAYER_COUNT_OFFSET = 32;
    internal const int FACE_COUNT_OFFSET = 36;
    internal const int LEVEL_COUNT_OFFSET = 40;
    internal const int SUPERCOMPRESSION_OFFSET = 44;
    internal const int DFD_OFFSET_FIELD = 48;
    internal const int DFD_LENGTH_FIELD = 52;
    internal const int KVD_LENGTH_FIELD = 60;
    internal const int SGD_LENGTH_FIELD = 72;

    internal const int DFD_HEADER_SIZE = 4;
    internal const int DFD_BASIC_BLOCK_SIZE = 24;
    internal const int DFD_SAMPLE_SIZE = 16;

    private const uint DFD_VERSION = 2;
    private const byte PRIMARIES_BT709 = 1;
    private const byte CHANNEL_TYPE_UNORM = 0;

    private static readonly byte[] _identifier = [
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

    /// <summary>
    /// Writes one complete KTX2 payload for a packed texture format.
    /// </summary>
    /// <param name="format">The packed texture format.</param>
    /// <param name="width">The base-level width in pixels.</param>
    /// <param name="height">The base-level height in pixels.</param>
    /// <param name="srgb">
    /// Overrides the data format transfer function; the default follows the format. A value that
    /// disagrees with the format produces a payload the reader must reject.
    /// </param>
    /// <param name="mipLevels">
    /// Overrides the stored level count; the default is the complete mip chain. A value that is not
    /// the complete chain produces a payload the reader must reject.
    /// </param>
    /// <returns>The complete KTX2 2.0 file bytes.</returns>
    internal static byte[] Write(
        TextureFormat format,
        int width,
        int height,
        bool? srgb = null,
        int? mipLevels = null) {
        FormatInfo info = GetFormatInfo(format);
        bool isSrgb = srgb ?? format is TextureFormat.Rgba8Srgb or TextureFormat.Bc7Srgb;
        int fullMipCount = FullMipCount(width, height);
        int levelCount = mipLevels ?? fullMipCount;
        if (levelCount <= 0) {
            throw new ArgumentOutOfRangeException(nameof(mipLevels), levelCount, "The level count must be positive.");
        }

        int alignment = LeastCommonMultiple(info.BytesPerBlock, 4);
        int sampleCount = info.BlockWidth == 4 ? (format == TextureFormat.Bc5Unorm ? 2 : 1) : 4;
        if (format == TextureFormat.R8Unorm) {
            sampleCount = 1;
        }

        int blockSize = DFD_BASIC_BLOCK_SIZE + (DFD_SAMPLE_SIZE * sampleCount);
        int dfdLength = DFD_HEADER_SIZE + blockSize;
        int dfdOffset = HEADER_SIZE + (LEVEL_INDEX_ENTRY_SIZE * levelCount);

        int[] levelLengths = new int[levelCount];
        for (int level = 0; level < levelCount; level++) {
            int levelWidth = LevelExtent(width, level);
            int levelHeight = LevelExtent(height, level);
            levelLengths[level] = GetLevelByteLength(info, levelWidth, levelHeight);
        }

        int[] levelOffsets = new int[levelCount];
        int cursor = Align(dfdOffset + dfdLength, alignment);
        for (int level = levelCount - 1; level >= 0; level--) {
            cursor = Align(cursor, alignment);
            levelOffsets[level] = cursor;
            cursor = checked(cursor + levelLengths[level]);
        }

        byte[] payload = new byte[cursor];
        _identifier.CopyTo(payload, 0);
        WriteUInt32(payload, VK_FORMAT_OFFSET, info.VkFormat);
        WriteUInt32(payload, TYPE_SIZE_OFFSET, info.BlockWidth == 4 ? 16u : 1u);
        WriteUInt32(payload, WIDTH_OFFSET, (uint)width);
        WriteUInt32(payload, HEIGHT_OFFSET, (uint)height);
        WriteUInt32(payload, PIXEL_DEPTH_OFFSET, 0u);
        WriteUInt32(payload, LAYER_COUNT_OFFSET, 0u);
        WriteUInt32(payload, FACE_COUNT_OFFSET, 1u);
        WriteUInt32(payload, LEVEL_COUNT_OFFSET, (uint)levelCount);
        WriteUInt32(payload, SUPERCOMPRESSION_OFFSET, 0u);
        WriteUInt32(payload, DFD_OFFSET_FIELD, (uint)dfdOffset);
        WriteUInt32(payload, DFD_LENGTH_FIELD, (uint)dfdLength);
        WriteUInt32(payload, KVD_LENGTH_FIELD, 0u);
        WriteUInt64(payload, SGD_LENGTH_FIELD, 0ul);

        for (int level = 0; level < levelCount; level++) {
            int entry = HEADER_SIZE + (level * LEVEL_INDEX_ENTRY_SIZE);
            WriteUInt64(payload, entry, (ulong)levelOffsets[level]);
            WriteUInt64(payload, entry + 8, (ulong)levelLengths[level]);
            WriteUInt64(payload, entry + 16, (ulong)levelLengths[level]);
        }

        WriteDfd(payload, dfdOffset, info, isSrgb, sampleCount, blockSize);
        for (int level = levelCount - 1; level >= 0; level--) {
            Span<byte> data = payload.AsSpan(levelOffsets[level], levelLengths[level]);
            for (int index = 0; index < data.Length; index++) {
                data[index] = (byte)((level * 53) + index);
            }
        }

        return payload;
    }

    /// <summary>
    /// Gets the absolute data format descriptor offset of a well-formed payload.
    /// </summary>
    internal static int DfdOffset(byte[] payload) =>
        (int)BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(DFD_OFFSET_FIELD));

    /// <summary>
    /// Gets the absolute level index entry offset of one level.
    /// </summary>
    internal static int LevelEntryOffset(int level) => HEADER_SIZE + (level * LEVEL_INDEX_ENTRY_SIZE);

    /// <summary>
    /// Gets the complete mip chain length of a base extent.
    /// </summary>
    internal static int FullMipCount(int width, int height) {
        int largest = Math.Max(width, height);
        int count = 1;
        while (largest > 1) {
            largest >>= 1;
            count++;
        }

        return count;
    }

    /// <summary>
    /// Gets one level's pixel extent.
    /// </summary>
    internal static int LevelExtent(int baseExtent, int level) => Math.Max(1, baseExtent >> level);

    private static void WriteDfd(
        byte[] payload,
        int dfdOffset,
        FormatInfo info,
        bool isSrgb,
        int sampleCount,
        int blockSize) {
        WriteUInt32(payload, dfdOffset, (uint)(DFD_HEADER_SIZE + blockSize));
        WriteUInt32(payload, dfdOffset + 4, 0u);
        WriteUInt32(payload, dfdOffset + 8, ((uint)blockSize << 16) | DFD_VERSION);
        WriteUInt32(
            payload,
            dfdOffset + 12,
            (uint)((isSrgb ? 2 : 1) << 16 | PRIMARIES_BT709 << 8 | info.ColorModel));
        WriteUInt32(
            payload,
            dfdOffset + 16,
            (uint)((info.BlockWidth - 1) | ((info.BlockHeight - 1) << 8)));
        WriteUInt32(payload, dfdOffset + 20, (uint)info.BytesPerBlock);

        int sampleOffset = dfdOffset + DFD_HEADER_SIZE + DFD_BASIC_BLOCK_SIZE;
        if (info.BlockWidth == 4) {
            if (sampleCount == 1) {
                WriteSample(payload, sampleOffset, 0, 128, 0, uint.MaxValue);
            } else {
                WriteSample(payload, sampleOffset, 0, 64, 0, uint.MaxValue);
                WriteSample(payload, sampleOffset + DFD_SAMPLE_SIZE, 64, 64, 1, uint.MaxValue);
            }

            return;
        }

        int channelCount = sampleCount;
        for (int channel = 0; channel < channelCount; channel++) {
            byte channelId = channel switch {
                0 => 0,
                1 => 1,
                2 => 2,
                _ => 15,
            };
            WriteSample(
                payload,
                sampleOffset + (channel * DFD_SAMPLE_SIZE),
                (ushort)(channel * 8),
                8,
                channelId,
                255);
        }
    }

    private static void WriteSample(
        byte[] payload,
        int offset,
        ushort bitOffset,
        byte bitLength,
        byte channel,
        uint upper) {
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(offset), bitOffset);
        payload[offset + 2] = (byte)(bitLength - 1);
        payload[offset + 3] = CHANNEL_TYPE_UNORM;
        payload[offset + 4] = channel;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset + 8), 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset + 12), upper);
    }

    private static int GetLevelByteLength(FormatInfo info, int width, int height) {
        int blocksWide = info.BlockWidth == 4 ? (width + 3) / 4 : width;
        int blocksHigh = info.BlockHeight == 4 ? (height + 3) / 4 : height;
        return blocksWide * blocksHigh * info.BytesPerBlock;
    }

    private static FormatInfo GetFormatInfo(TextureFormat format) => format switch {
        TextureFormat.Rgba8Unorm => new FormatInfo(37u, 1, 1, 4, 1),
        TextureFormat.Rgba8Srgb => new FormatInfo(43u, 1, 1, 4, 1),
        TextureFormat.Bc7Unorm => new FormatInfo(145u, 4, 4, 16, 134),
        TextureFormat.Bc7Srgb => new FormatInfo(146u, 4, 4, 16, 134),
        TextureFormat.Bc5Unorm => new FormatInfo(141u, 4, 4, 16, 132),
        TextureFormat.R8Unorm => new FormatInfo(9u, 1, 1, 1, 1),
        _ => throw new ArgumentOutOfRangeException(
            nameof(format),
            format,
            "The test writer supports packed texture formats only."),
    };

    private static int LeastCommonMultiple(int left, int right) {
        int a = left;
        int b = right;
        while (b != 0) {
            (a, b) = (b, a % b);
        }

        return (left / a) * right;
    }

    private static int Align(int value, int alignment) => checked(((value + alignment - 1) / alignment) * alignment);

    private static void WriteUInt32(byte[] payload, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset), value);

    private static void WriteUInt64(byte[] payload, int offset, ulong value) =>
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(offset), value);

    private readonly record struct FormatInfo(
        uint VkFormat,
        int BlockWidth,
        int BlockHeight,
        int BytesPerBlock,
        byte ColorModel);
}
