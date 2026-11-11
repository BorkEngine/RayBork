using System.Buffers.Binary;

namespace RayBork.Internal;

/// <summary>
/// Parses and validates the narrow KTX2 2.0 profile the bake writes for packed texture assets.
/// </summary>
/// <remarks>
/// <para>
/// The accepted profile is one 2D image with <c>pixelDepth = 0</c>, <c>layerCount = 0</c> (one
/// layer), <c>faceCount = 1</c>, no key/value or supercompression-global-data section, no
/// supercompression, and a complete mip chain with the base level first in the level index. The file
/// layout is the specification order: identifier, 80-byte header, level index, data format
/// descriptor, then the level data.
/// </para>
/// <para>
/// The header's <c>vkFormat</c>, base dimensions, and level count must match the descriptor; every
/// level must have the stored byte length the format requires, must not overlap the header, level
/// index, or data format descriptor, and must start on an <c>lcm(texelBlockSize, 4)</c> boundary. The
/// data format descriptor must be the single basic block of the format's color model, with the linear
/// or sRGB transfer function the descriptor selects. A malformed, mismatched, or
/// Basis/UASTC-supercompressed payload is an <see cref="AssetLoadException"/> with the <c>RB1008</c>
/// diagnostic code; the reader never decompresses, transcodes, or partially accepts a payload.
/// </para>
/// </remarks>
internal static class Ktx2Reader {
    /// <summary>The fixed KTX2 2.0 header size in bytes.</summary>
    internal const int HEADER_SIZE = 80;

    /// <summary>The size of one level index entry in bytes.</summary>
    internal const int LEVEL_INDEX_ENTRY_SIZE = 24;

    /// <summary>The size of the KTX2 2.0 identifier in bytes.</summary>
    internal const int IDENTIFIER_SIZE = 12;

    private const int VK_FORMAT_OFFSET = 12;
    private const int PIXEL_WIDTH_OFFSET = 20;
    private const int PIXEL_HEIGHT_OFFSET = 24;
    private const int PIXEL_DEPTH_OFFSET = 28;
    private const int LAYER_COUNT_OFFSET = 32;
    private const int FACE_COUNT_OFFSET = 36;
    private const int LEVEL_COUNT_OFFSET = 40;
    private const int SUPERCOMPRESSION_OFFSET = 44;
    private const int DFD_OFFSET_FIELD = 48;
    private const int DFD_LENGTH_FIELD = 52;
    private const int KVD_LENGTH_FIELD = 60;
    private const int SGD_LENGTH_FIELD = 72;

    private const int DFD_HEADER_SIZE = 4;
    private const int DFD_BASIC_BLOCK_SIZE = 24;
    private const int DFD_SAMPLE_SIZE = 16;
    private const uint DFD_VERSION = 2;
    private const uint DFD_VENDOR_ID = 0;
    private const uint DFD_DESCRIPTOR_TYPE = 0;
    private const uint SUPERCOMPRESSION_NONE = 0;
    private const int FACE_COUNT_SINGLE = 1;

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
    /// Parses and validates one packed KTX2 payload against its descriptor.
    /// </summary>
    /// <param name="payload">The complete KTX2 2.0 file bytes.</param>
    /// <param name="descriptor">The descriptor whose format, dimensions, and mip count must match.</param>
    /// <returns>The parsed texture with its level index entries.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="descriptor"/> is <c>null</c>.</exception>
    /// <exception cref="AssetLoadException">
    /// The payload is not the narrow KTX2 profile, a header field or level length does not match the
    /// descriptor, or the data format descriptor is malformed or uses the wrong transfer function.
    /// The diagnostic code is <c>RB1008</c>.
    /// </exception>
    internal static Ktx2Texture Read(ReadOnlySpan<byte> payload, TextureAssetDescriptor descriptor) {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (!VulkanTextureFormat.IsPacked(descriptor.Format)) {
            throw CreateFailure(
                descriptor,
                $"the descriptor format '{descriptor.Format}' is a render-target format and cannot be uploaded as a packed texture");
        }

        if (payload.Length < IDENTIFIER_SIZE || !payload[..IDENTIFIER_SIZE].SequenceEqual(_identifier)) {
            throw CreateFailure(
                descriptor,
                "the KTX2 2.0 identifier is missing or invalid; the payload is not a KTX2 2.0 file");
        }

        if (payload.Length < HEADER_SIZE) {
            throw CreateFailure(
                descriptor,
                $"the header is truncated ({payload.Length} byte(s), {HEADER_SIZE} required)");
        }

        uint vkFormat = ReadUInt32(payload, VK_FORMAT_OFFSET);
        uint width = ReadUInt32(payload, PIXEL_WIDTH_OFFSET);
        uint height = ReadUInt32(payload, PIXEL_HEIGHT_OFFSET);
        uint pixelDepth = ReadUInt32(payload, PIXEL_DEPTH_OFFSET);
        uint layerCount = ReadUInt32(payload, LAYER_COUNT_OFFSET);
        uint faceCount = ReadUInt32(payload, FACE_COUNT_OFFSET);
        uint levelCount = ReadUInt32(payload, LEVEL_COUNT_OFFSET);
        uint supercompression = ReadUInt32(payload, SUPERCOMPRESSION_OFFSET);

        if (width == 0 || height == 0) {
            throw CreateFailure(
                descriptor,
                $"the base extent {width}x{height} is empty");
        }

        if (width > int.MaxValue || height > int.MaxValue) {
            throw CreateFailure(
                descriptor,
                $"the base extent {width}x{height} exceeds the maximum supported dimension {int.MaxValue}");
        }

        if (width != descriptor.Width || height != descriptor.Height) {
            throw CreateFailure(
                descriptor,
                $"the header base extent {width}x{height} does not match the descriptor extent {descriptor.Width}x{descriptor.Height}");
        }

        if (pixelDepth != 0) {
            throw CreateFailure(
                descriptor,
                $"the payload is not a 2D image (pixelDepth is {pixelDepth}, expected 0)");
        }

        if (layerCount != 0) {
            throw CreateFailure(
                descriptor,
                $"the payload has {layerCount} array layer(s); the bake profile stores one layer (layerCount 0)");
        }

        if (faceCount != FACE_COUNT_SINGLE) {
            throw CreateFailure(
                descriptor,
                $"the payload has {faceCount} face(s); the bake profile stores one face");
        }

        if (supercompression != SUPERCOMPRESSION_NONE) {
            throw CreateFailure(
                descriptor,
                $"the payload uses the {DescribeSupercompression(supercompression)} supercompression scheme; the bake profile stores uncompressed levels");
        }

        uint expectedVkFormat = (uint)VulkanTextureFormat.ToVkFormat(descriptor.Format);
        if (vkFormat != expectedVkFormat) {
            throw CreateFailure(
                descriptor,
                $"the header vkFormat {vkFormat} does not match the descriptor format '{descriptor.Format}' ({expectedVkFormat})");
        }

        uint fullMipCount = VulkanTextureFormat.FullMipCount(width, height);
        if (levelCount != fullMipCount || descriptor.MipLevels != levelCount) {
            throw CreateFailure(
                descriptor,
                $"the header stores {levelCount} mip level(s), the descriptor declares {descriptor.MipLevels}, and {width}x{height} requires the full chain of {fullMipCount}");
        }

        if (ReadUInt32(payload, KVD_LENGTH_FIELD) != 0 || ReadUInt64(payload, SGD_LENGTH_FIELD) != 0) {
            throw CreateFailure(
                descriptor,
                "the payload carries key/value or supercompression-global data; the bake profile stores neither");
        }

        long levelIndexEnd = HEADER_SIZE + ((long)levelCount * LEVEL_INDEX_ENTRY_SIZE);
        if (levelIndexEnd > payload.Length) {
            throw CreateFailure(
                descriptor,
                $"the {levelCount}-entry level index is truncated ({payload.Length} byte(s) in the payload)");
        }

        int dfdOffset = ReadDfd(payload, descriptor, levelIndexEnd);
        ValidateDfd(payload, descriptor, dfdOffset);
        long levelDataStart = Math.Max(levelIndexEnd, (long)dfdOffset + ReadUInt32(payload, DFD_LENGTH_FIELD));

        Ktx2Level[] levels = new Ktx2Level[(int)levelCount];
        int alignment = VulkanTextureFormat.GetLevelAlignment(descriptor.Format);
        for (int level = 0; level < levels.Length; level++) {
            int entry = HEADER_SIZE + (level * LEVEL_INDEX_ENTRY_SIZE);
            ulong byteOffset = ReadUInt64(payload, entry);
            ulong byteLength = ReadUInt64(payload, entry + 8);
            ulong uncompressedByteLength = ReadUInt64(payload, entry + 16);
            if (byteLength != uncompressedByteLength) {
                throw CreateFailure(
                    descriptor,
                    $"level {level} declares a stored length of {byteLength} byte(s) and an uncompressed length of {uncompressedByteLength}; supercompressed levels are not supported");
            }

            int levelWidth = VulkanTextureFormat.LevelExtent(width, level);
            int levelHeight = VulkanTextureFormat.LevelExtent(height, level);
            int expectedByteLength = VulkanTextureFormat.GetLevelByteLength(descriptor.Format, levelWidth, levelHeight);
            if (byteLength != (ulong)expectedByteLength) {
                throw CreateFailure(
                    descriptor,
                    $"level {level} ({levelWidth}x{levelHeight}) stores {byteLength} byte(s); {descriptor.Format} requires {expectedByteLength}");
            }

            if (byteOffset % (ulong)alignment != 0) {
                throw CreateFailure(
                    descriptor,
                    $"level {level} starts at offset {byteOffset}, which is not aligned to the required {alignment} byte(s)");
            }

            if (byteOffset < (ulong)levelDataStart || !IsInside(payload.Length, byteOffset, byteLength)) {
                throw CreateFailure(
                    descriptor,
                    $"level {level} at offset {byteOffset} with {byteLength} byte(s) does not lie inside the level data region after offset {levelDataStart}");
            }

            levels[level] = new Ktx2Level(byteOffset, byteLength);
        }

        return new Ktx2Texture(descriptor.Format, descriptor.IsSrgb, width, height, levels);
    }

    /// <summary>
    /// Validates the data format descriptor location in the header and returns its absolute offset.
    /// </summary>
    private static int ReadDfd(ReadOnlySpan<byte> payload, TextureAssetDescriptor descriptor, long levelIndexEnd) {
        uint dfdByteOffset = ReadUInt32(payload, DFD_OFFSET_FIELD);
        uint dfdByteLength = ReadUInt32(payload, DFD_LENGTH_FIELD);
        if (dfdByteOffset < (ulong)levelIndexEnd
            || dfdByteLength < DFD_HEADER_SIZE + DFD_BASIC_BLOCK_SIZE + DFD_SAMPLE_SIZE
            || !IsInside(payload.Length, dfdByteOffset, dfdByteLength)) {
            throw CreateFailure(
                descriptor,
                $"the data format descriptor at offset {dfdByteOffset} with {dfdByteLength} byte(s) is missing or outside the payload");
        }

        if (ReadUInt32(payload, (int)dfdByteOffset) != dfdByteLength) {
            throw CreateFailure(
                descriptor,
                $"the data format descriptor declares a total size that does not match its {dfdByteLength} stored byte(s)");
        }

        return (int)dfdByteOffset;
    }

    /// <summary>
    /// Validates the single basic data format descriptor block of the payload.
    /// </summary>
    private static void ValidateDfd(ReadOnlySpan<byte> payload, TextureAssetDescriptor descriptor, int dfdOffset) {
        uint descriptorBlockWord = ReadUInt32(payload, dfdOffset + 4);
        uint vendorId = descriptorBlockWord & 0x1FFFF;
        uint descriptorType = descriptorBlockWord >> 17;
        if (vendorId != DFD_VENDOR_ID || descriptorType != DFD_DESCRIPTOR_TYPE) {
            throw CreateFailure(
                descriptor,
                $"the data format descriptor uses vendor {vendorId} and type {descriptorType}; only the Khronos basic block is supported");
        }

        uint versionBlockSize = ReadUInt32(payload, dfdOffset + 8);
        uint version = versionBlockSize & 0xFFFF;
        uint blockSize = versionBlockSize >> 16;
        if (version != DFD_VERSION) {
            throw CreateFailure(
                descriptor,
                $"the data format descriptor version is {version}; version {DFD_VERSION} is required");
        }

        if (blockSize < DFD_BASIC_BLOCK_SIZE + DFD_SAMPLE_SIZE
            || (blockSize - DFD_BASIC_BLOCK_SIZE) % DFD_SAMPLE_SIZE != 0
            || DFD_HEADER_SIZE + blockSize != ReadUInt32(payload, dfdOffset)) {
            throw CreateFailure(
                descriptor,
                $"the data format descriptor block size {blockSize} is malformed for its declared total size");
        }

        uint colorModelWord = ReadUInt32(payload, dfdOffset + 12);
        byte colorModel = (byte)(colorModelWord & 0xFF);
        byte transferFunction = (byte)((colorModelWord >> 16) & 0xFF);
        byte expectedColorModel = VulkanTextureFormat.DfdColorModel(descriptor.Format);
        if (colorModel != expectedColorModel) {
            throw CreateFailure(
                descriptor,
                $"the data format descriptor color model {colorModel} does not match the {descriptor.Format} model {expectedColorModel}");
        }

        if (descriptor.IsSrgb != VulkanTextureFormat.IsSrgb(descriptor.Format)) {
            throw CreateFailure(
                descriptor,
                $"the descriptor sRGB flag {descriptor.IsSrgb} disagrees with the '{descriptor.Format}' transfer function");
        }

        byte expectedTransfer = VulkanTextureFormat.DfdTransferFunction(descriptor.Format);
        if (transferFunction != expectedTransfer) {
            throw CreateFailure(
                descriptor,
                $"the data format descriptor transfer function {transferFunction} does not match the expected {expectedTransfer} for '{descriptor.Format}'");
        }

        uint blockDimensions = ReadUInt32(payload, dfdOffset + 16);
        byte blockWidthMinusOne = (byte)blockDimensions;
        byte blockHeightMinusOne = (byte)(blockDimensions >> 8);
        byte blockDepthMinusOne = (byte)(blockDimensions >> 16);
        byte blockLayersMinusOne = (byte)(blockDimensions >> 24);
        (byte expectedWidth, byte expectedHeight) = VulkanTextureFormat.IsBlockCompressed(descriptor.Format)
            ? ((byte)3, (byte)3)
            : ((byte)0, (byte)0);
        if (blockWidthMinusOne != expectedWidth
            || blockHeightMinusOne != expectedHeight
            || blockDepthMinusOne != 0
            || blockLayersMinusOne != 0) {
            throw CreateFailure(
                descriptor,
                $"the data format descriptor texel block {blockWidthMinusOne + 1}x{blockHeightMinusOne + 1}x{blockDepthMinusOne + 1} does not match the {descriptor.Format} block");
        }

        uint bytesPlane = ReadUInt32(payload, dfdOffset + 20);
        if (bytesPlane != (uint)VulkanTextureFormat.BytesPerBlock(descriptor.Format)) {
            throw CreateFailure(
                descriptor,
                $"the data format descriptor declares {bytesPlane} byte(s) per texel block; {descriptor.Format} requires {VulkanTextureFormat.BytesPerBlock(descriptor.Format)}");
        }
    }

    private static AssetLoadException CreateFailure(TextureAssetDescriptor descriptor, string reason) => new(
        $"The KTX2 payload '{descriptor.Payload}' is invalid: {reason}.",
        DiagnosticCodes.ASSET_LOAD_FAILED,
        null);

    private static string DescribeSupercompression(uint scheme) => scheme switch {
        1 => "BasisLZ",
        2 => "Zstandard",
        _ => $"unknown ({scheme})",
    };

    private static bool IsInside(int payloadLength, ulong byteOffset, ulong byteLength) {
        ulong length = (ulong)payloadLength;
        return byteOffset <= length && byteLength <= length - byteOffset;
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> payload, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(payload[offset..]);

    private static ulong ReadUInt64(ReadOnlySpan<byte> payload, int offset) =>
        BinaryPrimitives.ReadUInt64LittleEndian(payload[offset..]);
}
